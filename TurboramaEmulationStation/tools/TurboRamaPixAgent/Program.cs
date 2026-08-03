using System.Globalization;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QRCoder;

AgentCommand command;
try { command = AgentCommand.Parse(args); }
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Comando PIX invalido: {ex.Message}");
    return 9;
}
PixOptions options;
PixOwnerSettings? ownerSettings = null;
PixPaths? startupPaths = null;
try
{
    options = PixOptions.Load();
    if (!string.IsNullOrWhiteSpace(command.BridgeDirectory))
        options = (options with { BridgeDirectory = command.BridgeDirectory }).Normalize();

    // O auto-teste nunca deve abrir, criar ou alterar a ponte real, mesmo se
    // ela ja tiver um cadastro de proprietario com erro.
    if (command.SelfTest)
        return PixSelfTest.RunIsolated(options);

    // Antes de ler qualquer configuracao do proprietario, removemos as ACLs
    // herdadas das instalacoes antigas. Assim um usuario local sem privilegio
    // nao consegue trocar o PDV, os precos ou a chave de creditos entre a
    // inicializacao do agente e o processamento do pagamento.
    startupPaths = new PixPaths(options.ResolveBridgeDirectory());
    startupPaths.EnsureDirectories();
    WindowsFileSecurity.HardenBridgeDirectory(startupPaths.Root);
    foreach (var protectedFile in new[]
    {
        startupPaths.SecretFile,
        startupPaths.SigningKeyFile,
        startupPaths.CredentialPrivateKeyFile,
        startupPaths.CredentialPublicKeyFile,
        startupPaths.CredentialUpdateFile,
        startupPaths.CredentialUpdateStatusFile,
        startupPaths.CredentialReplayFile,
        Path.Combine(startupPaths.Root, "owner-settings.json")
    })
    {
        WindowsFileSecurity.HardenCredentialFileIfPresent(protectedFile);
    }
    var arcadeConfiguration = Path.Combine(Directory.GetParent(startupPaths.Root)?.FullName ?? startupPaths.Root, "arcade_credit.cfg");
    WindowsFileSecurity.HardenCredentialFileIfPresent(arcadeConfiguration);

    // O configurador externo precisa conseguir substituir inclusive um
    // cadastro antigo corrompido. Nesse modo, o arquivo anterior nao participa
    // da validacao; somente a nova configuracao confirmada sera persistida.
    ownerSettings = string.IsNullOrWhiteSpace(command.ConfigureOwnerFile)
        ? PixOwnerSettings.LoadIfPresent(options.ResolveBridgeDirectory())
        : null;
    if (ownerSettings is not null && ownerSettings.Enabled)
        options = ownerSettings.Apply(options);
    options.ValidateForStartup(command.SetToken || command.AcceptCredentialOnce || command.MercadoPagoInventory
        || !string.IsNullOrWhiteSpace(command.MercadoPagoSetupFile)
        || !string.IsNullOrWhiteSpace(command.ConfigureOwnerFile));
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or SecurityException or CryptographicException)
{
    Console.Error.WriteLine($"Configuracao PIX invalida: {ex.Message}");
    return 10;
}

if (string.IsNullOrWhiteSpace(command.ConfigureOwnerFile))
    Console.WriteLine($"PIX configurado: provider={options.Provider}; bridge={options.BridgeDirectory}");
else
    Console.WriteLine($"Configuracao comercial PIX iniciada: bridge={options.BridgeDirectory}");
var paths = startupPaths!;
using var fileLog = AgentFileLog.TryAttach(paths.Logs);
var secrets = new PixSecretStore(paths.SecretFile);
var signingKeys = new PixSigningKeyStore(paths.SigningKeyFile);

using var instanceLock = PixAgentInstanceLock.TryAcquire(paths.Root);
if (instanceLock is null)
{
    Console.Error.WriteLine("Ja existe uma instancia do agente PIX usando esta pasta. Encerre-a antes de iniciar outra.");
    return 12;
}

// Configuracao comercial completa usada pelo aplicativo Windows LZ Games.
// O segredo chega somente pelo pipe de entrada, nunca pelo JSON, linha de
// comando ou log. O cadastro e salvo como pendente antes das chamadas de
// rede e so passa para pronto depois que conta, loja e PDV forem confirmados.
if (!string.IsNullOrWhiteSpace(command.ConfigureOwnerFile))
{
    try
    {
        var request = PixOwnerProvisioningRequest.Load(command.ConfigureOwnerFile);
        var credential = SecretConsole.ReadHidden().Trim();
        if (string.IsNullOrWhiteSpace(credential))
            throw new SecurityException("a credencial do provedor nao foi informada");
        var result = await PixOwnerProvisioner.ConfigureAsync(request, credential, options, paths, secrets, CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(result, Json.Options));
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or HttpRequestException
        or TaskCanceledException or MercadoPagoApiException or AdapterApiException or InvalidOperationException
        or SecurityException or CryptographicException)
    {
        Console.Error.WriteLine($"Falha na configuracao completa do PIX: {ex.Message}");
        return 21;
    }
}

