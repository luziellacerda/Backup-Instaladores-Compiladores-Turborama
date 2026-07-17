using System.Diagnostics;
using TurboRama.Core.Baseline;
using TurboRama.Core.Manifest;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Windows.Baseline;

namespace TurboRama.Installation.Steps;

/// <summary>
/// Etapa Fase 1: captura baseline completo do Windows e grava manifesto inicial.
/// </summary>
public sealed class CaptureWindowsBaselineStep : IInstallationStep
{
    public string Name => "CaptureWindowsBaseline";
    public int Order => 20;

    public Task<OperationResult> CaptureAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        // O próprio Apply é a captura — Capture prévio marca início.
        return Task.FromResult(OperationResult.Ok(
            "Pronto para capturar baseline em " + context.InstallationBackupRoot,
            Name));
    }

    public Task<OperationResult> ApplyAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        OperationResult result = WindowsBaselineService.Capture(
            context.InstallationId,
            context.ProductVersion,
            out BaselineDocument document);
        sw.Stop();

        if (!result.Success)
        {
            return Task.FromResult(OperationResult.Fail(
                result.Message,
                result.ErrorCode,
                Name,
                exception: result.Exception,
                duration: sw.Elapsed));
        }

        var manifest = new ChangeManifest
        {
            SchemaVersion = 1,
            InstallationId = context.InstallationId,
            ProductVersion = context.ProductVersion,
            StartedAt = DateTimeOffset.Now,
            Profile = context.Profile.ToString(),
            MachineName = Environment.MachineName
        };

        ChangeManifestStore.AddChange(
            manifest,
            "BaselineDocument",
            BaselineStore.GetDocumentPath(context.InstallationId),
            originalValue: null,
            newValue: "captured",
            originalExisted: false,
            stepName: Name,
            status: "Applied");

        foreach (RegistryValueSnapshot reg in document.RegistryValues.Where(v => v.Existed).Take(50))
        {
            ChangeManifestStore.AddChange(
                manifest,
                "RegistryValue",
                reg.Path + "\\" + reg.Name + " [" + reg.RegistryView + "]",
                originalValue: reg.Value,
                newValue: reg.Value,
                originalExisted: true,
                stepName: Name,
                status: "Captured");
        }

        if (document.Bcd is not null)
        {
            ChangeManifestStore.AddChange(
                manifest,
                "BcdExport",
                document.Bcd.ExportRelativePath ?? "bcd-backup",
                originalValue: document.Bcd.Sha256,
                newValue: "export",
                originalExisted: document.Bcd.ExportSucceeded,
                stepName: Name,
                status: document.Bcd.ExportSucceeded ? "Captured" : "Warning");
        }

        ChangeManifestStore.Save(manifest);
        context.Properties["BaselinePath"] = BaselineStore.GetDocumentPath(context.InstallationId);

        return Task.FromResult(OperationResult.Ok(
            result.Message,
            Name,
            currentState: BaselineStore.GetDocumentPath(context.InstallationId),
            duration: sw.Elapsed));
    }

    public Task<OperationResult> ValidateAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        OperationResult integrity = WindowsBaselineService.ValidateIntegrity(context.InstallationId);
        if (!integrity.Success)
        {
            return Task.FromResult(OperationResult.Fail(
                integrity.Message,
                integrity.ErrorCode ?? "BASELINE_VALIDATE",
                Name));
        }

        return Task.FromResult(OperationResult.Ok(integrity.Message, Name));
    }

    public Task<OperationResult> RollbackAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        // Baseline em si não altera o Windows — rollback = manter arquivo (auditoria).
        return Task.FromResult(OperationResult.Ok(
            "Baseline preservado para auditoria (captura não altera o sistema).",
            Name));
    }
}
