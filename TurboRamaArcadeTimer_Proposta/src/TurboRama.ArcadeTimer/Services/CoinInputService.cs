namespace TurboRama.ArcadeTimer.Services;

/// <summary>
/// Aceita pulsos de ficha com debounce monotónico (imune a salto de relógio).
/// </summary>
public sealed class CoinInputService
{
    private readonly long _debounceMs;
    private readonly object _sync = new();
    private long _lastAcceptedTick = -1;

    public CoinInputService(int debounceMilliseconds)
    {
        _debounceMs = Math.Clamp(debounceMilliseconds, 100, 5000);
    }

    public event Action? CoinAccepted;

    public void ReceivePulse()
    {
        long now = Environment.TickCount64;
        bool accept;

        lock (_sync)
        {
            if (_lastAcceptedTick >= 0)
            {
                long delta = now - _lastAcceptedTick;
                // TickCount64 wrap is not an issue for decades; negative only if clock weird.
                if (delta >= 0 && delta < _debounceMs)
                    return;
            }

            _lastAcceptedTick = now;
            accept = true;
        }

        if (accept)
            CoinAccepted?.Invoke();
    }

    /// <summary>Apenas para testes unitários / QA.</summary>
    public int DebounceMilliseconds => (int)_debounceMs;
}
