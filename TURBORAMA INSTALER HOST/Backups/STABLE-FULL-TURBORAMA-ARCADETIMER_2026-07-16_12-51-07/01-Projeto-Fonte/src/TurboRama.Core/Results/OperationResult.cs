namespace TurboRama.Core.Results;

/// <summary>
/// Resultado padronizado de qualquer operação do TurboRama.
/// Nenhuma falha deve ser engolida: preencher Message, ErrorCode e contexto.
/// </summary>
public sealed class OperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public string? OperationName { get; init; }
    public string? CommandOrApi { get; init; }
    public int? ExitCode { get; init; }
    public string? PreviousState { get; init; }
    public string? CurrentState { get; init; }
    public bool CanRollback { get; init; }
    public Exception? Exception { get; init; }
    public TimeSpan? Duration { get; init; }

    public static OperationResult Ok(
        string message = "OK",
        string? operationName = null,
        string? previousState = null,
        string? currentState = null,
        TimeSpan? duration = null) =>
        new()
        {
            Success = true,
            Message = message,
            OperationName = operationName,
            PreviousState = previousState,
            CurrentState = currentState,
            CanRollback = true,
            Duration = duration
        };

    public static OperationResult Fail(
        string message,
        string? errorCode = null,
        string? operationName = null,
        string? commandOrApi = null,
        int? exitCode = null,
        string? previousState = null,
        string? currentState = null,
        bool canRollback = true,
        Exception? exception = null,
        TimeSpan? duration = null) =>
        new()
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode,
            OperationName = operationName,
            CommandOrApi = commandOrApi,
            ExitCode = exitCode,
            PreviousState = previousState,
            CurrentState = currentState,
            CanRollback = canRollback,
            Exception = exception,
            Duration = duration
        };

    public override string ToString()
    {
        if (Success)
        {
            return $"[OK] {OperationName ?? "op"}: {Message}";
        }

        return $"[FAIL] {OperationName ?? "op"} ({ErrorCode ?? "ERR"}): {Message}";
    }
}
