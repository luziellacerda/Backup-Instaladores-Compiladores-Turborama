using System.Text.Json;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Windows.Optional;
using TurboRama.Windows.Security;

namespace TurboRama.Installation.Steps;

/// <summary>
/// Fase 4: módulos opcionais. Só aplica o que estiver em context.Properties:
/// EnableUwf=1, EnableKeyboardFilter=1, EnableBootBranding=1
/// Default: nada (estudo: default OFF + aviso de risco).
/// </summary>
public sealed class OptionalAdvancedModulesStep : IInstallationStep
{
    public string Name => "OptionalAdvancedModules";
    public int Order => 100;

    public Task<OperationResult> CaptureAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        var status = new
        {
            Uwf = UwfModuleService.GetStatus().Message,
            KeyboardFilter = KeyboardFilterModuleService.GetStatus().Message,
            Boot = BootBrandingModuleService.Status().Success
        };

        string path = Path.Combine(context.InstallationBackupRoot, "optional-modules-before.json");
        Directory.CreateDirectory(context.InstallationBackupRoot);
        File.WriteAllText(path, JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
        context.Properties["OptionalModulesCapture"] = path;
        return Task.FromResult(OperationResult.Ok("Estado opcional capturado.", Name));
    }

    public Task<OperationResult> ApplyAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        bool uwf = IsOn(context, "EnableUwf");
        bool kb = IsOn(context, "EnableKeyboardFilter");
        bool boot = IsOn(context, "EnableBootBranding");

        if (!uwf && !kb && !boot)
        {
            return Task.FromResult(OperationResult.Ok(
                "Nenhum módulo avançado selecionado (default seguro).",
                Name));
        }

        var messages = new List<string>();
        var applied = new List<string>();

        if (uwf)
        {
            OperationResult r = UwfModuleService.EnableWithExclusions();
            messages.Add("UWF: " + r.Message);
            if (!r.Success)
            {
                return Task.FromResult(OperationResult.Fail(string.Join(" | ", messages), "OPT_UWF", Name));
            }

            applied.Add("Uwf");
            context.Properties["AppliedUwf"] = "1";
        }

        if (kb)
        {
            // Enable serviço + CadBlock completo (reg, WEKF, tarefas boot — validado 2026-07-15)
            OperationResult r = KeyboardFilterModuleService.Enable();
            messages.Add("KeyboardFilter: " + r.Message);
            OperationResult cad = CadBlockService.ApplySystemWide();
            messages.Add("CadBlock: " + cad.Message);
            if (!r.Success && !cad.Success)
            {
                return Task.FromResult(OperationResult.Fail(string.Join(" | ", messages), "OPT_KB", Name));
            }

            applied.Add("KeyboardFilter");
            context.Properties["AppliedKeyboardFilter"] = "1";
        }

        if (boot)
        {
            string bcdDir = Path.Combine(context.InstallationBackupRoot, "bcd-branding");
            OperationResult r = BootBrandingModuleService.CaptureAndApplyQuietBoot(bcdDir);
            messages.Add("BootBranding: " + r.Message);
            if (!r.Success)
            {
                return Task.FromResult(OperationResult.Fail(string.Join(" | ", messages), "OPT_BOOT", Name));
            }

            applied.Add("BootBranding");
            context.Properties["AppliedBootBranding"] = "1";
        }

        context.Properties["OptionalApplied"] = string.Join(",", applied);
        return Task.FromResult(OperationResult.Ok(
            "Módulos aplicados: " + string.Join(", ", applied) + " | " + string.Join(" | ", messages),
            Name));
    }

    public Task<OperationResult> ValidateAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        if (!context.Properties.TryGetValue("OptionalApplied", out string? applied) ||
            string.IsNullOrWhiteSpace(applied))
        {
            return Task.FromResult(OperationResult.Ok("Sem módulos opcionais — validação OK.", Name));
        }

        return Task.FromResult(OperationResult.Ok("Opcionais ativos: " + applied, Name));
    }

    public Task<OperationResult> RollbackAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        var messages = new List<string>();

        if (context.Properties.TryGetValue("AppliedUwf", out string? u) && u == "1")
        {
            messages.Add(UwfModuleService.Disable().Message);
        }

        if (context.Properties.TryGetValue("AppliedKeyboardFilter", out string? k) && k == "1")
        {
            messages.Add(KeyboardFilterModuleService.Disable().Message);
        }

        if (context.Properties.TryGetValue("AppliedBootBranding", out string? b) && b == "1")
        {
            // Branding: BCD import automático não é feito (segurança). Avisar.
            messages.Add("Boot branding: restaure BCD manualmente do backup em baseline se necessário (import não automático).");
        }

        if (messages.Count == 0)
        {
            return Task.FromResult(OperationResult.Ok("Nada a reverter nos opcionais.", Name));
        }

        return Task.FromResult(OperationResult.Ok(string.Join(" | ", messages), Name));
    }

    private static bool IsOn(InstallationContext context, string key) =>
        context.Properties.TryGetValue(key, out string? v) &&
        (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
}
