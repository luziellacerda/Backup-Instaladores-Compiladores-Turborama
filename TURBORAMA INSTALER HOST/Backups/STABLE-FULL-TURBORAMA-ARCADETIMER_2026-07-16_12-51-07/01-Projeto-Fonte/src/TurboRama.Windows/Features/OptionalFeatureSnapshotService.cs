using System.Diagnostics;
using SysProcess = System.Diagnostics.Process;
using TurboRama.Core.Baseline;

namespace TurboRama.Windows.Features;

public static class OptionalFeatureSnapshotService
{
    private static readonly string[] Features =
    {
        "Client-DeviceLockdown",
        "Client-EmbeddedBootExp",
        "Client-EmbeddedLogon",
        "Client-UnifiedWriteFilter"
    };

    public static List<OptionalFeatureSnapshot> CaptureAll() =>
        Features.Select(CaptureOne).ToList();

    public static OptionalFeatureSnapshot CaptureOne(string featureName)
    {
        var snap = new OptionalFeatureSnapshot { FeatureName = featureName };
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dism.exe",
                Arguments = "/Online /Get-FeatureInfo /FeatureName:" + featureName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using SysProcess proc = SysProcess.Start(psi)!;
            string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
            proc.WaitForExit(60000);

            if (output.Contains("0x800f080c", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("was not found", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("não foi encontrado", StringComparison.OrdinalIgnoreCase))
            {
                snap.Present = false;
                snap.State = "NotPresent";
                return snap;
            }

            snap.Present = true;
            if (output.Contains("State : Enabled", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Estado : Habilitado", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Enabled", StringComparison.OrdinalIgnoreCase) && output.Contains("State", StringComparison.OrdinalIgnoreCase))
            {
                // crude parse
                foreach (string line in output.Split('\n'))
                {
                    if (line.Contains("State", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Estado", StringComparison.OrdinalIgnoreCase))
                    {
                        snap.State = line.Trim();
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(snap.State) || snap.State == "Unknown")
                {
                    snap.State = output.Contains("Enabled", StringComparison.OrdinalIgnoreCase) ? "Enabled" : "Disabled";
                }
            }
            else if (output.Contains("Disabled", StringComparison.OrdinalIgnoreCase) ||
                     output.Contains("Desabilitado", StringComparison.OrdinalIgnoreCase))
            {
                snap.State = "Disabled";
            }
            else
            {
                snap.State = proc.ExitCode == 0 ? "Unknown" : "Error";
            }
        }
        catch (Exception ex)
        {
            snap.Present = false;
            snap.State = "Error: " + ex.Message;
        }

        return snap;
    }
}
