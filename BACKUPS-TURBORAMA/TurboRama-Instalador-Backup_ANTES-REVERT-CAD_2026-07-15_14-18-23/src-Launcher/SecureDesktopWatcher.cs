using System.Runtime.InteropServices;
using System.Text;

namespace TurboRama.Launcher;

/// <summary>
/// Equivalente kiosk ao fluxo Ctrl+Alt+Del:
/// o Windows manda o SAS para o desktop Winlogon (não capturável em user-mode).
/// Quando o utilizador volta desse desktop, abrimos o painel TurboRama (como o CAD, mas nosso).
/// </summary>
internal sealed class SecureDesktopWatcher
{
    private bool _wasOnSecure;
    private int _pendingOpen;

    /// <summary>True se devemos abrir o painel (consumo único).</summary>
    public bool ConsumePendingOpen()
    {
        return Interlocked.Exchange(ref _pendingOpen, 0) > 0;
    }

    public void Poll()
    {
        try
        {
            bool onSecure = IsOnSecureDesktop();
            if (onSecure)
            {
                _wasOnSecure = true;
                return;
            }

            if (_wasOnSecure)
            {
                // Saiu do CAD / Winlogon → pedir painel TurboRama
                Interlocked.Increment(ref _pendingOpen);
                _wasOnSecure = false;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static bool IsOnSecureDesktop()
    {
        // DESKTOP_READOBJECTS = 0x0001
        IntPtr hDesk = OpenInputDesktop(0, false, 0x0001u);
        if (hDesk == IntPtr.Zero)
        {
            // Sem acesso ao desktop de input → tipicamente desktop seguro (Winlogon/CAD)
            return true;
        }

        try
        {
            byte[] name = new byte[512];
            if (!GetUserObjectInformation(hDesk, 2 /*UOI_NAME*/, name, (uint)name.Length, out _))
            {
                return false;
            }

            string desktopName = Encoding.Unicode.GetString(name).TrimEnd('\0');
            return desktopName.Contains("Winlogon", StringComparison.OrdinalIgnoreCase)
                   || desktopName.Contains("Disconnect", StringComparison.OrdinalIgnoreCase)
                   || desktopName.Contains("Screen-saver", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CloseDesktop(hDesk);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetUserObjectInformation(
        IntPtr hObj,
        int nIndex,
        byte[] pvInfo,
        uint nLength,
        out uint lpnLengthNeeded);
}
