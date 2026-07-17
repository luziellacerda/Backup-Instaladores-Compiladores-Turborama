using System.Diagnostics;
using Microsoft.Win32;
using TurboRama.Core.Baseline;
using TurboRama.Core.Manifest;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Windows.Registry;

namespace TurboRama.Installation.Steps;

/// <summary>
/// Prova de rollback real (Fase 1): grava/remove um valor sob HKLM\SOFTWARE\TurboRama\Secure.
/// Não toca Winlogon, LSA, BCD nem timeouts.
/// </summary>
public sealed class Phase1ProbeStep : IInstallationStep
{
    public const string SubKey = @"SOFTWARE\TurboRama\Secure";
    public const string ValueName = "Phase1Probe";
    public const string ProbeValue = "TurboRama-Phase1-OK";

    public string Name => "Phase1Probe";
    public int Order => 30;

    public Task<OperationResult> CaptureAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        string dir = Path.Combine(context.InstallationBackupRoot, "probe");
        Directory.CreateDirectory(dir);

        RegistryValueSnapshot snap64 = RegistryValueHelper.Capture(
            RegistryHive.LocalMachine, SubKey, ValueName, RegistryView.Registry64);
        RegistryValueSnapshot snap32 = RegistryValueHelper.Capture(
            RegistryHive.LocalMachine, SubKey, ValueName, RegistryView.Registry32);

        string path = Path.Combine(dir, "phase1-probe-snapshot.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new[] { snap64, snap32 }, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        }));

        context.Properties["ProbeSnapshot"] = path;

        return Task.FromResult(OperationResult.Ok(
            "Probe capturado. Existia64=" + snap64.Existed + " valor=" + (snap64.Value ?? "(null)"),
            Name,
            previousState: snap64.Existed ? snap64.Value : "(absent)"));
    }

    public Task<OperationResult> ApplyAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        OperationResult set = RegistryValueHelper.SetValue(
            RegistryHive.LocalMachine,
            SubKey,
            ValueName,
            ProbeValue,
            RegistryValueKind.String,
            RegistryView.Registry64);
        sw.Stop();

        if (!set.Success)
        {
            return Task.FromResult(OperationResult.Fail(set.Message, set.ErrorCode, Name, exception: set.Exception, duration: sw.Elapsed));
        }

        if (ChangeManifestStore.Load(context.InstallationId, out ChangeManifest? manifest).Success && manifest is not null)
        {
            ChangeManifestStore.AddChange(
                manifest,
                "RegistryValue",
                @"HKLM\" + SubKey + "\\" + ValueName,
                originalValue: null,
                newValue: ProbeValue,
                originalExisted: false,
                stepName: Name,
                status: "Applied");
            ChangeManifestStore.Save(manifest);
        }

        return Task.FromResult(OperationResult.Ok(
            "Probe aplicado: " + ProbeValue,
            Name,
            previousState: "(absent-or-other)",
            currentState: ProbeValue,
            duration: sw.Elapsed));
    }

    public Task<OperationResult> ValidateAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        RegistryValueSnapshot now = RegistryValueHelper.Capture(
            RegistryHive.LocalMachine, SubKey, ValueName, RegistryView.Registry64);

        if (!now.Existed || !string.Equals(now.Value, ProbeValue, StringComparison.Ordinal))
        {
            return Task.FromResult(OperationResult.Fail(
                "Probe não encontrado após Apply. Valor=" + (now.Value ?? "(null)"),
                "PROBE_VALIDATE",
                Name,
                currentState: now.Value));
        }

        return Task.FromResult(OperationResult.Ok("Probe validado no Registro.", Name, currentState: now.Value));
    }

    public Task<OperationResult> RollbackAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        string? snapPath = context.Properties.TryGetValue("ProbeSnapshot", out string? p) ? p : null;
        if (string.IsNullOrEmpty(snapPath) || !File.Exists(snapPath))
        {
            // Fallback: remover se não existia no baseline típico
            OperationResult del = RegistryValueHelper.DeleteValue(
                RegistryHive.LocalMachine, SubKey, ValueName, RegistryView.Registry64);
            return Task.FromResult(del.Success
                ? OperationResult.Ok("Probe removido (fallback).", Name)
                : OperationResult.Fail(del.Message, del.ErrorCode, Name));
        }

        try
        {
            RegistryValueSnapshot[]? snaps =
                System.Text.Json.JsonSerializer.Deserialize<RegistryValueSnapshot[]>(File.ReadAllText(snapPath));

            if (snaps is null || snaps.Length == 0)
            {
                return Task.FromResult(OperationResult.Fail("Snapshot probe vazio.", "PROBE_SNAP", Name));
            }

            // Restaura o capturado (se não existia, remove)
            OperationResult restore = RegistryValueHelper.Restore(snaps[0]);
            if (!restore.Success)
            {
                return Task.FromResult(OperationResult.Fail(restore.Message, restore.ErrorCode, Name));
            }

            if (ChangeManifestStore.Load(context.InstallationId, out ChangeManifest? manifest).Success && manifest is not null)
            {
                foreach (ChangeEntry c in manifest.Changes.Where(c => c.StepName == Name))
                {
                    c.RollbackStatus = "RolledBack";
                    c.Status = "RolledBack";
                }

                ChangeManifestStore.Save(manifest);
            }

            return Task.FromResult(OperationResult.Ok(
                "Probe restaurado ao estado capturado (existia=" + snaps[0].Existed + ").",
                Name,
                previousState: ProbeValue,
                currentState: snaps[0].Existed ? snaps[0].Value : "(deleted)"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail(
                "Rollback probe: " + ex.Message,
                "PROBE_RB",
                Name,
                exception: ex));
        }
    }
}
