using TurboRama.Core.Baseline;
using TurboRama.Core.Results;
using TurboRama.Windows.Bcd;
using TurboRama.Windows.Exec;

namespace TurboRama.Windows.Optional;

/// <summary>
/// Branding de boot opcional (estudo §17). Só cosmético; preserva recuperação.
/// Nunca oculta opções avançadas / WinRE.
/// </summary>
public static class BootBrandingModuleService
{
    public static OperationResult CaptureAndApplyQuietBoot(string baselineDirectory)
    {
        Directory.CreateDirectory(baselineDirectory);
        BcdSnapshot export = BcdExportService.Capture(baselineDirectory);
        if (!export.ExportSucceeded)
        {
            return OperationResult.Fail(
                "Não foi possível exportar BCD antes do branding: " + export.Message,
                "BCD_EXPORT",
                "BootBranding.Apply");
        }

        // Apenas elementos estéticos leves
        var results = new List<string>();
        results.Add(RunBcd("bootux disabled")); // quiet boot visual (se suportado)
        results.Add(RunBcd("-set {globalsettings} custom:16000067 true")); // quietboot legacy attempt
        // NÃO: desabilitar recovery, advanced options, etc.

        return OperationResult.Ok(
            "Branding leve aplicado (BCD exportado em " + baselineDirectory + "). " +
            "Detalhes: " + string.Join(" | ", results),
            "BootBranding.Apply",
            currentState: export.Sha256);
    }

    public static OperationResult Status()
    {
        OperationResult r = ProcessRunner.Run("bcdedit.exe", "/enum {current}", operationName: "bcd-enum");
        return r.Success
            ? OperationResult.Ok(r.Message, "BootBranding.Status", currentState: r.CurrentState)
            : OperationResult.Fail(r.Message, "BCD_STATUS", "BootBranding.Status");
    }

    private static string RunBcd(string args)
    {
        OperationResult r = ProcessRunner.Run("bcdedit.exe", args, operationName: "bcd-set");
        return args + " => " + (r.Success ? "OK" : "SKIP/FAIL");
    }
}
