using System.Diagnostics;
using SysProcess = System.Diagnostics.Process;
using System.Security.AccessControl;
using System.Security.Principal;
using TurboRama.Core.Baseline;
using TurboRama.Core.Results;

namespace TurboRama.Windows.Acl;

public static class AclSnapshotService
{
    public static AclSnapshot Capture(string targetPath, string baselineDirectory, string fileLabel)
    {
        Directory.CreateDirectory(baselineDirectory);
        var snap = new AclSnapshot { TargetPath = targetPath };

        if (!Directory.Exists(targetPath) && !File.Exists(targetPath))
        {
            snap.Succeeded = false;
            snap.Message = "Caminho inexistente (ainda não criado).";
            return snap;
        }

        string outFile = Path.Combine(baselineDirectory, fileLabel + "-icacls.txt");
        snap.IcaclsRelativePath = Path.GetFileName(outFile);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "icacls.exe",
                Arguments = "\"" + targetPath + "\" /save \"" + outFile + "\" /t /c",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using SysProcess proc = SysProcess.Start(psi)!;
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(120000);

            snap.Succeeded = proc.ExitCode == 0 || File.Exists(outFile);
            snap.Message = (stdout + " " + stderr).Trim();
            if (string.IsNullOrWhiteSpace(snap.Message))
            {
                snap.Message = snap.Succeeded ? "icacls OK" : "icacls exit " + proc.ExitCode;
            }

            try
            {
                if (Directory.Exists(targetPath))
                {
                    DirectoryInfo di = new(targetPath);
                    DirectorySecurity sec = di.GetAccessControl();
                    IdentityReference? owner = sec.GetOwner(typeof(NTAccount));
                    snap.Owner = owner?.ToString();
                }
            }
            catch
            {
                // owner opcional
            }
        }
        catch (Exception ex)
        {
            snap.Succeeded = false;
            snap.Message = ex.Message;
        }

        return snap;
    }

    public static OperationResult RestoreFromIcacls(string icaclsFile, string parentDirectory)
    {
        if (!File.Exists(icaclsFile))
        {
            return OperationResult.Fail("Arquivo icacls ausente: " + icaclsFile, "ACL_MISSING", "AclSnapshotService.Restore");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "icacls.exe",
                Arguments = "\"" + parentDirectory + "\" /restore \"" + icaclsFile + "\" /c",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using SysProcess proc = SysProcess.Start(psi)!;
            string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
            proc.WaitForExit(120000);

            if (proc.ExitCode != 0)
            {
                return OperationResult.Fail(
                    "icacls restore falhou: " + output.Trim(),
                    "ACL_RESTORE",
                    "AclSnapshotService.Restore",
                    exitCode: proc.ExitCode,
                    commandOrApi: "icacls /restore");
            }

            return OperationResult.Ok("ACL restaurada via icacls.", "AclSnapshotService.Restore");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("ACL restore: " + ex.Message, "ACL_EX", "AclSnapshotService.Restore", exception: ex);
        }
    }
}
