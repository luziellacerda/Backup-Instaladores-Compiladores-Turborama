using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

// Offline commercial licensing core.
//
// A commercial build embeds only an authorized certificate/SPKI. The private
// key remains in the owner's certificate store, token or HSM. A license binds
// that issuer signature to the non-exportable machine key exposed through
// IPixMachineBinding. Copying the program, vault and license to another PC
// therefore does not authorize that PC.
//
// This file intentionally has no startup or UI integration. Program.cs can
// call CommercialLicenseVerifier and CommercialLicenseSelfTest after the
// concurrent audit of the main agent is complete.

enum CommercialLicenseValidationState
{
    Valid,
    Missing,
    InvalidFormat,
    UntrustedIssuer,
    InvalidSignature,
    WrongProduct,
    MissingFeature,
    NotYetValid,
    Expired,
    WrongMachine,
    MachineBindingUnavailable,
    ReadFailure
}

sealed record CommercialLicenseValidationResult(
    CommercialLicenseValidationState State,
    string Message,
    string LicenseId = "")
{
    public bool IsValid => State == CommercialLicenseValidationState.Valid;

    public static CommercialLicenseValidationResult Valid(string licenseId)
        => new(CommercialLicenseValidationState.Valid, "Licenca comercial valida para este quiosque.", licenseId);

    public static CommercialLicenseValidationResult Failed(
        CommercialLicenseValidationState state, string message)
        => new(state, message);
}

sealed record CommercialLicensePolicy(
    string Product,
    int ProductMajor,
    string RequiredFeature,
    long AllowedClockSkewSeconds = 300)
{
    public void Validate()
    {
        CommercialLicenseCodec.RequireIdentifier(Product, 2, 64, "produto", requireLowercase: false);
        CommercialLicenseCodec.RequireIdentifier(RequiredFeature, 2, 64, "recurso", requireLowercase: true);
        if (ProductMajor is < 1 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(ProductMajor), "A versao principal do produto e invalida.");
        if (AllowedClockSkewSeconds is < 0 or > 86_400)
            throw new ArgumentOutOfRangeException(nameof(AllowedClockSkewSeconds), "A tolerancia de relogio e invalida.");
    }
}

sealed record CommercialLicensePayload(
    int SchemaVersion,
    string Kind,
    string LicenseId,
    string ActivationRequestId,
    string Product,
    int ProductMajor,
    string MachineKeySha256,
    IReadOnlyList<string> Features,
    long IssuedAtUnixSeconds,
    long? NotAfterUnixSeconds);

sealed record CommercialLicenseEnvelope(
    int SchemaVersion,
    string Algorithm,
    string IssuerSpkiSha256,
    byte[] Payload,
    byte[] Signature);

sealed record CommercialActivationRequest(
    int SchemaVersion,
    string Kind,
    string RequestId,
    string Product,
    int ProductMajor,
    string MachineKeySha256,
    long GeneratedAtUnixSeconds);

sealed class CommercialLicenseTrustedIssuer
{
    private enum IssuerKeyKind { Rsa, Ecdsa }

    private readonly byte[] _subjectPublicKeyInfo;
    private readonly IssuerKeyKind _keyKind;

    private CommercialLicenseTrustedIssuer(byte[] subjectPublicKeyInfo, IssuerKeyKind keyKind)
    {
        _subjectPublicKeyInfo = subjectPublicKeyInfo;
        _keyKind = keyKind;
        SpkiSha256 = CommercialLicenseCodec.Sha256Hex(_subjectPublicKeyInfo);
    }

    public string SpkiSha256 { get; }

    public byte[] ExportSubjectPublicKeyInfo() => (byte[])_subjectPublicKeyInfo.Clone();

