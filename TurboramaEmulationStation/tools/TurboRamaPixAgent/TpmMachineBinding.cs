using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

// Vínculo opcional do cofre PIX à máquina. A chave privada é persistente,
// não exportável e criada no Microsoft Platform Crypto Provider, portanto
// permanece dentro do TPM. O fluxo normal só instancia esta classe quando o
// operador ativa RequireTpmMachineBinding; máquinas sem TPM pronto continuam
// com o comportamento anterior e não são alteradas silenciosamente.
interface IPixMachineBinding
{
    string GetOrCreateFingerprint();
    void VerifyFingerprint(string expectedFingerprint);
}

// Extensao usada pelo cofre comercial v3. A chave de dados aleatoria so pode
// ser recuperada por uma operacao RSA privada executada dentro do TPM.
interface IPixMachineSecretBinding : IPixMachineBinding
{
    PixWrappedMachineKey WrapKey(ReadOnlySpan<byte> keyMaterial);
    byte[] UnwrapKey(string expectedFingerprint, ReadOnlySpan<byte> wrappedKey);
}

sealed record PixWrappedMachineKey(string Fingerprint, byte[] WrappedKey);

sealed class TpmCngMachineBinding : IPixMachineSecretBinding
{
    // v2 inclui uso de decriptacao. A chave v1, criada apenas para assinatura,
    // nao e reutilizada nem sobrescrita silenciosamente.
    private const string KeyPrefix = "TurboRama.PixAgent.Binding.v2";
    private const string ImplementationTypeProperty = "Impl Type";
    private const int HardwareImplementationFlag = 0x00000001;
    private static readonly CngProvider PlatformProvider = CngProvider.MicrosoftPlatformCryptoProvider;
    private readonly object _gate = new();

    public string GetOrCreateFingerprint()
    {
        lock (_gate)
        {
            using var key = OpenOrCreateKey();
            return ProveKeyAndGetFingerprint(key);
        }
    }

    public void VerifyFingerprint(string expectedFingerprint)
    {
        var normalized = NormalizeFingerprint(expectedFingerprint);
        lock (_gate)
        {
            using var key = OpenExistingKey();
            var actual = ProveKeyAndGetFingerprint(key);
            RequireSameFingerprint(normalized, actual);
        }
    }

    public PixWrappedMachineKey WrapKey(ReadOnlySpan<byte> keyMaterial)
    {
        if (keyMaterial.Length is < 16 or > 64)
            throw new SecurityException("o tamanho da chave do cofre PIX e invalido");
        lock (_gate)
        {
            using var key = OpenOrCreateKey();
            var fingerprint = ProveKeyAndGetFingerprint(key);
            using var rsa = new RSACng(key);
            return new PixWrappedMachineKey(fingerprint,
                rsa.Encrypt(keyMaterial, RSAEncryptionPadding.OaepSHA256));
        }
    }

    public byte[] UnwrapKey(string expectedFingerprint, ReadOnlySpan<byte> wrappedKey)
    {
        if (wrappedKey.Length is < 128 or > 1024)
            throw new SecurityException("a chave protegida do cofre PIX possui tamanho invalido");
        var normalized = NormalizeFingerprint(expectedFingerprint);
        lock (_gate)
        {
            using var key = OpenExistingKey();
            var actual = ProveKeyAndGetFingerprint(key);
            RequireSameFingerprint(normalized, actual);
            using var rsa = new RSACng(key);
            return rsa.Decrypt(wrappedKey, RSAEncryptionPadding.OaepSHA256);
        }
    }

