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
        if (fixture == "--pipe-cancel") bridge.CancelAccess();
        else if (fixture == "--pipe-denied") bridge.Deny();
        else if (fixture != "--pipe-pending")
            bridge.Ready(Authorized);
        if (fixture == "--pipe-cancel-after-ready") bridge.CancelAccess();
        lifetime.Token.WaitHandle.WaitOne();
        return 0;
    }

    internal static async Task RunAsync()
    {
        VerifyFailurePresentation();
        VerifyCacheEnvelope();
        await VerifyPipeChecksAndDenialAsync();
        await VerifyMalformedCommandAsync();
        await VerifyParentEofAsync(pending: false);
        await VerifyParentEofAsync(pending: true);
        await VerifyExplicitCancelAsync();
    }

    private static void VerifyFailurePresentation()
    {
        const string sensitiveFixture = "DO-NOT-DISPLAY-SYNTHETIC-IDENTIFIER";
        var unavailable = new TurboBoxManager.Licensing.SuiteApiException(
            404, "ONLINE_DENIED", sensitiveFixture);
        var notFound = new TurboBoxManager.Licensing.SuiteApiException(
            404, "LICENSE_NOT_FOUND", sensitiveFixture);
        var invalidResponse = new TurboBoxManager.Licensing.SuiteApiException(
            502, "INVALID_RESPONSE", sensitiveFixture);
        var sessionConflict = new TurboBoxManager.Licensing.SuiteApiException(
            409, "ES_SESSION_CONFLICT", sensitiveFixture);
        Require(AccessFailurePresentation.Describe(unavailable)
            == AccessFailurePresentation.ServerUnavailable, "missing route is not license denial");
        Require(AccessFailurePresentation.Describe(notFound)
            == AccessFailurePresentation.AccessDenied, "explicit server license denial");
        Require(AccessFailurePresentation.Describe(invalidResponse)
            == AccessFailurePresentation.ServerUnconfirmed, "invalid response is not activation request");
        Require(AccessFailurePresentation.Describe(sessionConflict)
            == AccessFailurePresentation.SessionConflict, "shared ES session conflict is preserved");
        foreach (var exception in new Exception[] { unavailable, notFound, invalidResponse, sessionConflict,
            new ArgumentException(sensitiveFixture), new SecurityException(sensitiveFixture),
            new HttpRequestException(sensitiveFixture), new Exception(sensitiveFixture) })
            Require(!AccessFailurePresentation.Describe(exception).Contains(sensitiveFixture,
                StringComparison.Ordinal), "presentation must not expose exception data");
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

    private static async Task VerifyExplicitCancelAsync()
    {
        foreach (var fixture in new[] { "--pipe-cancel", "--pipe-denied" })
        {
            using var child = StartFixture(fixture);
            Require(await ReadTokenAsync(child) == (fixture == "--pipe-cancel" ? "CANCELLED" : "DENIED"),
                "cancel must be distinct from authorization failure");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Require(child.ExitCode == 0, "cancel/denial fixture exits");
            Require((await child.StandardOutput.ReadToEndAsync()).Length == 0,
                "no READY or extra token after cancellation");
        }
        using var ready = StartFixture("--pipe-cancel-after-ready");
        Require(await ReadTokenAsync(ready) == "READY", "established authorization fixture");
        await ready.StandardInput.WriteAsync("CHECK\n");
        await ready.StandardInput.FlushAsync();
        Require(await ReadTokenAsync(ready) == "OK", "cancel-only token cannot mask established session state");
        ready.StandardInput.Close();
        await ready.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
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
