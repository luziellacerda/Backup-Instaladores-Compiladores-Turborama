using System.Text.Json;

namespace TurboRama.ArcadeTimer.Configuration;

public sealed class TimerConfig
{
    /// <summary>Processos que NUNCA podem ser encerrados (hard security).</summary>
    public static readonly string[] HardProtectedProcesses =
    [
        "emulationstation",
        "es-de",
        "explorer",
        "TurboRama.ArcadeTimer",
        "TurboRama.Launcher",
        "TurboRama.Watchdog",
        "TurboRama.Maintenance",
        "csrss",
        "winlogon",
        "services",
        "lsass",
        "smss",
        "System",
        "Idle"
    ];

    public int MinutesPerCoin { get; set; } = 5;
    public string CoinKey { get; set; } = "F10";
    public int CoinDebounceMilliseconds { get; set; } = 300;

    /// <summary>Teto de crédito acumulado (segundos). Default 8h. Enterprise: evita saldo absurdo.</summary>
    public long MaxRemainingSeconds { get; set; } = 28_800;

    public int WarningSeconds { get; set; } = 60;
    public int CriticalWarningSeconds { get; set; } = 10;

    public bool CountOnlyWhileEmulatorIsRunning { get; set; } = true;
    public bool BlockGameWithoutCredit { get; set; } = true;
    public bool CloseEmulatorWhenTimeEnds { get; set; } = true;

    public int EmulatorCheckIntervalMilliseconds { get; set; } = 1000;
    public int GracefulCloseTimeoutMilliseconds { get; set; } = 3000;
    public bool ForceCloseAfterTimeout { get; set; } = true;

    public bool SaveRemainingTime { get; set; } = true;
    public bool RestoreCreditAfterRestart { get; set; } = true;

    public WindowConfig Window { get; set; } = new();
    public SoundConfig Sound { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();

    public List<string> EmulatorProcesses { get; set; } =
    [
        "retroarch",
        "mame",
        "mame64",
        "pcsx2",
        "pcsx2-qt",
        "dolphin",
        "duckstation-qt",
        "duckstation-qt-x64-ReleaseLTCG",
        "ppssppwindows",
        "ppssppwindows64",
        "rpcs3",
        "cemu",
        "xenia",
        "xenia_canary",
        "flycast",
        "demul",
        "supermodel"
    ];

    public List<string> ProtectedProcesses { get; set; } =
    [
        "emulationstation",
        "es-de",
        "explorer",
        "TurboRama.ArcadeTimer",
        "TurboRama.Launcher",
        "TurboRama.Watchdog",
        "TurboRama.Maintenance"
    ];

    public static TimerConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                var created = new TimerConfig();
                created.Validate();
                Save(path, created);
                return created;
            }

            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<TimerConfig>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new TimerConfig();

            loaded.Validate();
            return loaded;
        }
        catch (Exception ex)
        {
            LogService.Write("Falha ao carregar config.json", ex);
            var fallback = new TimerConfig();
            fallback.Validate();
            return fallback;
        }
    }

    public static void Save(string path, TimerConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string json = JsonSerializer.Serialize(
            config,
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(path, json);
    }

    public void Validate()
    {
        MinutesPerCoin = Math.Clamp(MinutesPerCoin, 1, 60);
        CoinDebounceMilliseconds = Math.Clamp(CoinDebounceMilliseconds, 100, 5000);
        MaxRemainingSeconds = Math.Clamp(MaxRemainingSeconds, 60, 7 * 24 * 3600L);
        WarningSeconds = Math.Max(1, WarningSeconds);
        CriticalWarningSeconds = Math.Clamp(CriticalWarningSeconds, 1, WarningSeconds);
        EmulatorCheckIntervalMilliseconds = Math.Clamp(
            EmulatorCheckIntervalMilliseconds, 250, 5000);
        GracefulCloseTimeoutMilliseconds = Math.Clamp(
            GracefulCloseTimeoutMilliseconds, 500, 30000);

        if (string.IsNullOrWhiteSpace(CoinKey))
            CoinKey = "F10";

        EmulatorProcesses = Normalize(EmulatorProcesses);
        ProtectedProcesses = Normalize(ProtectedProcesses);

        // Hard security: protegidos obrigatórios sempre presentes.
        var hard = new HashSet<string>(HardProtectedProcesses, StringComparer.OrdinalIgnoreCase);
        foreach (string p in hard)
        {
            if (!ProtectedProcesses.Contains(p, StringComparer.OrdinalIgnoreCase))
                ProtectedProcesses.Add(p);
        }

        // Nunca permitir hard-protected na whitelist de kill.
        EmulatorProcesses = EmulatorProcesses
            .Where(e => !hard.Contains(e))
            .ToList();

        Window ??= new WindowConfig();
        Window.Opacity = Math.Clamp(Window.Opacity, 0.30, 1.0);
        Window.Width = Math.Clamp(Window.Width, 80, 800);
        Window.Height = Math.Clamp(Window.Height, 40, 400);
    }

    private static List<string> Normalize(IEnumerable<string>? values)
    {
        if (values is null)
            return [];

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v =>
            {
                string t = v.Trim();
                // Bloquear path traversal / paths absolutos → só nome do processo.
                t = t.Replace('/', '\\');
                if (t.Contains('\\'))
                    t = Path.GetFileName(t);
                return Path.GetFileNameWithoutExtension(t) ?? t;
            })
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Where(v => v.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class WindowConfig
{
    public bool Enabled { get; set; } = true;
    public int Width { get; set; } = 200;
    public int Height { get; set; } = 72;
    public int RightMargin { get; set; } = 12;
    public int TopMargin { get; set; } = 12;
    public bool TopMost { get; set; } = true;
    public double Opacity { get; set; } = 0.88;
    public bool AllowClose { get; set; } = false;
    public bool Compact { get; set; } = true;
}

public sealed class SoundConfig
{
    public bool Enabled { get; set; } = true;
    public string CoinAccepted { get; set; } = "sounds/coin.wav";
    public string Warning { get; set; } = "sounds/warning.wav";
    public string TimeEnded { get; set; } = "sounds/end.wav";
}

public sealed class LoggingConfig
{
    public bool Enabled { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public bool IncludeProcessEvents { get; set; } = true;
}
