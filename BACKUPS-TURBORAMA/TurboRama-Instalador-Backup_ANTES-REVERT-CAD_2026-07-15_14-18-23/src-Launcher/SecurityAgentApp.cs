using System.Diagnostics;
using System.Runtime.InteropServices;
using TurboRama.Configuration;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Agente de segurança (todas as contas / sempre vivo).
/// Ctrl+Alt+Del fica desativado (CadShield no instalador).
/// Atalho do menu TurboRama (substitui o CAD): Ctrl+End.
/// Arquitetura inspirada no FactoryShell, implementação nova.
/// </summary>
internal sealed class SecurityAgentApp : ApplicationContext
{
    private static Mutex? _mutex;
    private readonly AgentNativeWindow _window;
    private readonly System.Windows.Forms.Timer _triggerTimer;
    private readonly System.Windows.Forms.Timer _aliveTimer;
    private readonly ITurboRamaLogger? _log;
    private readonly string _pin;
    private bool _uiBusy;
    private NotifyIcon? _tray;

    public SecurityAgentApp(ITurboRamaLogger? log)
    {
        _log = log;
        ConfigurationStore.Load(out ProductConfiguration config);
        _pin = string.IsNullOrWhiteSpace(config.OperatorPin)
            ? FactoryDefaults.ResolveKioskPassword(config)
            : config.OperatorPin.Trim();

        bool created;
        try
        {
            _mutex = new Mutex(true, "Local\\TurboRama.SecurityAgent." + Environment.UserName, out created);
        }
        catch
        {
            created = true;
        }

        if (!created)
        {
            _log?.Info("SecurityAgent", "Já existe agente nesta sessão — a sair.");
            Environment.Exit(0);
            return;
        }

        CadRuntimeShield.ApplyCurrentUser();

        _window = new AgentNativeWindow();
        _window.HotkeyFired += OpenSecurityUi;

        bool hotOk = _window.RegisterCtrlEndHotkey();
        _window.InstallLowLevelHook();
        _log?.Info("SecurityAgent",
            "Iniciado User=" + Environment.UserName +
            " PID=" + Environment.ProcessId +
            " RegisterHotKey=" + hotOk + " Hook=ON (Ctrl+End)");

        try
        {
            _tray = new NotifyIcon
            {
                Text = "TurboRama Security (Ctrl+End)",
                Visible = true,
                Icon = SystemIcons.Shield
            };
            _tray.DoubleClick += (_, _) => OpenSecurityUi("tray");
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir menu TurboRama", null, (_, _) => OpenSecurityUi("tray-menu"));
            _tray.ContextMenuStrip = menu;
        }
        catch
        {
            _tray = null;
        }

        // Trigger file: C:\TurboRama\State\open-security.trigger
        _triggerTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _triggerTimer.Tick += (_, _) => PollTriggerFile();
        _triggerTimer.Start();

        _aliveTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _aliveTimer.Tick += (_, _) => WriteAlive();
        _aliveTimer.Start();
        WriteAlive();
    }

    private void PollTriggerFile()
    {
        try
        {
            string path = Path.Combine(ProductPaths.State, "open-security.trigger");
            if (!File.Exists(path))
            {
                return;
            }

            try { File.Delete(path); } catch { /* ignore */ }
            OpenSecurityUi("trigger-file");
        }
        catch
        {
            // ignore
        }
    }

