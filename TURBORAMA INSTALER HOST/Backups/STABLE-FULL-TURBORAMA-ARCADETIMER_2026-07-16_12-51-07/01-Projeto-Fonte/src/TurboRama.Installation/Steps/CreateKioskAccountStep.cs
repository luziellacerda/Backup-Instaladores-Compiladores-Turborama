using System.Text.Json;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Security.Secrets;
using TurboRama.Windows.Accounts;

namespace TurboRama.Installation.Steps;

public sealed class CreateKioskAccountStep : IInstallationStep
{
    public string Name => "CreateKioskAccount";
    public int Order => 40;

    public Task<OperationResult> CaptureAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        LocalAccountInfo info = LocalAccountService.GetInfo(context.KioskUserName);
        string path = Path.Combine(context.InstallationBackupRoot, "kiosk-account.json");
        Directory.CreateDirectory(context.InstallationBackupRoot);
        File.WriteAllText(path, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        context.Properties["KioskAccountCapture"] = path;
        context.Properties["KioskAccountExisted"] = info.Exists ? "1" : "0";
        return Task.FromResult(OperationResult.Ok(
            "Conta capturada: exists=" + info.Exists + " admin=" + info.IsAdministrator,
            Name,
            previousState: info.Exists ? info.Sid : "(absent)"));
    }

    public Task<OperationResult> ApplyAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        if (!LocalAccountService.HasOtherAdministrator(context.KioskUserName))
        {
            return Task.FromResult(OperationResult.Fail(
                "É obrigatória outra conta Administrador além da kiosk.",
                "ACCT_NO_ADMIN",
                Name));
        }

        // Senha: embutida no instalador (FactoryDefaults) ou override no contexto — NÃO aleatória sem controle.
        string password = context.KioskPassword?.Trim() ?? string.Empty;
        string source = "context";
        if (password.Length < 8)
        {
            // Fallback seguro se contexto vazio (não deveria acontecer se UI/CLI passar FactoryDefaults)
            password = PasswordGenerator.Generate(20);
            source = "generated";
        }

        OperationResult create = LocalAccountService.CreateStandardUser(context.KioskUserName, password);
        if (!create.Success)
        {
            return Task.FromResult(create);
        }

        OperationResult secret = DpapiSecretStore.SaveKioskPassword(password);
        if (!secret.Success)
        {
            return Task.FromResult(secret);
        }

        context.Properties["KioskPasswordSource"] = source;

        // Perfil: tenta CreateProfile; se falhar, segue (1º logon cria).
        // NÃO bloquear a Fase 2 em bootstrap de perfil.
        OperationResult profile = ProfileHelper.CreateWindowsProfile(context.KioskUserName);
        if (!profile.Success)
        {
            try
            {
                OperationResult boot = LocalAccountService.EnsureProfileExists(context.KioskUserName, password);
                profile = ProfileHelper.CreateWindowsProfile(context.KioskUserName);
                if (!profile.Success)
                {
                    profile = OperationResult.Ok(
                        "Perfil adiado para 1º logon (" + boot.Message + " / " + profile.Message + ")",
                        Name);
                }
            }
            catch (Exception ex)
            {
                profile = OperationResult.Ok("Perfil adiado (ex: " + ex.Message + ")", Name);
            }
        }

        context.Properties["KioskPasswordStored"] = "1";
        return Task.FromResult(OperationResult.Ok(
            create.Message + " | DPAPI OK | senha=" + source + " | perfil: " + profile.Message,
            Name,
            currentState: context.KioskUserName));
    }

    public Task<OperationResult> ValidateAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        LocalAccountInfo info = LocalAccountService.GetInfo(context.KioskUserName);
        if (!info.Exists)
        {
            return Task.FromResult(OperationResult.Fail("Conta kiosk não existe.", "ACCT_VAL", Name));
        }

        if (info.IsAdministrator)
        {
            return Task.FromResult(OperationResult.Fail("Conta kiosk ainda é Admin.", "ACCT_VAL_ADMIN", Name));
        }

        if (!File.Exists(DpapiSecretStore.KioskPasswordPath))
        {
            return Task.FromResult(OperationResult.Fail("Segredo DPAPI ausente.", "ACCT_VAL_SECRET", Name));
        }

        return Task.FromResult(OperationResult.Ok(
            "Conta kiosk OK: " + context.KioskUserName + " SID=" + (info.Sid ?? "?"),
            Name));
    }

    public Task<OperationResult> RollbackAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        bool existed = context.Properties.TryGetValue("KioskAccountExisted", out string? e) && e == "1";
        DpapiSecretStore.ClearKioskPassword();
        if (!existed)
        {
            OperationResult del = LocalAccountService.DeleteUser(context.KioskUserName);
            return Task.FromResult(del.Success
                ? OperationResult.Ok("Conta kiosk removida (não existia antes).", Name)
                : del);
        }

        return Task.FromResult(OperationResult.Ok(
            "Conta preexistente preservada; senha DPAPI limpa.",
            Name));
    }
}
