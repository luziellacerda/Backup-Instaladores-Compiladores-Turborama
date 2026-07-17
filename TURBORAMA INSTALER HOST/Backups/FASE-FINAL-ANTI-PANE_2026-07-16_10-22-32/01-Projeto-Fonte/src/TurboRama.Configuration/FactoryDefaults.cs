namespace TurboRama.Configuration;

/// <summary>
/// Valores de fábrica embutidos no instalador (não dependem de script manual).
/// </summary>
public static class FactoryDefaults
{
    /// <summary>Conta kiosk padrão.</summary>
    public const string KioskUserName = "Arcade";

    /// <summary>
    /// Senha padrão do kiosk na linha de montagem.
    /// Usada na Fase 2 se <see cref="ProductConfiguration.KioskPassword"/> estiver vazia.
    /// Autologon e DPAPI usam a mesma senha.
    /// </summary>
    public const string KioskPassword = "Lz2026@$";

    /// <summary>Mínimo de caracteres aceito para senha kiosk (config ou padrão).</summary>
    public const int MinKioskPasswordLength = 8;

    /// <summary>
    /// Resolve senha efetiva: config explícita (se válida) senão senha de fábrica.
    /// </summary>
    public static string ResolveKioskPassword(ProductConfiguration? config)
    {
        string? fromConfig = config?.KioskPassword?.Trim();
        if (!string.IsNullOrEmpty(fromConfig) && fromConfig.Length >= MinKioskPasswordLength)
        {
            return fromConfig;
        }

        return KioskPassword;
    }

    /// <summary>
    /// PIN do menu segurança (Ctrl+End) = senha kiosk de fábrica <see cref="KioskPassword"/>
    /// (Lz2026@$), salvo se SecurityMenuPin estiver explicitamente no config.
    /// </summary>
    public static string ResolveSecurityMenuPin(ProductConfiguration? config)
    {
        string? fromConfig = config?.SecurityMenuPin?.Trim();
        // Ignorar pin antigo de teste se ainda estiver em algum JSON
        if (!string.IsNullOrEmpty(fromConfig) &&
            fromConfig.Length >= 4 &&
            !fromConfig.Equals("Lz2026@Sec", StringComparison.Ordinal))
        {
            return fromConfig;
        }

        return ResolveKioskPassword(config);
    }
}
