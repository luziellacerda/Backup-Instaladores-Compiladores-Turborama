using TurboRama.Core.Results;
using TurboRama.Windows.Exec;
using TurboRama.Windows.Features;
using TurboRama.Windows.Services;
using WinReg = Microsoft.Win32;

namespace TurboRama.Windows.Optional;

/// <summary>
/// Keyboard Filter (MsKeyboardFilter) — Windows 10 IoT Enterprise.
/// No IoT LTSC o serviço costuma já existir; só precisa de AUTO + start + políticas.
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
        // IoT LTSC 2021: o serviço já vem no SO. DISM Client-DeviceLockdown é reforço.
        var snap = ServiceSnapshotService.CaptureOne("MsKeyboardFilter");
        if (!snap.Exists)
        {
            var feature = OptionalFeatureSnapshotService.CaptureOne("Client-DeviceLockdown");
            if (!feature.Present)
            {
                return OperationResult.Fail(
                    "MsKeyboardFilter ausente e Client-DeviceLockdown não existe nesta edição.",
                    "KB_FEATURE",
                    "KbFilter.Enable");
            }

            // Microsoft IoT lab: ambas as features
            ProcessRunner.Run(
                "dism.exe",
                "/Online /Enable-Feature /FeatureName:Client-DeviceLockdown /FeatureName:Client-KeyboardFilter /All /NoRestart",
                timeoutMs: 180_000,
                operationName: "dism-keyboardfilter");

            snap = ServiceSnapshotService.CaptureOne("MsKeyboardFilter");
            if (!snap.Exists)
            {
                return OperationResult.Fail(
                    "Client-DeviceLockdown pedido mas MsKeyboardFilter ainda ausente (pode precisar reboot).",
                    "KB_FEATURE",
                    "KbFilter.Enable");
            }
        }
        else
        {
            // Reforço: DeviceLockdown + KeyboardFilter (oficial Microsoft IoT)
            try
            {
                ProcessRunner.Run(
                    "dism.exe",
                    "/Online /Enable-Feature /FeatureName:Client-DeviceLockdown /FeatureName:Client-KeyboardFilter /All /NoRestart",
                    timeoutMs: 120_000,
                    operationName: "dism-keyboardfilter");
            }
            catch
            {
                // ignore
            }
        }

        // Serviço AUTO + start (crítico no kiosk IoT)
        try
        {
            using WinReg.RegistryKey? svc = WinReg.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\MsKeyboardFilter", true);
            svc?.SetValue("Start", 2, WinReg.RegistryValueKind.DWord); // AUTO
        }
        catch
        {
            // sc config abaixo
        }

        // IMPORTANTE (IoT LTSC): NÃO fazer sc start antes do reboot.
        // Se o serviço arranca sem o filter driver no stack, o SCM reverte
        // AUTO → DEMAND (evento 7040) e Ctrl+Alt+Del continua a abrir.
        ProcessRunner.Run("sc.exe", "config MsKeyboardFilter start= auto", operationName: "kb-config");
        ProcessRunner.Run(
            "sc.exe",
            "failure MsKeyboardFilter reset= 86400 actions= restart/3000/restart/5000/restart/10000",
            operationName: "kb-failure");

        var after = ServiceSnapshotService.CaptureOne("MsKeyboardFilter");
        bool auto =
            after.Exists &&
            (string.Equals(after.StartType, "Auto", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(after.StartType, "Automatic", StringComparison.OrdinalIgnoreCase));

        // Se já estiver Running (pós-reboot), ok total
        bool running =
            after.Exists &&
            string.Equals(after.State, "Running", StringComparison.OrdinalIgnoreCase);

        if (running)
        {
            return OperationResult.Ok(
                "Keyboard Filter IoT Running (MsKeyboardFilter AUTO).",
                "KbFilter.Enable");
        }

        if (auto)
        {
            return OperationResult.Ok(
                "MsKeyboardFilter = Automatic (STOPPED até reboot). " +
                "Reinicie o PC para bloquear Ctrl+Alt+Del de verdade.",
                "KbFilter.Enable",
                currentState: after.State ?? "Stopped");
        }

        return OperationResult.Fail(
            "Não foi possível pôr MsKeyboardFilter em Automatic. state=" +
            (after.State ?? "?") + " start=" + (after.StartType ?? "?"),
            "KB_START",
            "KbFilter.Enable");
    }

    public static OperationResult Disable()
    {
        ProcessRunner.Run("sc.exe", "stop MsKeyboardFilter", operationName: "kb-stop");
        ProcessRunner.Run("sc.exe", "config MsKeyboardFilter start= disabled", operationName: "kb-disable");
        return OperationResult.Ok("Keyboard Filter desabilitado (serviço).", "KbFilter.Disable");
    }
}
