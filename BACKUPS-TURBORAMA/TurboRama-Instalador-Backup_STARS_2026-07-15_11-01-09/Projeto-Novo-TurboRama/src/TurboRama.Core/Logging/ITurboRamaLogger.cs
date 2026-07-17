namespace TurboRama.Core.Logging;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Security = 4
}

public interface ITurboRamaLogger
{
    void Log(
        LogLevel level,
        string component,
        string message,
        string? stage = null,
        string? operation = null,
        string? errorCode = null,
        TimeSpan? duration = null);

    void Info(string component, string message, string? stage = null);

    void Warning(string component, string message, string? stage = null);

    void Error(string component, string message, string? stage = null, string? errorCode = null);
}
