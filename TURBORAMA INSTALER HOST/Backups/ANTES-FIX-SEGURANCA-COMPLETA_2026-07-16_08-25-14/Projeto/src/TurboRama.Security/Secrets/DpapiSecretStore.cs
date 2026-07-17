using System.Security.Cryptography;
using System.Text;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;

namespace TurboRama.Security.Secrets;

/// <summary>
/// Armazena segredos com DPAPI LocalMachine (não texto claro).
/// </summary>
public static class DpapiSecretStore
{
    public static string KioskPasswordPath => Path.Combine(ProductPaths.Config, "kiosk-user.secret");

    public static OperationResult Save(string name, string secret, string? path = null)
    {
        try
        {
            path ??= Path.Combine(ProductPaths.Config, name + ".secret");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] plain = Encoding.UTF8.GetBytes(secret);
            byte[] protectedBytes = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(path, protectedBytes);

            // ACL restrita: tenta icacls
            try
            {
                Windows.Exec.ProcessRunner.Run(
                    "icacls.exe",
                    "\"" + path + "\" /inheritance:r /grant:r *S-1-5-32-544:F /grant:r *S-1-5-18:F",
                    operationName: "acl-secret");
            }
            catch
            {
            }

            return OperationResult.Ok("Segredo protegido em " + path, "DpapiSecretStore.Save");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("DPAPI save: " + ex.Message, "DPAPI_SAVE", "DpapiSecretStore.Save", exception: ex);
        }
    }

    public static OperationResult Load(string name, out string? secret, string? path = null)
    {
        secret = null;
        try
        {
            path ??= Path.Combine(ProductPaths.Config, name + ".secret");
            if (!File.Exists(path))
            {
                return OperationResult.Fail("Segredo ausente: " + path, "DPAPI_MISSING", "DpapiSecretStore.Load");
            }

            byte[] protectedBytes = File.ReadAllBytes(path);
            byte[] plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
            secret = Encoding.UTF8.GetString(plain);
            return OperationResult.Ok("Segredo carregado.", "DpapiSecretStore.Load");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("DPAPI load: " + ex.Message, "DPAPI_LOAD", "DpapiSecretStore.Load", exception: ex);
        }
    }

    public static OperationResult SaveKioskPassword(string password) =>
        Save("kiosk-user", password, KioskPasswordPath);

    public static OperationResult LoadKioskPassword(out string? password) =>
        Load("kiosk-user", out password, KioskPasswordPath);

    public static void ClearKioskPassword()
    {
        try
        {
            if (File.Exists(KioskPasswordPath))
            {
                File.Delete(KioskPasswordPath);
            }
        }
        catch
        {
        }
    }
}