    private void WriteAlive()
    {
        try
        {
            Directory.CreateDirectory(ProductPaths.Logs);
            File.WriteAllText(
                Path.Combine(ProductPaths.Logs, "security-agent-alive.txt"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                " PID=" + Environment.ProcessId +
                " User=" + Environment.UserName +
                " Hotkey=Ctrl+End");
        }
        catch
        {
            // ignore
        }
    }

    private void OpenSecurityUi(string reason)
    {
        if (_uiBusy)
        {
            return;
        }

        _uiBusy = true;
        try
        {
            _log?.Info("SecurityAgent", "Abrir menu TurboRama. Motivo=" + reason);
            using var form = new OperatorConsoleForm(_pin);
            form.TopMost = true;
            form.ShowDialog();
            var action = form.ChosenAction;
            _log?.Info("SecurityAgent", "Ação=" + action);

            switch (action)
            {
                case OperatorConsoleForm.OperatorAction.OpenDesktop:
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            UseShellExecute = true
                        });
                    }
                    catch { /* ignore */ }
                    break;

                case OperatorConsoleForm.OperatorAction.RebootMachine:
                    PowerShutdownHelper.RebootNow(out _, _log);
                    break;

                case OperatorConsoleForm.OperatorAction.PowerOffMachine:
                    PowerShutdownHelper.ShutdownNow(out _, _log);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log?.Error("SecurityAgent", "UI: " + ex.Message, errorCode: "TR-SEC-UI");
        }
        finally
        {
            _uiBusy = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _triggerTimer.Stop();
            _triggerTimer.Dispose();
            _aliveTimer.Stop();
            _aliveTimer.Dispose();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }

