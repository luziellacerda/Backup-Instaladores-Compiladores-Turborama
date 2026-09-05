using System;
using System.IO;

namespace InstallerHost
{
    internal static class InstallationFlowPolicy
    {
        // Presence is only a UI hint, never package authentication. Even an
        // incomplete/legacy package must enter the verified product flow, not
        // silently turn into a successful dependencies-only installation.
        internal static bool HasProductPackageArtifacts(string executablePath)
        {
            string fullPath = Path.GetFullPath(executablePath);
            string leaf = Path.GetFileName(fullPath);
            string legacyLeaf = Path.GetFileNameWithoutExtension(leaf);
            foreach (string entry in Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(fullPath)))
            {
                string name = Path.GetFileName(entry);
                if (name.Equals(leaf + ".sha256.txt", StringComparison.OrdinalIgnoreCase) ||
                    IsPackageName(name, leaf) || IsPackageName(name, legacyLeaf)) return true;
            }
            return false;
        }

        private static bool IsPackageName(string name, string setupLeaf)
        {
            return name.Equals(setupLeaf + ".pkg", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(setupLeaf + ".pkg.", StringComparison.OrdinalIgnoreCase);
        }

        // Read-only suggestion: never reserve, empty, rename or reuse user data.
        // Extraction revalidates the destination under the limited token.
        internal static string SuggestEmptyDestination(string preferredPath)
        {
            string fullPath = Path.GetFullPath(preferredPath);
            if (fullPath.Equals(Path.GetPathRoot(fullPath), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Escolha uma pasta abaixo da raiz da unidade.");
            string preferred = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Exception lastError = null;
            for (int index = 1; index <= 100; index++)
            {
                string candidate = index == 1 ? preferred : preferred + "-" + index;
                try { return SecureExtractionGuard.ValidateDestinationSelection(candidate); }
                catch (IOException error) { lastError = error; }
                catch (UnauthorizedAccessException error) { lastError = error; }
            }
            throw new IOException("Não foi possível sugerir uma pasta vazia. Use Procurar para escolher outro destino.", lastError);
        }
    }
}
