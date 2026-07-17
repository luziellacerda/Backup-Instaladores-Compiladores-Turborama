using System.Diagnostics;

namespace TurboRama.ArcadeTimer.Services;

public sealed class EmulatorMonitor
{
    private readonly HashSet<string> _emulators;

    public EmulatorMonitor(IEnumerable<string> emulatorProcesses)
    {
        _emulators = new HashSet<string>(
            emulatorProcesses
                .Select(p => Path.GetFileNameWithoutExtension(p) ?? p)
                .Where(p => !string.IsNullOrWhiteSpace(p)),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<Process> GetRunningEmulators()
    {
        var result = new List<Process>();

        foreach (string name in _emulators)
        {
            try
            {
                result.AddRange(Process.GetProcessesByName(name));
            }
            catch (Exception ex)
            {
                LogService.Write($"Falha ao verificar {name}", ex);
            }
        }

        return result;
    }

    public bool IsAnyRunning()
    {
        var processes = GetRunningEmulators();
        bool any = processes.Count > 0;

        foreach (var process in processes)
            process.Dispose();

        return any;
    }
}
