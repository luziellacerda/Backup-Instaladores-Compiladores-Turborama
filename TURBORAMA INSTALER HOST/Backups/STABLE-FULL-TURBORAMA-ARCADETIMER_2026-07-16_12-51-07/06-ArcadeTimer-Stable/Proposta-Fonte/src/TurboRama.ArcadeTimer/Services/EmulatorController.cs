using System.Diagnostics;
using TurboRama.ArcadeTimer.Configuration;

namespace TurboRama.ArcadeTimer.Services;

public sealed class EmulatorController
{
    private readonly HashSet<string> _allowed;
    private readonly HashSet<string> _protected;
    private readonly int _timeout;
    private readonly bool _forceClose;

    public EmulatorController(
        IEnumerable<string> allowed,
        IEnumerable<string> protectedProcesses,
        int timeoutMilliseconds,
        bool forceClose)
    {
        _allowed = new HashSet<string>(
            allowed
                .Select(p => Path.GetFileNameWithoutExtension(p) ?? p)
                .Where(p => !string.IsNullOrWhiteSpace(p)),
            StringComparer.OrdinalIgnoreCase);

        _protected = new HashSet<string>(
            protectedProcesses
                .Select(p => Path.GetFileNameWithoutExtension(p) ?? p)
                .Where(p => !string.IsNullOrWhiteSpace(p)),
            StringComparer.OrdinalIgnoreCase);

        // Hard security: sempre unir protegidos críticos (config não pode remover).
        foreach (string hard in TimerConfig.HardProtectedProcesses)
            _protected.Add(hard);

        // Nunca matar hard-protected mesmo se estiverem na whitelist.
        foreach (string hard in _protected)
            _allowed.Remove(hard);

        _timeout = timeoutMilliseconds;
        _forceClose = forceClose;
    }

    public void CloseAuthorizedEmulators(IEnumerable<Process> processes)
    {
        foreach (Process process in processes)
        {
            try
            {
                string name = process.ProcessName;

                if (_protected.Contains(name) || !_allowed.Contains(name))
                {
                    LogService.Write($"Processo ignorado por segurança: {name}");
                    continue;
                }

                // PID 0/4 e sistema — nunca.
                if (process.Id <= 4)
                {
                    LogService.Write($"PID de sistema ignorado: {process.Id}");
                    continue;
                }

                if (process.HasExited)
                    continue;

                LogService.Write($"Solicitando fechamento normal de {name} (pid={process.Id}).");

                bool closeRequested = false;
                try { closeRequested = process.CloseMainWindow(); } catch { }

                if (!closeRequested || !process.WaitForExit(_timeout))
                {
                    if (_forceClose && !process.HasExited)
                    {
                        LogService.Write($"Encerramento forçado de {name} (pid={process.Id}).");
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(2000);
                    }
                }

                LogService.Write($"Processo encerrado: {name}.");
            }
            catch (Exception ex)
            {
                LogService.Write("Falha ao encerrar emulador autorizado", ex);
            }
            finally
            {
                try { process.Dispose(); } catch { }
            }
        }
    }
}
