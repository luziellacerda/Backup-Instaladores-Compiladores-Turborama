using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

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
		private const uint WtdChoiceCatalog = 2;
		private const uint WtdStateActionIgnore = 0;
		private const uint WtdStateActionVerify = 1;
		private const uint WtdStateActionClose = 2;
		private const uint WtdRevocationCheckChain = 0x00000040;
		private const uint WtdCacheOnlyUrlRetrieval = 0x00001000;
		private const int TrustENoSignature = unchecked((int)0x800B0100);
		private const int TrustEExplicitDistrust = unchecked((int)0x800B0111);
		private const int CryptERevocationOffline = unchecked((int)0x80092013);
		private const int CertERevocationFailure = unchecked((int)0x800B010E);
		private const int CertChainPolicyMicrosoftRoot = 7;
		private const long MaxArchiveExpandedBytes = 536870912L;

		private static readonly Guid WinTrustActionGenericVerifyV2 =
			new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
		private static readonly Guid DriverActionVerify =
			new Guid("F750E6C3-38EE-11D1-85E5-00C04FC295EE");

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
			string systemFileName = System.IO.Path.GetFileName(fullPath);
			if (!string.Equals(systemFileName, "msiexec.exe", StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(systemFileName, "dism.exe", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("Comando do Windows fora da lista aprovada: " + systemFileName + ".");
			}
			string systemPath = System.IO.Path.GetFullPath(Environment.SystemDirectory)
				.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
			if (!fullPath.StartsWith(systemPath, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(System.IO.Path.GetDirectoryName(fullPath), systemPath.TrimEnd(System.IO.Path.DirectorySeparatorChar),
					StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("Comando fora do System32 protegido do Windows: " + fullPath + ".");
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
				// Only a registered Windows catalog can authorize these two inbox
				// executables. The catalog member is verified against this already-open
				// handle and its approved signer is inspected before WVT state closes.
				// This is a local OS-integrity check and never requires network access.
				string catalogPath;
				int trustStatus = VerifyCatalogAuthenticode(
					fullPath, stream.SafeFileHandle.DangerousGetHandle(), out catalogPath);
				if (trustStatus != 0)
				{
					throw new InvalidDataException(
						"A assinatura local do comando oficial do Windows não pôde ser confirmada (0x" +
						trustStatus.ToString("X8") + ").");
				}
				Logger.Log("Verified protected Windows binary: " +
					(label ?? System.IO.Path.GetFileName(fullPath)) + " | source=windows-catalog");
				return new TrustedInstallerFile(fullPath, stream);
			}
			catch
			{
				stream.Dispose();
				throw;
			}
		}

		private static int VerifyCatalogAuthenticode(
			string filePath,
			IntPtr fileHandle,
			out string verifiedCatalogPath)
		{
			verifiedCatalogPath = null;
			IntPtr catalogAdmin = IntPtr.Zero;
			IntPtr catalogContext = IntPtr.Zero;
			Guid subsystem = DriverActionVerify;
			if (!CryptCATAdminAcquireContext2(out catalogAdmin, ref subsystem, null, IntPtr.Zero, 0))
			{
				return GetLastWin32Failure();
			}

			try
			{
				uint hashLength = 0;
				if (!CryptCATAdminCalcHashFromFileHandle2(
					catalogAdmin, fileHandle, ref hashLength, null, 0) ||
					hashLength == 0 || hashLength > 128)
				{
					return GetLastWin32Failure();
				}

				byte[] hash = new byte[hashLength];
				if (!CryptCATAdminCalcHashFromFileHandle2(catalogAdmin, fileHandle, ref hashLength, hash, 0))
				{
					return GetLastWin32Failure();
				}
				if (hashLength == 0 || hashLength > hash.Length)
				{
					return TrustENoSignature;
				}
				if (hashLength != hash.Length)
				{
					Array.Resize(ref hash, (int)hashLength);
				}

				bool catalogFound = false;
				int lastTrustStatus = TrustENoSignature;
				while ((catalogContext = CryptCATAdminEnumCatalogFromHash(
					catalogAdmin, hash, hashLength, 0, ref catalogContext)) != IntPtr.Zero)
				{
					catalogFound = true;
					CatalogInfo catalogInfo = new CatalogInfo();
					catalogInfo.StructSize = (uint)Marshal.SizeOf(typeof(CatalogInfo));
					if (!CryptCATCatalogInfoFromContext(catalogContext, ref catalogInfo, 0) ||
						string.IsNullOrWhiteSpace(catalogInfo.CatalogFile))
					{
						lastTrustStatus = GetLastWin32Failure();
						continue;
					}

					string candidateCatalogPath = System.IO.Path.GetFullPath(catalogInfo.CatalogFile);
					if (!IsProtectedWindowsCatalogPath(candidateCatalogPath))
					{
						lastTrustStatus = TrustEExplicitDistrust;
						continue;
					}

					lastTrustStatus = VerifyCatalogMember(
						filePath, fileHandle, hash, catalogAdmin, candidateCatalogPath);
					if (lastTrustStatus == 0)
					{
						verifiedCatalogPath = candidateCatalogPath;
						return 0;
					}
				}

				return catalogFound ? lastTrustStatus : TrustENoSignature;
			}
			finally
			{
				if (catalogContext != IntPtr.Zero)
				{
					CryptCATAdminReleaseCatalogContext(catalogAdmin, catalogContext, 0);
				}
				if (catalogAdmin != IntPtr.Zero)
				{
					CryptCATAdminReleaseContext(catalogAdmin, 0);
				}
			}
		}

		private static int GetLastWin32Failure()
		{
			int result = Marshal.GetHRForLastWin32Error();
			return result == 0 ? TrustENoSignature : result;
		}

		private static int VerifyCatalogMember(
			string filePath,
			IntPtr fileHandle,
			byte[] hash,
			IntPtr catalogAdmin,
			string catalogPath)
		{
			WinTrustCatalogInfo catalog = new WinTrustCatalogInfo();
			IntPtr catalogPointer = IntPtr.Zero;
			bool structureCreated = false;
			bool winTrustCalled = false;
			WinTrustData trustData = new WinTrustData();
			Guid action = WinTrustActionGenericVerifyV2;
			try
			{
				catalog.StructSize = (uint)Marshal.SizeOf(typeof(WinTrustCatalogInfo));
				catalog.CatalogFilePath = Marshal.StringToCoTaskMemUni(catalogPath);
				catalog.MemberTag = Marshal.StringToCoTaskMemUni(ToHex(hash));
				catalog.MemberFilePath = Marshal.StringToCoTaskMemUni(filePath);
				catalog.MemberFile = fileHandle;
				catalog.CalculatedFileHash = Marshal.AllocCoTaskMem(hash.Length);
				Marshal.Copy(hash, 0, catalog.CalculatedFileHash, hash.Length);
				catalog.CalculatedFileHashLength = (uint)hash.Length;
				catalog.CatalogAdmin = catalogAdmin;

				catalogPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WinTrustCatalogInfo)));
				Marshal.StructureToPtr(catalog, catalogPointer, false);
				structureCreated = true;

				trustData.StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData));
				trustData.UiChoice = WtdUiNone;
				trustData.RevocationChecks = WtdRevokeNone;
				trustData.UnionChoice = WtdChoiceCatalog;
				trustData.FileInfoPointer = catalogPointer;
				trustData.StateAction = WtdStateActionVerify;
				trustData.ProviderFlags = WtdCacheOnlyUrlRetrieval;

				winTrustCalled = true;
				int status = WinVerifyTrust(new IntPtr(-1), ref action, ref trustData);
				if (status == 0 && !IsApprovedMicrosoftWindowsSigner(trustData.StateData))
				{
					return TrustEExplicitDistrust;
				}
				return status;
			}
			finally
			{
				if (winTrustCalled)
				{
					trustData.StateAction = WtdStateActionClose;
					WinVerifyTrust(new IntPtr(-1), ref action, ref trustData);
				}
				if (catalogPointer != IntPtr.Zero)
				{
					if (structureCreated)
					{
						Marshal.DestroyStructure(catalogPointer, typeof(WinTrustCatalogInfo));
					}
					Marshal.FreeCoTaskMem(catalogPointer);
				}
				if (catalog.CatalogFilePath != IntPtr.Zero) Marshal.FreeCoTaskMem(catalog.CatalogFilePath);
				if (catalog.MemberTag != IntPtr.Zero) Marshal.FreeCoTaskMem(catalog.MemberTag);
				if (catalog.MemberFilePath != IntPtr.Zero) Marshal.FreeCoTaskMem(catalog.MemberFilePath);
				if (catalog.CalculatedFileHash != IntPtr.Zero) Marshal.FreeCoTaskMem(catalog.CalculatedFileHash);
			}
		}

		private static bool IsProtectedWindowsCatalogPath(string catalogPath)
		{
			if (string.IsNullOrWhiteSpace(catalogPath) || !File.Exists(catalogPath)) return false;
			string fullCatalogPath = System.IO.Path.GetFullPath(catalogPath);
			string windowsPath = System.IO.Path.GetFullPath(
				Environment.GetFolderPath(Environment.SpecialFolder.Windows))
				.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
			if (!fullCatalogPath.StartsWith(windowsPath, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(System.IO.Path.GetExtension(fullCatalogPath), ".cat", StringComparison.OrdinalIgnoreCase) ||
				(File.GetAttributes(fullCatalogPath) & FileAttributes.ReparsePoint) != 0)
			{
				return false;
			}
			return true;
		}

		private static bool IsApprovedMicrosoftWindowsSigner(IntPtr stateData)
		{
			if (stateData == IntPtr.Zero) return false;
			IntPtr providerData = WTHelperProvDataFromStateData(stateData);
			if (providerData == IntPtr.Zero) return false;
			IntPtr providerSigner = WTHelperGetProvSignerFromChain(providerData, 0, 0, 0);
			if (providerSigner == IntPtr.Zero) return false;

			// WinVerifyTrust already built this exact catalog signer's chain. Require
			// that same chain to terminate at a Microsoft root before accepting its
			// leaf identity; a locally trusted lookalike certificate is not enough.
			CryptProviderSignerHeader signerHeader =
				(CryptProviderSignerHeader)Marshal.PtrToStructure(
					providerSigner, typeof(CryptProviderSignerHeader));
			if (signerHeader.ChainContext == IntPtr.Zero ||
				!IsMicrosoftRootChain(signerHeader.ChainContext))
			{
				return false;
			}

			IntPtr providerCertificate = WTHelperGetProvCertFromChain(providerSigner, 0);
			if (providerCertificate == IntPtr.Zero) return false;

			CryptProviderCertificateHeader certificateHeader =
				(CryptProviderCertificateHeader)Marshal.PtrToStructure(
					providerCertificate, typeof(CryptProviderCertificateHeader));
			if (certificateHeader.CertificateContext == IntPtr.Zero) return false;
			return string.Equals(
				GetCertificateOrganization(certificateHeader.CertificateContext),
				"Microsoft Corporation",
				StringComparison.Ordinal);
		}

		private static bool IsMicrosoftRootChain(IntPtr chainContext)
		{
			CertChainPolicyParameters parameters = new CertChainPolicyParameters();
			parameters.StructSize = (uint)Marshal.SizeOf(typeof(CertChainPolicyParameters));
			CertChainPolicyStatus status = new CertChainPolicyStatus();
			status.StructSize = (uint)Marshal.SizeOf(typeof(CertChainPolicyStatus));
			return CertVerifyCertificateChainPolicy(
				new IntPtr(CertChainPolicyMicrosoftRoot),
				chainContext,
				ref parameters,
				ref status) && status.Error == 0;
		}

		private static string GetCertificateOrganization(IntPtr certificateContext)
		{
			const uint CertNameAttributeType = 3;
			IntPtr organizationOid = IntPtr.Zero;
			try
			{
				organizationOid = Marshal.StringToHGlobalAnsi("2.5.4.10");
				uint required = CertGetNameStringW(
					certificateContext, CertNameAttributeType, 0, organizationOid, null, 0);
				if (required <= 1 || required > 256) return string.Empty;
				StringBuilder value = new StringBuilder((int)required);
				uint written = CertGetNameStringW(
					certificateContext, CertNameAttributeType, 0, organizationOid, value, required);
				return written == required ? value.ToString() : string.Empty;
			}
			finally
			{
				if (organizationOid != IntPtr.Zero) Marshal.FreeHGlobal(organizationOid);
			}
		}

#if CONSUMER_UI_TESTS
		internal static int VerifyCatalogAuthenticodeForTest(string filePath)
		{
			using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				string catalogPath;
				return VerifyCatalogAuthenticode(
					System.IO.Path.GetFullPath(filePath), stream.SafeFileHandle.DangerousGetHandle(), out catalogPath);
			}
		}

		internal static int[] GetWinTrustLayoutForTest()
		{
			return new[]
			{
				Marshal.SizeOf(typeof(WinTrustData)),
				Marshal.SizeOf(typeof(WinTrustFileInfo)),
				Marshal.SizeOf(typeof(WinTrustCatalogInfo)),
				Marshal.SizeOf(typeof(CryptProviderCertificateHeader)),
				(int)Marshal.OffsetOf(typeof(WinTrustData), "StateData"),
				(int)Marshal.OffsetOf(typeof(CryptProviderCertificateHeader), "CertificateContext"),
				Marshal.SizeOf(typeof(CryptProviderSignerHeader)),
				(int)Marshal.OffsetOf(typeof(CryptProviderSignerHeader), "ChainContext"),
				Marshal.SizeOf(typeof(CertChainPolicyParameters)),
				Marshal.SizeOf(typeof(CertChainPolicyStatus))
			};
		}
