using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using TurboBoxManager.Licensing;

namespace TurboRama.EmulationStation.Access;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // This diagnostic does not sign, contact the server, create a CNG key,
        // reveal machine identifiers, or inspect/persist the user's license.
        if (args is ["--probe-identity"]) return ProbeIdentity();
        if (args is not ["--bridge"] || !Console.IsInputRedirected
            || !Console.IsOutputRedirected) return 64;

        ApplicationConfiguration.Initialize();
        using var lifetime = new CancellationTokenSource();
        using var bridge = new BridgeConnection(lifetime);
        SuiteLicensingRuntime? runtime = null;
        try
        {
            var authority = LoadAuthority();
            var identity = new SuiteCngMachineIdentity(authority.IdentityPolicy);
            // Reuse-only preflight, before even collecting the license identifier.
            _ = identity.Describe();
            var client = new SuiteLicenseClient(authority, identity);
            runtime = new SuiteLicensingRuntime(client, authority, TimeProvider.System);
            using var networkCollector = NetworkInventoryCollector.TryStart(client, runtime, lifetime.Token);
            using var form = new LicenseForm(runtime, bridge, lifetime);
            bridge.Start(() => runtime.IsAvailable
                && runtime.CurrentContext?.IsAuthorized == true);
            Application.Run(form);
            return bridge.WasReady ? 0 : 20;
        }
        catch (Exception ex) when (ex is SecurityException or CryptographicException
            or SuiteLicensingUnavailableException or UnauthorizedAccessException
            or PlatformNotSupportedException or NotSupportedException
            or ArgumentException or IOException)
        {
            bridge.Deny();
            // Deliberately never display exception details, paths, or identifiers.
            LicenseForm.ShowUnavailable(AccessFailurePresentation.Describe(ex));
            return 21;
        }
        finally
        {
            lifetime.Cancel();
            if (runtime is not null)
                runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static int ProbeIdentity()
    {
        try
        {
            var authority = LoadAuthority();
            _ = new SuiteCngMachineIdentity(authority.IdentityPolicy).Describe();
            Console.Write("EXISTING_IDENTITY_AVAILABLE\n");
            return 0;
        }
        catch
        {
            Console.Write("EXISTING_IDENTITY_UNAVAILABLE\n");
            return 21;
        }
    }

    internal static SuiteAuthorityConfiguration LoadAuthority()
    {
        var loaded = SuiteEmbeddedAuthorityLoader.Load(
            Assembly.GetExecutingAssembly(), TimeProvider.System);
        return loaded.Configuration
            ?? throw new SuiteLicensingUnavailableException(loaded.FailureCode);
    }
}
