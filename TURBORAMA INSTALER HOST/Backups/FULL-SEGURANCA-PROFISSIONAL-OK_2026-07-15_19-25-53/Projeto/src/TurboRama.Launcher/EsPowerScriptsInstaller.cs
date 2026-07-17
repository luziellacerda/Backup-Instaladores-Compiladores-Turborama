using TurboRama.Core.Logging;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Garante scripts EmulationStation (quit/shutdown/reboot) que gravam power-request.txt.
/// Idempotente — seguro chamar em cada arranque do Launcher.
/// </summary>
internal static class EsPowerScriptsInstaller
{
    public static void EnsureInstalled(ITurboRamaLogger? logger = null)
    {
        try
        {
            string[] destRoots =
            {
                Path.Combine(@"D:\Turborama", "emulationstation", ".emulationstation", "scripts"),
                Path.Combine(ProductPaths.Frontend, "emulationstation", ".emulationstation", "scripts"),
            };

            foreach (string destRoot in destRoots)
            {
                // Só cria se o pai .emulationstation já existir (instalação real)
                string esConfigDir = Path.GetDirectoryName(destRoot) ?? "";
                if (!Directory.Exists(esConfigDir))
                {
                    continue;
                }

                // quit e shutdown = desligar PC no kiosk (menu "Desligar" do ES usa pasta quit)
                WriteAction(destRoot, "shutdown", "shutdown", killFrontend: true);
                WriteAction(destRoot, "reboot", "reboot", killFrontend: true);
                WriteAction(destRoot, "quit", "shutdown", killFrontend: true);
                logger?.Info("Launcher", "ES power scripts OK em " + destRoot);
            }
        }
        catch (Exception ex)
        {
            logger?.Warning("Launcher", "ES power scripts: " + ex.Message);
        }
    }

    private static void WriteAction(string destRoot, string action, string token, bool killFrontend)
    {
        string dir = Path.Combine(destRoot, action);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "01-turborama-power-request.bat");
        // killFrontend: fecha ES depressa para a splash TurboRama aparecer (sem ecrã preto)
        string kill = killFrontend
            ? "shutdown /a >nul 2>&1\r\n" +
              "taskkill /IM emulationstation.exe /F >nul 2>&1\r\n" +
              "taskkill /IM TurboRama.exe /F >nul 2>&1\r\n"
            : "";
        string bat =
            "@echo off\r\n" +
            "set \"TR_STATE=C:\\TurboRama\\State\"\r\n" +
            "if not exist \"%TR_STATE%\" mkdir \"%TR_STATE%\" 2>nul\r\n" +
            "> \"%TR_STATE%\\power-request.txt\" echo " + token + "\r\n" +
            kill +
            "exit /b 0\r\n";
        File.WriteAllText(path, bat);
    }
}
