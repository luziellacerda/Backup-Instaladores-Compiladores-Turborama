namespace TurboRama.Core.Steps;

/// <summary>
/// Perfis do estudo: básico (recomendado), reforçado, arcade dedicado.
/// </summary>
public enum InstallationProfile
{
    /// <summary>Conta kiosk, shell por usuário, autologon, políticas, watchdog.</summary>
    KioskBasic = 0,

    /// <summary>Básico + bloqueios extras, hook de teclado, serviço de manutenção.</summary>
    KioskHardened = 1,

    /// <summary>Reforçado + UWF / Embedded / Keyboard Filter / branding (opcional, com aviso).</summary>
    ArcadeDedicated = 2
}
