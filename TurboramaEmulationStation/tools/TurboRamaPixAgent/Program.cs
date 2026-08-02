using System.Globalization;
using System.Drawing;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security;
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
PixOwnerSettings? ownerSettings;
try
{
    options = PixOptions.Load();
    if (!string.IsNullOrWhiteSpace(command.BridgeDirectory))
        options = (options with { BridgeDirectory = command.BridgeDirectory }).Normalize();
    ownerSettings = PixOwnerSettings.LoadIfPresent(options.ResolveBridgeDirectory());
    if (ownerSettings is not null && ownerSettings.Enabled)
        options = ownerSettings.Apply(options);
    options.ValidateForStartup(command.SetToken || command.SelfTest || command.MercadoPagoInventory
        || !string.IsNullOrWhiteSpace(command.MercadoPagoSetupFile));
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
{
    Console.Error.WriteLine($"Configuracao PIX invalida: {ex.Message}");
    return 10;
}
Console.WriteLine($"PIX configurado: provider={options.Provider}; bridge={options.BridgeDirectory}");
var paths = new PixPaths(options.ResolveBridgeDirectory());
try { paths.EnsureDirectories(); }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Nao foi possivel preparar as pastas PIX: {ex.Message}");
    return 11;
}
using var fileLog = AgentFileLog.TryAttach(paths.Logs);
var secrets = new PixSecretStore(paths.SecretFile);
var signingKeys = new PixSigningKeyStore(paths.SigningKeyFile);

if (command.SelfTest)
    return PixSelfTest.Run(options, paths, signingKeys);

using var instanceLock = PixAgentInstanceLock.TryAcquire(paths.Root);
if (instanceLock is null)
{
    Console.Error.WriteLine("Ja existe uma instancia do agente PIX usando esta pasta. Encerre-a antes de iniciar outra.");
    return 12;
}

if (command.SetToken)
{
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
        (token.Length is < 40 or > 512 ||
         !token.StartsWith("APP_USR-", StringComparison.Ordinal) ||
         token.Any(char.IsWhiteSpace)))
    {
        Console.Error.WriteLine("Access Token recusado: formato inesperado. Copie o Access Token de teste completo, iniciado por APP_USR-, sem espacos.");
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
            var inventory = await mercadoPago.GetInfrastructureAsync(CancellationToken.None);
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
    provider.Name == "mock" || !string.IsNullOrWhiteSpace(secrets.Load()), provider.Name == "mock", "starting");

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
            if (ownerInfrastructure is not null && !ownerInfrastructure.Ready)
                await ownerInfrastructure.TryEnsureAsync(force: false, cancellation.Token);

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
                provider.Name == "mock" || !string.IsNullOrWhiteSpace(secrets.Load()), providerHealthy,
                providerHealthy ? "online" : ownerInfrastructure is { Ready: false } ? "owner_setup_pending" : "provider_unavailable");
            if (providerHealthy) await engine.RunOnceAsync(cancellation.Token);
            else if (command.Once) return 13;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException)
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

