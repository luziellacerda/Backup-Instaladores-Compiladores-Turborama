using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Windows.Exec;

namespace TurboRama.Windows.Acl;

/// <summary>
/// ACLs recomendadas (proposta §7): Backup/Recovery só Admin+SYSTEM;
/// App/Frontend leitura+exec; Data/Saves/Logs graváveis.
/// </summary>
public static class ProductAclService
{
    public static OperationResult ApplyRecommendedLayoutAcls()
    {
        ProductPaths.EnsureLayout();
        var messages = new List<string>();

        // Backup + Recovery: Administrators (S-1-5-32-544) + SYSTEM (S-1-5-18) full, sem herança
        foreach (string dir in new[] { ProductPaths.Backup, ProductPaths.Recovery })
        {
            OperationResult r = ProcessRunner.Run(
                "icacls.exe",
                "\"" + dir + "\" /inheritance:r /grant:r *S-1-5-32-544:(OI)(CI)F /grant:r *S-1-5-18:(OI)(CI)F",
                timeoutMs: 30_000,
                operationName: "acl-backup-recovery");
            messages.Add(Path.GetFileName(dir) + "=" + (r.Success ? "Admin+SYSTEM" : "aviso"));
        }

        // App + Frontend: Authenticated Users RX + Admin F
        foreach (string dir in new[] { ProductPaths.App, ProductPaths.Frontend })
        {
            OperationResult r = ProcessRunner.Run(
                "icacls.exe",
                "\"" + dir + "\" /grant:r *S-1-5-11:(OI)(CI)RX /grant:r *S-1-5-32-544:(OI)(CI)F /grant:r *S-1-5-18:(OI)(CI)F",
                timeoutMs: 30_000,
                operationName: "acl-app");
            messages.Add(Path.GetFileName(dir) + "=" + (r.Success ? "RX+Admin" : "aviso"));
        }

        // Data, Saves, Logs: Users modify
        foreach (string dir in new[] { ProductPaths.Data, ProductPaths.Saves, ProductPaths.Logs })
        {
            OperationResult r = ProcessRunner.Run(
                "icacls.exe",
                "\"" + dir + "\" /grant:r *S-1-5-32-545:(OI)(CI)M /grant:r *S-1-5-32-544:(OI)(CI)F /grant:r *S-1-5-18:(OI)(CI)F",
                timeoutMs: 30_000,
                operationName: "acl-data");
            messages.Add(Path.GetFileName(dir) + "=" + (r.Success ? "Users-M" : "aviso"));
        }

        // Config: Users R, Admin F (senha DPAPI já tem ACL própria)
        ProcessRunner.Run(
            "icacls.exe",
            "\"" + ProductPaths.Config + "\" /grant:r *S-1-5-32-545:(OI)(CI)R /grant:r *S-1-5-32-544:(OI)(CI)F /grant:r *S-1-5-18:(OI)(CI)F",
            timeoutMs: 20_000,
            operationName: "acl-config");

        return OperationResult.Ok(
            "ACLs layout: " + string.Join("; ", messages),
            "ProductAclService");
    }
}
