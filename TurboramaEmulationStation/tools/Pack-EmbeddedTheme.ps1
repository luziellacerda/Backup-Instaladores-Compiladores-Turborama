#Requires -Version 5.1
<#
.SYNOPSIS
Creates the deterministic embedded TurboRama theme payload without Python.

.DESCRIPTION
Uses only Windows PowerShell and the installed .NET Framework. The resulting
resource starts with a payload identity header and contains a deterministic ZIP
obfuscated with the key expected by EmbeddedTheme.cpp.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Source,

    [Parameter(Mandatory = $true)]
    [string]$Output
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packerSource = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TurboRama.Build
{
    public static class EmbeddedThemePacker
    {
        private static readonly byte[] Key = new byte[] {
            0xB3, 0x57, 0x9E, 0x24, 0xC8, 0x6A, 0x11, 0xFD,
            0x45, 0x8B, 0xD2, 0x37, 0xE9, 0x02, 0xAC, 0x71
        };

        private const int CopyBufferSize = 4 * 1024 * 1024;

        public static string Pack(string embeddedRoot, string output)
        {
            string themeDirectory = ResolveThemeDirectory(Path.GetFullPath(embeddedRoot));
            if (!Directory.Exists(themeDirectory))
                throw new DirectoryNotFoundException("Theme folder not found: " + themeDirectory);
            if (!File.Exists(Path.Combine(themeDirectory, "theme.xml")))
                throw new FileNotFoundException("theme.xml not found in: " + themeDirectory);

            output = Path.GetFullPath(output);
            string outputDirectory = Path.GetDirectoryName(output);
            if (String.IsNullOrEmpty(outputDirectory))
                throw new InvalidOperationException("The theme output directory is invalid.");
            Directory.CreateDirectory(outputDirectory);

            string nonce = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            string zipTemporary = Path.Combine(outputDirectory, ".embedded-theme-" + nonce + ".zip");
            string outputTemporary = output + "." + nonce + ".tmp";
            try
            {
                int fileCount = CreateDeterministicZip(themeDirectory, zipTemporary);
                string identity = ComputeMd5(zipTemporary);
                Obfuscate(zipTemporary, outputTemporary, identity);
                ReplaceAtomically(outputTemporary, output);
                return identity + "|" + new FileInfo(zipTemporary).Length.ToString(CultureInfo.InvariantCulture)
                    + "|" + fileCount.ToString(CultureInfo.InvariantCulture);
            }
            finally
            {
                TryDelete(zipTemporary);
                TryDelete(outputTemporary);
            }
        }

        private static string ResolveThemeDirectory(string embeddedRoot)
        {
            if (!Directory.Exists(embeddedRoot) || File.Exists(Path.Combine(embeddedRoot, "theme.xml")))
                return embeddedRoot;

            return Directory.GetDirectories(embeddedRoot)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(path => File.Exists(Path.Combine(path, "theme.xml"))) ?? embeddedRoot;
        }

        private static int CreateDeterministicZip(string themeDirectory, string destination)
        {
            string root = themeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string[] files = Directory.GetFiles(themeDirectory, "*", SearchOption.AllDirectories)
                .OrderBy(path => path.Substring(root.Length).Replace('\\', '/'), StringComparer.Ordinal)
                .ToArray();

            using (FileStream stream = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 4096, FileOptions.WriteThrough))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, false, Encoding.UTF8))
            {
                foreach (string source in files)
                {
                    string relative = source.Substring(root.Length).Replace('\\', '/');
                    ZipArchiveEntry entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    entry.ExternalAttributes = unchecked((int)0x81A40000); // regular file, mode 0644
                    using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                        CopyBufferSize, FileOptions.SequentialScan))
                    using (Stream output = entry.Open())
                    {
                        input.CopyTo(output, CopyBufferSize);
                    }
                }
            }
            return files.Length;
        }

        private static string ComputeMd5(string file)
        {
            using (MD5 digest = MD5.Create())
            using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                CopyBufferSize, FileOptions.SequentialScan))
            {
                return BitConverter.ToString(digest.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void Obfuscate(string source, string destination, string identity)
        {
            byte[] header = Encoding.ASCII.GetBytes("TRTHEME1:" + identity + "\n");
            byte[] buffer = new byte[CopyBufferSize];
            long offset = 0;
            using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                CopyBufferSize, FileOptions.SequentialScan))
            using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                CopyBufferSize, FileOptions.WriteThrough))
            {
                output.Write(header, 0, header.Length);
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int index = 0; index < read; ++index)
                        buffer[index] ^= Key[(offset + index) % Key.Length];
                    output.Write(buffer, 0, read);
                    offset += read;
                }
                output.Flush(true);
            }
        }

        private static void ReplaceAtomically(string source, string destination)
        {
            if (File.Exists(destination))
                File.Replace(source, destination, null, true);
            else
                File.Move(source, destination);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
'@

Add-Type -TypeDefinition $packerSource -Language CSharp -ReferencedAssemblies @(
    'System.IO.Compression.dll',
    'System.IO.Compression.FileSystem.dll'
)

$result = [TurboRama.Build.EmbeddedThemePacker]::Pack($Source, $Output).Split('|')
Write-Host "Tema empacotado de forma deterministica: $($result[2]) arquivos, $($result[1]) bytes."
Write-Host "Identidade do payload: $($result[0])"
Write-Host "Saida: $([IO.Path]::GetFullPath($Output))"
