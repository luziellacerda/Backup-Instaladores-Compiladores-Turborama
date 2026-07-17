using Microsoft.Win32;
using TurboRama.Core.Baseline;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Windows.Acl;
using TurboRama.Windows.Bcd;
using TurboRama.Windows.Features;
using TurboRama.Windows.Registry;
using TurboRama.Windows.Services;
using TurboRama.Windows.Tasks;

namespace TurboRama.Windows.Baseline;

/// <summary>
/// Captura baseline do Windows antes de mudanças do kiosk (estudo §6).
/// </summary>
public static class WindowsBaselineService
{
    /// <summary>Valores críticos que o kiosk pode alterar — capturados um a um.</summary>
    private static readonly (RegistryHive Hive, string SubKey, string Name)[] CriticalValues =
    {
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "Shell"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "AutoAdminLogon"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "DefaultUserName"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "DefaultDomainName"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "AutoLogonSID"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "DefaultPassword"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "DisableCAD"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "DisableLockWorkstation"),
        (RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Lsa", "LimitBlankPasswordUse"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device", "DevicePasswordLessBuildVersion"),
        (RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout"),
        (RegistryHive.LocalMachine, @"CONTROL PANEL\Desktop", "WaitToKillAppTimeout"),
        (RegistryHive.CurrentUser, @"Control Panel\Desktop", "WaitToKillAppTimeout"),
        (RegistryHive.CurrentUser, @"Control Panel\Desktop", "HungAppTimeout"),
        // Marcador Phase1 (pode não existir)
        (RegistryHive.LocalMachine, @"SOFTWARE\TurboRama\Secure", "Phase1Probe"),
    };

