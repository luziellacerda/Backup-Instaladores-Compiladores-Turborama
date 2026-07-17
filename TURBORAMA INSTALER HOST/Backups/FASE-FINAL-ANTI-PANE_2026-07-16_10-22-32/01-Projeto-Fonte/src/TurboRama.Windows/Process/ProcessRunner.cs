using System.Diagnostics;
using System.Text;
using TurboRama.Core.Results;

namespace TurboRama.Windows.Exec;

public static class ProcessRunner
{
    public static OperationResult Run(
        string fileName,
        string arguments,
        int timeoutMs = 120_000,
        string? operationName = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Não iniciou: " + fileName);

            // Leitura assíncrona — evita deadlock e permite timeout real.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                try { Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 1000); } catch { /* ignore */ }
                return OperationResult.Fail(
                    "Timeout: " + fileName + " " + arguments,
                    "PROC_TIMEOUT",
                    operationName ?? fileName,
                    commandOrApi: fileName + " " + arguments);
            }

            Task.WaitAll(new Task[] { stdoutTask, stderrTask }, Math.Min(5000, timeoutMs));
            string stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
            string stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
            string output = (stdout + Environment.NewLine + stderr).Trim();

            if (proc.ExitCode != 0)
            {
                return OperationResult.Fail(
                    fileName + " falhou (exit " + proc.ExitCode + "): " + output,
                    "PROC_EXIT",
                    operationName ?? fileName,
                    commandOrApi: fileName + " " + arguments,
                    exitCode: proc.ExitCode,
                    currentState: output);
            }

            return new OperationResult
            {
                Success = true,
                Message = string.IsNullOrWhiteSpace(output) ? fileName + " OK" : output,
                OperationName = operationName ?? fileName,
                CommandOrApi = fileName + " " + arguments,
                ExitCode = 0,
                CurrentState = output,
                CanRollback = true
            };
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                fileName + ": " + ex.Message,
                "PROC_EX",
                operationName ?? fileName,
                commandOrApi: fileName + " " + arguments,
                exception: ex);
        }
    }
}
