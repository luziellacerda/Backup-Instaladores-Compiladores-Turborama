using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TurboramaRomLinker.Services
{
    /// <summary>
    /// Corrige .bat/.cmd para funcionarem quando o jogo e aberto via junction
    /// em outra unidade (mestre). Caminhos relativos (cd ..\pasta) passam a absolutos
    /// na unidade fisica do arquivo.
    /// </summary>
    public static class BatLaunchFixer
    {
        private static readonly Regex CdLine = new Regex(
            @"^\s*(?<cmd>cd|chdir)(?<d>\s+/[dD])?\s+(?<path>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex StartLine = new Regex(
            @"^\s*start\s+(?<opts>(?:/(?:wait|b|min|max|i|low|normal|high|realtime|abovenormal|belownormal)\s+)*)(?<title>""[^""]*""\s+)?(?<rest>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Corrige todos os .bat/.cmd sob rootDirectory (recursivo).
        /// Retorna quantos arquivos foram alterados.
        /// </summary>
        public static int FixDirectory(string rootDirectory, ICollection<string> log)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
                return 0;

            int changed = 0;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(rootDirectory, "*.*", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                if (log != null) log.Add("BatFix: nao foi possivel listar " + rootDirectory + " — " + ex.Message);
                return 0;
            }

            foreach (string file in files)
            {
                string ext = Path.GetExtension(file);
                if (!string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(ext, ".cmd", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (FixFile(file))
                    {
                        changed++;
                        if (log != null) log.Add("BatFix OK: " + file);
                    }
                }
                catch (Exception ex)
                {
                    if (log != null) log.Add("BatFix ERRO: " + file + " — " + ex.Message);
                }
            }

            return changed;
        }

        public static bool FixFile(string batPath)
        {
            if (string.IsNullOrWhiteSpace(batPath) || !File.Exists(batPath))
                return false;

            Encoding encoding;
            string original;
            using (StreamReader reader = new StreamReader(batPath, Encoding.Default, true))
            {
                encoding = reader.CurrentEncoding ?? Encoding.Default;
                original = reader.ReadToEnd();
            }

            if (string.IsNullOrEmpty(original))
                return false;

            string batDir = Path.GetDirectoryName(Path.GetFullPath(batPath));
            if (string.IsNullOrEmpty(batDir))
                return false;

            string[] lines = original.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool any = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string fixedLine;
                if (TryFixCdLine(lines[i], batDir, out fixedLine)
                    || TryFixStartLine(lines[i], batDir, out fixedLine))
                {
                    if (!string.Equals(lines[i], fixedLine, StringComparison.Ordinal))
                    {
                        lines[i] = fixedLine;
                        any = true;
                    }
                }
            }

            if (!any)
                return false;

            // Preserva final de linha Windows
            string result = string.Join("\r\n", lines);
            if (original.EndsWith("\n") || original.EndsWith("\r"))
            {
                // keep trailing newline style
                if (!result.EndsWith("\r\n"))
                    result += "\r\n";
            }

            // So grava se realmente mudou
            string normalizedOriginal = original.Replace("\r\n", "\n").Replace("\r", "\n");
            string normalizedResult = result.Replace("\r\n", "\n").Replace("\r", "\n");
            if (string.Equals(normalizedOriginal, normalizedResult, StringComparison.Ordinal))
                return false;

            File.WriteAllText(batPath, result, encoding);
            return true;
        }

        private static bool TryFixCdLine(string line, string batDir, out string fixedLine)
        {
            fixedLine = line;
            Match m = CdLine.Match(line);
            if (!m.Success)
                return false;

            string rawPath = m.Groups["path"].Value.Trim();
            if (rawPath.Length == 0)
                return false;

            // remove aspas externas
            bool quoted = rawPath.Length >= 2 && rawPath[0] == '"' && rawPath[rawPath.Length - 1] == '"';
            string path = quoted ? rawPath.Substring(1, rawPath.Length - 2) : rawPath;

            // Ja absoluto com letra de unidade — ok
            if (IsAbsoluteWindowsPath(path))
                return false;

            // Variaveis de ambiente / %~dp0 ja resolvem — nao mexer se ja usa %~
            if (path.IndexOf("%~", StringComparison.Ordinal) >= 0
                || path.IndexOf("%SYSTEM", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("%PROGRAM", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            string absolute;
            if (!TryResolveRelative(batDir, path, out absolute))
                return false;

            string dFlag = m.Groups["d"].Success ? m.Groups["d"].Value : " /d";
            // Sempre /d para mudar de unidade quando necessario
            if (string.IsNullOrWhiteSpace(dFlag))
                dFlag = " /d";

            fixedLine = m.Groups["cmd"].Value + dFlag + " \"" + absolute + "\"";
            // preserva indentacao
            int lead = 0;
            while (lead < line.Length && (line[lead] == ' ' || line[lead] == '\t'))
                lead++;
            if (lead > 0)
                fixedLine = line.Substring(0, lead) + fixedLine;
            return true;
        }

        private static bool TryFixStartLine(string line, string batDir, out string fixedLine)
        {
            fixedLine = line;
            Match m = StartLine.Match(line);
            if (!m.Success)
                return false;

            string rest = m.Groups["rest"].Value.Trim();
            if (rest.Length == 0)
                return false;

            string exePart;
            string args = string.Empty;
            if (rest[0] == '"')
            {
                int end = rest.IndexOf('"', 1);
                if (end < 0)
                    return false;
                exePart = rest.Substring(1, end - 1);
                args = rest.Substring(end + 1).TrimStart();
            }
            else
            {
                int sp = rest.IndexOfAny(new char[] { ' ', '\t' });
                if (sp < 0)
                {
                    exePart = rest;
                }
                else
                {
                    exePart = rest.Substring(0, sp);
                    args = rest.Substring(sp + 1).TrimStart();
                }
            }

            if (IsAbsoluteWindowsPath(exePart)
                || exePart.IndexOf("%~", StringComparison.Ordinal) >= 0
                || exePart.IndexOf('%') >= 0)
                return false;

            // so nomes simples no mesmo dir (game.exe) — ok apos cd absoluto
            if (exePart.IndexOf('\\') < 0 && exePart.IndexOf('/') < 0)
                return false;

            string absolute;
            if (!TryResolveRelative(batDir, exePart, out absolute))
                return false;

            string opts = m.Groups["opts"].Value;
            string title = m.Groups["title"].Success ? m.Groups["title"].Value : "\"\" ";
            if (!m.Groups["title"].Success)
                title = "\"\" ";

            string rebuilt = "start " + opts + title + "\"" + absolute + "\"";
            if (!string.IsNullOrEmpty(args))
                rebuilt += " " + args;

            int lead = 0;
            while (lead < line.Length && (line[lead] == ' ' || line[lead] == '\t'))
                lead++;
            if (lead > 0)
                rebuilt = line.Substring(0, lead) + rebuilt;

            fixedLine = rebuilt;
            return true;
        }

        private static bool TryResolveRelative(string batDir, string relativePath, out string absolute)
        {
            absolute = null;
            try
            {
                string combined = Path.GetFullPath(Path.Combine(batDir, relativePath.Replace('/', '\\')));
                absolute = combined;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAbsoluteWindowsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            // C:\... or \\server\share
            if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
                return true;
            if (path.StartsWith("\\\\", StringComparison.Ordinal))
                return true;
            return false;
        }
    }
}
