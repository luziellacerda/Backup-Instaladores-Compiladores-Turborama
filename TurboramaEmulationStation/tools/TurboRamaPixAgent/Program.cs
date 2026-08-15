using System.Globalization;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using QRCoder;

AgentCommand command;
try { command = AgentCommand.Parse(args); }
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Comando PIX invalido: {ex.Message}");
    return 9;
}

PixDaemonIdentity? daemonIdentity = null;
try
{
    if (command.RunMode == AgentRunMode.Daemon)
        daemonIdentity = PixDaemonIdentity.CreateForCurrentProcess();
}
catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException
    or SecurityException or CryptographicException)
{
    Console.Error.WriteLine($"Identidade do daemon PIX recusada: {ex.Message}");
    return 12;
}
using var daemonIdentityLifetime = daemonIdentity;

// Preflight estritamente de leitura usado pela interface antes de ela copiar
// ou transmitir a credencial. Ele nao depende de appsettings.json, nao abre a
// bridge, nao muda ACL e nao toca DPAPI.
if (command.CheckKioskIdentity)
{
    if (KioskProcessIdentity.TryValidateCurrent(out var identityReason))
    {
        Console.WriteLine("Identidade Windows do quiosque confirmada.");
        return 0;
    }
    Console.Error.WriteLine($"Identidade PIX recusada: {identityReason}. Abra o configurador na mesma conta Windows configurada no TurboRama/Winlogon. Neste gabinete a conta operacional e Admin.");
    return 19;
}

PixOptions options;
PixOwnerSettings? ownerSettings = null;
PixOwnerControlSnapshot? ownerControlSnapshot = null;
OnlineOwnerConfiguration? requestedOnlineConfiguration = null;
PixPaths? startupPaths = null;
CommercialLicenseBuildIdentity? startupCommercialIdentity = null;
IPixMachineBinding? startupMachineBinding = null;
CommercialLicenseVerifier? startupCommercialLicense = null;
var commercialLicensePolicy = new CommercialLicensePolicy("TurboRama-PIX", 25, "pix-production");
try
{
    options = PixOptions.Load();
    if (!string.IsNullOrWhiteSpace(command.BridgeDirectory))
        options = (options with { BridgeDirectory = command.BridgeDirectory }).Normalize();

    // O auto-teste nunca deve abrir, criar ou alterar a ponte real, mesmo se
    // ela ja tiver um cadastro de proprietario com erro.
    if (command.SelfTest)
        return PixSelfTest.RunIsolated(options);
    if (command.VerifyCommercialBuild)
    {
        var buildIdentity = CommercialLicenseBuildIdentity.LoadCurrent();
        if (!buildIdentity.Required || buildIdentity.TrustedIssuer is null)
            throw new SecurityException("a DLL nao possui identidade de licenca comercial obrigatoria");
        Console.WriteLine("Identidade de licenca comercial incorporada: OK");
        return 0;
    }

    // A ponte, as ACLs e a DPAPI pertencem exclusivamente a conta local que
    // o Launcher declarou para o quiosque e que o Winlogon vai iniciar. Nao
    // deixamos uma elevacao UAC, um usuario de manutencao ou uma conta trocada
    // reatribuir esses arquivos antes desta comprovacao falhar fechada.
    KioskProcessIdentity.RequireTrustedKioskProcess();

    // Antes de ler qualquer configuracao do proprietario, removemos as ACLs
    // herdadas das instalacoes antigas. Assim um usuario local sem privilegio
    // nao consegue trocar o PDV, os precos ou a chave de creditos entre a
    // inicializacao do agente e o processamento do pagamento.
    startupPaths = new PixPaths(options.ResolveBridgeDirectory());
    startupPaths.EnsureDirectories();
    startupCommercialIdentity = CommercialLicenseBuildIdentity.LoadCurrent();
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
        startupPaths.LicenseFile,
        Path.Combine(startupPaths.Root, "owner-settings.json")
    })
    {
        WindowsFileSecurity.HardenCredentialFileIfPresent(protectedFile);
    }
    var arcadeConfiguration = Path.Combine(Directory.GetParent(startupPaths.Root)?.FullName ?? startupPaths.Root, "arcade_credit.cfg");
    WindowsFileSecurity.HardenCredentialFileIfPresent(arcadeConfiguration);

    // O configurador externo substitui os campos bancarios mesmo quando eles
    // estao antigos, mas nunca pode apagar licenca, perfil da maquina, trava
    // remota ou versao recebida do servidor. Esses campos sao lidos e validados
    // separadamente para nao depender da validade do cadastro do PDV a reparar.
    if (string.IsNullOrWhiteSpace(command.ConfigureOwnerFile))
        ownerSettings = PixOwnerSettings.LoadIfPresent(options.ResolveBridgeDirectory());
    else
    {
        ownerControlSnapshot = PixOwnerControlSnapshot.LoadIfPresent(options.ResolveBridgeDirectory());
        if (ownerControlSnapshot is not null)
            options = ownerControlSnapshot.Apply(options);
    }
    if (!string.IsNullOrWhiteSpace(command.OnlineConfigureFile))
    {
        requestedOnlineConfiguration = OnlineOwnerConfiguration.Load(command.OnlineConfigureFile);
        ownerSettings = requestedOnlineConfiguration.ToOwnerSettings(ownerSettings, options);
        options = ownerSettings.Apply(options);
    }
    else if (ownerSettings is not null)
        options = ownerSettings.Apply(options);
    // A chave e a licenca precisam usar o perfil efetivamente escolhido pelo
    // cadastro protegido. Criar o vinculo antes de aplicar owner-settings
    // faria uma maquina SOFTWARE_BOUND_ONLINE tentar abrir o TPM por engano.
    startupMachineBinding = MachineBindingFactory.Create(options);
    if (startupCommercialIdentity.Required)
        startupCommercialLicense = startupCommercialIdentity.CreateRequiredVerifier(
            startupMachineBinding, commercialLicensePolicy);
    options.ValidateForStartup(command.SetToken || command.AcceptCredentialOnce || command.MercadoPagoInventory
        || !string.IsNullOrWhiteSpace(command.MercadoPagoSetupFile)
        || !string.IsNullOrWhiteSpace(command.ConfigureOwnerFile)
        || !string.IsNullOrWhiteSpace(command.OnlineConfigureFile)
        || command.HasLicenseAdministrativeMode);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
    or InvalidOperationException or SecurityException or CryptographicException
    or FormatException or ArgumentException)
{
    var startupError = $"Configuracao PIX invalida: {ex.Message}";
    PixStartupErrorContract.Publish(startupPaths, daemonIdentity?.Descriptor, 10, startupError);
    Console.Error.WriteLine(startupError);
    return 10;
}

if (string.IsNullOrWhiteSpace(command.ConfigureOwnerFile))
    Console.WriteLine($"PIX configurado: provider={options.Provider}; bridge={options.BridgeDirectory}");
else
    Console.WriteLine($"Configuracao comercial PIX iniciada: bridge={options.BridgeDirectory}");
var paths = startupPaths!;
var commercialIdentity = startupCommercialIdentity!;
var machineBinding = startupMachineBinding!;
var commercialLicense = startupCommercialLicense;
using var fileLog = AgentFileLog.TryAttach(paths.Logs);
var secrets = new PixSecretStore(paths.SecretFile,
    options.RequireTpmMachineBinding || commercialIdentity.Required, machineBinding);
var signingKeys = new PixSigningKeyStore(paths.SigningKeyFile);

using var instanceLock = PixAgentInstanceLock.TryAcquire(paths.Root);
if (instanceLock is null)
{
    Console.Error.WriteLine("Ja existe uma instancia do agente PIX usando esta pasta. Encerre-a antes de iniciar outra.");
    return 12;
}

if (!string.IsNullOrWhiteSpace(command.OnlineConfigureFile))
{
    try
    {
        var configuration = requestedOnlineConfiguration
            ?? throw new SecurityException("A configuracao on-line nao foi validada na inicializacao.");
        var settings = ownerSettings
            ?? configuration.ToOwnerSettings(null, options);
        var destination = Path.Combine(paths.Root, "owner-settings.json");
        paths.WriteAtomically(destination, settings);
        WindowsFileSecurity.HardenCredentialFile(destination, allowBuiltinUsersRead: false);
        Console.WriteLine("Licenciamento TurboRama Online configurado. Provedor, PDV, token e precos locais foram preservados.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
        or SecurityException or InvalidOperationException or FormatException)
    {
        Console.Error.WriteLine($"Configuracao on-line recusada: {ex.Message}");
        return 24;
    }
}

var licenseCommandExit = CommercialLicenseRuntime.HandleAdministrativeCommand(
    command, commercialIdentity, commercialLicensePolicy, machineBinding, commercialLicense, paths);
if (licenseCommandExit.HasValue) return licenseCommandExit.Value;

Func<CommercialLicenseValidationResult>? validateCommercialLicense = commercialLicense is null
    ? null
    : () => commercialLicense.ValidateFile(paths.LicenseFile);

if (command.OnlineActivate)
{
    try
    {
        if (!options.OnlineLicensingEnabled)
            throw new InvalidOperationException("A ativacao on-line exige o licenciamento TurboRama configurado.");
        var localLicense = validateCommercialLicense?.Invoke();
        if (commercialIdentity.Required && localLicense is not { IsValid: true })
            throw new SecurityException(localLicense?.Message ?? "A licenca comercial local nao esta instalada.");
        Console.Write("Digite o codigo de ativacao on-line e pressione Enter: ");
        var activationCode = SecretConsole.ReadHidden();
        Console.WriteLine();
        using var activationCancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Clamp(options.HttpTimeoutSeconds * 3, 15, 180)));
        var activationClient = new OnlineLicenseClient(options);
        await activationClient.ActivateAsync(activationCode, activationCancellation.Token);
        Console.WriteLine("Maquina ativada no servidor TurboRama sem armazenar o codigo de ativacao.");
        return 0;
    }
    catch (OnlineActivationIndeterminateException ex)
    {
        Console.Error.WriteLine($"Ativacao on-line inconclusiva: {ex.Message}");
        return 25;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
        or SecurityException or InvalidOperationException or HttpRequestException or TaskCanceledException
        or OnlineApiException)
    {
        Console.Error.WriteLine($"Ativacao on-line recusada: {ex.Message}");
        return 24;
    }
}

// O frontend assina cada pedido com a mesma chave usada nos eventos de
// credito. Publique-a antes de anunciar o servico como pronto.
try { signingKeys.GetOrCreate(); }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
{
    var startupError = $"Nao foi possivel preparar a chave do contrato PIX: {ex.Message}";
    PixStartupErrorContract.Publish(paths, daemonIdentity?.Descriptor, 10, startupError);
    Console.Error.WriteLine(startupError);
    return 10;
}

// Configuracao comercial completa usada pelo aplicativo Windows LZ Games.
// O segredo chega somente pelo pipe de entrada, nunca pelo JSON, linha de
// comando ou log. Leituras de conta/inventario acontecem antes de substituir
// o cadastro; o estado pendente so e salvo antes dos POSTs estritamente
// necessarios e so passa para pronto apos conta, loja e PDV serem confirmados.
if (!string.IsNullOrWhiteSpace(command.ConfigureOwnerFile))
{
    try
    {
        var request = PixOwnerProvisioningRequest.Load(command.ConfigureOwnerFile);
        var credential = SecretConsole.ReadHidden().Trim();
        if (string.IsNullOrWhiteSpace(credential))
            throw new SecurityException("a credencial do provedor nao foi informada");
        var result = await PixOwnerProvisioner.ConfigureAsync(request, credential, options, paths, secrets,
            CancellationToken.None, ownerControlSnapshot: ownerControlSnapshot);
        Console.WriteLine(JsonSerializer.Serialize(result, Json.Options));
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or HttpRequestException
        or TaskCanceledException or MercadoPagoApiException or AdapterApiException or InvalidOperationException
        or SecurityException or CryptographicException)
    {
        Console.Error.WriteLine($"Falha na configuracao completa do PIX: {PixOwnerProvisioner.SafeSetupMessage(ex.Message)}");
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
PixCredentialInbox? credentialInbox = null;
var credentialInboxReady = false;
var nextCredentialInboxAttempt = DateTimeOffset.MinValue;
var lastCredentialInboxError = "";
try
{
    if (options.Provider is "mercadopago" or "adapter")
    {
        credentialInbox = new PixCredentialInbox(paths, secrets);
        credentialInbox.EnsureReady();
        credentialInboxReady = true;
        credentialInbox.TryAcceptPendingUpdate();
    }
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidOperationException or SecurityException)
{
    Console.Error.WriteLine($"Falha ao preparar a atualizacao segura de credencial PIX: {ex.Message}");
    lastCredentialInboxError = ex.Message;
    nextCredentialInboxAttempt = DateTimeOffset.UtcNow.AddSeconds(15);
}

var provider = PixProviderFactory.Create(options, secrets);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
using var heartbeat = daemonIdentity is null ? null : new PixAgentHeartbeat(options, paths, provider.Name,
    provider.Name == "mock" || secrets.TryLoad().IsAvailable,
    provider.Name == "mock", "starting", daemonIdentity.Descriptor,
    validateCommercialLicense);
await using var stopMonitor = daemonIdentity is null ? null : PixAgentStopMonitor.Start(paths,
    daemonIdentity.Descriptor, cancellation, () =>
{
    try { heartbeat!.Update(false, false, "stopping"); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
    {
        Console.Error.WriteLine($"Nao foi possivel publicar o estado final do agente PIX: {ex.Message}");
    }
    Console.WriteLine("Solicitacao de parada graciosa recebida; agente PIX encerrado com seguranca.");
});

// O cadastro pode ser salvo sem internet. A criacao/confirmacao da loja e do
// PDV e retomada automaticamente a cada 15 segundos ate a conexao voltar.
OwnerInfrastructureCoordinator? ownerInfrastructure = null;
if (ownerSettings is not null && ownerSettings.Enabled && provider is MercadoPagoPixProvider ownerMercadoPago)
{
    ownerInfrastructure = new OwnerInfrastructureCoordinator(ownerSettings, ownerMercadoPago, paths);
    try { await ownerInfrastructure.TryEnsureAsync(force: true, cancellation.Token); }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 0; }
}
var ownerSetupPendingWithoutCoordinator = ownerInfrastructure is null
    && ownerSettings is { Enabled: true }
    && !ownerSettings.SetupState.Equals("ready", StringComparison.OrdinalIgnoreCase);
var baseConfigurationUsesLegacyTestPdv = ownerSettings is null
    && options.Provider.Equals("mercadopago", StringComparison.OrdinalIgnoreCase)
    && MercadoPagoOptions.IsLegacyTestExternalPosId(options.MercadoPago.ExternalPosId);

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
                ? await mercadoPago.GetInfrastructureForConfiguredAccountAsync(ownerSettings.AccountId, cancellation.Token)
                : await mercadoPago.GetInfrastructureAsync(cancellation.Token);
            Console.WriteLine(JsonSerializer.Serialize(inventory, Json.Options));
        }
        else
        {
            var setup = MercadoPagoSetupRequest.Load(command.MercadoPagoSetupFile);
            var result = await mercadoPago.EnsureInfrastructureAsync(setup, cancellation.Token);
            Console.WriteLine(JsonSerializer.Serialize(result, Json.Options));
        }
        return 0;
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 0; }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or HttpRequestException
        or MercadoPagoApiException or InvalidOperationException or SecurityException)
    {
        Console.Error.WriteLine($"Falha na configuracao do Mercado Pago: {ex.Message}");
        return 18;
    }
}

var engine = new PixEngine(options, paths, provider, signingKeys, validateCommercialLicense);
var onlineLicense = options.OnlineLicensingEnabled ? new OnlineLicenseClient(options) : null;
var licenseAvailability = new OnlineLicenseAvailabilityPolicy(onlineLicense is not null);
var nextLicenseCheck = DateTimeOffset.MinValue;

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
        await provider.CheckHealthAsync(cancellation.Token);
        Console.WriteLine("Credencial e conexao com o provedor confirmadas.");
        return 0;
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 0; }
    catch (Exception ex) when (ex is HttpRequestException or MercadoPagoApiException or AdapterApiException or OnlineApiException or InvalidOperationException or SecurityException)
    {
        Console.Error.WriteLine($"Falha na verificacao do provedor: {ex.Message}");
        return 16;
    }
}

Console.WriteLine($"TurboRama PIX Agent | provedor: {provider.Name} | pasta: {paths.Root}");
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
            if (credentialInbox is not null && !credentialInboxReady && now >= nextCredentialInboxAttempt)
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
            var credentialChanged = credentialInboxReady && credentialInbox is not null
                && credentialInbox.TryAcceptPendingUpdate();
            if (credentialChanged)
            {
                providerHealthy = false;
                nextHealthCheck = DateTimeOffset.MinValue;
            }
            if (ownerInfrastructure is not null && (!ownerInfrastructure.Ready || credentialChanged))
                await ownerInfrastructure.TryEnsureAsync(force: credentialChanged, cancellation.Token);

            // O servidor TurboRama reconhece a instalacao, mas nao e provedor
            // de pagamento nem autoridade de precos. Falha de rede, timeout ou
            // erro 5xx preserva a ultima autorizacao local; apenas uma recusa
            // criptograficamente confirmada (403/409) bloqueia novas cobrancas.
            if (onlineLicense is not null && now >= nextLicenseCheck)
            {
                try
                {
                    await onlineLicense.CheckHealthAsync(cancellation.Token);
                    if (!licenseAvailability.AllowsNewPix
                        || !string.IsNullOrWhiteSpace(licenseAvailability.LastError))
                        Console.WriteLine("Licenca TurboRama Online confirmada novamente.");
                    licenseAvailability.Confirmed();
                    nextLicenseCheck = now.AddSeconds(60);
                }
                catch (OnlineApiException ex) when (
                    OnlineLicenseAvailabilityPolicy.IsTransientStatus(ex.StatusCode))
                {
                    var previousError = licenseAvailability.LastError;
                    var preserved = licenseAvailability.TransientFailure(ex.Message);
                    nextLicenseCheck = now.AddSeconds(30);
                    if (!ex.Message.Equals(previousError, StringComparison.Ordinal))
                        Console.Error.WriteLine(preserved
                            ? "Servidor de licenca temporariamente indisponivel; a autorizacao confirmada nesta execucao foi preservada."
                            : "Servidor de licenca indisponivel antes da confirmacao desta execucao; somente novas cobrancas PIX permanecem bloqueadas.");
                }
                catch (OnlineApiException ex)
                {
                    var previousError = licenseAvailability.LastError;
                    licenseAvailability.ExplicitlyDenied(ex.Message);
                    nextLicenseCheck = now.AddSeconds(15);
                    if (!ex.Message.Equals(previousError, StringComparison.Ordinal))
                        Console.Error.WriteLine($"Licenca TurboRama recusada: {ex.Message}");
                }
                catch (SecurityException ex)
                {
                    var previousError = licenseAvailability.LastError;
                    licenseAvailability.ExplicitlyDenied(ex.Message);
                    nextLicenseCheck = now.AddSeconds(15);
                    if (!ex.Message.Equals(previousError, StringComparison.Ordinal))
                        Console.Error.WriteLine($"Licenca TurboRama recusada: {ex.Message}");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    var previousError = licenseAvailability.LastError;
                    var preserved = licenseAvailability.TransientFailure(ex.Message);
                    nextLicenseCheck = now.AddSeconds(30);
                    if (!ex.Message.Equals(previousError, StringComparison.Ordinal))
                        Console.Error.WriteLine(preserved
                            ? "Servidor de licenca temporariamente indisponivel; a autorizacao confirmada nesta execucao foi preservada."
                            : "Servidor de licenca indisponivel antes da confirmacao desta execucao; somente novas cobrancas PIX permanecem bloqueadas.");
                }
            }

            if (baseConfigurationUsesLegacyTestPdv)
            {
                providerHealthy = false;
                nextHealthCheck = DateTimeOffset.UtcNow.AddSeconds(60);
                const string message =
                    "O PDV LZPIXCOMP pertence a uma configuracao antiga de teste. Abra CONFIGURAR-USER-TOKEN-PIX.exe e grave o PDV real desta conta Mercado Pago. O quiosque e os creditos locais continuam funcionando; somente novas cobrancas PIX ficam bloqueadas.";
                OwnerSetupStatus.Publish(paths, "pending", message);
                if (!message.Equals(lastHealthError, StringComparison.Ordinal))
                    Console.Error.WriteLine("Mercado Pago precisa configurar PDV real: LZPIXCOMP e um identificador antigo de teste.");
                lastHealthError = message;
            }
            else if (ownerSetupPendingWithoutCoordinator || ownerInfrastructure is { Ready: false })
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
                catch (Exception ex) when (ex is HttpRequestException or MercadoPagoApiException or AdapterApiException or OnlineApiException or InvalidOperationException or SecurityException)
                {
                    providerHealthy = false;
                    if (ownerInfrastructure is not null)
                        ownerInfrastructure.InvalidateAfterHealthFailure(ex);
                    else if (OwnerInfrastructureCoordinator.RequiresInfrastructureReconciliation(ex))
                        OwnerSetupStatus.Publish(paths, "pending",
                            "O caixa PIX configurado nao existe nesta conta e nao ha cadastro completo para recria-lo. Abra CONFIGURAR-USER-TOKEN-PIX.exe na conta Windows configurada no TurboRama/Winlogon. Neste gabinete a conta operacional e Admin.");
                    if (!ex.Message.Equals(lastHealthError, StringComparison.Ordinal))
                        Console.Error.WriteLine($"Provedor PIX indisponivel: {ex.Message}");
                    lastHealthError = ex.Message;
                }
                nextHealthCheck = DateTimeOffset.UtcNow.AddSeconds(providerHealthy ? 60 : 10);
            }
            var readyForNewPix = providerHealthy && licenseAvailability.AllowsNewPix;
            heartbeat?.Update(provider.Name == "mock" || secrets.TryLoad().IsAvailable,
                readyForNewPix, readyForNewPix ? "online"
                    : !licenseAvailability.AllowsNewPix ? "license_denied"
                    : baseConfigurationUsesLegacyTestPdv || ownerSetupPendingWithoutCoordinator || ownerInfrastructure is { Ready: false }
                        ? "owner_setup_pending" : "provider_unavailable");
            if (readyForNewPix) await engine.RunOnceAsync(cancellation.Token,
                heartbeat is null ? null : heartbeat.PulseSafely);
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

enum AgentRunMode { Daemon, OneShot, Administrative }