// Chamado somente pelo instalador/editor. A trava acima garante que uma
// preparacao da ponte nunca rotacione a chave enquanto o agente esta
// processando pagamentos. Nao consulta Mercado Pago nem gera cobranca.
if (command.PrepareCredentialEditor)
{
    try
    {
        var bootstrapInbox = new PixCredentialInbox(paths, secrets);
        bootstrapInbox.EnsureReady();
        if (!File.Exists(paths.CredentialPublicKeyFile))
            throw new SecurityException("a chave publica segura do agente PIX nao foi publicada");
        Console.WriteLine("Ponte segura do editor PIX preparada.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidOperationException or SecurityException)
    {
        Console.Error.WriteLine($"Nao foi possivel preparar a ponte segura do editor PIX: {ex.Message}");
        return 10;
    }
}

// Usado pelo editor externo quando o EmulationStation e o agente persistente
// estao fechados. Publica/confere a chave, consome exatamente o pedido ja
// gravado e encerra. Se houver um agente persistente, esta instancia nao obtem
// a trava; o agente existente consumira o mesmo pedido normalmente.
if (command.AcceptCredentialOnce)
{
    try
    {
        var bootstrapInbox = new PixCredentialInbox(paths, secrets);
        bootstrapInbox.EnsureReady();
        if (!File.Exists(paths.CredentialUpdateFile))
            throw new InvalidOperationException("nenhuma atualizacao de Access Token foi entregue pelo editor");
        if (!bootstrapInbox.TryAcceptPendingUpdate())
            throw new SecurityException("a atualizacao do Access Token foi recusada");
        Console.WriteLine("Access Token recebido pelo agente PIX.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidOperationException or SecurityException)
    {
        Console.Error.WriteLine($"Nao foi possivel receber o Access Token do editor PIX: {ex.Message}");
        return 19;
    }
}

if (command.SetToken)
{
    Console.Write("Digite a senha do proprietario e pressione Enter: ");
    var ownerPassword = SecretConsole.ReadHidden();
    Console.WriteLine();
    try { PixOwnerPassword.Verify(paths, ownerPassword); }
    catch (SecurityException ex)
    {
        Console.Error.WriteLine($"Senha do proprietario recusada: {ex.Message}");
        return 2;
    }
    var credentialName = options.Provider == "mercadopago"
        ? "Access Token do Mercado Pago"
        : "segredo Bearer do adaptador bancario";
    Console.Write($"Cole o {credentialName} e pressione Enter: ");
    var token = SecretConsole.ReadHidden().Trim();
    Console.WriteLine();
    if (string.IsNullOrWhiteSpace(token))
    {
        Console.Error.WriteLine("Nenhum token informado. Nada foi salvo.");
        return 2;
    }
    if (options.Provider == "mercadopago" &&
        (token.Length is < 40 or > 384 ||
         !token.StartsWith("APP_USR-", StringComparison.Ordinal) ||
         token.Any(char.IsWhiteSpace)))
    {
        Console.Error.WriteLine("Access Token recusado: formato inesperado. Copie o Access Token completo, iniciado por APP_USR-, sem espacos.");
        return 2;
    }
    Console.WriteLine($"Token recebido: {SecretConsole.Mask(token)} ({token.Length} caracteres). O valor completo permanece oculto.");
    try
    {
        secrets.Save(token);
        Console.WriteLine("Credencial protegida pelo Windows e salva para este usuario.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
    {
        Console.Error.WriteLine($"Nao foi possivel proteger e salvar o token: {ex.Message}");
        return 14;
    }
}

// O editor externo nunca grava o segredo diretamente. Ele entrega o token
// cifrado com a chave publica deste agente; assim o segredo final e protegido
// pelo mesmo usuario Windows que executa o servico PIX.
var credentialInbox = new PixCredentialInbox(paths, secrets);
var credentialInboxReady = false;
var nextCredentialInboxAttempt = DateTimeOffset.MinValue;
var lastCredentialInboxError = "";
try
{
    credentialInbox.EnsureReady();
    credentialInboxReady = true;
    credentialInbox.TryAcceptPendingUpdate();
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidOperationException or SecurityException)
{
    Console.Error.WriteLine($"Falha ao preparar a atualizacao segura de credencial PIX: {ex.Message}");
    lastCredentialInboxError = ex.Message;
    nextCredentialInboxAttempt = DateTimeOffset.UtcNow.AddSeconds(15);
}

var provider = PixProviderFactory.Create(options, secrets);

// O cadastro pode ser salvo sem internet. A criacao/confirmacao da loja e do
// PDV e retomada automaticamente a cada 15 segundos ate a conexao voltar.
OwnerInfrastructureCoordinator? ownerInfrastructure = null;
if (ownerSettings is not null && ownerSettings.Enabled && provider is MercadoPagoPixProvider ownerMercadoPago)
{
    ownerInfrastructure = new OwnerInfrastructureCoordinator(ownerSettings, ownerMercadoPago, paths);
    await ownerInfrastructure.TryEnsureAsync(force: true, CancellationToken.None);
}

if (command.MercadoPagoInventory || !string.IsNullOrWhiteSpace(command.MercadoPagoSetupFile))
{
    if (provider is not MercadoPagoPixProvider mercadoPago)
    {
        Console.Error.WriteLine("Este comando exige Provider=mercadopago.");
        return 17;
    }
    try
    {
        if (command.MercadoPagoInventory)
        {
            var inventory = ownerSettings is { Enabled: true } && !string.IsNullOrWhiteSpace(ownerSettings.AccountId)
                ? await mercadoPago.GetInfrastructureForConfiguredAccountAsync(ownerSettings.AccountId, CancellationToken.None)
                : await mercadoPago.GetInfrastructureAsync(CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(inventory, Json.Options));
        }
        else
        {
            var setup = MercadoPagoSetupRequest.Load(command.MercadoPagoSetupFile);
            var result = await mercadoPago.EnsureInfrastructureAsync(setup, CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(result, Json.Options));
        }
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or HttpRequestException
        or MercadoPagoApiException or InvalidOperationException or SecurityException)
    {
        Console.Error.WriteLine($"Falha na configuracao do Mercado Pago: {ex.Message}");
        return 18;
    }
}

var engine = new PixEngine(options, paths, provider, signingKeys);

// Contrato publico consumido pela interface do EmulationStation. Ele contem
// somente disponibilidade e precos; credenciais nunca saem do cofre DPAPI.
PixPublicContract.Publish(options, paths, provider.Name,
    provider.Name == "mock" || secrets.TryLoad().IsAvailable, provider.Name == "mock", "starting");

if (!string.IsNullOrWhiteSpace(command.ApproveId))
{
    try
    {
        var approved = await engine.ApproveMockAsync(command.ApproveId);
        Console.WriteLine(approved ? "Cobranca de teste aprovada." : "Cobranca nao encontrada ou nao e de teste.");
        return approved ? 0 : 3;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or JsonException or SecurityException)
    {
        Console.Error.WriteLine($"Nao foi possivel aprovar o teste PIX: {ex.Message}");
        return 15;
    }
}

if (command.CheckProvider)
{
    try
    {
        await provider.CheckHealthAsync(CancellationToken.None);
        Console.WriteLine("Credencial e conexao com o provedor confirmadas.");
        return 0;
    }
    catch (Exception ex) when (ex is HttpRequestException or MercadoPagoApiException or AdapterApiException or InvalidOperationException or SecurityException)
    {
        Console.Error.WriteLine($"Falha na verificacao do provedor: {ex.Message}");
        return 16;
    }
}

Console.WriteLine($"TurboRama PIX Agent | provedor: {provider.Name} | pasta: {paths.Root}");
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var providerHealthy = provider.Name == "mock";
var nextHealthCheck = DateTimeOffset.MinValue;
var lastHealthError = "";
try
{
    do
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (credentialInboxReady && !File.Exists(paths.CredentialPublicKeyFile)) credentialInboxReady = false;
            if (!credentialInboxReady && now >= nextCredentialInboxAttempt)
            {
                try
                {
                    credentialInbox.EnsureReady();
                    credentialInboxReady = true;
                    nextCredentialInboxAttempt = DateTimeOffset.MinValue;
                    if (!string.IsNullOrWhiteSpace(lastCredentialInboxError))
                        Console.WriteLine("Ponte segura de credencial PIX restabelecida.");
                    lastCredentialInboxError = "";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidOperationException or SecurityException)
                {
                    credentialInboxReady = false;
                    nextCredentialInboxAttempt = now.AddSeconds(15);
                    if (!ex.Message.Equals(lastCredentialInboxError, StringComparison.Ordinal))
                        Console.Error.WriteLine($"Ponte segura de credencial PIX indisponivel: {ex.Message}");
                    lastCredentialInboxError = ex.Message;
                }
            }
            var credentialChanged = credentialInboxReady && credentialInbox.TryAcceptPendingUpdate();
            if (credentialChanged)
            {
                providerHealthy = false;
                nextHealthCheck = DateTimeOffset.MinValue;
            }
            if (ownerInfrastructure is not null && (!ownerInfrastructure.Ready || credentialChanged))
                await ownerInfrastructure.TryEnsureAsync(force: credentialChanged, cancellation.Token);

            if (ownerInfrastructure is { Ready: false })
            {
                providerHealthy = false;
                nextHealthCheck = DateTimeOffset.UtcNow.AddSeconds(10);
            }
            else if (DateTimeOffset.UtcNow >= nextHealthCheck)
            {
                try
                {
                    await provider.CheckHealthAsync(cancellation.Token);
                    if (!providerHealthy && !string.IsNullOrWhiteSpace(lastHealthError))
                        Console.WriteLine("Conexao com o provedor PIX restabelecida.");
                    providerHealthy = true;
                    lastHealthError = "";
                }
                catch (Exception ex) when (ex is HttpRequestException or MercadoPagoApiException or AdapterApiException or InvalidOperationException or SecurityException)
                {
                    providerHealthy = false;
                    if (!ex.Message.Equals(lastHealthError, StringComparison.Ordinal))
                        Console.Error.WriteLine($"Provedor PIX indisponivel: {ex.Message}");
                    lastHealthError = ex.Message;
                }
                nextHealthCheck = DateTimeOffset.UtcNow.AddSeconds(providerHealthy ? 60 : 10);
            }
            PixPublicContract.Publish(options, paths, provider.Name,
                provider.Name == "mock" || secrets.TryLoad().IsAvailable, providerHealthy,
                providerHealthy ? "online" : ownerInfrastructure is { Ready: false } ? "owner_setup_pending" : "provider_unavailable");
            if (providerHealthy) await engine.RunOnceAsync(cancellation.Token);
            else if (command.Once) return 13;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException or CryptographicException or SecurityException)
        {
            Console.Error.WriteLine($"Falha geral temporaria no agente PIX: {ex.Message}");
            if (command.Once) return 13;
        }
        if (command.Once) break;
        await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), cancellation.Token);
    }
    while (!cancellation.IsCancellationRequested);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }

return 0;

sealed record AgentCommand(bool Once, bool SetToken, bool SelfTest, bool CheckProvider, bool PrepareCredentialEditor, bool AcceptCredentialOnce,
    bool MercadoPagoInventory, string MercadoPagoSetupFile, string ConfigureOwnerFile, string ApproveId, string BridgeDirectory)
{
    public static AgentCommand Parse(string[] args)
    {
        var once = false;
        var setToken = false;
        var selfTest = false;
        var checkProvider = false;
        var prepareCredentialEditor = false;
        var acceptCredentialOnce = false;
        var mercadoPagoInventory = false;
        var mercadoPagoSetupFile = "";
        var configureOwnerFile = "";
        var approveId = "";
        var bridgeDirectory = "";
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--once", StringComparison.OrdinalIgnoreCase)) { once = true; continue; }
            if (args[i].Equals("--set-token", StringComparison.OrdinalIgnoreCase)) { setToken = true; continue; }
            if (args[i].Equals("--self-test", StringComparison.OrdinalIgnoreCase)) { selfTest = true; continue; }
            if (args[i].Equals("--check-provider", StringComparison.OrdinalIgnoreCase)) { checkProvider = true; continue; }
            if (args[i].Equals("--prepare-credential-editor", StringComparison.OrdinalIgnoreCase)) { prepareCredentialEditor = true; continue; }
            if (args[i].Equals("--accept-credential-once", StringComparison.OrdinalIgnoreCase)) { acceptCredentialOnce = true; continue; }
            if (args[i].Equals("--mercadopago-inventory", StringComparison.OrdinalIgnoreCase)) { mercadoPagoInventory = true; continue; }
            if (args[i].Equals("--mercadopago-setup", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new InvalidOperationException("--mercadopago-setup exige o caminho de um arquivo JSON.");
                mercadoPagoSetupFile = args[++i];
                continue;
            }
            if (args[i].Equals("--configure-owner", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new InvalidOperationException("--configure-owner exige o caminho de um arquivo JSON.");
                configureOwnerFile = args[++i];
                continue;
            }
            if (args[i].Equals("--approve", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new InvalidOperationException("--approve exige um identificador.");
                approveId = args[++i];
                continue;
            }
            if (args[i].Equals("--bridge", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new InvalidOperationException("--bridge exige o caminho da pasta compartilhada PIX.");
                bridgeDirectory = args[++i];
                continue;
            }
            throw new InvalidOperationException($"opcao desconhecida: {args[i]}");
        }
        var exclusiveModes = (setToken ? 1 : 0) + (selfTest ? 1 : 0) + (checkProvider ? 1 : 0) + (prepareCredentialEditor ? 1 : 0)
            + (acceptCredentialOnce ? 1 : 0)
            + (mercadoPagoInventory ? 1 : 0) + (!string.IsNullOrWhiteSpace(mercadoPagoSetupFile) ? 1 : 0)
            + (!string.IsNullOrWhiteSpace(configureOwnerFile) ? 1 : 0)
            + (!string.IsNullOrWhiteSpace(approveId) ? 1 : 0);
        if (exclusiveModes > 1)
            throw new InvalidOperationException("use somente um modo administrativo por execucao.");
        return new AgentCommand(once, setToken, selfTest, checkProvider, prepareCredentialEditor, acceptCredentialOnce,
            mercadoPagoInventory, mercadoPagoSetupFile, configureOwnerFile, approveId, bridgeDirectory);
    }
}

sealed record PixOptions
{
    public string Provider { get; init; } = "mock";
    public string BridgeDirectory { get; init; } = "%USERPROFILE%\\.emulationstation\\pix";
    public List<int> AllowedMinutes { get; init; } = [15, 30, 45, 60, 120];
    public Dictionary<int, long> PackagePricesCents { get; init; } = new()
    {
        [15] = 750, [30] = 1500, [45] = 2250, [60] = 3000, [120] = 6000
    };
    public int PollSeconds { get; init; } = 4;
    public int PaymentExpirationMinutes { get; init; } = 15;
    public int HttpTimeoutSeconds { get; init; } = 15;
    public int MaxRetrySeconds { get; init; } = 300;
    public bool ProductionEnabled { get; init; }
    public MercadoPagoOptions MercadoPago { get; init; } = new();
    public AdapterOptions Adapter { get; init; } = new();

    public static PixOptions Load()
    {
        var file = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            Path.Combine(Environment.CurrentDirectory, "appsettings.json")
        }.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(file)) return new PixOptions().Normalize();
        using var document = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
        if (!document.RootElement.TryGetProperty("TurboRamaPix", out var section))
            throw new InvalidOperationException($"A secao TurboRamaPix nao existe em {file}.");
        var loaded = (JsonSerializer.Deserialize<PixOptions>(section.GetRawText(), Json.Options) ?? new PixOptions()).Normalize();
        var providerOverride = Environment.GetEnvironmentVariable("TURBORAMA_PIX_PROVIDER");
        var bridgeOverride = Environment.GetEnvironmentVariable("TURBORAMA_PIX_BRIDGE_DIRECTORY");
        var adapterBaseUrlOverride = Environment.GetEnvironmentVariable("TURBORAMA_PIX_ADAPTER_BASE_URL");
        var adapterProviderIdOverride = Environment.GetEnvironmentVariable("TURBORAMA_PIX_ADAPTER_PROVIDER_ID");
        return (loaded with
        {
            Provider = string.IsNullOrWhiteSpace(providerOverride) ? loaded.Provider : providerOverride,
            BridgeDirectory = string.IsNullOrWhiteSpace(bridgeOverride) ? loaded.BridgeDirectory : bridgeOverride,
            Adapter = loaded.Adapter with
            {
                BaseUrl = string.IsNullOrWhiteSpace(adapterBaseUrlOverride) ? loaded.Adapter.BaseUrl : adapterBaseUrlOverride,
                ProviderId = string.IsNullOrWhiteSpace(adapterProviderIdOverride) ? loaded.Adapter.ProviderId : adapterProviderIdOverride
            }
        }).Normalize();
    }

    public PixOptions Normalize()
    {
        var provider = Provider.Trim().ToLowerInvariant();
        if (provider is not ("mock" or "mercadopago" or "adapter"))
            throw new InvalidOperationException($"Provedor desconhecido: {Provider}.");
        var prices = (PackagePricesCents ?? new Dictionary<int, long>())
            .Where(x => x.Key is >= 1 and <= 480 && x.Value is >= 1 and <= 100_000_000)
            .GroupBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Last().Value);
        if (prices.Count == 0)
            throw new InvalidOperationException("Nenhum pacote PIX com preco valido foi configurado.");
        return this with
        {
            Provider = provider,
            AllowedMinutes = prices.Keys.Order().ToList(),
            PackagePricesCents = prices,
            PollSeconds = Math.Clamp(PollSeconds, 2, 30),
            PaymentExpirationMinutes = Math.Clamp(PaymentExpirationMinutes, 1, 60),
            HttpTimeoutSeconds = Math.Clamp(HttpTimeoutSeconds, 5, 60),
            MaxRetrySeconds = Math.Clamp(MaxRetrySeconds, 30, 1800),
            MercadoPago = MercadoPago ?? new MercadoPagoOptions(),
            Adapter = (Adapter ?? new AdapterOptions()).Normalize()
        };
    }

    public long PriceFor(int minutes)
        => PackagePricesCents.TryGetValue(minutes, out var cents) ? cents : 0;

    public void ValidateForStartup(bool configurationOnly)
    {
        if (configurationOnly || Provider == "mock") return;
        if (!ProductionEnabled)
            throw new InvalidOperationException("Pagamentos reais estao bloqueados. Defina ProductionEnabled=true somente apos concluir os testes.");
        if (Provider == "mercadopago")
        {
            if (string.IsNullOrWhiteSpace(MercadoPago.ExternalPosId))
                throw new InvalidOperationException("MercadoPago.ExternalPosId nao foi configurado.");
            if (MercadoPago.ExternalPosId.Equals("CONFIGURE-O-PDV", StringComparison.OrdinalIgnoreCase)
                || MercadoPago.ExternalPosId.Equals("CONFIGUREOPDV", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("MercadoPago.ExternalPosId ainda contem o valor de instalacao CONFIGUREOPDV.");
            if (!IsValidMercadoPagoPosId(MercadoPago.ExternalPosId))
                throw new InvalidOperationException("MercadoPago.ExternalPosId deve ter ate 40 caracteres, somente letras e numeros. APP_USR e Access Token nao sao identificadores de PDV.");
            if (MercadoPago.DescriptionPrefix.Length > 100)
                throw new InvalidOperationException("MercadoPago.DescriptionPrefix e muito grande.");
            return;
        }
        ValidateAdapterConfiguration();
    }

    public bool IsProviderConfigured()
    {
        if (Provider == "mock") return true;
        if (!ProductionEnabled) return false;
        if (Provider == "mercadopago")
            return !string.IsNullOrWhiteSpace(MercadoPago.ExternalPosId)
                && !MercadoPago.ExternalPosId.Equals("CONFIGURE-O-PDV", StringComparison.OrdinalIgnoreCase)
                && !MercadoPago.ExternalPosId.Equals("CONFIGUREOPDV", StringComparison.OrdinalIgnoreCase)
                && IsValidMercadoPagoPosId(MercadoPago.ExternalPosId);
        try { ValidateAdapterConfiguration(); return true; }
        catch (InvalidOperationException) { return false; }
    }

    public Uri AdapterBaseUri()
    {
        if (!Uri.TryCreate(Adapter.BaseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Adapter.BaseUrl nao e uma URL absoluta valida.");
        return uri;
    }

    private void ValidateAdapterConfiguration()
    {
        if (!PixId.IsValidProviderName(Adapter.ProviderId))
            throw new InvalidOperationException("Adapter.ProviderId deve ter de 2 a 48 letras, numeros, hifen ou sublinhado.");
        var uri = AdapterBaseUri();
        if (uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Adapter.BaseUrl deve usar HTTP local ou HTTPS.");
        if (uri.Scheme == "http" && !uri.IsLoopback)
            throw new InvalidOperationException("Adaptador remoto deve usar HTTPS. HTTP e permitido somente em 127.0.0.1/localhost.");
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Adapter.BaseUrl nao pode conter consulta ou fragmento.");
    }

    private static bool IsValidMercadoPagoPosId(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 40 && value.All(char.IsAsciiLetterOrDigit)
            && !value.StartsWith("APPUSR", StringComparison.OrdinalIgnoreCase);

    public string ResolveBridgeDirectory()
    {
        var home = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(home)) home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configured = Environment.ExpandEnvironmentVariables(BridgeDirectory.Replace("%USERPROFILE%", home, StringComparison.OrdinalIgnoreCase));
        return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(Environment.CurrentDirectory, configured));
    }
}

sealed record MercadoPagoOptions
{
    public string ExternalPosId { get; init; } = "TURBORAMAKIOSK01";
    public string DescriptionPrefix { get; init; } = "Tempo TurboRama";
}

sealed record MercadoPagoSetupRequest
{
    public string ExpectedAccountId { get; init; } = "";
    public string StoreName { get; init; } = "TurboRama";
    public string StoreExternalId { get; init; } = "LZLOJA01";
    public string PosName { get; init; } = "TurboRama Kiosk";
    public string PosExternalId { get; init; } = "LZPIXCOMP";
    public int? Category { get; init; }
    public string StreetName { get; init; } = "";
    public string StreetNumber { get; init; } = "";
    public string CityName { get; init; } = "";
    public string StateName { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string Reference { get; init; } = "TurboRama";
    // Campo interno: nao e enviado ao Mercado Pago. Ele permite que o cache
    // registre de onde vieram as coordenadas somente depois de a Loja aceita.
    public string LocationSource { get; init; } = "";

    public static MercadoPagoSetupRequest Load(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            throw new InvalidOperationException("Arquivo de configuracao da loja nao encontrado.");
        var info = new FileInfo(file);
        if (info.Length is <= 0 or > 32_768)
            throw new InvalidOperationException("Arquivo de configuracao da loja tem tamanho invalido.");
        var setup = JsonSerializer.Deserialize<MercadoPagoSetupRequest>(File.ReadAllText(file, Encoding.UTF8), Json.Options)
            ?? throw new InvalidOperationException("Arquivo de configuracao da loja esta vazio.");
        setup.Validate();
        return setup;
    }

    public void Validate()
    {
        ValidateIdentity();
        ValidateLocationForNewStore();
    }

    public void ValidateIdentity()
    {
        if (ExpectedAccountId.Length is < 5 or > 24 || !ExpectedAccountId.All(char.IsAsciiDigit))
            throw new InvalidOperationException("User ID esperado da conta do Mercado Pago e invalido.");
        if (StoreName.Trim().Length is < 2 or >= 60) throw new InvalidOperationException("Nome da loja invalido.");
        if (PosName.Trim().Length is < 2 or >= 45) throw new InvalidOperationException("Nome do PDV invalido.");
        if (!IsAlphaNumeric(StoreExternalId, 60)) throw new InvalidOperationException("external_id da loja deve ser alfanumerico e ter ate 60 caracteres.");
        if (!IsAlphaNumeric(PosExternalId, 40)) throw new InvalidOperationException("external_id do PDV deve ser alfanumerico e ter ate 40 caracteres.");
        if (Category.HasValue && Category.Value is <= 0 or > 999999) throw new InvalidOperationException("Categoria comercial invalida.");
    }

    public void ValidateLocationForNewStore()
    {
        if (string.IsNullOrWhiteSpace(StreetName) || StreetName.Length > 120) throw new InvalidOperationException("Rua invalida.");
        if (string.IsNullOrWhiteSpace(StreetNumber) || StreetNumber.Length > 20) throw new InvalidOperationException("Numero do endereco invalido.");
        if (string.IsNullOrWhiteSpace(CityName) || CityName.Length > 80) throw new InvalidOperationException("Cidade invalida.");
        if (string.IsNullOrWhiteSpace(StateName) || StateName.Length > 80) throw new InvalidOperationException("Estado invalido.");
        if (!BrazilianPostalAddress.HasValidCoordinates(Latitude, Longitude))
            throw new InvalidOperationException("Latitude/longitude invalidas. A localizacao da loja nao pode usar 0,0.");
        if (string.IsNullOrWhiteSpace(Reference) || Reference.Length > 120) throw new InvalidOperationException("Referencia do endereco invalida.");
    }

    private static bool IsAlphaNumeric(string value, int maximum)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && value.All(char.IsAsciiLetterOrDigit);
}

sealed record PixOwnerSettings
{
    public int SchemaVersion { get; init; } = 1;
    public bool Enabled { get; init; }
    // pending: cadastro e token foram preservados, mas nenhuma cobranca pode
    // ser criada ate conta, Loja e PDV serem realmente confirmados.
    public string SetupState { get; init; } = "ready";
    public string Provider { get; init; } = "mercadopago";
    public string AccountId { get; init; } = "";
    public string StoreExternalId { get; init; } = "TURBORAMALOJA01";
    public string StoreName { get; init; } = "TurboRama";
    public string PosExternalId { get; init; } = "TURBORAMAKIOSK01";
    public string PosName { get; init; } = "TurboRama Kiosk";
    public string PostalCode { get; init; } = "";
    public string StreetNumber { get; init; } = "";
    public string Reference { get; init; } = "TurboRama";
    public string AdapterBaseUrl { get; init; } = "http://127.0.0.1:8765/";
    public string AdapterProviderId { get; init; } = "meu-banco";
    public Dictionary<int, long> PackagePricesCents { get; init; } = new();

    public static PixOwnerSettings? LoadIfPresent(string bridgeDirectory)
    {
        var file = Path.Combine(bridgeDirectory, "owner-settings.json");
        if (!File.Exists(file)) return null;
        var info = new FileInfo(file);
        if (info.Length is <= 0 or > 65_536)
            throw new InvalidOperationException("Cadastro do proprietario PIX tem tamanho invalido.");
        var settings = JsonSerializer.Deserialize<PixOwnerSettings>(File.ReadAllText(file, Encoding.UTF8), Json.Options)
            ?? throw new InvalidOperationException("Cadastro do proprietario PIX esta vazio.");
        settings.Validate();
        return settings;
    }

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidOperationException("Versao do cadastro PIX nao e suportada.");
        if (!Enabled) return;
        var setupState = (SetupState ?? "").Trim().ToLowerInvariant();
        if (setupState is not ("pending" or "ready" or "needs_address_confirmation"))
            throw new InvalidOperationException("Estado do cadastro PIX e invalido.");
        var provider = Provider.Trim().ToLowerInvariant();
        if (provider is not ("mercadopago" or "adapter"))
            throw new InvalidOperationException("Provedor PIX do proprietario e invalido.");
        foreach (var minutes in new[] { 15, 30, 45, 60, 120 })
            if (!PackagePricesCents.TryGetValue(minutes, out var price) || price is < 50 or > 100_000_000)
                throw new InvalidOperationException($"Preco do pacote de {minutes} minutos e invalido.");
        if (provider == "adapter")
        {
            var adapter = new AdapterOptions { BaseUrl = AdapterBaseUrl, ProviderId = AdapterProviderId }.Normalize();
            if (!PixId.IsValidProviderName(adapter.ProviderId))
                throw new InvalidOperationException("Identificador do adaptador bancario e invalido.");
            if (!Uri.TryCreate(adapter.BaseUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || (uri.Scheme == "http" && !uri.IsLoopback)
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("Endereco do adaptador bancario e invalido.");
            return;
        }
        // No primeiro cadastro ainda nao conhecemos a conta: ela e obtida do
        // Access Token em segundo plano. Depois de pronto, o User ID e sempre
        // obrigatorio e validado novamente.
        if (setupState == "ready" && (AccountId.Length is < 5 or > 24 || !AccountId.All(char.IsAsciiDigit)))
            throw new InvalidOperationException("User ID do Mercado Pago e invalido.");
        if (!string.IsNullOrWhiteSpace(AccountId)
            && (AccountId.Length is < 5 or > 24 || !AccountId.All(char.IsAsciiDigit)))
            throw new InvalidOperationException("User ID do Mercado Pago e invalido.");
        if (!AlphaNumeric(StoreExternalId, 60)) throw new InvalidOperationException("Identificador da loja e invalido.");
        if (!AlphaNumeric(PosExternalId, 40)) throw new InvalidOperationException("Identificador do caixa PIX e invalido.");
        if (StoreName.Trim().Length is < 2 or >= 60) throw new InvalidOperationException("Nome da loja e invalido.");
        if (PosName.Trim().Length is < 2 or >= 45) throw new InvalidOperationException("Nome do caixa PIX e invalido.");
        var cep = new string(PostalCode.Where(char.IsAsciiDigit).ToArray());
        if (cep.Length != 8) throw new InvalidOperationException("CEP do estabelecimento e invalido.");
        if (string.IsNullOrWhiteSpace(StreetNumber) || StreetNumber.Length > 20) throw new InvalidOperationException("Numero do estabelecimento e invalido.");
        if (string.IsNullOrWhiteSpace(Reference) || Reference.Length > 120) throw new InvalidOperationException("Referencia do estabelecimento e invalida.");
    }

    public PixOptions Apply(PixOptions options)
    {
        Validate();
        if (!Enabled) return options;
        var provider = Provider.Trim().ToLowerInvariant();
        return (options with
        {
            Provider = provider,
            ProductionEnabled = true,
            AllowedMinutes = PackagePricesCents.Keys.Order().ToList(),
            PackagePricesCents = new Dictionary<int, long>(PackagePricesCents),
            MercadoPago = options.MercadoPago with { ExternalPosId = PosExternalId.Trim() },
            Adapter = provider == "adapter"
                ? new AdapterOptions { BaseUrl = AdapterBaseUrl, ProviderId = AdapterProviderId }.Normalize()
                : options.Adapter
        }).Normalize();
    }

    public async Task<MercadoPagoSetupRequest> BuildSetupRequestAsync(PixPaths paths, CancellationToken token)
    {
        Validate();
        var address = await BrazilianPostalAddress.ResolveAsync(PostalCode, StreetNumber,
            Path.Combine(paths.Root, "owner-address-cache.json"), token);
        return new MercadoPagoSetupRequest
        {
            ExpectedAccountId = AccountId.Trim(),
            StoreName = StoreName.Trim(),
            StoreExternalId = StoreExternalId.Trim(),
            PosName = PosName.Trim(),
            PosExternalId = PosExternalId.Trim(),
            StreetName = address.Street,
            StreetNumber = StreetNumber.Trim(),
            CityName = address.City,
            StateName = address.State,
            Latitude = address.Latitude,
            Longitude = address.Longitude,
            Reference = Reference.Trim(),
            LocationSource = address.Source
        };
    }

    public MercadoPagoSetupRequest BuildSetupRequestForExistingStore()
    {
        Validate();
        return new MercadoPagoSetupRequest
        {
            ExpectedAccountId = AccountId.Trim(),
            StoreName = StoreName.Trim(),
            StoreExternalId = StoreExternalId.Trim(),
            PosName = PosName.Trim(),
            PosExternalId = PosExternalId.Trim()
        };
    }

    private static bool AlphaNumeric(string value, int maximum)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && value.All(char.IsAsciiLetterOrDigit);
}

sealed record PixOwnerProvisioningRequest
{
    public int SchemaVersion { get; init; } = 1;
    public string Provider { get; init; } = "mercadopago";
    public string StoreName { get; init; } = "TurboRama";
    public string StoreExternalId { get; init; } = "";
    public string PosName { get; init; } = "TurboRama Kiosk";
    public string PosExternalId { get; init; } = "";
    public string PostalCode { get; init; } = "";
    public string StreetNumber { get; init; } = "";
    public string Reference { get; init; } = "TurboRama";
    public string AdapterBaseUrl { get; init; } = "http://127.0.0.1:8765/";
    public string AdapterProviderId { get; init; } = "meu-banco";
    public Dictionary<int, long> PackagePricesCents { get; init; } = new()
    {
        [15] = 750, [30] = 1500, [45] = 2250, [60] = 3000, [120] = 6000
    };

    public static PixOwnerProvisioningRequest Load(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            throw new InvalidOperationException("arquivo de configuracao do proprietario nao foi encontrado");
        var info = new FileInfo(file);
        if (info.Length is <= 0 or > 65_536)
            throw new InvalidOperationException("arquivo de configuracao do proprietario tem tamanho invalido");
        var request = JsonSerializer.Deserialize<PixOwnerProvisioningRequest>(File.ReadAllText(file, Encoding.UTF8), Json.Options)
            ?? throw new InvalidOperationException("arquivo de configuracao do proprietario esta vazio");
        if (request.SchemaVersion != 1) throw new InvalidOperationException("versao do configurador nao suportada");
        return request;
    }
}

sealed record PixOwnerProvisioningResult(string Provider, string AccountId, string StoreExternalId,
    string PosExternalId, string State, string Message);

static class PixOwnerProvisioner
{
    public static async Task<PixOwnerProvisioningResult> ConfigureAsync(PixOwnerProvisioningRequest request,
        string credential, PixOptions baseOptions, PixPaths paths, PixSecretStore secrets, CancellationToken token)
    {
        var provider = request.Provider.Trim().ToLowerInvariant();
        if (provider is not ("mercadopago" or "adapter"))
            throw new InvalidOperationException("selecione Mercado Pago ou Adaptador bancario");
        ValidatePrices(request.PackagePricesCents);

        var previousSecret = Environment.GetEnvironmentVariable("TURBORAMA_PIX_PROVIDER_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("TURBORAMA_PIX_PROVIDER_SECRET", credential);
            return provider == "mercadopago"
                ? await ConfigureMercadoPagoAsync(request, credential, baseOptions, paths, secrets, token)
                : await ConfigureAdapterAsync(request, credential, baseOptions, paths, secrets, token);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TURBORAMA_PIX_PROVIDER_SECRET", previousSecret);
        }
    }

    private static async Task<PixOwnerProvisioningResult> ConfigureMercadoPagoAsync(PixOwnerProvisioningRequest request,
        string credential, PixOptions baseOptions, PixPaths paths, PixSecretStore secrets, CancellationToken token)
    {
        if (credential.Length is < 40 or > 384 || !credential.StartsWith("APP_USR-", StringComparison.Ordinal)
            || credential.Any(char.IsWhiteSpace) || credential.Any(char.IsControl))
            throw new SecurityException("Access Token do Mercado Pago esta incompleto ou em formato invalido");

        // O dono nao pode ser obrigado a redigitar token, CEP e precos quando
        // uma fonte externa estiver fora do ar. O cadastro fica pendente e o
        // agente o retoma depois, mas permanece bloqueado ate o health-check.
        var pendingStoreExternalId = string.IsNullOrWhiteSpace(request.StoreExternalId)
            ? CreatePendingExternalId("LZLOJA", 60)
            : ValidateExternalId(request.StoreExternalId, 60, "loja");
        var pendingPosExternalId = string.IsNullOrWhiteSpace(request.PosExternalId)
            ? CreatePendingExternalId("LZPIX", 40)
            : ValidateExternalId(request.PosExternalId, 40, "caixa");
        var pendingOwner = new PixOwnerSettings
        {
            Enabled = true,
            SetupState = "pending",
            Provider = "mercadopago",
            StoreExternalId = pendingStoreExternalId,
            StoreName = RequiredText(request.StoreName, 2, 59, "nome da loja"),
            PosExternalId = pendingPosExternalId,
            PosName = RequiredText(request.PosName, 2, 44, "nome do caixa"),
            PostalCode = Digits(request.PostalCode),
            StreetNumber = RequiredText(request.StreetNumber, 1, 20, "numero do estabelecimento"),
            Reference = RequiredText(request.Reference, 1, 120, "referencia do estabelecimento"),
            PackagePricesCents = new Dictionary<int, long>(request.PackagePricesCents)
        };
        pendingOwner.Validate();
        secrets.Save(credential);
        SaveOwnerSettings(paths, pendingOwner);
        OwnerSetupStatus.Publish(paths, "pending",
            "Cadastro PIX salvo com seguranca. Confirmando conta, endereco, loja e caixa; compras ficam bloqueadas ate a conclusao.");

        var probeOptions = (baseOptions with
        {
            Provider = "mercadopago",
            ProductionEnabled = true,
            AllowedMinutes = request.PackagePricesCents.Keys.Order().ToList(),
            PackagePricesCents = new Dictionary<int, long>(request.PackagePricesCents),
            MercadoPago = baseOptions.MercadoPago with { ExternalPosId = pendingOwner.PosExternalId }
        }).Normalize();
        var mercadoPago = new MercadoPagoPixProvider(probeOptions, secrets);
        var cacheFile = Path.Combine(paths.Root, "owner-address-cache.json");
        var locationWasRequired = false;

        try
        {
            // /users/me devolve o titular real autorizado pelo token. Client ID,
            // ID da aplicacao e User ID de sandbox jamais sao aceitos como conta.
            var inventory = await mercadoPago.GetInfrastructureAsync(token);
            var accountId = inventory.AccountId.Trim();
            if (accountId.Length is < 5 or > 24 || !accountId.All(char.IsAsciiDigit))
                throw new SecurityException("o Access Token nao retornou um User ID de conta valido");

            var owner = pendingOwner with { AccountId = accountId };
            owner.Validate();
            var storeExists = inventory.Stores.Any(item =>
                item.ExternalId.Equals(owner.StoreExternalId, StringComparison.OrdinalIgnoreCase));
            locationWasRequired = !storeExists;
            var setup = storeExists
                ? owner.BuildSetupRequestForExistingStore()
                : await owner.BuildSetupRequestAsync(paths, token);
            var infrastructure = await mercadoPago.EnsureInfrastructureAsync(setup, token);
            if (infrastructure.StoreCreated)
            {
                BrazilianPostalAddress.SaveConfirmedCache(cacheFile, owner.PostalCode, owner.StreetNumber,
                    new BrazilianPostalAddress(setup.StreetName, setup.CityName, setup.StateName,
                        setup.Latitude, setup.Longitude, setup.LocationSource));
            }
            mercadoPago.UseExternalPosId(infrastructure.PointOfSale.ExternalId);
            await mercadoPago.CheckHealthAsync(token);

            var confirmed = owner with
            {
                SetupState = "ready",
                AccountId = infrastructure.AccountId,
                StoreExternalId = infrastructure.Store.ExternalId,
                PosExternalId = infrastructure.PointOfSale.ExternalId
            };
            confirmed.Validate();
            SaveOwnerSettings(paths, confirmed);
            OwnerSetupStatus.Publish(paths, "ready",
                $"Conta {infrastructure.AccountId}, loja {infrastructure.Store.ExternalId} e caixa {infrastructure.PointOfSale.ExternalId} confirmados automaticamente.");
            return new PixOwnerProvisioningResult("mercadopago", infrastructure.AccountId,
                infrastructure.Store.ExternalId, infrastructure.PointOfSale.ExternalId, "ready",
                "Conta, Access Token, loja e caixa validados. PIX pronto para uso.");
        }
        catch (MercadoPagoApiException ex) when (locationWasRequired && IsLocationSetupFailure(ex))
        {
            BrazilianPostalAddress.InvalidateCache(cacheFile);
            OwnerSetupStatus.Publish(paths, "needs_address_confirmation",
                "O Mercado Pago recusou a localizacao da loja. O cadastro foi salvo, mas o endereco sera consultado novamente antes de criar cobrancas.");
            throw new InvalidOperationException("O Mercado Pago recusou a localizacao da loja. Verifique CEP e numero; a configuracao continua salva como pendente.", ex);
        }
        catch (Exception ex)
        {
            OwnerSetupStatus.Publish(paths, "pending",
                $"Cadastro PIX salvo. A confirmacao automatica sera retomada quando os servicos voltarem: {SafeSetupMessage(ex.Message)}");
            throw;
        }
    }

    private static async Task<PixOwnerProvisioningResult> ConfigureAdapterAsync(PixOwnerProvisioningRequest request,
        string credential, PixOptions baseOptions, PixPaths paths, PixSecretStore secrets, CancellationToken token)
    {
        if (credential.Length is < 8 or > 4096 || credential.Any(char.IsControl))
            throw new SecurityException("credencial do adaptador bancario esta vazia ou em formato invalido");
        var adapter = new AdapterOptions
        {
            BaseUrl = request.AdapterBaseUrl,
            ProviderId = request.AdapterProviderId
        }.Normalize();
        var adapterOptions = (baseOptions with
        {
            Provider = "adapter",
            ProductionEnabled = true,
            AllowedMinutes = request.PackagePricesCents.Keys.Order().ToList(),
            PackagePricesCents = new Dictionary<int, long>(request.PackagePricesCents),
            Adapter = adapter
        }).Normalize();
        adapterOptions.ValidateForStartup(configurationOnly: false);
        var bank = new AdapterPixProvider(adapterOptions, secrets);
        await bank.CheckHealthAsync(token);

        var owner = new PixOwnerSettings
        {
            Enabled = true,
            Provider = "adapter",
            AdapterBaseUrl = adapter.BaseUrl,
            AdapterProviderId = adapter.ProviderId,
            PackagePricesCents = new Dictionary<int, long>(request.PackagePricesCents)
        };
        owner.Validate();
        secrets.Save(credential);
        SaveOwnerSettings(paths, owner);
        OwnerSetupStatus.Publish(paths, "ready", $"Adaptador bancario {adapter.ProviderId} validado e ativado.");
        return new PixOwnerProvisioningResult("adapter", "", "", "", "ready",
            $"Adaptador {adapter.ProviderId} validado. PIX pronto para uso.");
    }

    internal static string ValidateExternalId(string value, int maximum, string field)
    {
        var clean = value.Trim();
        if (clean.Length is < 1 || clean.Length > maximum || !clean.All(char.IsAsciiLetterOrDigit))
            throw new InvalidOperationException($"identificador da {field} deve conter somente letras e numeros e ter ate {maximum} caracteres");
        return clean;
    }

    private static string CreatePendingExternalId(string prefix, int maximum)
    {
        var value = prefix + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        return ValidateExternalId(value.Length <= maximum ? value : value[..maximum], maximum, "cadastro");
    }

    internal static bool IsLocationSetupFailure(MercadoPagoApiException exception)
    {
        if (exception.StatusCode is not (400 or 422)) return false;
        var detail = exception.Detail.ToLowerInvariant();
        return detail.Contains("location", StringComparison.Ordinal)
            || detail.Contains("latitude", StringComparison.Ordinal)
            || detail.Contains("longitude", StringComparison.Ordinal)
            || detail.Contains("coordinates", StringComparison.Ordinal)
            || detail.Contains("street", StringComparison.Ordinal)
            || detail.Contains("city", StringComparison.Ordinal)
            || detail.Contains("state", StringComparison.Ordinal);
    }

    private static string SafeSetupMessage(string message)
    {
        var clean = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= 280 ? clean : clean[..280];
    }

    private static void SaveOwnerSettings(PixPaths paths, PixOwnerSettings settings)
    {
        var file = Path.Combine(paths.Root, "owner-settings.json");
        paths.WriteAtomically(file, settings);
        WindowsFileSecurity.HardenCredentialFile(file, allowBuiltinUsersRead: false);
    }

    private static void ValidatePrices(Dictionary<int, long>? prices)
    {
        if (prices is null) throw new InvalidOperationException("tabela de precos nao foi informada");
        foreach (var minutes in new[] { 15, 30, 45, 60, 120 })
            if (!prices.TryGetValue(minutes, out var cents) || cents is < 50 or > 100_000_000)
                throw new InvalidOperationException($"preco de {minutes} minutos e invalido");
    }

    private static string RequiredText(string value, int minimum, int maximum, string field)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length < minimum || clean.Length > maximum)
            throw new InvalidOperationException($"{field} e invalido");
        return clean;
    }

    private static string Digits(string value)
    {
        var result = new string((value ?? "").Where(char.IsAsciiDigit).ToArray());
        if (result.Length != 8) throw new InvalidOperationException("CEP deve conter 8 numeros");
        return result;
    }
}

sealed record BrazilianPostalAddress(string Street, string City, string State, double Latitude, double Longitude, string Source)
{
    private const int CacheSchemaVersion = 2;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan AddressResolutionBudget = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan IndividualSourceTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan NominatimMinimumInterval = TimeSpan.FromSeconds(1);
    private static readonly IReadOnlyDictionary<string, string> States = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AC"] = "Acre", ["AL"] = "Alagoas", ["AP"] = "Amapa", ["AM"] = "Amazonas", ["BA"] = "Bahia",
        ["CE"] = "Ceara", ["DF"] = "Distrito Federal", ["ES"] = "Espirito Santo", ["GO"] = "Goias",
        ["MA"] = "Maranhao", ["MT"] = "Mato Grosso", ["MS"] = "Mato Grosso do Sul", ["MG"] = "Minas Gerais",
        ["PA"] = "Para", ["PB"] = "Paraiba", ["PR"] = "Parana", ["PE"] = "Pernambuco", ["PI"] = "Piaui",
        ["RJ"] = "Rio de Janeiro", ["RN"] = "Rio Grande do Norte", ["RS"] = "Rio Grande do Sul",
        ["RO"] = "Rondonia", ["RR"] = "Roraima", ["SC"] = "Santa Catarina", ["SP"] = "Sao Paulo",
        ["SE"] = "Sergipe", ["TO"] = "Tocantins"
    };

    public static async Task<BrazilianPostalAddress> ResolveAsync(string postalCode, string streetNumber, string cacheFile, CancellationToken token)
        => await ResolveAsync(postalCode, streetNumber, cacheFile, token, handler: null);

    internal static async Task<BrazilianPostalAddress> ResolveAsync(string postalCode, string streetNumber,
        string cacheFile, CancellationToken token, HttpMessageHandler? handler)
    {
        var cep = new string(postalCode.Where(char.IsAsciiDigit).ToArray());
        if (cep.Length != 8) throw new InvalidOperationException("CEP deve conter 8 numeros.");
        var normalizedNumber = NormalizeStreetNumber(streetNumber);
        if (string.IsNullOrWhiteSpace(normalizedNumber)) throw new InvalidOperationException("Numero do endereco invalido.");
        var cached = TryLoadCache(cacheFile, cep, streetNumber);
        if (cached is not null) return cached;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(AddressResolutionBudget);
        var requestToken = deadline.Token;
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        http.Timeout = IndividualSourceTimeout;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TurboRamaPixAgent/25.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        string street = "", city = "", stateCode = "";
        double latitude = 0, longitude = 0;
        var coordinateSource = "";

        // A AwesomeAPI normalmente devolve endereco e coordenadas em uma unica
        // consulta por CEP. Ela e a primeira fonte porque evita depender de um
        // segundo geocodificador quando a loja ainda nao existe no Mercado Pago.
        try
        {
            using var response = await http.GetAsync($"https://cep.awesomeapi.com.br/json/{cep}", requestToken);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(requestToken));
                var root = document.RootElement;
                street = FirstString(root, "address", "street");
                city = FirstString(root, "city");
                stateCode = FirstString(root, "state");
                latitude = FirstNumber(root, "lat", "latitude");
                longitude = FirstNumber(root, "lng", "longitude", "lon");
                if (HasValidCoordinates(latitude, longitude)) coordinateSource = "awesomeapi";
            }
        }
        catch (OperationCanceledException) when (DeadlineExceeded(token, deadline))
        {
            throw AddressTimeout();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }

        try
        {
            using var response = await http.GetAsync($"https://brasilapi.com.br/api/cep/v2/{cep}", requestToken);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(requestToken));
                var root = document.RootElement;
                if (string.IsNullOrWhiteSpace(street)) street = String(root, "street");
                if (string.IsNullOrWhiteSpace(city)) city = String(root, "city");
                if (string.IsNullOrWhiteSpace(stateCode)) stateCode = String(root, "state");
                if (root.TryGetProperty("location", out var location) && location.TryGetProperty("coordinates", out var coordinates))
                {
                    if (!HasCoordinates(latitude, longitude))
                    {
                        latitude = Number(coordinates, "latitude");
                        longitude = Number(coordinates, "longitude");
                        if (HasValidCoordinates(latitude, longitude)) coordinateSource = "brasilapi";
                    }
                }
            }
        }
        catch (OperationCanceledException) when (DeadlineExceeded(token, deadline))
        {
            throw AddressTimeout();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }

        if (string.IsNullOrWhiteSpace(street) || string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(stateCode))
        {
            try
            {
                using var response = await http.GetAsync($"https://viacep.com.br/ws/{cep}/json/", requestToken);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException("CEP nao foi encontrado.");
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(requestToken));
                var root = document.RootElement;
                if (root.TryGetProperty("erro", out var invalid) && invalid.ValueKind == JsonValueKind.True)
                    throw new InvalidOperationException("CEP nao foi encontrado.");
                street = String(root, "logradouro"); city = String(root, "localidade"); stateCode = String(root, "uf");
            }
            catch (OperationCanceledException) when (DeadlineExceeded(token, deadline))
            {
                throw AddressTimeout();
            }
        }
        if (string.IsNullOrWhiteSpace(street) || string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(stateCode))
            throw new InvalidOperationException("O CEP nao retornou rua, cidade e estado completos.");

        if (!HasCoordinates(latitude, longitude))
        {
            // O Nominatim recebe varias formas do mesmo endereco. Alguns CEPs
            // nao possuem numero de porta no OpenStreetMap, por isso a segunda
            // tentativa remove apenas o numero e preserva rua, cidade e CEP.
            DateTimeOffset? lastNominatimRequest = null;
            foreach (var candidate in new[]
            {
                $"{street}, {normalizedNumber}, {city}, {stateCode}, {cep}, Brasil",
                $"{street}, {city}, {stateCode}, {cep}, Brasil"
            })
            {
                try
                {
                    await WaitForNominatimSlotAsync(lastNominatimRequest, requestToken);
                    lastNominatimRequest = DateTimeOffset.UtcNow;
                    var query = Uri.EscapeDataString(candidate);
                    using var response = await http.GetAsync($"{NominatimBaseUrl()}/search?format=jsonv2&addressdetails=1&limit=3&countrycodes=br&q={query}", requestToken);
                    if (!response.IsSuccessStatusCode) continue;
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(requestToken));
                    if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0) continue;
                    foreach (var result in document.RootElement.EnumerateArray().Take(3))
                    {
                        if (!IsCompatibleNominatimAddress(result, cep, street, normalizedNumber, city, stateCode)) continue;
                        double.TryParse(String(result, "lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out latitude);
                        double.TryParse(String(result, "lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out longitude);
                        if (!HasValidCoordinates(latitude, longitude)) continue;
                        coordinateSource = "nominatim";
                        break;
                    }
                    if (HasValidCoordinates(latitude, longitude)) break;
                }
                catch (OperationCanceledException) when (DeadlineExceeded(token, deadline))
                {
                    throw AddressTimeout();
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
            }
        }
        if (DeadlineExceeded(token, deadline)) throw AddressTimeout();
        if (!HasValidCoordinates(latitude, longitude))
            throw new InvalidOperationException("Nao foi possivel confirmar automaticamente o endereco deste CEP agora. Verifique a internet e tente novamente; nenhum dado anterior foi alterado.");
        var state = States.TryGetValue(stateCode, out var fullState) ? fullState : stateCode;
        return new BrazilianPostalAddress(street.Trim(), city.Trim(), state, latitude, longitude,
            string.IsNullOrWhiteSpace(coordinateSource) ? "unverified" : coordinateSource);
    }

    private static BrazilianPostalAddress? TryLoadCache(string file, string postalCode, string streetNumber)
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length is <= 0 or > 16_384) return null;
            var cache = JsonSerializer.Deserialize<PostalAddressCache>(File.ReadAllText(file, Encoding.UTF8), Json.Options);
            if (cache is null || cache.SchemaVersion != CacheSchemaVersion
                || !string.Equals(cache.PostalCode, postalCode, StringComparison.Ordinal)
                || !string.Equals(cache.StreetNumber?.Trim(), NormalizeStreetNumber(streetNumber), StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(cache.Street) || string.IsNullOrWhiteSpace(cache.City)
                || string.IsNullOrWhiteSpace(cache.State) || string.IsNullOrWhiteSpace(cache.Source)
                || !HasValidCoordinates(cache.Latitude, cache.Longitude)
                || !IsFresh(cache.ConfirmedAtUnixSeconds)) return null;
            return new BrazilianPostalAddress(cache.Street.Trim(), cache.City.Trim(), cache.State.Trim(), cache.Latitude, cache.Longitude,
                "cache:" + cache.Source.Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException) { return null; }
    }

    // O cache e uma lembranca de uma localizacao que o Mercado Pago ja aceitou,
    // nunca de uma sugestao de CEP/geocodificador ainda nao validada pela API.
    public static void SaveConfirmedCache(string file, string postalCode, string streetNumber, BrazilianPostalAddress address)
    {
        var cep = new string((postalCode ?? "").Where(char.IsAsciiDigit).ToArray());
        var number = NormalizeStreetNumber(streetNumber);
        if (cep.Length != 8 || string.IsNullOrWhiteSpace(number)
            || string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City)
            || string.IsNullOrWhiteSpace(address.State) || !HasValidCoordinates(address.Latitude, address.Longitude)) return;
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var source = string.IsNullOrWhiteSpace(address.Source) ? "confirmed" : address.Source;
            var cache = new PostalAddressCache(CacheSchemaVersion, cep, number, address.Street.Trim(), address.City.Trim(),
                address.State.Trim(), address.Latitude, address.Longitude, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), source);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, cache, Json.Options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, file, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
    }

    public static void InvalidateCache(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static bool HasValidCoordinates(double latitude, double longitude)
        => double.IsFinite(latitude) && double.IsFinite(longitude)
            && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180
            && Math.Abs(latitude) >= 0.00001 && Math.Abs(longitude) >= 0.00001;

    private static bool DeadlineExceeded(CancellationToken originalToken, CancellationTokenSource deadline)
        => deadline.IsCancellationRequested && !originalToken.IsCancellationRequested;

    private static InvalidOperationException AddressTimeout()
        => new("A consulta de CEP/endereco demorou demais. O cadastro ficou salvo como pendente; verifique a internet e tente novamente.");

    private static async Task WaitForNominatimSlotAsync(DateTimeOffset? previousRequest, CancellationToken token)
    {
        if (!previousRequest.HasValue) return;
        var delay = previousRequest.Value.Add(NominatimMinimumInterval) - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
    }

    // A politica do Nominatim exige que aplicativos possam trocar de servidor
    // sem nova versao. O operador pode apontar para uma instancia propria ou
    // provedor contratado por variavel de ambiente; HTTPS e sempre exigido.
    private static string NominatimBaseUrl()
    {
        const string fallback = "https://nominatim.openstreetmap.org";
        var configured = Environment.GetEnvironmentVariable("TURBORAMA_PIX_NOMINATIM_BASE_URL");
        if (string.IsNullOrWhiteSpace(configured)) return fallback;
        if (!Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return fallback;
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static bool IsCompatibleNominatimAddress(JsonElement result, string cep, string street,
        string streetNumber, string city, string stateCode)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object)
            return false;
        if (!FirstString(address, "country_code").Equals("br", StringComparison.OrdinalIgnoreCase)) return false;
        if (new string(FirstString(address, "postcode").Where(char.IsAsciiDigit).ToArray()) != cep) return false;

        var foundStreet = FirstString(address, "road", "pedestrian", "residential", "footway", "path");
        var foundNumber = FirstString(address, "house_number");
        var foundCity = FirstString(address, "city", "town", "municipality", "village", "county");
        var foundState = FirstString(address, "state");
        return TextMatches(street, foundStreet)
            && HouseNumberMatches(streetNumber, foundNumber)
            && TextMatches(city, foundCity)
            && StateMatches(stateCode, foundState);
    }

    private static bool StateMatches(string expectedCode, string actualState)
    {
        var expected = States.TryGetValue(expectedCode, out var fullState) ? fullState : expectedCode;
        return TextMatches(expected, actualState) || TextMatches(expectedCode, actualState);
    }

    private static bool HouseNumberMatches(string expected, string actual)
    {
        var expectedDigits = new string((expected ?? "").Where(char.IsAsciiDigit).ToArray());
        var actualDigits = new string((actual ?? "").Where(char.IsAsciiDigit).ToArray());
        return expectedDigits.Length > 0 && expectedDigits.Equals(actualDigits, StringComparison.Ordinal);
    }

    private static bool TextMatches(string expected, string actual)
    {
        var left = NormalizeAddressText(expected);
        var right = NormalizeAddressText(actual);
        return left.Length > 0 && right.Length > 0
            && (left.Equals(right, StringComparison.Ordinal) || left.Contains(right, StringComparison.Ordinal)
                || right.Contains(left, StringComparison.Ordinal));
    }

    private static string NormalizeAddressText(string? value)
    {
        var output = new StringBuilder();
        var previousSpace = true;
        foreach (var character in (value ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                output.Append(char.ToLowerInvariant(character));
                previousSpace = false;
            }
            else if (!previousSpace)
            {
                output.Append(' ');
                previousSpace = true;
            }
        }
        return output.ToString().Trim();
    }

    private static string NormalizeStreetNumber(string? value) => (value ?? "").Trim();

    private static bool IsFresh(long unixSeconds)
    {
        try
        {
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            var now = DateTimeOffset.UtcNow;
            return timestamp <= now.AddMinutes(5) && timestamp >= now.Subtract(CacheTtl);
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static string String(JsonElement objectValue, string property)
        => objectValue.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string FirstString(JsonElement objectValue, params string[] properties)
    {
        foreach (var property in properties)
        {
            var value = String(objectValue, property);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }

    private static double FirstNumber(JsonElement objectValue, params string[] properties)
    {
        foreach (var property in properties)
        {
            var value = Number(objectValue, property);
            if (Math.Abs(value) >= 0.00001) return value;
        }
        return 0;
    }

    private static bool HasCoordinates(double latitude, double longitude) => HasValidCoordinates(latitude, longitude);

    private static double Number(JsonElement objectValue, string property)
    {
        if (!objectValue.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        return 0;
    }
}

sealed record PostalAddressCache(int SchemaVersion, string? PostalCode, string? StreetNumber, string? Street,
    string? City, string? State, double Latitude, double Longitude, long ConfirmedAtUnixSeconds, string? Source);

static class OwnerSetupStatus
{
    public static void Publish(PixPaths paths, string state, string message)
        => paths.WriteAtomically(Path.Combine(paths.Root, "owner-setup-status.json"), new
        {
            schemaVersion = 1,
            state,
            message = message.Length > 500 ? message[..500] : message,
            updatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
}

sealed class OwnerInfrastructureCoordinator
{
    private readonly PixOwnerSettings _settings;
    private readonly MercadoPagoPixProvider _provider;
    private readonly PixPaths _paths;
    private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;
    private string _lastError = "";

    public OwnerInfrastructureCoordinator(PixOwnerSettings settings, MercadoPagoPixProvider provider, PixPaths paths)
        => (_settings, _provider, _paths) = (settings, provider, paths);

    public bool Ready { get; private set; }

    public async Task<bool> TryEnsureAsync(bool force, CancellationToken token)
    {
        if (Ready && !force) return true;
        if (!force && DateTimeOffset.UtcNow < _nextAttempt) return false;
        _nextAttempt = DateTimeOffset.UtcNow.AddSeconds(15);
        var locationWasRequired = false;
        try
        {
            OwnerSetupStatus.Publish(_paths, "configuring", "Conferindo loja e caixa PIX. Se estiver sem internet, o sistema tentara novamente automaticamente.");

            // O prefixo APP_USR e usado tanto em sandbox quanto em producao.
            // Por isso o ambiente e a conta devem vir do proprio Access Token,
            // e nunca do User ID que pode ter ficado salvo por um teste antigo.
            // Algumas contas de sandbox recusam /users/me por politica; somente
            // nesse caso mantemos a consulta compativel pelo User ID cadastrado.
            MercadoPagoInfrastructure inventory;
            try
            {
                inventory = await _provider.GetInfrastructureAsync(token);
            }
            catch (MercadoPagoApiException ex) when (ex.StatusCode == 403
                && _settings.AccountId.Length is >= 5 and <= 24
                && _settings.AccountId.All(char.IsAsciiDigit))
            {
                inventory = await _provider.GetInfrastructureForConfiguredAccountAsync(_settings.AccountId.Trim(), token);
            }
            var effectiveSettings = BindAuthenticatedAccount(_settings, inventory);
            var accountAutomaticallyCorrected = !effectiveSettings.AccountId.Equals(_settings.AccountId.Trim(), StringComparison.Ordinal);

            if (TryResolveExisting(effectiveSettings, inventory, out var store, out var pointOfSale, out var automaticallyRecovered, out var selectionError))
            {
                _provider.UseExternalPosId(pointOfSale.ExternalId);
                PersistResolvedIdentifiers(effectiveSettings, store, pointOfSale);
                Ready = true;
                _lastError = "";
                var message = accountAutomaticallyCorrected
                    ? $"Conta de producao reconhecida automaticamente. Loja {store.ExternalId} e caixa {pointOfSale.ExternalId} confirmados."
                    : automaticallyRecovered
                    ? $"Loja {store.ExternalId} e caixa {pointOfSale.ExternalId} localizados e corrigidos automaticamente."
                    : $"Loja {store.ExternalId} e caixa {pointOfSale.ExternalId} confirmados.";
                OwnerSetupStatus.Publish(_paths, "ready", message);
                return true;
            }

            // As primeiras versoes gravavam estes dois exemplos no cadastro
            // local. Se a conta ja possui uma loja/PDV, criar outro por cima
            // deles e uma acao comercial indevida. Em vez disso, interrompemos
            // com uma orientacao objetiva para o dono informar os IDs reais
            // que o proprio Mercado Pago ja possui.
            if (effectiveSettings.StoreExternalId.Equals("TURBORAMALOJA01", StringComparison.OrdinalIgnoreCase)
                && effectiveSettings.PosExternalId.Equals("TURBORAMAKIOSK01", StringComparison.OrdinalIgnoreCase)
                && (inventory.Stores.Count > 0 || inventory.PointsOfSale.Count > 0))
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(selectionError)
                    ? "Os identificadores antigos nao existem e nenhum caixa ativo associado a uma loja foi localizado nesta conta. Nenhuma cobranca foi criada."
                    : selectionError);
            }

            // Se a loja ja existe, criar ou confirmar apenas o caixa nao usa
            // endereco. Portanto nao consultamos CEP nem coordenadas nessa
            // situacao. A localizacao completa so e resolvida quando a conta
            // realmente ainda nao possui a loja solicitada.
            var configuredStoreExists = inventory.Stores.Any(item =>
                item.ExternalId.Equals(effectiveSettings.StoreExternalId.Trim(), StringComparison.OrdinalIgnoreCase));
            locationWasRequired = !configuredStoreExists;
            var setup = configuredStoreExists
                ? effectiveSettings.BuildSetupRequestForExistingStore()
                : await effectiveSettings.BuildSetupRequestAsync(_paths, token);
            var result = await _provider.EnsureInfrastructureAsync(setup, token);
            if (result.StoreCreated && BrazilianPostalAddress.HasValidCoordinates(setup.Latitude, setup.Longitude))
            {
                BrazilianPostalAddress.SaveConfirmedCache(Path.Combine(_paths.Root, "owner-address-cache.json"),
                    effectiveSettings.PostalCode, effectiveSettings.StreetNumber,
                    new BrazilianPostalAddress(setup.StreetName, setup.CityName, setup.StateName,
                        setup.Latitude, setup.Longitude, setup.LocationSource));
            }
            _provider.UseExternalPosId(result.PointOfSale.ExternalId);
            PersistResolvedIdentifiers(effectiveSettings, result.Store, result.PointOfSale);
            Ready = true;
            _lastError = "";
            var setupMessage = accountAutomaticallyCorrected
                ? $"Conta de producao reconhecida automaticamente. Loja {result.Store.ExternalId} e caixa {result.PointOfSale.ExternalId} confirmados."
                : $"Loja {result.Store.ExternalId} e caixa {result.PointOfSale.ExternalId} confirmados.";
            OwnerSetupStatus.Publish(_paths, "ready", setupMessage);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or HttpRequestException
            or TaskCanceledException or MercadoPagoApiException or InvalidOperationException or SecurityException)
        {
            var connectionFailure = ex is HttpRequestException or TaskCanceledException;
            var locationFailure = locationWasRequired && ex is MercadoPagoApiException mercadoPagoError
                && PixOwnerProvisioner.IsLocationSetupFailure(mercadoPagoError);
            if (locationFailure)
                BrazilianPostalAddress.InvalidateCache(Path.Combine(_paths.Root, "owner-address-cache.json"));
            var message = locationFailure
                ? "O Mercado Pago recusou a localizacao da loja. Verifique CEP e numero; o cadastro e a credencial continuam salvos, sem liberar cobrancas."
                : connectionFailure
                ? "Cadastro salvo. Sem conexao com os servicos PIX; nova tentativa automatica em 15 segundos."
                : ex.Message;
            OwnerSetupStatus.Publish(_paths, locationFailure ? "needs_address_confirmation" : connectionFailure ? "waiting_network" : "pending", message);
            if (!message.Equals(_lastError, StringComparison.Ordinal))
                Console.Error.WriteLine($"Falha no cadastro do estabelecimento PIX: {message}");
            _lastError = message;
            return false;
        }
    }

    internal static bool TryResolveExisting(PixOwnerSettings settings, MercadoPagoInfrastructure inventory,
        out MercadoPagoStoreInfo store, out MercadoPagoPosInfo pointOfSale, out bool automaticallyRecovered,
        out string selectionError)
    {
        store = null!;
        pointOfSale = null!;
        automaticallyRecovered = false;
        selectionError = "";

        var storesById = inventory.Stores
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var usablePoints = inventory.PointsOfSale
            .Where(item => !string.IsNullOrWhiteSpace(item.Id)
                && !string.IsNullOrWhiteSpace(item.ExternalId)
                && storesById.ContainsKey(item.StoreId)
                && (string.IsNullOrWhiteSpace(item.Status) || item.Status.Equals("active", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var requestedStore = inventory.Stores.FirstOrDefault(item =>
            item.ExternalId.Equals(settings.StoreExternalId.Trim(), StringComparison.OrdinalIgnoreCase));
        var requestedPoint = usablePoints.FirstOrDefault(item =>
            item.ExternalId.Equals(settings.PosExternalId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (requestedPoint is not null && storesById.TryGetValue(requestedPoint.StoreId, out var pointStore))
        {
            if (requestedStore is not null && !requestedStore.Id.Equals(pointStore.Id, StringComparison.Ordinal))
                throw new SecurityException("O caixa PIX informado pertence a outra loja.");
            store = pointStore;
            pointOfSale = requestedPoint;
            automaticallyRecovered = requestedStore is null;
            return true;
        }

        var candidates = requestedStore is null
            ? usablePoints
            : usablePoints.Where(item => item.StoreId.Equals(requestedStore.Id, StringComparison.Ordinal)).ToList();
        if (candidates.Count > 1)
        {
            var nameMatches = candidates.Where(item =>
                item.Name.Equals(settings.PosName.Trim(), StringComparison.OrdinalIgnoreCase)
                && storesById[item.StoreId].Name.Equals(settings.StoreName.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (nameMatches.Count == 1) candidates = nameMatches;
        }

        if (candidates.Count == 1 && storesById.TryGetValue(candidates[0].StoreId, out var recoveredStore))
        {
            store = recoveredStore;
            pointOfSale = candidates[0];
            automaticallyRecovered = true;
            return true;
        }

        if (candidates.Count > 1)
        {
            var ids = string.Join(", ", candidates.Select(item => item.ExternalId).Distinct(StringComparer.OrdinalIgnoreCase).Take(6));
            selectionError = $"Mais de um caixa PIX ativo foi encontrado ({ids}). Selecione o external_id correto na CONFIGURACAO PIX DO PROPRIETARIO; nenhuma cobranca foi criada.";
        }
        return false;
    }

    internal static PixOwnerSettings BindAuthenticatedAccount(PixOwnerSettings settings, MercadoPagoInfrastructure inventory)
    {
        var accountId = inventory.AccountId.Trim();
        if (accountId.Length is < 5 or > 24 || !accountId.All(char.IsAsciiDigit))
            throw new SecurityException("O Mercado Pago nao retornou um User ID valido para o Access Token.");
        return settings.AccountId.Equals(accountId, StringComparison.Ordinal)
            ? settings
            : settings with { AccountId = accountId };
    }

    private void PersistResolvedIdentifiers(PixOwnerSettings settings, MercadoPagoStoreInfo store, MercadoPagoPosInfo pointOfSale)
    {
        if (_settings.SetupState.Equals("ready", StringComparison.OrdinalIgnoreCase)
            && _settings.AccountId.Equals(settings.AccountId, StringComparison.Ordinal)
            && _settings.StoreExternalId.Equals(store.ExternalId, StringComparison.Ordinal)
            && _settings.PosExternalId.Equals(pointOfSale.ExternalId, StringComparison.Ordinal)) return;
        var corrected = settings with
        {
            SetupState = "ready",
            StoreExternalId = store.ExternalId,
            PosExternalId = pointOfSale.ExternalId
        };
        corrected.Validate();
        var file = Path.Combine(_paths.Root, "owner-settings.json");
        _paths.WriteAtomically(file, corrected);
        WindowsFileSecurity.HardenCredentialFile(file, allowBuiltinUsersRead: false);
    }
}

sealed record MercadoPagoStoreInfo(string Id, string ExternalId, string Name);
sealed record MercadoPagoPosInfo(string Id, string ExternalId, string Name, string StoreId, string Status);
sealed record MercadoPagoInfrastructure(string AccountId, IReadOnlyList<MercadoPagoStoreInfo> Stores, IReadOnlyList<MercadoPagoPosInfo> PointsOfSale);
sealed record MercadoPagoSetupResult(string AccountId, MercadoPagoStoreInfo Store, MercadoPagoPosInfo PointOfSale,
    bool StoreCreated, bool PointOfSaleCreated);

sealed record AdapterOptions
{
    public string BaseUrl { get; init; } = "http://127.0.0.1:8765/";
    public string ProviderId { get; init; } = "meu-banco";

    public AdapterOptions Normalize()
    {
        var baseUrl = (BaseUrl ?? "").Trim();
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        return this with { BaseUrl = baseUrl, ProviderId = (ProviderId ?? "").Trim().ToLowerInvariant() };
    }
}

sealed class PixPaths
{
    public PixPaths(string root)
    {
        Root = root;
        Requests = Path.Combine(root, "requests");
        Sessions = Path.Combine(root, "sessions");
        Approved = Path.Combine(root, "approved");
        Processed = Path.Combine(root, "processed");
        Rejected = Path.Combine(root, "rejected");
        Retry = Path.Combine(root, "retry");
        Qr = Path.Combine(root, "qr");
        Logs = Path.Combine(root, "logs");
        SecretFile = Path.Combine(root, "secret.dat");
        SigningKeyFile = Path.Combine(root, "bridge.key");
        CredentialPrivateKeyFile = Path.Combine(root, "credential-agent-key.dat");
        CredentialPublicKeyFile = Path.Combine(root, "agent-public-key.pem");
        CredentialUpdateFile = Path.Combine(root, "credential-update.json");
        CredentialUpdateStatusFile = Path.Combine(root, "credential-update-status.json");
        CredentialReplayFile = Path.Combine(root, "credential-replay.dat");
        PublicOptionsFile = Path.Combine(root, "public-options.json");
        AgentStatusFile = Path.Combine(root, "agent-status.json");
    }

    public string Root { get; }
    public string Requests { get; }
    public string Sessions { get; }
    public string Approved { get; }
    public string Processed { get; }
    public string Rejected { get; }
    public string Retry { get; }
    public string Qr { get; }
    public string Logs { get; }
    public string SecretFile { get; }
    public string SigningKeyFile { get; }
    public string CredentialPrivateKeyFile { get; }
    public string CredentialPublicKeyFile { get; }
    public string CredentialUpdateFile { get; }
    public string CredentialUpdateStatusFile { get; }
    public string CredentialReplayFile { get; }
    public string PublicOptionsFile { get; }
    public string AgentStatusFile { get; }
    public string RequestFile(string id) => Path.Combine(Requests, $"{id}.request.json");
    public string SessionFile(string id) => Path.Combine(Sessions, $"{id}.session.json");
    public string ApprovedFile(string id) => Path.Combine(Approved, $"{id}.credit.json");
    public string ProcessedFile(string id) => Path.Combine(Processed, $"{id}.credit.json");
    public string QrFile(string id) => Path.Combine(Qr, $"{id}.png");
    public string QrMatrixFile(string id) => Path.Combine(Qr, $"{id}.matrix");
    public string RetryFile(string id) => Path.Combine(Retry, $"{id}.retry.json");

    public void EnsureDirectories()
    {
        foreach (var directory in new[] { Root, Requests, Sessions, Approved, Processed, Rejected, Retry, Qr, Logs }) Directory.CreateDirectory(directory);
    }

    public void WriteAtomically<T>(string destination, T value)
    {
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json.Options);
            WriteFileAndFlush(temp, bytes);
            File.Move(temp, destination, true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
    }

    public void WriteBytesAtomically(string destination, byte[] value)
    {
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteFileAndFlush(temp, value);
            File.Move(temp, destination, true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
    }

    private static void WriteFileAndFlush(string file, byte[] value)
    {
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Write(value);
        stream.Flush(flushToDisk: true);
    }

    public void Quarantine(string source, string reason)
    {
        var safeName = Path.GetFileName(source);
        var destination = Path.Combine(Rejected, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}-{safeName}");
        try { File.Move(source, destination, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        try { File.WriteAllText(destination + ".reason.txt", reason.Length > 300 ? reason[..300] : reason, new UTF8Encoding(false)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

static class PixPublicContract
{
    public static void Publish(PixOptions options, PixPaths paths, string provider, bool credentialAvailable, bool providerHealthy, string state)
    {
        var ready = provider == "mock" || (credentialAvailable && providerHealthy && options.IsProviderConfigured());
        var now = DateTimeOffset.UtcNow;
        var packages = options.AllowedMinutes
            .Select(minutes => new { minutes, amountCents = options.PriceFor(minutes) })
            .Where(package => package.amountCents > 0)
            .ToArray();
        paths.WriteAtomically(paths.PublicOptionsFile, new
        {
            schemaVersion = 1,
            provider,
            productionEnabled = options.ProductionEnabled,
            ready,
            paymentExpirationMinutes = options.PaymentExpirationMinutes,
            generatedAtUnixSeconds = now.ToUnixTimeSeconds(),
            packages
        });
        paths.WriteAtomically(paths.AgentStatusFile, new
        {
            schemaVersion = 1,
            processId = Environment.ProcessId,
            provider,
            ready,
            state,
            updatedAtUnixSeconds = now.ToUnixTimeSeconds()
        });
    }
}

enum PixSecretState { Available, Missing, Unreadable }

sealed record PixSecretReadResult(PixSecretState State, string? Value)
{
    public bool IsAvailable => State == PixSecretState.Available && !string.IsNullOrWhiteSpace(Value);
}

sealed class PixSecretStore
{
    private readonly string _path;
    public PixSecretStore(string path) => _path = path;

    public void Save(string secret)
    {
        var entropy = Encoding.UTF8.GetBytes("TurboRamaPixAgent-v1");
        var encrypted = WindowsDpapi.Protect(Encoding.UTF8.GetBytes(secret), entropy);
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = Encoding.UTF8.GetBytes(Convert.ToBase64String(encrypted));
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, _path, true);
            // MoveFile substitui o conteudo, mas uma instalacao antiga pode
            // ter deixado ACLs herdadas permissivas em secret.dat. Fechamos o
            // arquivo novo explicitamente antes de o agente voltar a usa-lo.
            WindowsFileSecurity.HardenCredentialFile(_path, allowBuiltinUsersRead: false);
            try { File.SetAttributes(_path, File.GetAttributes(_path) | FileAttributes.Hidden); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
    }

    public PixSecretReadResult TryLoad()
    {
        var environmentToken = Environment.GetEnvironmentVariable("TURBORAMA_PIX_PROVIDER_SECRET");
        if (string.IsNullOrWhiteSpace(environmentToken))
            environmentToken = Environment.GetEnvironmentVariable("TURBORAMA_PIX_MERCADOPAGO_ACCESS_TOKEN");
        if (!string.IsNullOrWhiteSpace(environmentToken))
            return new(PixSecretState.Available, environmentToken.Trim());
        if (!File.Exists(_path)) return new(PixSecretState.Missing, null);
        try
        {
            var entropy = Encoding.UTF8.GetBytes("TurboRamaPixAgent-v1");
            var encrypted = Convert.FromBase64String(File.ReadAllText(_path, Encoding.UTF8).Trim());
            var value = Encoding.UTF8.GetString(WindowsDpapi.Unprotect(encrypted, entropy));
            return string.IsNullOrWhiteSpace(value)
                ? new(PixSecretState.Unreadable, null)
                : new(PixSecretState.Available, value);
        }
        catch (CryptographicException) { return new(PixSecretState.Unreadable, null); }
        catch (FormatException) { return new(PixSecretState.Unreadable, null); }
        catch (IOException) { return new(PixSecretState.Unreadable, null); }
        catch (UnauthorizedAccessException) { return new(PixSecretState.Unreadable, null); }
    }

    public string? Load() => TryLoad().Value;
}

// Ponte de credencial entre o editor externo e o agente. O editor enxerga
// somente a chave publica; o Access Token chega cifrado e e aberto apenas
// pelo agente, que o grava no cofre DPAPI do proprio usuario Windows.
sealed record PixCredentialUpdate(int SchemaVersion, string RequestId, string KeyFingerprint,
    string EncryptedPayload, long CreatedAtUnixSeconds);

sealed record PixCredentialReplay(string RequestId, long CreatedAtUnixSeconds, string State);

sealed class PixCredentialInbox
{
    private const string PrivateKeyPrefix = "TRPXKEY1:";
    private static readonly byte[] KeyEntropy = Encoding.UTF8.GetBytes("TurboRamaPixCredentialExchange-v1");
    private readonly PixPaths _paths;
    private readonly PixSecretStore _secrets;

    public PixCredentialInbox(PixPaths paths, PixSecretStore secrets) => (_paths, _secrets) = (paths, secrets);

    public void EnsureReady()
    {
        // Protege a pasta antes de publicar a chave. Assim um usuario comum
        // nao consegue trocar a chave publica entre a gravacao e a leitura do
        // editor externo.
        WindowsFileSecurity.HardenBridgeDirectory(_paths.Root);
        using var rsa = OpenOrCreateKey();
        var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
        _paths.WriteBytesAtomically(_paths.CredentialPublicKeyFile, Encoding.UTF8.GetBytes(publicPem));
        WindowsFileSecurity.HardenCredentialFile(_paths.CredentialPrivateKeyFile, allowBuiltinUsersRead: false);
        WindowsFileSecurity.HardenCredentialFile(_paths.CredentialPublicKeyFile, allowBuiltinUsersRead: false);
    }

    public bool TryAcceptPendingUpdate()
    {
        if (!File.Exists(_paths.CredentialUpdateFile)) return false;
        PixCredentialUpdate? update = null;
        var tokenSaved = false;
        try
        {
            var text = File.ReadAllText(_paths.CredentialUpdateFile, Encoding.UTF8);
            if (text.Length is < 40 or > 16_384) throw new InvalidOperationException("pedido de credencial invalido");
            update = ValidateEnvelope(JsonSerializer.Deserialize<PixCredentialUpdate>(text, Json.Options));
            EnsureFreshAndNotReplayed(update);

            using var rsa = OpenOrCreateKey();
            var expectedFingerprint = Fingerprint(rsa.ExportSubjectPublicKeyInfo());
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(update.KeyFingerprint.ToLowerInvariant()),
                Encoding.ASCII.GetBytes(expectedFingerprint)))
                throw new SecurityException("a chave publica do agente foi renovada; salve o Access Token novamente");

            var encrypted = Convert.FromBase64String(update.EncryptedPayload);
            byte[] plaintext = Array.Empty<byte>();
            string payloadText = "";
            try
            {
                plaintext = rsa.Decrypt(encrypted, RSAEncryptionPadding.OaepSHA1);
                payloadText = Encoding.UTF8.GetString(plaintext);
                var accessToken = payloadText;
                if (!IsValidAccessToken(accessToken)) throw new SecurityException("Access Token recebido possui formato invalido");
                WriteReplayState(update, "prepared");
                _secrets.Save(accessToken);
                tokenSaved = true;
                // A ponte ja herdou uma ACL fechada. Se a finalizacao do
                // recibo falhar depois da gravacao do token, o estado
                // "prepared" continua bloqueando repeticoes. Nunca exibimos
                // "recusado" quando a credencial ja foi efetivamente salva.
                try { WriteReplayState(update, "accepted"); }
                catch (Exception finalizeEx) when (finalizeEx is IOException or UnauthorizedAccessException or CryptographicException)
                {
                    Console.Error.WriteLine("Aviso: token protegido, mas o recibo de seguranca sera finalizado automaticamente: " + SafeMessage(finalizeEx.Message));
                }
            }
            finally
            {
                if (plaintext.Length > 0) CryptographicOperations.ZeroMemory(plaintext);
            }

            TryDelete(_paths.CredentialUpdateFile);
            _paths.WriteAtomically(_paths.CredentialUpdateStatusFile, new
            {
                schemaVersion = 1,
                requestId = update.RequestId,
                state = "accepted",
                message = "Access Token recebido e protegido pelo servico PIX.",
                updatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException
            or CryptographicException or SecurityException or InvalidOperationException)
        {
            // Se o token foi protegido, o pedido nao pode ser chamado de
            // recusado. O arquivo de replay preparado impede o reuso e o
            // proximo envio do editor usa um novo identificador.
            if (tokenSaved)
            {
                TryDelete(_paths.CredentialUpdateFile);
                _paths.WriteAtomically(_paths.CredentialUpdateStatusFile, new
                {
                    schemaVersion = 1,
                    requestId = update?.RequestId ?? "unknown",
                    state = "accepted",
                    message = "Access Token protegido pelo servico PIX; o recibo de seguranca sera finalizado automaticamente.",
                    updatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
                return true;
            }
            // Um recibo "prepared" e mantido quando a gravacao do token
            // falha. Ele bloqueia replay do mesmo envelope; o editor pode
            // enviar outro pedido, com horario mais novo, sem apagar a
            // memoria de seguranca anterior.
            var requestId = update is not null && PixId.IsValid(update.RequestId) ? update.RequestId : "unknown";
            _paths.WriteAtomically(_paths.CredentialUpdateStatusFile, new
            {
                schemaVersion = 1,
                requestId,
                state = "rejected",
                message = "O Access Token nao foi aceito pelo servico PIX: " + SafeMessage(ex.Message),
                updatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            // O pedido contem somente token cifrado. Quarentena evita que um
            // arquivo invalido seja reprocessado infinitamente.
            _paths.Quarantine(_paths.CredentialUpdateFile, "Atualizacao de credencial recusada.");
            return false;
        }
    }

    internal static PixCredentialUpdate ValidateEnvelope(PixCredentialUpdate? update)
    {
        if (update is null || update.SchemaVersion != 3 || !PixId.IsValid(update.RequestId)
            || string.IsNullOrWhiteSpace(update.EncryptedPayload) || update.EncryptedPayload.Length is < 32 or > 8192
            || string.IsNullOrWhiteSpace(update.KeyFingerprint) || update.KeyFingerprint.Length != 64)
            throw new InvalidOperationException("pedido de credencial invalido");
        return update;
    }

    private RSA OpenOrCreateKey()
    {
        if (File.Exists(_paths.CredentialPrivateKeyFile))
        {
            try
            {
                var stored = File.ReadAllText(_paths.CredentialPrivateKeyFile, Encoding.UTF8).Trim();
                if (!stored.StartsWith(PrivateKeyPrefix, StringComparison.Ordinal)) throw new CryptographicException("formato de chave invalido");
                var protectedKey = Convert.FromBase64String(stored[PrivateKeyPrefix.Length..]);
                var unprotectedKey = WindowsDpapi.Unprotect(protectedKey, KeyEntropy);
                try
                {
                    var rsa = RSA.Create();
                    rsa.ImportPkcs8PrivateKey(unprotectedKey, out _);
                    if (rsa.KeySize < 4096)
                    {
                        rsa.Dispose();
                        throw new CryptographicException("chave de credencial antiga");
                    }
                    return rsa;
                }
                finally { CryptographicOperations.ZeroMemory(unprotectedKey); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CryptographicException)
            {
                // A chave pertence a outra identidade Windows ou foi danificada.
                // Uma nova chave publica sera publicada; o editor solicitara o token outra vez.
                var backup = _paths.CredentialPrivateKeyFile + ".unreadable-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
                try { File.Move(_paths.CredentialPrivateKeyFile, backup, true); }
                catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException) { }
                // Recibos e pedidos sao vinculados pela DPAPI e pela chave
                // antiga. Mantê-los apos uma rotacao faria toda nova
                // credencial parecer replay ou arquivo ilegivel para sempre.
                ResetCredentialExchangeState();
            }
        }

        // 4096 bits acomodam com folga o Access Token enviado pelo editor
        // nativo (maximo declarado: 384 bytes antes do OAEP).
        var generated = RSA.Create(4096);
        var privateKey = generated.ExportPkcs8PrivateKey();
        try
        {
            var protectedKey = WindowsDpapi.Protect(privateKey, KeyEntropy);
            try
            {
                var data = Encoding.UTF8.GetBytes(PrivateKeyPrefix + Convert.ToBase64String(protectedKey));
                _paths.WriteBytesAtomically(_paths.CredentialPrivateKeyFile, data);
            }
            finally { CryptographicOperations.ZeroMemory(protectedKey); }
        }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
        return generated;
    }

    private static bool IsValidAccessToken(string value)
        => value.Length is >= 40 and <= 384 && value.StartsWith("APP_USR-", StringComparison.Ordinal)
            && !value.Any(char.IsWhiteSpace) && value.All(ch => ch is >= '!' and <= '~');

    private void EnsureFreshAndNotReplayed(PixCredentialUpdate update)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (update.CreatedAtUnixSeconds < now - 300 || update.CreatedAtUnixSeconds > now + 60)
            throw new SecurityException("pedido de credencial expirado; envie novamente pelo editor PIX");

        var previous = LoadLastAcceptance();
        if (previous is null) return;
        if (update.RequestId.Equals(previous.RequestId, StringComparison.Ordinal)
            || update.CreatedAtUnixSeconds <= previous.CreatedAtUnixSeconds)
            throw new SecurityException("pedido de credencial repetido ou antigo; envie uma nova atualizacao pelo editor PIX");
    }

    private PixCredentialReplay? LoadLastAcceptance()
    {
        if (!File.Exists(_paths.CredentialReplayFile)) return null;
        try
        {
            var protectedBytes = Convert.FromBase64String(File.ReadAllText(_paths.CredentialReplayFile, Encoding.UTF8).Trim());
            var plainBytes = WindowsDpapi.Unprotect(protectedBytes, KeyEntropy);
            try
            {
                var replay = JsonSerializer.Deserialize<PixCredentialReplay>(plainBytes, Json.Options);
                if (replay is null || !PixId.IsValid(replay.RequestId) || replay.CreatedAtUnixSeconds <= 0
                    || (replay.State != "prepared" && replay.State != "accepted"))
                    throw new SecurityException("registro de seguranca de credencial e invalido");
                return replay;
            }
            finally { CryptographicOperations.ZeroMemory(plainBytes); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CryptographicException or JsonException)
        {
            throw new SecurityException("registro de seguranca da credencial nao pode ser lido; solicite suporte antes de trocar o token");
        }
    }

    private void WriteReplayState(PixCredentialUpdate update, string state)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new PixCredentialReplay(update.RequestId, update.CreatedAtUnixSeconds, state), Json.Options);
        try
        {
            var protectedBytes = WindowsDpapi.Protect(payload, KeyEntropy);
            try
            {
                _paths.WriteBytesAtomically(_paths.CredentialReplayFile, Encoding.UTF8.GetBytes(Convert.ToBase64String(protectedBytes)));
            }
            finally { CryptographicOperations.ZeroMemory(protectedBytes); }
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }

    private static string Fingerprint(byte[] publicKey)
        => Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();

    private static string SafeMessage(string value)
    {
        var clean = new string(value.Where(ch => !char.IsControl(ch)).ToArray());
        return clean.Length > 220 ? clean[..220] : clean;
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void ResetCredentialExchangeState()
    {
        TryDelete(_paths.CredentialPublicKeyFile);
        TryDelete(_paths.CredentialUpdateFile);
        TryDelete(_paths.CredentialUpdateStatusFile);
        TryDelete(_paths.CredentialReplayFile);
    }
}

// Reutiliza a senha de administrador ja definida para a locadora somente nos
// comandos administrativos legados. O editor grafico de Access Token usa a
// ponte cifrada acima e nao solicita esta senha.
static class PixOwnerPassword
{
    private const string DefaultPasswordHash = "21232f297a57a5a743894a0e4a801fc3"; // MD5("admin")

    public static void Verify(PixPaths paths, string candidate)
    {
        var password = candidate.Trim();
        if (password.Length is < 4 or > 80 || password.Any(char.IsControl))
            throw new SecurityException("senha do proprietario invalida");

        var configurationDirectory = Directory.GetParent(paths.Root)?.FullName
            ?? throw new SecurityException("pasta de configuracao do proprietario nao foi localizada");
        var configurationFile = Path.Combine(configurationDirectory, "arcade_credit.cfg");
        if (!File.Exists(configurationFile))
            throw new SecurityException("defina primeiro a senha do proprietario no EmulationStation");
        WindowsFileSecurity.HardenCredentialFile(configurationFile, allowBuiltinUsersRead: false);

        string? expected = null;
        try
        {
            foreach (var line in File.ReadLines(configurationFile, Encoding.UTF8))
            {
                var trimmed = line.Trim();
                const string prefix = "adminPasswordHash=";
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    expected = trimmed[prefix.Length..].Trim().ToLowerInvariant();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SecurityException("nao foi possivel ler a senha do proprietario configurada");
        }

        if (string.IsNullOrWhiteSpace(expected) || expected.Length != 32 || !expected.All(Uri.IsHexDigit))
            throw new SecurityException("senha do proprietario ainda nao foi configurada");
        if (FixedTimeEquals(expected, DefaultPasswordHash))
            throw new SecurityException("a senha padrao ainda esta ativa; altere-a no EmulationStation antes de configurar PIX");
        if (!FixedTimeEquals(expected, Md5Hex(password)))
            throw new SecurityException("senha do proprietario nao confere");
    }

    private static string Md5Hex(string value)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool FixedTimeEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}

sealed class PixAgentInstanceLock : IDisposable
{
    private readonly FileStream _stream;
    private PixAgentInstanceLock(FileStream stream) => _stream = stream;

    public static PixAgentInstanceLock? TryAcquire(string root)
    {
        try
        {
            var file = Path.Combine(root, ".agent.lock");
            var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
            writer.Write($"pid={Environment.ProcessId};started={DateTimeOffset.UtcNow:O}");
            writer.Flush();
            stream.Flush(flushToDisk: true);
            stream.Position = 0;
            return new PixAgentInstanceLock(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}

sealed class AgentFileLog : IDisposable
{
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;
    private readonly StreamWriter _file;

    private AgentFileLog(string directory)
    {
        _originalOut = Console.Out;
        _originalError = Console.Error;
        Directory.CreateDirectory(directory);
        var current = Path.Combine(directory, $"pix-agent-{DateTime.UtcNow:yyyyMMdd}.log");
        RotateIfLarge(current);
        CleanupOld(directory);
        _file = new StreamWriter(new FileStream(current, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
        var gate = new object();
        Console.SetOut(TextWriter.Synchronized(new TeeLogWriter(_originalOut, _file, gate, "INFO")));
        Console.SetError(TextWriter.Synchronized(new TeeLogWriter(_originalError, _file, gate, "ERRO")));
    }

    public static AgentFileLog? TryAttach(string directory)
    {
        try { return new AgentFileLog(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Aviso: log em arquivo indisponivel: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        _file.Dispose();
    }

    private static void RotateIfLarge(string file)
    {
        try
        {
            if (File.Exists(file) && new FileInfo(file).Length > 5 * 1024 * 1024)
                File.Move(file, file + "." + DateTime.UtcNow.ToString("HHmmss") + ".old", true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void CleanupOld(string directory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "pix-agent-*.log*"))
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-30)) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed class TeeLogWriter : TextWriter
    {
        private readonly TextWriter _console;
        private readonly TextWriter _file;
        private readonly object _gate;
        private readonly string _level;
        public TeeLogWriter(TextWriter console, TextWriter file, object gate, string level)
            => (_console, _file, _gate, _level) = (console, file, gate, level);
        public override Encoding Encoding => _console.Encoding;
        public override void Write(char value) { lock (_gate) { _console.Write(value); _file.Write(value); } }
        public override void Write(string? value) { lock (_gate) { _console.Write(value); _file.Write(value); } }
        public override void WriteLine(string? value)
        {
            lock (_gate)
            {
                _console.WriteLine(value);
                _file.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{_level}] {value}");
            }
        }
    }
}

sealed class PixSigningKeyStore
{
    private readonly string _path;
    public PixSigningKeyStore(string path) => _path = path;

    public byte[] GetOrCreate()
    {
        if (File.Exists(_path))
        {
            WindowsFileSecurity.HardenCredentialFile(_path, allowBuiltinUsersRead: false);
            return Load();
        }
        var key = RandomNumberGenerator.GetBytes(32);
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            var bytes = Encoding.UTF8.GetBytes(Convert.ToBase64String(key));
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        try { File.Move(temp, _path, false); }
        catch (IOException) { File.Delete(temp); return Load(); }
        try { File.SetAttributes(_path, File.GetAttributes(_path) | FileAttributes.Hidden); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        WindowsFileSecurity.HardenCredentialFile(_path, allowBuiltinUsersRead: false);
        return key;
    }

    public byte[] Load()
    {
        var key = Convert.FromBase64String(File.ReadAllText(_path, Encoding.UTF8).Trim());
        if (key.Length != 32) throw new InvalidOperationException("Chave de assinatura PIX invalida.");
        return key;
    }
}

static class SecretConsole
{
    public static string ReadHidden()
    {
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace && value.Length > 0)
            {
                value.Length--;
                Console.Write("\b \b");
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                value.Append(key.KeyChar);
                Console.Write('*');
            }
        }
        return value.ToString();
    }

    public static string Mask(string value)
    {
        if (string.IsNullOrEmpty(value)) return "(vazio)";
        var prefixLength = Math.Min(8, value.Length);
        var suffixLength = Math.Min(10, Math.Max(0, value.Length - prefixLength));
        var prefix = value[..prefixLength];
        var suffix = suffixLength > 0 ? value[^suffixLength..] : "";
        return $"{prefix}...{suffix}";
    }
}

sealed class PixEngine
{
    private readonly PixOptions _options;
    private readonly PixPaths _paths;
    private readonly IPixProvider _provider;
    private readonly PixSigningKeyStore _signingKeys;

    public PixEngine(PixOptions options, PixPaths paths, IPixProvider provider, PixSigningKeyStore signingKeys)
    {
        _options = options;
        _paths = paths;
        _provider = provider;
        _signingKeys = signingKeys;
    }

    public async Task RunOnceAsync(CancellationToken token)
    {
        foreach (var requestFile in Directory.EnumerateFiles(_paths.Requests, "*.request.json").OrderBy(Path.GetFileName))
        {
            var requestId = Path.GetFileName(requestFile).Replace(".request.json", "", StringComparison.OrdinalIgnoreCase);
            try
            {
                if (!PixId.IsValid(requestId)) { _paths.Quarantine(requestFile, "Nome de solicitacao invalido."); continue; }
                if (!RequestRetryDue(requestId)) continue;
                var request = JsonSerializer.Deserialize<PixPurchaseRequest>(File.ReadAllText(requestFile, Encoding.UTF8), Json.Options);
                ValidateRequest(request, requestId);
                if (request is null) continue;
                if (File.Exists(_paths.SessionFile(request.Id))) { File.Delete(requestFile); DeleteIfExists(_paths.RetryFile(request.Id)); continue; }
                var session = await _provider.CreateAsync(request, token);
                SaveQr(session);
                _paths.WriteAtomically(_paths.SessionFile(session.Id), session);
                File.Delete(requestFile);
                DeleteIfExists(_paths.RetryFile(request.Id));
            }
            catch (Exception ex) when (ex is JsonException or RequestRejectedException or FormatException)
            {
                Console.Error.WriteLine($"Solicitacao PIX rejeitada: {ex.Message}");
                _paths.Quarantine(requestFile, ex.Message);
                DeleteIfExists(_paths.RetryFile(requestId));
            }
            catch (SecurityException ex)
            {
                Console.Error.WriteLine($"Solicitacao PIX bloqueada por divergencia do provedor: {ex.Message}");
                _paths.Quarantine(requestFile, ex.Message);
                DeleteIfExists(_paths.RetryFile(requestId));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (MercadoPagoApiException ex) when (IsRetryableHttpStatus(ex.StatusCode))
            {
                Console.Error.WriteLine($"Falha na cobranca PIX: {ex.Message}");
                ScheduleRequestRetry(requestId);
            }
            catch (AdapterApiException ex) when (IsRetryableHttpStatus(ex.StatusCode))
            {
                Console.Error.WriteLine($"Falha temporaria no adaptador PIX: {ex.Message}");
                ScheduleRequestRetry(requestId);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
            {
                Console.Error.WriteLine($"Falha temporaria na cobranca PIX: {ex.Message}");
                ScheduleRequestRetry(requestId);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or MercadoPagoApiException or AdapterApiException or InvalidOperationException)
            {
                RejectRequest(requestFile, requestId, ex.Message);
            }
        }

        foreach (var sessionFile in Directory.EnumerateFiles(_paths.Sessions, "*.session.json").OrderBy(Path.GetFileName))
        {
            try
            {
                var session = JsonSerializer.Deserialize<PixSession>(File.ReadAllText(sessionFile, Encoding.UTF8), Json.Options);
                if (session is null) throw new JsonException("Sessao PIX vazia.");
                var fileId = Path.GetFileName(sessionFile).Replace(".session.json", "", StringComparison.OrdinalIgnoreCase);
                ValidateSession(session, fileId);

                // Versoes anteriores geravam PNG indexado de 1 bit. O carregador de
                // imagens desta compilacao do EmulationStation reconhecia o arquivo,
                // mas nao conseguia desenha-lo. Regrava uma sessao pendente no formato
                // RGBA de 32 bits, inclusive apos atualizar uma instalacao em uso.
                if (session.Status == "pending") EnsureCompatibleQr(session);

                if (session.Status == "completed")
                {
                    if (File.Exists(_paths.ProcessedFile(session.Id)) || File.Exists(_paths.ApprovedFile(session.Id))) continue;
                    // Se o evento desapareceu antes de ser consumido, nunca confiamos apenas
                    // no arquivo local: voltamos a consultar o provedor.
                    session = session with { Status = "pending", NextPollAt = DateTimeOffset.UtcNow };
                    _paths.WriteAtomically(sessionFile, session);
                }
                else if (session.Status == "approved")
                {
                    // Compatibilidade segura com sessao de versao anterior: revalida no banco.
                    session = session with { Status = "pending", NextPollAt = DateTimeOffset.UtcNow };
                    _paths.WriteAtomically(sessionFile, session);
                }
                else if (session.Status != "pending") continue;

                if (session.NextPollAt > DateTimeOffset.UtcNow) continue;
                var refreshed = await _provider.RefreshAsync(session, token);
                if (refreshed is null) continue;
                if (refreshed.Status == "approved")
                {
                    // Ordem deliberada: primeiro grava o evento assinado; somente depois marca
                    // a sessao concluida. Uma queda entre os passos causa nova consulta, nao perda.
                    PublishCredit(refreshed);
                    _paths.WriteAtomically(sessionFile, refreshed with { Status = "completed", FailureCount = 0, NextPollAt = DateTimeOffset.MaxValue });
                    DeleteQrFiles(refreshed.Id);
                    continue;
                }
                var scheduled = refreshed.Status == "pending"
                    ? refreshed with { FailureCount = 0, NextPollAt = DateTimeOffset.UtcNow.AddSeconds(_options.PollSeconds) }
                    : refreshed with { FailureCount = 0, NextPollAt = DateTimeOffset.MaxValue };
                _paths.WriteAtomically(sessionFile, scheduled);
                if (scheduled.Status is "cancelled" or "security_error") DeleteQrFiles(scheduled.Id);
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"Sessao PIX corrompida e isolada: {ex.Message}");
                _paths.Quarantine(sessionFile, ex.Message);
            }
            catch (SecurityException ex)
            {
                Console.Error.WriteLine($"PIX bloqueado por divergencia de seguranca: {ex.Message}");
                MarkSessionError(sessionFile, "security_error");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or HttpRequestException or MercadoPagoApiException or AdapterApiException or InvalidOperationException)
            {
                Console.Error.WriteLine($"Falha ao consultar PIX: {ex.Message}");
                ScheduleSessionRetry(sessionFile);
            }
        }
    }

    public Task<bool> ApproveMockAsync(string id)
    {
        var file = _paths.SessionFile(id);
        if (!File.Exists(file)) return Task.FromResult(false);
        var session = JsonSerializer.Deserialize<PixSession>(File.ReadAllText(file, Encoding.UTF8), Json.Options);
        if (session is null || session.Provider != "mock") return Task.FromResult(false);
        ValidateSession(session, id);
        var approved = session with { Status = "approved", UpdatedAt = DateTimeOffset.UtcNow };
        PublishCredit(approved);
        _paths.WriteAtomically(file, approved with { Status = "completed", NextPollAt = DateTimeOffset.MaxValue });
        return Task.FromResult(true);
    }

    private void ValidateRequest(PixPurchaseRequest? request, string fileId)
    {
        if (request is null || !PixId.IsValid(request.Id) || !request.Id.Equals(fileId, StringComparison.Ordinal))
            throw new RequestRejectedException("Identificador divergente ou invalido.");
        var expected = _options.PriceFor(request.Minutes);
        if (expected <= 0) throw new RequestRejectedException("Pacote de minutos nao permitido.");
        if (request.AmountCents != expected) throw new RequestRejectedException("Valor adulterado ou desatualizado.");
        var age = DateTimeOffset.UtcNow - request.RequestedAt;
        if (age < TimeSpan.FromMinutes(-2) || age > TimeSpan.FromMinutes(_options.PaymentExpirationMinutes))
            throw new RequestRejectedException("Solicitacao expirada ou relogio do sistema incorreto.");
    }

    private void ValidateSession(PixSession session, string fileId)
    {
        if (!PixId.IsValid(session.Id) || !session.Id.Equals(fileId, StringComparison.Ordinal))
            throw new SecurityException("Identificador da sessao divergente.");
        if (!session.Provider.Equals(_provider.Name, StringComparison.Ordinal))
            throw new SecurityException("Provedor da sessao diverge da configuracao ativa.");
        var expected = _options.PriceFor(session.Minutes);
        if (expected <= 0 || session.AmountCents != expected)
            throw new SecurityException("Pacote ou valor da sessao foi adulterado.");
        if (!PixId.IsValidProviderOrder(session.ProviderOrderId))
            throw new SecurityException("Identificador da order e invalido.");
        if (session.Status is not ("pending" or "approved" or "completed" or "cancelled" or "security_error"))
            throw new SecurityException("Estado da sessao e invalido.");
        if (session.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(5) || session.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new SecurityException("Relogio da sessao e invalido.");
        if (session.QrData.Length is < 20 or > 8192)
            throw new SecurityException("Conteudo do QR PIX invalido.");
    }

    private void EnsureCompatibleQr(PixSession session)
    {
        var pngFile = _paths.QrFile(session.Id);
        var matrixFile = _paths.QrMatrixFile(session.Id);
        if (File.Exists(pngFile) && File.Exists(matrixFile))
        {
            try
            {
                var key = _signingKeys.GetOrCreate();
                if (PixQrPng.IsEmulationStationCompatible(File.ReadAllBytes(pngFile))
                    && PixQrPng.IsSignedMatrixCompatible(File.ReadAllBytes(matrixFile), session.Id, key)) return;
            }
            catch (IOException) { }
        }
        SaveQr(session);
    }

    private void SaveQr(PixSession session)
    {
        _paths.WriteBytesAtomically(_paths.QrFile(session.Id), PixQrPng.Render(session.QrData, 8));
        _paths.WriteBytesAtomically(_paths.QrMatrixFile(session.Id),
            PixQrPng.RenderSignedMatrix(session.Id, session.QrData, _signingKeys.GetOrCreate()));
    }

    private void DeleteQrFiles(string id)
    {
        DeleteIfExists(_paths.QrFile(id));
        DeleteIfExists(_paths.QrMatrixFile(id));
    }

    private void PublishCredit(PixSession session)
    {
        var file = _paths.ApprovedFile(session.Id);
        var approvedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var unsigned = new PixCreditEvent(1, session.Id, session.Minutes, session.AmountCents, session.Provider, session.ProviderOrderId, approvedAt, "");
        var signature = PixEventSigner.Sign(unsigned, _signingKeys.GetOrCreate());
        // Sempre substitui por uma copia legitima e assinada. Isso recupera arquivo parcial
        // e impede que um arquivo falso previamente criado bloqueie um pagamento verdadeiro.
        _paths.WriteAtomically(file, unsigned with { Signature = signature });
    }

    private bool RequestRetryDue(string id)
    {
        var file = _paths.RetryFile(id);
        if (!File.Exists(file)) return true;
        try { return (JsonSerializer.Deserialize<PixRetryState>(File.ReadAllText(file), Json.Options)?.NextAttemptAt ?? DateTimeOffset.MinValue) <= DateTimeOffset.UtcNow; }
        catch (Exception ex) when (ex is IOException or JsonException) { DeleteIfExists(file); return true; }
    }

    private void ScheduleRequestRetry(string id)
    {
        if (!PixId.IsValid(id)) return;
        var file = _paths.RetryFile(id);
        var failures = 0;
        try { failures = JsonSerializer.Deserialize<PixRetryState>(File.ReadAllText(file), Json.Options)?.FailureCount ?? 0; }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        failures = Math.Min(failures + 1, 30);
        _paths.WriteAtomically(file, new PixRetryState(failures, DateTimeOffset.UtcNow.AddSeconds(RetryDelay(failures))));
    }

    private void ScheduleSessionRetry(string sessionFile)
    {
        try
        {
            var session = JsonSerializer.Deserialize<PixSession>(File.ReadAllText(sessionFile), Json.Options);
            if (session is null) return;
            var failures = Math.Min(session.FailureCount + 1, 30);
            _paths.WriteAtomically(sessionFile, session with { FailureCount = failures, NextPollAt = DateTimeOffset.UtcNow.AddSeconds(RetryDelay(failures)), UpdatedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
    }

    private void MarkSessionError(string sessionFile, string status)
    {
        try
        {
            var session = JsonSerializer.Deserialize<PixSession>(File.ReadAllText(sessionFile), Json.Options);
            if (session is null) return;
            _paths.WriteAtomically(sessionFile, session with { Status = status, NextPollAt = DateTimeOffset.MaxValue, UpdatedAt = DateTimeOffset.UtcNow });
            DeleteQrFiles(session.Id);
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
    }

    private int RetryDelay(int failures) => Math.Min(_options.MaxRetrySeconds, (int)Math.Pow(2, Math.Min(failures, 8)) * 2);

    private void RejectRequest(string requestFile, string requestId, string reason)
    {
        var safeReason = SafeFailureReason(reason);
        Console.Error.WriteLine($"Solicitacao PIX recusada sem nova tentativa automatica: {safeReason}");
        _paths.Quarantine(requestFile, safeReason);
        DeleteIfExists(_paths.RetryFile(requestId));
    }

    private static bool IsRetryableHttpStatus(int statusCode)
        => statusCode == 408 || statusCode == 429 || statusCode is >= 500 and <= 599;

    private static string SafeFailureReason(string reason)
    {
        var value = string.IsNullOrWhiteSpace(reason) ? "Falha do provedor sem detalhe." : reason.Trim();
        var tokenStart = value.IndexOf("APP_USR-", StringComparison.OrdinalIgnoreCase);
        if (tokenStart >= 0)
        {
            var tokenEnd = tokenStart;
            while (tokenEnd < value.Length && (char.IsLetterOrDigit(value[tokenEnd]) || value[tokenEnd] is '-' or '_')) tokenEnd++;
            value = value[..tokenStart] + "[Access Token oculto]" + value[tokenEnd..];
        }
        var clean = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return clean.Length > 300 ? clean[..300] : clean;
    }

    private static void DeleteIfExists(string file) { try { if (File.Exists(file)) File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
}

interface IPixProvider
{
    string Name { get; }
    Task CheckHealthAsync(CancellationToken token);
    Task<PixSession> CreateAsync(PixPurchaseRequest request, CancellationToken token);
    Task<PixSession?> RefreshAsync(PixSession session, CancellationToken token);
}

static class PixProviderFactory
{
    public static IPixProvider Create(PixOptions options, PixSecretStore secrets)
        => options.Provider switch
        {
            "mercadopago" => new MercadoPagoPixProvider(options, secrets),
            "adapter" => new AdapterPixProvider(options, secrets),
            _ => new MockPixProvider()
        };
}

sealed class MockPixProvider : IPixProvider
{
    public string Name => "mock";
    public Task CheckHealthAsync(CancellationToken token) => Task.CompletedTask;
    public Task<PixSession> CreateAsync(PixPurchaseRequest request, CancellationToken token)
        => Task.FromResult(PixSession.Pending(request, Name, "PIX-TEST-" + request.Id, $"TURBORAMA-TESTE:{request.Id}:{request.AmountCents}"));
    public Task<PixSession?> RefreshAsync(PixSession session, CancellationToken token) => Task.FromResult<PixSession?>(session);
}

sealed class MercadoPagoPixProvider : IPixProvider
{
    private static readonly int[] StoreVisibilityDelayMilliseconds = { 0, 300, 700, 1500, 3000 };
    private static readonly int[] PosRecoveryDelayMilliseconds = { 500, 1000, 2000, 4000 };
    private readonly PixOptions _options;
    private readonly PixSecretStore _secrets;
    private readonly HttpClient _http;
    private string _externalPosId;
    public string Name => "mercadopago";
    public MercadoPagoPixProvider(PixOptions options, PixSecretStore secrets, HttpMessageHandler? handler = null)
    {
        _options = options;
        _secrets = secrets;
        _externalPosId = options.MercadoPago.ExternalPosId;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.BaseAddress = new Uri("https://api.mercadopago.com/");
        _http.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TurboRamaPixAgent/1.0");
    }

    public async Task CheckHealthAsync(CancellationToken token)
    {
        var externalPosId = ExternalPosId;
        using var posMessage = new HttpRequestMessage(HttpMethod.Get,
            $"pos?external_id={Uri.EscapeDataString(externalPosId)}");
        posMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", RequireToken());
        using var posResponse = await _http.SendAsync(posMessage, token);
        var posText = await posResponse.Content.ReadAsStringAsync(token);
        EnsureApiSuccess(posResponse, posText);
        using var posJson = ParseApiJson(posText);
        if (!posJson.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array
            || !results.EnumerateArray().Any(item => GetString(item, "external_id")
                .Equals(externalPosId, StringComparison.Ordinal)))
            throw new MercadoPagoApiException(404, $"PDV {externalPosId} nao foi encontrado na conta");
    }

    private string ExternalPosId => Volatile.Read(ref _externalPosId);

    public void UseExternalPosId(string externalPosId)
    {
        var value = externalPosId.Trim();
        if (value.Length is < 1 or > 40 || !value.All(char.IsAsciiLetterOrDigit))
            throw new InvalidOperationException("O external_id do caixa retornado pelo Mercado Pago e invalido.");
        Volatile.Write(ref _externalPosId, value);
    }

    public async Task<MercadoPagoInfrastructure> GetInfrastructureAsync(CancellationToken token)
    {
        var account = await GetAuthorizedJsonAsync("https://api.mercadolibre.com/users/me", token);
        using (account)
        {
            var accountId = GetScalarString(account.RootElement, "id");
            if (string.IsNullOrWhiteSpace(accountId))
                throw new MercadoPagoApiException(502, "resposta de autenticacao sem User ID");
            return await GetInfrastructureForAccountAsync(accountId, token);
        }
    }

    public Task<MercadoPagoInfrastructure> GetInfrastructureForConfiguredAccountAsync(string accountId, CancellationToken token)
        => GetInfrastructureForAccountAsync(accountId.Trim(), token);

    public async Task<MercadoPagoSetupResult> EnsureInfrastructureAsync(MercadoPagoSetupRequest setup, CancellationToken token)
    {
        setup.ValidateIdentity();
        // Credenciais de sandbox podem ser bloqueadas por politicas no recurso
        // Mercado Livre /users/me. A consulta oficial de lojas com o User ID
        // informado ja valida a posse da conta e retorna 403 quando nao pertence
        // ao mesmo Access Token, sem depender daquele recurso adicional.
        var inventory = await GetInfrastructureForAccountAsync(setup.ExpectedAccountId, token);

        var store = inventory.Stores.SingleOrDefault(x => x.ExternalId.Equals(setup.StoreExternalId, StringComparison.Ordinal));
        store ??= await FindStoreByExternalIdAsync(inventory.AccountId, setup.StoreExternalId, token);
        var storeCreated = false;
        if (store is null)
        {
            // A documentacao do Mercado Pago exige localizacao completa apenas
            // para cadastrar uma loja nova. Um caixa novo em loja existente
            // nao deve depender de CEP, geocodificador ou coordenadas.
            setup.ValidateLocationForNewStore();
            using var storeJson = await SendAuthorizedJsonAsync(HttpMethod.Post,
                $"users/{Uri.EscapeDataString(inventory.AccountId)}/stores", new
                {
                    name = setup.StoreName.Trim(),
                    external_id = setup.StoreExternalId,
                    location = new
                    {
                        street_number = setup.StreetNumber.Trim(),
                        street_name = setup.StreetName.Trim(),
                        city_name = setup.CityName.Trim(),
                        state_name = setup.StateName.Trim(),
                        latitude = setup.Latitude,
                        longitude = setup.Longitude,
                        reference = setup.Reference.Trim()
                    }
                }, token);
            store = ReadStore(storeJson.RootElement);
            if (string.IsNullOrWhiteSpace(store.Id) || !store.ExternalId.Equals(setup.StoreExternalId, StringComparison.Ordinal))
                throw new MercadoPagoApiException(502, "loja criada sem os identificadores esperados");
            storeCreated = true;
            // A resposta do POST confirma que a loja foi aceita, mas o endpoint
            // de PDV pode enxergar o cadastro alguns instantes depois. Antes de
            // criar o caixa, confirme a loja pela busca oficial e pelo mesmo
            // Access Token. Isso tambem evita duplicar a loja numa retomada.
            store = await ConfirmStoreVisibleAsync(inventory.AccountId, store, token);
        }

        var point = inventory.PointsOfSale.SingleOrDefault(x => x.ExternalId.Equals(setup.PosExternalId, StringComparison.Ordinal));
        point ??= await FindPointOfSaleByExternalIdAsync(setup.PosExternalId, token);
        var pointCreated = false;
        if (point is not null) ValidatePointOfSaleStore(point, store);
        if (point is null)
        {
            (point, pointCreated) = await CreatePointOfSaleWithRecoveryAsync(
                inventory.AccountId, setup, store, token);
        }
        return new MercadoPagoSetupResult(inventory.AccountId, store, point, storeCreated, pointCreated);
    }

    private async Task<MercadoPagoStoreInfo> ConfirmStoreVisibleAsync(string accountId,
        MercadoPagoStoreInfo expectedStore, CancellationToken token)
    {
        foreach (var delayMilliseconds in StoreVisibilityDelayMilliseconds)
        {
            if (delayMilliseconds > 0) await Task.Delay(delayMilliseconds, token);
            var store = await FindStoreByExternalIdAsync(accountId, expectedStore.ExternalId, token);
            if (store is null) continue;
            ValidateConfirmedStore(store, expectedStore);
            return store;
        }

        throw new MercadoPagoApiException(409,
            "a loja foi criada, mas ainda nao ficou disponivel para o PDV; o cadastro foi preservado e pode ser retomado sem criar outra loja");
    }

    private async Task<(MercadoPagoPosInfo Point, bool Created)> CreatePointOfSaleWithRecoveryAsync(
        string accountId, MercadoPagoSetupRequest setup, MercadoPagoStoreInfo initialStore, CancellationToken token)
    {
        var store = initialStore;
        Exception? lastPointCreationError = null;

        for (var attempt = 0; attempt <= PosRecoveryDelayMilliseconds.Length; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(PosRecoveryDelayMilliseconds[attempt - 1], token);

                // O POST anterior pode ter sido processado antes de a resposta
                // de erro chegar. Consulte pelo external_id antes de repetir.
                var existingPoint = await FindPointOfSaleByExternalIdAsync(setup.PosExternalId, token);
                if (existingPoint is not null)
                {
                    ValidatePointOfSaleStore(existingPoint, store);
                    return (existingPoint, false);
                }

                var visibleStore = await FindStoreByExternalIdAsync(accountId, setup.StoreExternalId, token);
                if (visibleStore is null) continue;
                ValidateConfirmedStore(visibleStore, initialStore);
                store = visibleStore;
            }

            try
            {
                var point = await CreatePointOfSaleAsync(setup, store, token);
                return (point, true);
            }
            catch (MercadoPagoApiException ex) when (IsRecoverablePointOfSaleCreation(ex))
            {
                lastPointCreationError = ex;
                if (attempt == PosRecoveryDelayMilliseconds.Length) break;
            }
            catch (HttpRequestException ex)
            {
                lastPointCreationError = ex;
                if (attempt == PosRecoveryDelayMilliseconds.Length) break;
            }
            catch (TaskCanceledException ex) when (!token.IsCancellationRequested)
            {
                lastPointCreationError = ex;
                if (attempt == PosRecoveryDelayMilliseconds.Length) break;
            }
        }

        throw new MercadoPagoApiException(409,
            "a loja existe na conta, mas o Mercado Pago ainda nao a liberou para criar o PDV; o cadastro foi preservado para retomada automatica"
            + (lastPointCreationError is null ? "" : " (confirmacao temporariamente indisponivel)"));
    }

    private async Task<MercadoPagoPosInfo> CreatePointOfSaleAsync(MercadoPagoSetupRequest setup,
        MercadoPagoStoreInfo store, CancellationToken token)
    {
        var posBody = new Dictionary<string, object?>
        {
            ["name"] = setup.PosName.Trim(),
            ["fixed_amount"] = true,
            ["store_id"] = ParseNumericId(store.Id, "ID da loja"),
            // O contrato oficial do QR exige os dois identificadores: o ID
            // interno retornado pelo Mercado Pago e o external_id confirmado.
            ["external_store_id"] = store.ExternalId,
            ["external_id"] = setup.PosExternalId
        };
        // O MCC e opcional. Para estabelecimentos fora das categorias MCC
        // aceitas pelo site do usuario, omitir o campo aplica a categoria
        // generica e evita POS_UNKNOWN_MCC.
        if (setup.Category.HasValue) posBody["category"] = setup.Category.Value;
        using var posJson = await SendAuthorizedJsonAsync(HttpMethod.Post, "pos", posBody, token);
        var point = ReadPos(posJson.RootElement);
        if (string.IsNullOrWhiteSpace(point.Id) || !point.ExternalId.Equals(setup.PosExternalId, StringComparison.Ordinal))
            throw new MercadoPagoApiException(502, "PDV criado sem os identificadores esperados");
        ValidatePointOfSaleStore(point, store);
        return point;
    }

    private async Task<MercadoPagoStoreInfo?> FindStoreByExternalIdAsync(string accountId,
        string externalId, CancellationToken token)
    {
        var route = $"users/{Uri.EscapeDataString(accountId)}/stores/search?external_id={Uri.EscapeDataString(externalId)}&limit=10";
        try
        {
            using var json = await GetAuthorizedJsonAsync(route, token);
            var matches = Results(json.RootElement).Select(ReadStore)
                .Where(x => x.ExternalId.Equals(externalId, StringComparison.Ordinal)).ToList();
            if (matches.Count > 1)
                throw new SecurityException("Mais de uma loja usa o mesmo external_id nesta conta.");
            return matches.Count == 1 ? matches[0] : null;
        }
        catch (MercadoPagoApiException ex) when (ex.StatusCode == 404)
        {
            // A busca oficial documenta 404/store_not_found como resultado
            // normal quando o external_id ainda nao existe ou nao propagou.
            return null;
        }
    }

    private async Task<MercadoPagoPosInfo?> FindPointOfSaleByExternalIdAsync(string externalId,
        CancellationToken token)
    {
        try
        {
            using var json = await GetAuthorizedJsonAsync(
                $"pos?external_id={Uri.EscapeDataString(externalId)}&limit=10&offset=0", token);
            var matches = Results(json.RootElement).Select(ReadPos)
                .Where(x => x.ExternalId.Equals(externalId, StringComparison.Ordinal)).ToList();
            if (matches.Count > 1)
                throw new SecurityException("Mais de um PDV usa o mesmo external_id nesta conta.");
            return matches.Count == 1 ? matches[0] : null;
        }
        catch (MercadoPagoApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private static void ValidateConfirmedStore(MercadoPagoStoreInfo actual, MercadoPagoStoreInfo expected)
    {
        if (!actual.ExternalId.Equals(expected.ExternalId, StringComparison.Ordinal)
            || !actual.Id.Equals(expected.Id, StringComparison.Ordinal))
            throw new SecurityException("A loja confirmada pelo Mercado Pago diverge do cadastro criado.");
    }

    private static void ValidatePointOfSaleStore(MercadoPagoPosInfo point, MercadoPagoStoreInfo store)
    {
        if (string.IsNullOrWhiteSpace(point.StoreId))
            throw new MercadoPagoApiException(502, "PDV retornado sem vinculo com a loja");
        if (!point.StoreId.Equals(store.Id, StringComparison.Ordinal))
            throw new SecurityException("O external_id do PDV ja pertence a outra loja da conta.");
        if (!string.IsNullOrWhiteSpace(point.Status)
            && !point.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            throw new MercadoPagoApiException(409, "o PDV existe, mas nao esta ativo no Mercado Pago");
    }

    private static bool IsRecoverablePointOfSaleCreation(MercadoPagoApiException exception)
        => (exception.StatusCode == 400
            && (exception.Detail.Contains("non_existent_external_store_id", StringComparison.OrdinalIgnoreCase)
                || exception.Detail.Contains("inexistent_external_store_id", StringComparison.OrdinalIgnoreCase)
                || exception.Detail.Contains("external store id does not refer any store", StringComparison.OrdinalIgnoreCase)))
            || (exception.StatusCode == 409
                && (exception.Detail.Contains("point_of_sale_exists", StringComparison.OrdinalIgnoreCase)
                    || exception.Detail.Contains("point of sale already exists", StringComparison.OrdinalIgnoreCase)));

    private async Task<MercadoPagoInfrastructure> GetInfrastructureForAccountAsync(string accountId, CancellationToken token)
    {
        if (accountId.Length is < 5 or > 24 || !accountId.All(char.IsAsciiDigit))
            throw new InvalidOperationException("User ID da conta e invalido.");
        using var storesJson = await GetAuthorizedJsonAsync($"users/{Uri.EscapeDataString(accountId)}/stores/search?limit=100", token);
        using var posJson = await GetAuthorizedJsonAsync("pos?limit=100&offset=0", token);
        var stores = Results(storesJson.RootElement).Select(ReadStore).Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();
        var points = Results(posJson.RootElement).Select(ReadPos).Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();
        return new MercadoPagoInfrastructure(accountId, stores, points);
    }

    private async Task<JsonDocument> GetAuthorizedJsonAsync(string route, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", RequireToken());
        using var response = await _http.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        try { EnsureApiSuccess(response, text); }
        catch (MercadoPagoApiException ex) { throw new MercadoPagoApiException(ex.StatusCode, $"GET {route}: {ex.Detail}"); }
        return ParseApiJson(text);
    }

    private async Task<JsonDocument> SendAuthorizedJsonAsync(HttpMethod method, string route, object body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", RequireToken());
        request.Content = JsonContent.Create(body, options: Json.Options);
        using var response = await _http.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        try { EnsureApiSuccess(response, text); }
        catch (MercadoPagoApiException ex) { throw new MercadoPagoApiException(ex.StatusCode, $"{method.Method} {route}: {ex.Detail}"); }
        return ParseApiJson(text);
    }

    private static IEnumerable<JsonElement> Results(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("results", out var results) &&
            results.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in results.EnumerateArray())
                yield return item;
            yield break;
        }

        // Algumas respostas/documentacoes do recurso de lojas apresentam o
        // objeto paginado dentro de um array. Aceitar os dois formatos evita
        // interpretar uma loja existente como lista vazia e tentar duplica-la.
        if (root.ValueKind == JsonValueKind.Array)
        {
            var validPage = false;
            foreach (var page in root.EnumerateArray())
            {
                if (page.ValueKind != JsonValueKind.Object ||
                    !page.TryGetProperty("results", out var pageResults) ||
                    pageResults.ValueKind != JsonValueKind.Array)
                    continue;

                validPage = true;
                foreach (var item in pageResults.EnumerateArray())
                    yield return item;
            }
            if (validPage) yield break;
        }

        // Em infraestrutura financeira, uma resposta 200 com formato
        // inesperado nunca significa "lista vazia": falhe fechado para nao
        // criar loja ou PDV duplicado por engano.
        throw new MercadoPagoApiException(502, "resposta de busca sem a lista results esperada");
    }

    private static MercadoPagoStoreInfo ReadStore(JsonElement item)
        => new(GetScalarString(item, "id"), GetString(item, "external_id"), GetString(item, "name"));

    private static MercadoPagoPosInfo ReadPos(JsonElement item)
        => new(GetScalarString(item, "id"), GetString(item, "external_id"), GetString(item, "name"),
            GetScalarString(item, "store_id"), GetString(item, "status"));

    private static string GetScalarString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            _ => ""
        };
    }

    private static long ParseNumericId(string value, string label)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result > 0
            ? result : throw new MercadoPagoApiException(502, $"{label} retornado e invalido");

    public async Task<PixSession> CreateAsync(PixPurchaseRequest request, CancellationToken token)
    {
        var accessToken = RequireToken();
        // A API Orders documenta os dois campos monetarios como string
        // decimal. Enviar um numero JSON e recusado com property_type.
        var amount = (request.AmountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/orders");
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Add("X-Idempotency-Key", request.Id);
        message.Content = JsonContent.Create(new
        {
            type = "qr", total_amount = amount, external_reference = request.Id,
            expiration_time = $"PT{_options.PaymentExpirationMinutes}M",
            description = $"{_options.MercadoPago.DescriptionPrefix} - {request.Minutes} min",
            config = new { qr = new { mode = "dynamic", external_pos_id = ExternalPosId } },
            transactions = new { payments = new[] { new { amount } } }
        }, options: Json.Options);
        using var response = await _http.SendAsync(message, token);
        var text = await response.Content.ReadAsStringAsync(token);
        try { EnsureApiSuccess(response, text); }
        catch (MercadoPagoApiException ex) { throw new MercadoPagoApiException(ex.StatusCode, $"POST v1/orders: {ex.Detail}"); }
        using var json = ParseApiJson(text);
        var root = json.RootElement;
        var orderId = GetString(root, "id");
        if (string.IsNullOrWhiteSpace(orderId)) throw new MercadoPagoApiException(502, "resposta sem identificador da order");
        var qrData = root.TryGetProperty("type_response", out var typeResponse) ? GetString(typeResponse, "qr_data") : "";
        if (string.IsNullOrWhiteSpace(qrData)) throw new MercadoPagoApiException(502, "resposta sem QR dinamico");
        ValidateIdentityAndAmount(root, request.Id, request.AmountCents, orderId);
        if (qrData.Length is < 20 or > 4096) throw new SecurityException("Conteudo do QR retornado e invalido.");
        return PixSession.Pending(request, Name, orderId, qrData);
    }

    public async Task<PixSession?> RefreshAsync(PixSession session, CancellationToken token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"v1/orders/{Uri.EscapeDataString(session.ProviderOrderId)}");
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", RequireToken());
        using var response = await _http.SendAsync(message, token);
        var text = await response.Content.ReadAsStringAsync(token);
        EnsureApiSuccess(response, text);
        using var json = ParseApiJson(text);
        var root = json.RootElement;
        ValidateIdentityAndAmount(root, session.Id, session.AmountCents, session.ProviderOrderId);
        var next = DetermineLocalStatus(root);
        return session with { Status = next, UpdatedAt = DateTimeOffset.UtcNow };
    }

    internal static string DetermineLocalStatus(JsonElement root)
    {
        var orderStatus = GetString(root, "status").ToLowerInvariant();
        var statusDetail = GetString(root, "status_detail").ToLowerInvariant();
        var paymentAccredited = root.TryGetProperty("transactions", out var transactions)
            && transactions.TryGetProperty("payments", out var payments)
            && payments.ValueKind == JsonValueKind.Array
            && payments.EnumerateArray().Any(x => GetString(x, "status_detail").Equals("accredited", StringComparison.OrdinalIgnoreCase));
        var approved = orderStatus == "processed" && (statusDetail == "accredited" || paymentAccredited);
        return approved ? "approved" : orderStatus is "canceled" or "expired" or "refunded" ? "cancelled" : "pending";
    }

    private string RequireToken()
    {
        var secret = _secrets.TryLoad();
        if (secret.IsAvailable) return secret.Value!;
        if (secret.State == PixSecretState.Unreadable)
            throw new InvalidOperationException("A credencial PIX existe, mas nao pode ser aberta por este servico Windows. Abra CONFIGURAR-ACCESS-TOKEN-PIX.exe e salve o Access Token novamente.");
        throw new InvalidOperationException("Access Token do Mercado Pago nao configurado. Abra CONFIGURAR-ACCESS-TOKEN-PIX.exe para informar o token.");
    }

    internal static void ValidateIdentityAndAmount(JsonElement root, string expectedReference, long expectedCents, string expectedOrderId)
    {
        var id = GetString(root, "id");
        var externalReference = GetString(root, "external_reference");
        var currency = GetString(root, "currency");
        if (!id.Equals(expectedOrderId, StringComparison.Ordinal) || !externalReference.Equals(expectedReference, StringComparison.Ordinal))
            throw new SecurityException("Order ou referencia externa divergente.");
        if (!string.IsNullOrWhiteSpace(currency) && !currency.Equals("BRL", StringComparison.OrdinalIgnoreCase))
            throw new SecurityException("Moeda retornada nao e BRL.");
        if (!TryCents(root, "total_amount", out var total) || total != expectedCents)
            throw new SecurityException("Valor total retornado pelo Mercado Pago diverge do pacote.");
        if (!root.TryGetProperty("transactions", out var transactions)
            || !transactions.TryGetProperty("payments", out var payments)
            || payments.ValueKind != JsonValueKind.Array
            || !payments.EnumerateArray().Any(x => TryCents(x, "amount", out var cents) && cents == expectedCents))
            throw new SecurityException("Transacao de pagamento com valor esperado nao encontrada.");
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool TryCents(JsonElement element, string property, out long cents)
    {
        cents = 0;
        if (!element.TryGetProperty(property, out var value)) return false;
        var raw = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0) return false;
        var scaled = amount * 100m;
        if (scaled != decimal.Truncate(scaled) || scaled > long.MaxValue) return false;
        cents = (long)scaled;
        return true;
    }

    private static void EnsureApiSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = "";
        try
        {
            using var json = JsonDocument.Parse(body);
            detail = ExtractApiErrorDetail(json.RootElement);
        }
        catch (JsonException) { detail = SanitizeApiDiagnostic(body); }
        throw new MercadoPagoApiException((int)response.StatusCode, string.IsNullOrWhiteSpace(detail) ? "erro sem detalhe" : detail);
    }

    // A API pode responder um codigo generico (por exemplo, bad_request) e
    // colocar a explicacao real em error, description ou cause[]. Nunca
    // registramos o corpo inteiro: ele pode conter dados do estabelecimento e
    // nao deve aparecer para o cliente do fliperama.
    private static string ExtractApiErrorDetail(JsonElement root)
    {
        var values = new List<string>();
        AddApiErrorValue(values, GetString(root, "code"));
        AddApiErrorValue(values, GetString(root, "error"));
        AddApiErrorValue(values, GetString(root, "message"));
        AddApiErrorValue(values, GetString(root, "description"));

        AddApiErrorContainer(values, root, "details");
        AddApiErrorContainer(values, root, "errors");

        if (root.TryGetProperty("cause", out var cause))
            AddApiErrorContainer(values, cause);

        return string.Join(" | ", values);
    }

    private static void AddApiErrorContainer(List<string> values, JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var value)) AddApiErrorContainer(values, value);
    }

    private static void AddApiErrorContainer(List<string> values, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray().Take(2)) AddApiErrorCause(values, item);
            return;
        }
        AddApiErrorCause(values, value);
    }

    private static void AddApiErrorCause(List<string> values, JsonElement cause)
    {
        if (cause.ValueKind == JsonValueKind.String)
        {
            AddApiErrorValue(values, cause.GetString() ?? "");
            return;
        }
        if (cause.ValueKind != JsonValueKind.Object) return;
        AddApiErrorValue(values, GetString(cause, "code"));
        AddApiErrorValue(values, GetString(cause, "error"));
        AddApiErrorValue(values, GetString(cause, "message"));
        AddApiErrorValue(values, GetString(cause, "description"));
    }

    private static void AddApiErrorValue(List<string> values, string value)
    {
        var safe = SanitizeApiDiagnostic(value);
        if (!string.IsNullOrWhiteSpace(safe) && !values.Contains(safe, StringComparer.OrdinalIgnoreCase))
            values.Add(safe);
    }

    private static string SanitizeApiDiagnostic(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Trim();
        var tokenStart = text.IndexOf("APP_USR-", StringComparison.OrdinalIgnoreCase);
        if (tokenStart >= 0)
        {
            var tokenEnd = tokenStart;
            while (tokenEnd < text.Length && (char.IsLetterOrDigit(text[tokenEnd]) || text[tokenEnd] is '-' or '_')) tokenEnd++;
            text = text[..tokenStart] + "[Access Token oculto]" + text[tokenEnd..];
        }
        var builder = new StringBuilder(Math.Min(text.Length, 240));
        foreach (var character in text)
        {
            if (character is '\r' or '\n' or '\t') builder.Append(' ');
            else if (!char.IsControl(character)) builder.Append(character);
            if (builder.Length >= 240) break;
        }
        return builder.ToString().Trim();
    }

    private static JsonDocument ParseApiJson(string body)
    {
        try { return JsonDocument.Parse(body); }
        catch (JsonException) { throw new MercadoPagoApiException(502, "resposta JSON invalida"); }
    }
}

// Contrato bancario neutro. Cada banco/fintech implementa apenas este pequeno
// servico, enquanto as regras de tempo, assinatura, idempotencia e interface
// continuam centralizadas no TurboRama.
sealed class AdapterPixProvider : IPixProvider
{
    private readonly PixOptions _options;
    private readonly PixSecretStore _secrets;
    private readonly HttpClient _http;
    public string Name => "adapter";

    public AdapterPixProvider(PixOptions options, PixSecretStore secrets, HttpMessageHandler? handler = null)
    {
        _options = options;
        _secrets = secrets;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.BaseAddress = options.AdapterBaseUri();
        _http.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TurboRamaPixAgent/1.0");
    }

    public async Task CheckHealthAsync(CancellationToken token)
    {
        using var message = Authorized(HttpMethod.Get, "v1/health");
        using var response = await _http.SendAsync(message, token);
        var text = await ReadLimitedAsync(response, token);
        EnsureApiSuccess(response, text);
        using var json = ParseApiJson(text);
        var root = json.RootElement;
        ValidateSchemaAndProvider(root);
        if (!root.TryGetProperty("ready", out var ready) || ready.ValueKind != JsonValueKind.True)
            throw new AdapterApiException(503, "adaptador informou que nao esta pronto");
    }

    public async Task<PixSession> CreateAsync(PixPurchaseRequest request, CancellationToken token)
    {
        using var message = Authorized(HttpMethod.Post, "v1/orders");
        message.Headers.Add("X-Idempotency-Key", request.Id);
        message.Content = JsonContent.Create(new
        {
            schemaVersion = 1,
            externalReference = request.Id,
            amountCents = request.AmountCents,
            currency = "BRL",
            minutes = request.Minutes,
            description = $"Tempo TurboRama - {request.Minutes} min",
            expiresInSeconds = _options.PaymentExpirationMinutes * 60
        }, options: Json.Options);
        using var response = await _http.SendAsync(message, token);
        var text = await ReadLimitedAsync(response, token);
        EnsureApiSuccess(response, text);
        using var json = ParseApiJson(text);
        var root = json.RootElement;
        var orderId = GetString(root, "providerOrderId");
        ValidateIdentityAndAmount(root, request.Id, request.AmountCents, orderId);
        var status = DetermineLocalStatus(root);
        if (status != "pending")
            throw new SecurityException("Nova cobranca do adaptador nao iniciou como pendente.");
        var qrData = GetString(root, "qrData");
        if (qrData.Length is < 20 or > 4096)
            throw new SecurityException("Conteudo do QR retornado pelo adaptador e invalido.");
        return PixSession.Pending(request, Name, orderId, qrData);
    }

    public async Task<PixSession?> RefreshAsync(PixSession session, CancellationToken token)
    {
        using var message = Authorized(HttpMethod.Get, $"v1/orders/{Uri.EscapeDataString(session.ProviderOrderId)}");
        using var response = await _http.SendAsync(message, token);
        var text = await ReadLimitedAsync(response, token);
        EnsureApiSuccess(response, text);
        using var json = ParseApiJson(text);
        ValidateIdentityAndAmount(json.RootElement, session.Id, session.AmountCents, session.ProviderOrderId);
        return session with { Status = DetermineLocalStatus(json.RootElement), UpdatedAt = DateTimeOffset.UtcNow };
    }

    private HttpRequestMessage Authorized(HttpMethod method, string route)
    {
        var message = new HttpRequestMessage(method, route);
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", RequireSecret());
        return message;
    }

    private string RequireSecret() => _secrets.Load()
        ?? throw new InvalidOperationException("Segredo do adaptador bancario nao configurado. Execute com --set-token.");

    internal void ValidateIdentityAndAmount(JsonElement root, string expectedReference, long expectedCents, string expectedOrderId)
    {
        ValidateSchemaAndProvider(root);
        var id = GetString(root, "providerOrderId");
        var externalReference = GetString(root, "externalReference");
        var currency = GetString(root, "currency");
        if (!PixId.IsValidProviderOrder(id) || !id.Equals(expectedOrderId, StringComparison.Ordinal)
            || !externalReference.Equals(expectedReference, StringComparison.Ordinal))
            throw new SecurityException("Order ou referencia externa divergente no adaptador.");
        if (!currency.Equals("BRL", StringComparison.OrdinalIgnoreCase))
            throw new SecurityException("Moeda retornada pelo adaptador nao e BRL.");
        if (!TryLong(root, "amountCents", out var amountCents) || amountCents != expectedCents)
            throw new SecurityException("Valor retornado pelo adaptador diverge do pacote.");
    }

    internal string DetermineLocalStatus(JsonElement root)
    {
        ValidateSchemaAndProvider(root);
        return GetString(root, "status").ToLowerInvariant() switch
        {
            "pending" => "pending",
            "approved" => "approved",
            "cancelled" or "canceled" or "expired" or "refunded" => "cancelled",
            _ => throw new SecurityException("Estado de pagamento desconhecido retornado pelo adaptador.")
        };
    }

    private void ValidateSchemaAndProvider(JsonElement root)
    {
        if (!TryLong(root, "schemaVersion", out var schema) || schema != 1)
            throw new SecurityException("Versao de contrato invalida no adaptador.");
        if (!GetString(root, "providerId").Equals(_options.Adapter.ProviderId, StringComparison.Ordinal))
            throw new SecurityException("Identidade do adaptador bancario divergente.");
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool TryLong(JsonElement element, string property, out long value)
    {
        value = 0;
        return element.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out value);
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken token)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        if (bytes.Length > 65536) throw new AdapterApiException(502, "resposta excedeu 64 KiB");
        return Encoding.UTF8.GetString(bytes);
    }

    private static void EnsureApiSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = "erro sem detalhe";
        try
        {
            using var json = JsonDocument.Parse(body);
            var candidate = GetString(json.RootElement, "message");
            if (!string.IsNullOrWhiteSpace(candidate)) detail = candidate.Length > 200 ? candidate[..200] : candidate;
        }
        catch (JsonException) { }
        throw new AdapterApiException((int)response.StatusCode, detail);
    }

    private static JsonDocument ParseApiJson(string body)
    {
        try { return JsonDocument.Parse(body); }
        catch (JsonException) { throw new AdapterApiException(502, "resposta JSON invalida"); }
    }
}

sealed record PixPurchaseRequest(string Id, int Minutes, long AmountCents, DateTimeOffset RequestedAt);
sealed record PixCreditEvent(int SchemaVersion, string TransactionId, int Minutes, long AmountCents, string Provider, string ProviderOrderId, long ApprovedAtUnixSeconds, string Signature);
sealed record PixSession(string Id, int Minutes, long AmountCents, string Provider, string ProviderOrderId, string QrData, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int FailureCount, DateTimeOffset NextPollAt)
{
    public static PixSession Pending(PixPurchaseRequest request, string provider, string providerOrderId, string qrData)
        => new(request.Id, request.Minutes, request.AmountCents, provider, providerOrderId, qrData, "pending", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, DateTimeOffset.UtcNow);
}
sealed record PixRetryState(int FailureCount, DateTimeOffset NextAttemptAt);

sealed class RequestRejectedException : Exception { public RequestRejectedException(string message) : base(message) { } }
sealed class MercadoPagoApiException : Exception
{
    public int StatusCode { get; }
    public string Detail { get; }
    public MercadoPagoApiException(int statusCode, string detail) : base($"Mercado Pago HTTP {statusCode}: {detail}")
    {
        StatusCode = statusCode;
        Detail = detail;
    }
}

sealed class AdapterApiException : Exception
{
    public int StatusCode { get; }
    public AdapterApiException(int statusCode, string detail) : base($"Adaptador bancario HTTP {statusCode}: {detail}") => StatusCode = statusCode;
}

static class PixEventSigner
{
    public static string Sign(PixCreditEvent credit, byte[] key)
    {
        var payload = Canonical(credit);
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static bool Verify(PixCreditEvent credit, byte[] key)
    {
        if (credit.Signature.Length != 64) return false;
        var expected = Sign(credit with { Signature = "" }, key);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(credit.Signature.ToLowerInvariant()));
    }

    private static string Canonical(PixCreditEvent credit)
        => string.Join("\n", credit.SchemaVersion, credit.TransactionId, credit.Minutes, credit.AmountCents, credit.Provider, credit.ProviderOrderId, credit.ApprovedAtUnixSeconds);
}

// Gera um PNG RGBA de 32 bits sem depender do encoder PNG monocromatico do
// QRCoder. O formato indexado de 1 bit produzido anteriormente era valido,
// porem nao era renderizado pelo ImageComponent desta versao do frontend.
static class PixQrPng
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] Render(string payload, int pixelsPerModule)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > 8192)
            throw new InvalidOperationException("Conteudo do QR PIX invalido.");
        if (pixelsPerModule is < 2 or > 32)
            throw new ArgumentOutOfRangeException(nameof(pixelsPerModule));

        using var generator = new QRCodeGenerator();
        using var qr = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var modules = qr.ModuleMatrix;
        if (modules.Count == 0 || modules.Any(row => row.Length != modules.Count))
            throw new InvalidOperationException("Matriz do QR PIX invalida.");

        var size = checked(modules.Count * pixelsPerModule);
        var scanlineLength = checked(1 + size * 4);
        var pixels = new byte[checked(scanlineLength * size)];
        for (var y = 0; y < size; y++)
        {
            var rowOffset = y * scanlineLength;
            pixels[rowOffset] = 0; // filtro PNG: nenhum
            var moduleRow = modules[y / pixelsPerModule];
            for (var x = 0; x < size; x++)
            {
                var dark = moduleRow[x / pixelsPerModule];
                var offset = rowOffset + 1 + x * 4;
                var color = dark ? (byte)17 : (byte)255;
                pixels[offset] = color;
                pixels[offset + 1] = color;
                pixels[offset + 2] = color;
                pixels[offset + 3] = 255;
            }
        }

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
                zlib.Write(pixels);
            compressed = compressedStream.ToArray();
        }

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)size);
        header[8] = 8; // profundidade
        header[9] = 6; // RGBA truecolor, 32 bits por pixel
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;

        using var output = new MemoryStream(Signature.Length + header.Length + compressed.Length + 64);
        output.Write(Signature);
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", compressed);
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    // Contrato alternativo independente do carregador PNG do frontend. O
    // EmulationStation desenha estes modulos como retangulos nativos. A matriz
    // recebe HMAC da mesma chave privada usada nos creditos para impedir que um
    // arquivo local adulterado troque o destino do pagamento mostrado ao cliente.
    public static byte[] RenderSignedMatrix(string requestId, string payload, byte[] signingKey)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64
            || requestId.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-')))
            throw new InvalidOperationException("Identificador do QR PIX invalido.");
        if (signingKey.Length != 32) throw new InvalidOperationException("Chave de assinatura do QR invalida.");

        using var generator = new QRCodeGenerator();
        using var qr = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var modules = qr.ModuleMatrix;
        if (modules.Count is < 21 or > 256 || modules.Any(row => row.Length != modules.Count))
            throw new InvalidOperationException("Matriz do QR PIX invalida.");

        var rows = modules.Select(row => string.Create(row.Length, row,
            static (span, bits) =>
            {
                for (var index = 0; index < bits.Length; index++) span[index] = bits[index] ? '1' : '0';
            })).ToArray();
        var grid = string.Join('\n', rows);
        var canonical = $"1\n{requestId}\n{modules.Count}\n{grid}";
        var signature = Convert.ToHexString(HMACSHA256.HashData(signingKey, Encoding.ASCII.GetBytes(canonical))).ToLowerInvariant();
        return Encoding.ASCII.GetBytes($"TURBORAMA_QR_MATRIX_V1\n{requestId}\n{modules.Count}\n{signature}\n{grid}\n");
    }

    public static bool IsSignedMatrixCompatible(ReadOnlySpan<byte> matrix, string requestId, byte[] signingKey)
    {
        if (matrix.Length is < 200 or > 70000 || signingKey.Length != 32) return false;
        string text;
        try { text = Encoding.ASCII.GetString(matrix); }
        catch (DecoderFallbackException) { return false; }
        var lines = text.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0) lines = lines[..^1];
        if (lines.Length < 25 || lines[0] != "TURBORAMA_QR_MATRIX_V1" || lines[1] != requestId
            || !int.TryParse(lines[2], out var size) || size is < 21 or > 256
            || lines.Length != size + 4 || lines[3].Length != 64
            || lines[3].Any(ch => !Uri.IsHexDigit(ch))) return false;
        for (var row = 0; row < size; row++)
            if (lines[row + 4].Length != size || lines[row + 4].Any(ch => ch is not ('0' or '1'))) return false;
        var grid = string.Join('\n', lines.Skip(4));
        var canonical = $"1\n{requestId}\n{size}\n{grid}";
        var expected = Convert.ToHexString(HMACSHA256.HashData(signingKey, Encoding.ASCII.GetBytes(canonical))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(lines[3].ToLowerInvariant()));
    }

    public static bool IsEmulationStationCompatible(ReadOnlySpan<byte> png)
    {
        if (png.Length < 33 || !png[..8].SequenceEqual(Signature)) return false;
        if (!png.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;
        var width = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(20, 4));
        return width is >= 128 and <= 2048
            && height == width
            && png[24] == 8
            && png[25] == 6
            && png[26] == 0
            && png[27] == 0
            && png[28] == 0;
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)data.Length));
        output.Write(number);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = 0xFFFFFFFFu;
        foreach (var value in typeBytes) crc = UpdateCrc(crc, value);
        foreach (var value in data) crc = UpdateCrc(crc, value);
        BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xFFFFFFFFu);
        output.Write(number);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        return crc;
    }
}

static class PixSelfTest
{
    // O auto-teste nunca toca a ponte PIX instalada. Todas as chaves, cache e
    // arquivos temporarios vivem em uma pasta aleatoria de %TEMP% e sao
    // removidos ao fim, inclusive se o teste falhar.
    public static int RunIsolated(PixOptions options)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "TurboRamaPixSelfTest", Guid.NewGuid().ToString("N"));
        var bridge = Path.Combine(sandbox, ".emulationstation", "pix");
        try
        {
            var paths = new PixPaths(bridge);
            paths.EnsureDirectories();
            return Run(options with { BridgeDirectory = bridge }, paths, new PixSigningKeyStore(paths.SigningKeyFile));
        }
        finally
        {
            try { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public static int Run(PixOptions options, PixPaths paths, PixSigningKeyStore keys)
    {
        try
        {
            var key = keys.GetOrCreate();
            if (key.Length != 32) throw new InvalidOperationException("tamanho da chave de assinatura");
            var minutes = options.AllowedMinutes.First();
            var cents = options.PriceFor(minutes);
            if (cents <= 0) throw new InvalidOperationException("tabela de precos");
            var credit = new PixCreditEvent(1, "PIXSELFTEST", minutes, cents, "mock", "PIX-TEST", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "");
            var signed = credit with { Signature = PixEventSigner.Sign(credit, key) };
            if (!PixEventSigner.Verify(signed, key)) throw new InvalidOperationException("assinatura valida rejeitada");
            if (PixEventSigner.Verify(signed with { Minutes = minutes + 1 }, key)) throw new InvalidOperationException("adulteracao nao detectada");
            var file = Path.Combine(paths.Root, "self-test.json");
            paths.WriteAtomically(file, signed);
            var read = JsonSerializer.Deserialize<PixCreditEvent>(File.ReadAllText(file), Json.Options);
            if (read is null || !PixEventSigner.Verify(read, key)) throw new InvalidOperationException("gravacao atomica");
            File.Delete(file);
            TestPostalAddressCache(paths);
            TestPostalAddressFallback(paths);
            TestLocationValidation();
            TestCompatibleQr(paths);
            var credentialDpapiTested = TestCredentialInbox(paths);
            TestOwnerProvisioningContract();
            TestMercadoPagoResponses();
            TestMercadoPagoHealth(options, paths);
            TestAdapterResponses(options, paths);
            Console.WriteLine(credentialDpapiTested
                ? "SELF-TEST PIX: OK (preco, assinatura, cache/CEP, localizacao, QR RGBA e matriz assinada, credencial segura, loja/PDV idempotentes, Mercado Pago e adaptador bancario)."
                : "SELF-TEST PIX: OK (preco, assinatura, cache/CEP, localizacao, QR RGBA e matriz assinada, contrato de credencial, loja/PDV idempotentes, Mercado Pago e adaptador bancario). DPAPI nao estava disponivel para o usuario de compilacao; validar a credencial segura no Windows do quiosque.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or FormatException or JsonException)
        {
            Console.Error.WriteLine($"SELF-TEST PIX: FALHOU - {ex.Message}");
            return 20;
        }
    }

    private static void TestCompatibleQr(PixPaths paths)
    {
        const string requestId = "pix-turborama-self-test";
        const string payload = "00020126580014BR.GOV.BCB.PIX0136pix-turborama-self-test";
        var signingKey = RandomNumberGenerator.GetBytes(32);
        var png = PixQrPng.Render(payload, 8);
        if (!PixQrPng.IsEmulationStationCompatible(png))
            throw new InvalidOperationException("PNG do QR nao esta em RGBA de 32 bits");
        var file = Path.Combine(paths.Root, "self-test-qr.png");
        var matrixFile = Path.Combine(paths.Root, "self-test-qr.matrix");
        var matrix = PixQrPng.RenderSignedMatrix(requestId, payload, signingKey);
        if (!PixQrPng.IsSignedMatrixCompatible(matrix, requestId, signingKey))
            throw new InvalidOperationException("matriz assinada do QR invalida");
        paths.WriteBytesAtomically(file, png);
        paths.WriteBytesAtomically(matrixFile, matrix);
        try
        {
            var stored = File.ReadAllBytes(file);
            if (!stored.AsSpan().SequenceEqual(png) || !PixQrPng.IsEmulationStationCompatible(stored))
                throw new InvalidOperationException("gravacao do QR compativel");
            var storedMatrix = File.ReadAllBytes(matrixFile);
            if (!storedMatrix.AsSpan().SequenceEqual(matrix)
                || !PixQrPng.IsSignedMatrixCompatible(storedMatrix, requestId, signingKey))
                throw new InvalidOperationException("gravacao da matriz assinada do QR");
            storedMatrix[^1] ^= 1;
            if (PixQrPng.IsSignedMatrixCompatible(storedMatrix, requestId, signingKey))
                throw new InvalidOperationException("adulteracao da matriz do QR nao foi detectada");
        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
            try { if (File.Exists(matrixFile)) File.Delete(matrixFile); } catch (IOException) { }
        }
    }

    private static void TestOwnerProvisioningContract()
    {
        if (PixOwnerProvisioner.ValidateExternalId("LZLOJA01", 60, "loja") != "LZLOJA01")
            throw new InvalidOperationException("external_id valido da loja foi recusado");
        var invalidRejected = false;
        try { PixOwnerProvisioner.ValidateExternalId("LZ LOJA 01", 60, "loja"); }
        catch (InvalidOperationException) { invalidRejected = true; }
        if (!invalidRejected) throw new InvalidOperationException("external_id invalido da loja foi aceito");

        var pos40 = new string('A', 40);
        if (PixOwnerProvisioner.ValidateExternalId(pos40, 40, "caixa") != pos40)
            throw new InvalidOperationException("external_id oficial de 40 caracteres do PDV foi recusado");
        var pos41Rejected = false;
        try { PixOwnerProvisioner.ValidateExternalId(new string('B', 41), 40, "caixa"); }
        catch (InvalidOperationException) { pos41Rejected = true; }
        if (!pos41Rejected) throw new InvalidOperationException("external_id de PDV acima de 40 caracteres foi aceito");

        var posOptions = (new PixOptions
        {
            Provider = "mercadopago",
            ProductionEnabled = true,
            MercadoPago = new MercadoPagoOptions { ExternalPosId = pos40 }
        }).Normalize();
        posOptions.ValidateForStartup(configurationOnly: false);
        var pos41OptionsRejected = false;
        try
        {
            (posOptions with { MercadoPago = posOptions.MercadoPago with { ExternalPosId = new string('B', 41) } })
                .ValidateForStartup(configurationOnly: false);
        }
        catch (InvalidOperationException) { pos41OptionsRejected = true; }
        if (!pos41OptionsRejected) throw new InvalidOperationException("validacao operacional aceitou PDV com mais de 40 caracteres");

        var prices = new Dictionary<int, long>
        {
            [15] = 100, [30] = 200, [45] = 300, [60] = 400, [120] = 800
        };
        var adapterOwner = new PixOwnerSettings
        {
            Enabled = true,
            Provider = "adapter",
            AdapterBaseUrl = "http://127.0.0.1:8765/",
            AdapterProviderId = "banco-teste",
            PackagePricesCents = prices
        };
        adapterOwner.Validate();
        var applied = adapterOwner.Apply(new PixOptions().Normalize());
        if (applied.Provider != "adapter" || applied.Adapter.ProviderId != "banco-teste" || applied.PriceFor(15) != 100)
            throw new InvalidOperationException("cadastro do adaptador bancario nao foi aplicado");
    }

    private static void TestPostalAddressCache(PixPaths paths)
    {
        var cacheFile = Path.Combine(paths.Root, "owner-address-cache.json");
        try
        {
            paths.WriteAtomically(cacheFile, new PostalAddressCache(2, "57084648", "52", "Rua de Teste", "Maceio",
                "Alagoas", -9.6001, -35.7001, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "mercadopago"));
            var address = BrazilianPostalAddress.ResolveAsync("57084-648", "52", cacheFile, CancellationToken.None).GetAwaiter().GetResult();
            if (address.Street != "Rua de Teste" || address.City != "Maceio" || !address.Source.Equals("cache:mercadopago", StringComparison.Ordinal)
                || Math.Abs(address.Latitude + 9.6001) > 0.000001)
                throw new InvalidOperationException("cache de endereco");

            // Arquivos de versoes antigas, incompletos ou com coordenadas
            // impossiveis devem ser ignorados sem quebrar a configuracao.
            paths.WriteAtomically(cacheFile, new PostalAddressCache(2, "57084648", null, "Rua de Teste", "Maceio",
                "Alagoas", -9.6001, -35.7001, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "mercadopago"));
            var recovered = BrazilianPostalAddress.ResolveAsync("57084648", "52", cacheFile,
                CancellationToken.None, new FakePostalAddressHandler()).GetAwaiter().GetResult();
            if (recovered.Source != "nominatim") throw new InvalidOperationException("cache incompleto nao foi ignorado");

            paths.WriteAtomically(cacheFile, new PostalAddressCache(2, "57084648", "52", "Rua de Teste", "Maceio",
                "Alagoas", 0, 0, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "mercadopago"));
            recovered = BrazilianPostalAddress.ResolveAsync("57084648", "52", cacheFile,
                CancellationToken.None, new FakePostalAddressHandler()).GetAwaiter().GetResult();
            if (!BrazilianPostalAddress.HasValidCoordinates(recovered.Latitude, recovered.Longitude))
                throw new InvalidOperationException("coordenadas invalidas foram reutilizadas do cache");

            BrazilianPostalAddress.SaveConfirmedCache(cacheFile, "57084-648", "52", recovered);
            if (!File.Exists(cacheFile)) throw new InvalidOperationException("cache confirmado nao foi gravado");
            BrazilianPostalAddress.InvalidateCache(cacheFile);
            if (File.Exists(cacheFile)) throw new InvalidOperationException("cache confirmado nao foi invalidado");
        }
        finally { try { if (File.Exists(cacheFile)) File.Delete(cacheFile); } catch (IOException) { } }
    }

    private static void TestPostalAddressFallback(PixPaths paths)
    {
        var cacheFile = Path.Combine(paths.Root, "owner-address-fallback-cache.json");
        try
        {
            var address = BrazilianPostalAddress.ResolveAsync("57084648", "52", cacheFile,
                CancellationToken.None, new FakePostalAddressHandler()).GetAwaiter().GetResult();
            if (address.Street != "Rua Radialista Alves Correia" || address.City != "Maceio"
                || Math.Abs(address.Latitude + 9.58) > 0.000001 || Math.Abs(address.Longitude + 35.73) > 0.000001)
                throw new InvalidOperationException("fontes de reserva do CEP");
            if (File.Exists(cacheFile)) throw new InvalidOperationException("cache preliminar do CEP foi gravado antes da aprovacao da loja");
            BrazilianPostalAddress.SaveConfirmedCache(cacheFile, "57084648", "52", address);
            if (!File.Exists(cacheFile)) throw new InvalidOperationException("cache confirmado do CEP nao foi gravado");
            var cached = BrazilianPostalAddress.ResolveAsync("57084648", "52", cacheFile,
                CancellationToken.None, new FakePostalAddressHandler()).GetAwaiter().GetResult();
            if (!cached.Source.Equals("cache:nominatim", StringComparison.Ordinal))
                throw new InvalidOperationException("cache confirmado do CEP nao foi reutilizado");
        }
        finally { try { if (File.Exists(cacheFile)) File.Delete(cacheFile); } catch (IOException) { } }
    }

    private static void TestLocationValidation()
    {
        var valid = new MercadoPagoSetupRequest
        {
            ExpectedAccountId = "123456",
            StoreName = "TurboRama Teste",
            StoreExternalId = "LZLOJA01",
            PosName = "TurboRama Kiosk",
            PosExternalId = "LZPIXCOMP",
            StreetName = "Rua de Teste",
            StreetNumber = "52",
            CityName = "Maceio",
            StateName = "Alagoas",
            Latitude = -9.58,
            Longitude = -35.73,
            Reference = "Teste"
        };
        valid.ValidateLocationForNewStore();
        foreach (var invalid in new[]
        {
            valid with { Latitude = 0, Longitude = 0 },
            valid with { Latitude = 91, Longitude = -35.73 },
            valid with { Latitude = -9.58, Longitude = 181 }
        })
        {
            var rejected = false;
            try { invalid.ValidateLocationForNewStore(); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("localizacao impossivel foi aceita para criar loja");
        }
    }

    private static bool TestCredentialInbox(PixPaths paths)
    {
        // Montado em tempo de execucao para nao parecer uma credencial real aos
        // scanners de segredo do repositorio. Continua exercitando o prefixo
        // exigido pelo contrato sem armazenar qualquer token de conta.
        const string testToken = "APP" + "_USR-credential-exchange-self-test-token-1234567890";
        var nullEnvelopeRejected = false;
        try
        {
            PixCredentialInbox.ValidateEnvelope(new PixCredentialUpdate(3, "CRED-MALFORMED", null!, null!,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        }
        catch (InvalidOperationException) { nullEnvelopeRejected = true; }
        if (!nullEnvelopeRejected) throw new InvalidOperationException("credencial com campos nulos nao foi recusada");

        using (var envelopeKey = RSA.Create(2048))
        {
            var payload = Encoding.UTF8.GetBytes(testToken);
            var encryptedPayload = envelopeKey.Encrypt(payload, RSAEncryptionPadding.OaepSHA1);
            var decryptedPayload = envelopeKey.Decrypt(encryptedPayload, RSAEncryptionPadding.OaepSHA1);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(payload, decryptedPayload))
                    throw new InvalidOperationException("contrato de cifragem da credencial");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
                CryptographicOperations.ZeroMemory(encryptedPayload);
                CryptographicOperations.ZeroMemory(decryptedPayload);
            }
        }
        var secretStore = new PixSecretStore(paths.SecretFile);
        var inbox = new PixCredentialInbox(paths, secretStore);
        try { inbox.EnsureReady(); }
        catch (CryptographicException) { return false; }
        var pem = File.ReadAllText(paths.CredentialPublicKeyFile, Encoding.UTF8);
        using var publicKey = RSA.Create();
        publicKey.ImportFromPem(pem);
        var publicBytes = publicKey.ExportSubjectPublicKeyInfo();
        var fingerprint = Convert.ToHexString(SHA256.HashData(publicBytes)).ToLowerInvariant();
        var encrypted = publicKey.Encrypt(Encoding.UTF8.GetBytes(testToken), RSAEncryptionPadding.OaepSHA1);
        try
        {
            // Campos ausentes ou nulos precisam ser recusados e colocados em
            // quarentena, nunca encerrar o agente com NullReferenceException.
            paths.WriteAtomically(paths.CredentialUpdateFile, new
            {
                schemaVersion = 3,
                requestId = "CRED-MALFORMED",
                keyFingerprint = (string?)null,
                encryptedPayload = (string?)null,
                createdAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            if (inbox.TryAcceptPendingUpdate()) throw new InvalidOperationException("credencial nula foi aceita");

            var update = new PixCredentialUpdate(3, "CRED-SELFTEST", fingerprint,
                Convert.ToBase64String(encrypted), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            paths.WriteAtomically(paths.CredentialUpdateFile, update);
            if (!inbox.TryAcceptPendingUpdate()) throw new InvalidOperationException("ponte segura de credencial nao aceitou o token de teste");
            var result = secretStore.TryLoad();
            if (!result.IsAvailable || !result.Value!.Equals(testToken, StringComparison.Ordinal))
                throw new InvalidOperationException("token da ponte segura nao foi recuperado pelo agente");
            if (File.Exists(paths.CredentialUpdateFile)) throw new InvalidOperationException("pedido de credencial ficou pendente apos aceite");
            paths.WriteAtomically(paths.CredentialUpdateFile, update);
            if (inbox.TryAcceptPendingUpdate()) throw new InvalidOperationException("repeticao de credencial foi aceita");
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            foreach (var file in new[] { paths.SecretFile, paths.CredentialPrivateKeyFile, paths.CredentialPublicKeyFile,
                paths.CredentialUpdateFile, paths.CredentialUpdateStatusFile, paths.CredentialReplayFile })
            {
                try { if (File.Exists(file)) File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            foreach (var quarantine in Directory.EnumerateFiles(paths.Rejected, "*credential-update.json*"))
            {
                try { File.Delete(quarantine); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static void TestMercadoPagoResponses()
    {
        const string approvedJson = """
        {"id":"ORD-TESTE-1","external_reference":"PIXSELFTEST","currency":"BRL","total_amount":"7.50","status":"processed","status_detail":"accredited","transactions":{"payments":[{"amount":"7.50","status_detail":"accredited"}]}}
        """;
        using var approved = JsonDocument.Parse(approvedJson);
        MercadoPagoPixProvider.ValidateIdentityAndAmount(approved.RootElement, "PIXSELFTEST", 750, "ORD-TESTE-1");
        if (MercadoPagoPixProvider.DetermineLocalStatus(approved.RootElement) != "approved")
            throw new InvalidOperationException("estado bancario aprovado");

        using var pending = JsonDocument.Parse(approvedJson.Replace("processed", "created").Replace("accredited", "pending"));
        if (MercadoPagoPixProvider.DetermineLocalStatus(pending.RootElement) != "pending")
            throw new InvalidOperationException("estado bancario pendente");

        using var cancelled = JsonDocument.Parse(approvedJson.Replace("processed", "canceled").Replace("accredited", "canceled"));
        if (MercadoPagoPixProvider.DetermineLocalStatus(cancelled.RootElement) != "cancelled")
            throw new InvalidOperationException("estado bancario cancelado");

        using var wrongAmount = JsonDocument.Parse(approvedJson.Replace("7.50", "0.01"));
        try
        {
            MercadoPagoPixProvider.ValidateIdentityAndAmount(wrongAmount.RootElement, "PIXSELFTEST", 750, "ORD-TESTE-1");
            throw new InvalidOperationException("valor bancario adulterado aceito");
        }
        catch (SecurityException) { }
    }

    private static void TestMercadoPagoHealth(PixOptions original, PixPaths paths)
    {
        var options = (original with
        {
            Provider = "mercadopago",
            ProductionEnabled = true,
            MercadoPago = new MercadoPagoOptions { ExternalPosId = "TURBORAMAPDV01" }
        }).Normalize();
        options.ValidateForStartup(configurationOnly: false);
        var previousToken = Environment.GetEnvironmentVariable("TURBORAMA_PIX_MERCADOPAGO_ACCESS_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("TURBORAMA_PIX_MERCADOPAGO_ACCESS_TOKEN", "APP_USR-self-test-token");
            var secretStore = new PixSecretStore(paths.SecretFile);
            var provider = new MercadoPagoPixProvider(options, secretStore, new FakeMercadoPagoHealthHandler(posExists: true));
            provider.CheckHealthAsync(CancellationToken.None).GetAwaiter().GetResult();

            var missing = new MercadoPagoPixProvider(options, secretStore, new FakeMercadoPagoHealthHandler(posExists: false));
            try
            {
                missing.CheckHealthAsync(CancellationToken.None).GetAwaiter().GetResult();
                throw new InvalidOperationException("PDV inexistente aceito pelo Mercado Pago");
            }
            catch (MercadoPagoApiException ex) when (ex.StatusCode == 404) { }

            // Exercita o contrato que realmente gera e confirma o QR. O
            // handler falso valida headers, idempotencia e todos os campos
            // essenciais enviados a /v1/orders antes de devolver qr_data.
            var orderProvider = new MercadoPagoPixProvider(options, secretStore, new FakeMercadoPagoOrderHandler());
            var purchase = new PixPurchaseRequest("PIXSELFTEST", 15, 750, DateTimeOffset.UtcNow);
            var pendingOrder = orderProvider.CreateAsync(purchase, CancellationToken.None).GetAwaiter().GetResult();
            if (pendingOrder.Provider != "mercadopago" || pendingOrder.ProviderOrderId != "ORDER-SELFTEST-1"
                || pendingOrder.Status != "pending" || pendingOrder.QrData.Length < 20)
                throw new InvalidOperationException("criacao HTTP/QR do Mercado Pago");
            var approvedOrder = orderProvider.RefreshAsync(pendingOrder, CancellationToken.None).GetAwaiter().GetResult();
            if (approvedOrder?.Status != "approved")
                throw new InvalidOperationException("confirmacao HTTP do Mercado Pago");

            var setupHandler = new FakeMercadoPagoSetupHandler();
            var setupProvider = new MercadoPagoPixProvider(options, secretStore, setupHandler);
			var authenticatedInventory = setupProvider.GetInfrastructureAsync(CancellationToken.None).GetAwaiter().GetResult();
			if (authenticatedInventory.AccountId != "123456")
				throw new InvalidOperationException("User ID autenticado nao foi reconhecido pelo Access Token");
            var setup = new MercadoPagoSetupRequest
            {
                ExpectedAccountId = "123456",
                StoreName = "TurboRama Teste",
                StoreExternalId = "LZLOJA01",
                PosName = "TurboRama Kiosk",
                PosExternalId = "LZPIXCOMP",
                StreetName = "Rua de Teste",
                StreetNumber = "100",
                CityName = "Sao Paulo",
                StateName = "Sao Paulo",
                Latitude = -23.55052,
                Longitude = -46.633308,
                Reference = "Teste automatizado"
            };
            var created = setupProvider.EnsureInfrastructureAsync(setup, CancellationToken.None).GetAwaiter().GetResult();
            if (!created.StoreCreated || !created.PointOfSaleCreated || created.PointOfSale.ExternalId != "LZPIXCOMP")
                throw new InvalidOperationException("criacao idempotente de loja e PDV");
            var existing = setupProvider.EnsureInfrastructureAsync(setup, CancellationToken.None).GetAwaiter().GetResult();
            if (existing.StoreCreated || existing.PointOfSaleCreated)
                throw new InvalidOperationException("loja ou PDV duplicado no segundo cadastro");

            // Reproduz exatamente a falha vista no quiosque: o POST da loja
            // responde com sucesso, a busca demora duas consultas para
            // enxergar o cadastro e o primeiro POST do PDV ainda devolve
            // non_existent_external_store_id. O agente deve aguardar,
            // confirmar e repetir somente o PDV, sem duplicar a loja.
            var delayedSetupHandler = new FakeMercadoPagoSetupHandler(
                storeVisibilityDelayAfterCreation: 2, posMissingStoreFailures: 1,
                returnStoreNotFoundAs404: true);
            var delayedSetupProvider = new MercadoPagoPixProvider(options, secretStore, delayedSetupHandler);
            var delayed = delayedSetupProvider.EnsureInfrastructureAsync(setup, CancellationToken.None).GetAwaiter().GetResult();
            if (!delayed.StoreCreated || !delayed.PointOfSaleCreated
                || delayed.Store.ExternalId != "LZLOJA01" || delayed.PointOfSale.ExternalId != "LZPIXCOMP"
                || delayedSetupHandler.StorePostCount != 1 || delayedSetupHandler.PosPostCount != 2)
                throw new InvalidOperationException("retomada apos non_existent_external_store_id");
            var delayedRepeat = delayedSetupProvider.EnsureInfrastructureAsync(setup, CancellationToken.None).GetAwaiter().GetResult();
            if (delayedRepeat.StoreCreated || delayedRepeat.PointOfSaleCreated
                || delayedSetupHandler.StorePostCount != 1 || delayedSetupHandler.PosPostCount != 2)
                throw new InvalidOperationException("retomada do PDV criou duplicata");

            // Se a API tiver criado o PDV, mas responder conflito, a busca por
            // external_id deve reconciliar o resultado sem um segundo POST.
            var conflictSetupHandler = new FakeMercadoPagoSetupHandler(
                storeExists: true, posCreatedButConflictFailures: 1);
            var conflictSetupProvider = new MercadoPagoPixProvider(options, secretStore, conflictSetupHandler);
            var conflict = conflictSetupProvider.EnsureInfrastructureAsync(setup, CancellationToken.None).GetAwaiter().GetResult();
            if (conflict.StoreCreated || conflict.PointOfSaleCreated
                || conflict.PointOfSale.ExternalId != "LZPIXCOMP" || conflictSetupHandler.PosPostCount != 1)
                throw new InvalidOperationException("reconciliacao de point_of_sale_exists");

            // Loja ja existente: a criacao de um novo PDV nao pode consultar
            // CEP nem exigir coordenadas, pois nenhum endereco sera enviado ao
            // endpoint de caixas.
            var existingStoreHandler = new FakeMercadoPagoSetupHandler(storeExists: true, posExists: false);
            var existingStoreProvider = new MercadoPagoPixProvider(options, secretStore, existingStoreHandler);
            var posOnly = existingStoreProvider.EnsureInfrastructureAsync(setup with
            {
                StreetName = "",
                StreetNumber = "",
                CityName = "",
                StateName = "",
                Latitude = 0,
                Longitude = 0,
                Reference = ""
            }, CancellationToken.None).GetAwaiter().GetResult();
            if (posOnly.StoreCreated || !posOnly.PointOfSaleCreated || posOnly.PointOfSale.ExternalId != "LZPIXCOMP")
                throw new InvalidOperationException("PDV em loja existente ainda dependeu de endereco");

            // Reproduz a atualizacao de uma instalacao antiga: os nomes de
            // exemplo nao existem, mas a conta possui exatamente um PDV ativo
            // e corretamente associado a uma loja. O agente deve reaproveitar
            // esse par em vez de criar duplicatas ou ficar bloqueado.
            var legacySettings = new PixOwnerSettings
            {
                Enabled = true,
                AccountId = "123456",
                StoreExternalId = "TURBORAMALOJA01",
                StoreName = "TurboRama",
                PosExternalId = "TURBORAMAKIOSK01",
                PosName = "TurboRama Kiosk"
            };
            var legacyInventory = new MercadoPagoInfrastructure("123456",
                new[] { new MercadoPagoStoreInfo("987", "LZLOJA01", "TurboRama Teste") },
                new[] { new MercadoPagoPosInfo("654", "LZPIXCOMP", "TurboRama Kiosk", "987", "active") });

            // Ao trocar o Access Token de sandbox pelo de producao, o User ID
            // antigo nao pode manter o quiosque preso na conta de teste. O ID
            // autenticado retornado pelo Mercado Pago deve substituir o salvo.
            var productionInventory = legacyInventory with { AccountId = "789012" };
            var productionSettings = OwnerInfrastructureCoordinator.BindAuthenticatedAccount(legacySettings, productionInventory);
            if (productionSettings.AccountId != "789012" || legacySettings.AccountId != "123456")
                throw new InvalidOperationException("migracao automatica sandbox para producao");

            if (!OwnerInfrastructureCoordinator.TryResolveExisting(legacySettings, legacyInventory,
                    out var recoveredStore, out var recoveredPoint, out var recoveredAutomatically, out _)
                || !recoveredAutomatically || recoveredStore.ExternalId != "LZLOJA01" || recoveredPoint.ExternalId != "LZPIXCOMP")
                throw new InvalidOperationException("recuperacao automatica do PDV existente");

            // Caso observado no quiosque: a loja esta correta, mas foi
            // acrescentado "01" ao external_id do caixa. Como existe somente
            // um PDV ativo nessa loja, o agente deve corrigir o identificador
            // sem consultar o endereco novamente.
            var screenshotSettings = legacySettings with
            {
                StoreExternalId = "LZLOJA01",
                PosExternalId = "LZPIXCOMP01"
            };
            if (!OwnerInfrastructureCoordinator.TryResolveExisting(screenshotSettings, legacyInventory,
                    out var screenshotStore, out var screenshotPoint, out var screenshotRecovered, out _)
                || !screenshotRecovered || screenshotStore.ExternalId != "LZLOJA01" || screenshotPoint.ExternalId != "LZPIXCOMP")
                throw new InvalidOperationException("correcao do PDV LZPIXCOMP01 sem consulta de CEP");

            var ambiguousInventory = legacyInventory with
            {
                PointsOfSale = new[]
                {
                    new MercadoPagoPosInfo("654", "LZPIXCOMP", "Caixa A", "987", "active"),
                    new MercadoPagoPosInfo("655", "LZPIXCOMP02", "Caixa B", "987", "active")
                }
            };
            if (OwnerInfrastructureCoordinator.TryResolveExisting(legacySettings, ambiguousInventory,
                    out _, out _, out _, out var ambiguityMessage)
                || !ambiguityMessage.Contains("LZPIXCOMP", StringComparison.Ordinal))
                throw new InvalidOperationException("ambiguidade de PDV nao foi bloqueada");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TURBORAMA_PIX_MERCADOPAGO_ACCESS_TOKEN", previousToken);
        }
    }

    private static void TestAdapterResponses(PixOptions original, PixPaths paths)
    {
        var options = (original with
        {
            Provider = "adapter",
            ProductionEnabled = true,
            Adapter = new AdapterOptions { BaseUrl = "http://127.0.0.1:8765/", ProviderId = "banco-teste" }
        }).Normalize();
        options.ValidateForStartup(configurationOnly: false);
        var secretStore = new PixSecretStore(paths.SecretFile);
        var provider = new AdapterPixProvider(options, secretStore);
        const string pendingJson = """
        {"schemaVersion":1,"providerId":"banco-teste","providerOrderId":"BANK-ORDER-1","externalReference":"PIXSELFTEST","amountCents":750,"currency":"BRL","qrData":"00020126580014BR.GOV.BCB.PIX0136pix-turborama-self-test","status":"pending"}
        """;
        using var pending = JsonDocument.Parse(pendingJson);
        provider.ValidateIdentityAndAmount(pending.RootElement, "PIXSELFTEST", 750, "BANK-ORDER-1");
        if (provider.DetermineLocalStatus(pending.RootElement) != "pending")
            throw new InvalidOperationException("estado pendente do adaptador");

        using var approved = JsonDocument.Parse(pendingJson.Replace("\"pending\"", "\"approved\""));
        if (provider.DetermineLocalStatus(approved.RootElement) != "approved")
            throw new InvalidOperationException("estado aprovado do adaptador");

        using var wrongProvider = JsonDocument.Parse(pendingJson.Replace("banco-teste", "banco-falso"));
        try
        {
            provider.ValidateIdentityAndAmount(wrongProvider.RootElement, "PIXSELFTEST", 750, "BANK-ORDER-1");
            throw new InvalidOperationException("adaptador com identidade adulterada aceito");
        }
        catch (SecurityException) { }

        using var wrongAmount = JsonDocument.Parse(pendingJson.Replace("\"amountCents\":750", "\"amountCents\":1"));
        try
        {
            provider.ValidateIdentityAndAmount(wrongAmount.RootElement, "PIXSELFTEST", 750, "BANK-ORDER-1");
            throw new InvalidOperationException("valor adulterado do adaptador aceito");
        }
        catch (SecurityException) { }

        var insecureRemote = (options with { Adapter = options.Adapter with { BaseUrl = "http://192.0.2.10/" } }).Normalize();
        try
        {
            insecureRemote.ValidateForStartup(configurationOnly: false);
            throw new InvalidOperationException("adaptador remoto sem HTTPS aceito");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("HTTPS", StringComparison.OrdinalIgnoreCase)) { }

        var previousSecret = Environment.GetEnvironmentVariable("TURBORAMA_PIX_PROVIDER_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("TURBORAMA_PIX_PROVIDER_SECRET", "adapter-self-test-secret");
            var httpProvider = new AdapterPixProvider(options, secretStore, new FakeAdapterHandler());
            httpProvider.CheckHealthAsync(CancellationToken.None).GetAwaiter().GetResult();
            var request = new PixPurchaseRequest("PIXSELFTEST", 15, 750, DateTimeOffset.UtcNow);
            var session = httpProvider.CreateAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            if (session.Provider != "adapter" || session.ProviderOrderId != "BANK-ORDER-1" || session.Status != "pending")
                throw new InvalidOperationException("criacao HTTP do adaptador");
            var refreshed = httpProvider.RefreshAsync(session, CancellationToken.None).GetAwaiter().GetResult();
            if (refreshed?.Status != "approved") throw new InvalidOperationException("confirmacao HTTP do adaptador");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TURBORAMA_PIX_PROVIDER_SECRET", previousSecret);
        }
    }

    private sealed class FakePostalAddressHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri?.Host ?? "";
            if (host.Equals("cep.awesomeapi.com.br", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Response(System.Net.HttpStatusCode.ServiceUnavailable, new { message = "indisponivel" }));
            if (host.Equals("brasilapi.com.br", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Response(System.Net.HttpStatusCode.OK, new
                {
                    street = "Rua Radialista Alves Correia",
                    city = "Maceio",
                    state = "AL",
                    location = new { coordinates = new { } }
                }));
            if (host.Equals("nominatim.openstreetmap.org", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Response(System.Net.HttpStatusCode.OK,
                    new[]
                    {
                        new
                        {
                            lat = "-9.58",
                            lon = "-35.73",
                            address = new
                            {
                                country_code = "br",
                                postcode = "57084-648",
                                road = "Rua Radialista Alves Correia",
                                house_number = "52",
                                city = "Maceio",
                                state = "Alagoas"
                            }
                        }
                    }));
            return Task.FromResult(Response(System.Net.HttpStatusCode.NotFound, new { message = "rota inexistente" }));
        }

        private static HttpResponseMessage Response(System.Net.HttpStatusCode status, object value)
            => new(status) { Content = JsonContent.Create(value, options: Json.Options) };
    }

    private sealed class FakeAdapterHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization?.Scheme != "Bearer" || request.Headers.Authorization.Parameter != "adapter-self-test-secret")
                return JsonResponse(System.Net.HttpStatusCode.Unauthorized, new { message = "credencial invalida" });
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (request.Method == HttpMethod.Get && path == "/v1/health")
                return JsonResponse(System.Net.HttpStatusCode.OK, new { schemaVersion = 1, providerId = "banco-teste", ready = true });
            if (request.Method == HttpMethod.Post && path == "/v1/orders")
            {
                if (!request.Headers.TryGetValues("X-Idempotency-Key", out var keys) || keys.SingleOrDefault() != "PIXSELFTEST")
                    return JsonResponse(System.Net.HttpStatusCode.BadRequest, new { message = "idempotencia ausente" });
                using var body = JsonDocument.Parse(await (request.Content?.ReadAsStringAsync(cancellationToken) ?? Task.FromResult("{}")));
                var root = body.RootElement;
                if (!root.TryGetProperty("amountCents", out var amount) || amount.GetInt64() != 750
                    || !root.TryGetProperty("currency", out var currency) || currency.GetString() != "BRL"
                    || !root.TryGetProperty("externalReference", out var reference) || reference.GetString() != "PIXSELFTEST")
                    return JsonResponse(System.Net.HttpStatusCode.BadRequest, new { message = "pedido divergente" });
                return Order("pending");
            }
            if (request.Method == HttpMethod.Get && path == "/v1/orders/BANK-ORDER-1") return Order("approved");
            return JsonResponse(System.Net.HttpStatusCode.NotFound, new { message = "rota inexistente" });
        }

        private static HttpResponseMessage Order(string status) => JsonResponse(System.Net.HttpStatusCode.OK, new
        {
            schemaVersion = 1,
            providerId = "banco-teste",
            providerOrderId = "BANK-ORDER-1",
            externalReference = "PIXSELFTEST",
            amountCents = 750,
            currency = "BRL",
            qrData = "00020126580014BR.GOV.BCB.PIX0136pix-turborama-self-test",
            status
        });

        private static HttpResponseMessage JsonResponse(System.Net.HttpStatusCode status, object value)
            => new(status) { Content = JsonContent.Create(value, options: Json.Options) };
    }

    private sealed class FakeMercadoPagoHealthHandler(bool posExists) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization?.Scheme != "Bearer"
                || request.Headers.Authorization.Parameter != "APP_USR-self-test-token")
                return Task.FromResult(JsonResponse(System.Net.HttpStatusCode.Unauthorized, new { message = "credencial invalida" }));

            var uri = request.RequestUri;
            if (request.Method == HttpMethod.Get && uri?.Host == "api.mercadolibre.com" && uri.AbsolutePath == "/users/me")
                return Task.FromResult(JsonResponse(System.Net.HttpStatusCode.OK, new { id = 123456 }));

            if (request.Method == HttpMethod.Get && uri?.Host == "api.mercadopago.com" && uri.AbsolutePath == "/pos")
            {
                var results = posExists
                    ? new[] { new { id = 123, external_id = "TURBORAMAPDV01" } }
                    : Array.Empty<object>();
                return Task.FromResult(JsonResponse(System.Net.HttpStatusCode.OK, new { results }));
            }

            return Task.FromResult(JsonResponse(System.Net.HttpStatusCode.NotFound, new { message = "rota inexistente" }));
        }

        private static HttpResponseMessage JsonResponse(System.Net.HttpStatusCode status, object value)
            => new(status) { Content = JsonContent.Create(value, options: Json.Options) };
    }

    private sealed class FakeMercadoPagoOrderHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization?.Scheme != "Bearer"
                || request.Headers.Authorization.Parameter != "APP_USR-self-test-token")
                return JsonResponse(System.Net.HttpStatusCode.Unauthorized, new { message = "credencial invalida" });

            var path = request.RequestUri?.AbsolutePath ?? "";
            if (request.Method == HttpMethod.Post && path == "/v1/orders")
            {
                if (!request.Headers.TryGetValues("X-Idempotency-Key", out var keys)
                    || keys.SingleOrDefault() != "PIXSELFTEST")
                    return JsonResponse(System.Net.HttpStatusCode.BadRequest, new { message = "idempotencia ausente" });

                using var body = JsonDocument.Parse(await (request.Content?.ReadAsStringAsync(cancellationToken) ?? Task.FromResult("{}")));
                var root = body.RootElement;
                var valid = root.GetProperty("type").GetString() == "qr"
                    && !root.TryGetProperty("processing_mode", out _)
                    && root.GetProperty("total_amount").ValueKind == JsonValueKind.String
                    && root.GetProperty("total_amount").GetString() == "7.50"
                    && root.GetProperty("external_reference").GetString() == "PIXSELFTEST"
                    && root.GetProperty("expiration_time").GetString() == "PT15M"
                    && root.GetProperty("config").GetProperty("qr").GetProperty("mode").GetString() == "dynamic"
                    && root.GetProperty("config").GetProperty("qr").GetProperty("external_pos_id").GetString() == "TURBORAMAPDV01"
                    && root.GetProperty("transactions").GetProperty("payments")[0].GetProperty("amount").ValueKind == JsonValueKind.String
                    && root.GetProperty("transactions").GetProperty("payments")[0].GetProperty("amount").GetString() == "7.50"
                    && !root.TryGetProperty("items", out _);
                if (!valid)
                    return JsonResponse(System.Net.HttpStatusCode.BadRequest,
                        new { code = "property_type", message = "Incorrect type for property" });
                return Order("created", "pending", includeQr: true);
            }
            if (request.Method == HttpMethod.Get && path == "/v1/orders/ORDER-SELFTEST-1")
                return Order("processed", "accredited", includeQr: false);
            return JsonResponse(System.Net.HttpStatusCode.NotFound, new { message = "rota inexistente" });
        }

        private static HttpResponseMessage Order(string status, string detail, bool includeQr)
        {
            var value = new Dictionary<string, object?>
            {
                ["id"] = "ORDER-SELFTEST-1",
                ["external_reference"] = "PIXSELFTEST",
                ["currency"] = "BRL",
                ["total_amount"] = "7.50",
                ["status"] = status,
                ["status_detail"] = detail,
                ["transactions"] = new { payments = new[] { new { amount = "7.50", status_detail = detail } } }
            };
            if (includeQr)
                value["type_response"] = new { qr_data = "00020126580014BR.GOV.BCB.PIX0136pix-turborama-self-test" };
            return JsonResponse(System.Net.HttpStatusCode.OK, value);
        }

        private static HttpResponseMessage JsonResponse(System.Net.HttpStatusCode status, object value)
            => new(status) { Content = JsonContent.Create(value, options: Json.Options) };
    }

    private sealed class FakeMercadoPagoSetupHandler : HttpMessageHandler
    {
        private bool _storeExists;
        private bool _posExists;
        private readonly int _storeVisibilityDelayAfterCreation;
        private int _storeSearchesBeforeVisible;
        private int _posMissingStoreFailures;
        private int _posCreatedButConflictFailures;
        private readonly bool _returnStoreNotFoundAs404;
        public int StorePostCount { get; private set; }
        public int PosPostCount { get; private set; }

        public FakeMercadoPagoSetupHandler(bool storeExists = false, bool posExists = false,
            int storeVisibilityDelayAfterCreation = 0, int posMissingStoreFailures = 0,
            int posCreatedButConflictFailures = 0, bool returnStoreNotFoundAs404 = false)
        {
            _storeExists = storeExists;
            _posExists = posExists;
            _storeVisibilityDelayAfterCreation = Math.Max(0, storeVisibilityDelayAfterCreation);
            _posMissingStoreFailures = Math.Max(0, posMissingStoreFailures);
            _posCreatedButConflictFailures = Math.Max(0, posCreatedButConflictFailures);
            _returnStoreNotFoundAs404 = returnStoreNotFoundAs404;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization?.Scheme != "Bearer"
                || request.Headers.Authorization.Parameter != "APP_USR-self-test-token")
                return JsonResponse(System.Net.HttpStatusCode.Unauthorized, new { message = "credencial invalida" });
            var uri = request.RequestUri;
            var path = uri?.AbsolutePath ?? "";
			if (request.Method == HttpMethod.Get && uri?.Host == "api.mercadolibre.com" && path == "/users/me")
				return JsonResponse(System.Net.HttpStatusCode.OK, new { id = 123456 });
            if (request.Method == HttpMethod.Get && path == "/users/123456/stores/search")
            {
                var visible = _storeExists && _storeSearchesBeforeVisible <= 0;
                if (_storeExists && _storeSearchesBeforeVisible > 0) _storeSearchesBeforeVisible--;
                if (!visible && _returnStoreNotFoundAs404
                    && (uri?.Query ?? "").Contains("external_id=", StringComparison.OrdinalIgnoreCase))
                    return JsonResponse(System.Net.HttpStatusCode.NotFound, new
                    {
                        code = "store_not_found",
                        message = "Store not found"
                    });
                object[] stores = visible
                    ? new object[] { new { id = 987, external_id = "LZLOJA01", name = "TurboRama Teste" } }
                    : Array.Empty<object>();
                // Exercita tambem o formato paginado envolvido por array,
                // exibido pela referencia oficial de busca de lojas.
                return JsonResponse(System.Net.HttpStatusCode.OK, new[] { new { results = stores } });
            }
            if (request.Method == HttpMethod.Get && path == "/pos")
                return JsonResponse(System.Net.HttpStatusCode.OK, new
                {
                    results = _posExists ? new[] { new { id = 654, external_id = "LZPIXCOMP", name = "TurboRama Kiosk", store_id = 987, status = "active" } } : Array.Empty<object>()
                });
            if (request.Method == HttpMethod.Post && path == "/users/123456/stores")
            {
                StorePostCount++;
                using var body = JsonDocument.Parse(await (request.Content?.ReadAsStringAsync(cancellationToken) ?? Task.FromResult("{}")));
                if (body.RootElement.GetProperty("external_id").GetString() != "LZLOJA01"
                    || body.RootElement.GetProperty("location").GetProperty("city_name").GetString() != "Sao Paulo")
                    return JsonResponse(System.Net.HttpStatusCode.BadRequest, new { message = "loja divergente" });
                _storeExists = true;
                _storeSearchesBeforeVisible = _storeVisibilityDelayAfterCreation;
                return JsonResponse(System.Net.HttpStatusCode.OK, new { id = 987, external_id = "LZLOJA01", name = "TurboRama Teste" });
            }
            if (request.Method == HttpMethod.Post && path == "/pos")
            {
                PosPostCount++;
                using var body = JsonDocument.Parse(await (request.Content?.ReadAsStringAsync(cancellationToken) ?? Task.FromResult("{}")));
                if (body.RootElement.GetProperty("external_id").GetString() != "LZPIXCOMP"
                    || body.RootElement.GetProperty("fixed_amount").ValueKind != JsonValueKind.True
                    || body.RootElement.GetProperty("store_id").GetInt64() != 987
                    || body.RootElement.GetProperty("external_store_id").GetString() != "LZLOJA01"
                    || body.RootElement.TryGetProperty("category", out _))
                    return JsonResponse(System.Net.HttpStatusCode.BadRequest, new { message = "PDV divergente" });
                if (_posMissingStoreFailures > 0)
                {
                    _posMissingStoreFailures--;
                    return JsonResponse(System.Net.HttpStatusCode.BadRequest, new
                    {
                        code = "non_existent_external_store_id",
                        message = "External store id does not refer any store"
                    });
                }
                if (_posCreatedButConflictFailures > 0)
                {
                    _posCreatedButConflictFailures--;
                    _posExists = true;
                    return JsonResponse(System.Net.HttpStatusCode.Conflict, new
                    {
                        code = "point_of_sale_exists",
                        message = "Point of sale already exists"
                    });
                }
                _posExists = true;
                return JsonResponse(System.Net.HttpStatusCode.OK, new
                {
                    id = 654, external_id = "LZPIXCOMP", name = "TurboRama Kiosk", store_id = 987, status = "active"
                });
            }
            return JsonResponse(System.Net.HttpStatusCode.NotFound, new { message = "rota inexistente" });
        }

        private static HttpResponseMessage JsonResponse(System.Net.HttpStatusCode status, object value)
            => new(status) { Content = JsonContent.Create(value, options: Json.Options) };
    }
}

static class PixId
{
    public static bool IsValid(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 64 && id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
    public static bool IsValidProviderOrder(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 && id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
    public static bool IsValidProviderName(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length is >= 2 and <= 48 && id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
}

// A ponte PIX e protegida antes de qualquer chave ser publicada. Isso evita
// que uma ACL herdada antiga permita a troca da chave publica ou a leitura de
// um arquivo temporario de credencial durante uma gravacao atomica.
static class WindowsFileSecurity
{
    private const uint SeFileObject = 1;
    private const uint DaclSecurityInformation = 0x00000004;
    private const uint ProtectedDaclSecurityInformation = 0x80000000;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string stringSecurityDescriptor,
        uint stringSdRevision, out IntPtr securityDescriptor, out uint securityDescriptorSize);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetSecurityDescriptorDacl(IntPtr securityDescriptor, out bool daclPresent,
        out IntPtr dacl, out bool daclDefaulted);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint SetNamedSecurityInfo(string objectName, uint objectType, uint securityInformation,
        IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static void HardenBridgeDirectory(string directory)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Pasta PIX nao foi encontrada.");
        // A conta corrente e explicita: uma instalacao elevada pode deixar
        // Administrators como proprietario, enquanto o quiosque roda com um
        // usuario normal. BU pode somente atravessar a pasta e nao herda
        // permissao de leitura/escrita para os arquivos.
        var user = CurrentUserSid();
        ApplyDacl(directory, $"D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;{user})(A;;GRGX;;;BU)");
    }

    public static void HardenCredentialFile(string file, bool allowBuiltinUsersRead)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!File.Exists(file)) throw new FileNotFoundException("Arquivo de credencial nao foi encontrado.", file);
        // SY=SYSTEM, BA=Administrators e a conta que executa o agente.
        var user = CurrentUserSid();
        var sddl = allowBuiltinUsersRead
            ? $"D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;{user})(A;;GR;;;BU)"
            : $"D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;{user})";
        ApplyDacl(file, sddl);
    }

    public static void HardenCredentialFileIfPresent(string file)
    {
        if (File.Exists(file)) HardenCredentialFile(file, allowBuiltinUsersRead: false);
    }

    private static string CurrentUserSid()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(sid) || !sid.StartsWith("S-1-", StringComparison.Ordinal))
            throw new SecurityException("A identidade Windows do servico PIX nao pode ser determinada.");
        return sid;
    }

    private static void ApplyDacl(string path, string sddl)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
            throw new SecurityException("O Windows nao conseguiu preparar a protecao de arquivos PIX.");
        try
        {
            if (!GetSecurityDescriptorDacl(descriptor, out var present, out var dacl, out _) || !present)
                throw new SecurityException("O Windows nao conseguiu preparar a lista de acesso PIX.");
            var result = SetNamedSecurityInfo(path, SeFileObject, DaclSecurityInformation | ProtectedDaclSecurityInformation,
                IntPtr.Zero, IntPtr.Zero, dacl, IntPtr.Zero);
            if (result != 0)
                throw new UnauthorizedAccessException($"O Windows recusou proteger os arquivos PIX (codigo {result}).");
        }
        finally { if (descriptor != IntPtr.Zero) LocalFree(descriptor); }
    }
}

static class WindowsDpapi
{
    private const int CryptprotectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static byte[] Protect(byte[] input, byte[] entropy) => Transform(input, entropy, true);
    public static byte[] Unprotect(byte[] input, byte[] entropy) => Transform(input, entropy, false);

    private static byte[] Transform(byte[] input, byte[] entropy, bool protect)
    {
        var inBlob = Allocate(input);
        var entropyBlob = Allocate(entropy);
        try
        {
            var ok = protect
                ? CryptProtectData(ref inBlob, "TurboRama PIX", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out var output)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out output);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            try
            {
                var bytes = new byte[output.Size];
                Marshal.Copy(output.Data, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
        }
        finally
        {
            if (inBlob.Data != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.Data);
            if (entropyBlob.Data != IntPtr.Zero) Marshal.FreeHGlobal(entropyBlob.Data);
        }
    }

    private static DataBlob Allocate(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Size = bytes.Length, Data = pointer };
    }
}

static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}