    public static CommercialLicenseTrustedIssuer FromCertificate(ReadOnlySpan<byte> certificateDer)
    {
        if (certificateDer.IsEmpty || certificateDer.Length > 64 * 1024)
            throw new CryptographicException("O certificado publico da licenca e invalido.");
        var encoded = certificateDer.ToArray();
        try
        {
            if (X509Certificate2.GetCertContentType(encoded) != X509ContentType.Cert)
                throw new SecurityException("Somente certificado DER publico pode ser incorporado ao produto.");
            using var certificate = new X509Certificate2(encoded);
            if (certificate.HasPrivateKey)
                throw new SecurityException("Somente o certificado publico pode ser incorporado ao produto.");
            using var rsa = certificate.GetRSAPublicKey();
            if (rsa is not null)
                return FromSubjectPublicKeyInfo(rsa.ExportSubjectPublicKeyInfo());

            using var ecdsa = certificate.GetECDsaPublicKey();
            if (ecdsa is not null)
                return FromSubjectPublicKeyInfo(ecdsa.ExportSubjectPublicKeyInfo());

            throw new CryptographicException("O certificado da licenca nao possui chave RSA ou ECDSA compativel.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
        }
    }

    // Intended integration point for a generated CommercialLicenseBuildIdentity
    // class. Its constant contains public DER only; appsettings and customer
    // files must never select or replace the trusted issuer at runtime.
    public static CommercialLicenseTrustedIssuer FromCertificateBase64(string certificateDerBase64)
    {
        if (string.IsNullOrEmpty(certificateDerBase64) || certificateDerBase64.Length > 96 * 1024
            || certificateDerBase64.Any(char.IsWhiteSpace))
            throw new FormatException("O certificado publico incorporado nao usa base64 canonico.");
        byte[] certificateDer;
        try { certificateDer = Convert.FromBase64String(certificateDerBase64); }
        catch (FormatException ex)
        {
            throw new FormatException("O certificado publico incorporado nao usa base64 valido.", ex);
        }
        try
        {
            if (Convert.ToBase64String(certificateDer) != certificateDerBase64)
                throw new FormatException("O certificado publico incorporado nao usa base64 canonico.");
            return FromCertificate(certificateDer);
        }
        finally
        {
            // Public material is not secret, but avoid retaining an unnecessary
            // duplicate after the SPKI has been extracted and copied.
            CryptographicOperations.ZeroMemory(certificateDer);
        }
    }

    public static CommercialLicenseTrustedIssuer FromSubjectPublicKeyInfo(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        if (subjectPublicKeyInfo.IsEmpty || subjectPublicKeyInfo.Length > 16 * 1024)
            throw new CryptographicException("A chave publica da licenca e invalida.");

        var encoded = subjectPublicKeyInfo.ToArray();
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(encoded, out var consumed);
            if (consumed != encoded.Length || rsa.KeySize < 2048)
                throw new CryptographicException("A chave RSA da licenca e incompleta ou possui menos de 2048 bits.");
            var canonical = rsa.ExportSubjectPublicKeyInfo();
            try
            {
                if (!canonical.AsSpan().SequenceEqual(encoded))
                    throw new CryptographicException("A chave RSA da licenca nao usa SPKI DER canonico.");
            }
            finally { CryptographicOperations.ZeroMemory(canonical); }
            return new CommercialLicenseTrustedIssuer(encoded, IssuerKeyKind.Rsa);
        }
        catch (CryptographicException)
        {
            // Try ECDSA below. Import errors are intentionally not exposed to
            // callers because the public key is not a secret and only the
            // supported algorithm matters here.
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(encoded, out var consumed);
            var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
            var curveOid = parameters.Curve.Oid.Value ?? "";
            if (consumed != encoded.Length
                || curveOid is not ("1.2.840.10045.3.1.7" or "1.3.132.0.34" or "1.3.132.0.35")
                || ecdsa.KeySize is not (256 or 384 or 521))
                throw new CryptographicException("A chave ECDSA da licenca possui curva nao suportada.");
            var canonical = ecdsa.ExportSubjectPublicKeyInfo();
            try
            {
                if (!canonical.AsSpan().SequenceEqual(encoded))
                    throw new CryptographicException("A chave ECDSA da licenca nao usa SPKI DER canonico.");
            }
            finally { CryptographicOperations.ZeroMemory(canonical); }
            return new CommercialLicenseTrustedIssuer(encoded, IssuerKeyKind.Ecdsa);
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(encoded);
            throw new CryptographicException("A chave publica da licenca nao e RSA/ECDSA suportada.", ex);
        }
    }

    internal bool Verify(string algorithm, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        try
        {
            if (_keyKind == IssuerKeyKind.Rsa
                && algorithm.Equals(CommercialLicenseCodec.RsaAlgorithm, StringComparison.Ordinal))
            {
                using var rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(_subjectPublicKeyInfo, out var consumed);
                return consumed == _subjectPublicKeyInfo.Length
                    && rsa.VerifyData(message, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            if (_keyKind == IssuerKeyKind.Ecdsa
                && algorithm.Equals(CommercialLicenseCodec.EcdsaAlgorithm, StringComparison.Ordinal))
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(_subjectPublicKeyInfo, out var consumed);
                return consumed == _subjectPublicKeyInfo.Length
                    && ecdsa.VerifyData(message, signature, HashAlgorithmName.SHA256,
                        DSASignatureFormat.Rfc3279DerSequence);
            }
        }
        catch (CryptographicException)
        {
            return false;
        }

        return false;
    }
}

// Commercial builds inject these two values as AssemblyMetadata attributes in
// the agent DLL before Authenticode signing. Reading the metadata from this
// assembly keeps the trust anchor inside the signed binary and prevents an
// appsettings or sidecar-file replacement from choosing a pirate issuer.
sealed class CommercialLicenseBuildIdentity
{
    internal const string RequiredMetadataKey = "TurboRama.CommercialLicenseRequired";
    internal const string IssuerCertificateMetadataKey = "TurboRama.LicenseIssuerCertificateBase64";

    private CommercialLicenseBuildIdentity(bool required, CommercialLicenseTrustedIssuer? trustedIssuer)
    {
        Required = required;
        TrustedIssuer = trustedIssuer;
    }

    public bool Required { get; }
    public CommercialLicenseTrustedIssuer? TrustedIssuer { get; }

    public static CommercialLicenseBuildIdentity LoadCurrent()
        => Load(typeof(CommercialLicenseBuildIdentity).Assembly);

    public static CommercialLicenseBuildIdentity Load(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return FromMetadata(assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Select(attribute => new KeyValuePair<string, string?>(attribute.Key, attribute.Value)));
    }

    public CommercialLicenseVerifier CreateRequiredVerifier(
        IPixMachineBinding machineBinding,
        CommercialLicensePolicy policy,
        TimeProvider? timeProvider = null)
    {
        if (!Required || TrustedIssuer is null)
            throw new InvalidOperationException("Esta compilacao nao exige licenca comercial incorporada.");
        return new CommercialLicenseVerifier([TrustedIssuer], machineBinding, policy, timeProvider);
    }

