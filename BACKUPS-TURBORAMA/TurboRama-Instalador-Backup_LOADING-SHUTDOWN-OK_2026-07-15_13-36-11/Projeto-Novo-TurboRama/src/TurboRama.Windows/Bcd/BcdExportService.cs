using System.Diagnostics;
using SysProcess = System.Diagnostics.Process;
using TurboRama.Core.Baseline;
using TurboRama.Core.Manifest;
using TurboRama.Core.Results;

namespace TurboRama.Windows.Bcd;

/// <summary>
/// Exporta BCD antes de qualquer bcdedit (estudo §6.2).
/// </summary>
public static class BcdExportService
{
    public static BcdSnapshot Capture(string baselineDirectory)
    {
        Directory.CreateDirectory(baselineDirectory);
        var snap = new BcdSnapshot
        {
            CapturedAt = DateTimeOffset.Now,
            ExportFileName = "bcd-backup",
            ExportRelativePath = "bcd-backup",
            EnumTextRelativePath = "bcd-enum-all.txt"
        };

        string exportPath = Path.Combine(baselineDirectory, "bcd-backup");
        string enumPath = Path.Combine(baselineDirectory, "bcd-enum-all.txt");

        OperationResult export = RunBcdEdit("/export \"" + exportPath + "\"");
        snap.ExportSucceeded = export.Success;
        snap.Message = export.Message;

        OperationResult enumerate = RunBcdEdit("/enum all /v");
        if (enumerate.Success && enumerate.CurrentState is not null)
        {
            File.WriteAllText(enumPath, enumerate.CurrentState);
        }
        else
        {
            File.WriteAllText(enumPath, enumerate.Message);
        }

        if (File.Exists(exportPath))
        {
            try
            {
                snap.Sha256 = BaselineHash.Sha256File(exportPath);
            }
            catch (Exception ex)
            {
                snap.Message = (snap.Message ?? "") + " | hash: " + ex.Message;
            }
        }
        else if (Directory.Exists(exportPath))
        {
            // bcdedit /export cria arquivo; em alguns hosts pode variar
            snap.Message = (snap.Message ?? "") + " | export path is directory";
        }

        return snap;
    }

    public static OperationResult Restore(string baselineDirectory)
    {
        string exportPath = Path.Combine(baselineDirectory, "bcd-backup");
        if (!File.Exists(exportPath) && !Directory.Exists(exportPath))
        {
            return OperationResult.Fail(
                "Arquivo BCD de backup ausente: " + exportPath,
                "BCD_MISSING",
                "BcdExportService.Restore");
        }

        // Importação BCD é sensível — na Fase 1 apenas verificamos existência e hash.
        // Restore real de BCD fica documentado e exige confirmação explícita (Fase 4 branding).
        return OperationResult.Ok(
            "BCD backup presente. Importação automática desabilitada na Fase 1 (segurança). Use restore manual se necessário: bcdedit /import \"" + exportPath + "\"",
            "BcdExportService.Restore");
    }

    private static OperationResult RunBcdEdit(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bcdedit.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using SysProcess proc = SysProcess.Start(psi) ?? throw new InvalidOperationException("bcdedit não iniciou");
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(60000);

            string output = (stdout + Environment.NewLine + stderr).Trim();
            if (proc.ExitCode != 0)
            {
                return OperationResult.Fail(
                    "bcdedit falhou: " + output,
                    "BCD_EXIT",
                    "BcdExportService",
                    commandOrApi: "bcdedit " + arguments,
                    exitCode: proc.ExitCode);
            }

            return new OperationResult
            {
                Success = true,
                Message = "bcdedit OK",
                OperationName = "BcdExportService",
                CurrentState = output,
                CommandOrApi = "bcdedit " + arguments,
                ExitCode = 0,
                CanRollback = true
            };
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "bcdedit exceção: " + ex.Message,
                "BCD_EX",
                "BcdExportService",
                commandOrApi: "bcdedit " + arguments,
                exception: ex);
        }
    }
}
