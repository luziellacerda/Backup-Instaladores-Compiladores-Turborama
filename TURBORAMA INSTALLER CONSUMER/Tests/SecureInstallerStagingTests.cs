using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace InstallerHost
{
    internal static class SecureInstallerStagingTests
    {
        private static int Main()
        {
            try
            {
                string firstPath;
                string secondPath;
                using (SecureInstallerStaging first = SecureInstallerStaging.Create("StagingRegression"))
                using (SecureInstallerStaging second = SecureInstallerStaging.Create("StagingRegression"))
                {
                    firstPath = first.Path;
                    secondPath = second.Path;
                    Check(firstPath != secondPath, "Simultaneous runs have independent private roots");
                    string commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                    Check(string.Equals(Path.GetDirectoryName(firstPath), commonData, StringComparison.OrdinalIgnoreCase),
                        "Creation succeeds outside the legacy Admin-owned directory");
                    SecurityIdentifier owner = (SecurityIdentifier)Directory.GetAccessControl(firstPath,
                        AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
                    Check(owner.IsWellKnown(WellKnownSidType.LocalSystemSid), "New root belongs to SYSTEM");
                    string folder = first.CreateSubdirectory("component");
                    string file = Path.Combine(folder, "probe.dat");
                    using (FileStream stream = first.CreateFileForWrite(file)) { stream.WriteByte(42); stream.Flush(true); }
                    first.VerifyFilePolicy(file);
                    Check(File.ReadAllBytes(file)[0] == 42, "Payload creation and full security policy verification succeed");
                    bool duplicateRejected = false;
                    try { using (FileStream ignored = first.CreateFileForWrite(file)) { } }
                    catch (System.ComponentModel.Win32Exception) { duplicateRejected = true; }
                    Check(duplicateRejected, "Existing payload cannot be overwritten");
                    bool escapeRejected = false;
                    try { using (FileStream ignored = first.CreateFileForWrite(Path.Combine(second.Path, "escape.dat"))) { } }
                    catch (IOException) { escapeRejected = true; }
                    Check(escapeRejected, "Writing outside this run's private directory is rejected");
                }
                Check(!Directory.Exists(firstPath) && !Directory.Exists(secondPath), "Both private test trees are cleaned up");
                Console.WriteLine("STAGING INTEGRATION PASS: 7; real private directory/file operations; no installer execution.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static void Check(bool result, string description)
        {
            if (!result) throw new InvalidOperationException(description);
            Console.WriteLine("PASS: " + description);
        }
    }
}
