using System.Text;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;

namespace TurboRama.Installation;

/// <summary>
/// Instalação de fábrica completa em um PC alvo: seed do pack → layout → (pipeline externo).
/// </summary>
public static class FactoryFullInstall
{
    /// <summary>
    /// Descobre a pasta do pack a partir do EXE (Installer\ ou raiz do pack).
    /// </summary>
    public static string? FindPackRoot(string? hint = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(hint))
        {
            candidates.Add(hint);
        }

        string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        candidates.Add(baseDir);
        candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..")));
        candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "..")));

        foreach (string c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (LooksLikePack(c))
            {
                return c;
            }

            // EXE em Installer\
            string parent = Path.GetFullPath(Path.Combine(c, ".."));
            if (LooksLikePack(parent))
            {
                return parent;
            }
        }

        return null;
    }

    public static bool LooksLikePack(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return false;
        }

        return File.Exists(Path.Combine(dir, "App", "Launcher", "TurboRama.Launcher.exe"))
               || File.Exists(Path.Combine(dir, "App", "Watchdog", "TurboRama.Watchdog.exe"))
               || (Directory.Exists(Path.Combine(dir, "Installer")) &&
                   Directory.Exists(Path.Combine(dir, "App")));
    }

    /// <summary>
    /// Copia App/Config/Frontend/Tools do pack para C:\TurboRama (sem apagar dados de usuário).
    /// </summary>
    public static OperationResult SeedPackToMachine(string packRoot, ITurboRamaLogger? logger = null)
    {
        try
        {
            if (!Directory.Exists(packRoot))
            {
                return OperationResult.Fail("Pack root inexistente: " + packRoot, "SEED_ROOT", "FactorySeed");
            }

            ProductPaths.EnsureLayout();
            var log = new StringBuilder();

            CopyTree(Path.Combine(packRoot, "App", "Launcher"), ProductPaths.AppLauncher, log);
            CopyTree(Path.Combine(packRoot, "App", "Watchdog"), ProductPaths.AppWatchdog, log);
            CopyTree(Path.Combine(packRoot, "App", "Maintenance"), ProductPaths.AppMaintenance, log);
            CopyTree(Path.Combine(packRoot, "App", "Tools"), Path.Combine(ProductPaths.App, "Tools"), log);

            string cfgSrc = Path.Combine(packRoot, "Config", "turborama.json");
            string cfgDst = ProductPaths.ConfigFile;
            if (File.Exists(cfgSrc) && !File.Exists(cfgDst))
            {
                File.Copy(cfgSrc, cfgDst, false);
                log.AppendLine("config template copiado");
            }

            string feSrc = Path.Combine(packRoot, "Frontend");
            if (Directory.Exists(feSrc))
            {
                foreach (string exe in Directory.GetFiles(feSrc, "*.exe"))
                {
                    string dest = Path.Combine(ProductPaths.Frontend, Path.GetFileName(exe));
                    File.Copy(exe, dest, true);
                    log.AppendLine("frontend " + Path.GetFileName(exe));
                }
            }

            // Descobre frontend real (pack, C:\TurboRama\Frontend, ou instalação legada D:\Turborama)
            TryBindFrontendPath(cfgDst, log);

            // Scripts ES: quit/shutdown/reboot → power-request.txt (Launcher trata splash + energia)
            InstallEmulationStationPowerScripts(packRoot, log);

            // Garante Autologon no destino
            string auto = Path.Combine(ProductPaths.App, "Tools", "Autologon64.exe");
            if (!File.Exists(auto))
            {
                logger?.Warning("FactorySeed", "Autologon64.exe ausente no pack Tools");
            }

            string msg = "Seed OK de " + packRoot + " → C:\\TurboRama. " + log.ToString().Replace(Environment.NewLine, "; ");
            logger?.Info("FactorySeed", msg);
            return OperationResult.Ok(msg, "FactorySeed", currentState: ProductPaths.Root);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Seed falhou: " + ex.Message, "SEED_EX", "FactorySeed", exception: ex);
        }
    }

    private static void CopyTree(string src, string dst, StringBuilder log)
    {
        if (!Directory.Exists(src))
        {
            log.AppendLine("skip missing " + src);
            return;
        }

        Directory.CreateDirectory(dst);
        foreach (string file in Directory.GetFiles(src))
        {
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
        }

        foreach (string dir in Directory.GetDirectories(src))
        {
            CopyTree(dir, Path.Combine(dst, Path.GetFileName(dir)), log);
        }

        log.AppendLine("copied " + Path.GetFileName(src));
    }

    /// <summary>
    /// Instala scripts EmulationStation (quit/shutdown/reboot) que gravam
    /// C:\TurboRama\State\power-request.txt para o Launcher.
    /// </summary>
    public static void InstallEmulationStationPowerScripts(string? packRoot, StringBuilder? log = null)
    {
        log ??= new StringBuilder();
        try
        {
            string? scriptsSrc = null;
            string[] srcCandidates =
            {
                packRoot == null ? "" : Path.Combine(packRoot, "Frontend", "emulationstation-scripts"),
                packRoot == null ? "" : Path.Combine(packRoot, "scripts", "emulationstation"),
                Path.Combine(AppContext.BaseDirectory, "emulationstation-scripts"),
                @"D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama\scripts\emulationstation",
            };
            foreach (string c in srcCandidates)
            {
                if (!string.IsNullOrEmpty(c) && Directory.Exists(c) &&
                    Directory.Exists(Path.Combine(c, "shutdown")))
                {
                    scriptsSrc = c;
                    break;
                }
            }

            if (scriptsSrc == null)
            {
                // Gera scripts embutidos mínimos se o pack não trouxer pasta
                scriptsSrc = Path.Combine(ProductPaths.AppLauncher, "es-scripts-generated");
                WriteEmbeddedEsScripts(scriptsSrc);
            }

            string[] destRoots =
            {
                Path.Combine(@"D:\Turborama", "emulationstation", ".emulationstation", "scripts"),
                Path.Combine(ProductPaths.Frontend, "emulationstation", ".emulationstation", "scripts"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".emulationstation", "scripts"),
            };

            foreach (string destRoot in destRoots)
            {
                string parent = Path.GetDirectoryName(destRoot) ?? "";
                if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                {
                    // Cria scripts mesmo se .emulationstation existir no caminho Turborama
                    if (!destRoot.StartsWith(@"D:\Turborama", StringComparison.OrdinalIgnoreCase) &&
                        !destRoot.StartsWith(ProductPaths.Frontend, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                foreach (string action in new[] { "shutdown", "reboot", "quit" })
                {
                    string from = Path.Combine(scriptsSrc, action);
                    string to = Path.Combine(destRoot, action);
                    if (!Directory.Exists(from))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(to);
                    foreach (string file in Directory.GetFiles(from, "*.bat"))
                    {
                        File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
                    }
                }

                log.AppendLine("es-scripts → " + destRoot);
            }
        }
        catch (Exception ex)
        {
            log.AppendLine("es-scripts fail: " + ex.Message);
        }
    }

    private static void WriteEmbeddedEsScripts(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "shutdown"));
        Directory.CreateDirectory(Path.Combine(root, "reboot"));
        Directory.CreateDirectory(Path.Combine(root, "quit"));

        const string header = "@echo off\r\nset \"TR_STATE=C:\\TurboRama\\State\"\r\nif not exist \"%TR_STATE%\" mkdir \"%TR_STATE%\" 2>nul\r\n";
        File.WriteAllText(Path.Combine(root, "shutdown", "01-turborama-power-request.bat"),
            header + "> \"%TR_STATE%\\power-request.txt\" echo shutdown\r\nshutdown /a >nul 2>&1\r\nexit /b 0\r\n");
        File.WriteAllText(Path.Combine(root, "reboot", "01-turborama-power-request.bat"),
            header + "> \"%TR_STATE%\\power-request.txt\" echo reboot\r\nshutdown /a >nul 2>&1\r\nexit /b 0\r\n");
        // quit = shutdown no kiosk (menu Desligar do ES usa pasta quit)
        File.WriteAllText(Path.Combine(root, "quit", "01-turborama-power-request.bat"),
            header + "> \"%TR_STATE%\\power-request.txt\" echo shutdown\r\n" +
            "shutdown /a >nul 2>&1\r\n" +
            "taskkill /IM emulationstation.exe /F >nul 2>&1\r\n" +
            "taskkill /IM TurboRama.exe /F >nul 2>&1\r\n" +
            "exit /b 0\r\n");
    }

    /// <summary>
    /// Grava frontendExecutable no turborama.json apontando para o EXE real encontrado no PC.
    /// TurboRama.exe (bootstrap) em D:\Turborama é o padrão legado de fliperama.
    /// </summary>
    private static void TryBindFrontendPath(string configPath, StringBuilder log)
    {
        try
        {
            string[] candidates =
            {
                Path.Combine(ProductPaths.Frontend, "Frontend.exe"),
                Path.Combine(ProductPaths.Frontend, "TurboRama.exe"),
                @"D:\Turborama\TurboRama.exe",
                @"D:\TURBOPCINSTALL\build\TurboRama.exe",
                @"D:\Turborama\emulationstation\emulationstation.exe",
            };

            string? found = candidates.FirstOrDefault(File.Exists);
            if (found is null || !File.Exists(configPath))
            {
                if (found is null)
                {
                    log.AppendLine("frontend path: nenhum EXE candidato (configure depois)");
                }

                return;
            }

            string json = File.ReadAllText(configPath);
            string escaped = found.Replace("\\", "\\\\");
            if (json.Contains("\"frontendExecutable\"", StringComparison.Ordinal))
            {
                json = System.Text.RegularExpressions.Regex.Replace(
                    json,
                    "\"frontendExecutable\"\\s*:\\s*\"[^\"]*\"",
                    "\"frontendExecutable\": \"" + escaped + "\"");
            }
            else
            {
                // Insere após installDirectory se possível
                json = json.Replace(
                    "\"installDirectory\"",
                    "\"frontendExecutable\": \"" + escaped + "\",\r\n  \"installDirectory\"");
            }

            File.WriteAllText(configPath, json);
            log.AppendLine("frontendExecutable=" + found);
        }
        catch (Exception ex)
        {
            log.AppendLine("frontend bind skip: " + ex.Message);
        }
    }
}
