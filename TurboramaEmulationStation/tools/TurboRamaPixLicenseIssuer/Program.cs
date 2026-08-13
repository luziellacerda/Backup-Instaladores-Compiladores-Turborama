using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.Text.Json;

try
{
    var command = LicenseIssuerCommand.Parse(args);
    if (command.SelfTest)
    {
        CommercialLicenseSelfTest.Run();
        LicenseIssuerSelfTest.Run();
        Console.WriteLine("TurboRama PIX License Issuer self-test: OK");
        return 0;
    }
    if (command.ValidateKey)
    {
        using var validationCertificate = WindowsCertificateFinder.Find(command.Thumbprint, command.StoreLocation);
        Console.WriteLine("Chave privada do emissor confirmada como CNG, nao exportavel e protegida por hardware.");
        return 0;
    }

    var requestBytes = SafeFactoryFile.ReadLimited(command.RequestFile, 16 * 1024, "pedido de ativacao");
    var activationRequest = CommercialLicenseCodec.ParseActivationRequestStrict(requestBytes);
    LicenseIssuer.ValidateActivationRequest(activationRequest, DateTimeOffset.UtcNow);

    // Keep the ledger locked from the replay check through signing, durable
    // registration and publication of the license. Concurrent issuer
    // processes therefore cannot both consume the same activation request.
    using var ledger = IssuanceLedger.Acquire(command.LedgerFile);
    ledger.EnsureNotIssued(activationRequest.RequestId);
    using var certificate = WindowsCertificateFinder.Find(
        command.Thumbprint, command.StoreLocation);
    var issued = LicenseIssuer.Issue(activationRequest, certificate, DateTimeOffset.UtcNow);

    // Persist the request ID before publishing the license. A power loss can
    // consume a request without producing its output, but can never leave an
    // issued license whose request may silently be issued again.
    ledger.RecordIssued(activationRequest.RequestId);
    SafeFactoryFile.WriteNewAtomically(command.OutputFile, issued.Envelope);

    Console.WriteLine("Licenca comercial emitida com sucesso.");
    Console.WriteLine($"Licenca: {issued.LicenseId}");
    Console.WriteLine($"Pedido: {activationRequest.RequestId}");
    Console.WriteLine($"Emissor SPKI SHA-256: {issued.IssuerSpkiSha256}");
    Console.WriteLine($"Arquivo: {Path.GetFullPath(command.OutputFile)}");
    Console.WriteLine($"Registro: {Path.GetFullPath(command.LedgerFile)}");
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or FormatException or IOException
    or UnauthorizedAccessException or SecurityException or CryptographicException
    or InvalidOperationException or JsonException or PlatformNotSupportedException)
{
    Console.Error.WriteLine($"Falha ao emitir a licenca TurboRama: {ex.Message}");
    return 2;
}

static class LicenseIssuerContract
{
    internal const string Product = "TurboRama-PIX";
    internal const int ProductMajor = 25;
    internal const string CommercialFeature = "pix-production";
}

