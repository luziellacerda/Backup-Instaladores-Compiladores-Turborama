using System.Security;
using System.Security.Cryptography;
using System.Text;
using TurboBoxManager.Licensing;

namespace TurboRama.EmulationStation.Access;

internal static class LicenseCache
{
    private static readonly byte[] Entropy =
        "TurboRama.EmulationStation.Suite.LicenseId/v1"u8.ToArray();
    private static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TurboRama", "EmulationStation", "Suite", "license-id.dpapi");

    internal static string? TryRead()
    {
        byte[] cipher = [];
        try
        {
            var info = new FileInfo(CachePath);
            if (!info.Exists || info.Length is < 16 or > 2048) return null;
            using var input = new FileStream(info.FullName, FileMode.Open,
                FileAccess.Read, FileShare.Read);
            if (input.Length is < 16 or > 2048) return null;
            cipher = new byte[checked((int)input.Length)];
            input.ReadExactly(cipher);
            return UnprotectIdentifier(cipher);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or CryptographicException or SecurityException or ArgumentException) { return null; }
        finally
        {
            CryptographicOperations.ZeroMemory(cipher);
        }
    }

    internal static void TrySave(AuthorizedStoreContext context)
    {
        byte[] cipher = [];
        string? temporary = null;
        try
        {
            context.ThrowIfUnauthorized();
            var path = CachePath;
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            cipher = ProtectIdentifier(context.LicenseId);
            temporary = Path.Combine(directory, "license-id." + Guid.NewGuid().ToString("N") + ".tmp");
            using (var output = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                output.Write(cipher);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            temporary = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or CryptographicException or SecurityException or ArgumentException)
        {
            // A cache write failure never creates nor extends authorization.
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cipher);
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal static byte[] ProtectIdentifier(string identifier)
    {
        var canonical = SuiteOnlineLicenseProtocol.RequireIdentifier(identifier, "LicenseId", 6, 64);
        if (!string.Equals(identifier, canonical, StringComparison.Ordinal))
            throw new SecurityException("LicenseId não está canônico.");
        var plain = Encoding.UTF8.GetBytes("1\n" + identifier);
        try { return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    internal static string UnprotectIdentifier(byte[] cipher)
    {
        if (cipher.Length is < 16 or > 2048) throw new SecurityException("Cache inválido.");
        var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var value = new UTF8Encoding(false, true).GetString(plain);
            if (!value.StartsWith("1\n", StringComparison.Ordinal))
                throw new SecurityException("Cache inválido.");
            var identifier = value[2..];
            var canonical = SuiteOnlineLicenseProtocol.RequireIdentifier(identifier, "LicenseId", 6, 64);
            if (!string.Equals(identifier, canonical, StringComparison.Ordinal))
                throw new SecurityException("Cache inválido.");
            return identifier;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
