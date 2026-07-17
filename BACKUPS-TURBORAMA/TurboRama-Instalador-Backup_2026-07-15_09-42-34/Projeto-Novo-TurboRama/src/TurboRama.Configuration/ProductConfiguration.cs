namespace TurboRama.Configuration;

/// <summary>
/// Configuração versionada do equipamento (estudo §3.2).
/// Senha kiosk padrão: <see cref="FactoryDefaults.KioskPassword"/> embutida no instalador.
/// </summary>
public sealed class ProductConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public Guid InstallationId { get; set; } = Guid.Empty;
    public string KioskUser { get; set; } = FactoryDefaults.KioskUserName;
    public string InstallDirectory { get; set; } = @"C:\TurboRama";
    public string FrontendExecutable { get; set; } = @"C:\TurboRama\Frontend\Frontend.exe";
    public string Profile { get; set; } = "KioskBasic";
    public bool EnableAutoLogon { get; set; } = true;
    public bool EnableKeyboardFilter { get; set; } = false;
    public bool EnableUwf { get; set; } = false;
    public bool EnableBootBranding { get; set; } = false;
    /// <summary>Hook de teclado no Launcher (Win/Alt+Tab). Default OFF (seguro).</summary>
    public bool EnableLauncherKeyboardHook { get; set; } = false;
    /// <summary>Permite Ctrl+Shift+M no Launcher para menu técnico (pipe). Default ON.</summary>
    public bool EnableLauncherTechMenu { get; set; } = true;

    /// <summary>
    /// Tela de loading TurboRama após logon do usuário Arcade (não mexe nas bolinhas de boot do Windows).
    /// Default ON.
    /// </summary>
    public bool ShowLoadingScreen { get; set; } = true;

    /// <summary>
    /// Caminho do som de boot. Vazio = Assets\boot-up.mp3 se existir, senão Assets\boot.wav.
    /// </summary>
    public string LoadingSoundFile { get; set; } = "";

    /// <summary>Tempo mínimo da tela de loading em ms. Default 4500 (visível + som).</summary>
    public int LoadingMinDisplayMs { get; set; } = 4500;

    /// <summary>
    /// Override opcional da senha kiosk. Se vazio/curta, usa FactoryDefaults.KioskPassword.
    /// Não é regravada em turborama.json pelo Save().
    /// </summary>
    public string? KioskPassword { get; set; }

    public WatchdogOptions Watchdog { get; set; } = new();
    public string ProductVersion { get; set; } = "2.0.0-alpha";
}

public sealed class WatchdogOptions
{
    public bool Enabled { get; set; } = true;
    public int RestartDelaySeconds { get; set; } = 5;
    public int MaximumRestarts { get; set; } = 5;
}
