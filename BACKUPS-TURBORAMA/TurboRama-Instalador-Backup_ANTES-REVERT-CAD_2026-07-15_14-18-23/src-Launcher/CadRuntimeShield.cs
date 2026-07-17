using Microsoft.Win32;

namespace TurboRama.Launcher;

/// <summary>
/// Aplica o máximo possível de “anti-CAD” na sessão atual (HKCU),
/// para o Ctrl+Alt+Del ficar inútil e o painel TurboRama ser o menu real.
/// (Bloqueio total de SAS só com Keyboard Filter / IoT — feito no instalador.)
/// </summary>
internal static class CadRuntimeShield
{
    public static void ApplyCurrentUser()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Policies\System", true);
            if (key == null)
            {
                return;
            }

            key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
            key.SetValue("DisableChangePassword", 1, RegistryValueKind.DWord);
            key.SetValue("DisableLockWorkstation", 1, RegistryValueKind.DWord);
            key.SetValue("HideFastUserSwitching", 1, RegistryValueKind.DWord);
        }
        catch
        {
            // Conta sem permissão de policies — ignorar
        }

        try
        {
            using RegistryKey? exp = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", true);
            if (exp != null)
            {
                exp.SetValue("NoLogoff", 1, RegistryValueKind.DWord);
                exp.SetValue("StartMenuLogOff", 1, RegistryValueKind.DWord);
            }
        }
        catch
        {
            // ignore
        }
    }
}
