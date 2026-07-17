using TurboRama.Core.Paths;
using TurboRama.Core.Results;

namespace TurboRama.Core.State;

/// <summary>
/// Arquivo protegido que suspende reinícios do watchdog (estudo §13).
/// C:\TurboRama\State\maintenance.lock
/// </summary>
public static class MaintenanceLock
{
    public static string LockPath => ProductPaths.MaintenanceLockFile;

    public static bool IsActive()
    {
        try
        {
            return File.Exists(LockPath);
        }
        catch
        {
            return false;
        }
    }

    public static OperationResult Enter(string reason, string? byUser = null)
    {
        try
        {
            Directory.CreateDirectory(ProductPaths.State);
            string content =
                "reason=" + (reason ?? "maintenance") + Environment.NewLine +
                "user=" + (byUser ?? Environment.UserName) + Environment.NewLine +
                "at=" + DateTimeOffset.Now.ToString("o") + Environment.NewLine;
            File.WriteAllText(LockPath, content);
            return OperationResult.Ok("maintenance.lock criado.", "MaintenanceLock.Enter");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message, "LOCK_ENTER", "MaintenanceLock.Enter", exception: ex);
        }
    }

    public static OperationResult Exit()
    {
        try
        {
            if (File.Exists(LockPath))
            {
                File.Delete(LockPath);
            }

            // Limpa recovery.flag junto (Sair manutenção / Fase 6) — Watchdog sai do TR-008 em memória
            try
            {
                string recovery = Path.Combine(ProductPaths.State, "recovery.flag");
                if (File.Exists(recovery))
                {
                    File.Delete(recovery);
                }
            }
            catch
            {
                /* ignore */
            }

            return OperationResult.Ok("maintenance.lock e recovery.flag removidos.", "MaintenanceLock.Exit");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message, "LOCK_EXIT", "MaintenanceLock.Exit", exception: ex);
        }
    }

    public static string? ReadReason()
    {
        try
        {
            if (!File.Exists(LockPath))
            {
                return null;
            }

            foreach (string line in File.ReadAllLines(LockPath))
            {
                if (line.StartsWith("reason=", StringComparison.OrdinalIgnoreCase))
                {
                    return line["reason=".Length..].Trim();
                }
            }

            return "maintenance";
        }
        catch
        {
            return null;
        }
    }
}
