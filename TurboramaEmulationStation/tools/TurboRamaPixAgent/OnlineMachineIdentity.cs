using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

interface IOnlineMachineIdentity
{
    OnlineDeviceDescriptor Describe();
    string Sign(OnlineChallengeResponse challenge, string licenseId, string sessionId,
        string action, string contextHash);
}

sealed class CngOnlineMachineIdentity : IOnlineMachineIdentity
{
    private const string KeyPrefix = "TurboRama.PixAgent.OnlineIdentity.v1";
    private const string ImplementationTypeProperty = "Impl Type";
    private const int HardwareImplementationFlag = 0x00000001;
    private readonly OnlineProtectionProfile _profile;
    private readonly CngProvider _provider;
    private readonly object _gate = new();

    public CngOnlineMachineIdentity(OnlineProtectionProfile profile)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("A identidade on-line do TurboRama exige Windows.");
        _profile = profile;
        _provider = profile switch
        {
            OnlineProtectionProfile.TpmBound => CngProvider.MicrosoftPlatformCryptoProvider,
            OnlineProtectionProfile.SoftwareBoundOnline => CngProvider.MicrosoftSoftwareKeyStorageProvider,
            OnlineProtectionProfile.UsbTokenBound => throw new PlatformNotSupportedException(
                "USB_TOKEN_BOUND exige que o modelo de token e o provedor CNG sejam configurados explicitamente."),
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        };
    }

    public OnlineDeviceDescriptor Describe()
    {
        lock (_gate)
        {
            using var key = OpenOrCreateKey();
            ValidateKey(key);
            using var rsa = new RSACng(key);
            var spki = rsa.ExportSubjectPublicKeyInfo();
            try
            {
                var deviceId = OnlineLicenseProtocol.DeviceIdFromSpki(spki);
                return new OnlineDeviceDescriptor(OnlineLicenseProtocol.SchemaVersion, deviceId,
                    OnlineProtectionProfileCodec.Format(_profile), OnlineLicenseProtocol.SigningAlgorithm,
                    Convert.ToBase64String(spki), HardwareFingerprint.Create(), AgentVersion());
            }
            finally { CryptographicOperations.ZeroMemory(spki); }
        }
    }

    public string Sign(OnlineChallengeResponse challenge, string licenseId, string sessionId,
        string action, string contextHash)
    {
        lock (_gate)
        {
            using var key = OpenExistingKey();
            ValidateKey(key);
            using var rsa = new RSACng(key);
            var descriptor = DescribeWithOpenKey(rsa);
            var message = OnlineLicenseProtocol.BuildSigningMessage(challenge, licenseId,
                descriptor.DeviceId, sessionId, action, contextHash);
            byte[] signature = Array.Empty<byte>();
            try
            {
                signature = rsa.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                return Convert.ToBase64String(signature);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(message);
                if (signature.Length != 0) CryptographicOperations.ZeroMemory(signature);
            }
        }
    }

    private OnlineDeviceDescriptor DescribeWithOpenKey(RSA rsa)
    {
        var spki = rsa.ExportSubjectPublicKeyInfo();
        try
        {
            return new OnlineDeviceDescriptor(OnlineLicenseProtocol.SchemaVersion,
                OnlineLicenseProtocol.DeviceIdFromSpki(spki), OnlineProtectionProfileCodec.Format(_profile),
                OnlineLicenseProtocol.SigningAlgorithm, Convert.ToBase64String(spki),
                HardwareFingerprint.Create(), AgentVersion());
        }
        finally { CryptographicOperations.ZeroMemory(spki); }
    }

    private CngKey OpenOrCreateKey()
    {
        try { return OpenExistingKey(); }
        catch (CryptographicException)
        {
            var parameters = new CngKeyCreationParameters
            {
                Provider = _provider,
                ExportPolicy = CngExportPolicies.None,
                KeyUsage = CngKeyUsages.Signing,
                KeyCreationOptions = CngKeyCreationOptions.None
            };
            parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
            try { return CngKey.Create(CngAlgorithm.Rsa, KeyName(), parameters); }
            catch (CryptographicException) { return OpenExistingKey(); }
        }
    }

    private CngKey OpenExistingKey()
    {
        try { return CngKey.Open(KeyName(), _provider, CngKeyOpenOptions.UserKey); }
        catch (CryptographicException ex)
        {
            throw new CryptographicException(_profile == OnlineProtectionProfile.TpmBound
                ? "O TPM nao esta pronto ou perdeu a chave de identidade on-line deste quiosque."
                : "A identidade on-line protegida deste usuario Windows nao esta disponivel.", ex);
        }
    }

    private void ValidateKey(CngKey key)
    {
        if (!string.Equals(key.Provider?.Provider, _provider.Provider, StringComparison.Ordinal)
            || key.AlgorithmGroup != CngAlgorithmGroup.Rsa || key.KeySize < 2048 || key.IsEphemeral
            || (key.ExportPolicy & (CngExportPolicies.AllowExport | CngExportPolicies.AllowPlaintextExport
                | CngExportPolicies.AllowArchiving | CngExportPolicies.AllowPlaintextArchiving)) != 0
            || (key.KeyUsage & CngKeyUsages.Signing) == 0)
            throw new SecurityException("A chave de identidade on-line nao atende a politica comercial.");

        if (_profile == OnlineProtectionProfile.TpmBound)
        {
            byte[] implementation;
            try
            {
                implementation = key.GetProperty(ImplementationTypeProperty, CngPropertyOptions.None).GetValue()
                    ?? throw new SecurityException("O provedor TPM nao informou o tipo de implementacao.");
            }
            catch (CryptographicException ex)
            {
                throw new SecurityException("O provedor TPM nao comprovou implementacao em hardware.", ex);
            }
            try
            {
                if (implementation.Length < sizeof(int)
                    || (BitConverter.ToInt32(implementation, 0) & HardwareImplementationFlag) == 0)
                    throw new SecurityException("A chave declarada como TPM nao esta em hardware.");
            }
            finally { CryptographicOperations.ZeroMemory(implementation); }
        }
    }

    private string KeyName() => KeyPrefix + "." + OnlineProtectionProfileCodec.Format(_profile) + "." + SidSuffix();

    private static string SidSuffix()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new SecurityException("O Windows nao informou o SID do quiosque.");
        var bytes = Encoding.UTF8.GetBytes(sid);
        try { return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..24]; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static string AgentVersion()
        => typeof(CngOnlineMachineIdentity).Assembly.GetName().Version?.ToString() ?? "25.0.0.0";
}

