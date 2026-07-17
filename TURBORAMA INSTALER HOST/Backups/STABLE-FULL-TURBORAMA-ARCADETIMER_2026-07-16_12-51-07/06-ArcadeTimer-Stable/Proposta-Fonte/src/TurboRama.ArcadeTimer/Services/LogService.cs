namespace TurboRama.ArcadeTimer;

public static class LogService
{
    private static readonly object Sync = new();
    private static bool _enabled = true;

    public static void Configure(bool enabled) => _enabled = enabled;

    public static void Write(string message, Exception? exception = null)
    {
        if (!_enabled)
            return;

        try
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(directory);

            string path = Path.Combine(
                directory,
                $"arcade-timer-{DateTime.Now:yyyy-MM-dd}.log");

            string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

            if (exception is not null)
                text += Environment.NewLine + exception;

            lock (Sync)
            {
                File.AppendAllText(
                    path,
                    text + Environment.NewLine + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    public static void CleanupOldLogs(int retentionDays)
    {
        try
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "logs");
            if (!Directory.Exists(directory))
                return;

            DateTime limit = DateTime.Now.AddDays(-Math.Max(1, retentionDays));

            foreach (string file in Directory.GetFiles(directory, "*.log"))
            {
                if (File.GetLastWriteTime(file) < limit)
                    File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Write("Falha ao limpar logs antigos", ex);
        }
    }
}
