using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Som da tela de loading.
/// Caminho padrão: C:\TurboRama\App\Launcher\Assets\boot.wav
/// No shell Arcade o áudio pode atrasar — usa PlaySound (winmm) + retries.
/// </summary>
internal sealed class BootSoundPlayer : IDisposable
{
    private const string MciAlias = "TurboRamaBootSound";
    private SoundPlayer? _wavPlayer;
    private bool _mciOpen;
    private bool _playSoundActive;
    private bool _disposed;
    private string? _lastPath;

    // winmm PlaySound — mais fiável no arranque de sessão que SoundPlayer.Play()
    private const uint SND_SYNC = 0x0000;
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_FILENAME = 0x00020000;
    private const uint SND_NOSTOP = 0x0010;

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

        // Prioridade: boot.wav (padrão estável), depois MP3 opcional, depois pasta do EXE
        string[] candidates =
        {
            ProductPaths.DefaultBootSoundWav, // C:\TurboRama\App\Launcher\Assets\boot.wav
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "boot.wav"),
            ProductPaths.DefaultBootSoundMp3,
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "boot-up.mp3"),
            System.IO.Path.Combine(ProductPaths.Root, "Launcher", "assets", "boot.wav"),
            System.IO.Path.Combine(ProductPaths.Root, "Launcher", "assets", "boot-up.mp3"),
        };

        foreach (string c in candidates)
        {
            try
            {
                if (System.IO.File.Exists(c))
                {
                    return System.IO.Path.GetFullPath(c);
                }
            }
            catch
            {
            }
        }

        return ProductPaths.DefaultBootSoundWav;
    }

    /// <summary>
    /// Toca o som. Recomenda-se chamar DEPOIS da form de loading estar visível.
    /// Faz até 3 tentativas com atraso (áudio do Windows às vezes não está pronto no logon).
    /// </summary>
    public bool TryPlay(string? configuredPath, ITurboRamaLogger? logger)
    {
        string path = ResolveSoundPath(configuredPath);
        _lastPath = path;

        if (!System.IO.File.Exists(path))
        {
            logger?.Warning("BootSound",
                "Som ausente: " + path +
                " — coloque boot.wav em " + ProductPaths.AppLauncherAssets);
            return false;
        }

        // 2 tentativas rápidas (não bloquear a loading em sleeps longos)
        if (TryPlayOnce(path, logger, attempt: 1))
        {
            return true;
        }

        Thread.Sleep(120);
        if (TryPlayOnce(path, logger, attempt: 2))
        {
            return true;
        }

        logger?.Warning("BootSound", "Falhou a tocar: " + path);
        return false;
    }

    /// <summary>Repete o som a meio do loading se a 1ª tentativa foi cedo demais.</summary>
    public void EnsurePlaying(ITurboRamaLogger? logger)
    {
        if (_disposed || string.IsNullOrEmpty(_lastPath) || !System.IO.File.Exists(_lastPath))
        {
            return;
        }

        // Se já temos player ativo, ok
        if (_playSoundActive || _wavPlayer != null || _mciOpen)
        {
            return;
        }

        logger?.Info("BootSound", "Retry mid-hold: " + _lastPath);
        TryPlayOnce(_lastPath, logger, attempt: 99);
    }

    private bool TryPlayOnce(string path, ITurboRamaLogger? logger, int attempt)
    {
        try
        {
            StopInternal();
            string ext = System.IO.Path.GetExtension(path);

            // 1) WAV via PlaySound (melhor no kiosk/shell)
            if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                if (PlaySound(path, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT))
                {
                    _playSoundActive = true;
                    logger?.Info("BootSound", "PlaySound OK attempt=" + attempt + " path=" + path);
                    return true;
                }

                int err = Marshal.GetLastWin32Error();
                logger?.Warning("BootSound", "PlaySound falhou attempt=" + attempt + " err=" + err);

                // 2) Fallback SoundPlayer
                try
                {
                    _wavPlayer = new SoundPlayer(path);
                    _wavPlayer.Load();
                    _wavPlayer.Play();
                    logger?.Info("BootSound", "SoundPlayer OK attempt=" + attempt + " path=" + path);
                    return true;
                }
                catch (Exception exSp)
                {
                    logger?.Warning("BootSound", "SoundPlayer fail: " + exSp.Message);
                }

                // 3) Fallback MCI waveaudio
                if (TryPlayMci(path, "waveaudio", logger))
                {
                    logger?.Info("BootSound", "MCI waveaudio OK attempt=" + attempt);
                    return true;
                }
            }

            // MP3 / outros
            if (TryPlayMci(path, "mpegvideo", logger) || TryPlayMci(path, null, logger))
            {
                logger?.Info("BootSound", "MCI OK attempt=" + attempt + " path=" + path);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger?.Warning("BootSound", "TryPlayOnce: " + ex.Message);
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

    private bool TryPlayMci(string path, string? type, ITurboRamaLogger? logger)
    {
        try
        {
            // fecha alias residual
            mciSendString("close " + MciAlias, null, 0, IntPtr.Zero);

            string openCmd = type is null
                ? "open \"" + path + "\" alias " + MciAlias
                : "open \"" + path + "\" type " + type + " alias " + MciAlias;

            int openRc = mciSendString(openCmd, null, 0, IntPtr.Zero);
            if (openRc != 0)
            {
                logger?.Warning("BootSound", "MCI open rc=" + openRc + " type=" + (type ?? "auto"));
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
        catch (Exception ex)
        {
            logger?.Warning("BootSound", "MCI: " + ex.Message);
            return false;
        }
    }

    private void StopInternal()
    {
        try
        {
            if (_playSoundActive)
            {
                PlaySound(null, IntPtr.Zero, SND_ASYNC); // stop
                _playSoundActive = false;
            }
        }
        catch
        {
            _playSoundActive = false;
        }

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

    [DllImport("winmm.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr winHandle);
}