            _window.Dispose();
            try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
            _mutex?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Regista agente no logon (Run + Startup + schtasks).</summary>
    public static void RegisterAtLogon(ITurboRamaLogger? log = null)
    {
        try
        {
            string exe = Path.Combine(ProductPaths.AppLauncher, "TurboRama.Launcher.exe");
            if (!File.Exists(exe))
            {
                exe = Application.ExecutablePath;
            }

            string value = "\"" + exe + "\" --security-agent";

            using (var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                       @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                key?.SetValue("TurboRamaSecurityAgent", value);
            }

            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                       @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                key?.SetValue("TurboRamaSecurityAgent", value);
            }

            try
            {
                string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                Directory.CreateDirectory(commonStartup);
                File.WriteAllText(
                    Path.Combine(commonStartup, "TurboRamaSecurityAgent.bat"),
                    "@echo off\r\ntimeout /t 3 /nobreak >nul\r\nstart \"\" " + value + "\r\n");
            }
            catch
            {
                // ignore
            }

            // Task Scheduler ONLOGON
            try
            {
                RunSchtasks("/Delete /TN \"TurboRamaSecurityAgent\" /F");
                RunSchtasks(
                    "/Create /TN \"TurboRamaSecurityAgent\" /SC ONLOGON /RL LIMITED /F " +
                    "/TR \"\\\"" + exe + "\\\" --security-agent\"");
            }
            catch (Exception ex)
            {
                log?.Warning("SecurityAgent", "schtasks: " + ex.Message);
            }

            log?.Info("SecurityAgent", "Registado no logon: " + value);
        }
        catch (Exception ex)
        {
            log?.Warning("SecurityAgent", "RegisterAtLogon: " + ex.Message);
        }
    }

    /// <summary>Garante que o agente está a correr nesta sessão.</summary>
    public static void EnsureRunningOnce(ITurboRamaLogger? log = null)
    {
        try
        {
            foreach (Process p in Process.GetProcessesByName("TurboRama.Launcher"))
            {
                try
                {
                    // Heurística: se já há launcher e não somos o agente, arrancar agente mesmo assim
                    // (mutex do agente impede duplicados)
                }
                finally
                {
                    p.Dispose();
                }
            }

            string alive = Path.Combine(ProductPaths.Logs, "security-agent-alive.txt");
            if (File.Exists(alive))
            {
                try
                {
                    string t = File.ReadAllText(alive);
                    // se escreveu há < 90s, considera vivo
                    if (DateTime.TryParse(t.Split(' ')[0] + " " + t.Split(' ')[1], out DateTime dt) &&
                        (DateTime.Now - dt).TotalSeconds < 90)
                    {
                        // ainda verifica se processo existe com args — simplifica: tenta start, mutex sai
                    }
                }
                catch
                {
                    // ignore
                }
            }

            string exe = Path.Combine(ProductPaths.AppLauncher, "TurboRama.Launcher.exe");
            if (!File.Exists(exe))
            {
                exe = Application.ExecutablePath;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--security-agent",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ProductPaths.AppLauncher
            });
            log?.Info("SecurityAgent", "EnsureRunningOnce: start --security-agent");
        }
        catch (Exception ex)
        {
            log?.Warning("SecurityAgent", "EnsureRunningOnce: " + ex.Message);
        }
    }

    public static void RequestOpenViaTrigger()
    {
        try
        {
            Directory.CreateDirectory(ProductPaths.State);
            File.WriteAllText(Path.Combine(ProductPaths.State, "open-security.trigger"), DateTime.Now.ToString("o"));
        }
        catch
        {
            // ignore
        }
    }

    private static void RunSchtasks(string args)
    {
        using Process? p = Process.Start(new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        p?.WaitForExit(15000);
    }

    /// <summary>Janela nativa oculta: WM_HOTKEY + hook Ctrl+End.</summary>
    private sealed class AgentNativeWindow : NativeWindow, IDisposable
    {
        public event Action<string>? HotkeyFired;

        private const int HotkeyId = 0x54E1;
        private const int WmHotkey = 0x0312;
        private const uint ModControl = 0x0002;
        private const uint ModNoRepeat = 0x4000;
        private const uint VkEnd = 0x23;
        private const int VkCtrl = 0x11;
        private const int VkLCtrl = 0xA2;
        private const int VkRCtrl = 0xA3;
        private const int VkEndI = 0x23;

        private IntPtr _hook;
        private LowLevelKeyboardProc? _hookProc;
        private bool _ctrlDown;
        private bool _armed = true;

        public AgentNativeWindow()
        {
            CreateHandle(new CreateParams
            {
                Caption = "TurboRama.SecurityAgent",
                X = 0,
                Y = 0,
                Width = 0,
                Height = 0,
                Style = 0
            });
        }

        public bool RegisterCtrlEndHotkey()
        {
            bool ok = RegisterHotKey(Handle, HotkeyId, ModControl | ModNoRepeat, VkEnd);
            if (!ok)
            {
                ok = RegisterHotKey(Handle, HotkeyId, ModControl, VkEnd);
            }

            return ok;
        }

        public void InstallLowLevelHook()
        {
            _hookProc = HookCallback;
            using Process cur = Process.GetCurrentProcess();
            using ProcessModule mod = cur.MainModule!;
            _hook = SetWindowsHookEx(13, _hookProc, GetModuleHandle(mod.ModuleName!), 0);
        }

        private static bool IsCtrl(int vk) => vk is VkCtrl or VkLCtrl or VkRCtrl;

        private static bool IsCtrlHeld() =>
            (GetAsyncKeyState(VkCtrl) & 0x8000) != 0 ||
            (GetAsyncKeyState(VkLCtrl) & 0x8000) != 0 ||
            (GetAsyncKeyState(VkRCtrl) & 0x8000) != 0;

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    const int WmKeyDown = 0x0100, WmSysKeyDown = 0x0104;
                    const int WmKeyUp = 0x0101, WmSysKeyUp = 0x0105;
                    int vk = Marshal.ReadInt32(lParam);

                    if (msg is WmKeyDown or WmSysKeyDown)
                    {
                        if (IsCtrl(vk))
                        {
                            _ctrlDown = true;
                        }
                        else if (vk == VkEndI && (_ctrlDown || IsCtrlHeld()) && _armed)
                        {
                            _armed = false;
                            PostMessage(Handle, WmHotkey, new IntPtr(HotkeyId), IntPtr.Zero);
                        }
                    }
                    else if (msg is WmKeyUp or WmSysKeyUp)
                    {
                        if (IsCtrl(vk))
                        {
                            _ctrlDown = false;
                        }

                        if (vk == VkEndI)
                        {
                            _armed = true;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
            {
                if (_ctrlDown || IsCtrlHeld())
                {
                    HotkeyFired?.Invoke("Ctrl+End");
                }
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try { UnregisterHotKey(Handle, HotkeyId); } catch { /* ignore */ }
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            DestroyHandle();
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
