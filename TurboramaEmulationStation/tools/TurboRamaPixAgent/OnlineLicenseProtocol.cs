using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

enum OnlineProtectionProfile
{
    TpmBound,
    UsbTokenBound,
    SoftwareBoundOnline
}

static class OnlineProtectionProfileCodec
{
    public static string Format(OnlineProtectionProfile profile) => profile switch
    {
        OnlineProtectionProfile.TpmBound => "TPM_BOUND",
        OnlineProtectionProfile.UsbTokenBound => "USB_TOKEN_BOUND",
        OnlineProtectionProfile.SoftwareBoundOnline => "SOFTWARE_BOUND_ONLINE",
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    public static OnlineProtectionProfile Parse(string? value) => (value ?? "").Trim().ToUpperInvariant() switch
    {
        "TPM_BOUND" => OnlineProtectionProfile.TpmBound,
        "USB_TOKEN_BOUND" => OnlineProtectionProfile.UsbTokenBound,
        "SOFTWARE_BOUND_ONLINE" => OnlineProtectionProfile.SoftwareBoundOnline,
        _ => throw new SecurityException("O perfil de protecao on-line e invalido.")
    };
}

sealed record OnlineDeviceDescriptor(
    int SchemaVersion,
    string DeviceId,
    string BindingType,
    string Algorithm,
    string PublicKeySpki,
    string HardwareFingerprint,
    string AgentVersion);

sealed record OnlineActivationChallengeRequest(
    int SchemaVersion,
    string LicenseId,
    string ActivationCode,
    OnlineDeviceDescriptor Device);

sealed record OnlineChallengeRequest(
    int SchemaVersion,
    string LicenseId,
    string DeviceId,
    string SessionId,
    string Action,
    string ContextHash);

sealed record OnlineChallengeResponse(
    int SchemaVersion,
    string ChallengeId,
    string Nonce,
    long ExpiresAtUnixSeconds);

sealed record OnlineActivationProof(
    int SchemaVersion,
    string LicenseId,
    string ChallengeId,
    OnlineDeviceDescriptor Device,
    string Signature);

sealed record OnlineOperationProof(
    int SchemaVersion,
    string LicenseId,
    string DeviceId,
    string SessionId,
    string Action,
    string ContextHash,
    string ChallengeId,
    string Signature);

sealed record OnlinePaymentCreateProof(OnlineOperationProof Proof, OnlinePaymentCreateContext Context);
sealed record OnlinePaymentReadProof(OnlineOperationProof Proof, OnlinePaymentReadContext Context);
sealed record OnlineSessionProof(OnlineOperationProof Proof, OnlineSessionContext Context);
sealed record OnlineConfigurationReadProof(OnlineOperationProof Proof, OnlineConfigurationReadContext Context);
sealed record OnlineConfigurationWriteProof(OnlineOperationProof Proof, OnlineConfigurationWriteContext Context);

sealed record OnlineActivationResult(
    int SchemaVersion,
    string Status,
    string DeviceId,
    string BindingType);

sealed record OnlineSessionContext(
    int SchemaVersion,
    string SessionId,
    string HardwareFingerprint,
    string AgentVersion);

sealed record OnlinePaymentCreateContext(
    int SchemaVersion,
    string SessionId,
    string ExternalReference,
    long AmountCents,
    string Currency,
    int Minutes,
    long RequestExpiresAtUnixSeconds,
    int PaymentExpiresInSeconds);

sealed record OnlinePaymentReadContext(
    int SchemaVersion,
    string SessionId,
    string ExternalReference,
    string ProviderOrderId,
    long AmountCents,
    string Currency);

sealed record OnlineConfigurationReadContext(
    int SchemaVersion,
    string SessionId,
    long KnownVersion);

sealed record OnlineConfigurationWriteContext(
    int SchemaVersion,
    string SessionId,
    long ExpectedVersion,
    Dictionary<int, long> PackagePricesCents);

sealed record OnlinePriceConfigurationResponse(
    int SchemaVersion,
    string LicenseId,
    long Version,
    bool PixEnabled,
    Dictionary<int, long> PackagePricesCents,
    long UpdatedAtUnixSeconds);

sealed record OnlineOrderResponse(
    int SchemaVersion,
    string ProviderId,
    string ExternalReference,
    long AmountCents,
    string Currency,
    string ProviderOrderId,
    string QrData,
    string Status);

sealed record OnlineErrorResponse(int SchemaVersion, string Code, string Message);
sealed record OnlineMercadoPagoEnrollmentRequest(int SchemaVersion, string CustomerId,
    string EnrollmentCode, string ExternalPosId, string AccessToken);
sealed record OnlineMercadoPagoEnrollmentResult(int SchemaVersion, string Status, string CustomerId,
    string ExternalPosId);

static class OnlineLicenseProtocol
{
    public const int SchemaVersion = 1;
    public const string SigningAlgorithm = "rsa-pss-sha256";
    public const int MaximumBodyBytes = 64 * 1024;
    private static readonly byte[] SigningDomain = Encoding.ASCII.GetBytes("TurboRamaOnlineMachineProof/v1\0");
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.Default,
        Indented = false,
        SkipValidation = false
    };