sealed record LicenseIssuerCommand(
    bool SelfTest,
    bool ValidateKey,
    string RequestFile,
    string OutputFile,
    string LedgerFile,
    string Thumbprint,
    StoreLocation StoreLocation)
{
    public static LicenseIssuerCommand Parse(string[] arguments)
    {
        if (arguments.Length == 1 && arguments[0].Equals("--self-test", StringComparison.Ordinal))
            return new LicenseIssuerCommand(true, false, "", "", "", "", StoreLocation.CurrentUser);
        if (arguments.Length == 4
            && arguments[0].Equals("--validate-key", StringComparison.Ordinal)
            && arguments[2].Equals("--store", StringComparison.Ordinal))
        {
            var validationThumbprint = WindowsCertificateFinder.NormalizeThumbprint(arguments[1]);
            var validationStore = ParseStore(arguments[3]);
            return new LicenseIssuerCommand(false, true, "", "", "", validationThumbprint, validationStore);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Length; ++index)
        {
            var option = arguments[index];
            if (option is not ("--request" or "--output" or "--ledger" or "--thumbprint" or "--store"))
                throw Usage($"Opcao desconhecida: {option}");
            if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw Usage($"Valor ausente para {option}.");
            if (!values.TryAdd(option, arguments[++index]))
                throw Usage($"Opcao repetida: {option}.");
        }

        foreach (var required in new[] { "--request", "--output", "--ledger", "--thumbprint", "--store" })
        {
            if (!values.ContainsKey(required)) throw Usage($"Opcao obrigatoria ausente: {required}.");
        }

        var request = RequirePath(values["--request"], "pedido");
        var output = RequirePath(values["--output"], "saida");
        var ledger = RequirePath(values["--ledger"], "registro de emissoes");
        var requestFullPath = Path.GetFullPath(request);
        var outputFullPath = Path.GetFullPath(output);
        var ledgerFullPath = Path.GetFullPath(ledger);
        if (requestFullPath.Equals(outputFullPath, StringComparison.OrdinalIgnoreCase)
            || requestFullPath.Equals(ledgerFullPath, StringComparison.OrdinalIgnoreCase)
            || outputFullPath.Equals(ledgerFullPath, StringComparison.OrdinalIgnoreCase))
            throw Usage("O pedido, a licenca de saida e o registro de emissoes devem ser arquivos diferentes.");

        var thumbprint = WindowsCertificateFinder.NormalizeThumbprint(values["--thumbprint"]);
        var store = ParseStore(values["--store"]);
        return new LicenseIssuerCommand(false, false, request, output, ledger, thumbprint, store);
    }

    private static string RequirePath(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 || value.Any(char.IsControl))
            throw Usage($"O caminho de {label} e invalido.");
        return value;
    }

    private static StoreLocation ParseStore(string value)
        => value.ToLowerInvariant() switch
        {
            "currentuser" => StoreLocation.CurrentUser,
            "localmachine" => StoreLocation.LocalMachine,
            _ => throw Usage("--store deve ser CurrentUser ou LocalMachine.")
        };

    private static ArgumentException Usage(string message)
        => new($"{message}{Environment.NewLine}Uso: TurboRamaPixLicenseIssuer --request pedido.json --output quiosque.license --ledger issued-requests.log --thumbprint SHA1 --store CurrentUser|LocalMachine{Environment.NewLine}Validar chave: TurboRamaPixLicenseIssuer --validate-key SHA1 --store CurrentUser|LocalMachine{Environment.NewLine}Ou: TurboRamaPixLicenseIssuer --self-test");
}

sealed class IssuanceLedger : IDisposable
{
    private static readonly byte[] Header = Encoding.ASCII.GetBytes(
        "TurboRamaIssuedActivationRequests/v1\n");
    private const int RequestIdLength = 32;
    private const int RecordLength = RequestIdLength + 1;
    private const long MaximumLedgerBytes = 32L * 1024 * 1024;
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(30);

    private readonly FileStream _stream;
    private readonly HashSet<string> _issuedRequestIds;
    private bool _disposed;

    private IssuanceLedger(FileStream stream, HashSet<string> issuedRequestIds)
    {
        _stream = stream;
        _issuedRequestIds = issuedRequestIds;
    }

    public static IssuanceLedger Acquire(string path, TimeSpan? lockTimeout = null)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("A pasta do registro de emissoes e invalida.");
        Directory.CreateDirectory(directory);

