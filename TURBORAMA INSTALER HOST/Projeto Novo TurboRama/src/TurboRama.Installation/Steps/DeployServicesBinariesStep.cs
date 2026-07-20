using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Windows.Deploy;
using TurboRama.Windows.Exec;
using TurboRama.Windows.Services;

namespace TurboRama.Installation.Steps;

/// <summary>
/// Publica e copia Watchdog/Maintenance com TODAS as dependências (evita 1053).
/// Para serviços se estiverem RUNNING antes de sobrescrever DLLs (evita sharing violation).
/// </summary>
public sealed class DeployServicesBinariesStep : IInstallationStep
{
    public string Name => "DeployServicesBinaries";
    public int Order => 85;

    public Task<OperationResult> CaptureAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        context.Properties["WdExisted"] = File.Exists(Path.Combine(ProductPaths.AppWatchdog, "TurboRama.Watchdog.exe")) ? "1" : "0";
        context.Properties["MtExisted"] = File.Exists(Path.Combine(ProductPaths.AppMaintenance, "TurboRama.Maintenance.exe")) ? "1" : "0";
        return Task.FromResult(OperationResult.Ok("Captura de serviços binários.", Name));
    }

    public Task<OperationResult> ApplyAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        try
        {
            ProductPaths.EnsureLayout();

            // Obrigatório: parar serviços que travam as DLLs em C:\TurboRama\App\...
            WindowsServiceInstaller.Stop(WindowsServiceInstaller.WatchdogServiceName);
            WindowsServiceInstaller.Stop(WindowsServiceInstaller.MaintenanceServiceName);
            Thread.Sleep(2000);
            // mata processos residual
            KillProcess("TurboRama.Watchdog");
            KillProcess("TurboRama.Maintenance");
            Thread.Sleep(1000);

            // 1) tenta publish a partir do código-fonte
            string? solutionRoot = FindSolutionRoot();
            string pubWd = Path.Combine(Path.GetTempPath(), "tr-pub-watchdog");
            string pubMt = Path.Combine(Path.GetTempPath(), "tr-pub-maintenance");

            bool published = false;
            if (solutionRoot is not null)
            {
                string? dotnet = FindDotnet();
                if (dotnet is not null)
                {
                    string wdCsproj = Path.Combine(solutionRoot, "src", "TurboRama.Watchdog", "TurboRama.Watchdog.csproj");
                    string mtCsproj = Path.Combine(solutionRoot, "src", "TurboRama.Maintenance", "TurboRama.Maintenance.csproj");
                    if (File.Exists(wdCsproj) && File.Exists(mtCsproj))
                    {
                        OperationResult p1 = Publish(dotnet, wdCsproj, pubWd);
                        OperationResult p2 = Publish(dotnet, mtCsproj, pubMt);
                        published = p1.Success && p2.Success &&
                                    File.Exists(Path.Combine(pubWd, "TurboRama.Watchdog.exe")) &&
                                    File.Exists(Path.Combine(pubMt, "TurboRama.Maintenance.exe"));
                    }
                }
            }

            if (published)
            {
                // Deploy atômico (serviços já parados acima)
                OperationResult a1 = AtomicAppDeployer.DeployDirectory(pubWd, ProductPaths.AppWatchdog, "Watchdog", allowOverwriteRunning: true);
                OperationResult a2 = AtomicAppDeployer.DeployDirectory(pubMt, ProductPaths.AppMaintenance, "Maintenance", allowOverwriteRunning: true);
                if (!a1.Success || !a2.Success)
                {
                    CopyDirectory(pubWd, ProductPaths.AppWatchdog);
                    CopyDirectory(pubMt, ProductPaths.AppMaintenance);
                }
            }
            else
            {
                // 2) fallback: pastas de build
                string? wd = FindExe("TurboRama.Watchdog.exe");
                string? mt = FindExe("TurboRama.Maintenance.exe");
                if (wd is null || mt is null)
                {
                    // 3) se já existem no destino e só falhou publish, mantém os atuais
                    if (File.Exists(Path.Combine(ProductPaths.AppWatchdog, "TurboRama.Watchdog.exe")) &&
                        File.Exists(Path.Combine(ProductPaths.AppMaintenance, "TurboRama.Maintenance.exe")))
                    {
                        return Task.FromResult(OperationResult.Ok(
                            "Mantidos binários já instalados em C:\\TurboRama\\App (fonte de publish não achada).",
                            Name));
                    }

                    return Task.FromResult(OperationResult.Fail(
                        "Não achou binários. Rode REINSTALAR-SERVICOS.bat e tente de novo.",
                        "SVC_SRC",
                        Name));
                }

                string wdDir = Path.GetDirectoryName(wd)!;
                string mtDir = Path.GetDirectoryName(mt)!;
                OperationResult d1 = AtomicAppDeployer.DeployDirectory(wdDir, ProductPaths.AppWatchdog, "Watchdog", allowOverwriteRunning: true);
                OperationResult d2 = AtomicAppDeployer.DeployDirectory(mtDir, ProductPaths.AppMaintenance, "Maintenance", allowOverwriteRunning: true);
                if (!d1.Success || !d2.Success)
                {
                    CopyWithDeps(wd, ProductPaths.AppWatchdog);
                    CopyWithDeps(mt, ProductPaths.AppMaintenance);
                }
            }

            // valida deps críticas
            string miss = ValidateServiceFolder(ProductPaths.AppWatchdog, "TurboRama.Watchdog.exe")
                          + ValidateServiceFolder(ProductPaths.AppMaintenance, "TurboRama.Maintenance.exe");
            if (!string.IsNullOrEmpty(miss))
            {
                return Task.FromResult(OperationResult.Fail(
                    "Publicação incompleta: " + miss,
                    "SVC_DEPS",
                    Name));
            }

            return Task.FromResult(OperationResult.Ok(
                "Serviços implantados (watchdog+maintenance) com dependências.",
                Name));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail(ex.Message, "SVC_COPY", Name, exception: ex));
        }
    }

    public Task<OperationResult> ValidateAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        string wd = Path.Combine(ProductPaths.AppWatchdog, "TurboRama.Watchdog.exe");
        string mt = Path.Combine(ProductPaths.AppMaintenance, "TurboRama.Maintenance.exe");
        if (!File.Exists(wd) || !File.Exists(mt))
        {
            return Task.FromResult(OperationResult.Fail("Serviços não implantados.", "SVC_VAL", Name));
        }

        return Task.FromResult(OperationResult.Ok("Binários de serviço OK.", Name));
    }

    public Task<OperationResult> RollbackAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (!context.Properties.TryGetValue("WdExisted", out string? wdEx) || wdEx != "1")
            {
                TryDeleteDirContents(ProductPaths.AppWatchdog);
            }

            if (!context.Properties.TryGetValue("MtExisted", out string? mtEx) || mtEx != "1")
            {
                TryDeleteDirContents(ProductPaths.AppMaintenance);
            }

            return Task.FromResult(OperationResult.Ok("Rollback binários serviços.", Name));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail(ex.Message, "SVC_RB", Name, exception: ex));
        }
    }

    private static OperationResult Publish(string dotnet, string csproj, string outputDir)
    {
        try
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
            }

            Directory.CreateDirectory(outputDir);
            // framework-dependent win-x64 — usa runtime instalado no sistema
            return ProcessRunner.Run(
                dotnet,
                "publish \"" + csproj + "\" -c Release -r win-x64 --self-contained false -o \"" + outputDir + "\" /p:UseAppHost=true",
                timeoutMs: 300_000,
                operationName: "dotnet-publish");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message, "PUBLISH", exception: ex);
        }
    }

    private static string? FindDotnet()
    {
        string[] candidates =
        {
            @"D:\tr-dotnet\dotnet.exe",
            @"C:\Program Files\dotnet\dotnet.exe",
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindSolutionRoot()
    {
        string[] candidates =
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")),
            @"D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama",
        };
        foreach (string c in candidates)
        {
            if (File.Exists(Path.Combine(c, "TurboRama.sln")))
            {
                return c;
            }
        }

        return null;
    }

    private static string? FindExe(string name)
    {
        string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? pack = FactoryFullInstall.FindPackRoot();
        string component = name.Contains("Watchdog", StringComparison.OrdinalIgnoreCase) ? "Watchdog" : "Maintenance";

        var candidates = new List<string>
        {
            // Produção: pack e seed
            Path.Combine(baseDir, "App", component, name),
            Path.Combine(baseDir, "..", "App", component, name),
            Path.Combine(ProductPaths.App, component, name),
            Path.Combine(ProductPaths.AppWatchdog, name),
            Path.Combine(ProductPaths.AppMaintenance, name),
            Path.Combine(baseDir, name),
            // Dev
            @"D:\tr-phase3-fix\watchdog\" + name,
            @"D:\tr-phase3-fix\maintenance\" + name,
            Path.Combine(Path.GetTempPath(), "tr-pub-watchdog", name),
            Path.Combine(Path.GetTempPath(), "tr-pub-maintenance", name),
            @"D:\tr-factory-pack\TurboRama-Factory-Pack\App\" + component + "\\" + name,
        };
        if (!string.IsNullOrEmpty(pack))
        {
            candidates.Insert(0, Path.Combine(pack, "App", component, name));
        }

        foreach (string p in candidates)
        {
            try
            {
                string full = Path.GetFullPath(p);
                if (File.Exists(full))
                {
                    return full;
                }
            }
            catch
            {
                if (File.Exists(p))
                {
                    return p;
                }
            }
        }

        return null;
    }

    private static void CopyWithDeps(string sourceExe, string destDir)
    {
        Directory.CreateDirectory(destDir);
        string srcDir = Path.GetDirectoryName(sourceExe)!;
        foreach (string file in Directory.GetFiles(srcDir))
        {
            CopyFileRetry(file, Path.Combine(destDir, Path.GetFileName(file)));
        }

        foreach (string dir in Directory.GetDirectories(srcDir))
        {
            string name = Path.GetFileName(dir);
            CopyDirectory(dir, Path.Combine(destDir, name));
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string dest = Path.Combine(destDir, Path.GetFileName(file));
            CopyFileRetry(file, dest);
        }

        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }

    private static void CopyFileRetry(string source, string dest, int attempts = 8)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                File.Copy(source, dest, true);
                return;
            }
            catch (IOException) when (i < attempts - 1)
            {
                Thread.Sleep(400);
            }
        }

        File.Copy(source, dest, true);
    }

    private static void KillProcess(string processName)
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static string ValidateServiceFolder(string dir, string exeName)
    {
        if (!File.Exists(Path.Combine(dir, exeName)))
        {
            return exeName + " ausente; ";
        }

        if (!File.Exists(Path.Combine(dir, Path.ChangeExtension(exeName, ".runtimeconfig.json"))) &&
            !File.Exists(Path.Combine(dir, Path.GetFileNameWithoutExtension(exeName) + ".runtimeconfig.json")))
        {
            // runtimeconfig name matches assembly
            string rc = Path.Combine(dir, Path.GetFileNameWithoutExtension(exeName) + ".runtimeconfig.json");
            if (!File.Exists(rc))
            {
                return "runtimeconfig de " + exeName + " ausente; ";
            }
        }

        return string.Empty;
    }

    private static void TryDeleteDirContents(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
            {
                return;
            }

            foreach (string f in Directory.GetFiles(dir))
            {
                try { File.Delete(f); } catch { /* ignore */ }
            }
        }
        catch
        {
        }
    }
}