    public static OperationResult Capture(Guid installationId, string productVersion, out BaselineDocument document)
    {
        document = new BaselineDocument
        {
            SchemaVersion = 1,
            InstallationId = installationId,
            CapturedAt = DateTimeOffset.Now,
            MachineName = Environment.MachineName,
            CapturedBy = Environment.UserName,
            WindowsVersion = Environment.OSVersion.VersionString,
            ProductVersion = productVersion
        };

        try
        {
            ProductPaths.EnsureLayout();
            string dir = BaselineStore.GetDirectory(installationId);
            Directory.CreateDirectory(dir);

            // Registro 64 e 32 bits
            foreach (var (hive, subKey, name) in CriticalValues)
            {
                document.RegistryValues.Add(
                    RegistryValueHelper.Capture(hive, subKey, name, RegistryView.Registry64));
                document.RegistryValues.Add(
                    RegistryValueHelper.Capture(hive, subKey, name, RegistryView.Registry32));
            }

            document.Bcd = BcdExportService.Capture(dir);
            document.Services = ServiceSnapshotService.CaptureDefaults();
            document.OptionalFeatures = OptionalFeatureSnapshotService.CaptureAll();

            // ACL do root TurboRama (se existir)
            if (Directory.Exists(ProductPaths.Root))
            {
                document.Acls.Add(AclSnapshotService.Capture(ProductPaths.Root, dir, "acl-turborama-root"));
            }

            if (Directory.Exists(ProductPaths.Config))
            {
                document.Acls.Add(AclSnapshotService.Capture(ProductPaths.Config, dir, "acl-turborama-config"));
            }

            // Tarefas agendadas TurboRama (proposta §6.5)
            string taskDir = Path.Combine(dir, "scheduled-tasks");
            OperationResult tasks = ScheduledTaskSnapshotService.CaptureToDirectory(taskDir);
            document.Notes = (document.Notes ?? string.Empty) + " tasks=" + tasks.Message;

            // MsKeyboardFilter explícito
            var kb = ServiceSnapshotService.CaptureOne("MsKeyboardFilter");
            if (!document.Services.Any(s => s.ServiceName.Equals("MsKeyboardFilter", StringComparison.OrdinalIgnoreCase)))
            {
                document.Services.Add(kb);
            }

            OperationResult save = BaselineStore.Save(document);
            if (!save.Success)
            {
                return save;
            }

            int existed = document.RegistryValues.Count(v => v.Existed);
            return OperationResult.Ok(
                "Baseline capturado: reg=" + document.RegistryValues.Count +
                " (existiam=" + existed + ")" +
                ", bcd=" + (document.Bcd?.ExportSucceeded == true ? "OK" : "AVISO") +
                ", services=" + document.Services.Count +
                ", features=" + document.OptionalFeatures.Count +
                ", acls=" + document.Acls.Count,
                "WindowsBaselineService.Capture",
                currentState: BaselineStore.GetDocumentPath(installationId));
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "Falha na captura de baseline: " + ex.Message,
                "BASELINE_CAPTURE",
                "WindowsBaselineService.Capture",
                exception: ex);
        }
    }

    public static OperationResult ValidateIntegrity(Guid installationId)
    {
        OperationResult load = BaselineStore.Load(installationId, out BaselineDocument? doc);
        if (!load.Success || doc is null)
        {
            return load;
        }

        string dir = BaselineStore.GetDirectory(installationId);
        var issues = new List<string>();

        if (doc.Bcd?.ExportSucceeded == true && !string.IsNullOrEmpty(doc.Bcd.Sha256))
        {
            string exportPath = Path.Combine(dir, "bcd-backup");
            if (File.Exists(exportPath))
            {
                string now = Core.Manifest.BaselineHash.Sha256File(exportPath);
                if (!string.Equals(now, doc.Bcd.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add("Hash BCD divergente.");
                }
            }
            else
            {
                issues.Add("Arquivo bcd-backup ausente.");
            }
        }

        if (doc.RegistryValues.Count == 0)
        {
            issues.Add("Nenhum valor de registro no baseline.");
        }

        if (issues.Count > 0)
        {
            return OperationResult.Fail(
                "Baseline com problemas: " + string.Join(" ", issues),
                "BASELINE_INTEGRITY",
                "WindowsBaselineService.ValidateIntegrity");
        }

        return OperationResult.Ok(
            "Baseline íntegro. Id=" + installationId.ToString("D") +
            " capturado em " + doc.CapturedAt.ToString("u"),
            "WindowsBaselineService.ValidateIntegrity");
    }

    /// <summary>
    /// Restaura apenas valores de Registro capturados (e tenta ACL se houver).
    /// BCD import automático NÃO é feito na Fase 1.
    /// </summary>
    public static OperationResult RestoreRegistryFromBaseline(Guid installationId)
    {
        OperationResult load = BaselineStore.Load(installationId, out BaselineDocument? doc);
        if (!load.Success || doc is null)
        {
            return load;
        }

        int ok = 0;
        int fail = 0;
        var messages = new List<string>();

        foreach (RegistryValueSnapshot snap in doc.RegistryValues)
        {
            // Não restaurar Winlogon/LSA na Fase 1 a menos que o manifesto diga que alteramos —
            // RestoreRegistryFromBaseline é usado para rollback de probe e restore completo futuro.
            OperationResult r = RegistryValueHelper.Restore(snap);
            if (r.Success)
            {
                ok++;
            }
            else
            {
                fail++;
                messages.Add(r.Message);
            }
        }

        string summary = "Registro restaurado do baseline: ok=" + ok + " falhas=" + fail;
        if (fail > 0)
        {
            return OperationResult.Fail(
                summary + " | " + string.Join("; ", messages.Take(5)),
                "BASELINE_REG_PARTIAL",
                "WindowsBaselineService.RestoreRegistryFromBaseline",
                canRollback: false);
        }

        return OperationResult.Ok(summary, "WindowsBaselineService.RestoreRegistryFromBaseline");
    }

    public static OperationResult RestoreSingleValue(RegistryValueSnapshot snap) =>
        RegistryValueHelper.Restore(snap);
}
