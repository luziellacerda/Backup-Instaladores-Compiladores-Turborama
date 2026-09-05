using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace InstallerHost
{
    internal static class DotNetDesktopDetectionTests
    {
        private static int checks;
        private static int failures;

        private static int Main(string[] args)
        {
            try
            {
                string expectedBits = ReadArgument(args, "--expect-process-bits=");
                string requiredRuntime10 = ReadArgument(args, "--require-runtime10-x64=");
                int processBits = Environment.Is64BitProcess ? 64 : 32;
                Console.WriteLine("Read-only .NET Desktop probe: process=" + processBits + ", OS=" +
                    (Environment.Is64BitOperatingSystem ? 64 : 32));
                Verify(expectedBits == processBits.ToString(), "test executes in its explicitly requested process architecture");

                foreach (string architecture in new[] { "x64", "x86" })
                {
                    if (architecture == "x64" && !Environment.Is64BitOperatingSystem) continue;
                    List<Version> inventory = ReadIndependentInventory(architecture);
                    Console.WriteLine("Independent " + architecture + " inventory: " +
                        (inventory.Count == 0 ? "no registered or default-path runtimes" : string.Join(", ", inventory)));
                    foreach (int major in new[] { 8, 10 })
                    {
                        Version expected = inventory.Where(version => version.Major == major)
                            .OrderByDescending(version => version).FirstOrDefault();
                        string actualVersion;
                        bool actual = PrerequisiteDetector.IsDotNetDesktopRuntimeInstalled(major, architecture, out actualVersion);
                        string caseName = ".NET " + major + " " + architecture;
                        Verify(actual == (expected != null), caseName + " presence agrees with independent architecture inventory");
                        Verify(expected == null ? string.IsNullOrEmpty(actualVersion) : actualVersion == expected.ToString(),
                            caseName + " reports the exact highest installed version for that architecture");

                        GamingRuntimeComponent component = GamingRuntimeManifest.FindById("dotnet-desktop-" + major + "-" + architecture);
                        RuntimeComponentStatus status = PrerequisiteDetector.DetectRuntimeComponent(new GamingReadinessProfile(), component);
                        GamingReadinessState expectedState = GamingReadinessState.Attention;
                        if (expected != null)
                        {
                            PrerequisitePayloadLock payload = PrerequisiteIntegrityCatalog.GetRequiredPayload(component.BundleFileName);
                            Version minimum = Version.Parse(payload.productVersion);
                            expectedState = CompareTriplet(expected, minimum) >= 0
                                ? GamingReadinessState.Ready : GamingReadinessState.Attention;
                        }
                        Verify(status.State == expectedState, caseName + " readiness honors presence and the bundled minimum version");

                        if (architecture == "x64" && major == 10 && expected != null && expectedState == GamingReadinessState.Ready)
                            VerifyNoRepairLoop(status);

                        if (architecture == "x64" && major == 10 && !string.IsNullOrEmpty(requiredRuntime10))
                        {
                            Verify(expected != null && expected.ToString() == requiredRuntime10,
                                "required local .NET 10 x64 fixture exists at version " + requiredRuntime10);
                            Verify(actual && actualVersion == requiredRuntime10 && status.State == GamingReadinessState.Ready,
                                "required local .NET 10 x64 fixture is confirmed by the installer from this process architecture");
                        }
                    }

                    string absentVersion;
                    Verify(!PrerequisiteDetector.IsDotNetDesktopRuntimeInstalled(9999, architecture, out absentVersion) &&
                        string.IsNullOrEmpty(absentVersion), architecture + " never substitutes another major version for an absent runtime");
                }

                Console.WriteLine("DOTNET DETECTION " + (failures == 0 ? "PASS" : "FAIL") + ": " + checks +
                    " checks, " + failures + " failures; no packages installed and no Registry values changed.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 2;
            }
        }

        // Intentionally independent of the production path resolver. Microsoft registers
        // both runtime architectures under the 32-bit Registry view; the x64/x86 child
        // key identifies the payload architecture, not the process reading the key.
        private static List<Version> ReadIndependentInventory(string architecture)
        {
            HashSet<Version> versions = new HashSet<Version>();
            using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            using (RegistryKey runtime = machine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\" + architecture +
                @"\sharedfx\Microsoft.WindowsDesktop.App"))
            {
                if (runtime != null)
                {
                    foreach (string valueName in runtime.GetValueNames())
                    {
                        Version version;
                        if (Version.TryParse(valueName, out version)) versions.Add(version);
                    }
                }
            }

            // SDKs on CI can expose a default-path shared framework without an MSI
            // registration. Do not invent a required machine fixture on such runners.
            string programFiles = architecture == "x86"
                ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                : Environment.GetEnvironmentVariable("ProgramW6432");
            if (string.IsNullOrWhiteSpace(programFiles) && architecture == "x64" && Environment.Is64BitProcess)
                programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                string sharedFramework = Path.Combine(programFiles, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
                if (Directory.Exists(sharedFramework))
                {
                    foreach (string directory in Directory.GetDirectories(sharedFramework))
                    {
                        Version version;
                        if (Version.TryParse(Path.GetFileName(directory), out version) &&
                            File.Exists(Path.Combine(directory, "System.Windows.Forms.dll")) &&
                            File.Exists(Path.Combine(directory, "Microsoft.WindowsDesktop.App.deps.json")))
                            versions.Add(version);
                    }
                }
            }
            return versions.OrderByDescending(version => version).ToList();
        }

        private static int CompareTriplet(Version detected, Version required)
        {
            return new Version(detected.Major, detected.Minor, detected.Build)
                .CompareTo(new Version(required.Major, required.Minor, required.Build));
        }

        private static void VerifyNoRepairLoop(RuntimeComponentStatus realRuntimeStatus)
        {
            GamingReadinessProfile profile = new GamingReadinessProfile();
            foreach (GamingRuntimeComponent component in GamingRuntimeManifest.GetComponents())
            {
                RuntimeComponentStatus status = component.Id == realRuntimeStatus.Component.Id
                    ? realRuntimeStatus
                    : new RuntimeComponentStatus { Component = component, State = GamingReadinessState.Ready };
                // Make a payload available in this planning fixture so an incorrect
                // detection would genuinely schedule another install/repair attempt.
                status.BundleAvailable = true;
                profile.MutableRuntimeStatuses.Add(status);
            }
            RuntimeInstallPlanItem item = RuntimeInstallerHelper.BuildInstallationPlan(
                profile, GamingRuntimeInstallSelection.RecommendedDefaults())
                .Single(candidate => candidate.Component.Id == realRuntimeStatus.Component.Id);
            Verify(item.Disposition == RuntimeInstallDisposition.AlreadyInstalled,
                "confirmed current .NET 10 x64 is AlreadyInstalled, never scheduled for reinstallation");
            GamingReadinessRepairPlan repair = GamingReadinessRepairPlanner.Create(profile);
            Verify(!repair.RepairableComponents.Any(candidate => candidate.Component.Id == realRuntimeStatus.Component.Id) &&
                !repair.Selection.AllowedComponentIds.Contains(realRuntimeStatus.Component.Id),
                "confirmed current .NET 10 x64 is absent from both repair list and executable repair selection");
        }

        private static string ReadArgument(string[] args, string prefix)
        {
            string argument = args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
            return argument == null ? string.Empty : argument.Substring(prefix.Length);
        }

        private static void Verify(bool condition, string name)
        {
            checks++;
            if (!condition) failures++;
            Console.WriteLine((condition ? "PASS: " : "FAIL: ") + name);
        }
    }
}
