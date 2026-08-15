using System.Net.Http.Json;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

sealed record OnlineOwnerConfiguration
{
    public int SchemaVersion { get; init; } = 1;
    public string BaseUrl { get; init; } = "";
    public string LicenseId { get; init; } = "";
    public string ProtectionProfile { get; init; } = "SOFTWARE_BOUND_ONLINE";

    public static OnlineOwnerConfiguration Load(string file)
    {
        var full = Path.GetFullPath(file);
        var info = new FileInfo(full);
        if (!info.Exists || info.Length is < 16 or > 32 * 1024
            || (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new SecurityException("O arquivo de configuracao on-line e invalido.");
        var value = JsonSerializer.Deserialize<OnlineOwnerConfiguration>(File.ReadAllText(full, Encoding.UTF8), Json.Options)
            ?? throw new JsonException("A configuracao on-line esta vazia.");
        value.Validate();
        return value;
    }

    public void Validate()
    {
        if (SchemaVersion != 1) throw new SecurityException("A versao da configuracao on-line e invalida.");
        new OnlinePixOptions
        {
            BaseUrl = BaseUrl,
            LicenseId = LicenseId,
            ProtectionProfile = ProtectionProfile,
            ProviderId = "turborama-online"
        }.Normalize().Validate(configurationOnly: false);
    }

    public PixOwnerSettings ToOwnerSettings(PixOwnerSettings? existing, PixOptions baseOptions)
    {
        Validate();
        var normalized = new OnlinePixOptions
        {
            BaseUrl = BaseUrl,
            LicenseId = LicenseId,
            ProtectionProfile = ProtectionProfile,
            ProviderId = "turborama-online"
        }.Normalize();
        // Campos locais antigos sao preservados apenas para manutencao e
        // migracao. Quando OnlineLicensingEnabled=true, o daemon nao os usa:
        // toda nova cobranca e criada no servidor.
        var preserved = existing ?? new PixOwnerSettings
        {
            SchemaVersion = 1,
            Enabled = baseOptions.Provider is "mercadopago" or "adapter"
                && baseOptions.IsProviderConfigured(),
            SetupState = baseOptions.IsProviderConfigured() ? "ready" : "pending",
            Provider = baseOptions.Provider == "adapter" ? "adapter" : "mercadopago",
            MercadoPagoEnvironment = baseOptions.MercadoPago.Environment,
            PosExternalId = baseOptions.MercadoPago.ExternalPosId,
            AdapterBaseUrl = baseOptions.Adapter.BaseUrl,
            AdapterProviderId = baseOptions.Adapter.ProviderId,
            PackagePricesCents = new Dictionary<int, long>(baseOptions.PackagePricesCents)
        };
        return preserved with
        {
            OnlineLicensingEnabled = true,
            OnlineBaseUrl = normalized.BaseUrl,
            OnlineLicenseId = normalized.LicenseId,
            OnlineProtectionProfile = normalized.ProtectionProfile,
            OnlineConfigurationPending = false
        };
    }
}

sealed record OnlinePixOptions
{
    public string BaseUrl { get; init; } = "https://licensing.example.invalid/";
    public string LicenseId { get; init; } = "CONFIGURE-A-LICENCA";
    public string ProtectionProfile { get; init; } = "SOFTWARE_BOUND_ONLINE";
    public string ProviderId { get; init; } = "turborama-online";

    public OnlinePixOptions Normalize()
    {
        var baseUrl = (BaseUrl ?? "").Trim();
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        return this with
        {
            BaseUrl = baseUrl,
            LicenseId = (LicenseId ?? "").Trim(),
            ProtectionProfile = OnlineProtectionProfileCodec.Format(
                OnlineProtectionProfileCodec.Parse(ProtectionProfile)),
            ProviderId = (ProviderId ?? "").Trim().ToLowerInvariant()
        };
    }

    public void Validate(bool configurationOnly)
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Online.BaseUrl deve ser uma URL HTTPS absoluta, sem credencial, consulta ou fragmento.");
        _ = OnlineProtectionProfileCodec.Parse(ProtectionProfile);
        if (!configurationOnly)
        {
            OnlineLicenseProtocol.RequireIdentifier(LicenseId, "LicenseId", 6, 64);
            if (LicenseId.Equals("CONFIGURE-A-LICENCA", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Online.LicenseId ainda nao foi configurado.");
        }
        if (!ProviderId.Equals("turborama-online", StringComparison.Ordinal))
            throw new SecurityException("Online.ProviderId deve permanecer turborama-online.");
    }

    public Uri BaseUri()
    {
        Validate(configurationOnly: false);
        return new Uri(BaseUrl, UriKind.Absolute);
    }
}

sealed class OnlineApiException : Exception
{
    public OnlineApiException(int statusCode, string code, string message) : base(message)
        => (StatusCode, Code) = (statusCode, code);
    public int StatusCode { get; }
    public string Code { get; }
}

// A solicitacao final de ativacao pode ter chegado ao servidor mesmo quando a
// resposta se perde no caminho. Esse caso nao pode ser tratado como uma recusa
// comum: o cliente precisa tentar abrir uma sessao com a mesma chave antes de
// decidir se restaura a configuracao anterior.
sealed class OnlineActivationIndeterminateException : Exception
{
    public OnlineActivationIndeterminateException(string message, Exception? inner = null)
        : base(message, inner) { }
}

// O servidor e a autoridade da licenca e tambem da criacao/consulta de cada
// cobranca. O cliente nunca recebe a credencial bancaria: ele prova a posse da
// chave desta maquina e recebe somente os dados publicos da cobranca.
sealed class OnlineLicenseClient
{
    private readonly OnlinePixOptions _online;
    private readonly IOnlineMachineIdentity _identity;
    private readonly HttpClient _http;
    private readonly string _sessionId;
    private bool _sessionOpen;
    public OnlineLicenseClient(PixOptions options, HttpMessageHandler? handler = null,
        IOnlineMachineIdentity? identity = null)
    {
        _online = options.Online.Normalize();
        _online.Validate(configurationOnly: false);
        _identity = identity ?? new CngOnlineMachineIdentity(
            OnlineProtectionProfileCodec.Parse(_online.ProtectionProfile));
        _sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.BaseAddress = _online.BaseUri();
        _http.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TurboRamaPixAgent/25-online");
    }

    public async Task ActivateAsync(string activationCode, CancellationToken token)
    {
        var code = (activationCode ?? "").Trim();
        if (code.Length is < 16 or > 128 || code.Any(char.IsWhiteSpace))
            throw new SecurityException("O codigo de ativacao on-line possui formato invalido.");
        var device = _identity.Describe();
        var challenge = await PostAsync<OnlineActivationChallengeRequest, OnlineChallengeResponse>(
            "v1/activations/challenge",
            new OnlineActivationChallengeRequest(OnlineLicenseProtocol.SchemaVersion,
                _online.LicenseId, code, device), token);
        var contextHash = OnlineLicenseProtocol.ActivationContextHash(_online.LicenseId, device);
        var signature = _identity.Sign(challenge, _online.LicenseId, "", "device.activate", contextHash);
        OnlineActivationResult result;
        try
        {
            result = await PostAsync<OnlineActivationProof, OnlineActivationResult>(
                "v1/activations/complete",
                new OnlineActivationProof(OnlineLicenseProtocol.SchemaVersion, _online.LicenseId,
                    challenge.ChallengeId, device, signature), token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            || ex is OnlineApiException { StatusCode: >= 500 })
        {
            throw new OnlineActivationIndeterminateException(
                "A resposta final da ativacao nao chegou. A identidade sera conferida antes de qualquer restauracao.", ex);
        }
        if (!result.Status.Equals("ACTIVE", StringComparison.Ordinal)
            || !OnlineLicenseProtocol.FixedHexEquals(result.DeviceId, device.DeviceId)
            || !result.BindingType.Equals(device.BindingType, StringComparison.Ordinal))
            throw new OnlineActivationIndeterminateException(
                "O servidor respondeu, mas a confirmacao final da identidade ficou inconclusiva.");
    }

    public async Task CheckHealthAsync(CancellationToken token)
    {
        try
        {
            var device = _identity.Describe();
            var action = _sessionOpen ? "session.heartbeat" : "session.open";
            var context = new OnlineSessionContext(OnlineLicenseProtocol.SchemaVersion, _sessionId,
                device.HardwareFingerprint, device.AgentVersion);
            var contextHash = OnlineLicenseProtocol.ContextHash(context);
            var proof = await CreateProofAsync(action, contextHash, device, token);
            var result = await PostAsync<OnlineSessionProof, OnlineActivationResult>("v1/sessions",
                new OnlineSessionProof(proof, context), token);
            if (!result.Status.Equals("ACTIVE", StringComparison.Ordinal)
                || !OnlineLicenseProtocol.FixedHexEquals(result.DeviceId, device.DeviceId))
                throw new SecurityException("O servidor nao confirmou a sessao deste quiosque.");
            _sessionOpen = true;
        }
        catch (OnlineApiException ex) when (_sessionOpen && ex.StatusCode is 403 or 409)
        {
            // O painel pode encerrar a sessao ao bloquear o PIX ou exigir nova
            // autenticacao. Na proxima rodada abrimos uma sessao nova, sem
            // ficar presos enviando heartbeat de uma sessao ja revogada.
            _sessionOpen = false;
            throw;
        }
    }

    public async Task<OnlineOrderResponse> CreateOrderAsync(PixPurchaseRequest request,
        int paymentExpirationMinutes, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await EnsureSessionAsync(token);
            var device = _identity.Describe();
            var context = new OnlinePaymentCreateContext(OnlineLicenseProtocol.SchemaVersion,
                _sessionId, request.Id, request.AmountCents, "BRL", request.Minutes,
                request.ExpiresAtUnixSeconds, checked(Math.Clamp(paymentExpirationMinutes, 1, 60) * 60));
            var contextHash = OnlineLicenseProtocol.ContextHash(context);
            var proof = await CreateProofAsync("payment.create", contextHash, device, token);
            var result = await PostAsync<OnlinePaymentCreateProof, OnlineOrderResponse>("v1/orders",
                new OnlinePaymentCreateProof(proof, context), token);
            ValidateOrder(result, request.Id, request.AmountCents, expectedOrderId: null,
                requireQr: true, allowedStatuses: ["pending"]);
            return result;
        }
        catch (OnlineApiException ex) when (ex.StatusCode is 403 or 409)
        {
            _sessionOpen = false;
            throw;
        }
    }

    public async Task<OnlineOrderResponse> ReadOrderAsync(PixSession session,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        try
        {
            await EnsureSessionAsync(token);
            var device = _identity.Describe();
            var context = new OnlinePaymentReadContext(OnlineLicenseProtocol.SchemaVersion,
                _sessionId, session.Id, session.ProviderOrderId, session.AmountCents, "BRL");
            var contextHash = OnlineLicenseProtocol.ContextHash(context);
            var proof = await CreateProofAsync("payment.read", contextHash, device, token);
            var result = await PostAsync<OnlinePaymentReadProof, OnlineOrderResponse>("v1/orders/status",
                new OnlinePaymentReadProof(proof, context), token);
            ValidateOrder(result, session.Id, session.AmountCents, session.ProviderOrderId,
                requireQr: false, allowedStatuses: ["pending", "approved", "cancelled"]);
            return result;
        }
        catch (OnlineApiException ex) when (ex.StatusCode is 403 or 409)
        {
            _sessionOpen = false;
            throw;
        }
    }

    private async Task EnsureSessionAsync(CancellationToken token)
    {
        if (!_sessionOpen) await CheckHealthAsync(token);
    }

    private void ValidateOrder(OnlineOrderResponse result, string externalReference,
        long amountCents, string? expectedOrderId, bool requireQr,
        IReadOnlyCollection<string> allowedStatuses)
    {
        if (result.SchemaVersion != OnlineLicenseProtocol.SchemaVersion
            || !result.ProviderId.Equals(_online.ProviderId, StringComparison.Ordinal)
            || !result.ExternalReference.Equals(externalReference, StringComparison.Ordinal)
            || result.AmountCents != amountCents || result.Currency != "BRL"
            || !PixId.IsValidProviderOrder(result.ProviderOrderId)
            || (expectedOrderId is not null
                && !result.ProviderOrderId.Equals(expectedOrderId, StringComparison.Ordinal))
            || !allowedStatuses.Contains(result.Status, StringComparer.Ordinal)
            || (requireQr && (string.IsNullOrWhiteSpace(result.QrData)
                || result.QrData.Length is < 20 or > 4096))
            || (!requireQr && result.QrData.Length > 4096))
            throw new SecurityException("O servidor retornou uma cobranca PIX divergente do pedido assinado.");
    }

    private async Task<OnlineOperationProof> CreateProofAsync(string action, string contextHash,
        OnlineDeviceDescriptor device, CancellationToken token)
    {
        var challenge = await PostAsync<OnlineChallengeRequest, OnlineChallengeResponse>("v1/challenges",
            new OnlineChallengeRequest(OnlineLicenseProtocol.SchemaVersion, _online.LicenseId,
                device.DeviceId, _sessionId, action, contextHash), token);
        // O servidor e a autoridade do prazo do nonce. Comparar aqui com o
        // relogio do gabinete causava falso "desafio expirado" quando o Windows
        // estava alguns segundos fora de sincronia.
        var signature = _identity.Sign(challenge, _online.LicenseId, _sessionId, action, contextHash);
        return new OnlineOperationProof(OnlineLicenseProtocol.SchemaVersion, _online.LicenseId,
            device.DeviceId, _sessionId, action, contextHash, challenge.ChallengeId, signature);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string route, TRequest body,
        CancellationToken token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(body, options: Json.Options)
        };
        using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
        var bytes = await ReadBoundedAsync(response.Content, OnlineLicenseProtocol.MaximumBodyBytes, token);
        try
        {
            if (bytes.Length < 2)
                throw new OnlineApiException(502, "INVALID_RESPONSE", "O servidor on-line retornou uma resposta invalida.");
            if (!response.IsSuccessStatusCode)
            {
                OnlineErrorResponse? error;
                try { error = JsonSerializer.Deserialize<OnlineErrorResponse>(bytes, Json.Options); }
                catch (JsonException)
                {
                    throw new OnlineApiException((int)response.StatusCode, "ONLINE_DENIED",
                        "Nao foi possivel validar esta instalacao. Codigo: TR-ACT-104.");
                }
                throw new OnlineApiException((int)response.StatusCode, error?.Code ?? "ONLINE_DENIED",
                    SafeMessage(error?.Message));
            }
            try
            {
                return JsonSerializer.Deserialize<TResponse>(bytes, Json.Options)
                    ?? throw new JsonException("resposta vazia");
            }
            catch (JsonException ex)
            {
                throw new OnlineApiException(502, "INVALID_RESPONSE",
                    "O servidor on-line retornou dados invalidos: " + SafeMessage(ex.Message));
            }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximumBytes,
        CancellationToken token)
    {
        await using var input = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var remaining = maximumBytes + 1 - checked((int)output.Length);
                if (remaining <= 0)
                    throw new OnlineApiException(502, "INVALID_RESPONSE", "A resposta on-line excedeu o limite permitido.");
                var read = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), token);
                if (read == 0) return output.ToArray();
                output.Write(buffer, 0, read);
                if (output.Length > maximumBytes)
                    throw new OnlineApiException(502, "INVALID_RESPONSE", "A resposta on-line excedeu o limite permitido.");
            }
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    private static string SafeMessage(string? message)
    {
        var value = string.IsNullOrWhiteSpace(message)
            ? "Nao foi possivel validar esta instalacao. Codigo: TR-ACT-104."
            : new string(message.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return value.Length > 200 ? value[..200] : value;
    }
}

sealed class OnlineServerPixProvider : IPixProvider
{
    private readonly PixOptions _options;
    private readonly OnlineLicenseClient _client;

    public OnlineServerPixProvider(PixOptions options, OnlineLicenseClient client)
        => (_options, _client) = (options, client);

    public string Name => "turborama-online";

    public Task CheckHealthAsync(CancellationToken token) => _client.CheckHealthAsync(token);

    public async Task<PixSession> CreateAsync(PixPurchaseRequest request, CancellationToken token)
    {
        var order = await _client.CreateOrderAsync(request, _options.PaymentExpirationMinutes, token);
        return PixSession.Pending(request, Name, order.ProviderOrderId, order.QrData);
    }

    public async Task<PixSession?> RefreshAsync(PixSession session, CancellationToken token)
    {
        if (!session.Provider.Equals(Name, StringComparison.Ordinal))
            throw new SecurityException("A sessao PIX pertence a outro provedor.");
        var order = await _client.ReadOrderAsync(session, token);
        return session with { Status = order.Status, UpdatedAt = DateTimeOffset.UtcNow };
    }
}
