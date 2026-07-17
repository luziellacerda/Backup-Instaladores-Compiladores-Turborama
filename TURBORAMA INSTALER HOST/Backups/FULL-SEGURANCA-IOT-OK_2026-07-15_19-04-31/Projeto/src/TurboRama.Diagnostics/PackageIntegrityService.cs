using System.Security.Cryptography;
using System.Text;
using TurboRama.Core.Results;

namespace TurboRama.Diagnostics;

/// <summary>
/// Integridade do pacote/instalador (proposta §5): hashes de EXEs chave.
/// </summary>
public static class PackageIntegrityService
{
    public static OperationResult VerifyNearBaseDirectory(string? baseDir = null)
    {
        baseDir ??= AppContext.BaseDirectory;
        var exes = new List<string>();
        TryAdd(exes, Path.Combine(baseDir, "TurboRama.UI.exe"));
        // pack layout
        string packRoot = Path.GetFullPath(Path.Combine(baseDir, ".."));
        TryAdd(exes, Path.Combine(packRoot, "App", "Launcher", "TurboRama.Launcher.exe"));
        TryAdd(exes, Path.Combine(packRoot, "App", "Watchdog", "TurboRama.Watchdog.exe"));
        TryAdd(exes, Path.Combine(packRoot, "App", "Maintenance", "TurboRama.Maintenance.exe"));
        TryAdd(exes, Path.Combine(baseDir, "App", "Launcher", "TurboRama.Launcher.exe"));

        string hashesFile = Path.Combine(packRoot, "PACK-HASHES.sha256");
        if (!File.Exists(hashesFile))
        {
            hashesFile = Path.Combine(baseDir, "PACK-HASHES.sha256");
        }

        if (!File.Exists(hashesFile))
        {
            // gera hashes locais (auto-check) se pelo menos UI existe
            var present = exes.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (present.Count == 0)
            {
                return OperationResult.Ok(
                    "Integridade pack: nenhum EXE do pack ao lado do UI (normal se só UI publicado).",
                    "PackageIntegrity",
                    currentState: "Skipped");
            }

            var sb = new StringBuilder();
            foreach (string f in present)
            {
                sb.AppendLine(ComputeSha256(f) + "  " + f);
            }

            return OperationResult.Ok(
                "Integridade: " + present.Count + " EXE(s) hasheados (sem PACK-HASHES.sha256 de fábrica).",
                "PackageIntegrity",
                currentState: sb.ToString());
        }

        // verifica contra arquivo
        int ok = 0, fail = 0;
        var details = new List<string>();
        foreach (string line in File.ReadAllLines(hashesFile))
        {
            string t = line.Trim();
            if (t.Length < 66 || t.StartsWith('#'))
            {
                continue;
            }

            string expect = t[..64].Trim();
            string pathPart = t[64..].Trim();
            if (pathPart.StartsWith("*") || pathPart.StartsWith("  "))
            {
                pathPart = pathPart.TrimStart('*', ' ');
            }

            // resolve relative to pack root
            string full = Path.IsPathRooted(pathPart)
                ? pathPart
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(hashesFile)!, pathPart));
            if (!File.Exists(full))
            {
                // try filename only under pack
                string name = Path.GetFileName(pathPart);
                string alt = Directory.EnumerateFiles(Path.GetDirectoryName(hashesFile)!, name, SearchOption.AllDirectories)
                    .FirstOrDefault() ?? full;
                full = alt;
            }

            if (!File.Exists(full))
            {
                fail++;
                details.Add("MISSING " + pathPart);
                continue;
            }

            string actual = ComputeSha256(full);
            if (actual.Equals(expect, StringComparison.OrdinalIgnoreCase))
            {
                ok++;
            }
            else
            {
                fail++;
                details.Add("MISMATCH " + Path.GetFileName(full));
            }
        }

        if (fail > 0)
        {
            return OperationResult.Fail(
                "Integridade pack FALHOU: ok=" + ok + " fail=" + fail + " | " + string.Join("; ", details.Take(5)),
                "PACK_HASH",
                "PackageIntegrity");
        }

        return OperationResult.Ok("Integridade pack OK (" + ok + " arquivos).", "PackageIntegrity");
    }

    public static string ComputeSha256(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(fs);
        return Convert.ToHexString(hash);
    }

    private static void TryAdd(List<string> list, string path)
    {
        list.Add(path);
    }
}
