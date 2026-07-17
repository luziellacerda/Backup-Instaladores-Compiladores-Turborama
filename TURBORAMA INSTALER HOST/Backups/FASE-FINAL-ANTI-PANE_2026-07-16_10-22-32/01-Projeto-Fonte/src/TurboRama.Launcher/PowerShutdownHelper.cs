using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TurboRama.Core.Logging;

namespace TurboRama.Launcher;

/// <summary>
/// Desliga o PC (power off) com o mínimo de UI do Windows.
/// Usado após o jogador escolher Desligar no menu TurboRama.
/// </summary>
internal static class PowerShutdownHelper
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;
    private const int ErrorNotAllAssigned = 1300;

    private const uint EwxShutdown = 0x00000001;
    private const uint EwxPowerOff = 0x00000008;
    private const uint EwxForce = 0x00000004;
    private const uint EwxForceIfHung = 0x00000010;

    private const uint ShtnForceOthers = 0x00000001;
    private const uint ShtnForceApps = 0x00000002;
    private const uint ShtnPowerOff = 0x00000008;

    private const int ShutdownNoReboot = 0;
    private const int ShutdownPowerOff = 2;

    /// <summary>Reinicia o PC (mesma cadeia de privilégios que o power-off).</summary>
    public static bool RebootNow(out string message, ITurboRamaLogger? logger = null)
    {
        message = string.Empty;
        ApplyCurrentUserFastShutdown(logger);

        try
        {
            SetProcessShutdownParameters(0x100, 0);
        }
        catch
        {
            // ignore
        }

        bool privilegeOk = EnablePrivilege("SeShutdownPrivilege");
        logger?.Info("Shutdown", "Reboot SeShutdownPrivilege ativo=" + privilegeOk);

        if (TryRunShutdownArgs("/r /t 0 /f", out message, logger))
        {
            return true;
        }

        // ExitWindowsEx reboot
        const uint ewxReboot = 0x00000002;
        uint flags = ewxReboot | EwxForceIfHung | EwxForce;
        if (ExitWindowsEx(flags, 0))
        {
            message = "ExitWindowsEx(Reboot) OK.";
            logger?.Info("Shutdown", message);
            return true;
        }

        if (TryRunShutdownArgs("/r /t 1 /f", out message, logger))
        {
            return true;
        }

        message = "Falha ao reiniciar. privilege=" + privilegeOk;
        logger?.Error("Shutdown", message, errorCode: "TR-REBOOT");
        return false;
    }

    public static bool ShutdownNow(out string message, ITurboRamaLogger? logger = null)
    {
        message = string.Empty;
        ApplyCurrentUserFastShutdown(logger);

        try
        {
            // Encerra este processo por último — splash TurboRama fica na tela
            SetProcessShutdownParameters(0x100, 0);
        }
        catch
        {
            // ignore
        }

        bool privilegeOk = EnablePrivilege("SeShutdownPrivilege");
        logger?.Info("Shutdown", "SeShutdownPrivilege ativo=" + privilegeOk);

        // 1) NtShutdownSystem POWER OFF
        try
        {
            uint nt = NtShutdownSystem(ShutdownPowerOff);
            if (nt == 0)
            {
                message = "NtShutdownSystem(PowerOff) OK.";
                logger?.Info("Shutdown", message);
                return true;
            }

            logger?.Warning("Shutdown", "NtShutdownSystem(PowerOff) status=0x" + nt.ToString("X8"));

            nt = NtShutdownSystem(ShutdownNoReboot);
            if (nt == 0)
            {
                message = "NtShutdownSystem(NoReboot) OK.";
                logger?.Info("Shutdown", message);
                return true;
            }

            logger?.Warning("Shutdown", "NtShutdownSystem(NoReboot) status=0x" + nt.ToString("X8"));
        }
        catch (Exception ex)
        {
            logger?.Warning("Shutdown", "NtShutdownSystem: " + ex.Message);
        }

        // 2) shutdown /p /f
        if (TryRunShutdownArgs("/p /f", out message, logger))
        {
            return true;
        }

        // 3) SetSystemPowerState
        try
        {
            if (SetSystemPowerState(false, true))
            {
                message = "SetSystemPowerState OK.";
                logger?.Info("Shutdown", message);
                return true;
            }

            logger?.Warning("Shutdown", "SetSystemPowerState falhou codigo=" + Marshal.GetLastWin32Error());
        }
        catch (Exception ex)
        {
            logger?.Warning("Shutdown", "SetSystemPowerState: " + ex.Message);
        }

        // 4) ExitWindowsEx
        uint flags = EwxShutdown | EwxPowerOff | EwxForceIfHung | EwxForce;
        if (ExitWindowsEx(flags, 0))
        {
            message = "ExitWindowsEx OK.";
            logger?.Info("Shutdown", message);
            return true;
        }

        int errExit = Marshal.GetLastWin32Error();
        logger?.Warning("Shutdown", "ExitWindowsEx falhou codigo=" + errExit);

        // 5) InitiateSystemShutdownEx
        if (InitiateSystemShutdownEx(null, string.Empty, 0, true, false,
                ShtnForceOthers | ShtnForceApps | ShtnPowerOff))
        {
            message = "InitiateSystemShutdownEx OK.";
            logger?.Info("Shutdown", message);
            return true;
        }

        int errInit = Marshal.GetLastWin32Error();

        // 6) último recurso
        if (TryRunShutdownArgs("/s /t 0 /f", out message, logger))
        {
            return true;
        }

        message = "Falha ao desligar. privilege=" + privilegeOk +
                  " ExitWindowsEx=" + errExit + " Initiate=" + errInit;
        logger?.Error("Shutdown", message, errorCode: "TR-SHUT");
        return false;
    }

    private static void ApplyCurrentUserFastShutdown(ITurboRamaLogger? logger)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true)
                                    ?? Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
            if (key != null)
            {
                key.SetValue("AutoEndTasks", "1", RegistryValueKind.String);
            }
        }
        catch (Exception ex)
        {
            logger?.Warning("Shutdown", "FastShutdown reg: " + ex.Message);
        }
    }

    private static bool TryRunShutdownArgs(string arguments, out string message, ITurboRamaLogger? logger)
    {
        message = string.Empty;
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });

            if (process == null)
            {
                return false;
            }

            process.WaitForExit(5000);
            if (process.ExitCode == 0)
            {
                message = "shutdown.exe " + arguments + " OK.";
                logger?.Info("Shutdown", message);
                return true;
            }

            string err = string.Empty;
            try
            {
                err = process.StandardError.ReadToEnd();
            }
            catch
            {
                // ignore
            }

            logger?.Warning("Shutdown", "shutdown.exe " + arguments + " exit=" + process.ExitCode + " " + err);
            return false;
        }
        catch (Exception ex)
        {
            logger?.Warning("Shutdown", "shutdown.exe " + arguments + ": " + ex.Message);
            return false;
        }
    }

    private static bool EnablePrivilege(string privilegeName)
    {
        IntPtr tokenHandle = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAdjustPrivileges | TokenQuery, out tokenHandle))
            {
                return false;
            }

            if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
            {
                return false;
            }

            var tokenPrivileges = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SePrivilegeEnabled
            };

            if (!AdjustTokenPrivileges(tokenHandle, false, ref tokenPrivileges, 0, IntPtr.Zero, IntPtr.Zero))
            {
                return false;
            }

            return Marshal.GetLastWin32Error() != ErrorNotAllAssigned;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero)
            {
                CloseHandle(tokenHandle);
            }
        }
    }

    [DllImport("ntdll.dll")]
    private static extern uint NtShutdownSystem(int action);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool InitiateSystemShutdownEx(
        string? lpMachineName,
        string lpMessage,
        uint dwTimeout,
        bool bForceAppsClosed,
        bool bRebootAfterShutdown,
        uint dwReason);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetSystemPowerState(bool fSuspend, bool bForce);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessShutdownParameters(uint dwLevel, uint dwFlags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }
}
