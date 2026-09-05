using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using TurboBoxManager.Licensing;

namespace TurboRama.EmulationStation.Access;

// Presentation only. These messages never grant access, alter a key, retry an
// activation, or display server bodies, exception text or customer identifiers.
internal static class AccessFailurePresentation
{
    internal const string ServerUnavailable =
        "O acesso do EmulationStation ainda não está disponível no servidor. "
        + "A integração precisa ser habilitada; não refaça a ativação do Suite.";
    internal const string ServerUnconfirmed =
        "O servidor não retornou uma confirmação válida para o EmulationStation. "
        + "A integração precisa ser verificada; isso não significa que sua licença foi desativada.";
    internal const string AccessDenied =
        "O servidor não autorizou esta licença neste computador. "
        + "Confira o vínculo existente com o administrador, sem criar outra ativação.";
    internal const string SessionConflict =
        "Já existe uma sessão EmulationStation neste computador. "
        + "Solicite ao administrador o encerramento da sessão anterior no painel e tente novamente.";
    internal const string ExistingIdentityUnavailable =
        "Não foi possível usar a identificação existente do Suite nesta conta do Windows. "
        + "Abra o Suite na mesma conta já autorizada e confira o acesso.";
    internal const string SecureConnectionUnavailable =
        "Não foi possível confirmar o acesso por uma conexão segura com o servidor. "
        + "Confira a conexão e tente novamente.";
    internal const string InvalidIdentifier =
        "Informe somente o identificador da licença já usado no Suite, no formato TS-… .";

    internal static string Describe(Exception exception) => exception switch
    {
        SuiteApiException { Code: "ES_SESSION_CONFLICT" } => SessionConflict,
        SuiteApiException { Code: "LICENSE_NOT_FOUND" or "LICENSE_DENIED" or "DEVICE_DENIED" }
            => AccessDenied,
        SuiteApiException { StatusCode: 404 or 503 } => ServerUnavailable,
        SuiteApiException { Code: "INVALID_RESPONSE" } => ServerUnconfirmed,
        SuiteApiException { StatusCode: 429 } =>
            "O servidor recebeu muitas tentativas. Aguarde um pouco antes de tentar novamente.",
        SuiteApiException { StatusCode: >= 500 } => SecureConnectionUnavailable,
        SuiteApiException or SuiteAuthorizationException => AccessDenied,
        SuiteLicensingUnavailableException { FailureCode: "IDENTITY_UNAVAILABLE" }
            => ExistingIdentityUnavailable,
        SuiteLicensingUnavailableException => ServerUnconfirmed,
        HttpRequestException or TaskCanceledException => SecureConnectionUnavailable,
        CryptographicException or UnauthorizedAccessException
            or PlatformNotSupportedException or NotSupportedException
            => ExistingIdentityUnavailable,
        SecurityException => ServerUnconfirmed,
        ArgumentException => InvalidIdentifier,
        _ => "Não foi possível confirmar o acesso. Tente novamente ou consulte o administrador."
    };
}
