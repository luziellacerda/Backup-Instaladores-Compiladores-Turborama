using Microsoft.Win32;
using TurboRama.Core.Baseline;
using TurboRama.Core.Results;
using TurboRama.Windows.Accounts;
using TurboRama.Windows.Exec;
using TurboRama.Windows.Registry;

namespace TurboRama.Windows.Shell;

/// <summary>
/// Shell por usuário (hive NTUSER) — NÃO altera HKLM Winlogon global (estudo §11).
/// </summary>
public static class UserShellService
{
    private const string WinlogonSub = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
    private const string TempHiveName = "TurboRamaHiveTemp";

    public static OperationResult SetUserShell(string userName, string shellCommand, out RegistryValueSnapshot captured)
    {
        captured = new RegistryValueSnapshot
        {
            Path = "HKU\\?\\" + WinlogonSub,
            Name = "Shell",
            RegistryView = "Registry64",
            Existed = false
        };

        LocalAccountInfo account = LocalAccountService.GetInfo(userName);
        if (!account.Exists)
        {
            return OperationResult.Fail("Conta inexistente: " + userName, "SHELL_NO_USER", "SetUserShell");
        }

        string? profile = account.ProfilePath;
        if (string.IsNullOrWhiteSpace(profile) || !Directory.Exists(profile))
        {
            profile = Path.Combine(@"C:\Users", userName);
        }

        string ntuser = Path.Combine(profile, "NTUSER.DAT");
        if (!File.Exists(ntuser))
        {
            return OperationResult.Fail(
                "NTUSER.DAT ausente (perfil não criado). Faça um logon controlado da conta " + userName + " uma vez.",
                "SHELL_NO_PROFILE",
                "SetUserShell",
                currentState: ntuser);
        }

        bool loaded = false;
        string hiveRoot = @"HKEY_USERS\" + TempHiveName;
        try
        {
            // Se usuário está logado, pode editar via SID em HKU
            if (!string.IsNullOrEmpty(account.Sid))
            {
                using RegistryKey? live = Microsoft.Win32.Registry.Users.OpenSubKey(account.Sid + "\\" + WinlogonSub, true);
                if (live is not null)
                {
                    captured = RegistryValueHelper.Capture(
                        RegistryHive.Users, account.Sid + "\\" + WinlogonSub, "Shell", RegistryView.Registry64);
                    live.SetValue("Shell", shellCommand, RegistryValueKind.String);
                    return OperationResult.Ok(
                        "Shell do usuário (sessão viva) = " + shellCommand,
                        "SetUserShell",
                        previousState: captured.Value,
                        currentState: shellCommand);
                }
            }

            OperationResult load = ProcessRunner.Run(
                "reg.exe",
                "load \"" + hiveRoot + "\" \"" + ntuser + "\"",
                operationName: "reg-load");
            if (!load.Success)
            {
                return OperationResult.Fail(
                    "Falha ao carregar hive do usuário (conta logada? feche a sessão): " + load.Message,
                    "SHELL_LOAD",
                    "SetUserShell");
            }

            loaded = true;
            string sub = TempHiveName + "\\" + WinlogonSub;
            captured = RegistryValueHelper.Capture(RegistryHive.Users, sub, "Shell", RegistryView.Registry64);
            captured.Path = @"HKU\" + (account.Sid ?? TempHiveName) + "\\" + WinlogonSub;

            OperationResult set = RegistryValueHelper.SetValue(
                RegistryHive.Users, sub, "Shell", shellCommand, RegistryValueKind.String);
            if (!set.Success)
            {
                return set;
            }

            return OperationResult.Ok(
                "Shell por usuário aplicado (hive NTUSER): " + shellCommand,
                "SetUserShell",
                previousState: captured.Existed ? captured.Value : "(absent)",
                currentState: shellCommand);
        }
        finally
        {
            if (loaded)
            {
                ProcessRunner.Run("reg.exe", "unload \"" + hiveRoot + "\"", operationName: "reg-unload");
            }
        }
    }

    public static OperationResult RestoreUserShell(string userName, RegistryValueSnapshot original)
    {
        LocalAccountInfo account = LocalAccountService.GetInfo(userName);
        string? profile = account.ProfilePath ?? Path.Combine(@"C:\Users", userName);
        string ntuser = Path.Combine(profile, "NTUSER.DAT");
        if (!File.Exists(ntuser))
        {
            return OperationResult.Ok("Sem NTUSER para restaurar shell.", "RestoreUserShell");
        }

        // Força path para hive temp
        var snap = new RegistryValueSnapshot
        {
            Path = @"HKU\" + TempHiveName + "\\" + WinlogonSub,
            Name = "Shell",
            RegistryView = original.RegistryView,
            Existed = original.Existed,
            Kind = original.Kind ?? "String",
            Value = original.Value
        };

        bool loaded = false;
        string hiveRoot = @"HKEY_USERS\" + TempHiveName;
        try
        {
            if (!string.IsNullOrEmpty(account.Sid))
            {
                using RegistryKey? live = Microsoft.Win32.Registry.Users.OpenSubKey(account.Sid, true);
                if (live is not null)
                {
                    var liveSnap = new RegistryValueSnapshot
                    {
                        Path = @"HKU\" + account.Sid + "\\" + WinlogonSub,
                        Name = "Shell",
                        RegistryView = "Registry64",
                        Existed = original.Existed,
                        Kind = original.Kind ?? "String",
                        Value = original.Value
                    };
                    return RegistryValueHelper.Restore(liveSnap);
                }
            }

            OperationResult load = ProcessRunner.Run("reg.exe", "load \"" + hiveRoot + "\" \"" + ntuser + "\"", operationName: "reg-load");
            if (!load.Success)
            {
                return OperationResult.Fail("Restore shell: " + load.Message, "SHELL_RB_LOAD", "RestoreUserShell");
            }

            loaded = true;
            return RegistryValueHelper.Restore(snap);
        }
        finally
        {
            if (loaded)
            {
                ProcessRunner.Run("reg.exe", "unload \"" + hiveRoot + "\"", operationName: "reg-unload");
            }
        }
    }

    public static string DefaultLauncherCommand =>
        "\"" + Path.Combine(Core.Paths.ProductPaths.AppLauncher, "TurboRama.Launcher.exe") + "\"";
}
