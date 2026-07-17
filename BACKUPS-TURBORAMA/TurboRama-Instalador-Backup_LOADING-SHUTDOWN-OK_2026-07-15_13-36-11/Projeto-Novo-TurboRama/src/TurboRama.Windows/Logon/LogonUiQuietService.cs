using TurboRama.Core.Results;

namespace TurboRama.Windows.Logon;

/// <summary>
/// Reduz a UI de logon Windows no AutoLogon (Welcome / animação / flash de conta).
/// NÃO remove bolinhas de boot do Windows (sem BCD / Unbranded Boot).
/// </summary>
public static class LogonUiQuietService
{
    /// <summary>
    /// Aplica chaves HKLM seguras para o logon automático ser o mais “invisível” possível.
    /// Requer Admin. Falhas parciais não derrubam a instalação.
    /// </summary>
    public static OperationResult ApplyQuietAutoLogonUi()
    {
        int ok = 0;
        int fail = 0;
        var notes = new List<string>();

        // 1) Sem animação de primeiro logon / welcome animado
        if (SetDword(Microsoft.Win32.Registry.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
                "EnableFirstLogonAnimation", 0))
        {
            ok++;
        }
        else
        {
            fail++;
        }

        if (SetDword(Microsoft.Win32.Registry.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                "EnableFirstLogonAnimation", 0))
        {
            ok++;
        }
        else
        {
            fail++;
        }

        // 2) LogonUI sem animação (flash)
        if (SetDword(Microsoft.Win32.Registry.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI",
                "AnimationDisabled", 1))
        {
            ok++;
        }
        else
        {
            fail++;
        }

        // 3) Mensagens de status de logon mais discretas (não é bolinha de boot)
        if (SetDword(Microsoft.Win32.Registry.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                "DisableStatusMessages", 1))
        {
            ok++;
        }
        else
        {
            fail++;
        }

        if (SetDword(Microsoft.Win32.Registry.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                "VerboseStatus", 0))
        {
            ok++;
        }
        else
        {
            fail++;
        }

        // 4) HideAutoLogonUI (quando a edição/Windows aceita a chave EmbeddedLogon)
        //    Esconde a UI de conta durante AutoAdminLogon — efeito desejado no kiosk.
        if (SetDword(Microsoft.Win32.Registry.LocalMachine,
                @"SOFTWARE\Microsoft\Windows Embedded\EmbeddedLogon",
                "HideAutoLogonUI", 1))
        {
            ok++;
            notes.Add("HideAutoLogonUI=1");
        }
        else
        {
            fail++;
            notes.Add("HideAutoLogonUI skip");
        }

        if (SetDword(Microsoft.Win32.Registry.LocalMachine,
                @"SOFTWARE\Microsoft\Windows Embedded\EmbeddedLogon",
                "AnimationDisabled", 1))
        {
            ok++;
        }
        else
        {
            fail++;
        }

        // 5) Não forçar BrandingNeutral=63 (remove botões de shutdown em excesso em algumas edições)
        //    Só UIVerbosity alto para menos chrome de logon
        if (SetDword(Microsoft.Win32.Registry.LocalMachine,
                @"SOFTWARE\Microsoft\Windows Embedded\EmbeddedLogon",
                "UIVerbosityLevel", 1))
        {
            ok++;
        }
        else
        {
            fail++;
        }

        string msg = "Logon UI quiet: ok=" + ok + " fail=" + fail +
                     " (sem BCD/bolinhas). " + string.Join("; ", notes);
        // Sucesso se a maioria crítica passou
        bool success = ok >= 3;
        return success
            ? OperationResult.Ok(msg, "LogonUiQuiet")
            : OperationResult.Fail(msg, "LOGON_UI_QUIET", "LogonUiQuiet");
    }

    private static bool SetDword(Microsoft.Win32.RegistryKey root, string subKey, string name, int value)
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = root.CreateSubKey(subKey, true);
            if (key is null)
            {
                return false;
            }

            key.SetValue(name, value, Microsoft.Win32.RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
