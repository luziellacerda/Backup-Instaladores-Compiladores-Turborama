using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TurboRama.ArcadeTimer.Services;

public sealed class GlobalKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;

    private readonly LowLevelKeyboardProc _callback;
    private IntPtr _hookId;

    public event Action<Keys>? KeyPressed;

    public GlobalKeyboardHook()
    {
        _callback = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero)
            return;

        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;

        IntPtr moduleHandle = module is null
            ? IntPtr.Zero
            : GetModuleHandle(module.ModuleName);

        _hookId = SetWindowsHookEx(
            WhKeyboardLl,
            _callback,
            moduleHandle,
            0);

        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException("Não foi possível instalar o hook global.");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 &&
            (wParam == (IntPtr)WmKeyDown ||
             wParam == (IntPtr)WmSysKeyDown))
        {
            KeyPressed?.Invoke((Keys)Marshal.ReadInt32(lParam));
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
