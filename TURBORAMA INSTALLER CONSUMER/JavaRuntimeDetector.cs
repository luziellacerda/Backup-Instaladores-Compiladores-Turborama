using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace InstallerHost
{
	internal static class JavaRuntimeDetector
	{
		private const string RegistrationRoot = @"SOFTWARE\Eclipse Adoptium\JRE";

		internal sealed class Registration
		{
			internal string ProductVersion;
			internal string InstallationPath;
			internal bool MainFeatureInstalled;
		}

		// Temurin's FeatureMain registers this path even when PATH, JAVA_HOME,
		// file associations and the optional JavaSoft registry feature are omitted.
		// Version is the four-field MSI ProductVersion, not JAVA_VERSION text.
		internal static bool TryGetInstalledVersion(int majorVersion, out string detectedVersion)
		{
			detectedVersion = string.Empty;
			if (majorVersion <= 0 || !Environment.Is64BitOperatingSystem) return false;
			bool installed = TrySelectInstalledVersion(majorVersion, ReadRegistrations(majorVersion), IsMatchingAmd64JavaBinary, out detectedVersion);
			Logger.Log("Temurin JRE probe: HKLM Registry64\\" + RegistrationRoot + " | major=" + majorVersion +
				" | confirmed MSI version=" + (installed ? detectedVersion : "not found"));
			return installed;
		}

		// Registration-only evidence is deliberately separate from readiness. It
		// identifies maintenance mode and the existing installation destination
		// even if a file is missing; it must never mark the runtime as healthy.
		internal static bool TryGetRegisteredInstallation(int majorVersion, out string productVersion, out string installationPath)
		{
			productVersion = string.Empty;
			installationPath = string.Empty;
			if (majorVersion <= 0 || !Environment.Is64BitOperatingSystem) return false;
			return TrySelectRegisteredInstallation(majorVersion, ReadRegistrations(majorVersion), out productVersion, out installationPath);
		}

		private static List<Registration> ReadRegistrations(int majorVersion)
		{
			List<Registration> registrations = new List<Registration>();
			try
			{
				// Explicit Registry64 is necessary: the installer may itself run x86.
				// Do not read user-controlled JAVA_HOME/PATH or HKCU installations.
				using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
				using (RegistryKey root = machine.OpenSubKey(RegistrationRoot, false))
				{
					if (root != null)
					{
						foreach (string versionName in root.GetSubKeyNames())
						{
							Version parsed;
							if (!TryParseProductVersion(versionName, majorVersion, out parsed)) continue;
							using (RegistryKey registration = root.OpenSubKey(versionName + @"\hotspot\MSI", false))
							{
								if (registration == null) continue;
								registrations.Add(new Registration
								{
									ProductVersion = versionName,
									InstallationPath = registration.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string,
									MainFeatureInstalled = object.Equals(registration.GetValue("Main"), 1)
								});
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Temurin JRE registry probe failed (Registry64, major " + majorVersion + "): " + ex.Message);
				throw new InvalidOperationException("Não foi possível consultar o registro de instalação do Java " + majorVersion + " x64.", ex);
			}

			return registrations;
		}

		internal static bool TrySelectRegisteredInstallation(int majorVersion, IEnumerable<Registration> registrations,
			out string productVersion, out string installationPath)
		{
			productVersion = string.Empty;
			installationPath = string.Empty;
			if (majorVersion <= 0 || registrations == null) return false;
			Version newest = null;
			foreach (Registration registration in registrations)
			{
				Version parsed;
				string normalized;
				if (!TryReadRegistration(registration, majorVersion, out parsed, out normalized)) continue;
				if (newest == null || parsed > newest)
				{
					newest = parsed;
					installationPath = normalized;
				}
			}
			if (newest == null) return false;
			productVersion = newest.ToString();
			return true;
		}

		internal static bool TrySelectInstalledVersion(int majorVersion, IEnumerable<Registration> registrations,
			Func<string, Version, bool> isMatchingBinary, out string detectedVersion)
		{
			detectedVersion = string.Empty;
			if (majorVersion <= 0 || registrations == null || isMatchingBinary == null) return false;
			Version newest = null;
			foreach (Registration registration in registrations)
			{
				Version parsed;
				string installationPath;
				if (!TryReadRegistration(registration, majorVersion, out parsed, out installationPath)) continue;

				// A stale registry key or a Java executable alone is not an installed JRE.
				if (!isMatchingBinary(Path.Combine(installationPath, "bin", "java.exe"), parsed) ||
					!isMatchingBinary(Path.Combine(installationPath, "bin", "server", "jvm.dll"), parsed)) continue;
				if (newest == null || parsed > newest) newest = parsed;
			}
			if (newest == null) return false;
			detectedVersion = newest.ToString();
			return true;
		}

		private static bool TryReadRegistration(Registration registration, int requestedMajor,
			out Version productVersion, out string installationPath)
		{
			productVersion = null;
			installationPath = null;
			return registration != null && registration.MainFeatureInstalled &&
				TryParseProductVersion(registration.ProductVersion, requestedMajor, out productVersion) &&
				TryNormalizeLocalPath(registration.InstallationPath, out installationPath);
		}

		internal static bool TryParseProductVersion(string value, int requestedMajor, out Version version)
		{
			version = null;
			Version parsed;
			if (requestedMajor <= 0 || !Version.TryParse(value, out parsed) || parsed.Major != requestedMajor ||
				parsed.Build < 0 || parsed.Revision < 0) return false;
			version = parsed;
			return true;
		}

		private static bool TryNormalizeLocalPath(string value, out string normalized)
		{
			normalized = null;
			// Registered machine installation paths must be local absolute paths;
			// never turn environment text or a network share into executable evidence.
			if (string.IsNullOrWhiteSpace(value) || value.Length < 3 || !char.IsLetter(value[0]) ||
				value[1] != ':' || (value[2] != '\\' && value[2] != '/') || value.IndexOf('%') >= 0) return false;
			try
			{
				normalized = Path.GetFullPath(value);
				return true;
			}
			catch (ArgumentException) { return false; }
			catch (NotSupportedException) { return false; }
			catch (PathTooLongException) { return false; }
		}

		private static bool IsMatchingAmd64JavaBinary(string path, Version registeredVersion)
		{
			try
			{
				using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
				{
					if (!IsAmd64PortableExecutable(stream)) return false;
				}
				FileVersionInfo file = FileVersionInfo.GetVersionInfo(path);
				return FileVersionMatchesRegistration(registeredVersion,
					new Version(file.FileMajorPart, file.FileMinorPart, file.FileBuildPart, file.FilePrivatePart));
			}
			catch (Exception ex)
			{
				Logger.Log("Temurin JRE binary probe failed (" + path + "): " + ex.Message);
				return false;
			}
		}

		internal static bool FileVersionMatchesRegistration(Version registered, Version binary)
		{
			if (registered == null || binary == null || registered.Build < 0 || registered.Revision < 0 ||
				binary.Build < 0 || binary.Revision < 0 || registered.Major != binary.Major || registered.Minor != binary.Minor)
				return false;
			// Verified against the official MSI File tables. Java 8's file version
			// encodes update 504 as 5040; modern Java keeps the maintenance field.
			// Do not compare MSI's packed patch/build field with the PE revision.
			return registered.Major == 8
				? (long)registered.Build * 10 == binary.Build && registered.Revision == binary.Revision
				: registered.Build == binary.Build;
		}

		internal static bool IsAmd64PortableExecutable(Stream stream)
		{
			if (stream == null || !stream.CanRead || !stream.CanSeek || stream.Length < 64) return false;
			try
			{
				using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true))
				{
					stream.Position = 0;
					if (reader.ReadUInt16() != 0x5A4D) return false;
					stream.Position = 0x3C;
					int offset = reader.ReadInt32();
					if (offset < 64 || offset > stream.Length - 26) return false;
					stream.Position = offset;
					if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664) return false;
					stream.Position = offset + 24;
					return reader.ReadUInt16() == 0x020B;
				}
			}
			catch (IOException) { return false; }
			catch (ArgumentException) { return false; }
		}
	}
}
