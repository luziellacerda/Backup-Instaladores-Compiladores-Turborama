using System.Diagnostics;
using TurboRama.Core.Results;

namespace TurboRama.Windows.Security;

/// <summary>
/// Verificação soft de assinatura Authenticode (proposta §8/§27).
/// Não bloqueia instalação se não assinado — apenas reporta.
/// </summary>
public static class AuthenticodeHelper
{
    public static OperationResult CheckFile(string path)
    {
        if (!File.Exists(path))
        {
            return OperationResult.Fail("Arquivo ausente: " + path, "SIG_MISS", "Authenticode");
        }

        try
        {
            // Verificação leve: se não houver cert embutido no alpha, reporta NotSigned sem PowerShell pesado.
            // (Get-AuthenticodeSignature pode ser lento em alguns hosts.)
            try
            {
                var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
                if (cert is not null && !string.IsNullOrWhiteSpace(cert.Subject))
                {
                    return OperationResult.Ok(
                        "Assinado: " + Path.GetFileName(path) + " (" + cert.Subject + ")",
                        "Authenticode",
                        currentState: "Signed");
                }
            }
            catch
            {
                // CreateFromSignedFile lança se não assinado
            }

            return OperationResult.Ok(
                "Não assinado (esperado em alpha): " + Path.GetFileName(path),
                "Authenticode",
                currentState: "NotSigned");
        }
        catch (Exception ex)
        {
            return OperationResult.Ok("Assinatura: " + ex.Message, "Authenticode", currentState: "Error");
        }
    }
}
