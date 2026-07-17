using System.Text.Json;
using TurboRama.Core.Baseline;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Security.Secrets;
using TurboRama.Windows.Autologon;

namespace TurboRama.Installation.Steps;

public sealed class ConfigureAutologonStep : IInstallationStep
{
    public string Name => "ConfigureAutologon";
    public int Order => 60;

    public Task<OperationResult> CaptureAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        List<RegistryValueSnapshot> snaps = SysinternalsAutologonService.CaptureWinlogon();
        string path = Path.Combine(context.InstallationBackupRoot, "autologon-winlogon.json");
        Directory.CreateDirectory(context.InstallationBackupRoot);
        File.WriteAllText(path, JsonSerializer.Serialize(snaps, new JsonSerializerOptions { WriteIndented = true }));
        context.Properties["AutologonSnapshot"] = path;
        return Task.FromResult(OperationResult.Ok("Winlogon capturado (" + snaps.Count + " valores).", Name));
    }

    public Task<OperationResult> ApplyAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        OperationResult load = DpapiSecretStore.LoadKioskPassword(out string? password);
        if (!load.Success || string.IsNullOrEmpty(password))
        {
            return Task.FromResult(OperationResult.Fail(
                "Senha kiosk DPAPI indisponível para autologon.",
                "AUTO_PWD",
                Name));
        }

        OperationResult en = SysinternalsAutologonService.Enable(context.KioskUserName, password, ".");
        // não manter senha em memória além do necessário
        password = null;
        return Task.FromResult(en);
    }

    public Task<OperationResult> ValidateAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", false);
        string? auto = key?.GetValue("AutoAdminLogon") as string;
        string? user = key?.GetValue("DefaultUserName") as string;
        if (auto != "1")
        {
            return Task.FromResult(OperationResult.Fail("AutoAdminLogon != 1", "AUTO_VAL", Name));
        }

        if (!string.Equals(user, context.KioskUserName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(OperationResult.Fail(
                "DefaultUserName=" + user + " esperado " + context.KioskUserName,
                "AUTO_VAL_USER",
                Name));
        }

        return Task.FromResult(OperationResult.Ok("Autologon validado para " + user, Name));
    }

    public Task<OperationResult> RollbackAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        if (context.Properties.TryGetValue("AutologonSnapshot", out string? path) && File.Exists(path))
        {
            List<RegistryValueSnapshot>? snaps =
                JsonSerializer.Deserialize<List<RegistryValueSnapshot>>(File.ReadAllText(path));
            if (snaps is not null)
            {
                return Task.FromResult(SysinternalsAutologonService.RestoreSnapshots(snaps));
            }
        }

        return Task.FromResult(SysinternalsAutologonService.Disable());
    }
}
