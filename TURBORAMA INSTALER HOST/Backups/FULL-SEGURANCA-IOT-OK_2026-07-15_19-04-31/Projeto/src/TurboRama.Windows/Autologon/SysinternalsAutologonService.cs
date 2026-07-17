using Microsoft.Win32;
using TurboRama.Core.Baseline;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Windows.Exec;
using TurboRama.Windows.Registry;

namespace TurboRama.Windows.Autologon;

/// <summary>
/// Autologon via Sysinternals (sem DefaultPassword em texto quando possível).
/// </summary>
public static class SysinternalsAutologonService
{
    public static string ToolsDir => Path.Combine(ProductPaths.App, "Tools");
    public static string BundledAutologon64 => Path.Combine(ToolsDir, "Autologon64.exe");

    private static readonly (string SubKey, string Name)[] WinlogonValues =
    {
        (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "AutoAdminLogon"),
        (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "DefaultUserName"),
        (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "DefaultDomainName"),
        (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "DefaultPassword"),
        (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "AutoLogonCount"),
    };

    public static List<RegistryValueSnapshot> CaptureWinlogon()
    {
        var list = new List<RegistryValueSnapshot>();
        foreach (var (sub, name) in WinlogonValues)
        {
            list.Add(RegistryValueHelper.Capture(RegistryHive.LocalMachine, sub, name, RegistryView.Registry64));
            list.Add(RegistryValueHelper.Capture(RegistryHive.LocalMachine, sub, name, RegistryView.Registry32));
        }

        return list;
    }

    public static OperationResult EnsureToolAvailable(string? sourceAutologon64 = null)
    {
        Directory.CreateDirectory(ToolsDir);
        if (File.Exists(BundledAutologon64))
        {
            return OperationResult.Ok("Autologon64 presente.", "EnsureTool");
        }

        string[] candidates =
        {
            sourceAutologon64 ?? string.Empty,
            @"D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\TurboRamaFactoryShell\resources\Tools\Autologon64.exe",
            Path.Combine(AppContext.BaseDirectory, "Tools", "Autologon64.exe"),
            Path.Combine(AppContext.BaseDirectory, "resources", "Tools", "Autologon64.exe"),
        };

        foreach (string c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c) && File.Exists(c))
            {
                File.Copy(c, BundledAutologon64, true);
                return OperationResult.Ok("Autologon64 copiado de " + c, "EnsureTool");
            }
        }

        return OperationResult.Fail(
            "Autologon64.exe não encontrado. Coloque em " + ToolsDir,
            "AUTOLOGON_TOOL",
            "EnsureTool");
    }

    public static OperationResult Enable(string userName, string password, string? domain = null)
    {
        OperationResult tool = EnsureToolAvailable();
        if (!tool.Success)
        {
            return tool;
        }

        domain ??= ".";
        // Autologon64 user domain password
        OperationResult run = ProcessRunner.Run(
            BundledAutologon64,
            "\"" + userName + "\" \"" + domain + "\" \"" + password + "\" /accepteula",
            timeoutMs: 45_000,
            operationName: "sysinternals-autologon");

        if (!run.Success)
        {
            return run;
        }

        // Remove DefaultPassword se Sysinternals deixou (preferência LSA secret)
        try
        {
            using RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true);
            key?.DeleteValue("DefaultPassword", throwOnMissingValue: false);
        }
        catch
        {
        }

        return OperationResult.Ok(
            "Autologon configurado para " + userName + " (Sysinternals).",
            "SysinternalsAutologon.Enable",
            currentState: userName);
    }

    public static OperationResult Disable()
    {
        OperationResult tool = EnsureToolAvailable();
        if (tool.Success)
        {
            // Autologon /accepteula without credentials disables in some versions — set registry
        }

        try
        {
            using RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true);
            if (key is not null)
            {
                key.SetValue("AutoAdminLogon", "0", RegistryValueKind.String);
                key.DeleteValue("DefaultPassword", throwOnMissingValue: false);
                key.DeleteValue("DefaultUserName", throwOnMissingValue: false);
                key.DeleteValue("AutoLogonCount", throwOnMissingValue: false);
            }

            return OperationResult.Ok("Autologon desabilitado.", "SysinternalsAutologon.Disable");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Disable autologon: " + ex.Message, "AUTOLOGON_OFF", exception: ex);
        }
    }

    public static OperationResult RestoreSnapshots(IEnumerable<RegistryValueSnapshot> snaps)
    {
        int ok = 0, fail = 0;
        foreach (RegistryValueSnapshot s in snaps)
        {
            OperationResult r = RegistryValueHelper.Restore(s);
            if (r.Success) ok++; else fail++;
        }

        return fail == 0
            ? OperationResult.Ok("Autologon/Winlogon restaurado (" + ok + ").", "RestoreAutologon")
            : OperationResult.Fail("Restore parcial ok=" + ok + " fail=" + fail, "AUTOLOGON_RB_PARTIAL");
    }
}