    internal static CommercialLicenseBuildIdentity FromMetadata(
        IEnumerable<KeyValuePair<string, string?>> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        string? requiredText = null;
        string? certificateBase64 = null;
        var requiredFound = false;
        var certificateFound = false;

        foreach (var item in metadata)
        {
            if (string.Equals(item.Key, RequiredMetadataKey, StringComparison.Ordinal))
            {
                if (requiredFound) throw new SecurityException("Metadado de exigencia de licenca duplicado.");
                requiredFound = true;
                requiredText = item.Value;
            }
            else if (string.Equals(item.Key, IssuerCertificateMetadataKey, StringComparison.Ordinal))
            {
                if (certificateFound) throw new SecurityException("Metadado do emissor de licenca duplicado.");
                certificateFound = true;
                certificateBase64 = item.Value;
            }
        }

        // Existing development builds carry neither attribute and remain in
        // non-commercial mode. Partial, malformed or contradictory metadata
        // always fails closed instead of silently downgrading protection.
        if (!requiredFound && !certificateFound)
            return new CommercialLicenseBuildIdentity(required: false, trustedIssuer: null);
        if (!requiredFound || !certificateFound)
            throw new SecurityException("Metadados comerciais incompletos na DLL do agente.");
        if (requiredText is not ("true" or "false"))
            throw new SecurityException("Metadado de exigencia de licenca deve ser true ou false canonico.");

        var required = requiredText == "true";
        if (!required)
        {
            if (!string.IsNullOrEmpty(certificateBase64))
                throw new SecurityException("Compilacao sem licenca obrigatoria nao pode incorporar emissor comercial.");
            return new CommercialLicenseBuildIdentity(required: false, trustedIssuer: null);
        }

        if (string.IsNullOrEmpty(certificateBase64))
            throw new SecurityException("Compilacao comercial nao incorporou certificado publico do emissor.");
        return new CommercialLicenseBuildIdentity(required: true,
            CommercialLicenseTrustedIssuer.FromCertificateBase64(certificateBase64));
    }
}

sealed class CommercialLicenseVerifier
{
    private readonly IReadOnlyDictionary<string, CommercialLicenseTrustedIssuer> _issuers;
    private readonly IPixMachineBinding _machineBinding;
    private readonly CommercialLicensePolicy _policy;
    private readonly TimeProvider _timeProvider;

    public CommercialLicenseVerifier(
        IEnumerable<CommercialLicenseTrustedIssuer> trustedIssuers,
        IPixMachineBinding machineBinding,
        CommercialLicensePolicy policy,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(trustedIssuers);
        ArgumentNullException.ThrowIfNull(machineBinding);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        var issuers = new Dictionary<string, CommercialLicenseTrustedIssuer>(StringComparer.Ordinal);
        foreach (var issuer in trustedIssuers)
        {
            if (issuer is null) throw new ArgumentException("A lista de emissores contem item nulo.", nameof(trustedIssuers));
            if (!issuers.TryAdd(issuer.SpkiSha256, issuer))
                throw new ArgumentException("A lista de emissores contem chave publica duplicada.", nameof(trustedIssuers));
        }
        if (issuers.Count == 0)
            throw new ArgumentException("Ao menos um emissor de licenca precisa ser confiavel.", nameof(trustedIssuers));

        _issuers = issuers;
        _machineBinding = machineBinding;
        _policy = policy;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CommercialLicenseValidationResult ValidateFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return CommercialLicenseValidationResult.Failed(
                CommercialLicenseValidationState.Missing, "O arquivo de licenca nao foi informado.");
        try
        {
            if (!File.Exists(path))
                return CommercialLicenseValidationResult.Failed(
                    CommercialLicenseValidationState.Missing, "Licenca comercial ainda nao instalada.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > CommercialLicenseCodec.MaximumEnvelopeBytes)
                return CommercialLicenseValidationResult.Failed(
                    CommercialLicenseValidationState.InvalidFormat, "O tamanho do arquivo de licenca e invalido.");
            var bytes = new byte[(int)stream.Length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0)
                    return CommercialLicenseValidationResult.Failed(
                        CommercialLicenseValidationState.ReadFailure, "O arquivo de licenca terminou antes do esperado.");
                offset += read;
            }
            if (stream.ReadByte() != -1)
                return CommercialLicenseValidationResult.Failed(
                    CommercialLicenseValidationState.InvalidFormat, "O arquivo de licenca excede o limite permitido.");
            return Validate(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return CommercialLicenseValidationResult.Failed(
                CommercialLicenseValidationState.ReadFailure, "O arquivo de licenca nao pode ser lido com seguranca.");
        }
    }

    public CommercialLicenseValidationResult Validate(ReadOnlyMemory<byte> licenseBytes)
    {
        CommercialLicenseEnvelope envelope;
        CommercialLicensePayload payload;
        try
        {
            envelope = CommercialLicenseCodec.ParseEnvelopeStrict(licenseBytes);
            payload = CommercialLicenseCodec.ParsePayloadStrict(envelope.Payload);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException
            or OverflowException or DecoderFallbackException)
        {
            return CommercialLicenseValidationResult.Failed(
                CommercialLicenseValidationState.InvalidFormat, "A licenca comercial possui formato invalido.");
        }

        if (!_issuers.TryGetValue(envelope.IssuerSpkiSha256, out var issuer))
            return CommercialLicenseValidationResult.Failed(
                CommercialLicenseValidationState.UntrustedIssuer, "A licenca foi emitida por uma chave nao autorizada.");

        byte[] signingMessage = Array.Empty<byte>();
        try
        {
            signingMessage = CommercialLicenseCodec.BuildSigningMessage(envelope.Payload);
            if (!issuer.Verify(envelope.Algorithm, signingMessage, envelope.Signature))
                return CommercialLicenseValidationResult.Failed(
                    CommercialLicenseValidationState.InvalidSignature, "A assinatura da licenca e invalida.");
        }
        finally
        {
            if (signingMessage.Length != 0) CryptographicOperations.ZeroMemory(signingMessage);
        }

        if (!payload.Product.Equals(_policy.Product, StringComparison.Ordinal)
            || payload.ProductMajor != _policy.ProductMajor)
            return CommercialLicenseValidationResult.Failed(
                CommercialLicenseValidationState.WrongProduct, "A licenca pertence a outro produto ou versao principal.");

        if (!payload.Features.Contains(_policy.RequiredFeature, StringComparer.Ordinal))
            return CommercialLicenseValidationResult.Failed(
                CommercialLicenseValidationState.MissingFeature, "A licenca nao autoriza o recurso comercial solicitado.");

        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (payload.IssuedAtUnixSeconds > now + _policy.AllowedClockSkewSeconds)
            return CommercialLicenseValidationResult.Failed(
                CommercialLicenseValidationState.NotYetValid, "A licenca ainda nao e valida segundo o relogio deste quiosque.");
        if (payload.NotAfterUnixSeconds is long notAfter && now > notAfter + _policy.AllowedClockSkewSeconds)
            return CommercialLicenseValidationResult.Failed(
                CommercialLicenseValidationState.Expired, "A licenca comercial expirou.");

        try
        {
            // VerifyFingerprint opens the already-provisioned non-exportable
            // key and proves that this process still owns the exact machine key
            // named by the signed license. It must not silently create a new
            // key while validating an installed license.
            _machineBinding.VerifyFingerprint(payload.MachineKeySha256);
        }
        catch (SecurityException)
        {
            return CommercialLicenseValidationResult.Failed(
                CommercialLicenseValidationState.WrongMachine, "A licenca pertence a outro quiosque ou vinculo criptografico.");
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            return CommercialLicenseValidationResult.Failed(
                CommercialLicenseValidationState.MachineBindingUnavailable,
                "O vinculo criptografico da licenca nao esta disponivel neste quiosque.");
        }

        return CommercialLicenseValidationResult.Valid(payload.LicenseId);
    }
}

static class CommercialLicenseCodec
{
    internal const int SchemaVersion = 1;
    internal const int MaximumEnvelopeBytes = 64 * 1024;
    internal const string LicenseKind = "TurboRamaOfflineMachineLicense/v1";
    internal const string ActivationRequestKind = "TurboRamaActivationRequest/v1";
    internal const string RsaAlgorithm = "rsa-pkcs1-sha256";
    internal const string EcdsaAlgorithm = "ecdsa-der-sha256";