sealed record AgentCommand(bool Once, bool SetToken, bool SelfTest, bool CheckProvider,
    bool MercadoPagoInventory, string MercadoPagoSetupFile, string ApproveId, string BridgeDirectory)
{
    public static AgentCommand Parse(string[] args)
    {
        var once = false;
        var setToken = false;
        var selfTest = false;
        var checkProvider = false;
        var mercadoPagoInventory = false;
        var mercadoPagoSetupFile = "";
        var approveId = "";
        var bridgeDirectory = "";
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--once", StringComparison.OrdinalIgnoreCase)) { once = true; continue; }
            if (args[i].Equals("--set-token", StringComparison.OrdinalIgnoreCase)) { setToken = true; continue; }
            if (args[i].Equals("--self-test", StringComparison.OrdinalIgnoreCase)) { selfTest = true; continue; }
            if (args[i].Equals("--check-provider", StringComparison.OrdinalIgnoreCase)) { checkProvider = true; continue; }
            if (args[i].Equals("--mercadopago-inventory", StringComparison.OrdinalIgnoreCase)) { mercadoPagoInventory = true; continue; }
            if (args[i].Equals("--mercadopago-setup", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new InvalidOperationException("--mercadopago-setup exige o caminho de um arquivo JSON.");
                mercadoPagoSetupFile = args[++i];
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
        var exclusiveModes = (setToken ? 1 : 0) + (selfTest ? 1 : 0) + (checkProvider ? 1 : 0)
            + (mercadoPagoInventory ? 1 : 0) + (!string.IsNullOrWhiteSpace(mercadoPagoSetupFile) ? 1 : 0)
            + (!string.IsNullOrWhiteSpace(approveId) ? 1 : 0);
        if (exclusiveModes > 1)
            throw new InvalidOperationException("use somente um modo administrativo por execucao.");
        return new AgentCommand(once, setToken, selfTest, checkProvider, mercadoPagoInventory, mercadoPagoSetupFile, approveId, bridgeDirectory);
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
                throw new InvalidOperationException("MercadoPago.ExternalPosId deve ter menos de 40 caracteres, somente letras e numeros. APP_USR e Access Token nao sao identificadores de PDV.");
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
        => !string.IsNullOrWhiteSpace(value) && value.Length < 40 && value.All(char.IsAsciiLetterOrDigit)
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
        if (ExpectedAccountId.Length is < 5 or > 24 || !ExpectedAccountId.All(char.IsAsciiDigit))
            throw new InvalidOperationException("User ID esperado da conta de teste e invalido.");
        if (StoreName.Trim().Length is < 2 or >= 60) throw new InvalidOperationException("Nome da loja invalido.");
        if (PosName.Trim().Length is < 2 or >= 45) throw new InvalidOperationException("Nome do PDV invalido.");
        if (!IsAlphaNumeric(StoreExternalId, 60)) throw new InvalidOperationException("external_id da loja deve ser alfanumerico e ter ate 60 caracteres.");
        if (!IsAlphaNumeric(PosExternalId, 39)) throw new InvalidOperationException("external_id do PDV deve ser alfanumerico e ter ate 39 caracteres.");
        if (Category.HasValue && Category.Value is <= 0 or > 999999) throw new InvalidOperationException("Categoria comercial invalida.");
        if (string.IsNullOrWhiteSpace(StreetName) || StreetName.Length > 120) throw new InvalidOperationException("Rua invalida.");
        if (string.IsNullOrWhiteSpace(StreetNumber) || StreetNumber.Length > 20) throw new InvalidOperationException("Numero do endereco invalido.");
        if (string.IsNullOrWhiteSpace(CityName) || CityName.Length > 80) throw new InvalidOperationException("Cidade invalida.");
        if (string.IsNullOrWhiteSpace(StateName) || StateName.Length > 80) throw new InvalidOperationException("Estado invalido.");
        if (!double.IsFinite(Latitude) || Latitude is < -90 or > 90) throw new InvalidOperationException("Latitude invalida.");
        if (!double.IsFinite(Longitude) || Longitude is < -180 or > 180) throw new InvalidOperationException("Longitude invalida.");
        if (string.IsNullOrWhiteSpace(Reference) || Reference.Length > 120) throw new InvalidOperationException("Referencia do endereco invalida.");
    }

    private static bool IsAlphaNumeric(string value, int maximum)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && value.All(char.IsAsciiLetterOrDigit);
}

sealed record PixOwnerSettings
{
    public int SchemaVersion { get; init; } = 1;
    public bool Enabled { get; init; }
    public string AccountId { get; init; } = "";
    public string StoreExternalId { get; init; } = "TURBORAMALOJA01";
    public string StoreName { get; init; } = "TurboRama";
    public string PosExternalId { get; init; } = "TURBORAMAKIOSK01";
    public string PosName { get; init; } = "TurboRama Kiosk";
    public string PostalCode { get; init; } = "";
    public string StreetNumber { get; init; } = "";
    public string Reference { get; init; } = "TurboRama";
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
        if (AccountId.Length is < 5 or > 24 || !AccountId.All(char.IsAsciiDigit))
            throw new InvalidOperationException("User ID do Mercado Pago e invalido.");
        if (!AlphaNumeric(StoreExternalId, 60)) throw new InvalidOperationException("Identificador da loja e invalido.");
        if (!AlphaNumeric(PosExternalId, 39)) throw new InvalidOperationException("Identificador do caixa PIX e invalido.");
        if (StoreName.Trim().Length is < 2 or >= 60) throw new InvalidOperationException("Nome da loja e invalido.");
        if (PosName.Trim().Length is < 2 or >= 45) throw new InvalidOperationException("Nome do caixa PIX e invalido.");
        var cep = new string(PostalCode.Where(char.IsAsciiDigit).ToArray());
        if (cep.Length != 8) throw new InvalidOperationException("CEP do estabelecimento e invalido.");
        if (string.IsNullOrWhiteSpace(StreetNumber) || StreetNumber.Length > 20) throw new InvalidOperationException("Numero do estabelecimento e invalido.");
        if (string.IsNullOrWhiteSpace(Reference) || Reference.Length > 120) throw new InvalidOperationException("Referencia do estabelecimento e invalida.");
        foreach (var minutes in new[] { 15, 30, 45, 60, 120 })
            if (!PackagePricesCents.TryGetValue(minutes, out var price) || price is < 50 or > 100_000_000)
                throw new InvalidOperationException($"Preco do pacote de {minutes} minutos e invalido.");
    }

    public PixOptions Apply(PixOptions options)
    {
        Validate();
        if (!Enabled) return options;
        return (options with
        {
            Provider = "mercadopago",
            ProductionEnabled = true,
            AllowedMinutes = PackagePricesCents.Keys.Order().ToList(),
            PackagePricesCents = new Dictionary<int, long>(PackagePricesCents),
            MercadoPago = options.MercadoPago with { ExternalPosId = PosExternalId.Trim() }
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
            Reference = Reference.Trim()
        };
    }

    private static bool AlphaNumeric(string value, int maximum)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && value.All(char.IsAsciiLetterOrDigit);
}

sealed record BrazilianPostalAddress(string Street, string City, string State, double Latitude, double Longitude)
{
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
    {
        var cep = new string(postalCode.Where(char.IsAsciiDigit).ToArray());
        if (cep.Length != 8) throw new InvalidOperationException("CEP deve conter 8 numeros.");
        var cached = TryLoadCache(cacheFile, cep, streetNumber);
        if (cached is not null) return cached;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TurboRamaPixAgent/1.0");

        string street = "", city = "", stateCode = "";
        double latitude = 0, longitude = 0;
        try
        {
            using var response = await http.GetAsync($"https://brasilapi.com.br/api/cep/v2/{cep}", token);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                var root = document.RootElement;
                street = String(root, "street"); city = String(root, "city"); stateCode = String(root, "state");
                if (root.TryGetProperty("location", out var location) && location.TryGetProperty("coordinates", out var coordinates))
                {
                    latitude = Number(coordinates, "latitude");
                    longitude = Number(coordinates, "longitude");
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }

        if (string.IsNullOrWhiteSpace(street) || string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(stateCode))
        {
            using var response = await http.GetAsync($"https://viacep.com.br/ws/{cep}/json/", token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("CEP nao foi encontrado.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var root = document.RootElement;
            if (root.TryGetProperty("erro", out var invalid) && invalid.ValueKind == JsonValueKind.True)
                throw new InvalidOperationException("CEP nao foi encontrado.");
            street = String(root, "logradouro"); city = String(root, "localidade"); stateCode = String(root, "uf");
        }
        if (string.IsNullOrWhiteSpace(street) || string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(stateCode))
            throw new InvalidOperationException("O CEP nao retornou rua, cidade e estado completos.");

        if (Math.Abs(latitude) < 0.00001 && Math.Abs(longitude) < 0.00001)
        {
            var query = Uri.EscapeDataString($"{street}, {streetNumber}, {city}, {stateCode}, {cep}, Brasil");
            using var response = await http.GetAsync($"https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q={query}", token);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0)
                {
                    var first = document.RootElement[0];
                    double.TryParse(String(first, "lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out latitude);
                    double.TryParse(String(first, "lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out longitude);
                }
            }
        }
        if (Math.Abs(latitude) < 0.00001 && Math.Abs(longitude) < 0.00001)
            throw new InvalidOperationException("O endereco foi localizado pelo CEP, mas as coordenadas nao puderam ser verificadas. Tente novamente com internet ativa.");
        var state = States.TryGetValue(stateCode, out var fullState) ? fullState : stateCode;
        var result = new BrazilianPostalAddress(street.Trim(), city.Trim(), state, latitude, longitude);
        SaveCache(cacheFile, cep, streetNumber, result);
        return result;
    }

    private static BrazilianPostalAddress? TryLoadCache(string file, string postalCode, string streetNumber)
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length is <= 0 or > 16_384) return null;
            var cache = JsonSerializer.Deserialize<PostalAddressCache>(File.ReadAllText(file, Encoding.UTF8), Json.Options);
            if (cache is null || cache.SchemaVersion != 1 || cache.PostalCode != postalCode
                || !cache.StreetNumber.Equals(streetNumber.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
            if (string.IsNullOrWhiteSpace(cache.Street) || string.IsNullOrWhiteSpace(cache.City) || string.IsNullOrWhiteSpace(cache.State)
                || !double.IsFinite(cache.Latitude) || !double.IsFinite(cache.Longitude)
                || Math.Abs(cache.Latitude) < 0.00001 || Math.Abs(cache.Longitude) < 0.00001) return null;
            return new BrazilianPostalAddress(cache.Street, cache.City, cache.State, cache.Latitude, cache.Longitude);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static void SaveCache(string file, string postalCode, string streetNumber, BrazilianPostalAddress address)
    {
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var cache = new PostalAddressCache(1, postalCode, streetNumber.Trim(), address.Street, address.City,
                address.State, address.Latitude, address.Longitude, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
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

    private static string String(JsonElement objectValue, string property)
        => objectValue.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static double Number(JsonElement objectValue, string property)
    {
        if (!objectValue.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        return 0;
    }
}

sealed record PostalAddressCache(int SchemaVersion, string PostalCode, string StreetNumber, string Street,
    string City, string State, double Latitude, double Longitude, long UpdatedAtUnixSeconds);

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
        if (Ready) return true;
        if (!force && DateTimeOffset.UtcNow < _nextAttempt) return false;
        _nextAttempt = DateTimeOffset.UtcNow.AddSeconds(15);
        try
        {
            OwnerSetupStatus.Publish(_paths, "configuring", "Conferindo loja e caixa PIX. Se estiver sem internet, o sistema tentara novamente automaticamente.");

            // Se a loja e o PDV ja existem, nao dependemos novamente de CEP ou geocodificacao.
            var inventory = await _provider.GetInfrastructureAsync(token);
            if (!inventory.AccountId.Equals(_settings.AccountId.Trim(), StringComparison.Ordinal))
                throw new SecurityException("O Access Token pertence a outra conta do Mercado Pago.");
            var store = inventory.Stores.FirstOrDefault(item => item.ExternalId.Equals(_settings.StoreExternalId.Trim(), StringComparison.OrdinalIgnoreCase));
            var pointOfSale = inventory.PointsOfSale.FirstOrDefault(item => item.ExternalId.Equals(_settings.PosExternalId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (store is not null && pointOfSale is not null)
            {
                if (!pointOfSale.StoreId.Equals(store.Id, StringComparison.Ordinal))
                    throw new SecurityException("O caixa PIX informado pertence a outra loja.");
                Ready = true;
                _lastError = "";
                OwnerSetupStatus.Publish(_paths, "ready", $"Loja {store.ExternalId} e caixa {pointOfSale.ExternalId} confirmados.");
                return true;
            }

            var setup = await _settings.BuildSetupRequestAsync(_paths, token);
            var result = await _provider.EnsureInfrastructureAsync(setup, token);
            Ready = true;
            _lastError = "";
            OwnerSetupStatus.Publish(_paths, "ready", $"Loja {result.Store.ExternalId} e caixa {result.PointOfSale.ExternalId} confirmados.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or HttpRequestException
            or TaskCanceledException or MercadoPagoApiException or InvalidOperationException or SecurityException)
        {
            var connectionFailure = ex is HttpRequestException or TaskCanceledException;
            var message = connectionFailure
                ? "Cadastro salvo. Sem conexao com os servicos PIX; nova tentativa automatica em 15 segundos."
                : ex.Message;
            OwnerSetupStatus.Publish(_paths, connectionFailure ? "waiting_network" : "error", message);
            if (!message.Equals(_lastError, StringComparison.Ordinal))
                Console.Error.WriteLine($"Falha no cadastro do estabelecimento PIX: {message}");
            _lastError = message;
            return false;
        }
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
    public string PublicOptionsFile { get; }
    public string AgentStatusFile { get; }
    public string RequestFile(string id) => Path.Combine(Requests, $"{id}.request.json");
    public string SessionFile(string id) => Path.Combine(Sessions, $"{id}.session.json");
    public string ApprovedFile(string id) => Path.Combine(Approved, $"{id}.credit.json");
    public string ProcessedFile(string id) => Path.Combine(Processed, $"{id}.credit.json");
    public string QrFile(string id) => Path.Combine(Qr, $"{id}.png");
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
            try { File.SetAttributes(_path, File.GetAttributes(_path) | FileAttributes.Hidden); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
    }

    public string? Load()
    {
        var environmentToken = Environment.GetEnvironmentVariable("TURBORAMA_PIX_PROVIDER_SECRET");
        if (string.IsNullOrWhiteSpace(environmentToken))
            environmentToken = Environment.GetEnvironmentVariable("TURBORAMA_PIX_MERCADOPAGO_ACCESS_TOKEN");
        if (!string.IsNullOrWhiteSpace(environmentToken)) return environmentToken.Trim();
        if (!File.Exists(_path)) return null;
        try
        {
            var entropy = Encoding.UTF8.GetBytes("TurboRamaPixAgent-v1");
            var encrypted = Convert.FromBase64String(File.ReadAllText(_path, Encoding.UTF8).Trim());
            return Encoding.UTF8.GetString(WindowsDpapi.Unprotect(encrypted, entropy));
        }
        catch (CryptographicException) { return null; }
        catch (FormatException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
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
        if (File.Exists(_path)) return Load();
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or MercadoPagoApiException or AdapterApiException or InvalidOperationException)
            {
                Console.Error.WriteLine($"Falha na cobranca PIX: {ex.Message}");
                ScheduleRequestRetry(requestId);
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
                    DeleteIfExists(_paths.QrFile(refreshed.Id));
                    continue;
                }
                var scheduled = refreshed.Status == "pending"
                    ? refreshed with { FailureCount = 0, NextPollAt = DateTimeOffset.UtcNow.AddSeconds(_options.PollSeconds) }
                    : refreshed with { FailureCount = 0, NextPollAt = DateTimeOffset.MaxValue };
                _paths.WriteAtomically(sessionFile, scheduled);
                if (scheduled.Status is "cancelled" or "security_error") DeleteIfExists(_paths.QrFile(scheduled.Id));
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
    }

    private void SaveQr(PixSession session)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(session.QrData, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(8, Color.FromArgb(17, 17, 17), Color.White, true);
        _paths.WriteBytesAtomically(_paths.QrFile(session.Id), png);
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
            DeleteIfExists(_paths.QrFile(session.Id));
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
    }

    private int RetryDelay(int failures) => Math.Min(_options.MaxRetrySeconds, (int)Math.Pow(2, Math.Min(failures, 8)) * 2);
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
    private readonly PixOptions _options;
    private readonly PixSecretStore _secrets;
    private readonly HttpClient _http;
    public string Name => "mercadopago";
    public MercadoPagoPixProvider(PixOptions options, PixSecretStore secrets, HttpMessageHandler? handler = null)
    {
        _options = options;
        _secrets = secrets;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.BaseAddress = new Uri("https://api.mercadopago.com/");
        _http.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TurboRamaPixAgent/1.0");
    }

    public async Task CheckHealthAsync(CancellationToken token)
    {
        using var posMessage = new HttpRequestMessage(HttpMethod.Get,
            $"pos?external_id={Uri.EscapeDataString(_options.MercadoPago.ExternalPosId)}");
        posMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", RequireToken());
        using var posResponse = await _http.SendAsync(posMessage, token);
        var posText = await posResponse.Content.ReadAsStringAsync(token);
        EnsureApiSuccess(posResponse, posText);
        using var posJson = ParseApiJson(posText);
        if (!posJson.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array
            || !results.EnumerateArray().Any(item => GetString(item, "external_id")
                .Equals(_options.MercadoPago.ExternalPosId, StringComparison.Ordinal)))
            throw new MercadoPagoApiException(404, $"PDV {_options.MercadoPago.ExternalPosId} nao foi encontrado na conta");
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

    public async Task<MercadoPagoSetupResult> EnsureInfrastructureAsync(MercadoPagoSetupRequest setup, CancellationToken token)
    {
        setup.Validate();
        // Credenciais de sandbox podem ser bloqueadas por politicas no recurso
        // Mercado Livre /users/me. A consulta oficial de lojas com o User ID
        // informado ja valida a posse da conta e retorna 403 quando nao pertence
        // ao mesmo Access Token, sem depender daquele recurso adicional.
        var inventory = await GetInfrastructureForAccountAsync(setup.ExpectedAccountId, token);

        var store = inventory.Stores.SingleOrDefault(x => x.ExternalId.Equals(setup.StoreExternalId, StringComparison.Ordinal));
        var storeCreated = false;
        if (store is null)
        {
            using var storeJson = await SendAuthorizedJsonAsync(HttpMethod.Post,
                $"users/{Uri.EscapeDataString(inventory.AccountId)}/stores", new
                {
                    name = setup.StoreName.Trim(),
                    business_hours = StandardBusinessHours(),
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
        }

        var point = inventory.PointsOfSale.SingleOrDefault(x => x.ExternalId.Equals(setup.PosExternalId, StringComparison.Ordinal));
        var pointCreated = false;
        if (point is not null && !string.IsNullOrWhiteSpace(point.StoreId) && !point.StoreId.Equals(store.Id, StringComparison.Ordinal))
            throw new SecurityException("O external_id do PDV ja pertence a outra loja da conta.");
        if (point is null)
        {
            var posBody = new Dictionary<string, object?>
            {
                ["name"] = setup.PosName.Trim(),
                ["fixed_amount"] = true,
                ["store_id"] = ParseNumericId(store.Id, "ID da loja"),
                ["external_store_id"] = setup.StoreExternalId,
                ["external_id"] = setup.PosExternalId
            };
            // O MCC e opcional. Para estabelecimentos fora das categorias MCC
            // aceitas pelo site do usuario, omitir o campo aplica a categoria
            // generica e evita POS_UNKNOWN_MCC.
            if (setup.Category.HasValue) posBody["category"] = setup.Category.Value;
            using var posJson = await SendAuthorizedJsonAsync(HttpMethod.Post, "pos", posBody, token);
            point = ReadPos(posJson.RootElement);
            if (string.IsNullOrWhiteSpace(point.Id) || !point.ExternalId.Equals(setup.PosExternalId, StringComparison.Ordinal))
                throw new MercadoPagoApiException(502, "PDV criado sem os identificadores esperados");
            pointCreated = true;
        }
        return new MercadoPagoSetupResult(inventory.AccountId, store, point, storeCreated, pointCreated);
    }

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
            foreach (var page in root.EnumerateArray())
            {
                if (page.ValueKind != JsonValueKind.Object ||
                    !page.TryGetProperty("results", out var pageResults) ||
                    pageResults.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in pageResults.EnumerateArray())
                    yield return item;
            }
        }
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

    private static object StandardBusinessHours() => new
    {
        monday = new[] { new { open = "08:00", close = "23:59" } },
        tuesday = new[] { new { open = "08:00", close = "23:59" } },
        wednesday = new[] { new { open = "08:00", close = "23:59" } },
        thursday = new[] { new { open = "08:00", close = "23:59" } },
        friday = new[] { new { open = "08:00", close = "23:59" } },
        saturday = new[] { new { open = "08:00", close = "23:59" } },
        sunday = new[] { new { open = "08:00", close = "23:59" } }
    };

    public async Task<PixSession> CreateAsync(PixPurchaseRequest request, CancellationToken token)
    {
        var accessToken = RequireToken();
        var amount = (request.AmountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/orders");
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Add("X-Idempotency-Key", request.Id);
        message.Content = JsonContent.Create(new
        {
            type = "qr", processing_mode = "automatic", total_amount = amount, external_reference = request.Id,
            expiration_time = $"PT{_options.PaymentExpirationMinutes}M",
            description = $"{_options.MercadoPago.DescriptionPrefix} - {request.Minutes} min",
            config = new { qr = new { mode = "dynamic", external_pos_id = _options.MercadoPago.ExternalPosId } },
            transactions = new { payments = new[] { new { amount } } },
            items = new[] { new { title = $"{request.Minutes} minutos", unit_price = amount, quantity = 1, unit_measure = "unit", external_code = request.Minutes.ToString(CultureInfo.InvariantCulture) } }
        }, options: Json.Options);
        using var response = await _http.SendAsync(message, token);
        var text = await response.Content.ReadAsStringAsync(token);
        EnsureApiSuccess(response, text);
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

    private string RequireToken() => _secrets.Load() ?? throw new InvalidOperationException("Access Token do Mercado Pago nao configurado. Execute com --set-token.");

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
            detail = GetString(json.RootElement, "code");
            if (string.IsNullOrWhiteSpace(detail)) detail = GetString(json.RootElement, "message");
        }
        catch (JsonException) { }
        throw new MercadoPagoApiException((int)response.StatusCode, string.IsNullOrWhiteSpace(detail) ? "erro sem detalhe" : detail);
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

static class PixSelfTest
{
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
            TestMercadoPagoResponses();
            TestMercadoPagoHealth(options, paths);
            TestAdapterResponses(options, paths);
            Console.WriteLine("SELF-TEST PIX: OK (preco, assinatura, adulteracao, disco, loja/PDV idempotentes, Mercado Pago e adaptador bancario).");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or FormatException or JsonException)
        {
            Console.Error.WriteLine($"SELF-TEST PIX: FALHOU - {ex.Message}");
            return 20;
        }
    }

    private static void TestPostalAddressCache(PixPaths paths)
    {
        var cacheFile = Path.Combine(paths.Root, "owner-address-cache.json");
        paths.WriteAtomically(cacheFile, new PostalAddressCache(1, "57084648", "52", "Rua de Teste", "Maceio",
            "Alagoas", -9.6001, -35.7001, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        try
        {
            var address = BrazilianPostalAddress.ResolveAsync("57084-648", "52", cacheFile, CancellationToken.None).GetAwaiter().GetResult();
            if (address.Street != "Rua de Teste" || address.City != "Maceio" || Math.Abs(address.Latitude + 9.6001) > 0.000001)
                throw new InvalidOperationException("cache de endereco");
        }
        finally { try { if (File.Exists(cacheFile)) File.Delete(cacheFile); } catch (IOException) { } }
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

            var setupHandler = new FakeMercadoPagoSetupHandler();
            var setupProvider = new MercadoPagoPixProvider(options, secretStore, setupHandler);
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

    private sealed class FakeMercadoPagoSetupHandler : HttpMessageHandler
    {
        private bool _storeExists;
        private bool _posExists;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization?.Scheme != "Bearer"
                || request.Headers.Authorization.Parameter != "APP_USR-self-test-token")
                return JsonResponse(System.Net.HttpStatusCode.Unauthorized, new { message = "credencial invalida" });
            var uri = request.RequestUri;
            var path = uri?.AbsolutePath ?? "";
            if (request.Method == HttpMethod.Get && path == "/users/123456/stores/search")
            {
                object[] stores = _storeExists
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
                using var body = JsonDocument.Parse(await (request.Content?.ReadAsStringAsync(cancellationToken) ?? Task.FromResult("{}")));
                if (body.RootElement.GetProperty("external_id").GetString() != "LZLOJA01"
                    || body.RootElement.GetProperty("location").GetProperty("city_name").GetString() != "Sao Paulo")
                    return JsonResponse(System.Net.HttpStatusCode.BadRequest, new { message = "loja divergente" });
                _storeExists = true;
                return JsonResponse(System.Net.HttpStatusCode.OK, new { id = 987, external_id = "LZLOJA01", name = "TurboRama Teste" });
            }
            if (request.Method == HttpMethod.Post && path == "/pos")
            {
                using var body = JsonDocument.Parse(await (request.Content?.ReadAsStringAsync(cancellationToken) ?? Task.FromResult("{}")));
                if (body.RootElement.GetProperty("external_id").GetString() != "LZPIXCOMP"
                    || body.RootElement.GetProperty("fixed_amount").ValueKind != JsonValueKind.True
                    || body.RootElement.GetProperty("store_id").GetInt64() != 987
                    || body.RootElement.TryGetProperty("category", out _))
                    return JsonResponse(System.Net.HttpStatusCode.BadRequest, new { message = "PDV divergente" });
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
