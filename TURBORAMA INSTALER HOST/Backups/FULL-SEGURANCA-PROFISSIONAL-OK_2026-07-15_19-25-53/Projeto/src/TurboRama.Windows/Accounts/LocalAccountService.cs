using System.DirectoryServices.AccountManagement;
using System.Security.Principal;
using TurboRama.Core.Results;
using TurboRama.Windows.Exec;

namespace TurboRama.Windows.Accounts;

public sealed class LocalAccountInfo
{
    public string UserName { get; init; } = string.Empty;
    public string? Sid { get; init; }
    public bool Exists { get; init; }
    public bool IsAdministrator { get; init; }
    public string? ProfilePath { get; init; }
}

public static class LocalAccountService
{
    public static LocalAccountInfo GetInfo(string userName)
    {
        var info = new LocalAccountInfo { UserName = userName, Exists = false };
        try
        {
            using var ctx = new PrincipalContext(ContextType.Machine);
            using var user = UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, userName);
            if (user is null)
            {
                return info;
            }

            info = new LocalAccountInfo
            {
                UserName = userName,
                Exists = true,
                Sid = user.Sid?.Value,
                IsAdministrator = IsInAdministrators(user),
                ProfilePath = ResolveProfilePath(user.Sid?.Value)
            };
        }
        catch
        {
            // fallback net user
            OperationResult q = ProcessRunner.Run("net.exe", "user \"" + userName + "\"", operationName: "net-user-query");
            if (q.Success)
            {
                info = new LocalAccountInfo { UserName = userName, Exists = true };
            }
        }