sealed class SoftwareCngMachineBinding : IPixMachineSecretBinding
{
    private const string KeyPrefix = "TurboRama.PixAgent.SoftwareBinding.v1";
    private static readonly CngProvider Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider;
    private readonly object _gate = new();

    public string GetOrCreateFingerprint()
    {
        lock (_gate) { using var key = OpenOrCreateKey(); return FingerprintAndProve(key); }
    }

    public void VerifyFingerprint(string expectedFingerprint)
    {
        var expected = TpmCngMachineBinding.NormalizeFingerprint(expectedFingerprint);
        lock (_gate)
        {
            using var key = OpenExistingKey();
            var actual = FingerprintAndProve(key);
            if (!OnlineLicenseProtocol.FixedHexEquals(expected, actual))
                throw new SecurityException("A licenca pertence a outra instalacao Windows.");
        }
    }

    public PixWrappedMachineKey WrapKey(ReadOnlySpan<byte> keyMaterial)
    {
        if (keyMaterial.Length is < 16 or > 64) throw new SecurityException("A chave do cofre e invalida.");
        lock (_gate)
        {
            using var key = OpenOrCreateKey();
            var fingerprint = FingerprintAndProve(key);
            using var rsa = new RSACng(key);
            return new PixWrappedMachineKey(fingerprint, rsa.Encrypt(keyMaterial, RSAEncryptionPadding.OaepSHA256));
        }
    }

    public byte[] UnwrapKey(string expectedFingerprint, ReadOnlySpan<byte> wrappedKey)
    {
        if (wrappedKey.Length is < 128 or > 1024) throw new SecurityException("A chave protegida e invalida.");
        VerifyFingerprint(expectedFingerprint);
        lock (_gate)
        {
            using var key = OpenExistingKey();
            using var rsa = new RSACng(key);
            return rsa.Decrypt(wrappedKey, RSAEncryptionPadding.OaepSHA256);
        }
    }

