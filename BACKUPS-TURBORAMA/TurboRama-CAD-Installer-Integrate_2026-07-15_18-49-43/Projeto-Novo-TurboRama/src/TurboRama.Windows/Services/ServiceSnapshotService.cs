using System.Diagnostics;
using SysProcess = System.Diagnostics.Process;
using TurboRama.Core.Baseline;

namespace TurboRama.Windows.Services;

public static class ServiceSnapshotService
{
    private static readonly string[] DefaultServices =
    {
        "MsKeyboardFilter",
        "Schedule",
        "Winlogon"
    };

    public static List<ServiceSnapshot> CaptureDefaults() =>
        DefaultServices.Select(CaptureOne).ToList();

    public static ServiceSnapshot CaptureOne(string serviceName)
    {
        var snap = new ServiceSnapshot { ServiceName = serviceName };
        try
        {
            string raw = RunSc("qc \"" + serviceName + "\"");
            snap.RawQuery = raw;
            if (raw.Contains("1060", StringComparison.Ordinal) ||
                raw.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("não existe", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("nao existe", StringComparison.OrdinalIgnoreCase))
            {
                snap.Exists = false;
                return snap;
            }

            snap.Exists = true;
            snap.StartType = Extract(raw, "START_TYPE");
            snap.BinaryPath = Extract(raw, "BINARY_PATH_NAME");
            snap.Account = Extract(raw, "SERVICE_START_NAME");

            string stateRaw = RunSc("query \"" + serviceName + "\"");
            snap.State = Extract(stateRaw, "STATE") ?? Extract(stateRaw, "STATE              :");
        }
        catch (Exception ex)
        {
            snap.Exists = false;
            snap.RawQuery = ex.Message;
        }

        return snap;
    }

    private static string? Extract(string text, string key)
    {
        foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                int colon = line.IndexOf(':');
                if (colon >= 0 && colon + 1 < line.Length)
                {
                    return line[(colon + 1)..].Trim();
                }
            }
        }

        return null;
    }

    private static string RunSc(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using SysProcess proc = SysProcess.Start(psi)!;
        // Não usar ReadToEnd síncrono sem timeout — pode travar a UI de Status.
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(8000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return "ERROR: sc timeout " + args;
        }

        if (!Task.WaitAll(new Task[] { outTask, errTask }, 2000))
        {
            return "ERROR: sc read timeout " + args;
        }

        return (outTask.Result ?? string.Empty) + (errTask.Result ?? string.Empty);
    }
}
