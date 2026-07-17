using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using TurboRama.Core.Results;

namespace TurboRama.Windows.Tasks;

/// <summary>
/// Snapshot de tarefas agendadas TurboRama (proposta §6.5).
/// </summary>
public static class ScheduledTaskSnapshotService
{
    public static OperationResult CaptureToDirectory(string backupDir)
    {
        try
        {
            Directory.CreateDirectory(backupDir);
            string listPath = Path.Combine(backupDir, "scheduled-tasks-list.txt");
            string list = Run("schtasks.exe", "/Query /FO LIST /V");
            File.WriteAllText(listPath, list, Encoding.UTF8);

            // Exporta tarefas cujo nome/caminho contém TurboRama
            var names = new List<string>();
            foreach (string line in list.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("TaskName:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Nome da Tarefa:", StringComparison.OrdinalIgnoreCase))
                {
                    int i = line.IndexOf(':');
                    if (i > 0)
                    {
                        string name = line[(i + 1)..].Trim();
                        if (name.Contains("TurboRama", StringComparison.OrdinalIgnoreCase))
                        {
                            names.Add(name);
                        }
                    }
                }
            }

            int exported = 0;
            foreach (string task in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string safe = string.Join("_", task.Split(Path.GetInvalidFileNameChars()));
                string xmlPath = Path.Combine(backupDir, "task-" + safe + ".xml");
                string xml = Run("schtasks.exe", "/Query /TN \"" + task + "\" /XML");
                if (!string.IsNullOrWhiteSpace(xml) && xml.Contains('<'))
                {
                    File.WriteAllText(xmlPath, xml, Encoding.UTF8);
                    exported++;
                }
            }

            // Sempre grava manifesto vazio se nenhuma
            File.WriteAllText(
                Path.Combine(backupDir, "scheduled-tasks-manifest.txt"),
                "TurboRama tasks found=" + names.Count + " exported=" + exported + Environment.NewLine +
                string.Join(Environment.NewLine, names));

            return OperationResult.Ok(
                "Tarefas: listadas; TurboRama=" + names.Count + " XML=" + exported,
                "ScheduledTaskSnapshot");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message, "TASK_SNAP", "ScheduledTaskSnapshot", exception: ex);
        }
    }

    private static string Run(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                return string.Empty;
            }

            if (!p.WaitForExit(20_000))
            {
                try { p.Kill(true); } catch { /* ignore */ }
                return "TIMEOUT";
            }

            return p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
