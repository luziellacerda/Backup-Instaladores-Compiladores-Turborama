using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Som da tela de loading (modelo pack estável).
/// Ordem: config → boot-up.mp3 (se existir) → boot.wav (padrão) → Assets ao lado do EXE.
/// WAV: System.Media.SoundPlayer (confiável). MP3: MCI Windows.
/// Tutorial: Assets\TUTORIAL-SOM-LOADING.txt
/// </summary>
internal sealed class BootSoundPlayer : IDisposable
{
    private const string MciAlias = "TurboRamaBootSound";
    private SoundPlayer? _wavPlayer;
    private bool _mciOpen;
    private bool _disposed;

    /// <summary>Resolve o ficheiro de som a tocar.</summary>
    public static string ResolveSoundPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string cfg = System.IO.Path.GetFullPath(configuredPath.Trim());
            if (System.IO.File.Exists(cfg))
            {
                return cfg;
            }
        }

        // Candidatos: MP3 custom do técnico primeiro, depois WAV estável
        string[] candidates =
        {
            ProductPaths.DefaultBootSoundMp3,
            ProductPaths.DefaultBootSoundWav,
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "boot-up.mp3"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "boot.wav"),
            // Compat pack antigo C:\TurboRama\Launcher\assets\
            System.IO.Path.Combine(ProductPaths.Root, "Launcher", "assets", "boot.wav"),
            System.IO.Path.Combine(ProductPaths.Root, "Launcher", "assets", "boot-up.mp3"),
        };

        foreach (string c in candidates)
        {
            if (System.IO.File.Exists(c))
            {
                return c;
            }
        }

        return ProductPaths.DefaultBootSoundWav;
    }

    public bool TryPlay(string? configuredPath, ITurboRamaLogger? logger)
    {
        string path = ResolveSoundPath(configuredPath);
        if (!System.IO.File.Exists(path))
        {
            logger?.Warning("BootSound",
                "Som de boot ausente. Coloque boot.wav ou boot-up.mp3 em " +
                ProductPaths.AppLauncherAssets + " (ver TUTORIAL-SOM-LOADING.txt).");
            return false;
        }

        string ext = System.IO.Path.GetExtension(path);
        try
        {
            StopInternal();

            if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                _wavPlayer = new SoundPlayer(path);
                _wavPlayer.Load();
                _wavPlayer.Play(); // async — não bloqueia a UI
                logger?.Info("BootSound", "Tocando WAV: " + path);
                return true;
            }

            // MP3 / outros via MCI
            if (TryPlayMci(path, logger))
            {
                logger?.Info("BootSound", "Tocando MCI: " + path);
                return true;
            }

            logger?.Warning("BootSound", "Falha ao tocar: " + path);
            return false;
        }
        catch (Exception ex)
        {
            logger?.Warning("BootSound", "Não foi possível tocar som: " + ex.Message);
            StopInternal();
            return false;
        }
    }

    public void Stop() => StopInternal();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopInternal();
    }

    private bool TryPlayMci(string path, ITurboRamaLogger? logger)
    {
        string openCmd = "open \"" + path + "\" type mpegvideo alias " + MciAlias;
        int openRc = mciSendString(openCmd, null, 0, IntPtr.Zero);
        if (openRc != 0)
        {
            openCmd = "open \"" + path + "\" alias " + MciAlias;
            openRc = mciSendString(openCmd, null, 0, IntPtr.Zero);
        }

        if (openRc != 0)
        {
            logger?.Warning("BootSound", "MCI open rc=" + openRc + " path=" + path);
            return false;
        }

        _mciOpen = true;
        int playRc = mciSendString("play " + MciAlias, null, 0, IntPtr.Zero);
        if (playRc != 0)
        {
            logger?.Warning("BootSound", "MCI play rc=" + playRc);
            StopInternal();
            return false;
        }

        return true;
    }

    private void StopInternal()
    {
        try
        {
            _wavPlayer?.Stop();
            _wavPlayer?.Dispose();
            _wavPlayer = null;
        }
        catch
        {
            _wavPlayer = null;
        }

        if (_mciOpen)
        {
            try
            {
                mciSendString("stop " + MciAlias, null, 0, IntPtr.Zero);
                mciSendString("close " + MciAlias, null, 0, IntPtr.Zero);
            }
            catch
            {
            }

            _mciOpen = false;
        }
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr winHandle);
}
