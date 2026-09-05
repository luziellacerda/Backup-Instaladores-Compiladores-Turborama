using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TurboRamaSuite.Network;

public sealed record NetworkInterfaceSignal(string Mac, string InterfaceType, bool LocallyAdministered, bool Virtual);
public sealed record NetworkInventoryContext(int SchemaVersion, string ProductId, string LicenseId,
    string DeviceId, string SessionId, string AppScope, string Action, string HardwareFingerprint,
    string ClientVersion, long CollectedAtUnixSeconds, NetworkInterfaceSignal[] Interfaces);
public sealed record NetworkChallengeRequest(int SchemaVersion, string ProductId, string LicenseId,
    string DeviceId, string SessionId, string AppScope, string Action, string ContextHash);
public sealed record NetworkInventoryProof(NetworkInventoryContext Context, string ChallengeId, string Signature);
public sealed record NetworkAssertion(int SchemaVersion, string Kind, string ProductId, string LicenseId,
    string DeviceId, string SessionId, string AppScope, string Action, string ContextHash,
    string ChallengeId, string Nonce, string Status, long ServerTimeUnixSeconds, long ExpiresAtUnixSeconds);
public sealed record NetworkMachineProof(int SchemaVersion, string ProductId, string LicenseId,
    string DeviceId, string SessionId, string AppScope, string Action, string ContextHash,
    string ChallengeId, string Nonce, long ExpiresAtUnixSeconds);

// This source is shared verbatim with the ES helper. It adds a contract without
// modifying any Suite v1 DTO, machine-proof domain or hardware identity bytes.
public static class NetworkInventoryContract
{
    public const string Product = "TURBORAMA_SUITE";
    public const string Action = "network.inventory.submit";
    public const string ChallengeRoute = "/v1/suite/network/challenges";
    public const string InventoryRoute = "/v1/suite/network/inventory";
    public const string ChallengeKind = "TURBORAMA_SUITE_NETWORK_CHALLENGE_V1";
    public const string ResultKind = "TURBORAMA_SUITE_NETWORK_RESULT_V1";
    public const string ChallengeDomain = "TurboRamaSuiteNetworkAssertion/challenge/v1\0";
    public const string ResultDomain = "TurboRamaSuiteNetworkAssertion/result/v1\0";
    public const string MachineDomain = "TurboRamaSuiteNetworkMachineProof/v1\0";
    public const int MaximumBodyBytes = 8192;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8,
        Encoder = JavaScriptEncoder.Default, NumberHandling = JsonNumberHandling.Strict
    };

    public static byte[] Canonical<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    public static string Hash(NetworkInventoryContext context) =>
        Convert.ToHexString(SHA256.HashData(Canonical(context))).ToLowerInvariant();
    public static byte[] SigningMessage(NetworkMachineProof proof) =>
        Encoding.ASCII.GetBytes(MachineDomain).Concat(Canonical(proof)).ToArray();
    public static string Domain(NetworkAssertion value) => value.Kind switch
    {
        ChallengeKind => ChallengeDomain, ResultKind => ResultDomain,
        _ => throw new SecurityException("Network assertion kind is invalid.")
    };

    public static void Validate(NetworkChallengeRequest request)
    {
        if (request.SchemaVersion != 1 || request.ProductId != Product || request.Action != Action ||
            request.AppScope is not ("SUITE" or "EMULATIONSTATION") || !Identifier(request.LicenseId) ||
            !Hex(request.DeviceId) || !Hex(request.SessionId) || !Hex(request.ContextHash))
            throw new SecurityException("Network request is invalid.");
    }

    public static void Validate(NetworkInventoryContext context, long now)
    {
        Validate(new(1, context.ProductId, context.LicenseId, context.DeviceId,
            context.SessionId, context.AppScope, context.Action, new string('a',64)));
        if (context.SchemaVersion != 1 || !Hex(context.HardwareFingerprint) ||
            context.ClientVersion is not { Length: >= 1 and <= 64 } ||
            context.ClientVersion.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')) ||
            context.CollectedAtUnixSeconds < now - 300 || context.CollectedAtUnixSeconds > now + 60 ||
            context.Interfaces is null || context.Interfaces.Length > 8)
            throw new SecurityException("Network context is invalid.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in context.Interfaces)
        {
            if (item is null || item.Mac is not { Length: 17 } ||
                item.InterfaceType is not ("ETHERNET" or "WIRELESS") || !seen.Add(item.Mac))
                throw new SecurityException("Network interface is invalid.");
            for (var i = 0; i < 17; i++)
                if (i % 3 == 2 ? item.Mac[i] != ':' :
                    !(item.Mac[i] is >= '0' and <= '9' or >= 'A' and <= 'F'))
                    throw new SecurityException("Network address is invalid.");
            var first = Convert.ToByte(item.Mac[..2],16);
            if ((first & 1) != 0 || item.Mac == "00:00:00:00:00:00" ||
                item.LocallyAdministered != ((first & 2) != 0))
                throw new SecurityException("Network interface marker is invalid.");
        }
    }

    public static bool Hex(string? value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool Identifier(string? value) => value is { Length: >= 6 and <= 64 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public static string MaskMac(string value) => "**:**:**:**:" + value[^5..];
    public static string NormalizeIp(IPAddress address) =>
        (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    public static string MaskIp(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 ? $"{bytes[0]}.{bytes[1]}.*.*" :
            $"{Convert.ToHexString(bytes.AsSpan(0,2)).ToLowerInvariant()}:{Convert.ToHexString(bytes.AsSpan(2,2)).ToLowerInvariant()}:*:*";
    }
}
