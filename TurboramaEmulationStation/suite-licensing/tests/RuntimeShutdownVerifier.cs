using System.Diagnostics;
using TurboBoxManager.Licensing;

namespace TurboBoxManager.CatalogVerifier;

public static partial class SuiteProtocolVerifier
{
    private static async Task VerifyRuntimeShutdownAsync()
    {
        using var signer = new TestOnlineAssertionSigner();
        var time = new ManualTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        var authority = TestAuthority(time, TimeSpan.FromHours(1), signer);
        var handler = new SessionAuthorityHandler(time, signer, 180, stallHeartbeat: true);
        var client = SuiteLicenseClient.CreateForVerifier(authority, new TestMachineIdentity(), handler, time);
        var runtime = new SuiteLicensingRuntime(client, authority, time);
        var context = await runtime.OpenAsync(LicenseId);
        True(context.IsAuthorized, "synthetic session starts authorized");
        time.Advance(TimeSpan.FromSeconds(67)); // fixture requests 60s with up to 10% jitter
        await handler.HeartbeatStarted.WaitAsync(TimeSpan.FromSeconds(5));
        var beforeShutdown = handler.RequestCount;
        await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        True(!context.IsAuthorized && !runtime.IsAvailable,
            "shutdown revokes locally and stops an in-flight heartbeat");
        True(runtime.CurrentContext is null, "shutdown clears the capability");
        await runtime.DisposeAsync();
        Equal(beforeShutdown, handler.RequestCount, "shutdown uses no activation or new close API");

        // A new process/runtime uses a new session and a fresh server proof, just
        // like Suite. The synthetic server here does not inspect the live PC.
        var nextHandler = new SessionAuthorityHandler(time, signer, 180, false);
        var nextClient = SuiteLicenseClient.CreateForVerifier(authority, new TestMachineIdentity(), nextHandler, time);
        await using (var nextRuntime = new SuiteLicensingRuntime(nextClient, authority, time))
        {
            var reopened = await nextRuntime.OpenAsync(LicenseId);
            True(reopened.IsAuthorized && reopened.SessionId != context.SessionId,
                "reopen requires a fresh, distinct online session");
            Equal(2, nextHandler.RequestCount, "reopen performs challenge and signed proof");
        }

        foreach (var holdPastBudget in new[] { false, true })
        {
            var delayed = new DelayedOpeningHandler(new SessionAuthorityHandler(time, signer, 180, false));
            var delayedClient = SuiteLicenseClient.CreateForVerifier(authority, new TestMachineIdentity(), delayed, time);
            var pendingRuntime = new SuiteLicensingRuntime(delayedClient, authority, time);
            var opening = pendingRuntime.OpenAsync(LicenseId);
            await delayed.OpeningResponseReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var elapsed = Stopwatch.StartNew();
            var shutdown = pendingRuntime.DisposeAsync().AsTask();
            if (!holdPastBudget) delayed.ReleaseResponse.TrySetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
            True(elapsed.Elapsed < TimeSpan.FromSeconds(5), "unresponsive open cannot hold shutdown indefinitely");
            True(!pendingRuntime.IsAvailable && pendingRuntime.CurrentContext is null,
                "no capability can survive closing during login");
            delayed.ReleaseResponse.TrySetResult();
            await ThrowsAsync<OperationCanceledException>(async () =>
                await opening.WaitAsync(TimeSpan.FromSeconds(5)), "late successful response cannot authorize after shutdown");
            await pendingRuntime.DisposeAsync();
        }
        Console.WriteLine("RUNTIME_SHUTDOWN=OK (fresh reopen, cancelled heartbeat, late response, bounded shutdown, no Suite-process dependency)");
    }

    private sealed class DelayedOpeningHandler(HttpMessageHandler inner) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _inner = new(inner);
        internal TaskCompletionSource OpeningResponseReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseResponse { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await _inner.SendAsync(request, token).ConfigureAwait(false);
            if (request.RequestUri?.AbsolutePath.TrimStart('/') == SuiteOnlineLicenseProtocol.SuiteSessionRoute)
            {
                OpeningResponseReady.TrySetResult();
                // Deliberately emulate an uncooperative transport. The runtime's
                // shutdown must remain bounded without accepting this response.
                await ReleaseResponse.Task.ConfigureAwait(false);
            }
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