    private const int MaximumPayloadBytes = 32 * 1024;
    private const int MaximumSignatureBytes = 2048;
    private const long MaximumUnixSeconds = 253_402_300_799;
    private static readonly byte[] SignatureDomain = Encoding.ASCII.GetBytes(
        "TurboRamaOfflineMachineLicenseSignature/v1\0");
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16
    };
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.Default,
        Indented = false,
        SkipValidation = false
    };

    public static CommercialActivationRequest CreateActivationRequest(
        CommercialLicensePolicy policy,
        IPixMachineBinding machineBinding,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(machineBinding);
        policy.Validate();
        var fingerprint = NormalizeSha256Hex(machineBinding.GetOrCreateFingerprint(), "fingerprint da maquina");
        return new CommercialActivationRequest(
            SchemaVersion,
            ActivationRequestKind,
            Guid.NewGuid().ToString("N"),
            policy.Product,
            policy.ProductMajor,
            fingerprint,
            (timeProvider ?? TimeProvider.System).GetUtcNow().ToUnixTimeSeconds());
    }

    public static byte[] SerializeActivationRequest(CommercialActivationRequest request)
    {
        ValidateActivationRequest(request);
        using var stream = new MemoryStream(512);
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", request.SchemaVersion);
            writer.WriteString("kind", request.Kind);
            writer.WriteString("requestId", request.RequestId);
            writer.WriteString("product", request.Product);
            writer.WriteNumber("productMajor", request.ProductMajor);
            writer.WriteString("machineKeySha256", request.MachineKeySha256);
            writer.WriteNumber("generatedAtUnixSeconds", request.GeneratedAtUnixSeconds);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static CommercialActivationRequest ParseActivationRequestStrict(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > 16 * 1024) throw new FormatException("Pedido de ativacao com tamanho invalido.");
        using var document = JsonDocument.Parse(bytes, DocumentOptions);
        var root = RequireObject(document.RootElement, "pedido de ativacao");
        RequireProperties(root, "schemaVersion", "kind", "requestId", "product", "productMajor",
            "machineKeySha256", "generatedAtUnixSeconds");
        var request = new CommercialActivationRequest(
            RequireInt32(root, "schemaVersion"),
            RequireString(root, "kind"),
            RequireString(root, "requestId"),
            RequireString(root, "product"),
            RequireInt32(root, "productMajor"),
            RequireString(root, "machineKeySha256"),
            RequireInt64(root, "generatedAtUnixSeconds"));
        ValidateActivationRequest(request);
        RequireCanonical(bytes.Span, SerializeActivationRequest(request), "pedido de ativacao");
        return request;
    }

    public static byte[] SerializePayload(CommercialLicensePayload payload)
    {
        ValidatePayload(payload);
        using var stream = new MemoryStream(1024);
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", payload.SchemaVersion);
            writer.WriteString("kind", payload.Kind);
            writer.WriteString("licenseId", payload.LicenseId);
            writer.WriteString("activationRequestId", payload.ActivationRequestId);
            writer.WriteString("product", payload.Product);
            writer.WriteNumber("productMajor", payload.ProductMajor);
            writer.WriteString("machineKeySha256", payload.MachineKeySha256);
            writer.WritePropertyName("features");
            writer.WriteStartArray();
            foreach (var feature in payload.Features) writer.WriteStringValue(feature);
            writer.WriteEndArray();
            writer.WriteNumber("issuedAtUnixSeconds", payload.IssuedAtUnixSeconds);
            if (payload.NotAfterUnixSeconds is long notAfter)
                writer.WriteNumber("notAfterUnixSeconds", notAfter);
            else
                writer.WriteNull("notAfterUnixSeconds");
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static CommercialLicensePayload ParsePayloadStrict(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumPayloadBytes) throw new FormatException("Payload de licenca com tamanho invalido.");
        using var document = JsonDocument.Parse(bytes, DocumentOptions);
        var root = RequireObject(document.RootElement, "payload da licenca");
        RequireProperties(root, "schemaVersion", "kind", "licenseId", "activationRequestId", "product", "productMajor",
            "machineKeySha256", "features", "issuedAtUnixSeconds", "notAfterUnixSeconds");

        var featuresElement = root.GetProperty("features");
        if (featuresElement.ValueKind != JsonValueKind.Array)
            throw new FormatException("A lista de recursos da licenca e invalida.");
        var features = featuresElement.EnumerateArray().Select(element =>
        {
            if (element.ValueKind != JsonValueKind.String)
                throw new FormatException("A lista de recursos contem item invalido.");
            return element.GetString() ?? throw new FormatException("A lista de recursos contem item nulo.");
        }).ToArray();

        var notAfterElement = root.GetProperty("notAfterUnixSeconds");
        long? notAfter = notAfterElement.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when notAfterElement.TryGetInt64(out var parsed) => parsed,
            _ => throw new FormatException("A validade final da licenca e invalida.")
        };

