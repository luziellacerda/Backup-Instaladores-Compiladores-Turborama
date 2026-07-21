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
    /// Comprimento: 8 (alinhado a <see cref="MinKioskPasswordLength"/>).
    /// </summary>
    public const string KioskPassword = "Lz2026@$";

    /// <summary>
    /// Mínimo de caracteres aceito para senha kiosk (config ou padrão).
    /// Deve ser &lt;= comprimento de <see cref="KioskPassword"/> (senha de fábrica).
    /// </summary>
    public const int MinKioskPasswordLength = 8;

    /// <summary>
    /// Path preferido no JSON de fábrica (pasta clássica). Em runtime use
    /// <see cref="ResolveFrontendExecutable"/> para achar o EXE real no PC.
    /// </summary>
    public const string PreferredFrontendExecutable = @"D:\Turborama\TurboRama.exe";

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

    /// <summary>
    /// Candidatos de frontend em ordem de preferência de descoberta no PC alvo.
    /// Cobre instalação em pasta (D:\Turborama\...) e layout flat (D:\TurboRama.exe + ES na raiz D:\).
    /// </summary>
    public static IReadOnlyList<string> GetFrontendCandidates(string? configuredPath = null, string? packFrontendDir = null)
    {
        var list = new List<string>();
        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p))
            {
                return;
            }

            string full = p.Trim();
            if (!list.Any(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(full);
            }
        }

        Add(configuredPath);
        // Layout flat (setup estável / cópia modelo na raiz de D:)
        Add(@"D:\TurboRama.exe");
        // Pasta clássica de produção
        Add(PreferredFrontendExecutable);
        Add(@"D:\Turborama\TurboRama.exe");
        if (!string.IsNullOrWhiteSpace(packFrontendDir))
        {
            Add(Path.Combine(packFrontendDir, "TurboRama.exe"));
            Add(Path.Combine(packFrontendDir, "Frontend.exe"));
        }

        Add(@"C:\TurboRama\Frontend\TurboRama.exe");
        Add(@"C:\TurboRama\Frontend\Frontend.exe");
        Add(@"D:\Turborama\emulationstation\emulationstation.exe");
        Add(@"D:\emulationstation\emulationstation.exe");
        Add(@"D:\TURBOPCINSTALL\build\TurboRama.exe");
        return list;
    }

    /// <summary>Primeiro candidato que existe no disco, ou null.</summary>
    public static string? FindExistingFrontend(string? configuredPath = null, string? packFrontendDir = null)
    {
        foreach (string c in GetFrontendCandidates(configuredPath, packFrontendDir))
        {
            try
            {
                if (File.Exists(c))
                {
                    return c;
                }
            }
            catch
            {
                /* ignore bad path */
            }
        }

        return null;
    }

    /// <summary>
    /// Path a gravar em config / usar no Launcher: EXE real se existir, senão preferido de fábrica.
    /// </summary>
    public static string ResolveFrontendExecutable(string? configuredPath = null, string? packFrontendDir = null)
        => FindExistingFrontend(configuredPath, packFrontendDir) ?? PreferredFrontendExecutable;
}