    public static string DeviceIdFromSpki(ReadOnlySpan<byte> spki)
    {
        if (spki.Length is < 256 or > 4096)
            throw new SecurityException("A chave publica da maquina possui tamanho invalido.");
        return Convert.ToHexString(SHA256.HashData(spki)).ToLowerInvariant();
    }

    public static byte[] ParseAndValidateSpki(OnlineDeviceDescriptor descriptor)
    {
        ValidateDescriptorShape(descriptor);
        byte[] spki;
        try { spki = Convert.FromBase64String(descriptor.PublicKeySpki); }
        catch (FormatException ex) { throw new SecurityException("A chave publica da maquina e invalida.", ex); }
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(spki, out var consumed);
            if (consumed != spki.Length || rsa.KeySize < 2048 || rsa.KeySize > 4096)
                throw new SecurityException("A chave publica da maquina nao e RSA compativel.");
            var canonical = rsa.ExportSubjectPublicKeyInfo();
            try
            {
                if (!canonical.AsSpan().SequenceEqual(spki))
                    throw new SecurityException("A chave publica da maquina nao usa SPKI DER canonico.");
            }
            finally { CryptographicOperations.ZeroMemory(canonical); }
            var actual = DeviceIdFromSpki(spki);
            if (!FixedHexEquals(actual, descriptor.DeviceId))
                throw new SecurityException("O DeviceId nao corresponde a chave publica informada.");
            return spki;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(spki);
            throw;
        }
    }

    public static string ContextHash(OnlineSessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return HashCanonical(writer => WriteSessionContext(writer, context));
    }

    public static string ContextHash(OnlinePaymentCreateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return HashCanonical(writer => WritePaymentCreateContext(writer, context));
    }

    public static string ContextHash(OnlinePaymentReadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return HashCanonical(writer => WritePaymentReadContext(writer, context));
    }

    public static string ContextHash(OnlineConfigurationReadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return HashCanonical(writer => WriteConfigurationReadContext(writer, context));
    }

    public static string ContextHash(OnlineConfigurationWriteContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return HashCanonical(writer => WriteConfigurationWriteContext(writer, context));
    }

    public static string ActivationContextHash(string licenseId, OnlineDeviceDescriptor device)
        => HashCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("licenseId", RequireIdentifier(licenseId, "LicenseId", 6, 64));
            writer.WritePropertyName("device");
            WriteDevice(writer, device);
            writer.WriteEndObject();
        });

    public static byte[] BuildSigningMessage(OnlineChallengeResponse challenge, string licenseId,
        string deviceId, string sessionId, string action, string contextHash)
    {
        ValidateChallenge(challenge);
        var canonical = CanonicalBytes(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("challengeId", RequireHex(challenge.ChallengeId, "ChallengeId", 64));
            writer.WriteString("nonce", RequireBase64(challenge.Nonce, "nonce", 32, 64));
            writer.WriteNumber("expiresAtUnixSeconds", challenge.ExpiresAtUnixSeconds);
            writer.WriteString("licenseId", RequireIdentifier(licenseId, "LicenseId", 6, 64));
            writer.WriteString("deviceId", RequireHex(deviceId, "DeviceId", 64));
            writer.WriteString("sessionId", RequireHex(sessionId, "SessionId", 64, allowEmpty: true));
            writer.WriteString("action", RequireAction(action));
            writer.WriteString("contextHash", RequireHex(contextHash, "ContextHash", 64));
            writer.WriteEndObject();
        });
        var output = new byte[SigningDomain.Length + canonical.Length];
        SigningDomain.CopyTo(output, 0);
        canonical.CopyTo(output, SigningDomain.Length);
        CryptographicOperations.ZeroMemory(canonical);
        return output;
    }

    public static bool VerifyProof(OnlineDeviceDescriptor descriptor, OnlineChallengeResponse challenge,
        string licenseId, string sessionId, string action, string contextHash, string signatureBase64)
    {
        var spki = ParseAndValidateSpki(descriptor);
        byte[] signature = Array.Empty<byte>();
        byte[] message = Array.Empty<byte>();
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
            if (signature.Length is < 256 or > 512) return false;
            message = BuildSigningMessage(challenge, licenseId, descriptor.DeviceId, sessionId, action, contextHash);
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(spki, out var consumed);
            return consumed == spki.Length
                && rsa.VerifyData(message, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (FormatException) { return false; }
        catch (CryptographicException) { return false; }
        finally
        {
            CryptographicOperations.ZeroMemory(spki);
            if (signature.Length != 0) CryptographicOperations.ZeroMemory(signature);
            if (message.Length != 0) CryptographicOperations.ZeroMemory(message);
        }
    }

    public static void ValidateChallenge(OnlineChallengeResponse challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        if (challenge.SchemaVersion != SchemaVersion)
            throw new SecurityException("A versao do desafio on-line e invalida.");
        RequireHex(challenge.ChallengeId, "ChallengeId", 64);
        RequireBase64(challenge.Nonce, "nonce", 32, 64);
        if (challenge.ExpiresAtUnixSeconds < 1)
            throw new SecurityException("A expiracao do desafio on-line e invalida.");
    }

    public static void ValidateDescriptorShape(OnlineDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.SchemaVersion != SchemaVersion)
            throw new SecurityException("A versao da identidade da maquina e invalida.");
        RequireHex(descriptor.DeviceId, "DeviceId", 64);
        _ = OnlineProtectionProfileCodec.Parse(descriptor.BindingType);
        if (!string.Equals(descriptor.Algorithm, SigningAlgorithm, StringComparison.Ordinal))
            throw new SecurityException("O algoritmo da identidade da maquina e invalido.");
        RequireHex(descriptor.HardwareFingerprint, "HardwareFingerprint", 64);
        var agentVersion = descriptor.AgentVersion ?? "";
        if (agentVersion.Length is < 1 or > 64
            || agentVersion.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+')))
            throw new SecurityException("A versao declarada do agente e invalida.");
        var publicKey = descriptor.PublicKeySpki ?? "";
        if (publicKey.Length is < 300 or > 8192 || publicKey.Any(char.IsWhiteSpace))
            throw new SecurityException("A chave publica da maquina e invalida.");
    }

    public static string RequireIdentifier(string? value, string label, int minimum, int maximum)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length < minimum || normalized.Length > maximum
            || normalized.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new SecurityException($"{label} possui formato invalido.");
        return normalized;
    }

    public static string RequireHex(string? value, string label, int length, bool allowEmpty = false)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        if (allowEmpty && normalized.Length == 0) return "";
        if (normalized.Length != length || normalized.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')))
            throw new SecurityException($"{label} possui formato invalido.");
        return normalized;
    }

    public static bool FixedHexEquals(string left, string right)
    {
        try
        {
            var a = Encoding.ASCII.GetBytes(RequireHex(left, "hash", 64));
            var b = Encoding.ASCII.GetBytes(RequireHex(right, "hash", 64));
            try { return CryptographicOperations.FixedTimeEquals(a, b); }
            finally { CryptographicOperations.ZeroMemory(a); CryptographicOperations.ZeroMemory(b); }
        }
        catch (SecurityException) { return false; }
    }

    private static string HashCanonical(Action<Utf8JsonWriter> write)
    {
        var bytes = CanonicalBytes(write);
        try { return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static byte[] CanonicalBytes(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions)) write(writer);
        return stream.ToArray();
    }

    private static void WriteDevice(Utf8JsonWriter writer, OnlineDeviceDescriptor device)
    {
        ValidateDescriptorShape(device);
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", device.SchemaVersion);
        writer.WriteString("deviceId", device.DeviceId.ToLowerInvariant());
        writer.WriteString("bindingType", OnlineProtectionProfileCodec.Format(
            OnlineProtectionProfileCodec.Parse(device.BindingType)));
        writer.WriteString("algorithm", device.Algorithm);
        writer.WriteString("publicKeySpki", device.PublicKeySpki);
        writer.WriteString("hardwareFingerprint", device.HardwareFingerprint.ToLowerInvariant());
        writer.WriteString("agentVersion", device.AgentVersion);
        writer.WriteEndObject();
    }

    private static void WriteSessionContext(Utf8JsonWriter writer, OnlineSessionContext context)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", context.SchemaVersion);
        writer.WriteString("sessionId", RequireHex(context.SessionId, "SessionId", 64));
        writer.WriteString("hardwareFingerprint", RequireHex(context.HardwareFingerprint, "HardwareFingerprint", 64));
        writer.WriteString("agentVersion", RequireIdentifier((context.AgentVersion ?? "").Replace('.', '-').Replace('+', '-'), "AgentVersion", 1, 64));
        writer.WriteEndObject();
    }

    private static void WritePaymentCreateContext(Utf8JsonWriter writer, OnlinePaymentCreateContext context)
    {
        if (context.SchemaVersion != SchemaVersion || context.AmountCents is < 1 or > 100_000_000
            || context.Minutes is < 1 or > 480 || context.RequestExpiresAtUnixSeconds < 1
            || context.PaymentExpiresInSeconds is < 60 or > 3600)
            throw new SecurityException("O contexto da cobranca on-line e invalido.");
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", context.SchemaVersion);
        writer.WriteString("sessionId", RequireHex(context.SessionId, "SessionId", 64));
        writer.WriteString("externalReference", RequireIdentifier(context.ExternalReference, "ExternalReference", 4, 64));
        writer.WriteNumber("amountCents", context.AmountCents);
        writer.WriteString("currency", context.Currency == "BRL" ? "BRL" : throw new SecurityException("A moeda da cobranca deve ser BRL."));
        writer.WriteNumber("minutes", context.Minutes);
        writer.WriteNumber("requestExpiresAtUnixSeconds", context.RequestExpiresAtUnixSeconds);
        writer.WriteNumber("paymentExpiresInSeconds", context.PaymentExpiresInSeconds);
        writer.WriteEndObject();
    }

    private static void WritePaymentReadContext(Utf8JsonWriter writer, OnlinePaymentReadContext context)
    {
        if (context.SchemaVersion != SchemaVersion || context.AmountCents is < 1 or > 100_000_000)
            throw new SecurityException("O contexto da consulta on-line e invalido.");
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", context.SchemaVersion);
        writer.WriteString("sessionId", RequireHex(context.SessionId, "SessionId", 64));
        writer.WriteString("externalReference", RequireIdentifier(context.ExternalReference, "ExternalReference", 4, 64));
        writer.WriteString("providerOrderId", RequireIdentifier(context.ProviderOrderId, "ProviderOrderId", 1, 128));
        writer.WriteNumber("amountCents", context.AmountCents);
        writer.WriteString("currency", context.Currency == "BRL" ? "BRL" : throw new SecurityException("A moeda da consulta deve ser BRL."));
        writer.WriteEndObject();
    }

    private static void WriteConfigurationReadContext(Utf8JsonWriter writer,
        OnlineConfigurationReadContext context)
    {
        if (context.SchemaVersion != SchemaVersion || context.KnownVersion < 0)
            throw new SecurityException("O contexto de leitura da configuracao e invalido.");
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", context.SchemaVersion);
        writer.WriteString("sessionId", RequireHex(context.SessionId, "SessionId", 64));
        writer.WriteNumber("knownVersion", context.KnownVersion);
        writer.WriteEndObject();
    }

    private static void WriteConfigurationWriteContext(Utf8JsonWriter writer,
        OnlineConfigurationWriteContext context)
    {
        if (context.SchemaVersion != SchemaVersion || context.ExpectedVersion < 0)
            throw new SecurityException("O contexto de escrita da configuracao e invalido.");
        ValidatePackagePrices(context.PackagePricesCents);
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", context.SchemaVersion);
        writer.WriteString("sessionId", RequireHex(context.SessionId, "SessionId", 64));
        writer.WriteNumber("expectedVersion", context.ExpectedVersion);
        writer.WritePropertyName("packagePricesCents");
        writer.WriteStartObject();
        foreach (var minutes in new[] { 15, 30, 45, 60, 120 })
            writer.WriteNumber(minutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                context.PackagePricesCents[minutes]);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    public static void ValidatePackagePrices(IReadOnlyDictionary<int, long>? prices)
    {
        var required = new[] { 15, 30, 45, 60, 120 };
        if (prices is null || prices.Count != required.Length
            || required.Any(minutes => !prices.TryGetValue(minutes, out var cents)
                || cents is < 50 or > 100_000_000))
            throw new SecurityException("A tabela de precos da configuracao e invalida.");
    }

    private static string RequireAction(string? action) => action switch
    {
        "device.activate" or "session.open" or "session.heartbeat" or "payment.create" or "payment.read"
            or "configuration.read" or "configuration.write" => action,
        _ => throw new SecurityException("A acao on-line e invalida.")
    };

    private static string RequireBase64(string? value, string label, int minimumBytes, int maximumBytes)
    {
        var text = value ?? "";
        if (text.Any(char.IsWhiteSpace)) throw new SecurityException($"{label} possui formato invalido.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(text); }
        catch (FormatException ex) { throw new SecurityException($"{label} possui formato invalido.", ex); }
        try
        {
            if (bytes.Length < minimumBytes || bytes.Length > maximumBytes || Convert.ToBase64String(bytes) != text)
                throw new SecurityException($"{label} possui formato invalido.");
            return text;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
