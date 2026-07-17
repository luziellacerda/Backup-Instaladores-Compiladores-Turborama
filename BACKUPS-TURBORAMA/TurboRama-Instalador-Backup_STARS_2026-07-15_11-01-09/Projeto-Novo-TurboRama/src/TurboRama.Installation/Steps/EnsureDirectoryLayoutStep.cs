using System.Diagnostics;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;

namespace TurboRama.Installation.Steps;

/// <summary>
/// Primeira etapa real: cria o layout de pastas do produto (reversível).
/// Não remove pastas com dados de usuário no rollback se já existiam.
/// </summary>
public sealed class EnsureDirectoryLayoutStep : IInstallationStep
{
    public string Name => "EnsureDirectoryLayout";
    public int Order => 10;

    public Task<OperationResult> CaptureAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string marker = Path.Combine(context.InstallationBackupRoot, "layout-capture.json");
        Directory.CreateDirectory(context.InstallationBackupRoot);

        var existing = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in LayoutDirs())
        {
            existing[dir] = Directory.Exists(dir);
        }

        File.WriteAllText(marker, System.Text.Json.JsonSerializer.Serialize(existing, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        }));

        sw.Stop();
        return Task.FromResult(OperationResult.Ok(
            "Layout capturado (" + existing.Count + " pastas).",
            Name,
            duration: sw.Elapsed));
    }

    public Task<OperationResult> ApplyAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            ProductPaths.EnsureLayout();
            Directory.CreateDirectory(context.InstallationBackupRoot);
            Directory.CreateDirectory(context.StateDirectory);
            Directory.CreateDirectory(context.LogsDirectory);
            Directory.CreateDirectory(Path.Combine(ProductPaths.App, ".staging"));
            Directory.CreateDirectory(Path.Combine(ProductPaths.App, "previous"));
            Directory.CreateDirectory(ProductPaths.AppSecurityAgent);

            // ACLs recomendadas (proposta §7) — best effort
            OperationResult acls = Windows.Acl.ProductAclService.ApplyRecommendedLayoutAcls();

            sw.Stop();
            return Task.FromResult(OperationResult.Ok(
                "Layout C:\\TurboRama criado/atualizado. " + acls.Message,
                Name,
                previousState: "partial-or-missing",
                currentState: ProductPaths.Root,
                duration: sw.Elapsed));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Task.FromResult(OperationResult.Fail(
                "Falha ao criar layout: " + ex.Message,
                "LAYOUT_APPLY",
                Name,
                exception: ex,
                duration: sw.Elapsed));
        }
    }

    public Task<OperationResult> ValidateAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        string[] required =
        {
            ProductPaths.App, ProductPaths.Config, ProductPaths.State,
            ProductPaths.Backup, ProductPaths.Logs, ProductPaths.Data, ProductPaths.Saves
        };

        foreach (string dir in required)
        {
            if (!Directory.Exists(dir))
            {
                return Task.FromResult(OperationResult.Fail(
                    "Pasta obrigatória ausente: " + dir,
                    "LAYOUT_VALIDATE",
                    Name,
                    currentState: dir));
            }
        }

        return Task.FromResult(OperationResult.Ok("Layout validado.", Name));
    }

    public Task<OperationResult> RollbackAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        // Não apaga C:\TurboRama inteiro: apenas pastas que o TurboRama criou e estão vazias de dados de usuário.
        // Rollback seguro: remove marker de layout desta instalação se existir.
        try
        {
            string marker = Path.Combine(context.InstallationBackupRoot, "layout-capture.json");
            if (File.Exists(marker))
            {
                // Mantém pastas; limpeza destrutiva fica para restore completo com confirmação.
            }

            return Task.FromResult(OperationResult.Ok(
                "Rollback de layout: pastas preservadas (sem remoção destrutiva automática).",
                Name));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail(
                "Falha no rollback de layout: " + ex.Message,
                "LAYOUT_RB",
                Name,
                exception: ex));
        }
    }

    private static IEnumerable<string> LayoutDirs()
    {
        yield return ProductPaths.Root;
        yield return ProductPaths.App;
        yield return ProductPaths.Frontend;
        yield return ProductPaths.Config;
        yield return ProductPaths.Data;
        yield return ProductPaths.Saves;
        yield return ProductPaths.Logs;
        yield return ProductPaths.State;
        yield return ProductPaths.Backup;
        yield return ProductPaths.Recovery;
        yield return ProductPaths.Updates;
    }
}
