using System.Diagnostics;
using Microsoft.Win32;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Registo do agente de segurança (Ctrl+End) + reforço CAD.
/// Chamado pelo instalador (Admin) e pelo Launcher no logon Arcade.
/// </summary>
internal static class SecurityRegistration
{
    public const string RunValueName = "TurboRamaSecurityAgent";
    public const string TaskName = "TurboRamaSecurityAgent";

    public static string AgentCommand
    {
        get
        {
            string exe = Path.Combine(ProductPaths.AppLauncher, "TurboRama.Launcher.exe");
            if (!File.Exists(exe))
            {
                exe = Application.ExecutablePath;
            }

            return "\"" + exe + "\" --security-agent";
        }
    }

    public static string AgentExe
    {
        get
        {
            string exe = Path.Combine(ProductPaths.AppLauncher, "TurboRama.Launcher.exe");
            return File.Exists(exe) ? exe : Application.ExecutablePath;
        }
    }

    /// <summary>Regista Run + Startup + schtasks. Preferir Admin para HKLM/tasks.</summary>
    public static void RegisterEverywhere(ITurboRamaLogger? log = null)
    {
        try
        {
            ProductPaths.EnsureLayout();
            string cmd = AgentCommand;
            string exe = AgentExe;

            // HKCU (sempre possível na sessão)
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                key?.SetValue(RunValueName, cmd);
                log?.Info("SecurityReg", "HKCU Run OK");
            }
            catch (Exception ex)
            {
                log?.Warning("SecurityReg", "HKCU Run: " + ex.Message);
            }

            // HKLM (precisa Admin)
            try
            {
                using RegistryKey? key = Registry.LocalMachine.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                key?.SetValue(RunValueName, cmd);
                log?.Info("SecurityReg", "HKLM Run OK");
            }
            catch (Exception ex)
            {
                log?.Warning("SecurityReg", "HKLM Run (precisa Admin): " + ex.Message);
            }

            // Startup comum
            try
            {
                string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                if (!string.IsNullOrEmpty(commonStartup))
                {
                    Directory.CreateDirectory(commonStartup);
                    File.WriteAllText(
                        Path.Combine(commonStartup, "TurboRamaSecurityAgent.bat"),
                        "@echo off\r\n" +
                        "timeout /t 3 /nobreak >nul\r\n" +
                        "start \"\" " + cmd + "\r\n");
                    log?.Info("SecurityReg", "CommonStartup OK");
                }
            }
            catch (Exception ex)
            {
                log?.Warning("SecurityReg", "Startup: " + ex.Message);
            }

            // Startup do utilizador
            try
            {
                string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (!string.IsNullOrEmpty(userStartup))
                {
                    Directory.CreateDirectory(userStartup);
                    File.WriteAllText(
                        Path.Combine(userStartup, "TurboRamaSecurityAgent.bat"),
                        "@echo off\r\n" +
                        "timeout /t 2 /nobreak >nul\r\n" +
                        "start \"\" " + cmd + "\r\n");
                }
            }
            catch
            {
                // ignore
            }

            // Task Scheduler
            try
            {
                RunHidden("schtasks.exe", "/Delete /TN \"" + TaskName + "\" /F");
                int code = RunHidden(
                    "schtasks.exe",
                    "/Create /TN \"" + TaskName + "\" /SC ONLOGON /RL LIMITED /F " +
                    "/TR \"\\\"" + exe + "\\\" --security-agent\"");
                log?.Info("SecurityReg", "schtasks exit=" + code);
            }
            catch (Exception ex)
            {
                log?.Warning("SecurityReg", "schtasks: " + ex.Message);
            }

            // Atalho de teste no Desktop
            try
            {
                string desk = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                if (string.IsNullOrEmpty(desk) || !Directory.Exists(desk))
                {
                    desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                }

                if (!string.IsNullOrEmpty(desk))
                {
                    File.WriteAllText(
                        Path.Combine(desk, "ABRIR-SEGURANCA-TURBORAMA.bat"),
                        "@echo off\r\n" +
                        "start \"\" \"" + exe + "\" --test-operator\r\n");
                }
            }
            catch
            {
                // ignore
            }

            log?.Info("SecurityReg", "Registo concluído. Comando=" + cmd);
        }
        catch (Exception ex)
        {
            log?.Error("SecurityReg", ex.Message, errorCode: "TR-SEC-REG");
        }
    }

    public static void EnsureAgentRunning(ITurboRamaLogger? log = null)
    {
        try
        {
            // Se alive recente, ainda reinicia (mutex evita duplicado)
            string exe = AgentExe;
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--security-agent",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ProductPaths.AppLauncher
            });
            log?.Info("SecurityReg", "Agente iniciado: --security-agent");
        }
        catch (Exception ex)
        {
            log?.Warning("SecurityReg", "EnsureAgentRunning: " + ex.Message);
        }
    }

    private static int RunHidden(string file, string args)
    {
        using Process? p = Process.Start(new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (p == null)
        {
            return -1;
        }

        p.WaitForExit(20000);
        return p.ExitCode;
    }
}
