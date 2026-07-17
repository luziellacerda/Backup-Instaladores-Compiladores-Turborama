using System.Diagnostics;
using System.Runtime.InteropServices;
using TurboRama.Configuration;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Agente em background: Ctrl+End abre o menu TurboRama (substituto do CAD).
/// Tem message loop próprio (ApplicationContext) — funciona sem Explorer.
/// </summary>
internal sealed class SecurityAgentHost : ApplicationContext
{
    private static Mutex? _mutex;
    private readonly NativeHost _host;
    private readonly System.Windows.Forms.Timer _triggerTimer;
    private readonly ITurboRamaLogger? _log;
    private readonly string _pin;
    private bool _busy;

    public SecurityAgentHost(ITurboRamaLogger? log)
    {
        _log = log;
        ConfigurationStore.Load(out ProductConfiguration config);
        _pin = SystemSecurityForm.ResolvePin(config);

        bool created;
        try
        {
            _mutex = new Mutex(true, "Local\\TurboRama.SecAgent." + Environment.UserName, out created);
        }
        catch { created = true; }

        if (!created)
        {
            _log?.Info("SecAgent", "Já ativo nesta sessão.");
            Environment.Exit(0);
            return;
        }

        ApplySessionCadEmpty();

        _host = new NativeHost();
        _host.CtrlEndPressed += () => OpenMenu("Ctrl+End");
        bool hot = _host.TryRegisterHotkey();
        _host.InstallHook();
        _log?.Info("SecAgent", "Ativo. Ctrl+End. RegisterHotKey=" + hot + " User=" + Environment.UserName);

        _triggerTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _triggerTimer.Tick += (_, _) =>
        {
            string path = Path.Combine(ProductPaths.State, "open-security.trigger");
            if (!File.Exists(path)) return;
            try { File.Delete(path); } catch { /* ignore */ }
            OpenMenu("trigger");
        };
        _triggerTimer.Start();

        try
        {
            Directory.CreateDirectory(ProductPaths.Logs);
            File.WriteAllText(
                Path.Combine(ProductPaths.Logs, "security-agent-alive.txt"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " PID=" + Environment.ProcessId + " Ctrl+End");
        }
        catch { /* ignore */ }
    }

    private static void ApplySessionCadEmpty()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Policies\System", true);
            if (key == null) return;
            key.SetValue("DisableTaskMgr", 1, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("DisableChangePassword", 1, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("DisableLockWorkstation", 1, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("HideFastUserSwitching", 1, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch { /* ignore */ }
    }

    private void OpenMenu(string reason)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _log?.Info("SecAgent", "Menu aberto: " + reason);
            using var form = new SystemSecurityForm(_pin);
            form.ShowDialog();
            if (form.ResultAction is SystemSecurityForm.SecurityAction.Reboot
                or SystemSecurityForm.SecurityAction.Shutdown
                or SystemSecurityForm.SecurityAction.OpenExplorer)
            {
                SystemSecurityForm.RunAction(form.ResultAction);
            }
        }
        catch (Exception ex)
        {
            _log?.Error("SecAgent", ex.Message, errorCode: "TR-SEC");
        }
        finally
        {
            _busy = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _triggerTimer.Stop();
            _triggerTimer.Dispose();
            _host.Dispose();
            try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
            _mutex?.Dispose();
        }
        base.Dispose(disposing);
    }

    public static void RegisterAndStart(ITurboRamaLogger? log = null)
    {
        string exe = Path.Combine(ProductPaths.AppLauncher, "TurboRama.Launcher.exe");
        if (!File.Exists(exe)) exe = Application.ExecutablePath;
        string cmd = "\"" + exe + "\" --security-agent";

        try
        {
            using var cu = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            cu?.SetValue("TurboRamaSecurityAgent", cmd);
        }
        catch (Exception ex) { log?.Warning("SecAgent", "HKCU: " + ex.Message); }

        try
        {
            using var lm = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            lm?.SetValue("TurboRamaSecurityAgent", cmd);
        }
        catch (Exception ex) { log?.Warning("SecAgent", "HKLM: " + ex.Message); }

        try
        {
            Run("schtasks.exe", "/Delete /TN \"TurboRamaSecurityAgent\" /F");
            Run("schtasks.exe",
                "/Create /TN \"TurboRamaSecurityAgent\" /SC ONLOGON /RL LIMITED /F " +
                "/TR \"\\\"" + exe + "\\\" --security-agent\"");
        }
        catch (Exception ex) { log?.Warning("SecAgent", "task: " + ex.Message); }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--security-agent",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
            });
            log?.Info("SecAgent", "Registado e iniciado. Ctrl+End = menu TurboRama.");
        }
        catch (Exception ex) { log?.Warning("SecAgent", "start: " + ex.Message); }
    }

    private static void Run(string file, string args)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = file, Arguments = args, CreateNoWindow = true, UseShellExecute = false
        });
        p?.WaitForExit(15000);
    }

    private sealed class NativeHost : NativeWindow, IDisposable
    {
        public event Action? CtrlEndPressed;
        private const int HotId = 0x7E01;
        private const int WmHotkey = 0x0312;
        private const uint ModCtrl = 0x0002;
        private const uint ModNoRepeat = 0x4000;
        private const uint VkEnd = 0x23;
        private IntPtr _hook;
        private LowLevelKeyboardProc? _proc;
        private bool _ctrl;
        private bool _armed = true;

        public NativeHost()
        {
            CreateHandle(new CreateParams { Caption = "TR.SecHost", Width = 0, Height = 0 });
        }

        public bool TryRegisterHotkey()
        {
            if (RegisterHotKey(Handle, HotId, ModCtrl | ModNoRepeat, VkEnd)) return true;
            return RegisterHotKey(Handle, HotId, ModCtrl, VkEnd);
        }

        public void InstallHook()
        {
            _proc = Cb;
            using var cur = Process.GetCurrentProcess();
            using var mod = cur.MainModule!;
            _hook = SetWindowsHookEx(13, _proc, GetModuleHandle(mod.ModuleName!), 0);
        }

        private IntPtr Cb(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    int vk = Marshal.ReadInt32(lParam);
                    const int down = 0x100, sdown = 0x104, up = 0x101, sup = 0x105;
                    bool isCtrl = vk is 0x11 or 0xA2 or 0xA3;
                    if (msg is down or sdown)
                    {
                        if (isCtrl) _ctrl = true;
                        else if (vk == 0x23 && (_ctrl || CtrlHeld()) && _armed)
                        {
                            _armed = false;
                            PostMessage(Handle, WmHotkey, new IntPtr(HotId), IntPtr.Zero);
                        }
                    }
                    else if (msg is up or sup)
                    {
                        if (isCtrl) _ctrl = false;
                        if (vk == 0x23) _armed = true;
                    }
                }
            }
            catch { /* ignore */ }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static bool CtrlHeld() =>
            (GetAsyncKeyState(0x11) & 0x8000) != 0 ||
            (GetAsyncKeyState(0xA2) & 0x8000) != 0 ||
            (GetAsyncKeyState(0xA3) & 0x8000) != 0;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotId && (_ctrl || CtrlHeld()))
                CtrlEndPressed?.Invoke();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try { UnregisterHotKey(Handle, HotId); } catch { /* ignore */ }
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            DestroyHandle();
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc p, IntPtr m, uint t);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string n);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int v);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    }
}
