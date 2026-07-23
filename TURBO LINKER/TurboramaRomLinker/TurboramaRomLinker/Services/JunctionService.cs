using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using TurboramaRomLinker.Models;

namespace TurboramaRomLinker.Services
{
    public static class JunctionService
    {
        private const int SYMBOLIC_LINK_FLAG_FILE = 0;
        private const int SYMBOLIC_LINK_FLAG_DIRECTORY = 1;
        private const int SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE = 2;

        public static bool IsDirectoryReparsePoint(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
            try
            {
                DirectoryInfo info = new DirectoryInfo(path);
                return (info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Compat: junta = pasta reparse.</summary>
        public static bool IsReparsePoint(string path)
        {
            return IsDirectoryReparsePoint(path);
        }

        public static bool IsFileReparsePoint(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            try
            {
                FileAttributes attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Nome seguro para conflito multi-HD: D_nome, F_arquivo.bat
        /// </summary>
        public static string SafeLinkName(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return "src";
            string p;
            try { p = Path.GetFullPath(sourcePath); }
            catch { p = sourcePath; }

            StringBuilder name = new StringBuilder();
            if (p.Length >= 2 && p[1] == ':')
            {
                name.Append(char.ToUpperInvariant(p[0]));
                name.Append('_');
            }

            string baseName = Path.GetFileName(p.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(baseName)) baseName = "src";
            foreach (char c in baseName)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                    name.Append(c);
                else
                    name.Append('_');
            }
            return name.Length == 0 ? "src" : name.ToString();
        }

        public static string ResolveFinalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            if (!Directory.Exists(path) && !File.Exists(path))
                return null;

            IntPtr handle = CreateFile(
                path,
                0,
                FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero);

            // Para ficheiros normais/symlink, tenta sem OPEN_REPARSE se falhar
            if (handle == INVALID_HANDLE_VALUE)
            {
                handle = CreateFile(
                    path,
                    0,
                    FileShare.ReadWrite | FileShare.Delete,
                    IntPtr.Zero,
                    FileMode.Open,
                    FILE_FLAG_BACKUP_SEMANTICS,
                    IntPtr.Zero);
            }

            if (handle == INVALID_HANDLE_VALUE)
                return null;

            try
            {
                StringBuilder sb = new StringBuilder(1024);
                uint n = GetFinalPathNameByHandle(handle, sb, (uint)sb.Capacity, FILE_NAME_NORMALIZED);
                if (n == 0 || n >= sb.Capacity)
                    return null;

                string resolved = sb.ToString();
                if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                    resolved = @"\\" + resolved.Substring(8);
                else if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal))
                    resolved = resolved.Substring(4);
                return resolved;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>Cria junction de pasta (mklink /J) — sem admin.</summary>
        public static bool CreateDirectoryJunction(string linkPath, string targetPath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(linkPath) || string.IsNullOrWhiteSpace(targetPath))
            {
                error = "Caminhos inválidos.";
                return false;
            }
            if (!Directory.Exists(targetPath))
            {
                error = "Origem não existe: " + targetPath;
                return false;
            }
            if (Directory.Exists(linkPath) || File.Exists(linkPath))
            {
                error = "Destino já existe: " + linkPath;
                return false;
            }

            string parent = Path.GetDirectoryName(linkPath);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/C mklink /J " + Quote(linkPath) + " " + Quote(targetPath);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string err = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode == 0 && Directory.Exists(linkPath))
                    return true;
                error = (err + " " + output).Trim();
                if (string.IsNullOrWhiteSpace(error)) error = "Falha mklink /J.";
                return false;
            }
        }

        /// <summary>
        /// Cria link de ficheiro (.bat, .exe, .xml...) apontando para a unidade física.
        /// Usa CreateSymbolicLink (Developer Mode / privilegiado).
        /// </summary>
        public static bool CreateFileSymlink(string linkPath, string targetPath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(linkPath) || string.IsNullOrWhiteSpace(targetPath))
            {
                error = "Caminhos inválidos.";
                return false;
            }
            if (!File.Exists(targetPath))
            {
                error = "Arquivo origem não existe: " + targetPath;
                return false;
            }
            if (File.Exists(linkPath) || Directory.Exists(linkPath))
            {
                error = "Destino já existe: " + linkPath;
                return false;
            }

            string parent = Path.GetDirectoryName(linkPath);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);

