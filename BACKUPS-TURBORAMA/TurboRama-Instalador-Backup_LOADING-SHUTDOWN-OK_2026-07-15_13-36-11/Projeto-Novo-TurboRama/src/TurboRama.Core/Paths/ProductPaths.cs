namespace TurboRama.Core.Paths;

/// <summary>
/// Layout canônico em C:\TurboRama (estudo §7).
/// Executáveis separados de dados graváveis.
/// </summary>
public static class ProductPaths
{
    public const string Root = @"C:\TurboRama";

    public static string App => Path.Combine(Root, "App");
    public static string AppLauncher => Path.Combine(App, "Launcher");
    /// <summary>Assets do Launcher (som, logos, tutoriais).</summary>
    public static string AppLauncherAssets => Path.Combine(AppLauncher, "Assets");

    // ——— BOOT / LOADING (ficheiro e assets próprios) ———
    /// <summary>Som da tela de loading inicial.</summary>
    public static string DefaultBootSoundWav => Path.Combine(AppLauncherAssets, "boot.wav");
    /// <summary>Som opcional MP3 da loading.</summary>
    public static string DefaultBootSoundMp3 => Path.Combine(AppLauncherAssets, "boot-up.mp3");
    /// <summary>Logo da tela de LOADING (boot). Não usar no desligar.</summary>
    public static string DefaultBootLogoPng => Path.Combine(AppLauncherAssets, "logo.png");

    // ——— DESLIGAR / SHUTDOWN (ficheiro e assets SEPARADOS da loading) ———
    /// <summary>Logo da tela de DESLIGAR (ficheiro separado de logo.png).</summary>
    public static string DefaultShutdownLogoPng => Path.Combine(AppLauncherAssets, "logo-shutdown.png");
    /// <summary>Som opcional da tela de desligar (ficheiro separado de boot.wav).</summary>
    public static string DefaultShutdownSoundWav => Path.Combine(AppLauncherAssets, "shutdown.wav");
    public static string AppWatchdog => Path.Combine(App, "Watchdog");
    public static string AppMaintenance => Path.Combine(App, "Maintenance");
    public static string AppSecurityAgent => Path.Combine(App, "SecurityAgent");
    public static string Frontend => Path.Combine(Root, "Frontend");
    public static string Config => Path.Combine(Root, "Config");
    public static string Data => Path.Combine(Root, "Data");
    public static string Saves => Path.Combine(Root, "Saves");
    public static string Logs => Path.Combine(Root, "Logs");
    public static string State => Path.Combine(Root, "State");
    public static string Backup => Path.Combine(Root, "Backup");
    public static string Recovery => Path.Combine(Root, "Recovery");
    public static string Updates => Path.Combine(Root, "Updates");

    public static string InstallerLogs => Path.Combine(Logs, "Installer");
    public static string LauncherLogs => Path.Combine(Logs, "Launcher");
    public static string WatchdogLogs => Path.Combine(Logs, "Watchdog");
    public static string MaintenanceLogs => Path.Combine(Logs, "Maintenance");
    public static string RollbackLogs => Path.Combine(Logs, "Rollback");
    public static string SecurityLogs => Path.Combine(Logs, "Security");

    public static string InstallationStateFile => Path.Combine(State, "installation-state.json");
    public static string ConfigFile => Path.Combine(Config, "turborama.json");
    public static string MaintenanceLockFile => Path.Combine(State, "maintenance.lock");
    public static string ChangeManifestFile(Guid installationId) =>
        Path.Combine(Backup, installationId.ToString("D"), "change-manifest.json");

    public static void EnsureLayout()
    {
        string[] dirs =
        {
            App, AppLauncher, AppLauncherAssets, AppWatchdog, AppMaintenance, AppSecurityAgent,
            Frontend, Config, Data, Saves, Logs, State, Backup, Recovery, Updates,
            InstallerLogs, LauncherLogs, WatchdogLogs, MaintenanceLogs, RollbackLogs, SecurityLogs
        };

        foreach (string dir in dirs)
        {
            Directory.CreateDirectory(dir);
        }
    }
}
