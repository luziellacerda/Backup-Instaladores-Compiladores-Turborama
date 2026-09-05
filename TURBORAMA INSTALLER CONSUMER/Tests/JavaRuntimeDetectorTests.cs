using System;
using System.Collections.Generic;
using System.IO;

namespace InstallerHost
{
	internal static class JavaRuntimeDetectorTests
	{
		private static int Main()
		{
			try
			{
				Console.WriteLine("JAVA DETECTION TESTS: process bits=" + (IntPtr.Size * 8));
				Console.WriteLine("JAVA DETECTION PASS: " + Run());
				return 0;
			}
			catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
		}

		internal static int Run()
		{
			int passed = 0;
			Action<bool, string> verify = delegate(bool condition, string name)
			{
				if (!condition) throw new InvalidOperationException("FAIL: " + name);
				passed++;
				Console.WriteLine("PASS: " + name);
			};
			Version parsed;
			verify(JavaRuntimeDetector.TryParseProductVersion("8.0.504.1", 8, out parsed), "Java 8 accepts the exact four-field MSI version");
			verify(JavaRuntimeDetector.TryParseProductVersion("17.0.20.101", 17, out parsed), "Java 17 preserves the MSI patch/build field");
			verify(JavaRuntimeDetector.TryParseProductVersion("21.0.12.101", 21, out parsed), "Java 21 accepts its exact major");
			verify(JavaRuntimeDetector.TryParseProductVersion("25.0.4.101", 25, out parsed), "Java 25 accepts its exact major and MSI revision");
			verify(!JavaRuntimeDetector.TryParseProductVersion("21.0.12.101", 17, out parsed), "Java 21 cannot satisfy Java 17");
			verify(!JavaRuntimeDetector.TryParseProductVersion("8.0.504.1", 21, out parsed), "Java 8 cannot satisfy Java 21");
			verify(!JavaRuntimeDetector.TryParseProductVersion("17.0.20", 17, out parsed), "Partial MSI evidence is not comparable");
			verify(!JavaRuntimeDetector.TryParseProductVersion("17.0.20.1+1", 17, out parsed), "Java release text is not silently reinterpreted as MSI version");

			JavaRuntimeDetector.Registration java8 = Registration("8.0.504.1", @"C:\Program Files\Eclipse Adoptium\jre-8-test");
			JavaRuntimeDetector.Registration java17 = Registration("17.0.20.101", @"D:\Java Tests\jre-17-test");
			JavaRuntimeDetector.Registration java21 = Registration("21.0.12.101", @"C:\Program Files\Eclipse Adoptium\jre-21-test");
			JavaRuntimeDetector.Registration java25 = Registration("25.0.4.101", @"C:\Program Files\Eclipse Adoptium\jre-25-test");
			string detected;
			HashSet<string> knownFiles = FilesAt(java17.InstallationPath);
			Func<string, Version, bool> matchingFiles = delegate(string path, Version version) { return version.Major == 17 && knownFiles.Contains(path); };
			verify(JavaRuntimeDetector.TrySelectInstalledVersion(17, new[] { java8, java17, java21 }, matchingFiles, out detected) &&
				detected == "17.0.20.101", "Exact registered Java line and complete x64 files are selected from multiple versions");
			knownFiles.Remove(Path.Combine(java17.InstallationPath, "bin", "server", "jvm.dll"));
			verify(!JavaRuntimeDetector.TrySelectInstalledVersion(17, new[] { java17 }, matchingFiles, out detected), "Java executable without JVM is not installed evidence");
			string registeredPath;
			verify(JavaRuntimeDetector.TrySelectRegisteredInstallation(17, new[] { java17 }, out detected, out registeredPath) &&
				detected == "17.0.20.101" && registeredPath == java17.InstallationPath,
				"Incomplete binaries preserve registration-only evidence and original maintenance path");
			knownFiles = FilesAt(java17.InstallationPath);
			knownFiles.Remove(Path.Combine(java17.InstallationPath, "bin", "java.exe"));
			verify(!JavaRuntimeDetector.TrySelectInstalledVersion(17, new[] { java17 }, matchingFiles, out detected), "JVM without Java executable is rejected");
			verify(!JavaRuntimeDetector.TrySelectInstalledVersion(17, new[] { java17 }, delegate(string path, Version version) { return false; }, out detected),
				"Wrong-architecture or unreadable binaries cannot confirm a registry entry");
			knownFiles = FilesAt(java17.InstallationPath);
			java17.MainFeatureInstalled = false;
			verify(!JavaRuntimeDetector.TrySelectInstalledVersion(17, new[] { java17 }, matchingFiles, out detected), "Missing FeatureMain registration is rejected");
			verify(!JavaRuntimeDetector.TrySelectRegisteredInstallation(17, new[] { java17 }, out detected, out registeredPath),
				"Registration-only maintenance evidence also requires FeatureMain");
			java17.MainFeatureInstalled = true;
			foreach (string invalidPath in new[] { "", "relative\\java", @"C:relative\java", @"%JAVA_HOME%", @"\\server\java", @"\\?\C:\java" })
			{
				verify(!JavaRuntimeDetector.TrySelectInstalledVersion(17, new[] { Registration("17.0.20.101", invalidPath) },
					delegate(string path, Version version) { throw new InvalidOperationException("Invalid path reached file probing."); }, out detected),
					"Untrusted/non-local Java path is rejected: " + invalidPath);
				verify(!JavaRuntimeDetector.TrySelectRegisteredInstallation(17, new[] { Registration("17.0.20.101", invalidPath) },
					out detected, out registeredPath), "Maintenance rejects invalid path: " + invalidPath);
			}
			JavaRuntimeDetector.Registration old = Registration("17.0.19.9", @"D:\Java Tests\old");
			verify(JavaRuntimeDetector.TrySelectInstalledVersion(17, new[] { old, java17 }, delegate(string path, Version version) { return true; }, out detected) &&
				detected == "17.0.20.101", "Newest complete MSI version of the requested Java line is selected");
			verify(JavaRuntimeDetector.TrySelectRegisteredInstallation(17, new[] { old, java17, java21 }, out detected, out registeredPath) &&
				detected == "17.0.20.101" && registeredPath == java17.InstallationPath,
				"Maintenance chooses the highest same-major registration without crossing Java lines");
			verify(!JavaRuntimeDetector.TrySelectInstalledVersion(25, new[] { java8, java17, java21 }, matchingFiles, out detected),
				"Other Java lines do not satisfy Java 25");
			verify(JavaRuntimeDetector.TrySelectInstalledVersion(25, new[] { java21, java25 }, delegate(string path, Version version)
				{ return version.Major == 25 && FilesAt(java25.InstallationPath).Contains(path); }, out detected) && detected == "25.0.4.101",
				"Java 25 requires its own complete x64 installation");
			verify(JavaRuntimeDetector.FileVersionMatchesRegistration(new Version("8.0.504.1"), new Version("8.0.5040.1")),
				"Java 8 MSI and binary maintenance fields use their documented representation");
			verify(!JavaRuntimeDetector.FileVersionMatchesRegistration(new Version("8.0.504.1"), new Version("8.0.5020.1")),
				"Stale Java 8 binaries cannot confirm the latest registration");
			verify(JavaRuntimeDetector.FileVersionMatchesRegistration(new Version("17.0.20.101"), new Version("17.0.20.1")),
				"Modern Java MSI packaging revision is not confused with PE revision");
			verify(!JavaRuntimeDetector.FileVersionMatchesRegistration(new Version("17.0.20.101"), new Version("17.0.19.1")),
				"Stale Java 17 binaries cannot confirm a newer registered maintenance release");
			verify(!JavaRuntimeDetector.FileVersionMatchesRegistration(new Version("21.0.12.101"), new Version("17.0.12.1")),
				"Binary file version must match the registered Java major");
			verify(JavaRuntimeDetector.FileVersionMatchesRegistration(new Version("25.0.4.101"), new Version("25.0.4.1")),
				"Java 25 MSI and file maintenance fields agree");

			using (MemoryStream pe = PortableExecutable(0x8664, 0x020B))
				verify(JavaRuntimeDetector.IsAmd64PortableExecutable(pe), "AMD64 PE32+ is recognized without executing it");
			using (MemoryStream pe = PortableExecutable(0x014C, 0x010B))
				verify(!JavaRuntimeDetector.IsAmd64PortableExecutable(pe), "x86 PE cannot satisfy x64 Java");
			using (MemoryStream pe = PortableExecutable(0xAA64, 0x020B))
				verify(!JavaRuntimeDetector.IsAmd64PortableExecutable(pe), "ARM64 PE cannot satisfy x64 Java");
			using (MemoryStream pe = PortableExecutable(0x8664, 0x010B))
				verify(!JavaRuntimeDetector.IsAmd64PortableExecutable(pe), "Mismatched optional PE header is rejected");
			using (MemoryStream invalid = new MemoryStream(new byte[8]))
				verify(!JavaRuntimeDetector.IsAmd64PortableExecutable(invalid), "Truncated executable is rejected");
			using (MemoryStream invalid = PortableExecutable(0x8664, 0x020B))
			{
				invalid.Position = 0x3C;
				invalid.WriteByte(0xFF); invalid.WriteByte(0xFF); invalid.WriteByte(0xFF); invalid.WriteByte(0x7F);
				verify(!JavaRuntimeDetector.IsAmd64PortableExecutable(invalid), "PE offset beyond the file is rejected");
			}
			verify(!JavaRuntimeDetector.TryGetInstalledVersion(0, out detected), "Invalid major never triggers machine probing");
			verify(!JavaRuntimeDetector.TryGetRegisteredInstallation(0, out detected, out registeredPath), "Invalid major never triggers maintenance probing");
			verify(!JavaRuntimeDetector.TryGetInstalledVersion(int.MaxValue, out detected), "Read-only machine probe does not invent an absent Java line");
			return passed;
		}

		private static JavaRuntimeDetector.Registration Registration(string version, string path)
		{
			return new JavaRuntimeDetector.Registration { ProductVersion = version, InstallationPath = path, MainFeatureInstalled = true };
		}

		private static HashSet<string> FilesAt(string path)
		{
			return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				Path.Combine(path, "bin", "java.exe"),
				Path.Combine(path, "bin", "server", "jvm.dll")
			};
		}

		private static MemoryStream PortableExecutable(ushort machine, ushort magic)
		{
			MemoryStream stream = new MemoryStream(new byte[160]);
			using (BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
			{
				writer.Write((ushort)0x5A4D);
				stream.Position = 0x3C; writer.Write(64);
				stream.Position = 64; writer.Write((uint)0x00004550); writer.Write(machine);
				stream.Position = 88; writer.Write(magic);
			}
			stream.Position = 0;
			return stream;
		}
	}
}
