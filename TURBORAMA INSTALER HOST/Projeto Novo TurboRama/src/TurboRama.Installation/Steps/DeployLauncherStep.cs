using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Windows.Deploy;

namespace TurboRama.Installation.Steps;

/// <summary>
/// Implanta Launcher em C:\TurboRama\App\Launcher com deploy atômico (staging/previous).
/// </summary>
public sealed class DeployLauncherStep : IInstallationStep
{
    public string Name => "DeployLauncher";
    public int Order => 35;

    public Task<OperationResult> CaptureAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        string dest = Path.Combine(ProductPaths.AppLauncher, "TurboRama.Launcher.exe");
        context.Properties["LauncherExisted"] = File.Exists(dest) ? "1" : "0";
        return Task.FromResult(OperationResult.Ok("Launcher dest exists=" + File.Exists(dest), Name));
    }

    public Task<OperationResult> ApplyAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        try
        {
            ProductPaths.EnsureLayout();
            string destDir = ProductPaths.AppLauncher;

            // Ordem: pack de fábrica (PC alvo) → seed já em C:\TurboRama → builds de dev
            string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? packRoot = FactoryFullInstall.FindPackRoot();
            var sources = new List<string>
            {
                // Pack ao lado do Setup (produção / outro PC)
                Path.Combine(baseDir, "App", "Launcher", "TurboRama.Launcher.exe"),
                Path.Combine(baseDir, "..", "App", "Launcher", "TurboRama.Launcher.exe"),
                // Já semeado por FactoryFullInstall (não falhar se só o dest existir)
                Path.Combine(ProductPaths.AppLauncher, "TurboRama.Launcher.exe"),
                Path.Combine(baseDir, "TurboRama.Launcher.exe"),
                Path.Combine(baseDir, "..", "TurboRama.Launcher", "TurboRama.Launcher.exe"),
                Path.Combine(
                    Path.GetDirectoryName(typeof(DeployLauncherStep).Assembly.Location) ?? "",
                    "TurboRama.Launcher.exe"),
            };
            if (!string.IsNullOrEmpty(packRoot))
            {
                sources.Insert(0, Path.Combine(packRoot, "App", "Launcher", "TurboRama.Launcher.exe"));
            }

            // Dev builds (opcional)
            sources.Add(Path.Combine(baseDir, "..", "..", "..", "..", "TurboRama.Launcher", "bin", "Release", "net8.0-windows", "win-x64", "TurboRama.Launcher.exe"));
            sources.Add(Path.Combine(baseDir, "..", "..", "..", "..", "TurboRama.Launcher", "bin", "Release", "net8.0-windows", "TurboRama.Launcher.exe"));
            sources.Add(@"D:\tr-factory-pack\TurboRama-Factory-Pack\App\Launcher\TurboRama.Launcher.exe");

            string? found = sources
                .Select(p =>
                {
                    try { return Path.GetFullPath(p); }
                    catch { return p; }
                })
                .FirstOrDefault(File.Exists);

            // Se o destino já tem o EXE do seed e nenhuma fonte de pack/dev, aceita o seed
            string destExisting = Path.Combine(destDir, "TurboRama.Launcher.exe");
            if (found is null && File.Exists(destExisting))
            {
                found = destExisting;
            }

            if (found is null)
            {
                return Task.FromResult(OperationResult.Fail(
                    "TurboRama.Launcher.exe não encontrado. Use a pasta TurboRama-Factory-Pack completa (App\\Launcher) ou rode INSTALAR-COMPLETO a partir do pack.",
                    "LAUNCHER_SRC",
                    Name));
            }

            string srcDir = Path.GetDirectoryName(found)!;
            // Deploy atômico: se launcher em uso, allowOverwriteRunning false falha — kiosk instalando de Admin ok
            OperationResult atom = AtomicAppDeployer.DeployDirectory(
                srcDir,
                destDir,
                "Launcher",
                allowOverwriteRunning: true);

            if (!atom.Success)
            {
                // fallback copy simples (seguro se Admin e launcher parado)
                Directory.CreateDirectory(destDir);
                File.Copy(found, Path.Combine(destDir, "TurboRama.Launcher.exe"), true);
                foreach (string dll in Directory.GetFiles(srcDir, "TurboRama.*.dll"))
                {
                    File.Copy(dll, Path.Combine(destDir, Path.GetFileName(dll)), true);
                }
            }

            Windows.Autologon.SysinternalsAutologonService.EnsureToolAvailable();

            string dest = Path.Combine(destDir, "TurboRama.Launcher.exe");
            context.Properties["LauncherPath"] = dest;
            return Task.FromResult(OperationResult.Ok(
                "Launcher implantado (atômico/fallback): " + dest + " | " + atom.Message,
                Name,
                currentState: dest));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail(ex.Message, "LAUNCHER_COPY", Name, exception: ex));
        }
    }

    public Task<OperationResult> ValidateAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        string dest = Path.Combine(ProductPaths.AppLauncher, "TurboRama.Launcher.exe");
        if (!File.Exists(dest))
        {
            return Task.FromResult(OperationResult.Fail("Launcher ausente em " + dest, "LAUNCHER_VAL", Name));
        }

        return Task.FromResult(OperationResult.Ok("Launcher presente.", Name));
    }

    public Task<OperationResult> RollbackAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        bool existed = context.Properties.TryGetValue("LauncherExisted", out string? e) && e == "1";
        if (existed)
        {
            return Task.FromResult(OperationResult.Ok("Launcher preexistente preservado.", Name));
        }

        try
        {
            string dest = Path.Combine(ProductPaths.AppLauncher, "TurboRama.Launcher.exe");
            if (File.Exists(dest))
            {
                File.Delete(dest);
            }

            return Task.FromResult(OperationResult.Ok("Launcher removido.", Name));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail(ex.Message, "LAUNCHER_RB", Name, exception: ex));
        }
    }
}
