using System;
using System.Diagnostics;
using System.IO;
using TurboramaRomLinker.Models;

namespace TurboramaRomLinker.Services
{
    public static class JunctionService
    {
        public static bool IsReparsePoint(string path)
        {
            if (!Directory.Exists(path)) return false;
            DirectoryInfo info = new DirectoryInfo(path);
            return (info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }

        public static RomLinkPlanItem CreateJunction(RomLinkPlanItem item)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (!Directory.Exists(item.SourcePath))
            {
                item.Action = RomLinkAction.Error;
                item.Success = false;
                item.Message = "Origem não existe mais.";
                return item;
            }

            string parent = Path.GetDirectoryName(item.LinkPath);
            if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);

            if (Directory.Exists(item.LinkPath))
            {
                item.Success = false;
                item.Message = IsReparsePoint(item.LinkPath)
                    ? "Link já existe. Nenhuma alteração feita."
                    : "Já existe pasta real no destino. Preservada para evitar perda de dados.";
                return item;
            }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/C mklink /J " + Quote(item.LinkPath) + " " + Quote(item.SourcePath);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && Directory.Exists(item.LinkPath))
                {
                    item.Success = true;
                    item.Message = "Junction criada com sucesso.";
                }
                else
                {
                    item.Action = RomLinkAction.Error;
                    item.Success = false;
                    item.Message = (error + " " + output).Trim();
                    if (string.IsNullOrWhiteSpace(item.Message)) item.Message = "Falha ao criar junction.";
                }
            }

            return item;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
