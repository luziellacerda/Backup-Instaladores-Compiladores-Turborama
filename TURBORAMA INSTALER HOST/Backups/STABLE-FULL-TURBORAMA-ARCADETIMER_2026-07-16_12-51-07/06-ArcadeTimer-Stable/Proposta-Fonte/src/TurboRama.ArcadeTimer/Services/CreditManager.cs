using TurboRama.ArcadeTimer.Models;

namespace TurboRama.ArcadeTimer.Services;

/// <summary>
/// Contador de crédito/ficha com limites e tempo monotónico (ms).
/// </summary>
public sealed class CreditManager
{
    private readonly object _sync = new();
    private readonly CreditStore _store;
    private readonly long _maxRemainingSeconds;
    private readonly int _minutesPerCoinCap;
    private CreditData _data;
    private long _msAccumulator;

    public CreditManager(
        CreditStore store,
        bool restoreCredit,
        long maxRemainingSeconds = 28_800,
        int minutesPerCoinCap = 60)
    {
        _store = store;
        _maxRemainingSeconds = Math.Clamp(maxRemainingSeconds, 60, 7 * 24 * 3600L); // 1 min .. 7 dias
        _minutesPerCoinCap = Math.Clamp(minutesPerCoinCap, 1, 120);
        _data = restoreCredit ? store.Load() : new CreditData();
        ClampData();
    }

    public event Action<TimeSpan>? CreditChanged;
    public event Action? CreditEnded;

    public long MaxRemainingSeconds => _maxRemainingSeconds;

    public TimeSpan Remaining
    {
        get
        {
            lock (_sync)
                return TimeSpan.FromSeconds(_data.RemainingSeconds);
        }
    }

    public long RemainingSeconds
    {
        get
        {
            lock (_sync)
                return _data.RemainingSeconds;
        }
    }

    public long TotalCoinsAccepted
    {
        get
        {
            lock (_sync)
                return _data.TotalCoinsAccepted;
        }
    }

    public void AddCoin(int minutesPerCoin)
    {
        int minutes = Math.Clamp(minutesPerCoin, 1, _minutesPerCoinCap);
        long addSeconds = minutes * 60L;
        bool changed;

        lock (_sync)
        {
            long before = _data.RemainingSeconds;
            long sum = before + addSeconds;
            if (sum < before) // overflow long
                sum = _maxRemainingSeconds;
            _data.RemainingSeconds = Math.Min(sum, _maxRemainingSeconds);
            if (_data.TotalCoinsAccepted < long.MaxValue)
                _data.TotalCoinsAccepted++;
            _data.UpdatedAt = DateTimeOffset.Now;
            changed = _data.RemainingSeconds != before;
            _store.Save(_data);
        }

        if (changed)
            CreditChanged?.Invoke(Remaining);
    }

    /// <summary>
    /// Consome tempo real acumulando milissegundos (preciso com qualquer intervalo de tick).
    /// </summary>
    public bool Consume(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
            return false;

        bool ended = false;
        bool changed = false;

        lock (_sync)
        {
            if (_data.RemainingSeconds <= 0)
                return false;

            long ms = (long)elapsed.TotalMilliseconds;
            if (ms <= 0)
                return false;

            // Teto por tick: evita dreno absurdo (sleep/hibernação/salto).
            if (ms > 5_000)
                ms = 5_000;

            _msAccumulator += ms;
            long seconds = _msAccumulator / 1000;
            if (seconds <= 0)
                return false;

            _msAccumulator %= 1000;
            long before = _data.RemainingSeconds;
            _data.RemainingSeconds = Math.Max(0, before - seconds);
            _data.UpdatedAt = DateTimeOffset.Now;
            changed = _data.RemainingSeconds != before;
            ended = _data.RemainingSeconds == 0 && before > 0;

            if (changed)
                _store.Save(_data);
        }

        if (changed)
            CreditChanged?.Invoke(Remaining);

        if (ended)
            CreditEnded?.Invoke();

        return changed;
    }

    public void Save()
    {
        lock (_sync)
        {
            ClampData();
            _store.Save(_data);
        }
    }

    private void ClampData()
    {
        if (_data.RemainingSeconds < 0)
            _data.RemainingSeconds = 0;
        if (_data.RemainingSeconds > _maxRemainingSeconds)
            _data.RemainingSeconds = _maxRemainingSeconds;
        if (_data.TotalCoinsAccepted < 0)
            _data.TotalCoinsAccepted = 0;
    }
}
