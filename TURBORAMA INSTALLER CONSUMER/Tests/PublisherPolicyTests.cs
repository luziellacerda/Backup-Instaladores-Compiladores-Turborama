using System;
using System.IO;
using System.Linq;

namespace InstallerHost
{
	internal static class PublisherPolicyTests
	{
		private const string DokanySubject = "CN=LEOSAC, O=LEOSAC, STREET=39 rue Principale, PostalCode=67220, L=Breitenau, S=Bas-Rhin, C=FR, SERIALNUMBER=919 690 420 00014, OID.1.3.6.1.4.1.311.60.2.1.1=Colmar, OID.1.3.6.1.4.1.311.60.2.1.2=Haut-Rhin, OID.1.3.6.1.4.1.311.60.2.1.3=FR, OID.2.5.4.15=Private Organization";
		private const string ExcludedPrereleaseSubject = "CN=NAVIMATICS LLC, O=NAVIMATICS LLC, L=KIRKLAND, S=Washington, C=US";
		private const string MicrosoftSubject = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";
		private const string Thumbprint = "0123456789ABCDEF0123456789ABCDEF01234567";
		private const string PublicKeyHash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

		internal static int Run()
		{
			int passed = 0;
			Action<bool, string> verify = delegate(bool condition, string name)
			{
				if (!condition) throw new InvalidOperationException("FAIL: " + name);
				passed++;
				Console.WriteLine("PASS: " + name);
			};
			const string eclipseSubject = "CN=Eclipse Foundation, O=Eclipse Foundation, L=Bruxelles, C=BE";
			foreach (int major in new[] { 8, 17, 21, 25 })
			{
				string payload = "temurin-jre-" + major + "-x64.msi";
				verify(Accepts(payload, payload, eclipseSubject, Thumbprint, PublicKeyHash), "Temurin " + major + " binds its exact publisher to its MSI name");
				verify(!Accepts(payload, payload, MicrosoftSubject, Thumbprint, PublicKeyHash), "Microsoft cannot replace a Temurin " + major + " payload");
			}
			verify(!Accepts("vc_redist.x64.exe", "vc_redist.x64.exe", eclipseSubject, Thumbprint, PublicKeyHash),
				"Eclipse signer cannot replace a Microsoft package");

			verify(Accepts("DokanSetup.exe", "DokanSetup.exe", DokanySubject, Thumbprint, PublicKeyHash),
				"Dokany accepts only its exact top-level signer subject");
			verify(!Accepts("winfsp-2.2.26215.msi", "winfsp-2.2.26215.msi", ExcludedPrereleaseSubject, Thumbprint, PublicKeyHash),
				"The removed prerelease WinFsp signer has no approved payload exception");
			verify(!Accepts("DokanSetup.exe", "DokanSetup.exe", ExcludedPrereleaseSubject, Thumbprint, PublicKeyHash),
				"An excluded third-party signer cannot replace Dokany");
			verify(!Accepts("DokanSetup.exe", "DokanSetup.exe", MicrosoftSubject, Thumbprint, PublicKeyHash),
				"Microsoft signer cannot replace the exact signer required by Dokany");
			verify(!Accepts("vc_redist.x64.exe", "vc_redist.x64.exe", DokanySubject, Thumbprint, PublicKeyHash),
				"Dokany signer cannot be reused for a Microsoft payload");
			verify(!Accepts("DokanSetup.exe", "DokanSetup.exe/inner.exe", DokanySubject, Thumbprint, PublicKeyHash) &&
				!Accepts("DokanSetup.exe", "DokanSetup.exe/inner.exe", MicrosoftSubject, Thumbprint, PublicKeyHash),
				"Third-party exception is restricted to the top-level bound payload");
			verify(Accepts("vc_redist.x64.exe", "vc_redist.x64.exe", MicrosoftSubject, Thumbprint, PublicKeyHash),
				"Existing Microsoft payload policy remains accepted");
			verify(!Accepts("DokanSetup.exe", "DokanSetup.exe", DokanySubject, "XYZ", PublicKeyHash),
				"Malformed thumbprint is rejected after publisher validation");
			verify(!Accepts("DokanSetup.exe", "DokanSetup.exe", DokanySubject, Thumbprint, "00"),
				"Malformed public-key hash is rejected after publisher validation");

			string msiexec = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
			string dism = Path.Combine(Environment.SystemDirectory, "dism.exe");
			using (TrustedInstallerFile lease = InstallerPackageSecurity.OpenTrustedSystemBinary(msiexec, "Windows Installer test"))
			{
				verify(lease.Stream.Length > 0L,
					"Catalog-signed System32 msiexec is accepted without an online lookup");
			}
			using (TrustedInstallerFile lease = InstallerPackageSecurity.OpenTrustedSystemBinary(dism, "DISM test"))
			{
				verify(lease.Stream.Length > 0L,
					"Catalog-signed System32 DISM is accepted without an online lookup");
			}
			int[] layout = InstallerPackageSecurity.GetWinTrustLayoutForTest();
			int[] expectedLayout = IntPtr.Size == 8
				? new[] { 88, 32, 72, 16, 56, 8, 64, 56, 16, 24 }
				: new[] { 52, 16, 40, 8, 32, 4, 44, 40, 12, 20 };
			verify(layout.SequenceEqual(expectedLayout),
				"WinTrust and Microsoft-root policy layouts match the Windows SDK ABI for this process architecture");
			bool unapprovedSystemCommandRejected = false;
			try
			{
				using (InstallerPackageSecurity.OpenTrustedSystemBinary(
					Path.Combine(Environment.SystemDirectory, "cmd.exe"), "unapproved command test"))
				{
				}
			}
			catch (InvalidDataException)
			{
				unapprovedSystemCommandRejected = true;
			}
			verify(unapprovedSystemCommandRejected,
				"Only the two explicitly required Windows system executables are authorized");
			for (int catalogAttempt = 0; catalogAttempt < 32; catalogAttempt++)
			{
				if (InstallerPackageSecurity.VerifyCatalogAuthenticodeForTest(msiexec) != 0)
				{
					throw new InvalidOperationException("Catalog verification state failed to close cleanly.");
				}
			}
			verify(true, "Repeated catalog verification closes each WinTrust state before reuse");
			verify(InstallerPackageSecurity.VerifyCatalogAuthenticodeForTest(
				typeof(PublisherPolicyTests).Assembly.Location) != 0,
				"An unsigned executable cannot pass through the Windows catalog fallback");
			string alteredSystemFile = Path.Combine(
				Path.GetDirectoryName(typeof(PublisherPolicyTests).Assembly.Location),
				"altered-msiexec-" + Guid.NewGuid().ToString("N") + ".exe");
			try
			{
				File.Copy(msiexec, alteredSystemFile, false);
				using (FileStream altered = new FileStream(
					alteredSystemFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
				{
					altered.Position = altered.Length - 1;
					int original = altered.ReadByte();
					altered.Position = altered.Length - 1;
					altered.WriteByte((byte)(original ^ 0x01));
					altered.Flush(true);
				}
				verify(InstallerPackageSecurity.VerifyCatalogAuthenticodeForTest(alteredSystemFile) != 0,
					"A modified copy cannot reuse the genuine Windows catalog signature");
			}
			finally
			{
				if (File.Exists(alteredSystemFile)) File.Delete(alteredSystemFile);
			}
			bool outsideSystem32Rejected = false;
			try
			{
				using (InstallerPackageSecurity.OpenTrustedSystemBinary(
					typeof(PublisherPolicyTests).Assembly.Location, "outside System32 test"))
				{
				}
			}
			catch (InvalidDataException)
			{
				outsideSystem32Rejected = true;
			}
			verify(outsideSystem32Rejected,
				"Catalog verification never permits a system command outside System32");
			verify(GamingRuntimeManifest.GetComponents().All(component =>
				RuntimeInstallerHelper.UsesWindowsInstaller(component) ==
				(component.CanInstallOffline && string.Equals(
					Path.GetExtension(component.BundleFileName), ".msi", StringComparison.OrdinalIgnoreCase))),
				"Complete-plan preflight recognizes every MSI execution family before the first install");
			GamingRuntimeComponent[] legacyComponents = GamingRuntimeManifest.GetComponents()
				.Where(component => component.CanInstallOffline && component.IsLegacy).ToArray();
			verify(legacyComponents.Length == 10 && legacyComponents.All(component =>
			{
				PrerequisitePayloadLock payload =
					PrerequisiteIntegrityCatalog.GetRequiredPayload(component.BundleFileName);
				return string.Equals(payload.fileType, "Zip", StringComparison.Ordinal) &&
					(payload.archiveEntries ?? new PrerequisiteArchiveEntryLock[0]).Length == 1 &&
					string.Equals(payload.archiveEntries[0].name, component.InstallerFileName, StringComparison.OrdinalIgnoreCase) &&
					string.Equals(Path.GetExtension(payload.archiveEntries[0].name), ".exe", StringComparison.OrdinalIgnoreCase);
			}), "All 10 legacy Visual C++ components require one pinned inner EXE in a managed ZIP preflight");

			return passed;
		}

		private static bool Accepts(string payloadName, string label, string subject, string thumbprint, string publicKeyHash)
		{
			try
			{
				PrerequisiteIntegrityCatalog.ValidateSignerAnchorForTest(
					payloadName, label, subject, thumbprint, publicKeyHash);
				return true;
			}
			catch (InvalidDataException)
			{
				return false;
			}
		}
	}
}
