using System;
using System.IO;

namespace InstallerHost
{
	internal static class PublisherPolicyTests
	{
		private const string DokanySubject = "CN=LEOSAC, O=LEOSAC, STREET=39 rue Principale, PostalCode=67220, L=Breitenau, S=Bas-Rhin, C=FR, SERIALNUMBER=919 690 420 00014, OID.1.3.6.1.4.1.311.60.2.1.1=Colmar, OID.1.3.6.1.4.1.311.60.2.1.2=Haut-Rhin, OID.1.3.6.1.4.1.311.60.2.1.3=FR, OID.2.5.4.15=Private Organization";
		private const string WinFspSubject = "CN=NAVIMATICS LLC, O=NAVIMATICS LLC, L=KIRKLAND, S=Washington, C=US, SERIALNUMBER=604 419 559, OID.2.5.4.15=Private Organization, OID.1.3.6.1.4.1.311.60.2.1.2=Washington, OID.1.3.6.1.4.1.311.60.2.1.3=US";
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

			verify(Accepts("DokanSetup.exe", "DokanSetup.exe", DokanySubject, Thumbprint, PublicKeyHash),
				"Dokany accepts only its exact top-level signer subject");
			verify(Accepts("winfsp-2.2.26215.msi", "winfsp-2.2.26215.msi", WinFspSubject, Thumbprint, PublicKeyHash),
				"WinFsp accepts only its exact top-level signer subject");
			verify(!Accepts("DokanSetup.exe", "DokanSetup.exe", WinFspSubject, Thumbprint, PublicKeyHash) &&
				!Accepts("winfsp-2.2.26215.msi", "winfsp-2.2.26215.msi", DokanySubject, Thumbprint, PublicKeyHash),
				"Third-party signers cannot be crossed between their payload names");
			verify(!Accepts("DokanSetup.exe", "DokanSetup.exe", MicrosoftSubject, Thumbprint, PublicKeyHash) &&
				!Accepts("winfsp-2.2.26215.msi", "winfsp-2.2.26215.msi", MicrosoftSubject, Thumbprint, PublicKeyHash),
				"Microsoft signer cannot replace the exact signer required by a third-party payload");
			verify(!Accepts("vc_redist.x64.exe", "vc_redist.x64.exe", DokanySubject, Thumbprint, PublicKeyHash),
				"Dokany signer cannot be reused for a Microsoft payload");
			verify(!Accepts("vcredist2013_x64.zip", "vcredist2013_x64.zip/vcredist2013_x64.exe", WinFspSubject, Thumbprint, PublicKeyHash),
				"WinFsp signer cannot be reused for a Microsoft ZIP entry");
			verify(!Accepts("DokanSetup.exe", "DokanSetup.exe/inner.exe", DokanySubject, Thumbprint, PublicKeyHash) &&
				!Accepts("DokanSetup.exe", "DokanSetup.exe/inner.exe", MicrosoftSubject, Thumbprint, PublicKeyHash),
				"Third-party exception is restricted to the top-level bound payload");
			verify(Accepts("vc_redist.x64.exe", "vc_redist.x64.exe", MicrosoftSubject, Thumbprint, PublicKeyHash),
				"Existing Microsoft payload policy remains accepted");
			verify(!Accepts("DokanSetup.exe", "DokanSetup.exe", DokanySubject, "XYZ", PublicKeyHash),
				"Malformed thumbprint is rejected after publisher validation");
			verify(!Accepts("winfsp-2.2.26215.msi", "winfsp-2.2.26215.msi", WinFspSubject, Thumbprint, "00"),
				"Malformed public-key hash is rejected after publisher validation");

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
