using System.Text;

namespace TurboRama.EmulationStation.Access;

// Only fixed tokens cross inherited anonymous pipes. Neither activation codes,
// license identifiers, signatures, machine identifiers nor URLs cross this IPC.
internal sealed class BridgeConnection : IDisposable
{
    private readonly CancellationTokenSource _lifetime;
    private readonly object _gate = new();
    private readonly StreamWriter _output;
    private int _ready;
    private int _disposed;
    private bool _cancelled;
    private Task? _reader;

    internal BridgeConnection(CancellationTokenSource lifetime)
    {
        _lifetime = lifetime;
        _output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            AutoFlush = true,
            NewLine = "\n"
        };
    }

    internal bool WasReady => Volatile.Read(ref _ready) != 0;

    internal void Start(Func<bool> authorized)
    {
        if (_reader is not null) throw new InvalidOperationException();
        // Keep EOF detection alive while the first-run form or network is busy.
        // The thread-pool reader is a background thread and cannot keep an
        // orphan helper alive after Application.Run returns.
        _reader = Task.Run(() => ReadCommands(authorized));
    }

    internal bool Ready(Func<bool> authorized)
    {
        lock (_gate)
        {
            if (_lifetime.IsCancellationRequested || !authorized()
                || Volatile.Read(ref _disposed) != 0 || WasReady) return false;
            Volatile.Write(ref _ready, 1);
            return WriteUnsafe("READY\n");
        }
    }

    internal void Deny()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) == 0 && !_cancelled) WriteUnsafe("DENIED\n");
        }
        Cancel();
    }

    internal void CancelAccess()
    {
        lock (_gate)
        {
            // An explicit user cancellation before READY is not an activation
            // failure. It never turns a revoked/established session into success.
            if (WasReady || _cancelled || _lifetime.IsCancellationRequested
                || Volatile.Read(ref _disposed) != 0) return;
            _cancelled = true;
            WriteUnsafe("CANCELLED\n");
            Cancel();
        }
    }

    private void ReadCommands(Func<bool> authorized)
    {
        try
        {
            using var input = Console.OpenStandardInput();
            var expected = "CHECK\n"u8.ToArray();
            while (!_lifetime.IsCancellationRequested)
            {
                foreach (var value in expected)
                {
                    var actual = input.ReadByte();
                    if (actual == -1) return;
                    if (actual != value) { Deny(); return; }
                }
                lock (_gate)
                {
                    if (Volatile.Read(ref _disposed) != 0) return;
                    if (!WasReady || _lifetime.IsCancellationRequested || !authorized())
                    {
                        WriteUnsafe("DENIED\n");
                        return;
                    }
                    if (!WriteUnsafe("OK\n")) return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException
            or InvalidOperationException) { }
        finally { Cancel(); }
    }

    private bool WriteUnsafe(string token)
    {
        try { _output.Write(token); return true; }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        { Cancel(); return false; }
    }

    private void Cancel()
    {
        try { _lifetime.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        Cancel();
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _output.Dispose();
        }
    }
}
