using TurboRama.Core.Results;
using TurboRama.Windows.Exec;
using TurboRama.Windows.Features;
using TurboRama.Windows.Services;

namespace TurboRama.Windows.Optional;

/// <summary>
/// Keyboard Filter opcional (estudo §15). Default OFF. Requer edição Embedded + conta Admin.
/// </summary>
public static class KeyboardFilterModuleService
{
    public static OperationResult GetStatus()
    {
        var snap = ServiceSnapshotService.CaptureOne("MsKeyboardFilter");
        if (!snap.Exists)
        {
            return OperationResult.Ok(
                "MsKeyboardFilter não instalado/presente.",
                "KbFilter.Status",
                currentState: "NotPresent");
        }

        return OperationResult.Ok(
            "MsKeyboardFilter exists start=" + (snap.StartType ?? "?") + " state=" + (snap.State ?? "?"),
            "KbFilter.Status",
            currentState: snap.State);
    }

    public static OperationResult Enable()
    {
        // Tenta habilitar feature se existir
        var feature = OptionalFeatureSnapshotService.CaptureOne("Client-DeviceLockdown");
        if (!feature.Present)
        {
            return OperationResult.Fail(
                "Client-DeviceLockdown não existe nesta edição. Keyboard Filter não suportado.",
                "KB_FEATURE",
                "KbFilter.Enable");
        }

        ProcessRunner.Run(
            "dism.exe",
            "/Online /Enable-Feature /FeatureName:Client-DeviceLockdown /All /NoRestart",
            timeoutMs: 180_000,
            operationName: "dism-lockdown");

        // Serviço
        ProcessRunner.Run("sc.exe", "config MsKeyboardFilter start= auto", operationName: "kb-config");
        OperationResult start = ProcessRunner.Run("sc.exe", "start MsKeyboardFilter", operationName: "kb-start");
        if (!start.Success && !start.Message.Contains("1056"))
        {
            return OperationResult.Fail(
                "Não foi possível iniciar MsKeyboardFilter: " + start.Message,
                "KB_START",
                "KbFilter.Enable");
        }

        return OperationResult.Ok(
            "Keyboard Filter habilitado (se a edição suportar). Teste rollback antes de produção.",
            "KbFilter.Enable");
    }

    public static OperationResult Disable()
    {
        ProcessRunner.Run("sc.exe", "stop MsKeyboardFilter", operationName: "kb-stop");
        ProcessRunner.Run("sc.exe", "config MsKeyboardFilter start= disabled", operationName: "kb-disable");
        return OperationResult.Ok("Keyboard Filter desabilitado (serviço).", "KbFilter.Disable");
    }
}
