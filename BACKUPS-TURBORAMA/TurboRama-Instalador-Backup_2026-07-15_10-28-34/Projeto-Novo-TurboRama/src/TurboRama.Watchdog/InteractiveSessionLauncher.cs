using System.Runtime.InteropServices;

namespace TurboRama.Watchdog;

/// <summary>
/// Inicia um processo na sessão interativa do console (não em Session 0 do serviço).
/// Process.Start a partir do serviço Watchdog cai em SYSTEM/Session 0 → MessageBox crash + WER.
/// </summary>
internal static class InteractiveSessionLauncher
{
    public static bool TryStartInActiveSession(string exePath, string workingDirectory, out string detail)
    {
        detail = "";
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            detail = "EXE ausente: " + exePath;
            return false;
        }

        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF || sessionId == 0)
        {
            // Session 0 = serviços; 0xFFFFFFFF = nenhuma
            detail = "Sem sessão de console interativa (sessionId=" + sessionId + ").";
            return false;
        }

        IntPtr userToken = IntPtr.Zero;
        IntPtr env = IntPtr.Zero;
        try
        {
            if (!WTSQueryUserToken(sessionId, out userToken))
            {
                detail = "WTSQueryUserToken falhou session=" + sessionId + " err=" + Marshal.GetLastWin32Error();
                return false;
            }

            if (!CreateEnvironmentBlock(out env, userToken, false))
            {
                // Continua sem bloco de ambiente customizado
                env = IntPtr.Zero;
            }

            string cmdLine = "\"" + exePath + "\"";
            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = "winsta0\\default";

            var pi = new PROCESS_INFORMATION();
            uint flags = CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE;

            bool ok = CreateProcessAsUser(
                userToken,
                null,
                cmdLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                flags,
                env,
                string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
                ref si,
                out pi);

            if (!ok)
            {
                detail = "CreateProcessAsUser falhou err=" + Marshal.GetLastWin32Error();
                return false;
            }

            if (pi.hThread != IntPtr.Zero)
            {
                CloseHandle(pi.hThread);
            }

            if (pi.hProcess != IntPtr.Zero)
            {
                CloseHandle(pi.hProcess);
            }

            detail = "PID=" + pi.dwProcessId + " session=" + sessionId;
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
        finally
        {
            if (env != IntPtr.Zero)
            {
                DestroyEnvironmentBlock(env);
            }

            if (userToken != IntPtr.Zero)
            {
                CloseHandle(userToken);
            }
        }
    }

    public static bool HasInteractiveConsoleSession()
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        return sessionId != 0 && sessionId != 0xFFFFFFFF;
    }

    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NEW_CONSOLE = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
