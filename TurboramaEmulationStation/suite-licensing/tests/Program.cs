using TurboBoxManager.CatalogVerifier;

if (args.Length == 1 && args[0].StartsWith("--pipe-", StringComparison.Ordinal))
    return AccessIntegrationVerifier.RunPipeFixture(args[0]);

try
{
    SuiteProtocolVerifier.Run();
    await AccessIntegrationVerifier.RunAsync();
    Console.WriteLine("Suite access: cryptographic, TLS, replay, capability, expiry, renewal, DPAPI and IPC checks passed.");
    return 0;
}
catch (Exception ex)
{
    // All fixture data is synthetic; test failures never use live credentials.
    Console.Error.WriteLine(ex);
    return 1;
}