#endif

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
					ValidatePinnedCertificateInputs(
						expectedSubject, expectedThumbprint, expectedPublicKeySha256, label);
					bool signerAnchorRejected;
					int trustStatus = VerifyAuthenticode(
						fullPath, stream.SafeFileHandle.DangerousGetHandle(), true, false,
						expectedSubject, expectedThumbprint, expectedPublicKeySha256,
						out signerAnchorRejected);
					if (signerAnchorRejected)
					{
						throw new InvalidDataException(
							"O editor/âncora efetivamente aprovado pelo Windows não corresponde ao catálogo para " + label + ".");
					}
					bool offlineFallback = false;
					if (trustStatus == CryptERevocationOffline || trustStatus == CertERevocationFailure)
					{
						if (!allowRevocationFallback || string.IsNullOrWhiteSpace(expectedSha256) ||
							string.IsNullOrWhiteSpace(expectedSubject) || string.IsNullOrWhiteSpace(expectedThumbprint) ||
							string.IsNullOrWhiteSpace(expectedPublicKeySha256))
						{
							throw new InvalidDataException("A revogação online não pôde ser consultada para " + label + ".");
						}
						trustStatus = VerifyAuthenticode(
							fullPath, stream.SafeFileHandle.DangerousGetHandle(), false, true,
							expectedSubject, expectedThumbprint, expectedPublicKeySha256,
							out signerAnchorRejected);
						if (signerAnchorRejected)
						{
							throw new InvalidDataException(
								"O editor/âncora efetivamente aprovado pelo Windows não corresponde ao catálogo para " + label + ".");
						}
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

		private static void ValidatePinnedCertificateInputs(
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
		}

		private static bool IsPinnedSigner(
			IntPtr stateData,
			string expectedSubject,
			string expectedThumbprint,
			string expectedPublicKeySha256)
		{
			if (stateData == IntPtr.Zero) return false;
			IntPtr providerData = WTHelperProvDataFromStateData(stateData);
			if (providerData == IntPtr.Zero) return false;
			IntPtr providerSigner = WTHelperGetProvSignerFromChain(providerData, 0, 0, 0);
			if (providerSigner == IntPtr.Zero) return false;
			IntPtr providerCertificate = WTHelperGetProvCertFromChain(providerSigner, 0);
			if (providerCertificate == IntPtr.Zero) return false;
			CryptProviderCertificateHeader certificateHeader =
				(CryptProviderCertificateHeader)Marshal.PtrToStructure(
					providerCertificate, typeof(CryptProviderCertificateHeader));
			if (certificateHeader.CertificateContext == IntPtr.Zero) return false;

			using (X509Certificate2 certificate =
				new X509Certificate2(certificateHeader.CertificateContext))
			{
				string publicKeyHash;
				using (SHA256 sha256 = SHA256.Create())
				{
					publicKeyHash = ToHex(sha256.ComputeHash(certificate.GetPublicKey()));
				}
				return string.Equals(certificate.Subject ?? string.Empty, expectedSubject, StringComparison.Ordinal) &&
					FixedTimeHexEquals(expectedThumbprint, NormalizeHex(certificate.Thumbprint)) &&
					FixedTimeHexEquals(expectedPublicKeySha256, publicKeyHash);
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

		private static int VerifyAuthenticode(
			string filePath,
			IntPtr fileHandle,
			bool onlineRevocation,
			bool cacheOnly,
			string expectedSubject,
			string expectedThumbprint,
			string expectedPublicKeySha256,
			out bool signerAnchorRejected)
		{
			signerAnchorRejected = false;
			WinTrustFileInfo fileInfo = new WinTrustFileInfo();
			fileInfo.StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
			fileInfo.FilePath = Marshal.StringToCoTaskMemUni(System.IO.Path.GetFullPath(filePath));
			fileInfo.FileHandle = fileHandle;

			IntPtr fileInfoPointer = IntPtr.Zero;
			bool winTrustCalled = false;
			WinTrustData trustData = new WinTrustData();
			Guid action = WinTrustActionGenericVerifyV2;
			try
			{
				fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WinTrustFileInfo)));
				Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

				trustData.StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData));
				trustData.UiChoice = WtdUiNone;
				trustData.RevocationChecks = onlineRevocation ? WtdRevokeWholeChain : WtdRevokeNone;
				trustData.UnionChoice = WtdChoiceFile;
				trustData.FileInfoPointer = fileInfoPointer;
				trustData.StateAction = WtdStateActionVerify;
				trustData.ProviderFlags = (onlineRevocation ? WtdRevocationCheckChain : 0U) |
					(cacheOnly ? WtdCacheOnlyUrlRetrieval : 0U);

				winTrustCalled = true;
				int trustStatus = WinVerifyTrust(new IntPtr(-1), ref action, ref trustData);
				if (trustStatus == 0 && !IsPinnedSigner(
					trustData.StateData,
					expectedSubject,
					expectedThumbprint,
					expectedPublicKeySha256))
				{
					signerAnchorRejected = true;
					return TrustEExplicitDistrust;
				}
				return trustStatus;
			}
			finally
			{
				if (winTrustCalled)
				{
					trustData.StateAction = WtdStateActionClose;
					WinVerifyTrust(new IntPtr(-1), ref action, ref trustData);
				}
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

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
		private struct WinTrustFileInfo
		{
			public uint StructSize;
			public IntPtr FilePath;
			public IntPtr FileHandle;
			public IntPtr KnownSubject;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
		private struct WinTrustCatalogInfo
		{
			public uint StructSize;
			public uint CatalogVersion;
			public IntPtr CatalogFilePath;
			public IntPtr MemberTag;
			public IntPtr MemberFilePath;
			public IntPtr MemberFile;
			public IntPtr CalculatedFileHash;
			public uint CalculatedFileHashLength;
			public IntPtr CatalogContext;
			public IntPtr CatalogAdmin;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
		private struct CatalogInfo
		{
			public uint StructSize;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
			public string CatalogFile;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
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
			public IntPtr SignatureSettings;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 8)]
		private struct CryptProviderCertificateHeader
		{
			public uint StructSize;
			public IntPtr CertificateContext;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 8)]
		private struct CryptProviderSignerHeader
		{
			public uint StructSize;
			public uint VerifyAsOfLowDateTime;
			public uint VerifyAsOfHighDateTime;
			public uint CertificateChainCount;
			public IntPtr CertificateChain;
			public uint SignerType;
			public IntPtr SignerInfo;
			public uint Error;
			public uint CounterSignerCount;
			public IntPtr CounterSigners;
			public IntPtr ChainContext;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 8)]
		private struct CertChainPolicyParameters
		{
			public uint StructSize;
			public uint Flags;
			public IntPtr ExtraPolicyParameters;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 8)]
		private struct CertChainPolicyStatus
		{
			public uint StructSize;
			public uint Error;
			public int ChainIndex;
			public int ElementIndex;
			public IntPtr ExtraPolicyStatus;
		}

		[DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern int WinVerifyTrust(IntPtr windowHandle, ref Guid actionId, ref WinTrustData trustData);

		[DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool CryptCATAdminAcquireContext2(
			out IntPtr catalogAdmin,
			ref Guid subsystem,
			string hashAlgorithm,
			IntPtr strongHashPolicy,
			uint flags);

		[DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool CryptCATAdminCalcHashFromFileHandle2(
			IntPtr catalogAdmin,
			IntPtr fileHandle,
			ref uint hashLength,
			[Out] byte[] hash,
			uint flags);

		[DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
		private static extern IntPtr CryptCATAdminEnumCatalogFromHash(
			IntPtr catalogAdmin,
			byte[] hash,
			uint hashLength,
			uint flags,
			ref IntPtr previousCatalogInfo);

		[DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
		private static extern IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

		[DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
		private static extern IntPtr WTHelperGetProvSignerFromChain(
			IntPtr providerData,
			uint signerIndex,
			int counterSigner,
			uint counterSignerIndex);

		[DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
		private static extern IntPtr WTHelperGetProvCertFromChain(
			IntPtr providerSigner,
			uint certificateIndex);

		[DllImport("crypt32.dll", EntryPoint = "CertGetNameStringW", ExactSpelling = true,
			CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern uint CertGetNameStringW(
			IntPtr certificateContext,
			uint nameType,
			uint flags,
			IntPtr typeParameter,
			StringBuilder name,
			uint nameLength);

		[DllImport("crypt32.dll", ExactSpelling = true, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool CertVerifyCertificateChainPolicy(
			IntPtr policyOid,
			IntPtr chainContext,
			ref CertChainPolicyParameters policyParameters,
			ref CertChainPolicyStatus policyStatus);

		[DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool CryptCATCatalogInfoFromContext(
			IntPtr catalogInfo,
			ref CatalogInfo catalog,
			uint flags);

		[DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool CryptCATAdminReleaseCatalogContext(
			IntPtr catalogAdmin,
			IntPtr catalogInfo,
			uint flags);

		[DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool CryptCATAdminReleaseContext(IntPtr catalogAdmin, uint flags);
	}
}