    internal static string NormalizeFingerprint(string value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        if (normalized.Length != 64 || !normalized.All(static character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f'))
            throw new SecurityException("a impressão do vínculo TPM é inválida");
        return normalized;
    }

    private static void RequireSameFingerprint(string expected, string actual)
    {
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var actualBytes = Encoding.ASCII.GetBytes(actual);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
                throw new SecurityException("o cofre PIX pertence a outro TPM ou a chave da máquina foi substituída");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(actualBytes);
        }
    }

    private static CngKey OpenOrCreateKey()
    {
        try { return OpenExistingKey(); }
        catch (CryptographicException)
        {
            var parameters = new CngKeyCreationParameters
            {
                Provider = PlatformProvider,
                ExportPolicy = CngExportPolicies.None,
                KeyUsage = CngKeyUsages.Decryption,
                KeyCreationOptions = CngKeyCreationOptions.None
            };
            parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
            try { return CngKey.Create(CngAlgorithm.Rsa, KeyName(), parameters); }
            catch (CryptographicException)
            {
                // Outra instância pode ter criado a mesma chave entre o Open
                // e o Create. Nunca usamos OverwriteExistingKey.
                return OpenExistingKey();
            }
        }
    }

    private static CngKey OpenExistingKey()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("o vínculo TPM do PIX exige Windows");
        try
        {
            return CngKey.Open(KeyName(), PlatformProvider, CngKeyOpenOptions.UserKey);
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException(
                "o TPM/CNG não está pronto ou a chave vinculada deste quiosque não existe", ex);
        }
    }

    private static string ProveKeyAndGetFingerprint(CngKey key)
    {
        if (!string.Equals(key.Provider?.Provider, PlatformProvider.Provider, StringComparison.Ordinal)
            || key.AlgorithmGroup != CngAlgorithmGroup.Rsa
			|| key.KeySize < 2048
            || key.IsEphemeral
            || (key.ExportPolicy & (CngExportPolicies.AllowExport | CngExportPolicies.AllowPlaintextExport
                | CngExportPolicies.AllowArchiving | CngExportPolicies.AllowPlaintextArchiving)) != 0
            || (key.KeyUsage & CngKeyUsages.Decryption) == 0)
            throw new SecurityException("a chave do vínculo PIX não é uma chave RSA persistente do TPM");

        byte[] implementation;
        try
        {
            implementation = key.GetProperty(ImplementationTypeProperty, CngPropertyOptions.None).GetValue()
                ?? throw new SecurityException("o provedor TPM nao informou o tipo de implementacao");
        }
        catch (CryptographicException ex)
        {
            throw new SecurityException("o provedor TPM nao comprovou implementacao em hardware", ex);
        }
        try
        {
            if (implementation.Length < sizeof(int)
                || (BitConverter.ToInt32(implementation, 0) & HardwareImplementationFlag) == 0)
                throw new SecurityException("a chave declarada como TPM nao esta em hardware");
        }
        finally { CryptographicOperations.ZeroMemory(implementation); }

        using var rsa = new RSACng(key);
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        var challenge = RandomNumberGenerator.GetBytes(32);
        byte[] encryptedChallenge = Array.Empty<byte>();
        byte[] decryptedChallenge = Array.Empty<byte>();
        try
        {
            encryptedChallenge = rsa.Encrypt(challenge, RSAEncryptionPadding.OaepSHA256);
            decryptedChallenge = rsa.Decrypt(encryptedChallenge, RSAEncryptionPadding.OaepSHA256);
            if (!CryptographicOperations.FixedTimeEquals(challenge, decryptedChallenge))
                throw new CryptographicException("o TPM não comprovou posse da chave vinculada");
            return Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicKey);
            CryptographicOperations.ZeroMemory(challenge);
            if (encryptedChallenge.Length != 0) CryptographicOperations.ZeroMemory(encryptedChallenge);
            if (decryptedChallenge.Length != 0) CryptographicOperations.ZeroMemory(decryptedChallenge);
        }
    }

    private static string KeyName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
            throw new SecurityException("o Windows não informou o SID do usuário do quiosque");
        var sidBytes = Encoding.UTF8.GetBytes(sid);
        try
        {
            var suffix = Convert.ToHexString(SHA256.HashData(sidBytes)).ToLowerInvariant()[..24];
            return KeyPrefix + "." + suffix;
        }
        finally { CryptographicOperations.ZeroMemory(sidBytes); }
    }
}