            string targetFull = Path.GetFullPath(targetPath);

            // 1) API nativa (preferida, permite unprivileged no Win10+)
            int flags = SYMBOLIC_LINK_FLAG_FILE | SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE;
            if (CreateSymbolicLink(linkPath, targetFull, flags))
            {
                if (File.Exists(linkPath) || IsFileReparsePoint(linkPath))
                    return true;
            }

            flags = SYMBOLIC_LINK_FLAG_FILE;
            if (CreateSymbolicLink(linkPath, targetFull, flags))
            {
                if (File.Exists(linkPath) || IsFileReparsePoint(linkPath))
                    return true;
            }

            // 2) mklink arquivo (precisa privilégio)
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/C mklink " + Quote(linkPath) + " " + Quote(targetFull);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string err = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode == 0 && (File.Exists(linkPath) || IsFileReparsePoint(linkPath)))
                    return true;
                error = (err + " " + output).Trim();
                if (string.IsNullOrWhiteSpace(error))
                    error = "Falha ao criar link de arquivo (ative Modo de Programador no Windows ou execute como Admin).";
                return false;
            }
        }

        /// <summary>
        /// Espelha o conteúdo de uma pasta TurboRoms para a mestre:
        /// - cada SUBPASTA → junction (jogo completo com exe/bat internos)
        /// - cada ARQUIVO (.bat, .exe, .xml, roms...) → symlink de ficheiro
        /// Assim o ES vê os ficheiros dentro de sistema\roms\&lt;sistema&gt;.
        /// </summary>
        public static int MirrorFolderContents(string sourceRoot, string destRoot, ICollection<string> log)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
                return 0;
            if (string.IsNullOrWhiteSpace(destRoot))
                return 0;

            if (!Directory.Exists(destRoot))
                Directory.CreateDirectory(destRoot);

            // Destino NÃO pode ser junction da pasta inteira — precisa ser pasta real
            if (IsDirectoryReparsePoint(destRoot))
            {
                if (log != null)
                    log.Add("AVISO: destino é junction de pasta inteira; convertendo para pasta real: " + destRoot);
                string resolved = ResolveFinalPath(destRoot);
                try
                {
                    Directory.Delete(destRoot);
                }
                catch (Exception ex)
                {
                    if (log != null) log.Add("ERRO ao remover junction: " + ex.Message);
                    return 0;
                }
                Directory.CreateDirectory(destRoot);
                // Se a origem for a mesma do junction antigo, vamos relinkar o conteúdo abaixo
                if (!string.IsNullOrWhiteSpace(resolved) && !PathsEqual(resolved, sourceRoot) && Directory.Exists(resolved))
                {
                    // primeiro espelha o antigo (outra unidade) se for multi
                    MirrorFolderContents(resolved, destRoot, log);
                }
            }

            int created = 0;
            string drivePrefix = GetDrivePrefix(sourceRoot);

            // Pastas de jogos (top-level)
            foreach (string dir in SafeGetDirectories(sourceRoot))
            {
                string name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name)) continue;
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;

                string dest = Path.Combine(destRoot, name);
                if (EntryExists(dest))
                {
                    if (SameLinkTarget(dest, dir))
                    {
                        if (log != null) log.Add("OK (já): " + name);
                        continue;
                    }
                    // Conflito multi-HD → prefixo da unidade
                    dest = Path.Combine(destRoot, drivePrefix + name);
                    if (EntryExists(dest))
                    {
                        if (SameLinkTarget(dest, dir))
                        {
                            if (log != null) log.Add("OK (já): " + Path.GetFileName(dest));
                            continue;
                        }
                        if (log != null) log.Add("PULO pasta (conflito): " + name + " de " + sourceRoot);
                        continue;
                    }
                }

                string err;
                if (CreateDirectoryJunction(dest, dir, out err))
                {
                    created++;
                    if (log != null) log.Add("Pasta link: " + Path.GetFileName(dest) + "  →  " + dir);
                }
                else if (log != null)
                    log.Add("ERRO pasta " + name + ": " + err);
            }

            // Arquivos soltos (bat, exe, xml, roms...)
            foreach (string file in SafeGetFiles(sourceRoot))
            {
                string name = Path.GetFileName(file);
                if (string.IsNullOrEmpty(name)) continue;

                string dest = Path.Combine(destRoot, name);
                if (EntryExists(dest))
                {
                    if (SameLinkTarget(dest, file))
                    {
                        if (log != null) log.Add("OK (já): " + name);
                        continue;
                    }
                    dest = Path.Combine(destRoot, drivePrefix + name);
                    if (EntryExists(dest))
                    {
                        if (SameLinkTarget(dest, file))
                        {
                            if (log != null) log.Add("OK (já): " + Path.GetFileName(dest));
                            continue;
                        }
                        if (log != null) log.Add("PULO arquivo (conflito): " + name);
                        continue;
                    }
                }

                string err;
                if (CreateFileSymlink(dest, file, out err))
                {
                    created++;
                    if (log != null) log.Add("Arquivo link: " + Path.GetFileName(dest) + "  →  " + file);
                }
                else if (log != null)
                    log.Add("ERRO arquivo " + name + ": " + err);
            }

            return created;
        }

        /// <summary>Remove apenas reparse points (links) em pasta de sistema — preserva pastas reais.</summary>
        public static int CleanReparsePointsIn(string systemDir, out int preserved)
        {
            preserved = 0;
            int removed = 0;
            if (!Directory.Exists(systemDir)) return 0;

            // Ficheiros-link no nível do sistema
            foreach (string file in SafeGetFiles(systemDir))
            {
                try
                {
                    if (IsFileReparsePoint(file))
                    {
                        File.Delete(file);
                        removed++;
                    }
                }
                catch { }
            }

            foreach (string dir in SafeGetDirectories(systemDir))
            {
                try
                {
                    if (IsDirectoryReparsePoint(dir))
                    {
                        Directory.Delete(dir);
                        removed++;
                    }
                    else
                    {
                        // multi antigo: limpa junctions filhos
                        int nested = 0;
                        foreach (string child in SafeGetDirectories(dir))
                        {
                            if (IsDirectoryReparsePoint(child))
                            {
                                Directory.Delete(child);
                                removed++;
                                nested++;
                            }
                        }
                        foreach (string childFile in SafeGetFiles(dir))
                        {
                            if (IsFileReparsePoint(childFile))
                            {
                                File.Delete(childFile);
                                removed++;
                                nested++;
                            }
                        }
                        if (nested == 0) preserved++;
                        else
                        {
                            try
                            {
                                if (Directory.GetDirectories(dir).Length == 0 && Directory.GetFiles(dir).Length == 0)
                                    Directory.Delete(dir);
                            }
                            catch { }
                        }
                    }
                }
                catch { preserved++; }
            }

            return removed;
        }

        public static RomLinkPlanItem CreateJunction(RomLinkPlanItem item)
        {
            // Compat: junction de pasta inteira (legado)
            if (item == null) throw new ArgumentNullException("item");
            string err;
            if (CreateDirectoryJunction(item.LinkPath, item.SourcePath, out err))
            {
                item.Success = true;
                item.Message = "Junction criada com sucesso.";
            }
            else
            {
                item.Action = RomLinkAction.Error;
                item.Success = false;
                item.Message = err;
            }
            return item;
        }

        private static bool EntryExists(string path)
        {
            return Directory.Exists(path) || File.Exists(path);
        }

        private static bool SameLinkTarget(string linkPath, string expectedTarget)
        {
            try
            {
                string resolved = ResolveFinalPath(linkPath);
                if (string.IsNullOrWhiteSpace(resolved)) return false;
                return PathsEqual(resolved, expectedTarget);
            }
            catch
            {
                return false;
            }
        }

        private static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd('\\', '/'),
                    Path.GetFullPath(b).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string GetDrivePrefix(string path)
        {
            try
            {
                string root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root) && root.Length >= 1)
                    return char.ToUpperInvariant(root[0]) + "_";
            }
            catch { }
            return "X_";
        }

        private static string[] SafeGetDirectories(string path)
        {
            try { return Directory.GetDirectories(path); }
            catch { return new string[0]; }
        }

        private static string[] SafeGetFiles(string path)
        {
            try { return Directory.GetFiles(path); }
            catch { return new string[0]; }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        private const uint FILE_NAME_NORMALIZED = 0;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            FileMode dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetFinalPathNameByHandle(
            IntPtr hFile,
            StringBuilder lpszFilePath,
            uint cchFilePath,
            uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);
    }
}
