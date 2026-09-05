using System;
using System.IO;
using System.Linq;

namespace InstallerHost
{
    // Runs only against a unique fixture below the supplied test-output root.
    // No installation, user-folder cleanup, or recursive deletion is performed.
    internal static class InstallationFlowPolicyTests
    {
        internal static int Run(string safeRoot)
        {
            string root = Path.GetFullPath(safeRoot);
            Directory.CreateDirectory(root);
            string fixture = Path.Combine(root, "flow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixture);
            int assertions = 0;
            Action<bool, string> verify = delegate(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException("Installation flow: " + message);
                assertions++;
            };

            string standalone = NewCase(fixture, "standalone");
            string standaloneExe = Path.Combine(standalone, "InstallerHost.exe");
            File.WriteAllText(standaloneExe, "Test fixture, never executed.");
            verify(!InstallationFlowPolicy.HasProductPackageArtifacts(standaloneExe),
                "an executable alone is a dependency-only delivery");
            File.WriteAllText(standaloneExe + ".sha256", "delivery checksum, not a product sidecar");
            File.WriteAllText(Path.Combine(standalone, "AnotherInstaller.exe.pkg.001"), "unrelated");
            verify(!InstallationFlowPolicy.HasProductPackageArtifacts(standaloneExe),
                "delivery .exe.sha256 and unrelated package names do not select product mode");

            string[] packageNames =
            {
                "InstallerHost.exe.pkg.001", // Canonical multipart package.
                "InstallerHost.pkg.001",     // Legacy multipart package: cannot be silently ignored.
                "InstallerHost.exe.pkg.bad", // Malformed package: later verification must reject it.
                "InstallerHost.exe.sha256.txt", // Sidecar alone is an incomplete product delivery.
                "InstallerHost.exe.pkg",     // Unsplit canonical package.
                "InstallerHost.pkg",         // Unsplit legacy package.
                "INSTALLERHOST.EXE.PKG.002"  // Windows names are case insensitive; part 001 is absent.
            };
            for (int index = 0; index < packageNames.Length; index++)
            {
                string packageCase = NewCase(fixture, "package-" + index);
                File.WriteAllText(Path.Combine(packageCase, packageNames[index]), "untrusted fixture");
                verify(InstallationFlowPolicy.HasProductPackageArtifacts(Path.Combine(packageCase, "InstallerHost.exe")),
                    "package evidence must select the verified product flow: " + packageNames[index]);
            }

            string directoryArtifact = NewCase(fixture, "directory-artifact");
            Directory.CreateDirectory(Path.Combine(directoryArtifact, "InstallerHost.exe.pkg.001"));
            verify(InstallationFlowPolicy.HasProductPackageArtifacts(Path.Combine(directoryArtifact, "InstallerHost.exe")),
                "a directory using a package name is not silently treated as an absent package");
            verify(ThrowsIOException(delegate
            {
                InstallationFlowPolicy.HasProductPackageArtifacts(Path.Combine(fixture, "missing-directory", "InstallerHost.exe"));
            }), "an unreadable or missing delivery directory does not silently become standalone");

            string occupiedCase = NewCase(fixture, "occupied");
            string preferred = Path.Combine(occupiedCase, "TurboRama");
            Directory.CreateDirectory(preferred);
            string sentinel = Path.Combine(preferred, "existing-user-content.txt");
            File.WriteAllText(sentinel, "preserve-this-content");
            string[] beforeSuggestion = Snapshot(occupiedCase);
            string suggestion = InstallationFlowPolicy.SuggestEmptyDestination(preferred);
            verify(SamePath(suggestion, preferred + "-2"),
                "an occupied preferred folder selects the next free sibling");
            verify(File.ReadAllText(sentinel) == "preserve-this-content",
                "existing preferred-folder contents are preserved");
            verify(beforeSuggestion.SequenceEqual(Snapshot(occupiedCase)),
                "suggesting a sibling performs no filesystem writes");
            verify(!Directory.Exists(suggestion), "a suggested new folder is not reserved or created");

            string fileCase = NewCase(fixture, "file-collision");
            string filePreferred = Path.Combine(fileCase, "TurboRama");
            File.WriteAllText(filePreferred, "existing-file");
            verify(SamePath(InstallationFlowPolicy.SuggestEmptyDestination(filePreferred), filePreferred + "-2"),
                "an existing file at the preferred name is skipped");
            verify(File.ReadAllText(filePreferred) == "existing-file" && !Directory.Exists(filePreferred + "-2"),
                "a file collision is preserved and the suggested sibling remains uncreated");

            string emptyCase = NewCase(fixture, "empty");
            string emptyPreferred = Path.Combine(emptyCase, "TurboRama");
            Directory.CreateDirectory(emptyPreferred);
            verify(SamePath(InstallationFlowPolicy.SuggestEmptyDestination(emptyPreferred), emptyPreferred),
                "a pre-existing empty directory is a valid destination");
            verify(!Directory.EnumerateFileSystemEntries(emptyPreferred).Any(),
                "suggesting an existing empty directory leaves it empty");
            verify(SamePath(InstallationFlowPolicy.SuggestEmptyDestination(emptyPreferred + Path.DirectorySeparatorChar), emptyPreferred),
                "a trailing separator does not turn the suggestion into a child folder");
            verify(ThrowsIOException(delegate { InstallationFlowPolicy.SuggestEmptyDestination(Path.GetPathRoot(root)); }),
                "a drive root is rejected without turning into a drive-relative path");

            string absentCase = NewCase(fixture, "absent");
            string absentPreferred = Path.Combine(absentCase, "NotCreated", "TurboRama");
            verify(SamePath(InstallationFlowPolicy.SuggestEmptyDestination(absentPreferred), absentPreferred),
                "a safe not-yet-created destination is suggested unchanged");
            verify(!Directory.EnumerateFileSystemEntries(absentCase).Any(),
                "suggestion does not create missing parent directories");

            string boundedCase = NewCase(fixture, "bounded");
            string boundedPreferred = Path.Combine(boundedCase, "TurboRama");
            for (int index = 1; index <= 100; index++)
            {
                string candidate = index == 1 ? boundedPreferred : boundedPreferred + "-" + index;
                Directory.CreateDirectory(candidate);
                File.WriteAllText(Path.Combine(candidate, "sentinel.txt"), "preserve-" + index);
            }
            string[] boundedBefore = Snapshot(boundedCase);
            verify(ThrowsIOException(delegate { InstallationFlowPolicy.SuggestEmptyDestination(boundedPreferred); }),
                "100 occupied candidates stop with an explicit failure");
            verify(boundedBefore.SequenceEqual(Snapshot(boundedCase)) &&
                !Directory.Exists(boundedPreferred + "-101"),
                "the bounded search neither changes existing entries nor invents a fallback beyond its limit");
            verify(Enumerable.Range(1, 100).All(index =>
            {
                string candidate = index == 1 ? boundedPreferred : boundedPreferred + "-" + index;
                return File.ReadAllText(Path.Combine(candidate, "sentinel.txt")) == "preserve-" + index;
            }), "all 100 collision sentinels retain their contents");

            string invalidCase = NewCase(fixture, "invalid-parent");
            string invalidParent = Path.Combine(invalidCase, "ParentIsAFile");
            File.WriteAllText(invalidParent, "never-replace-parent");
            string[] invalidBefore = Snapshot(invalidCase);
            verify(ThrowsIOException(delegate
            {
                InstallationFlowPolicy.SuggestEmptyDestination(Path.Combine(invalidParent, "TurboRama"));
            }), "a file in the ancestor chain is rejected rather than used as a fallback");
            verify(invalidBefore.SequenceEqual(Snapshot(invalidCase)) &&
                File.ReadAllText(invalidParent) == "never-replace-parent",
                "an invalid parent and all neighboring entries are unchanged");

            return assertions;
        }

        private static string NewCase(string fixture, string name)
        {
            string path = Path.Combine(fixture, name);
            Directory.CreateDirectory(path);
            return path;
        }

        private static string[] Snapshot(string path)
        {
            return Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)
                .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static bool SamePath(string left, string right)
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static bool ThrowsIOException(Action action)
        {
            try { action(); }
            catch (IOException) { return true; }
            return false;
        }
    }
}
