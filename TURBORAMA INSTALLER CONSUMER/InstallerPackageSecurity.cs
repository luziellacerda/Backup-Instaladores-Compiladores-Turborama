using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace InstallerHost
{
	/// <summary>
	/// Mantém um handle de leitura sem compartilhamento de escrita/exclusão vivo
	/// desde a validação até o término do processo que consome o arquivo.
	/// </summary>
	internal sealed class TrustedInstallerFile : IDisposable
	{
		private FileStream stream;

		internal TrustedInstallerFile(string path, FileStream stream)
		{
			Path = path;
			this.stream = stream;
		}

		public string Path { get; private set; }
		internal FileStream Stream { get { return stream; } }

		public void Dispose()
		{
			FileStream current = stream;
			stream = null;
			if (current != null)
			{
				current.Dispose();
			}
		}
	}

	internal static class InstallerPackageSecurity
	{
		private const uint WtdUiNone = 2;
		private const uint WtdRevokeNone = 0;
		private const uint WtdRevokeWholeChain = 1;
		private const uint WtdChoiceFile = 1;
		private const uint WtdStateActionIgnore = 0;
		private const uint WtdRevocationCheckChain = 0x00000040;
		private const uint WtdCacheOnlyUrlRetrieval = 0x00001000;
		private const int CryptERevocationOffline = unchecked((int)0x80092013);
		private const int CertERevocationFailure = unchecked((int)0x800B010E);
		private const long MaxArchiveExpandedBytes = 536870912L;

		private static readonly Guid WinTrustActionGenericVerifyV2 =
			new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

		public static TrustedInstallerFile OpenTrustedInstaller(
			string filePath,
			GamingRuntimeComponent component,
			string label)
		{
			if (component == null)
			{
				throw new ArgumentNullException("component");
			}

			PrerequisitePayloadLock payload =
				PrerequisiteIntegrityCatalog.GetRequiredPayload(component.BundleFileName);
			string fileName = System.IO.Path.GetFileName(filePath);
			if (string.Equals(fileName, payload.name, StringComparison.OrdinalIgnoreCase))
			{
				if (payload.fileType == "Zip")
				{
					throw new InvalidDataException("Um ZIP nunca pode ser iniciado diretamente: " + payload.name + ".");
				}
				return OpenAndVerify(filePath, payload.length, payload.sha256,
					payload.signerSubject, payload.signerThumbprint, payload.certificatePublicKeySha256,
					label, true, true);
			}

			PrerequisiteArchiveEntryLock entry =
				PrerequisiteIntegrityCatalog.GetRequiredArchiveEntry(payload, fileName);
			return OpenAndVerify(filePath, entry.length, entry.sha256,
				entry.signerSubject, entry.signerThumbprint, entry.certificatePublicKeySha256,
				label, true, true);
		}

		public static TrustedInstallerFile OpenTrustedPayload(
			string filePath,
			GamingRuntimeComponent component,
			string label)
		{
			if (component == null)
			{
				throw new ArgumentNullException("component");
			}
			PrerequisitePayloadLock payload =
				PrerequisiteIntegrityCatalog.GetRequiredPayload(component.BundleFileName);
			bool authenticode = payload.fileType == "Exe" || payload.fileType == "Msi";
			return OpenAndVerify(filePath, payload.length, payload.sha256,
				payload.signerSubject, payload.signerThumbprint, payload.certificatePublicKeySha256,
				label, authenticode, true);
		}

		public static TrustedInstallerFile OpenTrustedSystemBinary(string filePath, string label)
		{
			string fullPath = System.IO.Path.GetFullPath(filePath ?? string.Empty);
			string windowsPath = System.IO.Path.GetFullPath(
				Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
			if (!fullPath.StartsWith(windowsPath, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("Comando fora do diretório protegido do Windows: " + fullPath + ".");
			}
			if (!File.Exists(fullPath))
			{
				throw new FileNotFoundException("Comando do Windows não encontrado.", fullPath);
			}
			if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
			{
				throw new InvalidDataException("Reparse point rejeitado para comando do Windows: " + fullPath + ".");
			}

			FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
			try
			{
				int trustStatus = VerifyAuthenticode(fullPath, true, false);
				if (trustStatus != 0)
				{
					throw new InvalidDataException(
						"A assinatura online do comando oficial do Windows não pôde ser confirmada (0x" +
						trustStatus.ToString("X8") + ").");
				}
				Logger.Log("Verified protected Windows binary: " + (label ?? System.IO.Path.GetFileName(fullPath)));
				return new TrustedInstallerFile(fullPath, stream);
			}
			catch
			{
				stream.Dispose();
				throw;
			}
		}

		public static string ExtractAndVerifyArchiveInstaller(
			string archivePath,
			GamingRuntimeComponent component,
			SecureInstallerStaging staging)
		{
			if (component == null)
			{
				throw new ArgumentNullException("component");
			}
			if (staging == null)
			{
				throw new ArgumentNullException("staging");
			}
			PrerequisitePayloadLock payload =
				PrerequisiteIntegrityCatalog.GetRequiredPayload(component.BundleFileName);
			if (payload.fileType != "Zip")
			{
				throw new InvalidDataException("O payload não é um ZIP registrado: " + payload.name + ".");
			}

			PrerequisiteArchiveEntryLock[] expectedEntries = payload.archiveEntries ?? new PrerequisiteArchiveEntryLock[0];
			if (expectedEntries.Length != 1)
			{
				throw new InvalidDataException("ZIP sem instalador interno único no catálogo: " + payload.name + ".");
			}

			string destinationRoot = System.IO.Path.GetFullPath(staging.Path)
				.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
			if (!Directory.Exists(destinationRoot))
			{
				throw new DirectoryNotFoundException("Destino privado de extração ausente: " + destinationRoot);
			}

			using (TrustedInstallerFile archiveLease = OpenTrustedPayload(archivePath, component, component.DisplayName + " (ZIP)"))
			{
				archiveLease.Stream.Position = 0L;
				using (ZipArchive archive = new ZipArchive(archiveLease.Stream, ZipArchiveMode.Read, true))
				{
					List<ZipArchiveEntry> files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToList();
					if (files.Count != expectedEntries.Length)
					{
						throw new InvalidDataException("Conteúdo inesperado no ZIP " + payload.name + ".");
					}

					long expandedTotal = 0L;
					string installerPath = null;
					foreach (ZipArchiveEntry zipEntry in files)
					{
						string normalizedName = NormalizeArchiveName(zipEntry.FullName);
						PrerequisiteArchiveEntryLock expected =
							PrerequisiteIntegrityCatalog.GetRequiredArchiveEntry(payload, normalizedName);
						if (zipEntry.Length != expected.length || zipEntry.Length <= 0L)
						{
							throw new InvalidDataException("Tamanho divergente da entrada " + normalizedName + ".");
						}
						expandedTotal = checked(expandedTotal + zipEntry.Length);
						if (expandedTotal > MaxArchiveExpandedBytes ||
							(zipEntry.CompressedLength > 0L && zipEntry.Length / zipEntry.CompressedLength > 1000L))
						{
							throw new InvalidDataException("Limites seguros de extração excedidos por " + payload.name + ".");
						}

						string outputPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
							destinationRoot, normalizedName.Replace('/', System.IO.Path.DirectorySeparatorChar)));
						if (!outputPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
						{
							throw new InvalidDataException("Caminho de extração inseguro: " + normalizedName + ".");
						}
						string parent = System.IO.Path.GetDirectoryName(outputPath);
						if (!string.Equals(parent, destinationRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
						{
							throw new InvalidDataException("Subdiretórios não são permitidos no ZIP de instalador: " + normalizedName + ".");
						}

						CopyArchiveEntry(zipEntry, outputPath, expected.length, staging);
						staging.VerifyFilePolicy(outputPath);
						using (TrustedInstallerFile verified = OpenAndVerify(
							outputPath, expected.length, expected.sha256,
							expected.signerSubject, expected.signerThumbprint, expected.certificatePublicKeySha256,
							component.DisplayName + " (interno)", true, true))
						{
						}
						installerPath = outputPath;
					}

					if (string.IsNullOrWhiteSpace(installerPath))
					{
						throw new InvalidDataException("Nenhum instalador aprovado foi extraído de " + payload.name + ".");
					}
					return installerPath;
				}
			}
		}

		private static TrustedInstallerFile OpenAndVerify(
			string filePath,
			long expectedLength,
			string expectedSha256,
			string expectedSubject,
			string expectedThumbprint,
			string expectedPublicKeySha256,
			string label,
			bool requireAuthenticode,
			bool allowRevocationFallback)
		{
			string fullPath = System.IO.Path.GetFullPath(filePath ?? string.Empty);
			if (!File.Exists(fullPath))
			{
				throw new FileNotFoundException("Payload não encontrado: " + fullPath, fullPath);
			}
			if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
			{
				throw new InvalidDataException("Reparse point rejeitado para payload: " + fullPath + ".");
			}

			FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
			try
			{
				if (stream.Length != expectedLength)
				{
					throw new InvalidDataException("Tamanho inesperado de " + label + ".");
				}

				string actualHash = CalculateSha256(stream);
				if (!FixedTimeHexEquals(expectedSha256, actualHash))
				{
					throw new InvalidDataException("SHA-256 divergente de " + label + ".");
				}

				if (requireAuthenticode)
				{
					VerifyPinnedCertificate(fullPath, expectedSubject, expectedThumbprint, expectedPublicKeySha256, label);
					int trustStatus = VerifyAuthenticode(fullPath, true, false);
					bool offlineFallback = false;
					if (trustStatus == CryptERevocationOffline || trustStatus == CertERevocationFailure)
					{
						if (!allowRevocationFallback || string.IsNullOrWhiteSpace(expectedSha256) ||
							string.IsNullOrWhiteSpace(expectedSubject) || string.IsNullOrWhiteSpace(expectedThumbprint) ||
							string.IsNullOrWhiteSpace(expectedPublicKeySha256))
						{
							throw new InvalidDataException("A revogação online não pôde ser consultada para " + label + ".");
						}
						trustStatus = VerifyAuthenticode(fullPath, false, true);
						offlineFallback = true;
					}
					if (trustStatus != 0)
					{
						throw new InvalidDataException("Assinatura Authenticode inválida para " + label +
							" (0x" + trustStatus.ToString("X8") + ").");
					}
					Logger.Log("Verified package: " + label + " | SHA-256=" + actualHash +
						(offlineFallback ? " | revocation=fallback-offline-pinned" : " | revocation=online"));
				}
				else
				{
					Logger.Log("Verified archive payload: " + label + " | SHA-256=" + actualHash);
				}

				stream.Position = 0L;
				return new TrustedInstallerFile(fullPath, stream);
			}
			catch
			{
				stream.Dispose();
				throw;
			}
		}

		private static void VerifyPinnedCertificate(
			string filePath,
			string expectedSubject,
			string expectedThumbprint,
			string expectedPublicKeySha256,
			string label)
		{
			if (string.IsNullOrWhiteSpace(expectedSubject) || string.IsNullOrWhiteSpace(expectedThumbprint) ||
				string.IsNullOrWhiteSpace(expectedPublicKeySha256))
			{
				throw new InvalidDataException("Âncora de certificado obrigatória ausente para " + label + ".");
			}

			using (X509Certificate2 certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath)))
			{
				string subject = certificate.Subject ?? string.Empty;
				string thumbprint = NormalizeHex(certificate.Thumbprint);
				string publicKeyHash;
				using (SHA256 sha256 = SHA256.Create())
				{
					publicKeyHash = ToHex(sha256.ComputeHash(certificate.GetPublicKey()));
				}

				if (!string.Equals(subject, expectedSubject, StringComparison.Ordinal) ||
					!FixedTimeHexEquals(expectedThumbprint, thumbprint) ||
					!FixedTimeHexEquals(expectedPublicKeySha256, publicKeyHash))
				{
					throw new InvalidDataException("O editor/âncora do certificado não corresponde ao catálogo para " + label + ".");
				}
			}
		}

		private static void CopyArchiveEntry(
			ZipArchiveEntry entry,
			string outputPath,
			long expectedLength,
			SecureInstallerStaging staging)
		{
			long total = 0L;
			using (Stream input = entry.Open())
			using (FileStream output = staging.CreateFileForWrite(outputPath))
			{
				byte[] buffer = new byte[65536];
				int read;
				while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
				{
					total = checked(total + read);
					if (total > expectedLength)
					{
						throw new InvalidDataException("Entrada expandiu além do tamanho registrado: " + entry.FullName + ".");
					}
					output.Write(buffer, 0, read);
				}
				output.Flush(true);
			}
			if (total != expectedLength)
			{
				throw new InvalidDataException("Entrada extraída com tamanho incompleto: " + entry.FullName + ".");
			}
		}

		private static string NormalizeArchiveName(string value)
		{
			string normalized = (value ?? string.Empty).Replace('\\', '/').TrimStart('/');
			if (normalized.Length == 0 || normalized.Contains("../") || normalized.IndexOf(':') >= 0 ||
				System.IO.Path.IsPathRooted(normalized))
			{
				throw new InvalidDataException("Entrada ZIP insegura: " + value + ".");
			}
			return normalized;
		}

		private static string CalculateSha256(Stream stream)
		{
			stream.Position = 0L;
			using (SHA256 sha256 = SHA256.Create())
			{
				return ToHex(sha256.ComputeHash(stream));
			}
		}

		private static bool FixedTimeHexEquals(string expected, string actual)
		{
			byte[] left;
			byte[] right;
			try
			{
				left = ParseHex(expected);
				right = ParseHex(actual);
			}
			catch
			{
				return false;
			}

			int difference = left.Length ^ right.Length;
			int length = Math.Min(left.Length, right.Length);
			for (int index = 0; index < length; index++)
			{
				difference |= left[index] ^ right[index];
			}
			return difference == 0;
		}

		private static byte[] ParseHex(string value)
		{
			string normalized = NormalizeHex(value);
			if ((normalized.Length & 1) != 0)
			{
				throw new FormatException("Hexadecimal com comprimento ímpar.");
			}
			byte[] bytes = new byte[normalized.Length / 2];
			for (int index = 0; index < bytes.Length; index++)
			{
				bytes[index] = Convert.ToByte(normalized.Substring(index * 2, 2), 16);
			}
			return bytes;
		}

		private static string NormalizeHex(string value)
		{
			return (value ?? string.Empty).Replace(" ", string.Empty).Replace(":", string.Empty).ToUpperInvariant();
		}

		private static string ToHex(byte[] bytes)
		{
			return BitConverter.ToString(bytes ?? new byte[0]).Replace("-", string.Empty);
		}

		private static int VerifyAuthenticode(string filePath, bool onlineRevocation, bool cacheOnly)
		{
			WinTrustFileInfo fileInfo = new WinTrustFileInfo();
			fileInfo.StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
			fileInfo.FilePath = Marshal.StringToCoTaskMemUni(System.IO.Path.GetFullPath(filePath));

			IntPtr fileInfoPointer = IntPtr.Zero;
			try
			{
				fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WinTrustFileInfo)));
				Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

				WinTrustData trustData = new WinTrustData();
				trustData.StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData));
				trustData.UiChoice = WtdUiNone;
				trustData.RevocationChecks = onlineRevocation ? WtdRevokeWholeChain : WtdRevokeNone;
				trustData.UnionChoice = WtdChoiceFile;
				trustData.FileInfoPointer = fileInfoPointer;
				trustData.StateAction = WtdStateActionIgnore;
				trustData.ProviderFlags = (onlineRevocation ? WtdRevocationCheckChain : 0U) |
					(cacheOnly ? WtdCacheOnlyUrlRetrieval : 0U);

				Guid action = WinTrustActionGenericVerifyV2;
				return WinVerifyTrust(new IntPtr(-1), ref action, ref trustData);
			}
			finally
			{
				if (fileInfoPointer != IntPtr.Zero)
				{
					Marshal.DestroyStructure(fileInfoPointer, typeof(WinTrustFileInfo));
					Marshal.FreeCoTaskMem(fileInfoPointer);
				}
				if (fileInfo.FilePath != IntPtr.Zero)
				{
					Marshal.FreeCoTaskMem(fileInfo.FilePath);
				}
			}
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct WinTrustFileInfo
		{
			public uint StructSize;
			public IntPtr FilePath;
			public IntPtr FileHandle;
			public IntPtr KnownSubject;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct WinTrustData
		{
			public uint StructSize;
			public IntPtr PolicyCallbackData;
			public IntPtr SipClientData;
			public uint UiChoice;
			public uint RevocationChecks;
			public uint UnionChoice;
			public IntPtr FileInfoPointer;
			public uint StateAction;
			public IntPtr StateData;
			public IntPtr UrlReference;
			public uint ProviderFlags;
			public uint UiContext;
		}

		[DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern int WinVerifyTrust(IntPtr windowHandle, ref Guid actionId, ref WinTrustData trustData);
	}
}
