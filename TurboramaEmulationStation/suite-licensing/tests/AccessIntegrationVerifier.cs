using System.Diagnostics;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using TurboRama.EmulationStation.Access;

internal static class AccessIntegrationVerifier
{
    internal static int RunPipeFixture(string fixture)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var bridge = new BridgeConnection(lifetime);
        var checks = 0;
        bool Authorized() => fixture != "--pipe-deny-second"
            || Interlocked.Increment(ref checks) <= 2; // READY + first CHECK
        bridge.Start(Authorized);
        if (fixture != "--pipe-pending")
            bridge.Ready(Authorized);
        lifetime.Token.WaitHandle.WaitOne();
        return 0;
    }

    internal static async Task RunAsync()
    {
        VerifyCacheEnvelope();
        await VerifyPipeChecksAndDenialAsync();
        await VerifyMalformedCommandAsync();
        await VerifyParentEofAsync(pending: false);
        await VerifyParentEofAsync(pending: true);
    }

    private static void VerifyCacheEnvelope()
    {
        // Exercise DPAPI with synthetic data in memory; do not touch the real
        // per-user cache or create/activate a Suite key.
        var cipher = LicenseCache.ProtectIdentifier("SYNTHETIC-TEST-ONLY");
        try
        {
            Require(LicenseCache.UnprotectIdentifier(cipher) == "SYNTHETIC-TEST-ONLY",
                "DPAPI identifier round trip");
            cipher[^1] ^= 1;
            try
            {
                _ = LicenseCache.UnprotectIdentifier(cipher);
                throw new InvalidOperationException("Tampered cache accepted.");
            }
            catch (CryptographicException) { }
            catch (SecurityException) { }
        }
        finally { CryptographicOperations.ZeroMemory(cipher); }
        try
        {
            _ = LicenseCache.UnprotectIdentifier(new byte[4096]);
            throw new InvalidOperationException("Oversized cache accepted.");
        }
        catch (SecurityException) { }
    }

    private static async Task VerifyPipeChecksAndDenialAsync()
    {
        using var child = StartFixture("--pipe-deny-second");
        Require(await ReadTokenAsync(child) == "READY", "initial authorization token");
        await child.StandardInput.WriteAsync("CHECK\n");
        await child.StandardInput.FlushAsync();
        Require(await ReadTokenAsync(child) == "OK", "authorized CHECK");
        await child.StandardInput.WriteAsync("CHECK\n");
        await child.StandardInput.FlushAsync();
        Require(await ReadTokenAsync(child) == "DENIED", "revoked CHECK must deny");
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Require(child.ExitCode == 0, "revocation shuts down helper");
    }

    private static async Task VerifyMalformedCommandAsync()
    {
        using var child = StartFixture("--pipe-valid");
        Require(await ReadTokenAsync(child) == "READY", "initial READY");
        await child.StandardInput.WriteAsync("CHECKX");
        await child.StandardInput.FlushAsync();
        Require(await ReadTokenAsync(child) == "DENIED", "malformed IPC must deny");
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task VerifyParentEofAsync(bool pending)
    {
        using var child = StartFixture(pending ? "--pipe-pending" : "--pipe-valid");
        if (!pending) Require(await ReadTokenAsync(child) == "READY", "initial READY");
        child.StandardInput.Close();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Require(child.ExitCode == 0, "parent EOF cancels helper, including pending login");
    }

    private static Process StartFixture(string fixture)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException();
        var start = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add(fixture);
        return Process.Start(start) ?? throw new InvalidOperationException();
    }

    private static async Task<string> ReadTokenAsync(Process child)
    {
        var line = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        return line ?? throw new InvalidOperationException("Helper ended before token.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
