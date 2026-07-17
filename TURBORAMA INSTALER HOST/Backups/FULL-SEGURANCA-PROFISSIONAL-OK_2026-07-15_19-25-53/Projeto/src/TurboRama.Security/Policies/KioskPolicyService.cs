using System.Text.Json;
using Microsoft.Win32;
using TurboRama.Core.Baseline;
using TurboRama.Core.Results;
using TurboRama.Windows.Accounts;
using TurboRama.Windows.Exec;
using TurboRama.Windows.Registry;

namespace TurboRama.Security.Policies;

/// <summary>
/// Políticas por SID da conta kiosk (estudo §15 camada 2) — não HKLM global.
/// </summary>
public static class KioskPolicyService
{
    private static readonly (string RelativeSubKey, string Name, object Value, RegistryValueKind Kind)[] Policies =
    {
        (@"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr", 1, RegistryValueKind.DWord),
        (@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoControlPanel", 1, RegistryValueKind.DWord),
        (@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRun", 1, RegistryValueKind.DWord),
        (@"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableLockWorkstation", 1, RegistryValueKind.DWord),
        (@"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableRegistryTools", 1, RegistryValueKind.DWord),
        (@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "StartMenuLogOff", 1, RegistryValueKind.DWord),
        (@"Software\Microsoft\Windows\CurrentVersion\Policies\System", "HideFastUserSwitching", 1, RegistryValueKind.DWord),
    };

    public static OperationResult ApplyForUser(string userName, string backupJsonPath, out List<RegistryValueSnapshot> captured)
    {
        captured = new List<RegistryValueSnapshot>();
        LocalAccountInfo account = LocalAccountService.GetInfo(userName);
        if (!account.Exists || string.IsNullOrEmpty(account.Sid))
        {
            return OperationResult.Fail("SID da conta kiosk indisponível.", "POL_SID", "ApplyForUser");
        }

        string? profile = account.ProfilePath ?? Path.Combine(@"C:\Users", userName);
        string ntuser = Path.Combine(profile, "NTUSER.DAT");
        if (!File.Exists(ntuser))
        {
            return OperationResult.Fail("Perfil/NTUSER ausente para políticas.", "POL_PROFILE", "ApplyForUser");
        }

        const string temp = "TurboRamaPolTemp";
        string hiveRoot = @"HKEY_USERS\" + temp;
        bool loaded = false;
        try
        {
            // sessão viva?
            using RegistryKey? liveRoot = Microsoft.Win32.Registry.Users.OpenSubKey(account.Sid, true);
            if (liveRoot is not null)
            {
                return ApplyOnHive(RegistryHive.Users, account.Sid, account.Sid, backupJsonPath, out captured);
            }

            OperationResult load = ProcessRunner.Run("reg.exe", "load \"" + hiveRoot + "\" \"" + ntuser + "\"", operationName: "pol-load");
            if (!load.Success)
            {
                return OperationResult.Fail("Load hive políticas: " + load.Message, "POL_LOAD", "ApplyForUser");
            }

            loaded = true;
            return ApplyOnHive(RegistryHive.Users, temp, account.Sid, backupJsonPath, out captured);
        }
        finally
        {
            if (loaded)
            {
                ProcessRunner.Run("reg.exe", "unload \"" + hiveRoot + "\"", operationName: "pol-unload");
            }
        }
    }

    private static OperationResult ApplyOnHive(
        RegistryHive hive,
        string hivePrefix,
        string sidLabel,
        string backupJsonPath,
        out List<RegistryValueSnapshot> captured)
    {
        captured = new List<RegistryValueSnapshot>();
        foreach (var (rel, name, value, kind) in Policies)
        {
            string sub = hivePrefix + "\\" + rel;
            RegistryValueSnapshot snap = RegistryValueHelper.Capture(hive, sub, name, RegistryView.Registry64);
            snap.Path = @"HKU\" + sidLabel + "\\" + rel;
            captured.Add(snap);
            OperationResult set = RegistryValueHelper.SetValue(hive, sub, name, value, kind);
            if (!set.Success)
            {
                return set;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(backupJsonPath)!);
        File.WriteAllText(backupJsonPath, JsonSerializer.Serialize(captured, new JsonSerializerOptions { WriteIndented = true }));
        return OperationResult.Ok("Políticas kiosk aplicadas (" + Policies.Length + ").", "ApplyForUser");
    }

    public static OperationResult RestoreFromBackup(string userName, string backupJsonPath)
    {
        if (!File.Exists(backupJsonPath))
        {
            return OperationResult.Fail("Backup de políticas ausente.", "POL_RB_MISS", "RestoreFromBackup");
        }

        List<RegistryValueSnapshot>? snaps =
            JsonSerializer.Deserialize<List<RegistryValueSnapshot>>(File.ReadAllText(backupJsonPath));
        if (snaps is null)
        {
            return OperationResult.Fail("Backup de políticas inválido.", "POL_RB_PARSE", "RestoreFromBackup");
        }

        LocalAccountInfo account = LocalAccountService.GetInfo(userName);
        string? profile = account.ProfilePath ?? Path.Combine(@"C:\Users", userName);
        string ntuser = Path.Combine(profile, "NTUSER.DAT");
        const string temp = "TurboRamaPolTemp";
        string hiveRoot = @"HKEY_USERS\" + temp;
        bool loaded = false;
        try
        {
            if (!string.IsNullOrEmpty(account.Sid) && Microsoft.Win32.Registry.Users.OpenSubKey(account.Sid) is not null)
            {
                int ok = 0;
                foreach (RegistryValueSnapshot s in snaps)
                {
                    if (RegistryValueHelper.Restore(s).Success) ok++;
                }

                return OperationResult.Ok("Políticas restauradas (live): " + ok, "RestoreFromBackup");
            }

            if (!File.Exists(ntuser))
            {
                return OperationResult.Ok("Sem NTUSER para restaurar políticas.", "RestoreFromBackup");
            }

            OperationResult load = ProcessRunner.Run("reg.exe", "load \"" + hiveRoot + "\" \"" + ntuser + "\"", operationName: "pol-rb-load");
            if (!load.Success)
            {
                return OperationResult.Fail(load.Message, "POL_RB_LOAD", "RestoreFromBackup");
            }

            loaded = true;
            int restored = 0;
            foreach (RegistryValueSnapshot s in snaps)
            {
                // remap path to temp hive
                string path = s.Path;
                int idx = path.IndexOf('\\');
                string rest = idx >= 0 ? path[(idx + 1)..] : path;
                // strip SID prefix
                int second = rest.IndexOf('\\');
                string rel = second >= 0 ? rest[(second + 1)..] : rest;
                var mapped = new RegistryValueSnapshot
                {
                    Path = @"HKU\" + temp + "\\" + rel,
                    Name = s.Name,
                    RegistryView = s.RegistryView,
                    Existed = s.Existed,
                    Kind = s.Kind,
                    Value = s.Value
                };
                if (RegistryValueHelper.Restore(mapped).Success) restored++;
            }

            return OperationResult.Ok("Políticas restauradas: " + restored, "RestoreFromBackup");
        }
        finally
        {
            if (loaded)
            {
                ProcessRunner.Run("reg.exe", "unload \"" + hiveRoot + "\"", operationName: "pol-rb-unload");
            }
        }
    }
}
