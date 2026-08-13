using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static class OnlineProtocolSelfTest
{
    public static void Run(PixOptions baseOptions)
    {
        var localOwner = new PixOwnerSettings
        {
            Enabled = true,
            SetupState = "ready",
            Provider = "adapter",
            AdapterBaseUrl = "http://127.0.0.1:8765/",
            AdapterProviderId = "banco-local",
            PackagePricesCents = new Dictionary<int, long>
            {
                [15] = 750, [30] = 1_500, [45] = 2_250, [60] = 3_000, [120] = 6_000
            }
        };
        var initialOwner = new OnlineOwnerConfiguration
        {
            BaseUrl = "https://licensing.example.test/",
            LicenseId = "TR-SELFTEST-001",
            ProtectionProfile = "SOFTWARE_BOUND_ONLINE"
        }.ToOwnerSettings(localOwner, baseOptions);
        if (!initialOwner.OnlineLicensingEnabled || initialOwner.Provider != "adapter"
            || initialOwner.AdapterProviderId != "banco-local"
            || initialOwner.PackagePricesCents[15] != 750
            || initialOwner.OnlineConfigurationPending)
            throw new InvalidOperationException("o licenciamento alterou o provedor ou os precos locais");

        using var identity = new TestIdentity();
        using var handler = new TestServerHandler(identity.Descriptor);
        var options = baseOptions with
        {
            Provider = "mercadopago",
            OnlineLicensingEnabled = true,
            ProductionEnabled = true,
            Online = new OnlinePixOptions
            {
                BaseUrl = "https://127.0.0.1:54321/",
                LicenseId = "TR-SELFTEST",
                ProtectionProfile = "SOFTWARE_BOUND_ONLINE",
                ProviderId = "turborama-online"
            }
        };
        if (MachineBindingFactory.Create(options) is not SoftwareCngMachineBinding)
            throw new InvalidOperationException("perfil SOFTWARE_BOUND_ONLINE tentou usar outro vinculo local");
        var license = new OnlineLicenseClient(options, handler, identity);
        license.ActivateAsync("SELFTEST-ACTIVATION-CODE-123456", CancellationToken.None).GetAwaiter().GetResult();
        license.CheckHealthAsync(CancellationToken.None).GetAwaiter().GetResult();
        license.CheckHealthAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (handler.Activations != 1 || handler.Sessions != 2)
            throw new InvalidOperationException("contagem do protocolo de licenciamento on-line");

        using var uncertainIdentity = new TestIdentity();
        using var uncertainHandler = new TestServerHandler(uncertainIdentity.Descriptor,
            makeCompletionIndeterminate: true);
        var uncertainProvider = new OnlineLicenseClient(options, uncertainHandler, uncertainIdentity);
        var indeterminateDetected = false;
        try
        {
            uncertainProvider.ActivateAsync("SELFTEST-ACTIVATION-CODE-123456", CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (OnlineActivationIndeterminateException) { indeterminateDetected = true; }
        if (!indeterminateDetected || uncertainHandler.Activations != 1)
            throw new InvalidOperationException("ativacao aceita sem resposta nao foi marcada como inconclusiva");
    }

    private sealed class TestIdentity : IOnlineMachineIdentity, IDisposable
    {
        private readonly RSA _key = RSA.Create(2048);
        public OnlineDeviceDescriptor Descriptor { get; }

        public TestIdentity()
        {
            var spki = _key.ExportSubjectPublicKeyInfo();
            try
            {
                Descriptor = new OnlineDeviceDescriptor(1, OnlineLicenseProtocol.DeviceIdFromSpki(spki),
                    "SOFTWARE_BOUND_ONLINE", OnlineLicenseProtocol.SigningAlgorithm,
                    Convert.ToBase64String(spki), new string('b', 64), "25.0.0.0");
            }
            finally { CryptographicOperations.ZeroMemory(spki); }
        }

        public OnlineDeviceDescriptor Describe() => Descriptor;

        public string Sign(OnlineChallengeResponse challenge, string licenseId, string sessionId,
            string action, string contextHash)
        {
            var message = OnlineLicenseProtocol.BuildSigningMessage(challenge, licenseId,
                Descriptor.DeviceId, sessionId, action, contextHash);
            var signature = _key.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            try { return Convert.ToBase64String(signature); }
            finally { CryptographicOperations.ZeroMemory(message); CryptographicOperations.ZeroMemory(signature); }
        }

        public void Dispose() => _key.Dispose();
    }

    private sealed class TestServerHandler(OnlineDeviceDescriptor descriptor,
        bool makeCompletionIndeterminate = false) : HttpMessageHandler
    {
        private readonly Dictionary<string, (OnlineChallengeResponse Challenge, string LicenseId,
            string DeviceId, string SessionId, string Action, string ContextHash)> _challenges = new();
        public int Activations { get; private set; }
        public int Sessions { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var route = request.RequestUri?.AbsolutePath.TrimStart('/') ?? "";
            var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            try
            {
                return route switch
                {
                    "v1/activations/challenge" => ActivationChallenge(bytes),
                    "v1/activations/complete" => ActivationComplete(bytes),
                    "v1/challenges" => OperationChallenge(bytes),
                    "v1/sessions" => Session(bytes),
                    _ => Response(HttpStatusCode.NotFound, new OnlineErrorResponse(1, "NOT_FOUND", "not found"))
                };
            }
            finally { if (bytes.Length != 0) CryptographicOperations.ZeroMemory(bytes); }
        }

        private HttpResponseMessage ActivationChallenge(byte[] bytes)
        {
            var request = Read<OnlineActivationChallengeRequest>(bytes);
            if (request.LicenseId != "TR-SELFTEST" || request.ActivationCode != "SELFTEST-ACTIVATION-CODE-123456"
                || request.Device.DeviceId != descriptor.DeviceId) return Denied();
            var hash = OnlineLicenseProtocol.ActivationContextHash(request.LicenseId, request.Device);
            return Response(HttpStatusCode.OK, NewChallenge(request.LicenseId, request.Device.DeviceId,
                "", "device.activate", hash));
        }

        private HttpResponseMessage ActivationComplete(byte[] bytes)
        {
            var proof = Read<OnlineActivationProof>(bytes);
            var hash = OnlineLicenseProtocol.ActivationContextHash(proof.LicenseId, proof.Device);
            if (!Verify(proof.ChallengeId, proof.LicenseId, proof.Device.DeviceId, "", "device.activate",
                    hash, proof.Signature)) return Denied();
            Activations++;
            if (makeCompletionIndeterminate)
                return Response(HttpStatusCode.ServiceUnavailable,
                    new OnlineErrorResponse(1, "ONLINE_RETRY", "temporary"));
            return Response(HttpStatusCode.OK, new OnlineActivationResult(1, "ACTIVE",
                descriptor.DeviceId, descriptor.BindingType));
        }

        private HttpResponseMessage OperationChallenge(byte[] bytes)
        {
            var request = Read<OnlineChallengeRequest>(bytes);
            return Response(HttpStatusCode.OK, NewChallenge(request.LicenseId, request.DeviceId,
                request.SessionId, request.Action, request.ContextHash));
        }

        private HttpResponseMessage Session(byte[] bytes)
        {
            var request = Read<OnlineSessionProof>(bytes);
            var hash = OnlineLicenseProtocol.ContextHash(request.Context);
            if (!Verify(request.Proof.ChallengeId, request.Proof.LicenseId, request.Proof.DeviceId,
                    request.Proof.SessionId, request.Proof.Action, hash, request.Proof.Signature)) return Denied();
            Sessions++;
            return Response(HttpStatusCode.OK, new OnlineActivationResult(1, "ACTIVE",
                descriptor.DeviceId, descriptor.BindingType));
        }

        private OnlineChallengeResponse NewChallenge(string licenseId, string deviceId, string sessionId,
            string action, string contextHash)
        {
            var challenge = new OnlineChallengeResponse(1,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds());
            _challenges.Add(challenge.ChallengeId, (challenge, licenseId, deviceId, sessionId, action, contextHash));
            return challenge;
        }

        private bool Verify(string challengeId, string licenseId, string deviceId, string sessionId,
            string action, string contextHash, string signature)
        {
            if (!_challenges.Remove(challengeId, out var stored)
                || stored.LicenseId != licenseId || stored.DeviceId != deviceId
                || stored.SessionId != sessionId || stored.Action != action
                || !OnlineLicenseProtocol.FixedHexEquals(stored.ContextHash, contextHash)) return false;
            return OnlineLicenseProtocol.VerifyProof(descriptor, stored.Challenge, licenseId,
                sessionId, action, contextHash, signature);
        }

        private static T Read<T>(byte[] bytes)
            => JsonSerializer.Deserialize<T>(bytes, Json.Options) ?? throw new JsonException("request vazia");

        private static HttpResponseMessage Denied()
            => Response(HttpStatusCode.Forbidden, new OnlineErrorResponse(1, "DENIED", "denied"));

        private static HttpResponseMessage Response<T>(HttpStatusCode status, T value)
            => new(status)
            {
                Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value, Json.Options))
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") }
                }
            };
    }
}