        var payload = new CommercialLicensePayload(
            RequireInt32(root, "schemaVersion"),
            RequireString(root, "kind"),
            RequireString(root, "licenseId"),
            RequireString(root, "activationRequestId"),
            RequireString(root, "product"),
            RequireInt32(root, "productMajor"),
            RequireString(root, "machineKeySha256"),
            features,
            RequireInt64(root, "issuedAtUnixSeconds"),
            notAfter);
        ValidatePayload(payload);
        RequireCanonical(bytes.Span, SerializePayload(payload), "payload da licenca");
        return payload;
    }

    public static byte[] SerializeEnvelope(CommercialLicenseEnvelope envelope)
    {
        ValidateEnvelope(envelope);
        using var stream = new MemoryStream(2048);
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", envelope.SchemaVersion);
            writer.WriteString("algorithm", envelope.Algorithm);
            writer.WriteString("issuerSpkiSha256", envelope.IssuerSpkiSha256);
            writer.WriteString("payload", Base64UrlEncode(envelope.Payload));
            writer.WriteString("signature", Base64UrlEncode(envelope.Signature));
            writer.WriteEndObject();
        }
        var encoded = stream.ToArray();
        if (encoded.Length > MaximumEnvelopeBytes) throw new FormatException("Envelope de licenca excede o limite.");
        return encoded;
    }

    public static CommercialLicenseEnvelope ParseEnvelopeStrict(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumEnvelopeBytes) throw new FormatException("Envelope de licenca com tamanho invalido.");
        using var document = JsonDocument.Parse(bytes, DocumentOptions);
        var root = RequireObject(document.RootElement, "envelope da licenca");
        RequireProperties(root, "schemaVersion", "algorithm", "issuerSpkiSha256", "payload", "signature");
        var envelope = new CommercialLicenseEnvelope(
            RequireInt32(root, "schemaVersion"),
            RequireString(root, "algorithm"),
            RequireString(root, "issuerSpkiSha256"),
            Base64UrlDecodeStrict(RequireString(root, "payload"), MaximumPayloadBytes, "payload"),
            Base64UrlDecodeStrict(RequireString(root, "signature"), MaximumSignatureBytes, "assinatura"));
        ValidateEnvelope(envelope);
        RequireCanonical(bytes.Span, SerializeEnvelope(envelope), "envelope da licenca");
        return envelope;
    }

    internal static byte[] BuildSigningMessage(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > MaximumPayloadBytes)
            throw new FormatException("Payload de assinatura com tamanho invalido.");
        var message = new byte[SignatureDomain.Length + payload.Length];
        SignatureDomain.CopyTo(message, 0);
        payload.CopyTo(message.AsSpan(SignatureDomain.Length));
        return message;
    }

    internal static string Sha256Hex(ReadOnlySpan<byte> value)
        => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    internal static string NormalizeSha256Hex(string value, string label)
    {
        if (value is null || value.Length != 64 || !value.All(IsLowerHex))
            throw new FormatException($"O {label} deve conter SHA-256 hexadecimal minusculo.");
        return value;
    }

    internal static void RequireIdentifier(
        string value, int minimumLength, int maximumLength, string label, bool requireLowercase)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < minimumLength || value.Length > maximumLength
            || !value.Equals(value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
            throw new FormatException($"O identificador de {label} e invalido.");
        if (!IsAsciiAlphaNumeric(value[0]))
            throw new FormatException($"O identificador de {label} deve iniciar com letra ou numero.");
        foreach (var character in value)
        {
            if (!IsAsciiAlphaNumeric(character) && character is not ('.' or '_' or '-'))
                throw new FormatException($"O identificador de {label} possui caractere invalido.");
            if (requireLowercase && character is >= 'A' and <= 'Z')
                throw new FormatException($"O identificador de {label} deve estar em minusculas.");
        }
    }

    private static void ValidateActivationRequest(CommercialActivationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SchemaVersion != SchemaVersion || request.Kind != ActivationRequestKind)
            throw new FormatException("A versao do pedido de ativacao nao e suportada.");
        RequireLowerHexId(request.RequestId, "pedido");
        RequireIdentifier(request.Product, 2, 64, "produto", requireLowercase: false);
        if (request.ProductMajor is < 1 or > 9999) throw new FormatException("A versao principal do produto e invalida.");
        NormalizeSha256Hex(request.MachineKeySha256, "fingerprint da maquina");
        RequireUnixSeconds(request.GeneratedAtUnixSeconds, "data do pedido");
    }

    private static void ValidatePayload(CommercialLicensePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.SchemaVersion != SchemaVersion || payload.Kind != LicenseKind)
            throw new FormatException("A versao da licenca nao e suportada.");
        RequireLowerHexId(payload.LicenseId, "licenca");
        RequireLowerHexId(payload.ActivationRequestId, "pedido de ativacao");
        RequireIdentifier(payload.Product, 2, 64, "produto", requireLowercase: false);
        if (payload.ProductMajor is < 1 or > 9999) throw new FormatException("A versao principal do produto e invalida.");
        NormalizeSha256Hex(payload.MachineKeySha256, "fingerprint da maquina");
        if (payload.Features is null || payload.Features.Count is < 1 or > 32)
            throw new FormatException("A lista de recursos da licenca e invalida.");
        string? previous = null;
        foreach (var feature in payload.Features)
        {
            RequireIdentifier(feature, 2, 64, "recurso", requireLowercase: true);
            if (previous is not null && string.CompareOrdinal(previous, feature) >= 0)
                throw new FormatException("Os recursos da licenca devem ser unicos e ordenados.");
            previous = feature;
        }
        RequireUnixSeconds(payload.IssuedAtUnixSeconds, "data de emissao");
        if (payload.NotAfterUnixSeconds is long notAfter)
        {
            RequireUnixSeconds(notAfter, "validade final");
            if (notAfter <= payload.IssuedAtUnixSeconds)
                throw new FormatException("A validade final precisa ser posterior a emissao.");
        }
    }

    private static void ValidateEnvelope(CommercialLicenseEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.SchemaVersion != SchemaVersion)
            throw new FormatException("A versao do envelope da licenca nao e suportada.");
        if (envelope.Algorithm is not (RsaAlgorithm or EcdsaAlgorithm))
            throw new FormatException("O algoritmo da licenca nao e suportado.");
        NormalizeSha256Hex(envelope.IssuerSpkiSha256, "emissor");
        if (envelope.Payload is null || envelope.Payload.Length is <= 0 or > MaximumPayloadBytes)
            throw new FormatException("O payload da licenca e invalido.");
        if (envelope.Signature is null || envelope.Signature.Length is <= 0 or > MaximumSignatureBytes)
            throw new FormatException("A assinatura da licenca e invalida.");
    }

    private static JsonElement RequireObject(JsonElement element, string label)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new FormatException($"O {label} deve ser um objeto JSON.");
        return element;
    }

    private static void RequireProperties(JsonElement root, params string[] expected)
    {
        var properties = root.EnumerateObject().ToArray();
        if (properties.Length != expected.Length)
            throw new FormatException("O JSON possui campos ausentes, duplicados ou desconhecidos.");
        for (var index = 0; index < expected.Length; ++index)
        {
            if (!properties[index].NameEquals(expected[index]))
                throw new FormatException("Os campos JSON nao estao na ordem canonica.");
        }
    }

    private static string RequireString(JsonElement root, string property)
    {
        var value = root.GetProperty(property);
        if (value.ValueKind != JsonValueKind.String)
            throw new FormatException($"O campo {property} deve ser texto.");
        return value.GetString() ?? throw new FormatException($"O campo {property} nao pode ser nulo.");
    }

    private static int RequireInt32(JsonElement root, string property)
    {
        var value = root.GetProperty(property);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed))
            throw new FormatException($"O campo {property} deve ser inteiro.");
        return parsed;
    }

    private static long RequireInt64(JsonElement root, string property)
    {
        var value = root.GetProperty(property);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var parsed))
            throw new FormatException($"O campo {property} deve ser inteiro.");
        return parsed;
    }

    private static void RequireCanonical(ReadOnlySpan<byte> original, ReadOnlySpan<byte> canonical, string label)
    {
        if (!original.SequenceEqual(canonical))
            throw new FormatException($"O {label} nao usa a codificacao canonica exigida.");
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecodeStrict(string value, int maximumBytes, string label)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximumBytes * 2 || value.Contains('=')
            || value.Any(character => !IsBase64Url(character)) || value.Length % 4 == 1)
            throw new FormatException($"O campo {label} nao e base64url canonico.");
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        byte[] decoded;
        try { decoded = Convert.FromBase64String(padded); }
        catch (FormatException ex) { throw new FormatException($"O campo {label} nao e base64url valido.", ex); }
        if (decoded.Length is <= 0 || decoded.Length > maximumBytes || Base64UrlEncode(decoded) != value)
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new FormatException($"O campo {label} nao e base64url canonico.");
        }
        return decoded;
    }

    private static void RequireLowerHexId(string value, string label)
    {
        if (value is null || value.Length != 32 || !value.All(IsLowerHex))
            throw new FormatException($"O identificador de {label} deve conter 32 caracteres hexadecimais minusculos.");
    }

    private static void RequireUnixSeconds(long value, string label)
    {
        if (value is < 0 or > MaximumUnixSeconds)
            throw new FormatException($"A {label} esta fora do intervalo suportado.");
    }

    private static bool IsAsciiAlphaNumeric(char character)
        => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static bool IsLowerHex(char character)
        => character is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static bool IsBase64Url(char character)
        => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_';
}

