using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TurboRama.Launcher;

/// <summary>
/// Atalho global do painel operador: Alt+End.
/// Desenhado de novo para este Launcher (não usa o hook legado Ctrl+Delete).
/// Thread-safe: o callback só marca um pedido; o loop do Launcher mostra a UI.
/// </summary>
internal static class OperatorHotkeyService
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int VkEnd = 0x23;
    private const int VkMenu = 0x12; // Alt
    private const int VkControl = 0x11;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkTab = 0x09;
    private const int VkEscape = 0x1B;

    private static IntPtr _hook;
    private static LowLevelKeyboardProc? _proc;
    private static int _requestCount;
    private static bool _blockEscapeKeys;
    private static bool _consoleOpen;

    public static bool IsArmed => _hook != IntPtr.Zero;

    /// <summary>Consome um pedido para abrir o painel (Ctrl+End).</summary>
    public static bool ConsumeOpenRequest()
    {
        return Interlocked.Exchange(ref _requestCount, 0) > 0;
    }

    /// <summary>Força pedido de abertura (ex.: retorno do desktop CAD/Winlogon).</summary>
    public static void RequestOpenFromSecureDesktop()
    {
        if (!_consoleOpen)
        {
            Interlocked.Increment(ref _requestCount);
        }
    }

    public static void MarkConsoleOpen(bool open) => _consoleOpen = open;

    public static void Install(bool alsoBlockWinAndAltTab)
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _blockEscapeKeys = alsoBlockWinAndAltTab;
        _proc = HookProc;
        using Process cur = Process.GetCurrentProcess();
        using ProcessModule mod = cur.MainModule!;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(mod.ModuleName!), 0);
    }

    public static void Uninstall()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _proc = null;
    }

    private static void SignalOpen()
    {
        if (!_consoleOpen)
        {
            Interlocked.Increment(ref _requestCount);
        }
    }

    private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown))
        {
            int vk = Marshal.ReadInt32(lParam);
            bool altDown = (GetAsyncKeyState(VkMenu) & 0x8000) != 0;
            bool ctrlDown = (GetAsyncKeyState(VkControl) & 0x8000) != 0;

            // Menu TurboRama = Ctrl+End (substitui Ctrl+Alt+Del)
            if (ctrlDown && vk == VkEnd)
            {
                SignalOpen();
                return (IntPtr)1;
            }

            if (_blockEscapeKeys && !_consoleOpen)
            {
                if (vk is VkLWin or VkRWin)
                {
                    return (IntPtr)1;
                }

                if (altDown && vk == VkTab)
                {
                    return (IntPtr)1;
                }

                if (ctrlDown && vk == VkEscape)
                {
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