sealed record AgentCommand(bool Daemon, bool Once, bool SetToken, bool SelfTest, bool VerifyCommercialBuild, bool CheckKioskIdentity, bool CheckProvider, bool PrepareCredentialEditor, bool AcceptCredentialOnce, bool OnlineActivate,
    bool MercadoPagoInventory, string MercadoPagoSetupFile, string ConfigureOwnerFile, string ApproveId,
    string LicenseRequestFile, string InstallLicenseFile, bool LicenseStatus, string OnlineConfigureFile, string BridgeDirectory)
{
    public bool HasLicenseAdministrativeMode => !string.IsNullOrWhiteSpace(LicenseRequestFile)
        || !string.IsNullOrWhiteSpace(InstallLicenseFile) || LicenseStatus;

    public bool HasAdministrativeMode => SetToken || SelfTest || VerifyCommercialBuild || CheckKioskIdentity || CheckProvider
        || PrepareCredentialEditor || AcceptCredentialOnce || OnlineActivate || MercadoPagoInventory
        || !string.IsNullOrWhiteSpace(MercadoPagoSetupFile)
        || !string.IsNullOrWhiteSpace(ConfigureOwnerFile)
        || !string.IsNullOrWhiteSpace(OnlineConfigureFile)
        || !string.IsNullOrWhiteSpace(ApproveId)
        || HasLicenseAdministrativeMode;

    public AgentRunMode RunMode => Daemon ? AgentRunMode.Daemon
        : HasAdministrativeMode ? AgentRunMode.Administrative : AgentRunMode.OneShot;

    public static AgentCommand Parse(string[] args)
    {
        var daemon = false;
        var once = false;
        var setToken = false;
        var selfTest = false;
        var verifyCommercialBuild = false;
        var checkKioskIdentity = false;
        var checkProvider = false;
        var prepareCredentialEditor = false;
        var acceptCredentialOnce = false;
        var onlineActivate = false;
        var onlineConfigureFile = "";
        var mercadoPagoInventory = false;
        var mercadoPagoSetupFile = "";
        var configureOwnerFile = "";
        var approveId = "";
        var licenseRequestFile = "";
        var installLicenseFile = "";
        var licenseStatus = false;
        var bridgeDirectory = "";
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--daemon", StringComparison.OrdinalIgnoreCase)) { daemon = true; continue; }
            if (args[i].Equals("--once", StringComparison.OrdinalIgnoreCase)) { once = true; continue; }
            if (args[i].Equals("--set-token", StringComparison.OrdinalIgnoreCase)) { setToken = true; continue; }
            if (args[i].Equals("--self-test", StringComparison.OrdinalIgnoreCase)) { selfTest = true; continue; }
            if (args[i].Equals("--verify-commercial-build", StringComparison.OrdinalIgnoreCase)) { verifyCommercialBuild = true; continue; }
            if (args[i].Equals("--check-kiosk-identity", StringComparison.OrdinalIgnoreCase)) { checkKioskIdentity = true; continue; }
            if (args[i].Equals("--check-provider", StringComparison.OrdinalIgnoreCase)) { checkProvider = true; continue; }
            if (args[i].Equals("--prepare-credential-editor", StringComparison.OrdinalIgnoreCase)) { prepareCredentialEditor = true; continue; }
            if (args[i].Equals("--accept-credential-once", StringComparison.OrdinalIgnoreCase)) { acceptCredentialOnce = true; continue; }
            if (args[i].Equals("--online-activate", StringComparison.OrdinalIgnoreCase)) { onlineActivate = true; continue; }
            if (args[i].Equals("--online-configure", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new InvalidOperationException("--online-configure exige o caminho de um arquivo JSON.");
                onlineConfigureFile = args[++i];
                continue;
            }
            if (args[i].Equals("--mercadopago-inventory", StringComparison.OrdinalIgnoreCase)) { mercadoPagoInventory = true; continue; }
            if (args[i].Equals("--license-status", StringComparison.OrdinalIgnoreCase)) { licenseStatus = true; continue; }
            if (args[i].Equals("--license-request", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new InvalidOperationException("--license-request exige o caminho do novo pedido JSON.");
                licenseRequestFile = args[++i];
                continue;
            }
            if (args[i].Equals("--install-license", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new InvalidOperationException("--install-license exige o caminho da licenca assinada.");
                installLicenseFile = args[++i];
                continue;
            }
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
        var exclusiveModes = (setToken ? 1 : 0) + (selfTest ? 1 : 0) + (verifyCommercialBuild ? 1 : 0)
            + (checkKioskIdentity ? 1 : 0)
            + (checkProvider ? 1 : 0) + (prepareCredentialEditor ? 1 : 0)
            + (acceptCredentialOnce ? 1 : 0)
            + (onlineActivate ? 1 : 0)
            + (!string.IsNullOrWhiteSpace(onlineConfigureFile) ? 1 : 0)
            + (mercadoPagoInventory ? 1 : 0) + (!string.IsNullOrWhiteSpace(mercadoPagoSetupFile) ? 1 : 0)
            + (!string.IsNullOrWhiteSpace(configureOwnerFile) ? 1 : 0)
            + (!string.IsNullOrWhiteSpace(approveId) ? 1 : 0)
            + (!string.IsNullOrWhiteSpace(licenseRequestFile) ? 1 : 0)
            + (!string.IsNullOrWhiteSpace(installLicenseFile) ? 1 : 0)
            + (licenseStatus ? 1 : 0);
        if (exclusiveModes > 1)
            throw new InvalidOperationException("use somente um modo administrativo por execucao.");
        if (daemon && (once || exclusiveModes != 0))
            throw new InvalidOperationException("--daemon nao pode ser combinado com --once nem com modos administrativos.");
        if (!daemon && !once && exclusiveModes == 0)
            throw new InvalidOperationException("informe explicitamente --daemon, --once ou um modo administrativo.");
        return new AgentCommand(daemon, once, setToken, selfTest, verifyCommercialBuild, checkKioskIdentity, checkProvider, prepareCredentialEditor, acceptCredentialOnce, onlineActivate,
            mercadoPagoInventory, mercadoPagoSetupFile, configureOwnerFile, approveId,
            licenseRequestFile, installLicenseFile, licenseStatus, onlineConfigureFile, bridgeDirectory);
    }
}

static class CommercialLicenseRuntime
{
    private const int LicenseExitCode = 23;

    public static int? HandleAdministrativeCommand(
        AgentCommand command,
        CommercialLicenseBuildIdentity buildIdentity,
        CommercialLicensePolicy policy,
        IPixMachineBinding machineBinding,
        CommercialLicenseVerifier? verifier,
        PixPaths paths)
    {
        if (!command.HasLicenseAdministrativeMode) return null;
        if (!buildIdentity.Required || verifier is null)
        {
            Console.Error.WriteLine("Esta compilacao de desenvolvimento nao possui emissor de licenca comercial incorporado.");
            return LicenseExitCode;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(command.LicenseRequestFile))
            {
                var request = CommercialLicenseCodec.CreateActivationRequest(policy, machineBinding);
                var bytes = CommercialLicenseCodec.SerializeActivationRequest(request);
                WriteNewPublicFile(command.LicenseRequestFile, bytes);
                Console.WriteLine($"Pedido de ativacao criado: {Path.GetFullPath(command.LicenseRequestFile)}");
                Console.WriteLine("O pedido contem somente a identidade publica do vinculo desta maquina; nenhuma credencial PIX foi incluida.");
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(command.InstallLicenseFile))
            {
                var bytes = ReadRegularLicenseFile(command.InstallLicenseFile);
                var result = verifier.Validate(bytes);
                if (!result.IsValid)
                {
                    Console.Error.WriteLine($"Licenca recusada: {result.Message}");
                    return LicenseExitCode;
                }
                WriteInstalledLicense(paths.LicenseFile, bytes);
                WindowsFileSecurity.HardenCredentialFile(paths.LicenseFile, allowBuiltinUsersRead: false);
                Console.WriteLine($"Licenca comercial instalada e vinculada a este quiosque: {result.LicenseId}");
                return 0;
            }

            var status = verifier.ValidateFile(paths.LicenseFile);
            if (!status.IsValid)
            {
                Console.Error.WriteLine($"Licenca comercial indisponivel: {status.Message}");
                return LicenseExitCode;
            }
            Console.WriteLine($"Licenca comercial valida para este quiosque: {status.LicenseId}");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or CryptographicException or FormatException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Operacao de licenca recusada: {ex.Message}");
            return LicenseExitCode;
        }
    }

    private static byte[] ReadRegularLicenseFile(string path)
    {
        var full = Path.GetFullPath(path);
        var attributes = File.GetAttributes(full);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new SecurityException("O arquivo de licenca nao pode ser diretorio nem redirecionamento.");
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > CommercialLicenseCodec.MaximumEnvelopeBytes)
            throw new FormatException("O tamanho do arquivo de licenca e invalido.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new FormatException("O arquivo de licenca mudou durante a leitura.");
        return bytes;
    }

    private static void WriteNewPublicFile(string path, byte[] bytes)
    {
        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw new DirectoryNotFoundException("A pasta escolhida para o pedido de ativacao nao existe.");
        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            throw new SecurityException("A pasta do pedido de ativacao nao pode ser um redirecionamento.");
        using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void WriteInstalledLicense(string destination, byte[] bytes)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
        }
    }
}

sealed record PixDaemonDescriptor(int ProcessId, ulong ProcessStartFileTimeUtc, string ManagerTokenHash);

// Identidade efemera compartilhada apenas entre o frontend que criou este
// daemon e o proprio daemon. Os mutexes nao sao trava de dados (essa funcao
// continua pertencendo a .agent.lock): eles provam que o PID representa o
// modo persistente e impedem dois daemons simultaneos durante a inicializacao.
sealed class PixDaemonIdentity : IDisposable
{
    internal const string ManagerTokenEnvironmentName = "TURBORAMA_PIX_MANAGER_TOKEN";
    internal const string SingletonMutexName = @"Local\TurboRamaPixAgent-Daemon-v1";
    private const string PidMutexPrefix = @"Local\TurboRamaPixAgent-Daemon-v1-";

    private Mutex? _singletonMutex;
    private Mutex? _pidMutex;

    private PixDaemonIdentity(PixDaemonDescriptor descriptor, Mutex singletonMutex, Mutex pidMutex)
    {
        Descriptor = descriptor;
        _singletonMutex = singletonMutex;
        _pidMutex = pidMutex;
    }

    public PixDaemonDescriptor Descriptor { get; }

    public static PixDaemonIdentity CreateForCurrentProcess()
    {
        var token = TakeManagerTokenFromEnvironment();
        var descriptor = new PixDaemonDescriptor(Environment.ProcessId,
            ReadCurrentProcessStartFileTimeUtc(), HashManagerToken(token));
        Mutex? singleton = null;
        Mutex? perPid = null;
        try
        {
            singleton = CreateExclusiveMutex(SingletonMutexName);
            perPid = CreateExclusiveMutex(PidMutexPrefix + descriptor.ProcessId.ToString(CultureInfo.InvariantCulture));
            return new PixDaemonIdentity(descriptor, singleton, perPid);
        }
        catch
        {
            perPid?.Dispose();
            singleton?.Dispose();
            throw;
        }
    }

    internal static bool IsManagerToken(string? value) => value is { Length: 64 }
        && value.All(static character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    internal static string SelectManagerToken(string? candidate, Func<byte[]> randomBytes)
    {
        if (IsManagerToken(candidate)) return candidate!;
        var generated = randomBytes();
        if (generated.Length != 32)
            throw new CryptographicException("o gerador do token do daemon retornou tamanho invalido");
        try { return Convert.ToHexString(generated).ToLowerInvariant(); }
        finally { CryptographicOperations.ZeroMemory(generated); }
    }

    internal static string HashManagerToken(string token)
    {
        var bytes = Encoding.ASCII.GetBytes(token);
        var digest = SHA256.HashData(bytes);
        try { return Convert.ToHexString(digest).ToLowerInvariant(); }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static string TakeManagerTokenFromEnvironment()
    {
        string? supplied = null;
        try
        {
            supplied = Environment.GetEnvironmentVariable(ManagerTokenEnvironmentName,
                EnvironmentVariableTarget.Process);
            return SelectManagerToken(supplied, () => RandomNumberGenerator.GetBytes(32));
        }
        finally
        {
            // Nao permita que processos filhos ou bibliotecas consultem o
            // segredo entregue pelo manager depois de sua derivacao.
            Environment.SetEnvironmentVariable(ManagerTokenEnvironmentName, null,
                EnvironmentVariableTarget.Process);
        }
    }

    private static Mutex CreateExclusiveMutex(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        if (createdNew) return mutex;
        mutex.Dispose();
        throw new InvalidOperationException($"ja existe um daemon PIX identificado por {name}");
    }

    private static ulong ReadCurrentProcessStartFileTimeUtc()
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("a identidade persistente do agente PIX exige Windows");
        if (!GetProcessTimes(GetCurrentProcess(), out var creation, out _, out _, out _))
            throw new InvalidOperationException($"o Windows nao informou o instante de criacao do daemon (codigo {Marshal.GetLastWin32Error()})");
        var value = ((ulong)creation.HighDateTime << 32) | creation.LowDateTime;
        if (value == 0) throw new InvalidOperationException("o instante de criacao do daemon e invalido");
        return value;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _pidMutex, null)?.Dispose();
        Interlocked.Exchange(ref _singletonMutex, null)?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(IntPtr process, out NativeFileTime creationTime,
        out NativeFileTime exitTime, out NativeFileTime kernelTime, out NativeFileTime userTime);
}

sealed record PixOptions
{
    public string Provider { get; init; } = "mock";
    public bool OnlineLicensingEnabled { get; init; }
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
    public bool RequireTpmMachineBinding { get; init; }
    public MercadoPagoOptions MercadoPago { get; init; } = new();
    public AdapterOptions Adapter { get; init; } = new();
    public OnlinePixOptions Online { get; init; } = new();

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
        var legacyOnlineProvider = provider == "online";
        if (legacyOnlineProvider) provider = "mercadopago";
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
            OnlineLicensingEnabled = OnlineLicensingEnabled || legacyOnlineProvider,
            AllowedMinutes = prices.Keys.Order().ToList(),
            PackagePricesCents = prices,
            PollSeconds = Math.Clamp(PollSeconds, 2, 30),
            PaymentExpirationMinutes = Math.Clamp(PaymentExpirationMinutes, 1, 60),
            HttpTimeoutSeconds = Math.Clamp(HttpTimeoutSeconds, 5, 60),
            MaxRetrySeconds = Math.Clamp(MaxRetrySeconds, 30, 1800),
            MercadoPago = (MercadoPago ?? new MercadoPagoOptions()).Normalize(),
            Adapter = (Adapter ?? new AdapterOptions()).Normalize(),
            Online = (Online ?? new OnlinePixOptions()).Normalize()
        };
    }

    public long PriceFor(int minutes)
        => PackagePricesCents.TryGetValue(minutes, out var cents) ? cents : 0;

    public void ValidateForStartup(bool configurationOnly)
    {
        if (OnlineLicensingEnabled) Online.Validate(configurationOnly: false);
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
                && !MercadoPagoOptions.IsLegacyTestExternalPosId(MercadoPago.ExternalPosId)
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
    // Ambiente declarado pelo operador. Quando /users/me fornece um sinal
    // verificavel (test_user ou e-mail @testuser.com), o agente falha fechado
    // se a conta nao corresponder a esta declaracao.
    public string Environment { get; init; } = "production";
    public string ExternalPosId { get; init; } = "TURBORAMAKIOSK01";
    public string DescriptionPrefix { get; init; } = "Tempo TurboRama";

    public MercadoPagoOptions Normalize()
    {
        var environment = (Environment ?? "").Trim().ToLowerInvariant();
        if (environment is not ("production" or "sandbox"))
            throw new InvalidOperationException("MercadoPago.Environment deve ser production ou sandbox.");
        return this with { Environment = environment };
    }

    public static bool IsLegacyTestExternalPosId(string? value)
        => (value ?? "").Trim().Equals("LZPIXCOMP", StringComparison.OrdinalIgnoreCase);
}

sealed record MercadoPagoSetupRequest
{
    public string ExpectedAccountId { get; init; } = "";
    public string StoreName { get; init; } = "TurboRama";
    public string StoreExternalId { get; init; } = "LZLOJA01";
    public string PosName { get; init; } = "TurboRama Kiosk";
    public string PosExternalId { get; init; } = "TURBORAMAKIOSK01";
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

// Campos administrativos que pertencem ao licenciamento/servidor e nao ao
// provedor de pagamento. O configurador Mercado Pago pode substituir um PDV
// quebrado, mas deve conservar estes valores byte-logicamente equivalentes.
sealed record PixOwnerControlSnapshot
{
    public bool OnlineLicensingEnabled { get; init; }
    public string OnlineBaseUrl { get; init; } = "https://licensing.example.invalid/";
    public string OnlineLicenseId { get; init; } = "CONFIGURE-A-LICENCA";
    public string OnlineProtectionProfile { get; init; } = "SOFTWARE_BOUND_ONLINE";
    public bool PixEnabled { get; init; } = true;
    public long OnlineConfigurationVersion { get; init; }
    public bool OnlineConfigurationPending { get; init; }

    public static PixOwnerControlSnapshot? LoadIfPresent(string bridgeDirectory)
    {
        var file = Path.Combine(bridgeDirectory, "owner-settings.json");
        if (!File.Exists(file)) return null;
        var info = new FileInfo(file);
        if (info.Length is <= 0 or > 65_536)
            throw new InvalidOperationException("Cadastro PIX existente tem tamanho invalido; a licenca nao foi alterada.");
        var raw = JsonSerializer.Deserialize<PixOwnerSettings>(File.ReadAllText(file, Encoding.UTF8), Json.Options)
            ?? throw new InvalidOperationException("Cadastro PIX existente esta vazio; a licenca nao foi alterada.");
        if (raw.SchemaVersion != 1)
            throw new InvalidOperationException("Versao do cadastro PIX existente nao e suportada; a licenca nao foi alterada.");
        if (raw.OnlineConfigurationVersion < 0)
            throw new InvalidOperationException("Versao on-line do cadastro PIX existente e invalida; a licenca nao foi alterada.");

        var enabled = raw.OnlineLicensingEnabled
            || string.Equals(raw.Provider?.Trim(), "online", StringComparison.OrdinalIgnoreCase);
        var normalized = new OnlinePixOptions
        {
            BaseUrl = raw.OnlineBaseUrl,
            LicenseId = raw.OnlineLicenseId,
            ProtectionProfile = raw.OnlineProtectionProfile,
            ProviderId = "turborama-online"
        }.Normalize();
        if (enabled) normalized.Validate(configurationOnly: false);
        return new PixOwnerControlSnapshot
        {
            OnlineLicensingEnabled = enabled,
            OnlineBaseUrl = normalized.BaseUrl,
            OnlineLicenseId = normalized.LicenseId,
            OnlineProtectionProfile = normalized.ProtectionProfile,
            PixEnabled = raw.PixEnabled,
            OnlineConfigurationVersion = raw.OnlineConfigurationVersion,
            OnlineConfigurationPending = raw.OnlineConfigurationPending
        };
    }

    public PixOptions Apply(PixOptions options)
        => !OnlineLicensingEnabled ? options : (options with
        {
            OnlineLicensingEnabled = true,
            Online = new OnlinePixOptions
            {
                BaseUrl = OnlineBaseUrl,
                LicenseId = OnlineLicenseId,
                ProtectionProfile = OnlineProtectionProfile,
                ProviderId = "turborama-online"
            }.Normalize()
        }).Normalize();

    public PixOwnerSettings Preserve(PixOwnerSettings settings)
        => settings with
        {
            OnlineLicensingEnabled = OnlineLicensingEnabled,
            OnlineBaseUrl = OnlineBaseUrl,
            OnlineLicenseId = OnlineLicenseId,
            OnlineProtectionProfile = OnlineProtectionProfile,
            PixEnabled = PixEnabled,
            OnlineConfigurationVersion = OnlineConfigurationVersion,
            OnlineConfigurationPending = OnlineConfigurationPending
        };
}

sealed record PixOwnerSettings
{
    public int SchemaVersion { get; init; } = 1;
    public bool Enabled { get; init; }
    // pending: cadastro e token foram preservados, mas nenhuma cobranca pode
    // ser criada ate conta, Loja e PDV serem realmente confirmados.
    public string SetupState { get; init; } = "ready";
    public string Provider { get; init; } = "mercadopago";
    public string MercadoPagoEnvironment { get; init; } = "production";
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
    public bool OnlineLicensingEnabled { get; init; }
    public string OnlineBaseUrl { get; init; } = "https://licensing.example.invalid/";
    public string OnlineLicenseId { get; init; } = "CONFIGURE-A-LICENCA";
    public string OnlineProtectionProfile { get; init; } = "SOFTWARE_BOUND_ONLINE";
    public bool PixEnabled { get; init; } = true;
    public long OnlineConfigurationVersion { get; init; }
    public bool OnlineConfigurationPending { get; init; }
    public Dictionary<int, long> PackagePricesCents { get; init; } = new();

    public static PixOwnerSettings? LoadIfPresent(string bridgeDirectory)
    {
        var file = Path.Combine(bridgeDirectory, "owner-settings.json");
        if (!File.Exists(file)) return null;
        var info = new FileInfo(file);
        if (info.Length is <= 0 or > 65_536)
            throw new InvalidOperationException("Cadastro do proprietario PIX tem tamanho invalido.");
        var settings = (JsonSerializer.Deserialize<PixOwnerSettings>(File.ReadAllText(file, Encoding.UTF8), Json.Options)
            ?? throw new InvalidOperationException("Cadastro do proprietario PIX esta vazio.")).NormalizeLegacy();
        settings.Validate();
        return settings;
    }

    private PixOwnerSettings NormalizeLegacy()
    {
        var provider = (Provider ?? "").Trim();
        var legacyTestPdv = MercadoPagoOptions.IsLegacyTestExternalPosId(PosExternalId);
        if (!provider.Equals("online", StringComparison.OrdinalIgnoreCase) && !legacyTestPdv) return this;
        var hasCompleteMercadoPago = !string.IsNullOrWhiteSpace(AccountId)
            && !string.IsNullOrWhiteSpace(PosExternalId)
            && new string((PostalCode ?? "").Where(char.IsAsciiDigit).ToArray()).Length == 8
            && !string.IsNullOrWhiteSpace(StreetNumber);
        var setupState = legacyTestPdv ? "pending" : Enabled && hasCompleteMercadoPago ? SetupState : "pending";
        return this with
        {
            Provider = "mercadopago",
            OnlineLicensingEnabled = OnlineLicensingEnabled || provider.Equals("online", StringComparison.OrdinalIgnoreCase),
            Enabled = Enabled && hasCompleteMercadoPago,
            SetupState = setupState,
            PosExternalId = legacyTestPdv ? "TURBORAMAKIOSK01" : PosExternalId,
            OnlineConfigurationPending = false
        };
    }

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidOperationException("Versao do cadastro PIX nao e suportada.");
        if (OnlineLicensingEnabled)
        {
            new OnlinePixOptions
            {
                BaseUrl = OnlineBaseUrl,
                LicenseId = OnlineLicenseId,
                ProtectionProfile = OnlineProtectionProfile,
                ProviderId = "turborama-online"
            }.Normalize().Validate(configurationOnly: false);
        }
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
        if ((MercadoPagoEnvironment ?? "").Trim().ToLowerInvariant() is not ("production" or "sandbox"))
            throw new InvalidOperationException("Ambiente do Mercado Pago no cadastro e invalido.");
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
        var licensed = (options with
        {
            OnlineLicensingEnabled = OnlineLicensingEnabled,
            Online = OnlineLicensingEnabled
                ? new OnlinePixOptions
                {
                    BaseUrl = OnlineBaseUrl,
                    LicenseId = OnlineLicenseId,
                    ProtectionProfile = OnlineProtectionProfile,
                    ProviderId = "turborama-online"
                }.Normalize()
                : options.Online
        }).Normalize();
        if (!Enabled) return licensed;
        var provider = Provider.Trim().ToLowerInvariant();
        return (licensed with
        {
            Provider = provider,
            ProductionEnabled = true,
            AllowedMinutes = PackagePricesCents.Keys.Order().ToList(),
            PackagePricesCents = new Dictionary<int, long>(PackagePricesCents),
            MercadoPago = licensed.MercadoPago with
            {
                Environment = (MercadoPagoEnvironment ?? "production").Trim().ToLowerInvariant(),
                ExternalPosId = PosExternalId.Trim()
            },
            Adapter = provider == "adapter"
                ? new AdapterOptions { BaseUrl = AdapterBaseUrl, ProviderId = AdapterProviderId }.Normalize()
                : licensed.Adapter
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
    // Obrigatorio para Mercado Pago. O prefixo APP_USR nao distingue uma
    // credencial real de uma credencial de teste, portanto o operador precisa
    // declarar a intencao e /users/me precisa confirma-la antes de qualquer
    // segredo, cadastro ou recurso remoto ser alterado.
    public string MercadoPagoEnvironment { get; init; } = "";
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
        string credential, PixOptions baseOptions, PixPaths paths, PixSecretStore secrets, CancellationToken token,
        HttpMessageHandler? mercadoPagoHandler = null, PixOwnerControlSnapshot? ownerControlSnapshot = null)
    {
        var provider = request.Provider.Trim().ToLowerInvariant();
        if (provider is not ("mercadopago" or "adapter"))
            throw new InvalidOperationException("selecione Mercado Pago ou Adaptador bancario");
        ValidatePrices(request.PackagePricesCents);

        // A credencial recebida pelo pipe permanece somente nesta instancia e
        // nunca e publicada no ambiente do processo (nem herdada por filhos).
        // O Save continua persistindo o valor via DPAPI no mesmo ponto atomico
        // do fluxo de configuracao.
        var transientSecrets = secrets.WithTransientSecret(credential);
        return provider == "mercadopago"
            ? await ConfigureMercadoPagoAsync(request, credential, baseOptions, paths, transientSecrets, token,
                mercadoPagoHandler, ownerControlSnapshot)
            : await ConfigureAdapterAsync(request, credential, baseOptions, paths, transientSecrets, token,
                ownerControlSnapshot);
    }

    private static async Task<PixOwnerProvisioningResult> ConfigureMercadoPagoAsync(PixOwnerProvisioningRequest request,
        string credential, PixOptions baseOptions, PixPaths paths, PixSecretStore secrets, CancellationToken token,
        HttpMessageHandler? handler, PixOwnerControlSnapshot? ownerControlSnapshot)
    {
        if (credential.Length is < 40 or > 384 || !credential.StartsWith("APP_USR-", StringComparison.Ordinal)
            || credential.Any(char.IsWhiteSpace) || credential.Any(char.IsControl))
            throw new SecurityException("Access Token do Mercado Pago esta incompleto ou em formato invalido");

        var declaredEnvironment = ValidateMercadoPagoEnvironment(request.MercadoPagoEnvironment);
        // APP_USR e credencial comercial. Telas antigas ou cadastros antigos
        // podem ainda enviar "sandbox"; o agente corrige isso antes de ler
        // /users/me, gravar owner-settings ou salvar o segredo. Assim um token
        // real nunca fica persistido como TESTE e nunca bloqueia o daemon depois.
        if (credential.StartsWith("APP_USR-", StringComparison.Ordinal)
            && declaredEnvironment.Equals("sandbox", StringComparison.OrdinalIgnoreCase))
            declaredEnvironment = "production";
        var requestedStoreName = RequiredText(request.StoreName, 2, 59, "nome da loja");
        var requestedPosName = RequiredText(request.PosName, 2, 44, "nome do caixa");
        var requestedStoreExternalId = string.IsNullOrWhiteSpace(request.StoreExternalId)
            ? ""
            : ValidateExternalId(request.StoreExternalId, 60, "loja");
        var requestedPosExternalId = string.IsNullOrWhiteSpace(request.PosExternalId)
            ? ""
            : ValidateExternalId(request.PosExternalId, 40, "caixa");
        // LZPIXCOMP foi um identificador de teste e ja provocou 404 real. Ele
        // nunca e reutilizado nem recriado. Campo vazio faz o inventario real
        // decidir entre reaproveitar um unico PDV ativo ou reparar o cadastro.
        if (MercadoPagoOptions.IsLegacyTestExternalPosId(requestedPosExternalId))
            requestedPosExternalId = "";

        var probeOptions = (baseOptions with
        {
            Provider = "mercadopago",
            ProductionEnabled = true,
            AllowedMinutes = request.PackagePricesCents.Keys.Order().ToList(),
            PackagePricesCents = new Dictionary<int, long>(request.PackagePricesCents),
            MercadoPago = baseOptions.MercadoPago with
            {
                Environment = declaredEnvironment,
                // A consulta de inventario nao usa o PDV, mas PixOptions exige
                // um identificador sintaticamente valido. O ID definitivo so e
                // escolhido depois da leitura fail-closed da conta.
                ExternalPosId = string.IsNullOrWhiteSpace(requestedPosExternalId)
                    ? "TURBORAMAPROBE"
                    : requestedPosExternalId
            }
        }).Normalize();
        var mercadoPago = new MercadoPagoPixProvider(probeOptions, secrets, handler);
        var cacheFile = Path.Combine(paths.Root, "owner-address-cache.json");
        var locationWasRequired = false;
        var pendingPersisted = false;

        try
        {
            // /users/me devolve o titular real autorizado pelo token. Client ID
            // e ID da aplicacao jamais sao aceitos como conta. APP_USR sempre
            // entra como producao neste fluxo comercial.
            var inventory = await mercadoPago.GetInfrastructureAsync(token);
            var accountId = inventory.AccountId.Trim();
            if (accountId.Length is < 5 or > 24 || !accountId.All(char.IsAsciiDigit))
                throw new SecurityException("o Access Token nao retornou um User ID de conta valido");

            // A máquina aceita apenas uma conta Mercado Pago. A conta é
            // descoberta pelo /users/me e comparada com o cadastro local
            // antes de qualquer decisão de Loja/PDV, gravação de segredo ou
            // POST remoto. Trocar silenciosamente para outra conta deixaria
            // o token, o cadastro e o caixa de uma máquina misturados.
            ValidateSingleMercadoPagoAccount(ReadExistingMercadoPagoAccountId(paths.Root), accountId);

            // Esta decisao e estritamente de leitura. Em particular, uma conta
            // que ja possua recursos ambiguos nao perde a credencial/cadastro
            // anterior e nao recebe um POST acidental.
            var decision = DecideMercadoPagoProvisioning(requestedStoreName, requestedStoreExternalId,
                requestedPosName, requestedPosExternalId, inventory);
            var pendingOwner = new PixOwnerSettings
            {
                Enabled = true,
                SetupState = "pending",
                Provider = "mercadopago",
                MercadoPagoEnvironment = declaredEnvironment,
                AccountId = accountId,
                StoreExternalId = decision.StoreExternalId,
                StoreName = requestedStoreName,
                PosExternalId = decision.PosExternalId,
                PosName = requestedPosName,
                PostalCode = Digits(request.PostalCode),
                StreetNumber = RequiredText(request.StreetNumber, 1, 20, "numero do estabelecimento"),
                Reference = RequiredText(request.Reference, 1, 120, "referencia do estabelecimento"),
                OnlineLicensingEnabled = baseOptions.OnlineLicensingEnabled,
                OnlineBaseUrl = baseOptions.Online.BaseUrl,
                OnlineLicenseId = baseOptions.Online.LicenseId,
                OnlineProtectionProfile = baseOptions.Online.ProtectionProfile,
                PackagePricesCents = new Dictionary<int, long>(request.PackagePricesCents)
            };
            if (ownerControlSnapshot is not null)
                pendingOwner = ownerControlSnapshot.Preserve(pendingOwner);
            pendingOwner.Validate();

            locationWasRequired = decision.CreateStore;
            var setup = decision.CreateStore
                ? await pendingOwner.BuildSetupRequestAsync(paths, token)
                : pendingOwner.BuildSetupRequestForExistingStore();

            // A barreira local vem antes da troca da credencial: se qualquer
            // etapa seguinte falhar, o daemon encontra owner-settings pendente
            // e nunca publica compras como prontas com um par incoerente.
            SaveOwnerSettings(paths, pendingOwner);
            pendingPersisted = true;
            OwnerSetupStatus.Publish(paths, "pending",
                "Conta e recursos PIX selecionados com seguranca. Validando credencial e infraestrutura antes de liberar cobrancas.");
            secrets.Save(credential);

            MercadoPagoSetupResult infrastructure;
            if (decision.RequiresRemoteWrite)
            {
                // Um POST que tenha sido aceito apesar de uma resposta perdida
                // permanece bloqueado e pode ser auditado sem trocar novamente
                // o cadastro pronto anterior por engano.
                infrastructure = await mercadoPago.EnsureInfrastructureAsync(setup, token,
                    new MercadoPagoCreationPolicy(decision.CreateStore, decision.CreatePointOfSale,
                        decision.RequireEmptyInventoryBeforeCreation));
            }
            else
            {
                infrastructure = new MercadoPagoSetupResult(accountId, decision.Store!, decision.PointOfSale!,
                    StoreCreated: false, PointOfSaleCreated: false);
            }

            if (infrastructure.StoreCreated)
            {
                BrazilianPostalAddress.SaveConfirmedCache(cacheFile, pendingOwner.PostalCode, pendingOwner.StreetNumber,
                    new BrazilianPostalAddress(setup.StreetName, setup.CityName, setup.StateName,
                        setup.Latitude, setup.Longitude, setup.LocationSource));
            }
            mercadoPago.UseExternalPosId(infrastructure.PointOfSale.ExternalId);
            await mercadoPago.CheckHealthAsync(token);

            var confirmed = pendingOwner with
            {
                SetupState = "ready",
                AccountId = infrastructure.AccountId,
                StoreExternalId = infrastructure.Store.ExternalId,
                PosExternalId = infrastructure.PointOfSale.ExternalId
            };
            confirmed.Validate();
            SaveOwnerSettings(paths, confirmed);
            OwnerSetupStatus.Publish(paths, "ready",
                $"Conta {infrastructure.AccountId}, loja {infrastructure.Store.ExternalId} e caixa {infrastructure.PointOfSale.ExternalId} confirmados.");
            return new PixOwnerProvisioningResult("mercadopago", infrastructure.AccountId,
                infrastructure.Store.ExternalId, infrastructure.PointOfSale.ExternalId, "ready",
                "Conta, Access Token, loja e caixa validados. PIX pronto para uso.");
        }
        catch (MercadoPagoApiException ex) when (pendingPersisted && locationWasRequired && IsLocationSetupFailure(ex))
        {
            BrazilianPostalAddress.InvalidateCache(cacheFile);
            OwnerSetupStatus.Publish(paths, "needs_address_confirmation",
                "O Mercado Pago recusou a localizacao da loja. O cadastro foi salvo, mas o endereco sera consultado novamente antes de criar cobrancas.");
            throw new InvalidOperationException("O Mercado Pago recusou a localizacao da loja. Verifique CEP e numero; a configuracao continua salva como pendente.", ex);
        }
        catch (Exception ex)
        {
            if (pendingPersisted)
                OwnerSetupStatus.Publish(paths, "pending",
                    $"Cadastro PIX salvo. A confirmacao automatica sera retomada quando os servicos voltarem: {SafeSetupMessage(ex.Message)}");
            throw;
        }
    }

    internal static MercadoPagoProvisioningDecision DecideMercadoPagoProvisioning(string storeName,
        string requestedStoreExternalId, string posName, string requestedPosExternalId,
        MercadoPagoInfrastructure inventory)
    {
        var accountId = (inventory.AccountId ?? "").Trim();
        if (accountId.Length is < 5 or > 24 || !accountId.All(char.IsAsciiDigit))
            throw new SecurityException("o inventario do Mercado Pago nao pertence a uma conta valida");

        var cleanStoreName = RequiredText(storeName, 2, 59, "nome da loja");
        var cleanPosName = RequiredText(posName, 2, 44, "nome do caixa");
        var storeExternalId = string.IsNullOrWhiteSpace(requestedStoreExternalId)
            ? ""
            : ValidateExternalId(requestedStoreExternalId, 60, "loja");
        var posExternalId = string.IsNullOrWhiteSpace(requestedPosExternalId)
            ? ""
            : ValidateExternalId(requestedPosExternalId, 40, "caixa");
        if (MercadoPagoOptions.IsLegacyTestExternalPosId(posExternalId))
            posExternalId = "";
        var storeWasExplicit = !string.IsNullOrWhiteSpace(storeExternalId);
        var posWasExplicit = !string.IsNullOrWhiteSpace(posExternalId);

        var stores = inventory.Stores ?? Array.Empty<MercadoPagoStoreInfo>();
        var points = inventory.PointsOfSale ?? Array.Empty<MercadoPagoPosInfo>();
        if (stores.Count == 0 && points.Count == 0)
        {
            return new MercadoPagoProvisioningDecision(accountId,
                storeWasExplicit ? storeExternalId : CreatePendingExternalId("LZLOJA", 60),
                posWasExplicit ? posExternalId : CreatePendingExternalId("LZPIX", 40),
                null, null, CreateStore: true, CreatePointOfSale: true,
                RequireEmptyInventoryBeforeCreation: true);
        }

        var requestedStore = storeWasExplicit
            ? SingleStoreByExternalId(stores, storeExternalId)
            : null;
        var requestedPoint = posWasExplicit
            ? SinglePointByExternalId(points, posExternalId)
            : null;

        // Operadores frequentemente colam User ID, Client ID ou numero da
        // aplicacao do Mercado Pago nos campos de external_id. Esses numeros
        // nao identificam Loja/PDV QR. Se nao existirem no inventario, ignore
        // o valor e tente selecionar o par real ja cadastrado na conta.
        if (storeWasExplicit && requestedStore is null && LooksLikeMercadoPagoNumericId(storeExternalId))
        {
            storeExternalId = "";
            storeWasExplicit = false;
        }
        if (posWasExplicit && requestedPoint is null && LooksLikeMercadoPagoNumericId(posExternalId))
        {
            posExternalId = "";
            posWasExplicit = false;
        }

        // Em uma conta ja povoada, um ID explicito inexistente e tratado como
        // erro de selecao, nunca como autorizacao implicita para cadastrar mais
        // uma Loja ou PDV. A unica excecao e o ID legado LZPIXCOMP, apagado
        // acima: ele autoriza apenas o reparo controlado de um PDV em uma loja
        // que ja foi identificada de forma inequivoca.
        if (storeWasExplicit && requestedStore is null)
            throw ExplicitSelectionRequired("o StoreExternalId informado nao existe nesta conta; a criacao automatica so e permitida quando a conta esta totalmente vazia");
        if (posWasExplicit && requestedPoint is null)
            throw ExplicitSelectionRequired("o PosExternalId informado nao existe nesta conta; a criacao automatica so e permitida quando a conta esta totalmente vazia");

        if (!storeWasExplicit && !posWasExplicit)
        {
            var candidates = CompatiblePairs(stores, points, cleanStoreName, cleanPosName);
            if (candidates.Count == 1)
            {
                var candidate = candidates[0];
                return Reuse(accountId, candidate.Store, candidate.PointOfSale);
            }
            if (candidates.Count > 1)
                throw ExplicitSelectionRequired(candidates.Count == 0
                    ? "nenhum par ativo de loja e PDV e compativel com os nomes informados"
                    : "mais de um par ativo de loja e PDV e compativel com os nomes informados");

            var storesByName = stores.Where(store => NamesEqual(store.Name, cleanStoreName)).ToList();
            if (storesByName.Count == 1 && !HasUsablePointForStore(points, storesByName[0]))
                return CreatePointOfSale(accountId, storesByName[0], cleanPosName);

            throw ExplicitSelectionRequired(storesByName.Count == 0
                ? "nenhum par ativo de loja e PDV e compativel com os nomes informados"
                : "a conta possui lojas ou PDVs que exigem selecao manual antes do reparo");
        }

        if (storeWasExplicit && posWasExplicit)
        {
            RequireActive(requestedPoint!);
            var pointStore = RequireAssociatedStore(stores, requestedPoint!);
            if (!requestedStore!.Id.Equals(pointStore.Id, StringComparison.Ordinal))
                throw new SecurityException("o PosExternalId informado nao pertence ao StoreExternalId informado nesta conta");
            return Reuse(accountId, requestedStore, requestedPoint!);
        }

        if (storeWasExplicit)
        {
            var candidates = points.Where(point => IsUsablePoint(point)
                    && point.StoreId.Equals(requestedStore!.Id, StringComparison.Ordinal)
                    && NamesEqual(point.Name, cleanPosName))
                .ToList();
            if (candidates.Count == 1)
                return Reuse(accountId, requestedStore!, candidates[0]);
            if (candidates.Count > 1)
                throw ExplicitSelectionRequired("a loja informada possui mais de um PDV ativo compativel com o nome do caixa");
            if (!HasUsablePointForStore(points, requestedStore!))
                return CreatePointOfSale(accountId, requestedStore!, cleanPosName);
            throw ExplicitSelectionRequired("a loja informada possui outro PDV ativo; selecione o external_id correto antes do reparo");
        }

        // Somente o PDV foi informado e sua existencia ja foi comprovada acima;
        // a associacao interna aponta inequivocamente para a Loja da mesma conta.
        RequireActive(requestedPoint!);
        return Reuse(accountId, RequireAssociatedStore(stores, requestedPoint!), requestedPoint!);
    }

    internal static void ValidateSingleMercadoPagoAccount(string? existingAccountId,
        string authenticatedAccountId)
    {
        var accountId = (authenticatedAccountId ?? "").Trim();
        if (accountId.Length is < 5 or > 24 || !accountId.All(char.IsAsciiDigit))
            throw new SecurityException("o Access Token nao retornou um User ID de conta valido");

        if (string.IsNullOrWhiteSpace(existingAccountId))
            return;

        existingAccountId = existingAccountId.Trim();
        if (existingAccountId.Length is < 5 or > 24 || !existingAccountId.All(char.IsAsciiDigit))
            throw new SecurityException("O cadastro Mercado Pago existente tem User ID invalido; nenhuma conta nova sera associada.");
        if (!existingAccountId.Equals(accountId, StringComparison.Ordinal))
            throw new SecurityException(
                "Esta maquina ja esta vinculada a uma conta Mercado Pago. A conta secundaria foi recusada; nenhuma configuracao, credencial ou recurso remoto foi alterado.");
    }

    internal static string? ReadExistingMercadoPagoAccountId(string bridgeDirectory)
    {
        var file = Path.Combine(bridgeDirectory, "owner-settings.json");
        if (!File.Exists(file)) return null;
        var info = new FileInfo(file);
        if (info.Length is <= 0 or > 65_536)
            throw new SecurityException(
                "O cadastro PIX existente tem tamanho invalido; nenhuma conta nova sera associada.");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new SecurityException(
                    "O cadastro PIX existente nao e um objeto JSON valido; nenhuma conta nova sera associada.");

            JsonElement schemaVersion = default, provider = default, account = default;
            var hasSchemaVersion = false;
            var hasProvider = false;
            var hasAccount = false;
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasSchemaVersion)
                        throw new SecurityException(
                            "O cadastro PIX existente e ambiguo; nenhuma conta nova sera associada.");
                    hasSchemaVersion = true;
                    schemaVersion = property.Value;
                }
                else if (property.Name.Equals("provider", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasProvider)
                        throw new SecurityException(
                            "O cadastro PIX existente e ambiguo; nenhuma conta nova sera associada.");
                    hasProvider = true;
                    provider = property.Value;
                }
                else if (property.Name.Equals("accountId", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasAccount)
                        throw new SecurityException(
                            "O cadastro PIX existente e ambiguo; nenhuma conta nova sera associada.");
                    hasAccount = true;
                    account = property.Value;
                }
            }

            if (!hasSchemaVersion || schemaVersion.ValueKind != JsonValueKind.Number
                || !schemaVersion.TryGetInt32(out var schema) || schema != 1)
                throw new SecurityException(
                    "A versao do cadastro PIX existente nao e suportada; nenhuma conta nova sera associada.");
            if (!hasProvider || provider.ValueKind != JsonValueKind.String)
                throw new SecurityException(
                    "O provedor do cadastro PIX existente esta ausente ou invalido; nenhuma conta nova sera associada.");
            var providerName = (provider.GetString() ?? "").Trim();
            if (!providerName.Equals("mercadopago", StringComparison.OrdinalIgnoreCase)
                && !providerName.Equals("online", StringComparison.OrdinalIgnoreCase))
                throw new SecurityException(
                    "O provedor do cadastro PIX existente e incompatível com o Mercado Pago; nenhuma conta nova sera associada.");
            if (!hasAccount || account.ValueKind != JsonValueKind.String)
                throw new SecurityException(
                    "O vinculo de conta do cadastro PIX existente esta ausente ou invalido; nenhuma conta nova sera associada.");

            var accountId = (account.GetString() ?? "").Trim();
            if (string.IsNullOrEmpty(accountId)) return null;
            if (accountId.Length is < 5 or > 24 || !accountId.All(char.IsAsciiDigit))
                throw new SecurityException(
                    "O cadastro Mercado Pago existente tem User ID invalido; nenhuma conta nova sera associada.");
            return accountId;
        }
        catch (JsonException ex)
        {
            throw new SecurityException(
                "O cadastro PIX existente nao pode ser lido com seguranca; nenhuma conta nova sera associada.", ex);
        }
    }

    private static bool LooksLikeMercadoPagoNumericId(string value)
    {
        var clean = (value ?? "").Trim();
        return clean.Length >= 8 && clean.Length <= 24 && clean.All(char.IsAsciiDigit);
    }

    internal static string ValidateMercadoPagoEnvironment(string? value)
    {
        var environment = (value ?? "").Trim().ToLowerInvariant();
        if (environment is not ("production" or "sandbox"))
            throw new InvalidOperationException("informe explicitamente environment=production ou environment=sandbox para o Mercado Pago");
        return environment;
    }

    private static MercadoPagoProvisioningDecision Reuse(string accountId, MercadoPagoStoreInfo store,
        MercadoPagoPosInfo point)
    {
        RequireActive(point);
        if (MercadoPagoOptions.IsLegacyTestExternalPosId(point.ExternalId))
            throw new SecurityException("o PDV legado LZPIXCOMP nao pode ser reutilizado");
        if (!PointBelongsToStore(point, store))
            throw new SecurityException("o PDV selecionado nao pertence a loja selecionada");
        if (string.IsNullOrWhiteSpace(store.ExternalId) || string.IsNullOrWhiteSpace(point.ExternalId))
            throw new SecurityException("a loja ou o PDV selecionado nao possui external_id valido");
        return new MercadoPagoProvisioningDecision(accountId, store.ExternalId, point.ExternalId,
            store, point, CreateStore: false, CreatePointOfSale: false);
    }

    private static IReadOnlyList<MercadoPagoProvisioningPair> CompatiblePairs(
        IReadOnlyList<MercadoPagoStoreInfo> stores, IReadOnlyList<MercadoPagoPosInfo> points,
        string storeName, string posName)
    {
        var result = new List<MercadoPagoProvisioningPair>();
        foreach (var point in points.Where(point => IsUsablePoint(point) && NamesEqual(point.Name, posName)))
        {
            var associated = stores.Where(store => PointBelongsToStore(point, store)
                    && NamesEqual(store.Name, storeName))
                .ToList();
            if (associated.Count > 1)
                throw new SecurityException("o inventario retornou mais de uma loja compativel com o mesmo PDV");
            if (associated.Count == 1) result.Add(new MercadoPagoProvisioningPair(associated[0], point));
        }
        return result;
    }

    private static MercadoPagoProvisioningDecision CreatePointOfSale(string accountId,
        MercadoPagoStoreInfo store, string posName)
    {
        if (string.IsNullOrWhiteSpace(store.Id) || string.IsNullOrWhiteSpace(store.ExternalId))
            throw new SecurityException("a loja selecionada nao possui identificadores validos");
        return new MercadoPagoProvisioningDecision(accountId, store.ExternalId,
            CreatePendingExternalId("LZPIX", 40), store, null,
            CreateStore: false, CreatePointOfSale: true,
            RequireEmptyInventoryBeforeCreation: false);
    }

    private static bool HasUsablePointForStore(IReadOnlyList<MercadoPagoPosInfo> points, MercadoPagoStoreInfo store)
        => points.Any(point => IsUsablePoint(point)
            && PointBelongsToStore(point, store));

    private static bool PointBelongsToStore(MercadoPagoPosInfo point, MercadoPagoStoreInfo store)
        => (!string.IsNullOrWhiteSpace(point.StoreId)
                && point.StoreId.Equals(store.Id, StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(point.ExternalStoreId)
                && point.ExternalStoreId.Equals(store.ExternalId, StringComparison.OrdinalIgnoreCase));

    private static bool IsUsablePoint(MercadoPagoPosInfo point)
        => IsActive(point)
            && !MercadoPagoOptions.IsLegacyTestExternalPosId(point.ExternalId)
            && !string.IsNullOrWhiteSpace(point.ExternalId);

    private static MercadoPagoStoreInfo? SingleStoreByExternalId(IReadOnlyList<MercadoPagoStoreInfo> stores,
        string externalId)
    {
        var matches = stores.Where(store => store.ExternalId.Equals(externalId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 1)
            throw new SecurityException("mais de uma loja usa o StoreExternalId informado nesta conta");
        return matches.Count == 1 ? matches[0] : null;
    }

    private static MercadoPagoPosInfo? SinglePointByExternalId(IReadOnlyList<MercadoPagoPosInfo> points,
        string externalId)
    {
        var matches = points.Where(point => point.ExternalId.Equals(externalId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 1)
            throw new SecurityException("mais de um PDV usa o PosExternalId informado nesta conta");
        return matches.Count == 1 ? matches[0] : null;
    }

    private static MercadoPagoStoreInfo RequireAssociatedStore(IReadOnlyList<MercadoPagoStoreInfo> stores,
        MercadoPagoPosInfo point)
    {
        var matches = stores.Where(store => PointBelongsToStore(point, store)).ToList();
        if (matches.Count != 1)
            throw new SecurityException(matches.Count == 0
                ? "o PDV informado nao esta associado a uma loja visivel desta conta"
                : "o PDV informado possui uma associacao de loja ambigua");
        return matches[0];
    }

    private static bool NamesEqual(string? left, string? right)
        => string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsActive(MercadoPagoPosInfo point)
        // A busca de PDVs do Mercado Pago omite `status` em algumas contas.
        // O proprio health-check ja considera essa resposta ativa; use a
        // mesma regra aqui para nao recusar um PDV valido durante o reuso.
        => string.IsNullOrWhiteSpace(point.Status)
            || point.Status.Equals("active", StringComparison.OrdinalIgnoreCase);

    private static void RequireActive(MercadoPagoPosInfo point)
    {
        if (!IsActive(point))
            throw new InvalidOperationException("o PDV selecionado nao esta ativo no Mercado Pago");
    }

    private static InvalidOperationException ExplicitSelectionRequired(string reason)
        => new($"{reason}. Informe StoreExternalId e PosExternalId explicitamente; nenhum cadastro local foi alterado e nenhum recurso foi criado.");

    private static async Task<PixOwnerProvisioningResult> ConfigureAdapterAsync(PixOwnerProvisioningRequest request,
        string credential, PixOptions baseOptions, PixPaths paths, PixSecretStore secrets, CancellationToken token,
        PixOwnerControlSnapshot? ownerControlSnapshot)
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
        var pendingOwner = new PixOwnerSettings
        {
            Enabled = true,
            SetupState = "pending",
            Provider = "adapter",
            AdapterBaseUrl = adapter.BaseUrl,
            AdapterProviderId = adapter.ProviderId,
            OnlineLicensingEnabled = baseOptions.OnlineLicensingEnabled,
            OnlineBaseUrl = baseOptions.Online.BaseUrl,
            OnlineLicenseId = baseOptions.Online.LicenseId,
            OnlineProtectionProfile = baseOptions.Online.ProtectionProfile,
            PackagePricesCents = new Dictionary<int, long>(request.PackagePricesCents)
        };
        if (ownerControlSnapshot is not null)
            pendingOwner = ownerControlSnapshot.Preserve(pendingOwner);
        pendingOwner.Validate();
        SaveOwnerSettings(paths, pendingOwner);
        OwnerSetupStatus.Publish(paths, "pending",
            $"Adaptador {adapter.ProviderId} selecionado. Validando a nova credencial antes de liberar cobrancas.");
        secrets.Save(credential);
        await bank.CheckHealthAsync(token);
        var confirmedOwner = pendingOwner with { SetupState = "ready" };
        confirmedOwner.Validate();
        SaveOwnerSettings(paths, confirmedOwner);
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

    internal static string SafeSetupMessage(string message)
    {
        var clean = (message ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        for (var tokenStart = clean.IndexOf("APP_USR-", StringComparison.OrdinalIgnoreCase);
             tokenStart >= 0;
             tokenStart = clean.IndexOf("APP_USR-", tokenStart + "[Access Token oculto]".Length,
                 StringComparison.OrdinalIgnoreCase))
        {
            var tokenEnd = tokenStart;
            while (tokenEnd < clean.Length
                && (char.IsLetterOrDigit(clean[tokenEnd]) || clean[tokenEnd] is '-' or '_')) tokenEnd++;
            clean = clean[..tokenStart] + "[Access Token oculto]" + clean[tokenEnd..];
        }
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

    // Um PDV pode ser removido, pertencer a outra conta depois da troca de
    // token ou ainda nao estar visivel no endpoint filtrado. Antes desta
    // transicao, o health-check deixava Ready=true para sempre e repetia 404
    // sem voltar ao inventario/fluxo idempotente de criacao. Somente a falta
    // confirmada do PDV invalida a infraestrutura; falhas de rede continuam
    // usando o backoff normal sem criar recursos.
    internal bool InvalidateAfterHealthFailure(Exception error)
    {
        if (!RequiresInfrastructureReconciliation(error)) return false;
        Ready = false;
        _nextAttempt = DateTimeOffset.MinValue;
        const string message = "O caixa PIX configurado nao foi encontrado. Reconciliando Loja/PDV da conta antes de liberar novas cobrancas.";
        OwnerSetupStatus.Publish(_paths, "pending", message);
        return true;
    }

    internal static bool RequiresInfrastructureReconciliation(Exception error)
        => error is MercadoPagoApiException mercadoPago
            && mercadoPago.StatusCode == 404
            && (mercadoPago.Detail.Contains("PDV", StringComparison.OrdinalIgnoreCase)
                || mercadoPago.Detail.Contains("point of sale", StringComparison.OrdinalIgnoreCase));

    public async Task<bool> TryEnsureAsync(bool force, CancellationToken token)
    {
        if (Ready && !force) return true;
        if (!force && DateTimeOffset.UtcNow < _nextAttempt) return false;
        _nextAttempt = DateTimeOffset.UtcNow.AddSeconds(15);
        var locationWasRequired = false;
        try
        {
            OwnerSetupStatus.Publish(_paths, "configuring", "Conferindo loja e caixa PIX. Se estiver sem internet, o sistema tentara novamente automaticamente.");

            // O ambiente e a conta devem vir do proprio Access Token. Sem uma
            // resposta confiavel de /users/me, o daemon permanece bloqueado;
            // um User ID antigo nunca substitui essa comprovacao.
            var inventory = await _provider.GetInfrastructureAsync(token);
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

            if (inventory.Stores.Count > 0 || inventory.PointsOfSale.Count > 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(selectionError)
                    ? "Os IDs configurados nao correspondem a um par ativo desta conta. A criacao automatica so e permitida quando a conta esta totalmente vazia; nenhum recurso foi criado."
                    : selectionError);

            locationWasRequired = true;
            var setup = await effectiveSettings.BuildSetupRequestAsync(_paths, token);
            var result = await _provider.EnsureInfrastructureAsync(setup, token,
                new MercadoPagoCreationPolicy(AllowStoreCreation: true, AllowPointOfSaleCreation: true,
                    RequireEmptyInventoryBeforeCreation: true));
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
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
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

        var usablePairs = new List<MercadoPagoProvisioningPair>();
        foreach (var item in inventory.PointsOfSale.Where(item => !string.IsNullOrWhiteSpace(item.Id)
                     && !string.IsNullOrWhiteSpace(item.ExternalId)
                     && !MercadoPagoOptions.IsLegacyTestExternalPosId(item.ExternalId)
                     && (string.IsNullOrWhiteSpace(item.Status)
                         || item.Status.Equals("active", StringComparison.OrdinalIgnoreCase))))
        {
            var matches = MatchingStores(inventory.Stores, item);
            if (matches.Count == 1)
                usablePairs.Add(new MercadoPagoProvisioningPair(matches[0], item));
            else if (item.ExternalId.Equals(settings.PosExternalId.Trim(), StringComparison.OrdinalIgnoreCase))
                selectionError = matches.Count == 0
                    ? "O caixa PIX informado nao esta associado a uma loja visivel desta conta."
                    : "O caixa PIX informado possui uma associacao de loja ambigua.";
        }

        var requestedStore = inventory.Stores.FirstOrDefault(item =>
            item.ExternalId.Equals(settings.StoreExternalId.Trim(), StringComparison.OrdinalIgnoreCase));
        var requestedPoint = usablePairs.FirstOrDefault(item =>
            item.PointOfSale.ExternalId.Equals(settings.PosExternalId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (requestedPoint is not null)
        {
            if (requestedStore is not null && !requestedStore.Id.Equals(requestedPoint.Store.Id, StringComparison.Ordinal))
                throw new SecurityException("O caixa PIX informado pertence a outra loja.");
            store = requestedPoint.Store;
            pointOfSale = requestedPoint.PointOfSale;
            automaticallyRecovered = requestedStore is null;
            return true;
        }

        var candidates = requestedStore is null
            ? usablePairs
            : usablePairs.Where(item => item.Store.Id.Equals(requestedStore.Id, StringComparison.Ordinal)).ToList();
        if (candidates.Count > 1)
        {
            var nameMatches = candidates.Where(item =>
                item.PointOfSale.Name.Equals(settings.PosName.Trim(), StringComparison.OrdinalIgnoreCase)
                && item.Store.Name.Equals(settings.StoreName.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (nameMatches.Count == 1) candidates = nameMatches;
        }

        if (candidates.Count == 1)
        {
            store = candidates[0].Store;
            pointOfSale = candidates[0].PointOfSale;
            automaticallyRecovered = true;
            return true;
        }

        if (candidates.Count > 1)
        {
            var ids = string.Join(", ", candidates.Select(item => item.PointOfSale.ExternalId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(6));
            selectionError = $"Mais de um caixa PIX ativo foi encontrado ({ids}). Selecione o external_id correto na CONFIGURACAO PIX DO PROPRIETARIO; nenhuma cobranca foi criada.";
        }
        return false;
    }

    private static List<MercadoPagoStoreInfo> MatchingStores(IReadOnlyList<MercadoPagoStoreInfo> stores,
        MercadoPagoPosInfo point)
    {
        var matches = new List<MercadoPagoStoreInfo>();
        foreach (var store in stores)
        {
            var sameInternalStore = !string.IsNullOrWhiteSpace(point.StoreId)
                && point.StoreId.Equals(store.Id, StringComparison.Ordinal);
            var sameExternalStore = !string.IsNullOrWhiteSpace(point.ExternalStoreId)
                && point.ExternalStoreId.Equals(store.ExternalId, StringComparison.OrdinalIgnoreCase);
            if ((sameInternalStore || sameExternalStore)
                && !matches.Any(item => item.Id.Equals(store.Id, StringComparison.Ordinal)))
                matches.Add(store);
        }
        return matches;
    }

    internal static PixOwnerSettings BindAuthenticatedAccount(PixOwnerSettings settings, MercadoPagoInfrastructure inventory)
    {
        var accountId = inventory.AccountId.Trim();
        if (accountId.Length is < 5 or > 24 || !accountId.All(char.IsAsciiDigit))
            throw new SecurityException("O Mercado Pago nao retornou um User ID valido para o Access Token.");
        var existingAccountId = (settings.AccountId ?? "").Trim();
        if (string.IsNullOrEmpty(existingAccountId))
            return settings with { AccountId = accountId };
        if (existingAccountId.Length is < 5 or > 24 || !existingAccountId.All(char.IsAsciiDigit))
            throw new SecurityException("O cadastro PIX existente tem User ID invalido. Compras permanecem bloqueadas.");
        if (!existingAccountId.Equals(accountId, StringComparison.Ordinal))
            throw new SecurityException("A credencial protegida nao pertence a conta Mercado Pago ja vinculada a esta maquina. Compras permanecem bloqueadas.");
        return settings;
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
sealed record MercadoPagoPosInfo(string Id, string ExternalId, string Name, string StoreId, string ExternalStoreId,
    string Status);
sealed record MercadoPagoInfrastructure(string AccountId, IReadOnlyList<MercadoPagoStoreInfo> Stores, IReadOnlyList<MercadoPagoPosInfo> PointsOfSale);
sealed record MercadoPagoProvisioningPair(MercadoPagoStoreInfo Store, MercadoPagoPosInfo PointOfSale);
sealed record MercadoPagoProvisioningDecision(string AccountId, string StoreExternalId, string PosExternalId,
    MercadoPagoStoreInfo? Store, MercadoPagoPosInfo? PointOfSale, bool CreateStore, bool CreatePointOfSale,
    bool RequireEmptyInventoryBeforeCreation = false)
{
    public bool RequiresRemoteWrite => CreateStore || CreatePointOfSale;
}
sealed record MercadoPagoCreationPolicy(bool AllowStoreCreation, bool AllowPointOfSaleCreation,
    bool RequireEmptyInventoryBeforeCreation = false);
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
        Reconciliation = Path.Combine(root, "reconciliation");
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
        LicenseFile = Path.Combine(root, "turborama-pix.license");
        PublicOptionsFile = Path.Combine(root, "public-options.json");
        AgentStatusFile = Path.Combine(root, "agent-status.json");
        StartupErrorFile = Path.Combine(root, "agent-startup-error.json");
        AgentStopRequestFile = Path.Combine(root, "agent-stop.request");
    }

    public string Root { get; }
    public string Requests { get; }
    public string Sessions { get; }
    public string Approved { get; }
    public string Processed { get; }
    public string Rejected { get; }
    public string Reconciliation { get; }
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
    public string LicenseFile { get; }
    public string PublicOptionsFile { get; }
    public string AgentStatusFile { get; }
    public string StartupErrorFile { get; }
    public string AgentStopRequestFile { get; }
    public string RequestFile(string id) => Path.Combine(Requests, $"{id}.request.json");
    public string SessionFile(string id) => Path.Combine(Sessions, $"{id}.session.json");
    public string ApprovedFile(string id) => Path.Combine(Approved, $"{id}.credit.json");
    public string ProcessedFile(string id) => Path.Combine(Processed, $"{id}.credit.json");
    public string QrFile(string id) => Path.Combine(Qr, $"{id}.png");
    public string QrMatrixFile(string id) => Path.Combine(Qr, $"{id}.matrix");
    public string RetryFile(string id) => Path.Combine(Retry, $"{id}.retry.json");

    public void EnsureDirectories()
    {
        foreach (var directory in new[] { Root, Requests, Sessions, Approved, Processed, Rejected, Reconciliation, Retry, Qr, Logs }) Directory.CreateDirectory(directory);
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

enum PixStopRequestDisposition { Authorized, InstallerUpdate, Mismatch, Invalid }

static class PixAgentControl
{
    private const int MaxStopRequestBytes = 16 * 1024;
    private static readonly HashSet<string> StopRequestFields = new(StringComparer.Ordinal)
    {
        "schemaVersion", "mode", "processId", "processStartFileTimeUtc", "managerTokenHash"
    };

    public static bool TryConsumeStopRequest(PixPaths paths, PixDaemonDescriptor identity)
    {
        if (!File.Exists(paths.AgentStopRequestFile)) return false;
        string payload;
        try
        {
            var info = new FileInfo(paths.AgentStopRequestFile);
            if (!info.Exists) return false;
            if ((info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
                || info.Length is <= 0 or > MaxStopRequestBytes)
            {
                paths.Quarantine(paths.AgentStopRequestFile, "sentinel de parada PIX invalido");
                return false;
            }
            payload = File.ReadAllText(paths.AgentStopRequestFile, new UTF8Encoding(false, true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or DecoderFallbackException)
        {
            Console.Error.WriteLine($"Nao foi possivel ler agent-stop.request: {ex.Message}");
            return false;
        }

        var disposition = ClassifyStopRequest(payload, identity);
        if (disposition is PixStopRequestDisposition.Mismatch or PixStopRequestDisposition.Invalid)
        {
            paths.Quarantine(paths.AgentStopRequestFile,
                disposition == PixStopRequestDisposition.Mismatch
                    ? "sentinel dirigido a outra instancia do daemon PIX"
                    : "contrato do sentinel de parada PIX invalido");
            return false;
        }
        try
        {
            File.Delete(paths.AgentStopRequestFile);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Console.Error.WriteLine($"Nao foi possivel consumir agent-stop.request: {ex.Message}");
            return false;
        }
    }

    internal static PixStopRequestDisposition ClassifyStopRequest(string payload,
        PixDaemonDescriptor identity)
    {
        if (payload.Length == 0 || Encoding.UTF8.GetByteCount(payload) > MaxStopRequestBytes)
            return PixStopRequestDisposition.Invalid;
        // O instalador comercial usa deliberadamente esta mensagem enquanto
        // substitui os binarios e nao possui o token efemero do frontend.
        if (payload.Trim().Equals("installer-update", StringComparison.Ordinal))
            return PixStopRequestDisposition.InstallerUpdate;
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return PixStopRequestDisposition.Invalid;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!StopRequestFields.Contains(property.Name) || !seen.Add(property.Name))
                    return PixStopRequestDisposition.Invalid;
            }
            if (seen.Count != StopRequestFields.Count)
                return PixStopRequestDisposition.Invalid;
            var root = document.RootElement;
            if (!root.GetProperty("schemaVersion").TryGetInt32(out var schemaVersion) || schemaVersion != 1
                || root.GetProperty("mode").ValueKind != JsonValueKind.String
                || !"daemon".Equals(root.GetProperty("mode").GetString(), StringComparison.Ordinal)
                || !root.GetProperty("processId").TryGetInt32(out var processId) || processId <= 0
                || !root.GetProperty("processStartFileTimeUtc").TryGetUInt64(out var processStart) || processStart == 0
                || root.GetProperty("managerTokenHash").ValueKind != JsonValueKind.String)
                return PixStopRequestDisposition.Invalid;
            var tokenHash = root.GetProperty("managerTokenHash").GetString() ?? "";
            if (!PixDaemonIdentity.IsManagerToken(tokenHash)
                || !tokenHash.Equals(tokenHash.ToLowerInvariant(), StringComparison.Ordinal))
                return PixStopRequestDisposition.Invalid;
            return processId == identity.ProcessId
                && processStart == identity.ProcessStartFileTimeUtc
                && tokenHash.Equals(identity.ManagerTokenHash, StringComparison.Ordinal)
                    ? PixStopRequestDisposition.Authorized
                    : PixStopRequestDisposition.Mismatch;
        }
        catch (JsonException)
        {
            return PixStopRequestDisposition.Invalid;
        }
    }
}

// Observa o sentinel em uma tarefa independente para que uma chamada HTTP ou
// reconciliacao em andamento receba cancelamento sem esperar o proximo ciclo.
sealed class PixAgentStopMonitor : IAsyncDisposable
{
    private static readonly TimeSpan ProductionPollInterval = TimeSpan.FromMilliseconds(250);
    private readonly PixPaths _paths;
    private readonly PixDaemonDescriptor _identity;
    private readonly CancellationTokenSource _agentCancellation;
    private readonly Action _publishStopping;
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _monitorCancellation = new();
    private readonly Task _watchTask;
    private int _stopPublished;

    private PixAgentStopMonitor(PixPaths paths, PixDaemonDescriptor identity,
        CancellationTokenSource agentCancellation,
        Action publishStopping, TimeSpan pollInterval)
    {
        _paths = paths;
        _identity = identity;
        _agentCancellation = agentCancellation;
        _publishStopping = publishStopping;
        _pollInterval = pollInterval;
        _watchTask = Task.Run(WatchAsync);
    }

    public static PixAgentStopMonitor Start(PixPaths paths, PixDaemonDescriptor identity,
        CancellationTokenSource agentCancellation,
        Action publishStopping, TimeSpan? pollInterval = null) =>
        new(paths, identity, agentCancellation, publishStopping, pollInterval ?? ProductionPollInterval);

    private async Task WatchAsync()
    {
        try
        {
            while (!_monitorCancellation.IsCancellationRequested)
            {
                if (PixAgentControl.TryConsumeStopRequest(_paths, _identity))
                {
                    SignalStopOnce();
                    return;
                }
                await Task.Delay(_pollInterval, _monitorCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_monitorCancellation.IsCancellationRequested) { }
    }

    private void SignalStopOnce()
    {
        if (Interlocked.Exchange(ref _stopPublished, 1) != 0) return;
        // Cancelar vem antes de qualquer escrita de status: callbacks do token
        // interrompem imediatamente HttpClient e as reconciliacoes em curso.
        _agentCancellation.Cancel();
        try { _publishStopping(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Console.Error.WriteLine($"Nao foi possivel publicar stopping: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _monitorCancellation.Cancel();
        await _watchTask.ConfigureAwait(false);
        _monitorCancellation.Dispose();
    }
}

// Mantem o watchdog informado mesmo enquanto uma chamada bancaria ou a
// conciliacao de um lote esta em andamento. O timer cobre uma unica operacao
// lenta; PulseSafely renova o status entre os itens do lote.
sealed class PixAgentHeartbeat : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private readonly object _gate = new();
    private readonly PixOptions _options;
    private readonly PixPaths _paths;
    private readonly PixDaemonDescriptor _identity;
    private readonly string _provider;
    private readonly Func<CommercialLicenseValidationResult>? _commercialLicenseValidation;
    private readonly Timer _timer;
    private bool _credentialAvailable;
    private bool _providerHealthy;
    private string _state;
    private bool _disposed;
    private string _lastError = "";

    public PixAgentHeartbeat(PixOptions options, PixPaths paths, string provider,
        bool credentialAvailable, bool providerHealthy, string state, PixDaemonDescriptor identity,
        Func<CommercialLicenseValidationResult>? commercialLicenseValidation = null)
    {
        (_options, _paths, _provider) = (options, paths, provider);
        _identity = identity;
        _commercialLicenseValidation = commercialLicenseValidation;
        (_credentialAvailable, _providerHealthy, _state) =
            (credentialAvailable, providerHealthy, state);
        PublishLocked();
        _timer = new Timer(_ => PulseSafely(), null, Interval, Interval);
    }

    public void Update(bool credentialAvailable, bool providerHealthy, string state)
    {
        lock (_gate)
        {
            if (_disposed) return;
            (_credentialAvailable, _providerHealthy, _state) =
                (credentialAvailable, providerHealthy, state);
            PublishLocked();
        }
    }

    public void PulseSafely()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed) return;
                PublishLocked();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var message = ex.Message;
            lock (_gate)
            {
                if (message.Equals(_lastError, StringComparison.Ordinal)) return;
                _lastError = message;
            }
            Console.Error.WriteLine($"Nao foi possivel renovar o heartbeat PIX: {message}");
        }
    }

    private void PublishLocked()
    {
        var license = _commercialLicenseValidation?.Invoke();
        var commerciallyAuthorized = license?.IsValid ?? true;
        PixPublicContract.Publish(_options, _paths, _provider,
            _credentialAvailable, _providerHealthy,
            commerciallyAuthorized ? _state : "license_required",
            _identity, commerciallyAuthorized);
        _lastError = "";
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
        }
    }
}

static class PixPublicContract
{
    public static void Publish(PixOptions options, PixPaths paths, string provider,
        bool credentialAvailable, bool providerHealthy, string state, PixDaemonDescriptor identity,
        bool commerciallyAuthorized = true)
    {
        var ready = commerciallyAuthorized
            && (provider == "mock" || (credentialAvailable && providerHealthy && options.IsProviderConfigured()));
        var now = DateTimeOffset.UtcNow;
        var packages = options.AllowedMinutes
            .Select(minutes => new { minutes, amountCents = options.PriceFor(minutes) })
            .Where(package => package.amountCents > 0)
            .ToArray();
        paths.WriteAtomically(paths.PublicOptionsFile, new
        {
            schemaVersion = 1,
            provider,
            productionEnabled = options.ProductionEnabled
                && (provider != "mercadopago" || options.MercadoPago.Environment == "production")
                && commerciallyAuthorized,
            ready,
            paymentExpirationMinutes = options.PaymentExpirationMinutes,
            generatedAtUnixSeconds = now.ToUnixTimeSeconds(),
            packages
        });
        paths.WriteAtomically(paths.AgentStatusFile, new
        {
            schemaVersion = 2,
            mode = "daemon",
            processId = identity.ProcessId,
            processStartFileTimeUtc = identity.ProcessStartFileTimeUtc,
            managerTokenHash = identity.ManagerTokenHash,
            provider,
            ready,
            state,
            updatedAtUnixSeconds = now.ToUnixTimeSeconds()
        });
    }
}

static class PixStartupErrorContract
{
    public static void Publish(PixPaths? paths, PixDaemonDescriptor? identity, int exitCode, string message)
    {
        if (paths is null) return;
        try
        {
            var safeMessage = (message ?? "").Trim();
            if (safeMessage.Length > 1024)
                safeMessage = safeMessage[..1024];
            paths.WriteAtomically(paths.StartupErrorFile, new
            {
                schemaVersion = 1,
                mode = "daemon",
                processId = identity?.ProcessId ?? Environment.ProcessId,
                processStartFileTimeUtc = identity?.ProcessStartFileTimeUtc ?? 0UL,
                exitCode,
                message = safeMessage,
                updatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or JsonException or InvalidOperationException or NotSupportedException)
        {
            // O erro principal continua sendo escrito no stderr. Esta publicacao e apenas
            // para a interface exibir a causa real quando o daemon fecha antes do status.
        }
    }
}

enum PixSecretState { Available, Missing, Unreadable }

sealed record PixSecretReadResult(PixSecretState State, string? Value)
{
    public bool IsAvailable => State == PixSecretState.Available && !string.IsNullOrWhiteSpace(Value);
}

sealed class PixSecretStore
{
    private const string HardwareSealedSecretPrefix = "TRPXSECRET3:";
    private const string LegacyBoundSecretPrefix = "TRPXSECRET2:";
    private const string LegacyEntropyLabel = "TurboRamaPixAgent-v1";
    private const string LegacyBoundEntropyLabel = "TurboRamaPixAgent-v2|";
    private const string HardwareSealedEntropyLabel = "TurboRamaPixAgent-v3|";
    private const int DataKeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int MaximumEnvelopeBytes = 32 * 1024;
    private readonly string _path;
    private readonly bool _requireTpmMachineBinding;
    private readonly IPixMachineBinding _machineBinding;
    private readonly string? _transientSecret;

    public PixSecretStore(string path)
        : this(path, requireTpmMachineBinding: false, new TpmCngMachineBinding(), transientSecret: null) { }

    internal PixSecretStore(string path, bool requireTpmMachineBinding, IPixMachineBinding machineBinding)
        : this(path, requireTpmMachineBinding, machineBinding, transientSecret: null) { }

    private PixSecretStore(string path, bool requireTpmMachineBinding, IPixMachineBinding machineBinding,
        string? transientSecret)
    {
        _path = path;
        _requireTpmMachineBinding = requireTpmMachineBinding;
        _machineBinding = machineBinding ?? throw new ArgumentNullException(nameof(machineBinding));
        _transientSecret = transientSecret;
    }

    public PixSecretStore WithTransientSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new SecurityException("A credencial PIX transitoria esta vazia.");
        return new PixSecretStore(_path, _requireTpmMachineBinding, _machineBinding, secret);
    }

    public void Save(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new SecurityException("A credencial PIX esta vazia.");

        var plaintext = Encoding.UTF8.GetBytes(secret);
        byte[] entropy = Array.Empty<byte>();
        byte[] dpapiProtected = Array.Empty<byte>();
        byte[] dataKey = Array.Empty<byte>();
        byte[] wrappedKey = Array.Empty<byte>();
        byte[] nonce = Array.Empty<byte>();
        byte[] tag = Array.Empty<byte>();
        byte[] ciphertext = Array.Empty<byte>();
        byte[] payload = Array.Empty<byte>();
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string serialized;
            if (_requireTpmMachineBinding)
            {
                if (_machineBinding is not IPixMachineSecretBinding secretBinding)
                    throw new SecurityException("o cofre comercial exige um TPM capaz de selar a chave privada");

                dataKey = RandomNumberGenerator.GetBytes(DataKeyBytes);
                var wrapped = secretBinding.WrapKey(dataKey);
                var fingerprint = TpmCngMachineBinding.NormalizeFingerprint(wrapped.Fingerprint);
                wrappedKey = wrapped.WrappedKey
                    ?? throw new SecurityException("o TPM nao retornou a chave protegida do cofre PIX");
                if (wrappedKey.Length is < 128 or > 1024)
                    throw new SecurityException("o TPM retornou uma chave protegida de tamanho invalido");

                entropy = HardwareSealedEntropy(fingerprint);
                dpapiProtected = WindowsDpapi.Protect(plaintext, entropy);
                nonce = RandomNumberGenerator.GetBytes(NonceBytes);
                tag = new byte[TagBytes];
                ciphertext = new byte[dpapiProtected.Length];
                using (var aes = new AesGcm(dataKey, TagBytes))
                    aes.Encrypt(nonce, dpapiProtected, ciphertext, tag, entropy);

                serialized = HardwareSealedSecretPrefix + fingerprint + ":"
                    + Convert.ToBase64String(wrappedKey) + ":"
                    + Convert.ToBase64String(nonce) + ":"
                    + Convert.ToBase64String(tag) + ":"
                    + Convert.ToBase64String(ciphertext);
            }
            else
            {
                entropy = Encoding.UTF8.GetBytes(LegacyEntropyLabel);
                dpapiProtected = WindowsDpapi.Protect(plaintext, entropy);
                serialized = Convert.ToBase64String(dpapiProtected);
            }
            if (Encoding.UTF8.GetByteCount(serialized) > MaximumEnvelopeBytes)
                throw new SecurityException("o envelope do cofre PIX excedeu o limite permitido");
            payload = Encoding.UTF8.GetBytes(serialized);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(payload);
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
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(entropy);
            if (dpapiProtected.Length != 0) CryptographicOperations.ZeroMemory(dpapiProtected);
            if (dataKey.Length != 0) CryptographicOperations.ZeroMemory(dataKey);
            if (wrappedKey.Length != 0) CryptographicOperations.ZeroMemory(wrappedKey);
            if (nonce.Length != 0) CryptographicOperations.ZeroMemory(nonce);
            if (tag.Length != 0) CryptographicOperations.ZeroMemory(tag);
            if (ciphertext.Length != 0) CryptographicOperations.ZeroMemory(ciphertext);
            if (payload.Length != 0) CryptographicOperations.ZeroMemory(payload);
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }

    public PixSecretReadResult TryLoad()
    {
        if (!string.IsNullOrWhiteSpace(_transientSecret))
            return new(PixSecretState.Available, _transientSecret);
        if (!File.Exists(_path)) return new(PixSecretState.Missing, null);
        try
        {
            var info = new FileInfo(_path);
            if (info.Length is <= 0 or > MaximumEnvelopeBytes)
                throw new FormatException("o tamanho do cofre PIX e invalido");
            var serialized = File.ReadAllText(_path, Encoding.UTF8).Trim();
            byte[] entropy;
            byte[] encrypted;
            if (serialized.StartsWith(HardwareSealedSecretPrefix, StringComparison.Ordinal))
            {
                var fields = serialized.Split(':');
                if (fields.Length != 6 || !fields[0].Equals("TRPXSECRET3", StringComparison.Ordinal))
                    throw new FormatException("envelope TPM v3 do cofre PIX invalido");
                var fingerprint = TpmCngMachineBinding.NormalizeFingerprint(fields[1]);
                var wrappedKey = Convert.FromBase64String(fields[2]);
                var nonce = Convert.FromBase64String(fields[3]);
                var tag = Convert.FromBase64String(fields[4]);
                var ciphertext = Convert.FromBase64String(fields[5]);
                byte[] dataKey = Array.Empty<byte>();
                byte[] dpapiProtected = Array.Empty<byte>();
                entropy = HardwareSealedEntropy(fingerprint);
                try
                {
                    if (_machineBinding is not IPixMachineSecretBinding secretBinding)
                        throw new SecurityException("o TPM deste processo nao pode abrir o cofre comercial");
                    if (wrappedKey.Length is < 128 or > 1024 || nonce.Length != NonceBytes
                        || tag.Length != TagBytes || ciphertext.Length is <= 0 or > MaximumEnvelopeBytes)
                        throw new FormatException("parametros criptograficos do cofre PIX sao invalidos");
                    dataKey = secretBinding.UnwrapKey(fingerprint, wrappedKey);
                    if (dataKey.Length != DataKeyBytes)
                        throw new SecurityException("o TPM abriu uma chave de dados com tamanho invalido");
                    dpapiProtected = new byte[ciphertext.Length];
                    using (var aes = new AesGcm(dataKey, TagBytes))
                        aes.Decrypt(nonce, ciphertext, tag, dpapiProtected, entropy);
                    encrypted = dpapiProtected.ToArray();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(wrappedKey);
                    CryptographicOperations.ZeroMemory(nonce);
                    CryptographicOperations.ZeroMemory(tag);
                    CryptographicOperations.ZeroMemory(ciphertext);
                    if (dataKey.Length != 0) CryptographicOperations.ZeroMemory(dataKey);
                    if (dpapiProtected.Length != 0) CryptographicOperations.ZeroMemory(dpapiProtected);
                }
            }
            else if (serialized.StartsWith(LegacyBoundSecretPrefix, StringComparison.Ordinal))
            {
                if (_requireTpmMachineBinding)
                    throw new SecurityException("a credencial TPM v2 precisa ser recadastrada no cofre v3 selado");
                var separator = serialized.IndexOf(':', LegacyBoundSecretPrefix.Length);
                if (separator <= LegacyBoundSecretPrefix.Length || separator == serialized.Length - 1)
                    throw new FormatException("envelope TPM do cofre PIX invalido");
                var fingerprint = TpmCngMachineBinding.NormalizeFingerprint(
                    serialized[LegacyBoundSecretPrefix.Length..separator]);
                _machineBinding.VerifyFingerprint(fingerprint);
                entropy = LegacyBoundEntropy(fingerprint);
                encrypted = Convert.FromBase64String(serialized[(separator + 1)..]);
            }
            else
            {
                if (_requireTpmMachineBinding)
                    throw new SecurityException("a credencial existente ainda nao foi vinculada ao TPM deste quiosque");
                entropy = Encoding.UTF8.GetBytes(LegacyEntropyLabel);
                encrypted = Convert.FromBase64String(serialized);
            }

            byte[] plaintext = Array.Empty<byte>();
            try
            {
                plaintext = WindowsDpapi.Unprotect(encrypted, entropy);
                var value = Encoding.UTF8.GetString(plaintext);
                return string.IsNullOrWhiteSpace(value)
                    ? new(PixSecretState.Unreadable, null)
                    : new(PixSecretState.Available, value);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(entropy);
                CryptographicOperations.ZeroMemory(encrypted);
                if (plaintext.Length != 0) CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (CryptographicException) { return new(PixSecretState.Unreadable, null); }
        catch (SecurityException) { return new(PixSecretState.Unreadable, null); }
        catch (FormatException) { return new(PixSecretState.Unreadable, null); }
        catch (IOException) { return new(PixSecretState.Unreadable, null); }
        catch (UnauthorizedAccessException) { return new(PixSecretState.Unreadable, null); }
    }

    public string? Load() => TryLoad().Value;

    internal static bool IsBoundEnvelope(string value)
        => !string.IsNullOrWhiteSpace(value)
            && (value.Trim().StartsWith(HardwareSealedSecretPrefix, StringComparison.Ordinal)
                || value.Trim().StartsWith(LegacyBoundSecretPrefix, StringComparison.Ordinal));

    internal static bool IsHardwareSealedEnvelope(string value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Trim().StartsWith(HardwareSealedSecretPrefix, StringComparison.Ordinal);

    private static byte[] LegacyBoundEntropy(string fingerprint)
        => SHA256.HashData(Encoding.UTF8.GetBytes(LegacyBoundEntropyLabel + fingerprint));

    private static byte[] HardwareSealedEntropy(string fingerprint)
        => SHA256.HashData(Encoding.UTF8.GetBytes(HardwareSealedEntropyLabel + fingerprint));
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

sealed class CommercialLicenseUnavailableException : Exception
{
    public CommercialLicenseUnavailableException(string message) : base(message) { }
}

sealed class PixEngine
{
    private readonly PixOptions _options;
    private readonly PixPaths _paths;
    private readonly IPixProvider _provider;
    private readonly PixSigningKeyStore _signingKeys;
    private readonly Func<CommercialLicenseValidationResult>? _commercialLicenseValidation;

    public PixEngine(PixOptions options, PixPaths paths, IPixProvider provider,
        PixSigningKeyStore signingKeys,
        Func<CommercialLicenseValidationResult>? commercialLicenseValidation = null)
    {
        _options = options;
        _paths = paths;
        _provider = provider;
        _signingKeys = signingKeys;
        _commercialLicenseValidation = commercialLicenseValidation;
    }

    public async Task RunOnceAsync(CancellationToken token, Action? heartbeat = null)
    {
        ReportProgress(heartbeat);
        foreach (var requestFile in Directory.EnumerateFiles(_paths.Requests, "*.request.json").OrderBy(Path.GetFileName))
        {
            ReportProgress(heartbeat);
            var requestId = Path.GetFileName(requestFile).Replace(".request.json", "", StringComparison.OrdinalIgnoreCase);
            try
            {
                if (!PixId.IsValid(requestId)) { _paths.Quarantine(requestFile, "Nome de solicitacao invalido."); continue; }
                if (!RequestRetryDue(requestId)) continue;
                var request = ReadContractFile<PixPurchaseRequest>(requestFile, 16 * 1024, "Solicitacao PIX");
                ValidateRequest(request, requestId);
                if (request is null) continue;
                if (File.Exists(_paths.SessionFile(request.Id))) { File.Delete(requestFile); DeleteIfExists(_paths.RetryFile(request.Id)); continue; }
                if (File.Exists(_paths.ApprovedFile(request.Id)) || File.Exists(_paths.ProcessedFile(request.Id)))
                {
                    _paths.Quarantine(requestFile, "Repeticao de pedido PIX ja aprovado/processado.");
                    DeleteIfExists(_paths.RetryFile(request.Id));
                    continue;
                }
                // A licenca e revalidada no ultimo instante antes da chamada
                // que pode criar dinheiro real. Falha de licenca mantem o
                // pedido para nova tentativa e nao interrompe a conciliacao
                // das cobrancas que ja existiam.
                var commercialAuthorization = _commercialLicenseValidation?.Invoke();
                if (commercialAuthorization is { IsValid: false })
                    throw new CommercialLicenseUnavailableException(commercialAuthorization.Message);
                var session = await _provider.CreateAsync(request, token);
                ReportProgress(heartbeat);
                SaveQr(session);
                _paths.WriteAtomically(_paths.SessionFile(session.Id), session);
                File.Delete(requestFile);
                DeleteIfExists(_paths.RetryFile(request.Id));
            }
            catch (Exception ex) when (ex is JsonException or RequestRejectedException or FormatException
                or NotSupportedException or InvalidDataException or ArgumentException)
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
            catch (CommercialLicenseUnavailableException ex)
            {
                Console.Error.WriteLine($"Nova cobranca PIX aguardando ativacao comercial: {ex.Message}");
                ScheduleRequestRetry(requestId);
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
            catch (OnlineApiException ex) when (IsRetryableHttpStatus(ex.StatusCode))
            {
                Console.Error.WriteLine($"Falha temporaria na autorizacao on-line: {ex.Message}");
                ScheduleRequestRetry(requestId);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
            {
                Console.Error.WriteLine($"Falha temporaria na cobranca PIX: {ex.Message}");
                ScheduleRequestRetry(requestId);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or MercadoPagoApiException or AdapterApiException or OnlineApiException or InvalidOperationException)
            {
                RejectRequest(requestFile, requestId, ex.Message);
            }
        }

        foreach (var sessionFile in Directory.EnumerateFiles(_paths.Sessions, "*.session.json").OrderBy(Path.GetFileName))
        {
            ReportProgress(heartbeat);
            try
            {
                var sessionJson = ReadContractJson(sessionFile, 64 * 1024, "Sessao PIX");
                using var sessionDocument = JsonDocument.Parse(sessionJson);
                var declaredSchema = sessionDocument.RootElement.TryGetProperty("schemaVersion", out var schemaValue)
                    && schemaValue.ValueKind == JsonValueKind.Number && schemaValue.TryGetInt32(out var parsedSchema)
                    ? parsedSchema : 0;
                var fileId = Path.GetFileName(sessionFile).Replace(".session.json", "", StringComparison.OrdinalIgnoreCase);
                if (declaredSchema is 0 or 1)
                {
                    var legacy = JsonSerializer.Deserialize<PixLegacySession>(sessionJson, Json.Options)
                        ?? throw new JsonException("Sessao PIX v1 vazia.");
                    await ProcessLegacySessionAsync(sessionFile, fileId, legacy, token, heartbeat);
                    continue;
                }
                var session = JsonSerializer.Deserialize<PixSession>(sessionJson, Json.Options);
                if (session is null) throw new JsonException("Sessao PIX vazia.");
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
                ReportProgress(heartbeat);
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
            catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException
                or InvalidDataException or ArgumentException)
            {
                Console.Error.WriteLine($"Sessao PIX corrompida e isolada: {ex.Message}");
                _paths.Quarantine(sessionFile, ex.Message);
            }
            catch (SecurityException ex)
            {
                Console.Error.WriteLine($"PIX bloqueado por divergencia de seguranca: {ex.Message}");
                MarkSessionError(sessionFile, "security_error");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or HttpRequestException or MercadoPagoApiException or AdapterApiException or OnlineApiException or InvalidOperationException)
            {
                Console.Error.WriteLine($"Falha ao consultar PIX: {ex.Message}");
                ScheduleSessionRetry(sessionFile);
            }
        }
        ReportProgress(heartbeat);
    }

    public Task<bool> ApproveMockAsync(string id)
    {
        var file = _paths.SessionFile(id);
        if (!File.Exists(file)) return Task.FromResult(false);
        var session = ReadContractFile<PixSession>(file, 64 * 1024, "Sessao PIX");
        if (session is null || session.Provider != "mock") return Task.FromResult(false);
        ValidateSession(session, id);
        var approved = session with { Status = "approved", UpdatedAt = DateTimeOffset.UtcNow };
        PublishCredit(approved);
        _paths.WriteAtomically(file, approved with { Status = "completed", NextPollAt = DateTimeOffset.MaxValue });
        return Task.FromResult(true);
    }

    private async Task ProcessLegacySessionAsync(string sessionFile, string fileId,
        PixLegacySession legacy, CancellationToken token, Action? heartbeat)
    {
        if (!PixId.IsValid(legacy.Id) || !legacy.Id.Equals(fileId, StringComparison.Ordinal)
            || legacy.Minutes is < 1 or > 480
            || legacy.AmountCents is < 1 or > 100_000_000
            || !PixId.IsValidProviderOrder(legacy.ProviderOrderId)
            || legacy.CreatedAt < DateTimeOffset.UnixEpoch.AddYears(50)
            || legacy.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(5)
            || legacy.Status is not ("pending" or "approved" or "completed" or "cancelled" or "security_error"))
            throw new InvalidDataException("Sessao PIX v1 possui campos invalidos.");

        if (File.Exists(_paths.ProcessedFile(legacy.Id)))
        {
            PreserveLegacyReconciliation(sessionFile, legacy, "already_applied_audit_only",
                "O recibo v1 ja esta em processed; preservar somente para auditoria, sem atribuir novo credito.", false);
            DeleteQrFiles(legacy.Id);
            return;
        }
        if (legacy.Status is "cancelled" or "security_error")
        {
            _paths.Quarantine(sessionFile, "Sessao PIX v1 encerrada sem aprovacao.");
            DeleteQrFiles(legacy.Id);
            return;
        }
        if (string.IsNullOrWhiteSpace(legacy.Provider)
            || !legacy.Provider.Equals(_provider.Name, StringComparison.Ordinal))
        {
            PreserveLegacyReconciliation(sessionFile, legacy, "provider_mismatch_unverified",
                "Sessao v1 usa outro provedor; confirmar manualmente antes de atribuir o credito.", true);
            return;
        }
        if (legacy.Status == "pending" && legacy.NextPollAt > DateTimeOffset.UtcNow) return;

        // O objeto temporario serve somente para consultar o provedor. O ID de
        // beneficiario abaixo nunca e publicado nem aplicado como credito.
        var requestedAt = legacy.CreatedAt.ToUnixTimeSeconds();
        var probe = new PixSession(PixContract.SchemaVersion, legacy.Id, legacy.Minutes,
            legacy.AmountCents, requestedAt,
            requestedAt + Math.Clamp(_options.PaymentExpirationMinutes, 1, 60) * 60L,
            "guest", "legacy_unassigned_wallet", "", legacy.Provider,
            legacy.ProviderOrderId, string.IsNullOrWhiteSpace(legacy.QrData)
                ? "LEGACY-QR-NOT-AVAILABLE" : legacy.QrData,
            "pending", legacy.CreatedAt, legacy.UpdatedAt, legacy.FailureCount,
            DateTimeOffset.UtcNow);
        try
        {
            var refreshed = await _provider.RefreshAsync(probe, token);
            ReportProgress(heartbeat);
            if (refreshed is null) return;
            if (refreshed.Status == "approved")
            {
                if (File.Exists(_paths.ProcessedFile(legacy.Id)))
                {
                    PreserveLegacyReconciliation(sessionFile, legacy, "already_applied_audit_only",
                        "O recibo v1 foi processado durante a revalidacao; nao atribuir novo credito.", false);
                    DeleteQrFiles(legacy.Id);
                    return;
                }
                PreserveLegacyReconciliation(sessionFile, legacy, "approved_unassigned",
                    "Pagamento v1 confirmado novamente no provedor; atribuir manualmente ao cliente correto.", true);
                DeleteQrFiles(legacy.Id);
                return;
            }
            if (refreshed.Status == "pending")
            {
                _paths.WriteAtomically(sessionFile, legacy with
                {
                    Status = "pending",
                    UpdatedAt = DateTimeOffset.UtcNow,
                    FailureCount = 0,
                    NextPollAt = DateTimeOffset.UtcNow.AddSeconds(_options.PollSeconds)
                });
                return;
            }
            _paths.Quarantine(sessionFile, "Pagamento PIX v1 cancelado/expirado segundo o provedor.");
            DeleteQrFiles(legacy.Id);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (SecurityException ex)
        {
            PreserveLegacyReconciliation(sessionFile, legacy, "security_mismatch_unverified",
                "Divergencia ao revalidar sessao v1: " + SafeFailureReason(ex.Message), true);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException
            or MercadoPagoApiException or AdapterApiException or OnlineApiException or InvalidOperationException)
        {
            var failures = Math.Min(legacy.FailureCount + 1, 30);
            Console.Error.WriteLine($"Falha temporaria ao reconciliar PIX v1: {SafeFailureReason(ex.Message)}");
            _paths.WriteAtomically(sessionFile, legacy with
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                FailureCount = failures,
                NextPollAt = DateTimeOffset.UtcNow.AddSeconds(RetryDelay(failures))
            });
        }
    }

    private void PreserveLegacyReconciliation(string sessionFile, PixLegacySession legacy,
        string state, string reason, bool requiresManualAssignment)
    {
        var detectedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var canonical = string.Join("\n", "legacy-reconciliation-v1", legacy.Id,
            legacy.Minutes.ToString(CultureInfo.InvariantCulture),
            legacy.AmountCents.ToString(CultureInfo.InvariantCulture), legacy.Provider,
            legacy.ProviderOrderId, state, requiresManualAssignment ? "1" : "0",
            detectedAt.ToString(CultureInfo.InvariantCulture));
        var signature = PixRequestSigner.Hmac(canonical, _signingKeys.GetOrCreate());
        var reconciliationFile = Path.Combine(_paths.Reconciliation,
            $"{legacy.Id}.legacy-reconciliation.json");
        _paths.WriteAtomically(reconciliationFile, new
        {
            schemaVersion = 1,
            kind = "legacy_unassigned_payment",
            state,
            transactionId = legacy.Id,
            legacy.Minutes,
            legacy.AmountCents,
            legacy.Provider,
            legacy.ProviderOrderId,
            detectedAtUnixSeconds = detectedAt,
            requiresManualAssignment,
            reason,
            signature
        });
        var preservedSession = Path.Combine(_paths.Reconciliation,
            $"{legacy.Id}.{state}.legacy-session.json");
        try { File.Move(sessionFile, preservedSession, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Conciliacao v1 gravada, mas sessao original nao foi movida: {ex.Message}");
        }
        try { File.WriteAllText(reconciliationFile + ".reason.txt", reason, new UTF8Encoding(false)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void ValidateRequest(PixPurchaseRequest? request, string fileId)
    {
        if (request is null) throw new RequestRejectedException("Solicitacao PIX vazia.");
        if (request.SchemaVersion != PixContract.SchemaVersion)
            throw new RequestRejectedException(request.SchemaVersion == 1
                ? "Contrato PIX v1 recusado: gere um novo pedido no frontend atualizado."
                : $"Versao de contrato PIX nao suportada: {request.SchemaVersion}.");
        if (!PixId.IsValid(request.Id) || !request.Id.Equals(fileId, StringComparison.Ordinal))
            throw new RequestRejectedException("Identificador divergente ou invalido.");
        if (!PixId.IsValidBeneficiary(request.BeneficiaryType, request.BeneficiaryId))
            throw new RequestRejectedException("Beneficiario PIX ausente ou invalido.");
        var expected = _options.PriceFor(request.Minutes);
        if (expected <= 0) throw new RequestRejectedException("Pacote de minutos nao permitido.");
        if (request.AmountCents != expected) throw new RequestRejectedException("Valor adulterado ou desatualizado.");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expectedLifetime = (long)_options.PaymentExpirationMinutes * 60;
        if (request.RequestedAtUnixSeconds < 1577836800L
            || request.RequestedAtUnixSeconds > now + 120
            || request.ExpiresAtUnixSeconds <= request.RequestedAtUnixSeconds
            || request.ExpiresAtUnixSeconds - request.RequestedAtUnixSeconds != expectedLifetime
            || request.ExpiresAtUnixSeconds < now)
            throw new RequestRejectedException("Solicitacao expirada ou relogio do sistema incorreto.");
        if (!PixRequestSigner.Verify(request, _signingKeys.GetOrCreate()))
            throw new RequestRejectedException("Assinatura do pedido PIX invalida.");
    }

    private void ValidateSession(PixSession session, string fileId)
    {
        if (session.SchemaVersion != PixContract.SchemaVersion)
            throw new InvalidDataException(session.SchemaVersion == 1
                ? "Sessao PIX v1 nao e compativel; gere uma nova cobranca."
                : "Versao da sessao PIX e invalida.");
        if (!PixId.IsValid(session.Id) || !session.Id.Equals(fileId, StringComparison.Ordinal))
            throw new InvalidDataException("Identificador da sessao divergente.");
        if (!PixId.IsValidBeneficiary(session.BeneficiaryType, session.BeneficiaryId))
            throw new InvalidDataException("Beneficiario da sessao PIX e invalido.");
        if (string.IsNullOrWhiteSpace(session.Provider)
            || !session.Provider.Equals(_provider.Name, StringComparison.Ordinal))
            throw new SecurityException("Provedor da sessao diverge da configuracao ativa.");
        var expected = _options.PriceFor(session.Minutes);
        if (expected <= 0 || session.AmountCents != expected)
            throw new SecurityException("Pacote ou valor da sessao foi adulterado.");
        if (!PixId.IsValidProviderOrder(session.ProviderOrderId))
            throw new SecurityException("Identificador da order e invalido.");
        if (session.Status is not ("pending" or "approved" or "completed" or "cancelled" or "security_error"))
            throw new InvalidDataException("Estado da sessao e invalido.");
        if (session.CreatedAt < DateTimeOffset.UnixEpoch.AddYears(50)
            || session.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(5)
            || session.UpdatedAt < session.CreatedAt.AddMinutes(-1)
            || session.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new InvalidDataException("Relogio da sessao e invalido.");
        if (string.IsNullOrWhiteSpace(session.QrData) || session.QrData.Length is < 20 or > 8192)
            throw new InvalidDataException("Conteudo do QR PIX invalido.");
        if (session.RequestedAtUnixSeconds < 1577836800L
            || session.ExpiresAtUnixSeconds <= session.RequestedAtUnixSeconds
            || session.ExpiresAtUnixSeconds - session.RequestedAtUnixSeconds is < 60 or > 3600
            || !PixRequestSigner.Verify(session.SignedRequest(), _signingKeys.GetOrCreate()))
            throw new InvalidDataException("Vinculo assinado da sessao PIX e invalido.");
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
        var unsigned = new PixCreditEvent(PixContract.SchemaVersion, session.Id, session.Minutes,
            session.AmountCents, session.Provider, session.ProviderOrderId, session.ExpiresAtUnixSeconds,
            session.BeneficiaryType, session.BeneficiaryId, approvedAt,
            approvedAt + PixContract.CreditEventLifetimeSeconds, "");
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

    private static T? ReadContractFile<T>(string file, long maximumBytes, string label)
        => JsonSerializer.Deserialize<T>(ReadContractJson(file, maximumBytes, label), Json.Options);

    private static void ReportProgress(Action? heartbeat) => heartbeat?.Invoke();

    private static string ReadContractJson(string file, long maximumBytes, string label)
    {
        var length = new FileInfo(file).Length;
        if (length <= 1 || length > maximumBytes)
            throw new InvalidDataException($"{label} excede o tamanho permitido ou esta vazia.");
        var json = File.ReadAllText(file, Encoding.UTF8);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException($"{label} deve ser um objeto JSON.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!names.Add(property.Name))
                throw new JsonException($"{label} contem o campo duplicado {property.Name}.");
        return document.RootElement.GetRawText();
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

sealed class CountingCreateTestProvider : IPixProvider
{
    public int CreateCount { get; private set; }
    public string Name => "mock";
    public Task CheckHealthAsync(CancellationToken token) => Task.CompletedTask;
    public Task<PixSession> CreateAsync(PixPurchaseRequest request, CancellationToken token)
    {
        CreateCount++;
        return Task.FromResult(PixSession.Pending(request, Name, "COUNTING-" + request.Id,
            "TURBORAMA-COUNTING:" + request.Id));
    }
    public Task<PixSession?> RefreshAsync(PixSession session, CancellationToken token)
        => Task.FromResult<PixSession?>(session);
}

sealed class ApprovedLegacyTestProvider : IPixProvider
{
    public string Name => "mock";
    public Task CheckHealthAsync(CancellationToken token) => Task.CompletedTask;
    public Task<PixSession> CreateAsync(PixPurchaseRequest request, CancellationToken token)
        => throw new NotSupportedException();
    public Task<PixSession?> RefreshAsync(PixSession session, CancellationToken token)
        => Task.FromResult<PixSession?>(session with { Status = "approved", UpdatedAt = DateTimeOffset.UtcNow });
}

sealed class SlowPendingBatchTestProvider : IPixProvider
{
    public int RefreshCount { get; private set; }
    public string Name => "mock";
    public Task CheckHealthAsync(CancellationToken token) => Task.CompletedTask;
    public Task<PixSession> CreateAsync(PixPurchaseRequest request, CancellationToken token)
        => throw new NotSupportedException();
    public async Task<PixSession?> RefreshAsync(PixSession session, CancellationToken token)
    {
        await Task.Delay(20, token);
        RefreshCount++;
        return session with { Status = "pending", UpdatedAt = DateTimeOffset.UtcNow };
    }
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
            || results.ValueKind != JsonValueKind.Array)
            throw new MercadoPagoApiException(502, "consulta de PDV sem a lista results esperada");
        if (results.EnumerateArray().Select(ReadPos).Any(item =>
                item.ExternalId.Equals(externalPosId, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(item.Status)
                    || item.Status.Equals("active", StringComparison.OrdinalIgnoreCase)))) return;

        // Algumas contas devolveram lista vazia no filtro external_id logo
        // depois de o mesmo PDV aparecer no inventario geral. Antes de declarar
        // 404 e tentar recriar recursos, confirme uma vez pela listagem da
        // propria conta. Isso preserva o PDV existente e evita falso erro.
        using var inventoryJson = await GetAuthorizedJsonAsync("pos?limit=100&offset=0", token);
        if (Results(inventoryJson.RootElement).Select(ReadPos).Any(item =>
                item.ExternalId.Equals(externalPosId, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(item.Status)
                    || item.Status.Equals("active", StringComparison.OrdinalIgnoreCase)))) return;
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
            ValidateAccountEnvironment(account.RootElement);
            var accountId = GetScalarString(account.RootElement, "id");
            if (string.IsNullOrWhiteSpace(accountId))
                throw new MercadoPagoApiException(502, "resposta de autenticacao sem User ID");
            return await GetInfrastructureForAccountAsync(accountId, token);
        }
    }

    internal void ValidateAccountEnvironment(JsonElement account)
    {
        var detected = DetectTestAccount(account);
        if (detected is null)
            throw new SecurityException("O Mercado Pago nao informou um sinal confiavel de conta real ou de teste; nenhuma configuracao foi alterada.");
        var expectsSandbox = _options.MercadoPago.Environment == "sandbox";
        if (detected.Value != expectsSandbox)
            throw new SecurityException(detected.Value
                ? "O Mercado Pago confirmou uma conta de teste, mas o agente esta configurado para producao. Defina MercadoPago.Environment=sandbox somente no quiosque de testes."
                : "O Mercado Pago confirmou uma conta real, mas o agente esta configurado como sandbox. Revise o ambiente antes de gerar cobrancas.");
    }

    internal static bool? DetectTestAccount(JsonElement account)
    {
        if (account.TryGetProperty("test_user", out var testUser)
            && testUser.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return testUser.GetBoolean();
        var email = GetString(account, "email").Trim();
        if (!string.IsNullOrWhiteSpace(email))
            return email.EndsWith("@testuser.com", StringComparison.OrdinalIgnoreCase);
        return null;
    }

    public Task<MercadoPagoInfrastructure> GetInfrastructureForConfiguredAccountAsync(string accountId, CancellationToken token)
        => GetInfrastructureForAccountAsync(accountId.Trim(), token);

    public Task<MercadoPagoSetupResult> EnsureInfrastructureAsync(MercadoPagoSetupRequest setup,
        CancellationToken token)
        => EnsureInfrastructureAsync(setup, token,
            new MercadoPagoCreationPolicy(AllowStoreCreation: true, AllowPointOfSaleCreation: true));

    internal async Task<MercadoPagoSetupResult> EnsureInfrastructureAsync(MercadoPagoSetupRequest setup,
        CancellationToken token, MercadoPagoCreationPolicy creationPolicy)
    {
        setup.ValidateIdentity();
        // Credenciais de sandbox podem ser bloqueadas por politicas no recurso
        // Mercado Livre /users/me. A consulta oficial de lojas com o User ID
        // informado ja valida a posse da conta e retorna 403 quando nao pertence
        // ao mesmo Access Token, sem depender daquele recurso adicional.
        var inventory = await GetInfrastructureForAccountAsync(setup.ExpectedAccountId, token);

        if (creationPolicy.RequireEmptyInventoryBeforeCreation)
        {
            // IDs gerados automaticamente so sao autorizados porque a primeira
            // leitura encontrou a conta totalmente vazia. Revalide imediatamente
            // antes dos POSTs para fechar a janela entre decisao e criacao. Uma
            // retomada idempotente pode enxergar somente os proprios IDs esperados.
            var unexpectedStore = inventory.Stores.Any(store =>
                !store.ExternalId.Equals(setup.StoreExternalId, StringComparison.OrdinalIgnoreCase));
            var unexpectedPoint = inventory.PointsOfSale.Any(point =>
                !point.ExternalId.Equals(setup.PosExternalId, StringComparison.OrdinalIgnoreCase));
            if (unexpectedStore || unexpectedPoint)
                throw new SecurityException("a conta deixou de estar vazia antes da criacao; selecione StoreExternalId e PosExternalId explicitamente");
        }

        var store = inventory.Stores.SingleOrDefault(x => x.ExternalId.Equals(setup.StoreExternalId, StringComparison.Ordinal));
        store ??= await FindStoreByExternalIdAsync(inventory.AccountId, setup.StoreExternalId, token);
        var storeCreated = false;
        if (store is null)
        {
            if (!creationPolicy.AllowStoreCreation)
                throw new SecurityException("a loja selecionada desapareceu antes da confirmacao; nenhuma nova loja foi criada");
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
            if (!creationPolicy.AllowPointOfSaleCreation)
                throw new SecurityException("o PDV selecionado desapareceu antes da confirmacao; nenhum novo PDV foi criado");
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
            GetScalarString(item, "store_id"), GetString(item, "external_store_id"),
            GetString(item, "status"));

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

static class PixContract
{
    public const int SchemaVersion = 2;
    public const long CreditEventLifetimeSeconds = 30L * 24 * 60 * 60;
}

sealed record PixPurchaseRequest(int SchemaVersion, string Id, int Minutes, long AmountCents,
    long RequestedAtUnixSeconds, long ExpiresAtUnixSeconds, string BeneficiaryType, string BeneficiaryId,
    string Signature)
{
    public DateTimeOffset RequestedAt => DateTimeOffset.FromUnixTimeSeconds(RequestedAtUnixSeconds);
    public DateTimeOffset ExpiresAt => DateTimeOffset.FromUnixTimeSeconds(ExpiresAtUnixSeconds);
}

sealed record PixCreditEvent(int SchemaVersion, string TransactionId, int Minutes, long AmountCents,
    string Provider, string ProviderOrderId, long RequestExpiresAtUnixSeconds, string BeneficiaryType,
    string BeneficiaryId, long ApprovedAtUnixSeconds, long EventExpiresAtUnixSeconds, string Signature);

// Sessao criada antes do contrato v2. Ela nao tem beneficiario e, portanto,
// jamais pode liberar tempo automaticamente. Ainda assim o agente consulta o
// provedor ate o estado final e preserva pagamentos aprovados para conciliacao.
sealed record PixLegacySession(string Id, int Minutes, long AmountCents, string Provider,
    string ProviderOrderId, string QrData, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, int FailureCount, DateTimeOffset NextPollAt);

sealed record PixSession(int SchemaVersion, string Id, int Minutes, long AmountCents,
    long RequestedAtUnixSeconds, long ExpiresAtUnixSeconds, string BeneficiaryType, string BeneficiaryId,
    string RequestSignature, string Provider, string ProviderOrderId, string QrData, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int FailureCount, DateTimeOffset NextPollAt)
{
    public static PixSession Pending(PixPurchaseRequest request, string provider, string providerOrderId, string qrData)
        => new(request.SchemaVersion, request.Id, request.Minutes, request.AmountCents,
            request.RequestedAtUnixSeconds, request.ExpiresAtUnixSeconds, request.BeneficiaryType,
            request.BeneficiaryId, request.Signature, provider, providerOrderId, qrData, "pending",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, DateTimeOffset.UtcNow);

    public PixPurchaseRequest SignedRequest()
        => new(SchemaVersion, Id, Minutes, AmountCents, RequestedAtUnixSeconds, ExpiresAtUnixSeconds,
            BeneficiaryType, BeneficiaryId, RequestSignature);
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

static class PixRequestSigner
{
    public static string Sign(PixPurchaseRequest request, byte[] key)
        => Hmac(Canonical(request), key);

    public static bool Verify(PixPurchaseRequest request, byte[] key)
    {
        if (request.Signature is null || request.Signature.Length != 64
            || request.Signature.Any(ch => !Uri.IsHexDigit(ch))) return false;
        var expected = Sign(request with { Signature = "" }, key);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(request.Signature.ToLowerInvariant()));
    }

    private static string Canonical(PixPurchaseRequest request)
        => string.Join("\n",
            request.SchemaVersion.ToString(CultureInfo.InvariantCulture), request.Id,
            request.Minutes.ToString(CultureInfo.InvariantCulture),
            request.AmountCents.ToString(CultureInfo.InvariantCulture),
            request.RequestedAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
            request.ExpiresAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
            request.BeneficiaryType, request.BeneficiaryId);

    internal static string Hmac(string canonical, byte[] key)
        => Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}

static class PixEventSigner
{
    public static string Sign(PixCreditEvent credit, byte[] key)
        => PixRequestSigner.Hmac(Canonical(credit), key);

    public static bool Verify(PixCreditEvent credit, byte[] key)
    {
        if (credit.Signature is null || credit.Signature.Length != 64
            || credit.Signature.Any(ch => !Uri.IsHexDigit(ch))) return false;
        var expected = Sign(credit with { Signature = "" }, key);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(credit.Signature.ToLowerInvariant()));
    }

    private static string Canonical(PixCreditEvent credit)
        => string.Join("\n",
            credit.SchemaVersion.ToString(CultureInfo.InvariantCulture), credit.TransactionId,
            credit.Minutes.ToString(CultureInfo.InvariantCulture), credit.AmountCents.ToString(CultureInfo.InvariantCulture),
            credit.Provider, credit.ProviderOrderId,
            credit.RequestExpiresAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
            credit.BeneficiaryType, credit.BeneficiaryId,
            credit.ApprovedAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
            credit.EventExpiresAtUnixSeconds.ToString(CultureInfo.InvariantCulture));
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
            TestDaemonIdentityContract();
            TestStopMonitorAsync(paths).GetAwaiter().GetResult();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            const string beneficiaryId = "player_SELFTEST_0123456789abcdef";
            var request = new PixPurchaseRequest(PixContract.SchemaVersion, "PIXSELFTEST", minutes, cents,
                now, now + options.PaymentExpirationMinutes * 60L, "player", beneficiaryId, "");
            request = request with { Signature = PixRequestSigner.Sign(request, key) };
            if (!PixRequestSigner.Verify(request, key)) throw new InvalidOperationException("assinatura do pedido valida rejeitada");
            if (PixRequestSigner.Verify(request with { BeneficiaryId = "guest_SELFTEST_0123456789abcdef" }, key))
                throw new InvalidOperationException("troca do beneficiario do pedido nao foi detectada");
            if (PixRequestSigner.Verify(request with { ExpiresAtUnixSeconds = request.ExpiresAtUnixSeconds + 1 }, key))
                throw new InvalidOperationException("adulteracao da expiracao do pedido nao foi detectada");

            var credit = new PixCreditEvent(PixContract.SchemaVersion, request.Id, minutes, cents, "mock",
                "PIX-TEST", request.ExpiresAtUnixSeconds, request.BeneficiaryType, request.BeneficiaryId,
                now, now + PixContract.CreditEventLifetimeSeconds, "");
            var signed = credit with { Signature = PixEventSigner.Sign(credit, key) };
            if (!PixEventSigner.Verify(signed, key)) throw new InvalidOperationException("assinatura valida rejeitada");
            if (PixEventSigner.Verify(signed with { Minutes = minutes + 1 }, key)) throw new InvalidOperationException("adulteracao nao detectada");
            if (PixEventSigner.Verify(signed with { BeneficiaryId = "guest_SELFTEST_0123456789abcdef" }, key))
                throw new InvalidOperationException("troca do beneficiario do credito nao foi detectada");
            var file = Path.Combine(paths.Root, "self-test.json");
            paths.WriteAtomically(file, signed);
            var read = JsonSerializer.Deserialize<PixCreditEvent>(File.ReadAllText(file), Json.Options);
            if (read is null || !PixEventSigner.Verify(read, key)) throw new InvalidOperationException("gravacao atomica");
            File.Delete(file);
            TestKioskIdentityJsonContract();
            TestSignedPurchaseContract(options, paths, keys, request);
            TestHeartbeatBatch(options, paths);
            TestPostalAddressCache(paths);
            TestPostalAddressFallback(paths);
            TestLocationValidation();
            TestCompatibleQr(paths);
            var credentialDpapiTested = TestCredentialInbox(paths);
            TestTpmSecretContract(paths, credentialDpapiTested);
            CommercialLicenseSelfTest.Run();
            TestOwnerProvisioningContract(paths);
            TestMercadoPagoResponses();
            TestMercadoPagoHealth(options, paths);
            TestMercadoPagoProvisioning(options, paths, credentialDpapiTested);
            TestAdapterResponses(options, paths);
            OnlineProtocolSelfTest.Run(options);
            Console.WriteLine(credentialDpapiTested
                ? "SELF-TEST PIX: OK (contrato v2 assinado, identidade do daemon, beneficiario, expiracao/replay, conciliacao v1, heartbeat/lote, parada graciosa dirigida, quarentena, preco, QR, credencial, loja/PDV, Mercado Pago, adaptador e servidor on-line)."
                : "SELF-TEST PIX: OK (contrato v2 assinado, identidade do daemon, beneficiario, expiracao/replay, conciliacao v1, heartbeat/lote, parada graciosa dirigida, quarentena, preco, QR, contrato de credencial, loja/PDV, Mercado Pago, adaptador e servidor on-line). DPAPI nao estava disponivel para o usuario de compilacao; validar a credencial segura no Windows do quiosque.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
            or InvalidOperationException or FormatException or JsonException or SecurityException or ArgumentException)
        {
            Console.Error.WriteLine($"SELF-TEST PIX: FALHOU - {ex.Message}");
            return 20;
        }
    }

    private static void TestDaemonIdentityContract()
    {
        if (AgentCommand.Parse(["--daemon"]).RunMode != AgentRunMode.Daemon
            || AgentCommand.Parse(["--daemon", "--bridge", "C:\\pix"]).RunMode != AgentRunMode.Daemon
            || AgentCommand.Parse(["--once"]).RunMode != AgentRunMode.OneShot
            || AgentCommand.Parse(["--self-test"]).RunMode != AgentRunMode.Administrative
            || AgentCommand.Parse(["--license-status"]).RunMode != AgentRunMode.Administrative
            || AgentCommand.Parse(["--license-request", "C:\\pedido.json"]).RunMode != AgentRunMode.Administrative
            || AgentCommand.Parse(["--online-activate"]).RunMode != AgentRunMode.Administrative
            || AgentCommand.Parse(["--online-configure", "C:\\online.json"]).RunMode != AgentRunMode.Administrative
            || AgentCommand.Parse(["--once", "--check-provider"]).RunMode != AgentRunMode.Administrative
            || !CommandIsRejected([])
            || !CommandIsRejected(["--bridge", "C:\\pix"])
            || !CommandIsRejected(["--daemon", "--once"])
            || !CommandIsRejected(["--daemon", "--check-provider"])
            || !CommandIsRejected(["--license-status", "--license-request", "C:\\pedido.json"]))
            throw new InvalidOperationException("classificacao dos modos do agente PIX");

        var supplied = new string('A', 64);
        var randomCalled = false;
        var selected = PixDaemonIdentity.SelectManagerToken(supplied, () =>
        {
            randomCalled = true;
            return new byte[32];
        });
        if (randomCalled || !selected.Equals(supplied, StringComparison.Ordinal))
            throw new InvalidOperationException("token valido do manager nao foi preservado");
        var generated = PixDaemonIdentity.SelectManagerToken("invalido",
            () => Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        if (!generated.Equals("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f",
                StringComparison.Ordinal)
            || !PixDaemonIdentity.IsManagerToken(generated)
            || PixDaemonIdentity.IsManagerToken(new string('g', 64)))
            throw new InvalidOperationException("geracao/validacao do token efemero do daemon");
        if (!PixDaemonIdentity.HashManagerToken("abc").Equals(
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                StringComparison.Ordinal))
            throw new InvalidOperationException("SHA-256 da identidade do daemon");

        var identity = new PixDaemonDescriptor(1234, 133123456789012345UL,
            PixDaemonIdentity.HashManagerToken(generated));
        var directed = StopRequestJson(identity);
        if (PixAgentControl.ClassifyStopRequest(directed, identity) != PixStopRequestDisposition.Authorized
            || PixAgentControl.ClassifyStopRequest(StopRequestJson(identity with { ProcessId = 1235 }), identity)
                != PixStopRequestDisposition.Mismatch
            || PixAgentControl.ClassifyStopRequest("installer-update\n", identity)
                != PixStopRequestDisposition.InstallerUpdate
            || PixAgentControl.ClassifyStopRequest("self-test", identity) != PixStopRequestDisposition.Invalid
            || PixAgentControl.ClassifyStopRequest(directed.Insert(directed.IndexOf('{') + 1,
                    "\"schemaVersion\":1,"), identity)
                != PixStopRequestDisposition.Invalid)
            throw new InvalidOperationException("matching estrito do pedido de parada do daemon");
    }

    private static bool CommandIsRejected(string[] args)
    {
        try { AgentCommand.Parse(args); return false; }
        catch (InvalidOperationException) { return true; }
    }

    private static string StopRequestJson(PixDaemonDescriptor identity) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        mode = "daemon",
        processId = identity.ProcessId,
        processStartFileTimeUtc = identity.ProcessStartFileTimeUtc,
        managerTokenHash = identity.ManagerTokenHash
    }, Json.Options);

    private static async Task TestStopMonitorAsync(PixPaths paths)
    {
        try { if (File.Exists(paths.AgentStopRequestFile)) File.Delete(paths.AgentStopRequestFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("limpeza do sentinel de parada", ex);
        }

        var identity = new PixDaemonDescriptor(4321, 133123456789012345UL,
            PixDaemonIdentity.HashManagerToken(new string('b', 64)));
        File.WriteAllText(paths.AgentStopRequestFile,
            StopRequestJson(identity with { ProcessId = identity.ProcessId + 1 }), new UTF8Encoding(false));
        if (PixAgentControl.TryConsumeStopRequest(paths, identity)
            || File.Exists(paths.AgentStopRequestFile))
            throw new InvalidOperationException("sentinel obsoleto nao foi colocado em quarentena");

        using var cancellation = new CancellationTokenSource();
        var publications = 0;
        var inFlightOperation = Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
        // Deixe o pedido pronto antes de agendar o monitor. O objetivo deste
        // teste e validar consumo/cancelamento, nao disputar o agendador do
        // ThreadPool de uma maquina de build carregada.
        File.WriteAllText(paths.AgentStopRequestFile, StopRequestJson(identity), new UTF8Encoding(false));
        await using var monitor = PixAgentStopMonitor.Start(paths, identity, cancellation,
            () => Interlocked.Increment(ref publications), TimeSpan.FromMilliseconds(10));

        try
        {
            await inFlightOperation.WaitAsync(TimeSpan.FromSeconds(10));
            throw new InvalidOperationException("monitor nao cancelou a operacao em andamento");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException("monitor nao observou o sentinel dentro do limite", ex);
        }

        await Task.Delay(50);
        if (publications != 1 || File.Exists(paths.AgentStopRequestFile)
            || PixAgentControl.TryConsumeStopRequest(paths, identity))
            throw new InvalidOperationException("monitor do sentinel de parada graciosa do agente");
    }

    private static void TestKioskIdentityJsonContract()
    {
        if (KioskProcessIdentity.ParseKioskUserJson("{\"kioskUser\":\"Admin\",\"frontendExecutable\":\"D:\\\\emulationstation\\\\emulationstation.exe\"}") != "Admin")
            throw new InvalidOperationException("kioskUser valido nao foi lido");
        foreach (var invalid in new[]
        {
            "{}",
            "{\"kioskUser\":\"Admin\",\"kioskUser\":\"Outro\"}",
            "{\"kioskUser\":null}",
            "{\"kioskUser\":\" Admin \"}",
            "[]"
        })
        {
            var rejected = false;
            try { KioskProcessIdentity.ParseKioskUserJson(invalid); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("JSON kioskUser inseguro foi aceito");
        }
        if (KioskProcessIdentity.GroupListContainsAdministrator(new[] { "Users" }, "Administrators"))
            throw new InvalidOperationException("grupo Users foi confundido com Administrators");
        if (!KioskProcessIdentity.GroupListContainsAdministrator(new[] { "Users", "Administrators" }, "Administrators"))
            throw new InvalidOperationException("grupo Administrators nao foi reconhecido");
        if (!KioskProcessIdentity.GroupListContainsAdministrator(new[] { "Usuarios", "Administradores" }, "Administradores"))
            throw new InvalidOperationException("nome localizado de Administrators nao foi reconhecido");
    }

    private static void TestSignedPurchaseContract(PixOptions original, PixPaths paths,
        PixSigningKeyStore keys, PixPurchaseRequest validRequest)
    {
        var options = (original with { Provider = "mock", ProductionEnabled = false }).Normalize();
        var engine = new PixEngine(options, paths, new MockPixProvider(), keys);

        var blockedRequest = validRequest with { Id = "LICENSEBLOCKSELFTEST", Signature = "" };
        blockedRequest = blockedRequest with { Signature = PixRequestSigner.Sign(blockedRequest, keys.GetOrCreate()) };
        paths.WriteAtomically(paths.RequestFile(blockedRequest.Id), blockedRequest);
        var blockedProvider = new CountingCreateTestProvider();
        var blockedEngine = new PixEngine(options, paths, blockedProvider, keys, () =>
            CommercialLicenseValidationResult.Failed(CommercialLicenseValidationState.Missing,
                "licenca ausente no autoteste"));
        blockedEngine.RunOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (blockedProvider.CreateCount != 0 || !File.Exists(paths.RequestFile(blockedRequest.Id))
            || File.Exists(paths.SessionFile(blockedRequest.Id)) || !File.Exists(paths.RetryFile(blockedRequest.Id)))
            throw new InvalidOperationException("o provedor foi chamado sem licenca comercial valida");
        File.Delete(paths.RequestFile(blockedRequest.Id));
        File.Delete(paths.RetryFile(blockedRequest.Id));

        var malformedFile = paths.RequestFile("MALFORMEDSELFTEST");
        File.WriteAllText(malformedFile, "{\"schemaVersion\":2,", new UTF8Encoding(false));
        engine.RunOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (File.Exists(malformedFile) || !Directory.EnumerateFiles(paths.Rejected, "*MALFORMEDSELFTEST*").Any())
            throw new InvalidOperationException("JSON malformado nao foi colocado em quarentena");

        var v1File = paths.RequestFile("V1SELFTEST");
        paths.WriteAtomically(v1File, new { schemaVersion = 1, id = "V1SELFTEST", minutes = validRequest.Minutes,
            amountCents = validRequest.AmountCents, requestedAt = DateTimeOffset.UtcNow });
        engine.RunOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (File.Exists(v1File) || !Directory.EnumerateFiles(paths.Rejected, "*V1SELFTEST*").Any())
            throw new InvalidOperationException("pedido v1 nao foi recusado claramente");

        var expired = validRequest with
        {
            Id = "EXPIREDSELFTEST",
            RequestedAtUnixSeconds = DateTimeOffset.UtcNow.AddMinutes(-options.PaymentExpirationMinutes - 2).ToUnixTimeSeconds(),
            ExpiresAtUnixSeconds = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds(),
            Signature = ""
        };
        expired = expired with { Signature = PixRequestSigner.Sign(expired, keys.GetOrCreate()) };
        paths.WriteAtomically(paths.RequestFile(expired.Id), expired);
        engine.RunOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (File.Exists(paths.RequestFile(expired.Id)) || !Directory.EnumerateFiles(paths.Rejected, "*EXPIREDSELFTEST*").Any())
            throw new InvalidOperationException("pedido expirado/repetido nao foi isolado");

        paths.WriteAtomically(paths.RequestFile(validRequest.Id), validRequest);
        engine.RunOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        var session = JsonSerializer.Deserialize<PixSession>(File.ReadAllText(paths.SessionFile(validRequest.Id)), Json.Options)
            ?? throw new InvalidOperationException("sessao v2 nao foi criada");
        if (session.SchemaVersion != PixContract.SchemaVersion
            || session.BeneficiaryType != validRequest.BeneficiaryType
            || session.BeneficiaryId != validRequest.BeneficiaryId
            || !PixRequestSigner.Verify(session.SignedRequest(), keys.GetOrCreate()))
            throw new InvalidOperationException("sessao nao preservou o vinculo assinado do beneficiario");
        if (!engine.ApproveMockAsync(validRequest.Id).GetAwaiter().GetResult())
            throw new InvalidOperationException("sessao mock v2 nao foi aprovada");
        var approved = JsonSerializer.Deserialize<PixCreditEvent>(
            File.ReadAllText(paths.ApprovedFile(validRequest.Id)), Json.Options)
            ?? throw new InvalidOperationException("evento v2 nao foi publicado");
        if (approved.BeneficiaryType != validRequest.BeneficiaryType
            || approved.BeneficiaryId != validRequest.BeneficiaryId
            || approved.EventExpiresAtUnixSeconds != approved.ApprovedAtUnixSeconds + PixContract.CreditEventLifetimeSeconds
            || !PixEventSigner.Verify(approved, keys.GetOrCreate()))
            throw new InvalidOperationException("evento de credito perdeu beneficiario, expiracao ou assinatura");

        const string legacyId = "LEGACYSELFTEST";
        var legacy = new PixLegacySession(legacyId, validRequest.Minutes,
            validRequest.AmountCents, "mock", "LEGACY-ORDER-1",
            "LEGACY-QR-PAYLOAD-SELFTEST", "completed", DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(-1), 0, DateTimeOffset.MinValue);
        paths.WriteAtomically(paths.SessionFile(legacyId), legacy);
        var legacyEngine = new PixEngine(options, paths, new ApprovedLegacyTestProvider(), keys);
        legacyEngine.RunOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        var reconciliationFile = Path.Combine(paths.Reconciliation,
            $"{legacyId}.legacy-reconciliation.json");
        if (File.Exists(paths.SessionFile(legacyId)) || !File.Exists(reconciliationFile)
            || File.Exists(paths.ApprovedFile(legacyId)))
            throw new InvalidOperationException("sessao v1 aprovada nao foi preservada para conciliacao");
        using var reconciliation = JsonDocument.Parse(File.ReadAllText(reconciliationFile));
        if (reconciliation.RootElement.GetProperty("state").GetString() != "approved_unassigned"
            || !reconciliation.RootElement.GetProperty("requiresManualAssignment").GetBoolean())
            throw new InvalidOperationException("conciliacao v1 nao exige atribuicao manual segura");

        const string appliedLegacyId = "LEGACYAPPLIEDSELFTEST";
        var alreadyApplied = legacy with { Id = appliedLegacyId, ProviderOrderId = "LEGACY-ORDER-2" };
        paths.WriteAtomically(paths.SessionFile(appliedLegacyId), alreadyApplied);
        paths.WriteAtomically(paths.ProcessedFile(appliedLegacyId), new
        {
            schemaVersion = 1,
            transactionId = appliedLegacyId
        });
        legacyEngine.RunOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        var auditFile = Path.Combine(paths.Reconciliation,
            $"{appliedLegacyId}.legacy-reconciliation.json");
        using var audit = JsonDocument.Parse(File.ReadAllText(auditFile));
        if (audit.RootElement.GetProperty("state").GetString() != "already_applied_audit_only"
            || audit.RootElement.GetProperty("requiresManualAssignment").GetBoolean())
            throw new InvalidOperationException("recibo v1 ja processado poderia duplicar credito");
    }

    private static void TestHeartbeatBatch(PixOptions original, PixPaths parentPaths)
    {
        var paths = new PixPaths(Path.Combine(parentPaths.Root, "heartbeat-batch"));
        paths.EnsureDirectories();
        var keys = new PixSigningKeyStore(paths.SigningKeyFile);
        var key = keys.GetOrCreate();
        var options = (original with { Provider = "mock", ProductionEnabled = false }).Normalize();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (var index = 1; index <= 3; index++)
        {
            var request = new PixPurchaseRequest(PixContract.SchemaVersion,
                $"HEARTBEATSELFTEST{index}", options.AllowedMinutes.First(),
                options.PriceFor(options.AllowedMinutes.First()), now,
                now + options.PaymentExpirationMinutes * 60L, "player",
                $"player_HEARTBEAT_0123456789abcde{index}", "");
            request = request with { Signature = PixRequestSigner.Sign(request, key) };
            var session = PixSession.Pending(request, "mock", $"SLOW-ORDER-{index}",
                $"TURBORAMA-HEARTBEAT-QR-PAYLOAD-{index}");
            paths.WriteAtomically(paths.SessionFile(request.Id), session);
        }

        var provider = new SlowPendingBatchTestProvider();
        var engine = new PixEngine(options, paths, provider, keys);
        var pulses = 0;
        engine.RunOnceAsync(CancellationToken.None, () => pulses++).GetAwaiter().GetResult();
        if (provider.RefreshCount != 3 || pulses < 8)
            throw new InvalidOperationException("heartbeat nao foi renovado entre sessoes lentas do lote");
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

    private static void TestOwnerProvisioningContract(PixPaths paths)
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

        var tokenLikeError = "erro remoto: APP" + "_USR-self-test-sensitive-value-1234567890";
        var safeError = PixOwnerProvisioner.SafeSetupMessage(tokenLikeError);
        if (safeError.Contains("sensitive-value", StringComparison.Ordinal)
            || !safeError.Contains("Access Token oculto", StringComparison.Ordinal))
            throw new InvalidOperationException("mensagem de provisionamento expos Access Token");

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

        var productionOwner = new PixOwnerSettings
        {
            Enabled = true,
            Provider = "mercadopago",
            AccountId = "123456",
            StoreExternalId = "LZLOJA01",
            PosExternalId = "LZPIXCOMP",
            PostalCode = "57084648",
            StreetNumber = "100",
            Reference = "Teste",
            PackagePricesCents = new Dictionary<int, long>
            {
                [15] = 100, [30] = 200, [45] = 300, [60] = 400, [120] = 800
            }
        };
        var migratedFromSandbox = productionOwner.Apply(posOptions with
        {
            MercadoPago = posOptions.MercadoPago with { Environment = "sandbox" }
        });
        if (migratedFromSandbox.MercadoPago.Environment != "production")
            throw new InvalidOperationException("cadastro comercial nao substituiu ambiente sandbox anterior");

        var legacyOwnerFile = Path.Combine(paths.Root, "owner-settings.json");
        paths.WriteAtomically(legacyOwnerFile, productionOwner);
        var normalizedLegacyOwner = PixOwnerSettings.LoadIfPresent(paths.Root)
            ?? throw new InvalidOperationException("cadastro legado nao foi carregado");
        if (!normalizedLegacyOwner.SetupState.Equals("pending", StringComparison.OrdinalIgnoreCase)
            || normalizedLegacyOwner.PosExternalId != "TURBORAMAKIOSK01")
            throw new InvalidOperationException("PDV legado LZPIXCOMP nao foi migrado para configuracao pendente segura");
        try { File.Delete(legacyOwnerFile); } catch (IOException) { }

        // Um reparo do Mercado Pago nao pode apagar a maquina reconhecida no
        // servidor, nem mesmo quando a parte bancaria anterior esta invalida.
        var corruptPaymentWithValidLicense = productionOwner with
        {
            PosExternalId = "",
            PackagePricesCents = new Dictionary<int, long>(),
            OnlineLicensingEnabled = true,
            OnlineBaseUrl = "https://pix.lzgames.com.br/",
            OnlineLicenseId = "TR-SELFTEST-001",
            OnlineProtectionProfile = "SOFTWARE_BOUND_ONLINE",
            PixEnabled = false,
            OnlineConfigurationVersion = 73,
            OnlineConfigurationPending = true
        };
        paths.WriteAtomically(legacyOwnerFile, corruptPaymentWithValidLicense);
        var controlSnapshot = PixOwnerControlSnapshot.LoadIfPresent(paths.Root)
            ?? throw new InvalidOperationException("controle on-line existente nao foi carregado");
        var repairedPayment = controlSnapshot.Preserve(productionOwner with { PosExternalId = "LZPIXCAIXA01" });
        if (!repairedPayment.OnlineLicensingEnabled
            || repairedPayment.OnlineLicenseId != "TR-SELFTEST-001"
            || repairedPayment.OnlineProtectionProfile != "SOFTWARE_BOUND_ONLINE"
            || repairedPayment.PixEnabled
            || repairedPayment.OnlineConfigurationVersion != 73
            || !repairedPayment.OnlineConfigurationPending)
            throw new InvalidOperationException("reparo bancario apagou campos de licenca/controle on-line");
        var repairedOptions = controlSnapshot.Apply(new PixOptions().Normalize());
        if (!repairedOptions.OnlineLicensingEnabled
            || repairedOptions.Online.LicenseId != "TR-SELFTEST-001")
            throw new InvalidOperationException("licenca preservada nao foi aplicada durante o reparo bancario");
        try { File.Delete(legacyOwnerFile); } catch (IOException) { }

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

    private static void TestTpmSecretContract(PixPaths paths, bool dpapiAvailable)
    {
        var firstFingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("TurboRama-TPM-self-test-1"))).ToLowerInvariant();
        var secondFingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("TurboRama-TPM-self-test-2"))).ToLowerInvariant();
        if (TpmCngMachineBinding.NormalizeFingerprint(firstFingerprint.ToUpperInvariant()) != firstFingerprint)
            throw new InvalidOperationException("normalizacao da impressao TPM");
        var invalidRejected = false;
        try { TpmCngMachineBinding.NormalizeFingerprint("invalida"); }
        catch (SecurityException) { invalidRejected = true; }
        if (!invalidRejected) throw new InvalidOperationException("impressao TPM invalida foi aceita");
        if (!dpapiAvailable) return;

        const string token = "APP" + "_USR-tpm-bound-self-test-token-1234567890";
        var boundFile = Path.Combine(paths.Root, "secret-tpm-self-test.dat");
        var legacyFile = Path.Combine(paths.Root, "secret-tpm-legacy-self-test.dat");
        try
        {
            var firstBinding = new FakePixMachineBinding(firstFingerprint);
            var boundStore = new PixSecretStore(boundFile, requireTpmMachineBinding: true, firstBinding);
            boundStore.Save(token);
            var serialized = File.ReadAllText(boundFile, Encoding.UTF8);
            if (!PixSecretStore.IsBoundEnvelope(serialized)
                || !PixSecretStore.IsHardwareSealedEnvelope(serialized)
                || serialized.Contains(token, StringComparison.Ordinal))
                throw new InvalidOperationException("envelope do cofre vinculado ao TPM");
            var recovered = boundStore.TryLoad();
            if (!recovered.IsAvailable || !recovered.Value!.Equals(token, StringComparison.Ordinal)
                || firstBinding.CreateCalls != 1 || firstBinding.VerifyCalls != 1)
                throw new InvalidOperationException("cofre vinculado ao TPM nao foi recuperado");

            // Mesmo com a opcao desativada depois, um envelope que ja nasceu
            // vinculado continua exigindo a chave original: nao ha downgrade.
            var copiedStore = new PixSecretStore(boundFile, requireTpmMachineBinding: false,
                new FakePixMachineBinding(secondFingerprint));
            if (copiedStore.TryLoad().State != PixSecretState.Unreadable)
                throw new InvalidOperationException("cofre TPM copiado para outra maquina foi aceito");

            new PixSecretStore(legacyFile).Save(token);
            var requiredStore = new PixSecretStore(legacyFile, requireTpmMachineBinding: true,
                firstBinding);
            if (requiredStore.TryLoad().State != PixSecretState.Unreadable)
                throw new InvalidOperationException("cofre legado foi aceito sem reenrolamento TPM");
        }
        finally
        {
            foreach (var file in new[] { boundFile, legacyFile })
            {
                try { if (File.Exists(file)) File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private sealed class FakePixMachineBinding : IPixMachineSecretBinding
    {
        private readonly string _fingerprint;
        private readonly RSA _rsa = RSA.Create(2048);

        public FakePixMachineBinding(string fingerprint)
            => _fingerprint = TpmCngMachineBinding.NormalizeFingerprint(fingerprint);

        public int CreateCalls { get; private set; }
        public int VerifyCalls { get; private set; }

        public string GetOrCreateFingerprint()
        {
            CreateCalls++;
            return _fingerprint;
        }

        public void VerifyFingerprint(string expectedFingerprint)
        {
            VerifyCalls++;
            if (!TpmCngMachineBinding.NormalizeFingerprint(expectedFingerprint)
                .Equals(_fingerprint, StringComparison.Ordinal))
                throw new SecurityException("vinculo TPM de teste divergente");
        }

        public PixWrappedMachineKey WrapKey(ReadOnlySpan<byte> keyMaterial)
        {
            CreateCalls++;
            return new PixWrappedMachineKey(_fingerprint,
                _rsa.Encrypt(keyMaterial, RSAEncryptionPadding.OaepSHA256));
        }

        public byte[] UnwrapKey(string expectedFingerprint, ReadOnlySpan<byte> wrappedKey)
        {
            VerifyFingerprint(expectedFingerprint);
            return _rsa.Decrypt(wrappedKey, RSAEncryptionPadding.OaepSHA256);
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
        {
            var secretStore = new PixSecretStore(paths.SecretFile)
                .WithTransientSecret("APP_USR-self-test-token");
            var provider = new MercadoPagoPixProvider(options, secretStore, new FakeMercadoPagoHealthHandler(posExists: true));
            using (var testAccount = JsonDocument.Parse("{\"id\":123456,\"test_user\":true,\"email\":\"seller@testuser.com\"}"))
            {
                var rejected = false;
                try { provider.ValidateAccountEnvironment(testAccount.RootElement); }
                catch (SecurityException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("conta sandbox foi aceita como producao");
            }
            var sandboxOptions = options with { MercadoPago = options.MercadoPago with { Environment = "sandbox" } };
            var sandboxProvider = new MercadoPagoPixProvider(sandboxOptions, secretStore, new FakeMercadoPagoHealthHandler(posExists: true));
            using (var testAccount = JsonDocument.Parse("{\"id\":123456,\"email\":\"seller@testuser.com\"}"))
                sandboxProvider.ValidateAccountEnvironment(testAccount.RootElement);
            using (var productionAccount = JsonDocument.Parse("{\"id\":123456,\"test_user\":false}"))
                provider.ValidateAccountEnvironment(productionAccount.RootElement);
            using (var unknownAccount = JsonDocument.Parse("{\"id\":123456}"))
            {
                var rejected = false;
                try { provider.ValidateAccountEnvironment(unknownAccount.RootElement); }
                catch (SecurityException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("conta sem sinal production/sandbox foi aceita");
            }
            provider.CheckHealthAsync(CancellationToken.None).GetAwaiter().GetResult();

            var inventoryFallback = new MercadoPagoPixProvider(options, secretStore,
                new FakeMercadoPagoHealthHandler(posExists: true, filteredQueryMiss: true));
            inventoryFallback.CheckHealthAsync(CancellationToken.None).GetAwaiter().GetResult();

            var missing = new MercadoPagoPixProvider(options, secretStore, new FakeMercadoPagoHealthHandler(posExists: false));
            try
            {
                missing.CheckHealthAsync(CancellationToken.None).GetAwaiter().GetResult();
                throw new InvalidOperationException("PDV inexistente aceito pelo Mercado Pago");
            }
            catch (MercadoPagoApiException ex) when (ex.StatusCode == 404) { }

            var inactive = new MercadoPagoPixProvider(options, secretStore,
                new FakeMercadoPagoHealthHandler(posExists: true, posStatus: "inactive"));
            try
            {
                inactive.CheckHealthAsync(CancellationToken.None).GetAwaiter().GetResult();
                throw new InvalidOperationException("PDV inativo foi aceito pelo health-check filtrado");
            }
            catch (MercadoPagoApiException ex) when (ex.StatusCode == 404) { }

            // Exercita o contrato que realmente gera e confirma o QR. O
            // handler falso valida headers, idempotencia e todos os campos
            // essenciais enviados a /v1/orders antes de devolver qr_data.
            var orderProvider = new MercadoPagoPixProvider(options, secretStore, new FakeMercadoPagoOrderHandler());
            var purchaseNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var purchase = new PixPurchaseRequest(PixContract.SchemaVersion, "PIXSELFTEST", 15, 750,
                purchaseNow, purchaseNow + options.PaymentExpirationMinutes * 60L, "player",
                "player_SELFTEST_0123456789abcdef", "");
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
                PosExternalId = "LZPIXCAIXA01",
                StreetName = "Rua de Teste",
                StreetNumber = "100",
                CityName = "Sao Paulo",
                StateName = "Sao Paulo",
                Latitude = -23.55052,
                Longitude = -46.633308,
                Reference = "Teste automatizado"
            };
            var created = setupProvider.EnsureInfrastructureAsync(setup, CancellationToken.None).GetAwaiter().GetResult();
            if (!created.StoreCreated || !created.PointOfSaleCreated || created.PointOfSale.ExternalId != "LZPIXCAIXA01")
                throw new InvalidOperationException("criacao idempotente de loja e PDV");
            var existing = setupProvider.EnsureInfrastructureAsync(setup, CancellationToken.None).GetAwaiter().GetResult();
            if (existing.StoreCreated || existing.PointOfSaleCreated)
                throw new InvalidOperationException("loja ou PDV duplicado no segundo cadastro");

            // Regressao real: depois de Ready, um health-check 404 mantinha o
            // coordenador permanentemente pronto e nunca voltava ao inventario.
            // A ausencia do PDV deve invalidar apenas a infraestrutura, tentar
            // reconciliar imediatamente e continuar sem duplicar Loja/PDV.
            var recoverySettings = new PixOwnerSettings
            {
                Enabled = true,
                SetupState = "ready",
                Provider = "mercadopago",
                AccountId = "123456",
                StoreExternalId = "LZLOJA01",
                StoreName = "TurboRama Teste",
                PosExternalId = "LZPIXCAIXA01",
                PosName = "TurboRama Kiosk",
                PostalCode = "01001000",
                StreetNumber = "100",
                Reference = "Teste automatizado",
                PackagePricesCents = new Dictionary<int, long>
                {
                    [15] = 750, [30] = 1500, [45] = 2250, [60] = 3000, [120] = 6000
                }
            };
            var recoveryHandler = new FakeMercadoPagoSetupHandler(storeExists: true, posExists: true);
            var recoveryProvider = new MercadoPagoPixProvider(options, secretStore, recoveryHandler);
            var recoveryCoordinator = new OwnerInfrastructureCoordinator(recoverySettings, recoveryProvider, paths);
            if (!recoveryCoordinator.TryEnsureAsync(force: true, CancellationToken.None).GetAwaiter().GetResult()
                || !recoveryCoordinator.Ready)
                throw new InvalidOperationException("preparo do coordenador para regressao 404");
            if (recoveryCoordinator.InvalidateAfterHealthFailure(
                    new MercadoPagoApiException(401, "credencial recusada")) || !recoveryCoordinator.Ready)
                throw new InvalidOperationException("falha nao relacionada invalidou Loja/PDV");
            if (!recoveryCoordinator.InvalidateAfterHealthFailure(
                    new MercadoPagoApiException(404, "PDV LZPIXCAIXA01 nao foi encontrado na conta"))
                || recoveryCoordinator.Ready)
                throw new InvalidOperationException("404 do PDV nao reabriu a reconciliacao");
            if (!recoveryCoordinator.TryEnsureAsync(force: false, CancellationToken.None).GetAwaiter().GetResult()
                || !recoveryCoordinator.Ready || recoveryHandler.StorePostCount != 0 || recoveryHandler.PosPostCount != 0)
                throw new InvalidOperationException("reconciliacao apos 404 duplicou ou nao recuperou Loja/PDV");

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
                || delayed.Store.ExternalId != "LZLOJA01" || delayed.PointOfSale.ExternalId != "LZPIXCAIXA01"
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
                || conflict.PointOfSale.ExternalId != "LZPIXCAIXA01" || conflictSetupHandler.PosPostCount != 1)
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
            if (posOnly.StoreCreated || !posOnly.PointOfSaleCreated || posOnly.PointOfSale.ExternalId != "LZPIXCAIXA01")
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
                new[] { new MercadoPagoPosInfo("654", "LZPIXCAIXA01", "TurboRama Kiosk", "987", "LZLOJA01", "active") });

            // Um User ID ja salvo vincula a maquina em qualquer estado. Nem um
            // cadastro ready nem um pending podem migrar silenciosamente para
            // a conta autenticada por outro Access Token.
            var productionInventory = legacyInventory with { AccountId = "789012" };
            foreach (var linkedSettings in new[]
                     {
                         legacySettings,
                         legacySettings with { SetupState = "pending" }
                     })
            {
                var secondaryAccountRejected = false;
                try
                {
                    OwnerInfrastructureCoordinator.BindAuthenticatedAccount(linkedSettings, productionInventory);
                }
                catch (SecurityException ex) when (ex.Message.Contains("ja vinculada", StringComparison.OrdinalIgnoreCase))
                {
                    secondaryAccountRejected = true;
                }
                if (!secondaryAccountRejected || linkedSettings.AccountId != "123456")
                    throw new InvalidOperationException("conta vinculada foi migrada automaticamente");
            }
            var firstBinding = OwnerInfrastructureCoordinator.BindAuthenticatedAccount(
                legacySettings with { AccountId = "" }, productionInventory);
            if (firstBinding.AccountId != "789012")
                throw new InvalidOperationException("primeiro vinculo Mercado Pago foi recusado");

            if (!OwnerInfrastructureCoordinator.TryResolveExisting(legacySettings, legacyInventory,
                    out var recoveredStore, out var recoveredPoint, out var recoveredAutomatically, out _)
                || !recoveredAutomatically || recoveredStore.ExternalId != "LZLOJA01" || recoveredPoint.ExternalId != "LZPIXCAIXA01")
                throw new InvalidOperationException("recuperacao automatica do PDV existente");

            var externalStoreOnlyLegacyInventory = legacyInventory with
            {
                PointsOfSale = new[]
                {
                    new MercadoPagoPosInfo("654", "LZPIXCAIXA01", "TurboRama Kiosk", "", "LZLOJA01", "active")
                }
            };
            if (!OwnerInfrastructureCoordinator.TryResolveExisting(legacySettings, externalStoreOnlyLegacyInventory,
                    out var externalStoreRecoveredStore, out var externalStoreRecoveredPoint,
                    out var externalStoreRecoveredAutomatically, out _)
                || !externalStoreRecoveredAutomatically
                || externalStoreRecoveredStore.ExternalId != "LZLOJA01"
                || externalStoreRecoveredPoint.ExternalId != "LZPIXCAIXA01")
                throw new InvalidOperationException("daemon nao recuperou PDV associado por external_store_id");

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
                || !screenshotRecovered || screenshotStore.ExternalId != "LZLOJA01" || screenshotPoint.ExternalId != "LZPIXCAIXA01")
                throw new InvalidOperationException("correcao do PDV LZPIXCOMP01 sem consulta de CEP");

            var ambiguousInventory = legacyInventory with
            {
                PointsOfSale = new[]
                {
                    new MercadoPagoPosInfo("654", "LZPIXCAIXA01", "Caixa A", "987", "LZLOJA01", "active"),
                    new MercadoPagoPosInfo("655", "LZPIXCAIXA02", "Caixa B", "987", "LZLOJA01", "active")
                }
            };
            if (OwnerInfrastructureCoordinator.TryResolveExisting(legacySettings, ambiguousInventory,
                    out _, out _, out _, out var ambiguityMessage)
                || !ambiguityMessage.Contains("LZPIXCAIXA01", StringComparison.Ordinal))
                throw new InvalidOperationException("ambiguidade de PDV nao foi bloqueada");
        }
    }

    private static void TestMercadoPagoProvisioning(PixOptions original, PixPaths paths,
        bool credentialDpapiTested)
    {
        const string accountId = "123456";
        const string storeName = "TurboRama Teste";
        const string posName = "TurboRama Kiosk";
        var prices = new Dictionary<int, long>
        {
            [15] = 750, [30] = 1500, [45] = 2250, [60] = 3000, [120] = 6000
        };

        // Regra comercial: cada máquina pode manter somente uma conta
        // Mercado Pago. A troca de token da mesma conta continua permitida;
        // uma segunda conta é recusada antes de tocar no cadastro, segredo ou
        // infraestrutura remota. Adaptador bancário é outro provedor e não
        // deve bloquear o primeiro cadastro Mercado Pago.
        PixOwnerProvisioner.ValidateSingleMercadoPagoAccount(null, accountId);
        PixOwnerProvisioner.ValidateSingleMercadoPagoAccount(accountId, accountId);
        PixOwnerProvisioner.ValidateSingleMercadoPagoAccount(null, accountId);
        var secondaryAccountRejected = false;
        try
        {
            PixOwnerProvisioner.ValidateSingleMercadoPagoAccount("654321", accountId);
        }
        catch (SecurityException ex) when (ex.Message.Contains("conta secundaria", StringComparison.OrdinalIgnoreCase))
        {
            secondaryAccountRejected = true;
        }
        if (!secondaryAccountRejected)
            throw new InvalidOperationException("segunda conta Mercado Pago nao foi recusada");

        PixOwnerProvisioningRequest Request(string environment = "production",
            string storeExternalId = "", string posExternalId = "") => new()
        {
            Provider = "mercadopago",
            MercadoPagoEnvironment = environment,
            StoreName = storeName,
            StoreExternalId = storeExternalId,
            PosName = posName,
            PosExternalId = posExternalId,
            PostalCode = "01001000",
            StreetNumber = "100",
            Reference = "Teste automatizado",
            PackagePricesCents = new Dictionary<int, long>(prices)
        };

        var emptyInventory = new MercadoPagoInfrastructure(accountId,
            Array.Empty<MercadoPagoStoreInfo>(), Array.Empty<MercadoPagoPosInfo>());
        var emptyDecision = PixOwnerProvisioner.DecideMercadoPagoProvisioning(storeName, "", posName, "",
            emptyInventory);
        if (!emptyDecision.CreateStore || !emptyDecision.CreatePointOfSale
            || !emptyDecision.RequireEmptyInventoryBeforeCreation
            || emptyDecision.StoreExternalId.Length is < 1 or > 60
            || emptyDecision.PosExternalId.Length is < 1 or > 40)
            throw new InvalidOperationException("conta vazia nao gerou um plano unico de Loja/PDV");
        var emptyExplicitDecision = PixOwnerProvisioner.DecideMercadoPagoProvisioning(storeName, "LZLOJANOVA",
            posName, "LZPIXNOVO", emptyInventory);
        if (!emptyExplicitDecision.CreateStore || !emptyExplicitDecision.CreatePointOfSale
            || !emptyExplicitDecision.RequireEmptyInventoryBeforeCreation)
            throw new InvalidOperationException("IDs explicitos em conta vazia nao preservaram a barreira de inventario vazio");

        var uniqueStore = new MercadoPagoStoreInfo("987", "LZLOJA01", storeName);
        var uniquePoint = new MercadoPagoPosInfo("654", "LZPIXCAIXA01", posName, uniqueStore.Id,
            uniqueStore.ExternalId, "active");
        var uniqueInventory = new MercadoPagoInfrastructure(accountId,
            new[] { uniqueStore }, new[] { uniquePoint });
        var uniqueDecision = PixOwnerProvisioner.DecideMercadoPagoProvisioning(storeName, "", posName, "",
            uniqueInventory);
        if (uniqueDecision.RequiresRemoteWrite || uniqueDecision.StoreExternalId != "LZLOJA01"
            || uniqueDecision.PosExternalId != "LZPIXCAIXA01")
            throw new InvalidOperationException("par unico compativel nao foi reutilizado sem criacao");

        var externalStoreOnlyInventory = uniqueInventory with
        {
            PointsOfSale = new[]
            {
                uniquePoint with { StoreId = "", ExternalStoreId = uniqueStore.ExternalId }
            }
        };
        var externalStoreOnlyDecision = PixOwnerProvisioner.DecideMercadoPagoProvisioning(storeName, "", posName, "",
            externalStoreOnlyInventory);
        if (externalStoreOnlyDecision.RequiresRemoteWrite
            || externalStoreOnlyDecision.StoreExternalId != "LZLOJA01"
            || externalStoreOnlyDecision.PosExternalId != "LZPIXCAIXA01")
            throw new InvalidOperationException("PDV compativel por external_store_id nao foi reutilizado");

        var statusOmittedDecision = PixOwnerProvisioner.DecideMercadoPagoProvisioning(storeName, "", posName, "",
            uniqueInventory with
            {
                PointsOfSale = new[] { uniquePoint with { Status = "" } }
            });
        if (statusOmittedDecision.RequiresRemoteWrite || statusOmittedDecision.PosExternalId != "LZPIXCAIXA01")
            throw new InvalidOperationException("PDV valido com status omitido nao foi reutilizado");

        var explicitDecision = PixOwnerProvisioner.DecideMercadoPagoProvisioning(storeName, "LZLOJA01",
            posName, "LZPIXCAIXA01", uniqueInventory);
        if (explicitDecision.RequiresRemoteWrite || explicitDecision.Store?.Id != uniqueStore.Id
            || explicitDecision.PointOfSale?.Id != uniquePoint.Id)
            throw new InvalidOperationException("IDs explicitos corretos nao foram validados e reutilizados");

        // Regressao do gabinete: o identificador LZPIXCOMP nao existe mais.
        // Com uma unica loja inequivoca e nenhum outro PDV utilizavel, o
        // configurador deve criar somente um novo PDV, nunca outra loja.
        var legacyOnlyInventory = new MercadoPagoInfrastructure(accountId,
            new[] { uniqueStore },
            new[] { new MercadoPagoPosInfo("653", "LZPIXCOMP", posName, uniqueStore.Id,
                uniqueStore.ExternalId, "active") });
        var repairDecision = PixOwnerProvisioner.DecideMercadoPagoProvisioning(storeName, "LZLOJA01",
            posName, "LZPIXCOMP", legacyOnlyInventory);
        if (repairDecision.CreateStore || !repairDecision.CreatePointOfSale
            || repairDecision.Store?.Id != uniqueStore.Id
            || repairDecision.RequireEmptyInventoryBeforeCreation
            || MercadoPagoOptions.IsLegacyTestExternalPosId(repairDecision.PosExternalId))
            throw new InvalidOperationException("PDV legado nao gerou reparo seguro somente do caixa");

        var storeWithoutPointInventory = legacyOnlyInventory with
        {
            PointsOfSale = Array.Empty<MercadoPagoPosInfo>()
        };
        var storeOnlyDecision = PixOwnerProvisioner.DecideMercadoPagoProvisioning(storeName, "", posName, "",
            storeWithoutPointInventory);
        if (storeOnlyDecision.CreateStore || !storeOnlyDecision.CreatePointOfSale
            || storeOnlyDecision.Store?.Id != uniqueStore.Id)
            throw new InvalidOperationException("loja unica sem PDV nao gerou reparo somente do caixa");

        void RequireMissingExplicitIdRejected(string storeId, string pointId)
        {
            var rejected = false;
            try
            {
                PixOwnerProvisioner.DecideMercadoPagoProvisioning(storeName, storeId, posName, pointId,
                    uniqueInventory);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("conta esta totalmente vazia", StringComparison.OrdinalIgnoreCase))
            {
                rejected = true;
            }
            if (!rejected)
                throw new InvalidOperationException("ID explicito inexistente autorizou criacao em conta povoada");
        }
        RequireMissingExplicitIdRejected("LZLOJA01", "LZPIXINEXISTENTE");
        RequireMissingExplicitIdRejected("LZLOJAINEXISTENTE", "LZPIXINEXISTENTE");
        RequireMissingExplicitIdRejected("", "LZPIXINEXISTENTE");
        RequireMissingExplicitIdRejected("LZLOJAINEXISTENTE", "");

        var ambiguousInventory = new MercadoPagoInfrastructure(accountId,
            new[]
            {
                uniqueStore,
                new MercadoPagoStoreInfo("988", "LZLOJA02", storeName)
            },
            new[]
            {
                uniquePoint,
                    new MercadoPagoPosInfo("655", "LZPIXCOMP02", posName, "988", "LZLOJA02", "active")
            });
        var ambiguityRejected = false;
        try
        {
            PixOwnerProvisioner.DecideMercadoPagoProvisioning(storeName, "", posName, "", ambiguousInventory);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("explicitamente", StringComparison.OrdinalIgnoreCase))
        {
            ambiguityRejected = true;
        }
        if (!ambiguityRejected)
            throw new InvalidOperationException("multiplos pares compativeis nao exigiram selecao explicita");

        foreach (var invalidEnvironment in new[] { "", "staging" })
        {
            var environmentRejected = false;
            try { PixOwnerProvisioner.ValidateMercadoPagoEnvironment(invalidEnvironment); }
            catch (InvalidOperationException) { environmentRejected = true; }
            if (!environmentRejected)
                throw new InvalidOperationException("ambiente Mercado Pago ausente ou desconhecido foi aceito");
        }

        var options = (original with
        {
            Provider = "mercadopago",
            ProductionEnabled = true,
            MercadoPago = new MercadoPagoOptions
            {
                Environment = "production",
                ExternalPosId = emptyDecision.PosExternalId
            }
        }).Normalize();
        var fakeToken = "APP" + "_USR-provisioning-self-test-token-1234567890";
        try
        {
            // Fluxo completo: uma segunda conta deve ser recusada depois de
            // /users/me, mas antes de ler/trocar segredo, cadastro ou enviar
            // qualquer POST para Loja/PDV.
            var oneAccountRoot = Path.Combine(paths.Root, "provisioning-one-account");
            var oneAccountPaths = new PixPaths(oneAccountRoot);
            oneAccountPaths.EnsureDirectories();
            var oneAccountSecret = Encoding.UTF8.GetBytes("ONE-ACCOUNT-SECRET-SENTINEL");
            File.WriteAllBytes(oneAccountPaths.SecretFile, oneAccountSecret);
            var oneAccountOwnerFile = Path.Combine(oneAccountPaths.Root, "owner-settings.json");
            var oneAccountOwner = new PixOwnerSettings
            {
                Enabled = true,
                SetupState = "ready",
                Provider = "mercadopago",
                AccountId = "654321",
                StoreExternalId = "LZLOJA01",
                StoreName = storeName,
                PosExternalId = "LZPIXCOMP",
                PosName = posName,
                PostalCode = "01001000",
                StreetNumber = "100",
                Reference = "Teste automatizado",
                PackagePricesCents = new Dictionary<int, long>(prices)
            };
            oneAccountOwner.Validate();
            File.WriteAllText(oneAccountOwnerFile, JsonSerializer.Serialize(oneAccountOwner, Json.Options), Encoding.UTF8);
            var oneAccountHandler = new FakeMercadoPagoProvisioningHandler(emptyInventory);
            var oneAccountRejected = false;
            try
            {
                PixOwnerProvisioner.ConfigureAsync(Request(), fakeToken, options, oneAccountPaths,
                    new PixSecretStore(oneAccountPaths.SecretFile), CancellationToken.None, oneAccountHandler)
                    .GetAwaiter().GetResult();
            }
            catch (SecurityException ex) when (ex.Message.Contains("conta secundaria", StringComparison.OrdinalIgnoreCase))
            {
                oneAccountRejected = true;
            }
            if (!oneAccountRejected || oneAccountHandler.TotalPostCount != 0
                || !File.ReadAllBytes(oneAccountPaths.SecretFile).SequenceEqual(oneAccountSecret)
                || !File.ReadAllText(oneAccountOwnerFile, Encoding.UTF8).Contains("654321", StringComparison.Ordinal))
                throw new InvalidOperationException("segunda conta alterou cadastro/segredo ou enviou POST");

            // A ausencia real de arquivo ou um accountId explicitamente vazio
            // sao os unicos estados locais tratados como primeiro vinculo.
            var unboundRoot = Path.Combine(paths.Root, "provisioning-unbound-account");
            var unboundPaths = new PixPaths(unboundRoot);
            unboundPaths.EnsureDirectories();
            if (PixOwnerProvisioner.ReadExistingMercadoPagoAccountId(unboundRoot) is not null)
                throw new InvalidOperationException("pasta sem owner-settings foi tratada como conta vinculada");
            File.WriteAllText(Path.Combine(unboundRoot, "owner-settings.json"),
                JsonSerializer.Serialize(oneAccountOwner with { AccountId = "" }, Json.Options), Encoding.UTF8);
            if (PixOwnerProvisioner.ReadExistingMercadoPagoAccountId(unboundRoot) is not null)
                throw new InvalidOperationException("cadastro explicitamente sem conta foi tratado como vinculado");

            // Qualquer arquivo existente que nao prove de forma inequivoca o
            // vinculo anterior deve falhar antes de trocar segredo/cadastro ou
            // criar Loja/PDV. O provider=online legado continua carregando o
            // User ID antigo, portanto tambem recusa uma segunda conta.
            var unsafeOwnerSettings = new (string Name, byte[] Content)[]
            {
                ("legacy-online", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                    oneAccountOwner with { Provider = "online" }, Json.Options))),
                ("malformed", Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"provider\":\"mercadopago\",")),
                ("oversized", Encoding.UTF8.GetBytes(new string('x', 65_537))),
                ("missing-provider", Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"accountId\":\"654321\"}")),
                ("different-provider", Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"provider\":\"adapter\",\"accountId\":\"654321\"}")),
                ("ambiguous-provider", Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"provider\":\"mercadopago\",\"Provider\":\"online\",\"accountId\":\"654321\"}")),
                ("incompatible-schema", Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"provider\":\"mercadopago\",\"accountId\":\"654321\"}"))
            };
            foreach (var unsafeOwner in unsafeOwnerSettings)
            {
                var unsafeRoot = Path.Combine(paths.Root, "provisioning-owner-" + unsafeOwner.Name);
                var unsafePaths = new PixPaths(unsafeRoot);
                unsafePaths.EnsureDirectories();
                var unsafeSecret = Encoding.UTF8.GetBytes("UNSAFE-OWNER-SECRET-SENTINEL");
                File.WriteAllBytes(unsafePaths.SecretFile, unsafeSecret);
                var unsafeOwnerFile = Path.Combine(unsafeRoot, "owner-settings.json");
                File.WriteAllBytes(unsafeOwnerFile, unsafeOwner.Content);
                var unsafeHandler = new FakeMercadoPagoProvisioningHandler(emptyInventory);
                var unsafeRejected = false;
                try
                {
                    PixOwnerProvisioner.ConfigureAsync(Request(), fakeToken, options, unsafePaths,
                        new PixSecretStore(unsafePaths.SecretFile), CancellationToken.None, unsafeHandler)
                        .GetAwaiter().GetResult();
                }
                catch (SecurityException)
                {
                    unsafeRejected = true;
                }
                if (!unsafeRejected || unsafeHandler.TotalPostCount != 0
                    || !File.ReadAllBytes(unsafePaths.SecretFile).SequenceEqual(unsafeSecret)
                    || !File.ReadAllBytes(unsafeOwnerFile).SequenceEqual(unsafeOwner.Content))
                    throw new InvalidOperationException(
                        $"owner-settings {unsafeOwner.Name} alterou cadastro/segredo ou enviou POST");
            }

            var uniqueReadHandler = new FakeMercadoPagoProvisioningHandler(uniqueInventory);
            var uniqueReadOptions = options with
            {
                MercadoPago = options.MercadoPago with { ExternalPosId = uniqueDecision.PosExternalId }
            };
            var uniqueReadProvider = new MercadoPagoPixProvider(uniqueReadOptions,
                new PixSecretStore(paths.SecretFile).WithTransientSecret(fakeToken), uniqueReadHandler);
            var uniqueReadInventory = uniqueReadProvider.GetInfrastructureAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            var uniqueReadDecision = PixOwnerProvisioner.DecideMercadoPagoProvisioning(storeName, "", posName, "",
                uniqueReadInventory);
            uniqueReadProvider.CheckHealthAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (uniqueReadDecision.RequiresRemoteWrite || uniqueReadHandler.TotalPostCount != 0)
                throw new InvalidOperationException("par unico provocou POST durante validacao read-only");

            // Conta realmente vazia: o plano permite exatamente um POST de
            // loja e um de PDV e o provider continua idempotente na repeticao.
            var emptyHandler = new FakeMercadoPagoProvisioningHandler(emptyInventory);
            var emptyProvider = new MercadoPagoPixProvider(options,
                new PixSecretStore(paths.SecretFile).WithTransientSecret(fakeToken), emptyHandler);
            var emptySetup = new MercadoPagoSetupRequest
            {
                ExpectedAccountId = accountId,
                StoreName = storeName,
                StoreExternalId = emptyDecision.StoreExternalId,
                PosName = posName,
                PosExternalId = emptyDecision.PosExternalId,
                StreetName = "Praca da Se",
                StreetNumber = "100",
                CityName = "Sao Paulo",
                StateName = "Sao Paulo",
                Latitude = -23.55052,
                Longitude = -46.633308,
                Reference = "Teste automatizado"
            };
            var created = emptyProvider.EnsureInfrastructureAsync(emptySetup, CancellationToken.None,
                new MercadoPagoCreationPolicy(emptyDecision.CreateStore, emptyDecision.CreatePointOfSale,
                    emptyDecision.RequireEmptyInventoryBeforeCreation))
                .GetAwaiter().GetResult();
            if (!created.StoreCreated || !created.PointOfSaleCreated
                || emptyHandler.StorePostCount != 1 || emptyHandler.PosPostCount != 1)
                throw new InvalidOperationException("conta vazia nao criou exatamente uma Loja e um PDV");
            var repeated = emptyProvider.EnsureInfrastructureAsync(emptySetup, CancellationToken.None,
                new MercadoPagoCreationPolicy(emptyDecision.CreateStore, emptyDecision.CreatePointOfSale,
                    emptyDecision.RequireEmptyInventoryBeforeCreation))
                .GetAwaiter().GetResult();
            if (repeated.StoreCreated || repeated.PointOfSaleCreated
                || emptyHandler.StorePostCount != 1 || emptyHandler.PosPostCount != 1)
                throw new InvalidOperationException("repeticao do plano vazio duplicou Loja/PDV");

            // Executa o fluxo completo na ambiguidade com arquivos sentinela.
            // A recusa deve ocorrer antes de DPAPI, owner-settings e qualquer
            // POST, mesmo que ja exista um cadastro anterior.
            var ambiguousRoot = Path.Combine(paths.Root, "provisioning-ambiguous");
            var ambiguousPaths = new PixPaths(ambiguousRoot);
            ambiguousPaths.EnsureDirectories();
            var secretSentinel = Encoding.UTF8.GetBytes("SECRET-SENTINEL-DO-NOT-REPLACE");
            var ownerSentinel = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                oneAccountOwner with { SetupState = "pending", AccountId = "" }, Json.Options));
            File.WriteAllBytes(ambiguousPaths.SecretFile, secretSentinel);
            var ownerFile = Path.Combine(ambiguousPaths.Root, "owner-settings.json");
            File.WriteAllBytes(ownerFile, ownerSentinel);
            var ambiguousHandler = new FakeMercadoPagoProvisioningHandler(ambiguousInventory);
            ambiguityRejected = false;
            try
            {
                PixOwnerProvisioner.ConfigureAsync(Request(), fakeToken, options, ambiguousPaths,
                    new PixSecretStore(ambiguousPaths.SecretFile), CancellationToken.None, ambiguousHandler)
                    .GetAwaiter().GetResult();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("explicitamente", StringComparison.OrdinalIgnoreCase))
            {
                ambiguityRejected = true;
            }
            if (!ambiguityRejected || ambiguousHandler.TotalPostCount != 0
                || !File.ReadAllBytes(ambiguousPaths.SecretFile).SequenceEqual(secretSentinel)
                || !File.ReadAllBytes(ownerFile).SequenceEqual(ownerSentinel))
                throw new InvalidOperationException("ambiguidade alterou segredo/cadastro ou enviou POST");

            // Um ID digitado incorretamente numa conta ja povoada tambem deve
            // falhar antes de DPAPI, owner-settings e qualquer POST.
            var missingExplicitRoot = Path.Combine(paths.Root, "provisioning-missing-explicit");
            var missingExplicitPaths = new PixPaths(missingExplicitRoot);
            missingExplicitPaths.EnsureDirectories();
            File.WriteAllBytes(missingExplicitPaths.SecretFile, secretSentinel);
            var missingExplicitOwnerFile = Path.Combine(missingExplicitPaths.Root, "owner-settings.json");
            File.WriteAllBytes(missingExplicitOwnerFile, ownerSentinel);
            var missingExplicitHandler = new FakeMercadoPagoProvisioningHandler(uniqueInventory);
            var missingExplicitRejected = false;
            try
            {
                PixOwnerProvisioner.ConfigureAsync(Request("production", "LZLOJA01", "LZPIXINEXISTENTE"),
                    fakeToken, options, missingExplicitPaths,
                    new PixSecretStore(missingExplicitPaths.SecretFile), CancellationToken.None,
                    missingExplicitHandler).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("conta esta totalmente vazia", StringComparison.OrdinalIgnoreCase))
            {
                missingExplicitRejected = true;
            }
            if (!missingExplicitRejected || missingExplicitHandler.TotalPostCount != 0
                || !File.ReadAllBytes(missingExplicitPaths.SecretFile).SequenceEqual(secretSentinel)
                || !File.ReadAllBytes(missingExplicitOwnerFile).SequenceEqual(ownerSentinel))
                throw new InvalidOperationException("ID explicito inexistente alterou segredo/cadastro ou enviou POST");

            // O ambiente tambem e confirmado antes de persistencia. Uma conta
            // test_user jamais pode ser ativada por um request production.
            var mismatchRoot = Path.Combine(paths.Root, "provisioning-environment-mismatch");
            var mismatchPaths = new PixPaths(mismatchRoot);
            mismatchPaths.EnsureDirectories();
            File.WriteAllBytes(mismatchPaths.SecretFile, secretSentinel);
            var mismatchHandler = new FakeMercadoPagoProvisioningHandler(emptyInventory, testAccount: true);
            var mismatchRejected = false;
            try
            {
                PixOwnerProvisioner.ConfigureAsync(Request("production"), fakeToken, options, mismatchPaths,
                    new PixSecretStore(mismatchPaths.SecretFile), CancellationToken.None, mismatchHandler)
                    .GetAwaiter().GetResult();
            }
            catch (SecurityException) { mismatchRejected = true; }
            if (!mismatchRejected || mismatchHandler.TotalPostCount != 0
                || !File.ReadAllBytes(mismatchPaths.SecretFile).SequenceEqual(secretSentinel))
                throw new InvalidOperationException("mismatch production/sandbox nao falhou antes da persistencia");

            var unknownEnvironmentRoot = Path.Combine(paths.Root, "provisioning-environment-unknown");
            var unknownEnvironmentPaths = new PixPaths(unknownEnvironmentRoot);
            unknownEnvironmentPaths.EnsureDirectories();
            File.WriteAllBytes(unknownEnvironmentPaths.SecretFile, secretSentinel);
            var unknownEnvironmentOwnerFile = Path.Combine(unknownEnvironmentPaths.Root, "owner-settings.json");
            File.WriteAllBytes(unknownEnvironmentOwnerFile, ownerSentinel);
            var unknownEnvironmentHandler = new FakeMercadoPagoProvisioningHandler(emptyInventory,
                omitEnvironmentSignal: true);
            var unknownEnvironmentRejected = false;
            try
            {
                PixOwnerProvisioner.ConfigureAsync(Request("production"), fakeToken, options,
                    unknownEnvironmentPaths, new PixSecretStore(unknownEnvironmentPaths.SecretFile),
                    CancellationToken.None, unknownEnvironmentHandler).GetAwaiter().GetResult();
            }
            catch (SecurityException) { unknownEnvironmentRejected = true; }
            if (!unknownEnvironmentRejected || unknownEnvironmentHandler.TotalPostCount != 0
                || !File.ReadAllBytes(unknownEnvironmentPaths.SecretFile).SequenceEqual(secretSentinel)
                || !File.ReadAllBytes(unknownEnvironmentOwnerFile).SequenceEqual(ownerSentinel))
                throw new InvalidOperationException("ambiente desconhecido alterou segredo/cadastro ou enviou POST");

            if (credentialDpapiTested)
            {
                // Par unico: o fluxo completo valida health, grava o novo
                // cadastro local e nao envia nenhum POST de Loja/PDV.
                var uniqueRoot = Path.Combine(paths.Root, "provisioning-unique");
                var uniquePaths = new PixPaths(uniqueRoot);
                uniquePaths.EnsureDirectories();
                var uniqueHandler = new FakeMercadoPagoProvisioningHandler(uniqueInventory);
                var result = PixOwnerProvisioner.ConfigureAsync(Request(), fakeToken, options, uniquePaths,
                    new PixSecretStore(uniquePaths.SecretFile), CancellationToken.None, uniqueHandler)
                    .GetAwaiter().GetResult();
                if (result.StoreExternalId != "LZLOJA01" || result.PosExternalId != "LZPIXCAIXA01"
                    || uniqueHandler.TotalPostCount != 0 || !File.Exists(uniquePaths.SecretFile)
                    || !File.Exists(Path.Combine(uniquePaths.Root, "owner-settings.json")))
                    throw new InvalidOperationException("par unico nao foi provisionado localmente sem POST");
                var savedReady = PixOwnerSettings.LoadIfPresent(uniquePaths.Root);
                if (savedReady is null || !savedReady.SetupState.Equals("ready", StringComparison.Ordinal))
                    throw new InvalidOperationException("cadastro confirmado nao terminou em estado ready");
                var sandboxRequestRoot = Path.Combine(paths.Root, "provisioning-appusr-forces-production");
                var sandboxRequestPaths = new PixPaths(sandboxRequestRoot);
                sandboxRequestPaths.EnsureDirectories();
                var sandboxRequestHandler = new FakeMercadoPagoProvisioningHandler(uniqueInventory);
                var sandboxRequestResult = PixOwnerProvisioner.ConfigureAsync(Request("sandbox"), fakeToken, options,
                    sandboxRequestPaths, new PixSecretStore(sandboxRequestPaths.SecretFile), CancellationToken.None,
                    sandboxRequestHandler).GetAwaiter().GetResult();
                var sandboxRequestSaved = PixOwnerSettings.LoadIfPresent(sandboxRequestPaths.Root);
                if (sandboxRequestResult.StoreExternalId != "LZLOJA01"
                    || sandboxRequestResult.PosExternalId != "LZPIXCAIXA01"
                    || sandboxRequestHandler.TotalPostCount != 0
                    || sandboxRequestSaved is null
                    || !sandboxRequestSaved.MercadoPagoEnvironment.Equals("production", StringComparison.Ordinal)
                    || !sandboxRequestSaved.SetupState.Equals("ready", StringComparison.Ordinal))
                    throw new InvalidOperationException("APP_USR enviado como TESTE nao foi salvo como PRODUCAO");

                // Fluxo completo do erro real LZPIXCOMP: conserva a unica
                // loja existente, cria exatamente um PDV novo e termina ready.
                var repairRoot = Path.Combine(paths.Root, "provisioning-repair-legacy-pos");
                var repairPaths = new PixPaths(repairRoot);
                repairPaths.EnsureDirectories();
                var repairHandler = new FakeMercadoPagoProvisioningHandler(legacyOnlyInventory);
                var repaired = PixOwnerProvisioner.ConfigureAsync(
                    Request("production", "LZLOJA01", "LZPIXCOMP"), fakeToken, options, repairPaths,
                    new PixSecretStore(repairPaths.SecretFile), CancellationToken.None, repairHandler)
                    .GetAwaiter().GetResult();
                if (repaired.StoreExternalId != "LZLOJA01"
                    || MercadoPagoOptions.IsLegacyTestExternalPosId(repaired.PosExternalId)
                    || repairHandler.StorePostCount != 0 || repairHandler.PosPostCount != 1)
                    throw new InvalidOperationException("reparo do PDV legado nao criou somente um caixa novo");
                var repairedReady = PixOwnerSettings.LoadIfPresent(repairPaths.Root);
                if (repairedReady is null || !repairedReady.SetupState.Equals("ready", StringComparison.Ordinal)
                    || MercadoPagoOptions.IsLegacyTestExternalPosId(repairedReady.PosExternalId))
                    throw new InvalidOperationException("reparo do PDV legado nao terminou em cadastro ready");

                // Se o health falhar depois da troca protegida, o arquivo local
                // precisa continuar pending e, portanto, incapaz de liberar compras.
                var healthFailureRoot = Path.Combine(paths.Root, "provisioning-health-failure");
                var healthFailurePaths = new PixPaths(healthFailureRoot);
                healthFailurePaths.EnsureDirectories();
                var healthFailureHandler = new FakeMercadoPagoProvisioningHandler(uniqueInventory,
                    failFilteredHealth: true);
                var healthFailed = false;
                try
                {
                    PixOwnerProvisioner.ConfigureAsync(Request(), fakeToken, options, healthFailurePaths,
                        new PixSecretStore(healthFailurePaths.SecretFile), CancellationToken.None,
                        healthFailureHandler).GetAwaiter().GetResult();
                }
                catch (MercadoPagoApiException) { healthFailed = true; }
                var savedPending = PixOwnerSettings.LoadIfPresent(healthFailurePaths.Root);
                if (!healthFailed || savedPending is null
                    || !savedPending.SetupState.Equals("pending", StringComparison.Ordinal)
                    || healthFailureHandler.TotalPostCount != 0)
                    throw new InvalidOperationException("falha de health nao preservou cadastro pending");

                // O daemon nunca cria um PDV ausente numa conta que ja possui
                // uma Loja; ele permanece bloqueado e exige selecao/reparo.
                var coordinatorRoot = Path.Combine(paths.Root, "provisioning-coordinator-nonempty");
                var coordinatorPaths = new PixPaths(coordinatorRoot);
                coordinatorPaths.EnsureDirectories();
                var storeOnlyInventory = new MercadoPagoInfrastructure(accountId,
                    new[] { uniqueStore }, Array.Empty<MercadoPagoPosInfo>());
                var coordinatorHandler = new FakeMercadoPagoProvisioningHandler(storeOnlyInventory);
                var coordinatorOptions = options with
                {
                    MercadoPago = options.MercadoPago with { ExternalPosId = "LZPIXINEXISTENTE" }
                };
                var coordinatorProvider = new MercadoPagoPixProvider(coordinatorOptions,
                    new PixSecretStore(coordinatorPaths.SecretFile).WithTransientSecret(fakeToken), coordinatorHandler);
                var coordinatorOwner = new PixOwnerSettings
                {
                    Enabled = true,
                    SetupState = "pending",
                    Provider = "mercadopago",
                    MercadoPagoEnvironment = "production",
                    AccountId = accountId,
                    StoreExternalId = uniqueStore.ExternalId,
                    StoreName = storeName,
                    PosExternalId = "LZPIXINEXISTENTE",
                    PosName = posName,
                    PostalCode = "01001000",
                    StreetNumber = "100",
                    Reference = "Teste automatizado",
                    PackagePricesCents = new Dictionary<int, long>(prices)
                };
                var coordinator = new OwnerInfrastructureCoordinator(coordinatorOwner,
                    coordinatorProvider, coordinatorPaths);
                var coordinatorReady = coordinator.TryEnsureAsync(force: true, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (coordinatorReady || coordinator.Ready || coordinatorHandler.TotalPostCount != 0)
                    throw new InvalidOperationException("coordenador criou recurso em inventario nao vazio");
            }
        }
        finally
        {
            foreach (var directory in new[]
            {
                Path.Combine(paths.Root, "provisioning-ambiguous"),
                Path.Combine(paths.Root, "provisioning-missing-explicit"),
                Path.Combine(paths.Root, "provisioning-environment-mismatch"),
                Path.Combine(paths.Root, "provisioning-environment-unknown"),
                Path.Combine(paths.Root, "provisioning-unique"),
                Path.Combine(paths.Root, "provisioning-appusr-forces-production"),
                Path.Combine(paths.Root, "provisioning-repair-legacy-pos"),
                Path.Combine(paths.Root, "provisioning-health-failure"),
                Path.Combine(paths.Root, "provisioning-coordinator-nonempty")
            })
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
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

        {
            var httpProvider = new AdapterPixProvider(options,
                secretStore.WithTransientSecret("adapter-self-test-secret"), new FakeAdapterHandler());
            httpProvider.CheckHealthAsync(CancellationToken.None).GetAwaiter().GetResult();
            var requestNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var request = new PixPurchaseRequest(PixContract.SchemaVersion, "PIXSELFTEST", 15, 750,
                requestNow, requestNow + options.PaymentExpirationMinutes * 60L, "player",
                "player_SELFTEST_0123456789abcdef", "");
            var session = httpProvider.CreateAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            if (session.Provider != "adapter" || session.ProviderOrderId != "BANK-ORDER-1" || session.Status != "pending")
                throw new InvalidOperationException("criacao HTTP do adaptador");
            var refreshed = httpProvider.RefreshAsync(session, CancellationToken.None).GetAwaiter().GetResult();
            if (refreshed?.Status != "approved") throw new InvalidOperationException("confirmacao HTTP do adaptador");
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

    private sealed class FakeMercadoPagoHealthHandler(bool posExists, bool filteredQueryMiss = false, string posStatus = "") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization?.Scheme != "Bearer"
                || request.Headers.Authorization.Parameter != "APP_USR-self-test-token")
                return Task.FromResult(JsonResponse(System.Net.HttpStatusCode.Unauthorized, new { message = "credencial invalida" }));

            var uri = request.RequestUri;
            if (request.Method == HttpMethod.Get && uri?.Host == "api.mercadolibre.com" && uri.AbsolutePath == "/users/me")
                return Task.FromResult(JsonResponse(System.Net.HttpStatusCode.OK,
                    new { id = 123456, test_user = false }));

            if (request.Method == HttpMethod.Get && uri?.Host == "api.mercadopago.com" && uri.AbsolutePath == "/pos")
            {
                var filtered = uri.Query.Contains("external_id=", StringComparison.OrdinalIgnoreCase);
                var results = posExists && !(filteredQueryMiss && filtered)
                    ? new[] { new { id = 123, external_id = "TURBORAMAPDV01", status = posStatus } }
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

    private sealed class FakeMercadoPagoProvisioningHandler : HttpMessageHandler
    {
        private readonly string _accountId;
        private readonly bool _testAccount;
        private readonly bool _omitEnvironmentSignal;
        private readonly bool _failFilteredHealth;
        private readonly List<MercadoPagoStoreInfo> _stores;
        private readonly List<MercadoPagoPosInfo> _points;
        private int _nextStoreId = 2000;
        private int _nextPointId = 3000;

        public FakeMercadoPagoProvisioningHandler(MercadoPagoInfrastructure inventory,
            bool testAccount = false, bool omitEnvironmentSignal = false,
            bool failFilteredHealth = false)
        {
            _accountId = inventory.AccountId;
            _testAccount = testAccount;
            _omitEnvironmentSignal = omitEnvironmentSignal;
            _failFilteredHealth = failFilteredHealth;
            _stores = inventory.Stores.ToList();
            _points = inventory.PointsOfSale.ToList();
        }

        public int StorePostCount { get; private set; }
        public int PosPostCount { get; private set; }
        public int TotalPostCount => StorePostCount + PosPostCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization?.Scheme != "Bearer"
                || request.Headers.Authorization.Parameter != "APP" + "_USR-provisioning-self-test-token-1234567890")
                return JsonResponse(System.Net.HttpStatusCode.Unauthorized, new { message = "credencial invalida" });
            var uri = request.RequestUri;
            var path = uri?.AbsolutePath ?? "";
            if (request.Method == HttpMethod.Get && uri?.Host == "api.mercadolibre.com" && path == "/users/me")
                return _omitEnvironmentSignal
                    ? JsonResponse(System.Net.HttpStatusCode.OK, new { id = _accountId })
                    : JsonResponse(System.Net.HttpStatusCode.OK, new { id = _accountId, test_user = _testAccount });
            if (request.Method == HttpMethod.Get && path == $"/users/{_accountId}/stores/search")
                return JsonResponse(System.Net.HttpStatusCode.OK, new
                {
                    results = _stores.Select(store => new
                    {
                        id = store.Id,
                        external_id = store.ExternalId,
                        name = store.Name
                    }).ToArray()
                });
            if (request.Method == HttpMethod.Get && path == "/pos")
            {
                if (_failFilteredHealth && (uri?.Query ?? "").Contains("external_id=", StringComparison.OrdinalIgnoreCase))
                    return JsonResponse(System.Net.HttpStatusCode.InternalServerError,
                        new { message = "health indisponivel" });
                return JsonResponse(System.Net.HttpStatusCode.OK, new
                {
                    results = _points.Select(point => new
                    {
                        id = point.Id,
                        external_id = point.ExternalId,
                        name = point.Name,
                        store_id = point.StoreId,
                        external_store_id = point.ExternalStoreId,
                        status = point.Status
                    }).ToArray()
                });
            }
            if (request.Method == HttpMethod.Post && path == $"/users/{_accountId}/stores")
            {
                StorePostCount++;
                using var body = JsonDocument.Parse(await (request.Content?.ReadAsStringAsync(cancellationToken)
                    ?? Task.FromResult("{}")));
                var externalId = body.RootElement.GetProperty("external_id").GetString() ?? "";
                var name = body.RootElement.GetProperty("name").GetString() ?? "";
                if (_stores.Any(store => store.ExternalId.Equals(externalId, StringComparison.OrdinalIgnoreCase)))
                    return JsonResponse(System.Net.HttpStatusCode.Conflict, new { message = "store already exists" });
                var store = new MercadoPagoStoreInfo((++_nextStoreId).ToString(CultureInfo.InvariantCulture),
                    externalId, name);
                _stores.Add(store);
                return JsonResponse(System.Net.HttpStatusCode.OK,
                    new { id = store.Id, external_id = store.ExternalId, name = store.Name });
            }
            if (request.Method == HttpMethod.Post && path == "/pos")
            {
                PosPostCount++;
                using var body = JsonDocument.Parse(await (request.Content?.ReadAsStringAsync(cancellationToken)
                    ?? Task.FromResult("{}")));
                var externalId = body.RootElement.GetProperty("external_id").GetString() ?? "";
                var name = body.RootElement.GetProperty("name").GetString() ?? "";
                var storeId = body.RootElement.GetProperty("store_id").GetInt64().ToString(CultureInfo.InvariantCulture);
                var externalStoreId = body.RootElement.GetProperty("external_store_id").GetString() ?? "";
                if (!_stores.Any(store => store.Id.Equals(storeId, StringComparison.Ordinal)
                        && store.ExternalId.Equals(externalStoreId, StringComparison.Ordinal)))
                    return JsonResponse(System.Net.HttpStatusCode.BadRequest, new { message = "store mismatch" });
                if (_points.Any(point => point.ExternalId.Equals(externalId, StringComparison.OrdinalIgnoreCase)))
                    return JsonResponse(System.Net.HttpStatusCode.Conflict, new { message = "point already exists" });
                var point = new MercadoPagoPosInfo((++_nextPointId).ToString(CultureInfo.InvariantCulture),
                    externalId, name, storeId, externalStoreId, "active");
                _points.Add(point);
                return JsonResponse(System.Net.HttpStatusCode.OK, new
                {
                    id = point.Id,
                    external_id = point.ExternalId,
                    name = point.Name,
                    store_id = point.StoreId,
                    external_store_id = point.ExternalStoreId,
                    status = point.Status
                });
            }
            return JsonResponse(System.Net.HttpStatusCode.NotFound, new { message = "rota inexistente" });
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
				return JsonResponse(System.Net.HttpStatusCode.OK, new { id = 123456, test_user = false });
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
                    results = _posExists ? new[] { new { id = 654, external_id = "LZPIXCAIXA01", name = "TurboRama Kiosk", store_id = 987, status = "active" } } : Array.Empty<object>()
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
                if (body.RootElement.GetProperty("external_id").GetString() != "LZPIXCAIXA01"
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
                    id = 654, external_id = "LZPIXCAIXA01", name = "TurboRama Kiosk", store_id = 987, status = "active"
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
    public static bool IsValidBeneficiary(string? type, string? id)
        => type is "player" or "guest" && !string.IsNullOrWhiteSpace(id)
            && id.Length is >= 16 and <= 128
            && id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
}

// A configuracao de login do Launcher faz parte do perimetro da DPAPI: se a
// ponte for inicializada por outra identidade, ela pode publicar uma chave
// publica/ACL que o verdadeiro quiosque nunca conseguira abrir. Esta classe
// valida tres fontes independentes antes de qualquer escrita sensivel:
//  1) kioskUser do JSON do Launcher;
//  2) AutoAdminLogon/DefaultUserName do Winlogon em HKLM;
//  3) o SID real do processo atual, que deve ser exatamente o usuario local
//     habilitado configurado nas duas fontes anteriores. O gabinete comercial
//     tambem pode usar a conta local Admin como quiosque; nesse caso aceitamos
//     o grupo Administrators sem ampliar a autorizacao para outro SID.
static class KioskProcessIdentity
{
    private const string LauncherConfig = @"C:\TurboRama\Config\turborama.json";
    private const string WinlogonPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    private const string ReenrollmentDirectory = @"C:\TurboRama\State";
    private const string ReenrollmentMarker = "pix-identity-reenrollment-required.json";
    private const uint UfAccountDisable = 0x0002;
    private const int NerrSuccess = 0;
    private const int LgIncludeIndirect = 1;
    private const int MaxPreferredLength = -1;
    private const FileSystemRights MarkerWriteRights = FileSystemRights.WriteData | FileSystemRights.AppendData
        | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions
        | FileSystemRights.TakeOwnership;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct UserInfo1
    {
        public string? Name;
        public string? Password;
        public uint PasswordAge;
        public uint Privilege;
        public string? HomeDirectory;
        public string? Comment;
        public uint Flags;
        public string? ScriptPath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LocalGroupUsersInfo0
    {
        public IntPtr Name;
    }

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserGetInfo(string? serverName, string userName, int level, out IntPtr buffer);
    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserGetLocalGroups(string? serverName, string userName, int level, int flags,
        out IntPtr buffer, int preferredMaximumLength, out int entriesRead, out int totalEntries);
    [DllImport("Netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    internal static void RequireTrustedKioskProcess()
    {
        var validation = Validate();
        if (validation.Trusted) return;
        throw new SecurityException("Identidade Windows do quiosque recusada: " + validation.Reason);
    }

    internal static bool TryValidateCurrent(out string reason)
    {
        var validation = Validate();
        reason = validation.Trusted ? "" : validation.Reason;
        return validation.Trusted;
    }

    private static KioskIdentityValidation Validate()
    {
        if (!OperatingSystem.IsWindows())
            return KioskIdentityValidation.Failed("o agente PIX comercial exige Windows para validar a identidade do quiosque");
        try
        {
            var kioskUser = ReadKioskUser();
            var kiosk = ResolveLocalUser(kioskUser);
            if (!IsEnabled(kiosk))
                return KioskIdentityValidation.Failed("a conta kioskUser local esta desabilitada");

            using var winlogon = Registry.LocalMachine.OpenSubKey(WinlogonPath, writable: false);
            if (winlogon is null)
                return KioskIdentityValidation.Failed("a configuracao Winlogon local nao esta acessivel");
            var autoLogon = RegistryString(winlogon, "AutoAdminLogon");
            var defaultUser = RegistryString(winlogon, "DefaultUserName");
            var defaultDomain = RegistryString(winlogon, "DefaultDomainName");
            if (!autoLogon.Equals("1", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(defaultUser))
                return KioskIdentityValidation.Failed("AutoAdminLogon/DefaultUserName nao confirmam o quiosque");

            var winlogonAccount = string.IsNullOrWhiteSpace(defaultDomain) || defaultDomain.Equals(".", StringComparison.Ordinal)
                ? defaultUser : $"{defaultDomain}\\{defaultUser}";
            var winlogonUser = ResolveLocalUser(winlogonAccount);
            if (!winlogonUser.Sid.Equals(kiosk.Sid))
                return KioskIdentityValidation.Failed("kioskUser do Launcher diverge do usuario configurado no Winlogon");
            if (!IsEnabled(winlogonUser))
                return KioskIdentityValidation.Failed("o usuario local do Winlogon esta desabilitado");

            var current = WindowsIdentity.GetCurrent();
            var currentSid = current.User;
            if (currentSid is null || !currentSid.Equals(kiosk.Sid))
                return KioskIdentityValidation.Failed("o processo PIX nao esta executando sob o SID local do quiosque");
            return KioskIdentityValidation.Ok();
        }
        catch (Exception)
        {
            return KioskIdentityValidation.Failed("a identidade do quiosque nao pode ser validada");
        }
    }

    // Exposto ao auto-teste somente para testar o contrato estrito do JSON,
    // sem consultar HKLM ou a identidade da maquina de compilacao.
    internal static string ParseKioskUserJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 1_048_576)
            throw new InvalidOperationException("arquivo kioskUser invalido");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("o JSON do Launcher deve ser um objeto");
        string? kioskUser = null;
        var found = 0;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!property.NameEquals("kioskUser")) continue;
            found++;
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("kioskUser deve ser uma string");
            kioskUser = property.Value.GetString();
        }
        if (found != 1 || string.IsNullOrWhiteSpace(kioskUser) || kioskUser.Length > 256
            || !kioskUser.Equals(kioskUser.Trim(), StringComparison.Ordinal)
            || kioskUser.Any(char.IsControl))
            throw new InvalidOperationException("kioskUser ausente, duplicado ou invalido");
        return kioskUser;
    }

    private static string ReadKioskUser()
    {
        AssertNoReparsePath(LauncherConfig, file: true);
        var info = new FileInfo(LauncherConfig);
        if (!info.Exists || info.Length is < 2 or > 1_048_576)
            throw new InvalidOperationException("o JSON do Launcher nao esta disponivel");
        return ParseKioskUserJson(File.ReadAllText(LauncherConfig, Encoding.UTF8));
    }

    private static void AssertNoReparsePath(string path, bool file)
    {
        var current = file ? Path.GetDirectoryName(Path.GetFullPath(path)) : Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(current)) throw new InvalidOperationException("caminho local invalido");
        while (!string.IsNullOrWhiteSpace(current))
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new SecurityException("redirecionamento de filesystem recusado na configuracao do quiosque");
            var parent = Directory.GetParent(current);
            if (parent is null || parent.FullName.Equals(current, StringComparison.OrdinalIgnoreCase)) break;
            current = parent.FullName;
        }
        if (file && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new SecurityException("redirecionamento de arquivo recusado na configuracao do quiosque");
    }

    private static KioskLocalUser ResolveLocalUser(string account)
    {
        var sid = (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
        var translated = (NTAccount)sid.Translate(typeof(NTAccount));
        var value = translated.Value;
        var separator = value.IndexOf('\\');
        if (separator <= 0 || separator == value.Length - 1)
            throw new SecurityException("a conta kioskUser nao pode ser resolvida como usuario local");
        var domain = value[..separator];
        var name = value[(separator + 1)..];
        if (!domain.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            throw new SecurityException("kioskUser precisa ser um usuario local desta maquina");
        return new KioskLocalUser(sid, name);
    }

    private static bool IsEnabled(KioskLocalUser user)
    {
        var status = NetUserGetInfo(null, user.Name, 1, out var buffer);
        if (status != NerrSuccess || buffer == IntPtr.Zero)
        {
            if (buffer != IntPtr.Zero) NetApiBufferFree(buffer);
            throw new SecurityException("o estado da conta local kioskUser nao pode ser consultado");
        }
        try
        {
            var info = Marshal.PtrToStructure<UserInfo1>(buffer);
            return (info.Flags & UfAccountDisable) == 0;
        }
        finally { NetApiBufferFree(buffer); }
    }

    private static bool IsLocalAdministrator(KioskLocalUser user)
    {
        var status = NetUserGetLocalGroups(null, user.Name, 0, LgIncludeIndirect, out var buffer,
            MaxPreferredLength, out var entriesRead, out _);
        if (status != NerrSuccess)
        {
            if (buffer != IntPtr.Zero) NetApiBufferFree(buffer);
            throw new SecurityException("os grupos locais de kioskUser nao podem ser consultados");
        }
        try
        {
            var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var administratorsAccount = (NTAccount)administratorsSid.Translate(typeof(NTAccount));
            var administratorsValue = administratorsAccount.Value;
            var separator = administratorsValue.LastIndexOf('\\');
            var administratorsName = separator >= 0 ? administratorsValue[(separator + 1)..] : administratorsValue;
            var groupNames = new List<string>(entriesRead);
            for (var index = 0; index < entriesRead; index++)
            {
                var entry = Marshal.PtrToStructure<LocalGroupUsersInfo0>(IntPtr.Add(buffer, index * IntPtr.Size));
                var groupName = Marshal.PtrToStringUni(entry.Name);
                if (string.IsNullOrWhiteSpace(groupName)) throw new SecurityException("grupo local invalido para kioskUser");
                groupNames.Add(groupName);
            }
            // NetUserGetLocalGroups devolve nomes de grupos BUILTIN sem
            // dominio (por exemplo "Users"). Prefixa-los com MachineName
            // gerava IdentityNotMappedException e bloqueava qualquer conta
            // kiosk valida. Comparamos somente com o nome localizado obtido do
            // SID well-known de Administrators; os demais grupos nao precisam
            // nem devem ser traduzidos.
            return GroupListContainsAdministrator(groupNames, administratorsName);
        }
        finally { if (buffer != IntPtr.Zero) NetApiBufferFree(buffer); }
    }

    internal static bool GroupListContainsAdministrator(IEnumerable<string> groupNames, string localizedAdministratorName)
        => !string.IsNullOrWhiteSpace(localizedAdministratorName)
            && groupNames.Any(group => group.Equals(localizedAdministratorName, StringComparison.OrdinalIgnoreCase));

    private static string RegistryString(RegistryKey key, string name)
        => (key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "").Trim();

    private static void TryWriteReenrollmentMarker()
    {
        // O marcador e somente orientativo: a decisao de bloquear nao depende
        // dele. Nao criamos diretorios e usamos CreateNew, portanto uma conta
        // errada nunca sobrescreve um arquivo existente durante essa falha.
        try
        {
            AssertNoReparsePath(ReenrollmentDirectory, file: false);
            if (!Directory.Exists(ReenrollmentDirectory)) return;
            if (!MarkerDirectoryAllowsOnlyTrustedWriters(ReenrollmentDirectory)) return;
            var marker = Path.Combine(ReenrollmentDirectory, ReenrollmentMarker);
            if (File.Exists(marker)) return;
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                state = "identity_reenrollment_required",
                detectedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                action = "Abra o TurboRama na conta Windows configurada no TurboRama/Winlogon e recadastre a ponte PIX."
            }, Json.Options);
            using var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.WriteThrough);
            stream.Write(payload);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or ArgumentException or NotSupportedException)
        {
            // A mensagem PIX_IDENTITY_REENROLLMENT_REQUIRED ainda e emitida;
            // nunca abrimos a ponte PIX somente para forcar o marcador.
        }
    }

    private static bool MarkerDirectoryAllowsOnlyTrustedWriters(string directory)
    {
        var security = new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Access);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true,
            targetType: typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & MarkerWriteRights) == 0)
                continue;
            if (rule.IdentityReference is not SecurityIdentifier sid
                || (!sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
                    && !sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)))
                return false;
        }
        return true;
    }

    private sealed record KioskLocalUser(SecurityIdentifier Sid, string Name);
    private sealed record KioskIdentityValidation(bool Trusted, string Reason)
    {
        public static KioskIdentityValidation Ok() => new(true, "");
        public static KioskIdentityValidation Failed(string reason) => new(false, reason);
    }
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
                throw new SecurityException($"Windows recusou proteger ACL de {Path.GetFileName(path)} (codigo {result}).");
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