// Isolated, network-free tests. Program.cs can invoke Run() from the existing
// self-test entry point. Test signing keys are generated ephemerally in memory;
// no private key or credential is embedded in the product.
static class CommercialLicenseSelfTest
{
    public static void Run()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var clock = new FixedTimeProvider(now);
        var machineFingerprint = new string('a', 64);
        var binding = new FakeMachineBinding(machineFingerprint);
        var policy = new CommercialLicensePolicy("TurboRama-PIX", 25, "pix-production");

        var request = CommercialLicenseCodec.CreateActivationRequest(policy, binding, clock);
        var requestBytes = CommercialLicenseCodec.SerializeActivationRequest(request);
        var parsedRequest = CommercialLicenseCodec.ParseActivationRequestStrict(requestBytes);
        Require(parsedRequest.MachineKeySha256 == machineFingerprint
            && parsedRequest.Product == policy.Product
            && parsedRequest.ProductMajor == policy.ProductMajor,
            "pedido de ativacao canonico nao preservou os campos");
        Require(!Encoding.UTF8.GetString(requestBytes).Contains("token", StringComparison.OrdinalIgnoreCase)
            && !Encoding.UTF8.GetString(requestBytes).Contains("secret", StringComparison.OrdinalIgnoreCase),
            "pedido de ativacao contem nome de campo sensivel");
        RequireThrows<FormatException>(() =>
            CommercialLicenseCodec.ParseActivationRequestStrict(requestBytes.Concat([(byte)' ']).ToArray()),
            "pedido de ativacao nao canonico foi aceito");

