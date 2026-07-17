using System.Text;

namespace TurboRama.Core.Logging;

/// <summary>
/// Logger em arquivo com rotação simples.
/// Nunca registrar senha, PIN, token ou código de recuperação completo.
/// </summary>
public sealed class FileTurboRamaLogger : ITurboRamaLogger
{
    private readonly string _directory;
    private readonly string _componentFilePrefix;
    private readonly object _sync = new();
    private readonly long _maxBytes;

    public FileTurboRamaLogger(string directory, string componentFilePrefix = "app", long maxBytes = 5_000_000)
    {
        _directory = directory;
        _componentFilePrefix = componentFilePrefix;
        _maxBytes = maxBytes;
        Directory.CreateDirectory(_directory);
    }

    public void Info(string component, string message, string? stage = null) =>
        Log(LogLevel.Info, component, message, stage);

    public void Warning(string component, string message, string? stage = null) =>
        Log(LogLevel.Warning, component, message, stage);

    public void Error(string component, string message, string? stage = null, string? errorCode = null) =>
        Log(LogLevel.Error, component, message, stage, errorCode: errorCode);

    public void Log(
        LogLevel level,
        string component,
        string message,
        string? stage = null,
        string? operation = null,
        string? errorCode = null,
        TimeSpan? duration = null)
    {
        string safeMessage = RedactSecrets(message);
        var sb = new StringBuilder();
        sb.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
        sb.Append(" [").Append(level.ToString().ToUpperInvariant()).Append(']');
        sb.Append(" component=").Append(component);
        if (!string.IsNullOrWhiteSpace(stage))
        {
            sb.Append(" stage=").Append(stage);
        }

        if (!string.IsNullOrWhiteSpace(operation))
        {
            sb.Append(" op=").Append(operation);
        }

        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            sb.Append(" code=").Append(errorCode);
        }

        if (duration.HasValue)
        {
            sb.Append(" ms=").Append((int)duration.Value.TotalMilliseconds);
        }

        sb.Append(" user=").Append(Environment.UserName);
        sb.Append(" | ").Append(safeMessage);

        string line = sb.ToString();
        string path = Path.Combine(_directory, $"{_componentFilePrefix}.log");

        lock (_sync)
        {
            RotateIfNeeded(path);
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    private void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var info = new FileInfo(path);
            if (info.Length < _maxBytes)
            {
                return;
            }

            string archive = path + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".old";
            File.Move(path, archive);
        }
        catch
        {
            // Não interromper fluxo por falha de rotação.
        }
    }

    public static string RedactSecrets(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        // Redação conservadora de padrões óbvios em logs.
        string result = message;
        string[] keys = { "password=", "senha=", "pin=", "token=", "secret=", "DefaultPassword=" };
        foreach (string key in keys)
        {
            int idx = result.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                int start = idx + key.Length;
                int end = start;
                while (end < result.Length && !char.IsWhiteSpace(result[end]) && result[end] != ';' && result[end] != ',')
                {
                    end++;
                }

                result = result.Substring(0, start) + "***" + result.Substring(end);
                idx = result.IndexOf(key, start + 3, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }
}