        var timeout = lockTimeout ?? DefaultLockTimeout;
        if (timeout < TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(lockTimeout));
        var started = System.Diagnostics.Stopwatch.StartNew();
        FileStream stream;
        while (true)
        {
            try
            {
                stream = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.SequentialScan);
                break;
            }
            catch (IOException ex) when (IsSharingViolation(ex) && started.Elapsed < timeout)
            {
                Thread.Sleep(25);
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                throw new IOException("O registro de emissoes esta sendo usado por outro processo; tente novamente.", ex);
            }
        }

        try
        {
            if (stream.Length == 0)
            {
                stream.Write(Header);
                stream.Flush(flushToDisk: true);
            }
            var requestIds = ReadStrict(stream);
            return new IssuanceLedger(stream, requestIds);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void EnsureNotIssued(string requestId)
    {
        ThrowIfDisposed();
        ValidateRequestId(requestId);
        if (_issuedRequestIds.Contains(requestId))
            throw new SecurityException("Este pedido de ativacao ja foi emitido e nao pode ser reutilizado.");
    }

    public void RecordIssued(string requestId)
    {
        ThrowIfDisposed();
        EnsureNotIssued(requestId);
        if (_stream.Length > MaximumLedgerBytes - RecordLength)
            throw new IOException("O registro de emissoes atingiu o limite de tamanho; arquive-o com seguranca.");

        var record = Encoding.ASCII.GetBytes(requestId + "\n");
        try
        {
            _stream.Position = _stream.Length;
            _stream.Write(record);
            _stream.Flush(flushToDisk: true);
            if (!_issuedRequestIds.Add(requestId))
                throw new SecurityException("O pedido de ativacao ja constava no registro de emissoes.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(record);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Dispose();
    }

    private static HashSet<string> ReadStrict(FileStream stream)
    {
        if (stream.Length < Header.Length || stream.Length > MaximumLedgerBytes
            || (stream.Length - Header.Length) % RecordLength != 0)
            throw new FormatException("O registro de emissoes esta truncado ou possui tamanho invalido.");

        stream.Position = 0;
        var bytes = new byte[checked((int)stream.Length)];
        try
        {
            stream.ReadExactly(bytes);
            if (!bytes.AsSpan(0, Header.Length).SequenceEqual(Header))
                throw new FormatException("O cabecalho do registro de emissoes e invalido.");

            var result = new HashSet<string>(StringComparer.Ordinal);
            for (var offset = Header.Length; offset < bytes.Length; offset += RecordLength)
            {
                var idBytes = bytes.AsSpan(offset, RequestIdLength);
                var requestId = Encoding.ASCII.GetString(idBytes);
                ValidateRequestId(requestId);
                if (bytes[offset + RequestIdLength] != (byte)'\n' || !result.Add(requestId))
                    throw new FormatException("O registro de emissoes contem uma linha invalida ou repetida.");
            }
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void ValidateRequestId(string requestId)
    {
        if (requestId is null || requestId.Length != RequestIdLength
            || requestId.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new FormatException("O identificador do pedido no registro de emissoes e invalido.");
    }

    private static bool IsSharingViolation(IOException exception)
        => (exception.HResult & 0xffff) is 32 or 33;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

static class WindowsCertificateFinder
{
    private const string CodeSigningOid = "1.3.6.1.5.5.7.3.3";

    public static string NormalizeThumbprint(string value)
    {
        var normalized = new string((value ?? "").Where(character => !char.IsWhiteSpace(character)).ToArray())
            .ToUpperInvariant();
        if (normalized.Length != 40 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new FormatException("O thumbprint deve conter os 40 digitos hexadecimais SHA-1 do certificado.");
        return normalized;
    }

    public static X509Certificate2 Find(string thumbprint, StoreLocation location)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("O emissor de licenca exige o cofre de certificados do Windows.");
        var normalized = NormalizeThumbprint(thumbprint);
        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

        var matches = store.Certificates
            .Find(X509FindType.FindByThumbprint, normalized, validOnly: false)
            .Cast<X509Certificate2>()
            .Where(certificate => NormalizeThumbprint(certificate.Thumbprint ?? "") == normalized)
            .ToArray();
        if (matches.Length == 0)
            throw new CryptographicException($"Certificado {normalized} nao encontrado em {location}\\My.");

        var distinct = matches
            .GroupBy(certificate => Convert.ToHexString(SHA256.HashData(certificate.RawData)), StringComparer.Ordinal)
            .ToArray();
        if (distinct.Length != 1)
            throw new SecurityException("Mais de um certificado diferente corresponde ao thumbprint informado.");

        var selected = new X509Certificate2(distinct[0].First());
        ValidateForLicenseSigning(selected, DateTimeOffset.UtcNow);
        return selected;
    }

    public static void ValidateForLicenseSigning(X509Certificate2 certificate, DateTimeOffset now,
        bool requireHardwareBackedKey = true)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.HasPrivateKey)
            throw new SecurityException("O certificado selecionado nao possui chave privada acessivel.");
        if (certificate.NotBefore.ToUniversalTime() > now.UtcDateTime
            || certificate.NotAfter.ToUniversalTime() <= now.UtcDateTime)
            throw new SecurityException("O certificado selecionado esta fora do periodo de validade.");

        var hasCodeSigningEku = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(extension => extension.EnhancedKeyUsages.Cast<Oid>())
            .Any(oid => oid.Value == CodeSigningOid);
        if (!hasCodeSigningEku)
            throw new SecurityException("O certificado precisa declarar a finalidade Code Signing.");

        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is not null)
        {
            if (rsa.KeySize < 2048)
                throw new SecurityException("A chave RSA do certificado possui menos de 2048 bits.");
            if (requireHardwareBackedKey) HardwarePrivateKeyPolicy.Require(rsa);
            return;
        }

        using var ecdsa = certificate.GetECDsaPrivateKey();
        if (ecdsa is null)
            throw new SecurityException("O certificado nao possui chave privada RSA ou ECDSA suportada.");
        ValidateSupportedEcdsaPublicKey(certificate);
        if (requireHardwareBackedKey) HardwarePrivateKeyPolicy.Require(ecdsa);
    }

    public static void ValidateSupportedEcdsaPublicKey(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        using var publicKey = certificate.GetECDsaPublicKey()
            ?? throw new SecurityException("O certificado nao possui chave publica ECDSA valida.");
        var parameters = publicKey.ExportParameters(includePrivateParameters: false);
        var curveOid = parameters.Curve.Oid.Value ?? "";
        if (curveOid is not ("1.2.840.10045.3.1.7" or "1.3.132.0.34" or "1.3.132.0.35")
            || publicKey.KeySize is not (256 or 384 or 521))
            throw new SecurityException("A chave ECDSA precisa usar P-256, P-384 ou P-521.");
    }
}

static class HardwarePrivateKeyPolicy
{
    private const string ImplementationTypeProperty = "Impl Type";
    private const int HardwareImplementationFlag = 0x00000001;

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int NCryptGetProperty(
        SafeNCryptKeyHandle objectHandle,
        string propertyName,
        [Out] byte[] output,
        int outputLength,
        out int resultLength,
        int flags);

    public static void Require(AsymmetricAlgorithm privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        using var cngKey = privateKey switch
        {
            RSACng rsa => rsa.Key,
            ECDsaCng ecdsa => ecdsa.Key,
            _ => throw new SecurityException(
                "O emissor exige chave privada CNG em TPM, smart card, token ou HSM.")
        };

        var forbiddenExport = CngExportPolicies.AllowExport
            | CngExportPolicies.AllowPlaintextExport
            | CngExportPolicies.AllowArchiving
            | CngExportPolicies.AllowPlaintextArchiving;
        if ((cngKey.ExportPolicy & forbiddenExport) != 0)
            throw new SecurityException("A chave privada do emissor permite exportacao e foi recusada.");

        var implementation = new byte[sizeof(int)];
        var status = NCryptGetProperty(cngKey.Handle, ImplementationTypeProperty,
            implementation, implementation.Length, out var resultLength, 0);
        if (status != 0 || resultLength != sizeof(int)
            || (BitConverter.ToInt32(implementation, 0) & HardwareImplementationFlag) == 0)
            throw new SecurityException(
                "O Windows nao confirmou que a chave privada do emissor esta protegida por hardware.");
    }
}

sealed record IssuedCommercialLicense(
    string LicenseId,
    string IssuerSpkiSha256,
    byte[] Envelope);

static class LicenseIssuer
{
    private static readonly TimeSpan MaximumRequestAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan AllowedFutureSkew = TimeSpan.FromMinutes(5);

    public static void ValidateActivationRequest(CommercialActivationRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Re-serialization invokes every structural validation even when a
        // caller constructed the record rather than parsing a file.
        _ = CommercialLicenseCodec.SerializeActivationRequest(request);
        if (!request.Product.Equals(LicenseIssuerContract.Product, StringComparison.Ordinal)
            || request.ProductMajor != LicenseIssuerContract.ProductMajor)
            throw new SecurityException("O pedido pertence a outro produto ou versao principal.");
        var generatedAt = DateTimeOffset.FromUnixTimeSeconds(request.GeneratedAtUnixSeconds);
        if (generatedAt > now.Add(AllowedFutureSkew))
            throw new SecurityException("O pedido de ativacao foi gerado no futuro.");
        if (generatedAt < now.Subtract(MaximumRequestAge))
            throw new SecurityException("O pedido de ativacao tem mais de 30 dias; gere um novo pedido.");
    }

    public static IssuedCommercialLicense Issue(
        CommercialActivationRequest request,
        X509Certificate2 certificate,
        DateTimeOffset now,
        bool requireHardwareBackedKey = true)
    {
        ValidateActivationRequest(request, now);
        WindowsCertificateFinder.ValidateForLicenseSigning(certificate, now, requireHardwareBackedKey);

        var certificateDer = certificate.Export(X509ContentType.Cert);
        var trustedIssuer = CommercialLicenseTrustedIssuer.FromCertificate(certificateDer);
        CryptographicOperations.ZeroMemory(certificateDer);

        var payload = new CommercialLicensePayload(
            CommercialLicenseCodec.SchemaVersion,
            CommercialLicenseCodec.LicenseKind,
            Guid.NewGuid().ToString("N"),
            request.RequestId,
            LicenseIssuerContract.Product,
            LicenseIssuerContract.ProductMajor,
            request.MachineKeySha256,
            [LicenseIssuerContract.CommercialFeature],
            now.ToUnixTimeSeconds(),
            null);
        var payloadBytes = CommercialLicenseCodec.SerializePayload(payload);
        var signingMessage = CommercialLicenseCodec.BuildSigningMessage(payloadBytes);
        byte[] signature = Array.Empty<byte>();
        try
        {
            string algorithm;
            using (var rsa = certificate.GetRSAPrivateKey())
            {
                if (rsa is not null)
                {
                    if (rsa.KeySize < 2048)
                        throw new SecurityException("A chave RSA do certificado possui menos de 2048 bits.");
                    algorithm = CommercialLicenseCodec.RsaAlgorithm;
                    signature = rsa.SignData(signingMessage, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
                else
                {
                    using var ecdsa = certificate.GetECDsaPrivateKey()
                        ?? throw new SecurityException("A chave privada ECDSA nao esta acessivel.");
                    WindowsCertificateFinder.ValidateSupportedEcdsaPublicKey(certificate);
                    algorithm = CommercialLicenseCodec.EcdsaAlgorithm;
                    signature = ecdsa.SignData(signingMessage, HashAlgorithmName.SHA256,
                        DSASignatureFormat.Rfc3279DerSequence);
                }
            }

            var envelope = CommercialLicenseCodec.SerializeEnvelope(new CommercialLicenseEnvelope(
                CommercialLicenseCodec.SchemaVersion,
                algorithm,
                trustedIssuer.SpkiSha256,
                payloadBytes,
                signature));

            // Verify the finished bytes with public material only before they
            // are allowed to reach disk. The factory side cannot prove TPM
            // provenance, but it does verify signature, product, feature and
            // exact binding to the fingerprint requested by the kiosk.
            var policy = new CommercialLicensePolicy(
                LicenseIssuerContract.Product,
                LicenseIssuerContract.ProductMajor,
                LicenseIssuerContract.CommercialFeature);
            var verifier = new CommercialLicenseVerifier(
                [trustedIssuer], new ExpectedFingerprintBinding(request.MachineKeySha256),
                policy, new FixedFactoryTimeProvider(now));
            var validation = verifier.Validate(envelope);
            if (!validation.IsValid || validation.LicenseId != payload.LicenseId)
                throw new CryptographicException($"A verificacao interna da licenca falhou: {validation.State}.");

            return new IssuedCommercialLicense(payload.LicenseId, trustedIssuer.SpkiSha256, envelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
            CryptographicOperations.ZeroMemory(signingMessage);
            if (signature.Length != 0) CryptographicOperations.ZeroMemory(signature);
        }
    }

    private sealed class ExpectedFingerprintBinding(string fingerprint) : IPixMachineBinding
    {
        private readonly string _fingerprint = CommercialLicenseCodec.NormalizeSha256Hex(
            fingerprint, "fingerprint do pedido");

        public string GetOrCreateFingerprint() => _fingerprint;

        public void VerifyFingerprint(string expectedFingerprint)
        {
            var expected = Encoding.ASCII.GetBytes(CommercialLicenseCodec.NormalizeSha256Hex(
                expectedFingerprint, "fingerprint da licenca"));
            var actual = Encoding.ASCII.GetBytes(_fingerprint);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                    throw new SecurityException("A licenca diverge do fingerprint solicitado.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expected);
                CryptographicOperations.ZeroMemory(actual);
            }
        }
    }

    private sealed class FixedFactoryTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

static class SafeFactoryFile
{
    public static byte[] ReadLimited(string path, int maximumBytes, string label)
    {
        var fullPath = Path.GetFullPath(path);
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.SequentialScan);
        if (stream.Length is <= 0 || stream.Length > maximumBytes)
            throw new FormatException($"O {label} possui tamanho invalido.");
        var bytes = new byte[(int)stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new IOException($"O {label} terminou antes do esperado.");
            offset += read;
        }
        if (stream.ReadByte() != -1) throw new FormatException($"O {label} excede o limite permitido.");
        return bytes;
    }

    public static void WriteNewAtomically(string destination, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > CommercialLicenseCodec.MaximumEnvelopeBytes)
            throw new FormatException("A licenca pronta possui tamanho invalido.");
        var fullPath = Path.GetFullPath(destination);
        if (File.Exists(fullPath))
            throw new IOException("O arquivo de saida ja existe; nenhuma licenca foi sobrescrita.");
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("A pasta do arquivo de saida e invalida.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory,
            "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: false);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

static class LicenseIssuerSelfTest
{
    public static void Run()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var fingerprint = new string('c', 64);
        var request = new CommercialActivationRequest(
            CommercialLicenseCodec.SchemaVersion,
            CommercialLicenseCodec.ActivationRequestKind,
            "1234567890abcdef1234567890abcdef",
            LicenseIssuerContract.Product,
            LicenseIssuerContract.ProductMajor,
            fingerprint,
            now.ToUnixTimeSeconds());
        var canonicalRequest = CommercialLicenseCodec.SerializeActivationRequest(request);
        _ = CommercialLicenseCodec.ParseActivationRequestStrict(canonicalRequest);

        using var rsa = RSA.Create(2048);
        using var rsaCertificate = CreateCodeSigningCertificate(rsa, now);
        TestCertificateAndIssuance(request, rsaCertificate, now);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var ecdsaCertificate = CreateCodeSigningCertificate(ecdsa, now);
        TestCertificateAndIssuance(request, ecdsaCertificate, now);

        using var ecdsa384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var ecdsa384Certificate = CreateCodeSigningCertificate(ecdsa384, now);
        TestCertificateAndIssuance(request, ecdsa384Certificate, now);

        using var ecdsa521 = ECDsa.Create(ECCurve.NamedCurves.nistP521);
        using var ecdsa521Certificate = CreateCodeSigningCertificate(ecdsa521, now);
        TestCertificateAndIssuance(request, ecdsa521Certificate, now);

        var stale = request with { GeneratedAtUnixSeconds = now.AddDays(-31).ToUnixTimeSeconds() };
        RequireThrows<SecurityException>(() => LicenseIssuer.ValidateActivationRequest(stale, now),
            "pedido antigo foi aceito");
        var future = request with { GeneratedAtUnixSeconds = now.AddMinutes(6).ToUnixTimeSeconds() };
        RequireThrows<SecurityException>(() => LicenseIssuer.ValidateActivationRequest(future, now),
            "pedido futuro foi aceito");

        TestAtomicNoOverwrite(canonicalRequest);
        TestIssuanceLedger(request.RequestId);
        CryptographicOperations.ZeroMemory(canonicalRequest);
    }

    private static void TestCertificateAndIssuance(
        CommercialActivationRequest request,
        X509Certificate2 certificate,
        DateTimeOffset now)
    {
        RequireThrows<SecurityException>(
            () => WindowsCertificateFinder.ValidateForLicenseSigning(certificate, now),
            "chave privada de software foi aceita pelo emissor comercial");
        WindowsCertificateFinder.ValidateForLicenseSigning(certificate, now, requireHardwareBackedKey: false);
        var issued = LicenseIssuer.Issue(request, certificate, now, requireHardwareBackedKey: false);
        var envelope = CommercialLicenseCodec.ParseEnvelopeStrict(issued.Envelope);
        var payload = CommercialLicenseCodec.ParsePayloadStrict(envelope.Payload);
        if (payload.NotAfterUnixSeconds is not null
            || payload.Features.Count != 1
            || payload.Features[0] != LicenseIssuerContract.CommercialFeature
            || payload.ActivationRequestId != request.RequestId
            || payload.MachineKeySha256 != request.MachineKeySha256)
            throw new InvalidOperationException("A licenca emitida nao preservou o contrato perpetuo esperado.");
    }

    private static X509Certificate2 CreateCodeSigningCertificate(
        AsymmetricAlgorithm key,
        DateTimeOffset now)
    {
        var request = key switch
        {
            RSA rsa => new CertificateRequest("CN=TurboRama Issuer Self Test", rsa,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            ECDsa ecdsa => new CertificateRequest("CN=TurboRama Issuer Self Test", ecdsa,
                HashAlgorithmName.SHA256),
            _ => throw new InvalidOperationException("Chave de autoteste nao suportada.")
        };
        var usages = new OidCollection { new("1.3.6.1.5.5.7.3.3", "Code Signing") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        return request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
    }

    private static void TestAtomicNoOverwrite(byte[] bytes)
    {
        var root = Path.Combine(Path.GetTempPath(), "TurboRamaLicenseIssuerSelfTest-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "test.license");
        Directory.CreateDirectory(root);
        try
        {
            SafeFactoryFile.WriteNewAtomically(output, bytes);
            var original = File.ReadAllBytes(output);
            RequireThrows<IOException>(() => SafeFactoryFile.WriteNewAtomically(output, [(byte)'x']),
                "arquivo existente foi sobrescrito");
            if (!original.SequenceEqual(File.ReadAllBytes(output)))
                throw new InvalidOperationException("arquivo existente mudou durante teste de nao sobrescrita");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void TestIssuanceLedger(string requestId)
    {
        var root = Path.Combine(Path.GetTempPath(), "TurboRamaLicenseLedgerSelfTest-" + Guid.NewGuid().ToString("N"));
        var ledgerPath = Path.Combine(root, "issued-requests.log");
        Directory.CreateDirectory(root);
        try
        {
            using (var ledger = IssuanceLedger.Acquire(ledgerPath, TimeSpan.FromSeconds(2)))
            {
                ledger.EnsureNotIssued(requestId);
                ledger.RecordIssued(requestId);
                RequireThrows<SecurityException>(() => ledger.EnsureNotIssued(requestId),
                    "pedido repetido foi aceito no mesmo processo");
            }
            using (var reopened = IssuanceLedger.Acquire(ledgerPath, TimeSpan.FromSeconds(2)))
            {
                RequireThrows<SecurityException>(() => reopened.EnsureNotIssued(requestId),
                    "pedido repetido foi aceito apos reabrir o registro");
            }

            var concurrentRequestId = "abcdefabcdefabcdefabcdefabcdefab";
            using var first = IssuanceLedger.Acquire(ledgerPath, TimeSpan.FromSeconds(2));
            first.EnsureNotIssued(concurrentRequestId);
            var secondStarted = new ManualResetEventSlim(false);
            var second = Task.Run(() =>
            {
                secondStarted.Set();
                using var competing = IssuanceLedger.Acquire(ledgerPath, TimeSpan.FromSeconds(5));
                RequireThrows<SecurityException>(() => competing.EnsureNotIssued(concurrentRequestId),
                    "processo concorrente aceitou pedido ja registrado");
            });
            if (!secondStarted.Wait(TimeSpan.FromSeconds(2)))
                throw new InvalidOperationException("teste concorrente do registro nao iniciou");
            Thread.Sleep(100);
            first.RecordIssued(concurrentRequestId);
            first.Dispose();
            if (!second.Wait(TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("teste concorrente do registro excedeu o tempo limite");
            second.GetAwaiter().GetResult();

            File.AppendAllText(ledgerPath, "linha-invalida\n", Encoding.ASCII);
            RequireThrows<FormatException>(
                () =>
                {
                    using var _ = IssuanceLedger.Acquire(ledgerPath, TimeSpan.FromSeconds(2));
                },
                "registro malformado foi aceito");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }
}