    private static CngKey OpenOrCreateKey()
    {
        try { return OpenExistingKey(); }
        catch (CryptographicException)
        {
            var parameters = new CngKeyCreationParameters
            {
                Provider = Provider,
                ExportPolicy = CngExportPolicies.None,
                KeyUsage = CngKeyUsages.Decryption,
                KeyCreationOptions = CngKeyCreationOptions.None
            };
            parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
            try { return CngKey.Create(CngAlgorithm.Rsa, KeyName(), parameters); }
            catch (CryptographicException) { return OpenExistingKey(); }
        }
    }

    private static CngKey OpenExistingKey()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("O vinculo de software exige Windows.");
        return CngKey.Open(KeyName(), Provider, CngKeyOpenOptions.UserKey);
    }

    private static string FingerprintAndProve(CngKey key)
    {
        if (!string.Equals(key.Provider?.Provider, Provider.Provider, StringComparison.Ordinal)
            || key.AlgorithmGroup != CngAlgorithmGroup.Rsa || key.KeySize < 2048 || key.IsEphemeral
            || (key.ExportPolicy & (CngExportPolicies.AllowExport | CngExportPolicies.AllowPlaintextExport
                | CngExportPolicies.AllowArchiving | CngExportPolicies.AllowPlaintextArchiving)) != 0
            || (key.KeyUsage & CngKeyUsages.Decryption) == 0)
            throw new SecurityException("A chave de software vinculada nao atende a politica comercial.");
        using var rsa = new RSACng(key);
        var spki = rsa.ExportSubjectPublicKeyInfo();
        try { return OnlineLicenseProtocol.DeviceIdFromSpki(spki); }
        finally { CryptographicOperations.ZeroMemory(spki); }
    }

    private static string KeyName() => KeyPrefix + "." + CngOnlineMachineIdentitySidSuffix();

    private static string CngOnlineMachineIdentitySidSuffix()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new SecurityException("O Windows nao informou o SID do quiosque.");
        var bytes = Encoding.UTF8.GetBytes(sid);
        try { return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..24]; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

static class HardwareFingerprint
{
    public static string Create()
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["machineGuid"] = ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Cryptography", "MachineGuid"),
            ["biosManufacturer"] = ReadRegistry(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "BIOSVendor"),
            ["biosVersion"] = ReadRegistry(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "BIOSVersion"),
            ["systemManufacturer"] = ReadRegistry(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer"),
            ["systemProduct"] = ReadRegistry(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName"),
            ["baseboard"] = ReadRegistry(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct"),
            ["processor"] = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "",
            ["architecture"] = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()
        };
        var canonical = string.Join("\n", values.Select(pair => pair.Key + "=" + Normalize(pair.Value)));
        var bytes = Encoding.UTF8.GetBytes("TurboRamaHardwareFingerprint/v1\0" + canonical);
        try { return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static string ReadRegistry(RegistryKey root, string path, string name)
    {
        try { using var key = root.OpenSubKey(path, writable: false); return key?.GetValue(name)?.ToString() ?? ""; }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { return ""; }
    }

    private static string Normalize(string value)
        => string.Join(' ', value.Trim().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

static class MachineBindingFactory
{
    public static IPixMachineBinding Create(PixOptions options)
    {
        if (!options.OnlineLicensingEnabled) return new TpmCngMachineBinding();
        var profile = OnlineProtectionProfileCodec.Parse(options.Online.ProtectionProfile);
        return profile switch
        {
            OnlineProtectionProfile.TpmBound => new TpmCngMachineBinding(),
            OnlineProtectionProfile.SoftwareBoundOnline => new SoftwareCngMachineBinding(),
            OnlineProtectionProfile.UsbTokenBound => throw new PlatformNotSupportedException(
                "USB_TOKEN_BOUND ainda exige a selecao explicita do modelo e provedor CNG do token."),
            _ => throw new SecurityException("Perfil de vinculo comercial invalido.")
        };
    }
}