        return info;
    }

    public static OperationResult CreateStandardUser(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(userName) ||
            userName.Equals("Administrator", StringComparison.OrdinalIgnoreCase) ||
            userName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail("Nome de conta kiosk inválido: " + userName, "ACCT_NAME", "CreateStandardUser");
        }

        if (string.IsNullOrEmpty(password) || password.Length < 12)
        {
            return OperationResult.Fail("Senha kiosk deve ter no mínimo 12 caracteres.", "ACCT_PWD", "CreateStandardUser");
        }

        try
        {
            using var ctx = new PrincipalContext(ContextType.Machine);
            using var existing = UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, userName);
            if (existing is not null)
            {
                existing.SetPassword(password);
                existing.PasswordNeverExpires = true;
                existing.UserCannotChangePassword = true;
                existing.Enabled = true;
                existing.Save();
                RemoveFromAdministrators(ctx, existing);
                EnsureInUsersGroup(ctx, existing);

                if (IsInAdministrators(existing))
                {
                    return OperationResult.Fail("Conta ainda é Administrador.", "ACCT_ADMIN", "CreateStandardUser");
                }

                return OperationResult.Ok(
                    "Conta existente atualizada (senha definida, sem Admin): " + userName,
                    "CreateStandardUser",
                    previousState: "existed",
                    currentState: existing.Sid?.Value);
            }

            using var user = new UserPrincipal(ctx)
            {
                SamAccountName = userName,
                Name = userName,
                DisplayName = "TurboRama Kiosk",
                UserCannotChangePassword = true,
                PasswordNeverExpires = true,
                Enabled = true
            };
            user.SetPassword(password);
            user.Save();

            EnsureInUsersGroup(ctx, user);
            RemoveFromAdministrators(ctx, user);

            LocalAccountInfo after = GetInfo(userName);
            if (!after.Exists)
            {
                return OperationResult.Fail("Conta não encontrada após criação.", "ACCT_MISSING", "CreateStandardUser");
            }

            if (after.IsAdministrator)
            {
                return OperationResult.Fail("Conta ainda é Administrador.", "ACCT_ADMIN", "CreateStandardUser");
            }

            return OperationResult.Ok(
                "Conta kiosk criada: " + userName + " SID=" + (after.Sid ?? "?"),
                "CreateStandardUser",
                previousState: "(absent)",
                currentState: after.Sid);
        }
        catch (Exception ex)
        {
            // Fallback net.exe com timeout curto (último recurso)
            OperationResult create = ProcessRunner.Run(
                "net.exe",
                "user \"" + userName + "\" \"" + password + "\" /add",
                timeoutMs: 20_000,
                operationName: "net-user-add-fallback");
            if (!create.Success)
            {
                return OperationResult.Fail(
                    "Falha API e net.exe: " + ex.Message + " | " + create.Message,
                    "ACCT_CREATE",
                    "CreateStandardUser",
                    exception: ex);
            }

            ProcessRunner.Run("net.exe", "localgroup Users \"" + userName + "\" /add", timeoutMs: 10_000, operationName: "add-users-group");
            ProcessRunner.Run("net.exe", "localgroup Administrators \"" + userName + "\" /delete", timeoutMs: 10_000, operationName: "ensure-not-admin");
            return OperationResult.Ok("Conta criada via net fallback: " + userName, "CreateStandardUser");
        }
    }

    private static void EnsureInUsersGroup(PrincipalContext ctx, UserPrincipal user)
    {
        try
        {
            using GroupPrincipal? group = GroupPrincipal.FindByIdentity(ctx, "Users")
                ?? GroupPrincipal.FindByIdentity(ctx, "Usuários");
            if (group is null)
            {
                return;
            }

            if (!user.IsMemberOf(group))
            {
                group.Members.Add(user);
                group.Save();
            }
        }
        catch
        {
            /* ignore */
        }
    }

    private static void RemoveFromAdministrators(PrincipalContext ctx, UserPrincipal user)
    {
        try
        {
            using GroupPrincipal? group = GroupPrincipal.FindByIdentity(ctx, "Administrators")
                ?? GroupPrincipal.FindByIdentity(ctx, "Administradores");
            if (group is null)
            {
                return;
            }

            if (user.IsMemberOf(group))
            {
                group.Members.Remove(user);
                group.Save();
            }
        }
        catch
        {
            /* ignore */
        }
    }

    public static OperationResult DeleteUser(string userName)
    {
        LocalAccountInfo info = GetInfo(userName);
        if (!info.Exists)
        {
            return OperationResult.Ok("Conta já inexistente: " + userName, "DeleteUser");
        }

        return ProcessRunner.Run(
            "net.exe",
            "user \"" + userName + "\" /delete",
            operationName: "net-user-delete");
    }

    public static OperationResult EnsureProfileExists(string userName, string password)
    {
        // Logon controlado para criar perfil (sem UI interativa completa)
        // runas /user: não funciona bem sem senha interativa; usamos CreateProcessWithLogonW via PowerShell
        string script =
            "$p = ConvertTo-SecureString '" + password.Replace("'", "''") + "' -AsPlainText -Force; " +
            "$c = New-Object System.Management.Automation.PSCredential('" + Environment.MachineName + "\\" + userName + "',$p); " +
            "Start-Process cmd.exe -Credential $c -ArgumentList '/c exit' -Wait -WindowStyle Hidden -ErrorAction SilentlyContinue";

        // Evita falha se política bloqueia — perfil pode ser criado no 1º logon
        OperationResult r = ProcessRunner.Run(
            "powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"",
            timeoutMs: 60_000,
            operationName: "create-profile");

        LocalAccountInfo info = GetInfo(userName);
        if (!string.IsNullOrWhiteSpace(info.ProfilePath) && Directory.Exists(info.ProfilePath))
        {
            return OperationResult.Ok("Perfil: " + info.ProfilePath, "EnsureProfileExists");
        }

        // Alternativa: dir padrão
        string defaultProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "..", userName);
        defaultProfile = Path.GetFullPath(defaultProfile);
        if (Directory.Exists(Path.Combine(@"C:\Users", userName)))
        {
            return OperationResult.Ok("Perfil em C:\\Users\\" + userName, "EnsureProfileExists");
        }

        return OperationResult.Ok(
            "Perfil será criado no primeiro logon (bootstrap opcional: " + r.Message + ")",
            "EnsureProfileExists");
    }

    private static bool IsInAdministrators(UserPrincipal user)
    {
        try
        {
            using var ctx = new PrincipalContext(ContextType.Machine);
            using var group = GroupPrincipal.FindByIdentity(ctx, "Administrators")
                ?? GroupPrincipal.FindByIdentity(ctx, "Administradores");
            if (group is null)
            {
                return false;
            }

            return user.IsMemberOf(group);
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveProfilePath(string? sid)
    {
        if (string.IsNullOrEmpty(sid))
        {
            return null;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid);
            return key?.GetValue("ProfileImagePath") as string;
        }
        catch
        {
            return null;
        }
    }

    public static bool HasOtherAdministrator(string kioskUserName)
    {
        try
        {
            using var ctx = new PrincipalContext(ContextType.Machine);
            using var group = GroupPrincipal.FindByIdentity(ctx, "Administrators")
                ?? GroupPrincipal.FindByIdentity(ctx, "Administradores");
            if (group is null)
            {
                return true; // não bloquear
            }

            foreach (Principal p in group.GetMembers(true))
            {
                if (p is UserPrincipal u &&
                    !string.Equals(u.SamAccountName, kioskUserName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(u.SamAccountName, "Guest", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            return true;
        }

        return false;
    }
}