        using var rsa = RSA.Create(2048);
        TestIssuerAndLicense(rsa, CommercialLicenseCodec.RsaAlgorithm, policy, binding, clock, now);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TestIssuerAndLicense(ecdsa, CommercialLicenseCodec.EcdsaAlgorithm, policy, binding, clock, now);
    }

    private static void TestIssuerAndLicense(
        AsymmetricAlgorithm signingKey,
        string algorithm,
        CommercialLicensePolicy policy,
        FakeMachineBinding binding,
        TimeProvider clock,
        DateTimeOffset now)
    {
        var spki = signingKey switch
        {
            RSA rsa => rsa.ExportSubjectPublicKeyInfo(),
            ECDsa ecdsa => ecdsa.ExportSubjectPublicKeyInfo(),
            _ => throw new InvalidOperationException("algoritmo de autoteste inesperado")
        };
        var issuer = CommercialLicenseTrustedIssuer.FromSubjectPublicKeyInfo(spki);
        using var testCertificate = CreateSelfSignedCertificate(signingKey, now);
        var certificateDer = testCertificate.Export(X509ContentType.Cert);
        var issuerFromCertificate = CommercialLicenseTrustedIssuer.FromCertificate(certificateDer);
        var issuerFromBase64Certificate = CommercialLicenseTrustedIssuer.FromCertificateBase64(
            Convert.ToBase64String(certificateDer));
        Require(issuerFromCertificate.SpkiSha256 == issuer.SpkiSha256,
            "certificado publico e SPKI produziram emissores diferentes");
        Require(issuerFromBase64Certificate.SpkiSha256 == issuer.SpkiSha256,
            "certificado DER base64 e SPKI produziram emissores diferentes");
        TestBuildIdentity(Convert.ToBase64String(certificateDer), issuer.SpkiSha256, policy, binding, clock);
        var verifier = new CommercialLicenseVerifier([issuer], binding, policy, clock);
        var payload = NewPayload(policy, binding.GetOrCreateFingerprint(), now);
        var license = SignEnvelope(payload, signingKey, algorithm, issuer.SpkiSha256);

        var result = verifier.Validate(license);
        Require(result.IsValid && result.LicenseId == payload.LicenseId,
            $"licenca {algorithm} valida foi rejeitada: {result.State}");

        var nonCanonical = license.Concat([(byte)'\n']).ToArray();
        RequireState(verifier.Validate(nonCanonical), CommercialLicenseValidationState.InvalidFormat,
            "envelope nao canonico foi aceito");

        var envelope = CommercialLicenseCodec.ParseEnvelopeStrict(license);
        var tamperedPayload = CommercialLicenseCodec.SerializePayload(payload with { Product = "OutroProduto" });
        var tamperedPayloadLicense = CommercialLicenseCodec.SerializeEnvelope(envelope with { Payload = tamperedPayload });
        RequireState(verifier.Validate(tamperedPayloadLicense), CommercialLicenseValidationState.InvalidSignature,
            "payload adulterado com assinatura antiga foi aceito");

        var damagedSignature = (byte[])envelope.Signature.Clone();
        damagedSignature[^1] ^= 0x01;
        var damagedLicense = CommercialLicenseCodec.SerializeEnvelope(envelope with { Signature = damagedSignature });
        RequireState(verifier.Validate(damagedLicense), CommercialLicenseValidationState.InvalidSignature,
            "assinatura adulterada foi aceita");

        using var otherRsa = RSA.Create(2048);
        var otherIssuer = CommercialLicenseTrustedIssuer.FromSubjectPublicKeyInfo(otherRsa.ExportSubjectPublicKeyInfo());
        var otherVerifier = new CommercialLicenseVerifier([otherIssuer], binding, policy, clock);
        RequireState(otherVerifier.Validate(license), CommercialLicenseValidationState.UntrustedIssuer,
            "emissor nao confiavel foi aceito");

        var wrongMachinePayload = NewPayload(policy, new string('b', 64), now);
        var wrongMachineLicense = SignEnvelope(wrongMachinePayload, signingKey, algorithm, issuer.SpkiSha256);
        RequireState(verifier.Validate(wrongMachineLicense), CommercialLicenseValidationState.WrongMachine,
            "licenca de outro TPM foi aceita");

        var missingFeaturePayload = NewPayload(policy, binding.GetOrCreateFingerprint(), now)
            with { Features = ["another-feature"] };
        var missingFeatureLicense = SignEnvelope(missingFeaturePayload, signingKey, algorithm, issuer.SpkiSha256);
        RequireState(verifier.Validate(missingFeatureLicense), CommercialLicenseValidationState.MissingFeature,
            "licenca sem recurso comercial foi aceita");

        var wrongProductPayload = NewPayload(policy, binding.GetOrCreateFingerprint(), now)
            with { Product = "OutroProduto" };
        var wrongProductLicense = SignEnvelope(wrongProductPayload, signingKey, algorithm, issuer.SpkiSha256);
        RequireState(verifier.Validate(wrongProductLicense), CommercialLicenseValidationState.WrongProduct,
            "licenca de outro produto foi aceita");

        var expiredPayload = NewPayload(policy, binding.GetOrCreateFingerprint(), now.AddHours(-2))
            with { NotAfterUnixSeconds = now.AddHours(-1).ToUnixTimeSeconds() };
        var expiredLicense = SignEnvelope(expiredPayload, signingKey, algorithm, issuer.SpkiSha256);
        RequireState(verifier.Validate(expiredLicense), CommercialLicenseValidationState.Expired,
            "licenca expirada foi aceita");

        var payloadBytes = CommercialLicenseCodec.SerializePayload(payload);
        RequireThrows<FormatException>(() =>
            CommercialLicenseCodec.ParsePayloadStrict(payloadBytes.Concat([(byte)' ']).ToArray()),
            "payload nao canonico foi aceito");

        var duplicateEnvelope = Encoding.UTF8.GetString(license).Replace(
            "{\"schemaVersion\":1,",
            "{\"schemaVersion\":1,\"schemaVersion\":1,",
            StringComparison.Ordinal);
        RequireState(verifier.Validate(Encoding.UTF8.GetBytes(duplicateEnvelope)),
            CommercialLicenseValidationState.InvalidFormat,
            "envelope com campo duplicado foi aceito");

        CryptographicOperations.ZeroMemory(spki);
        CryptographicOperations.ZeroMemory(payloadBytes);
        CryptographicOperations.ZeroMemory(tamperedPayload);
        CryptographicOperations.ZeroMemory(damagedSignature);
    }

    private static void TestBuildIdentity(
        string certificateBase64,
        string expectedSpkiSha256,
        CommercialLicensePolicy policy,
        IPixMachineBinding binding,
        TimeProvider clock)
    {
        var development = CommercialLicenseBuildIdentity.FromMetadata([]);
        Require(!development.Required && development.TrustedIssuer is null,
            "build sem metadados foi tratado como comercial");

        var commercial = CommercialLicenseBuildIdentity.FromMetadata([
            new(CommercialLicenseBuildIdentity.RequiredMetadataKey, "true"),
            new(CommercialLicenseBuildIdentity.IssuerCertificateMetadataKey, certificateBase64)
        ]);
        Require(commercial.Required
            && commercial.TrustedIssuer?.SpkiSha256 == expectedSpkiSha256,
            "build comercial nao carregou o emissor incorporado");
        _ = commercial.CreateRequiredVerifier(binding, policy, clock);

        var nonCommercial = CommercialLicenseBuildIdentity.FromMetadata([
            new(CommercialLicenseBuildIdentity.RequiredMetadataKey, "false"),
            new(CommercialLicenseBuildIdentity.IssuerCertificateMetadataKey, "")
        ]);
        Require(!nonCommercial.Required && nonCommercial.TrustedIssuer is null,
            "build explicitamente nao comercial foi rejeitado");

        RequireThrows<SecurityException>(() => CommercialLicenseBuildIdentity.FromMetadata([
            new(CommercialLicenseBuildIdentity.RequiredMetadataKey, "true")
        ]), "metadados comerciais parciais foram aceitos");
        RequireThrows<SecurityException>(() => CommercialLicenseBuildIdentity.FromMetadata([
            new(CommercialLicenseBuildIdentity.RequiredMetadataKey, "true"),
            new(CommercialLicenseBuildIdentity.RequiredMetadataKey, "true"),
            new(CommercialLicenseBuildIdentity.IssuerCertificateMetadataKey, certificateBase64)
        ]), "metadado comercial duplicado foi aceito");
        RequireThrows<SecurityException>(() => CommercialLicenseBuildIdentity.FromMetadata([
            new(CommercialLicenseBuildIdentity.RequiredMetadataKey, "false"),
            new(CommercialLicenseBuildIdentity.IssuerCertificateMetadataKey, certificateBase64)
        ]), "emissor comercial foi aceito em build sem licenca obrigatoria");
    }

    private static X509Certificate2 CreateSelfSignedCertificate(
        AsymmetricAlgorithm signingKey, DateTimeOffset now)
    {
        var request = signingKey switch
        {
            RSA rsa => new CertificateRequest(
                "CN=TurboRama Commercial License Self Test", rsa,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            ECDsa ecdsa => new CertificateRequest(
                "CN=TurboRama Commercial License Self Test", ecdsa,
                HashAlgorithmName.SHA256),
            _ => throw new InvalidOperationException("algoritmo de certificado de autoteste inesperado")
        };
        return request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
    }

    private static CommercialLicensePayload NewPayload(
        CommercialLicensePolicy policy, string fingerprint, DateTimeOffset issuedAt)
        => new(
            CommercialLicenseCodec.SchemaVersion,
            CommercialLicenseCodec.LicenseKind,
            "0123456789abcdef0123456789abcdef",
            "fedcba9876543210fedcba9876543210",
            policy.Product,
            policy.ProductMajor,
            fingerprint,
            [policy.RequiredFeature],
            issuedAt.ToUnixTimeSeconds(),
            null);

    private static byte[] SignEnvelope(
        CommercialLicensePayload payload,
        AsymmetricAlgorithm signingKey,
        string algorithm,
        string issuerSpkiSha256)
    {
        var payloadBytes = CommercialLicenseCodec.SerializePayload(payload);
        var message = CommercialLicenseCodec.BuildSigningMessage(payloadBytes);
        byte[] signature;
        try
        {
            signature = signingKey switch
            {
                RSA rsa when algorithm == CommercialLicenseCodec.RsaAlgorithm
                    => rsa.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                ECDsa ecdsa when algorithm == CommercialLicenseCodec.EcdsaAlgorithm
                    => ecdsa.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
                _ => throw new InvalidOperationException("algoritmo e chave de autoteste divergentes")
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(message);
        }

        try
        {
            return CommercialLicenseCodec.SerializeEnvelope(new CommercialLicenseEnvelope(
                CommercialLicenseCodec.SchemaVersion,
                algorithm,
                issuerSpkiSha256,
                payloadBytes,
                signature));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private static void RequireState(
        CommercialLicenseValidationResult result,
        CommercialLicenseValidationState expected,
        string message)
    {
        if (result.State != expected)
            throw new InvalidOperationException($"{message}; esperado={expected}; recebido={result.State}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }

    private sealed class FakeMachineBinding(string fingerprint) : IPixMachineBinding
    {
        private readonly string _fingerprint = CommercialLicenseCodec.NormalizeSha256Hex(fingerprint, "fingerprint de teste");

        public string GetOrCreateFingerprint() => _fingerprint;

        public void VerifyFingerprint(string expectedFingerprint)
        {
            var expected = Encoding.ASCII.GetBytes(
                CommercialLicenseCodec.NormalizeSha256Hex(expectedFingerprint, "fingerprint esperado"));
            var actual = Encoding.ASCII.GetBytes(_fingerprint);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                    throw new SecurityException("fingerprint TPM divergente");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expected);
                CryptographicOperations.ZeroMemory(actual);
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
