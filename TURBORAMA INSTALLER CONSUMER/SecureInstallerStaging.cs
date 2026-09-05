using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace InstallerHost
{
	/// <summary>
	/// Staging elevado com descritor de segurança exato. A criação de diretórios e
	/// arquivos usa SECURITY_ATTRIBUTES no mesmo syscall que cria o objeto, evitando
	/// uma janela com owner/DACL herdados do usuário. Objetos preexistentes nunca são
	/// "consertados": precisam satisfazer integralmente a política ou o fluxo para.
	/// </summary>
	internal sealed class SecureInstallerStaging : IDisposable
	{
		private const string DirectorySddl =
			"O:SYG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)S:P(ML;OICI;NW;;;HI)";
		private const string DirectorySddlWithoutIntegrity =
			"O:SYG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";
		private const string FileSddl =
			"O:SYG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)S:P(ML;;NW;;;HI)";
		private const string FileSddlWithoutIntegrity =
			"O:SYG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)";

		private const uint TokenAdjustPrivileges = 0x0020;
		private const uint TokenQuery = 0x0008;
		private const uint SePrivilegeEnabled = 0x00000002;
		private const int ErrorNotAllAssigned = 1300;
		private const int ErrorAlreadyExists = 183;
		private const int ErrorFileExists = 80;
		private const uint GenericWrite = 0x40000000;
		private const uint CreateNew = 1;
		private const uint FileAttributeNormal = 0x00000080;
		private const uint OwnerSecurityInformation = 0x00000001;
		private const uint GroupSecurityInformation = 0x00000002;
		private const uint DaclSecurityInformation = 0x00000004;
		private const uint SaclSecurityInformation = 0x00000008;
		private const uint LabelSecurityInformation = 0x00000010;
		private const uint ProtectedDaclSecurityInformation = 0x80000000;
		private const uint ProtectedSaclSecurityInformation = 0x40000000;
		private const int MandatoryLabelAceType = 0x11;
		private const int MandatoryNoWriteUp = 0x00000001;

		private static readonly SecurityIdentifier LocalSystemSid =
			new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
		private static readonly SecurityIdentifier AdministratorsSid =
			new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
		private static readonly SecurityIdentifier HighIntegritySid =
			new SecurityIdentifier("S-1-16-12288");
		private static readonly bool MandatoryIntegritySupported =
			Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Major >= 6;
		private static readonly byte[] DirectorySecurityDescriptor = BuildSecurityDescriptor(true);
		private static readonly byte[] FileSecurityDescriptor = BuildSecurityDescriptor(false);

		private bool disposed;

		private SecureInstallerStaging(string path)
		{
			Path = System.IO.Path.GetFullPath(path);
		}

		public string Path { get; private set; }

		public static SecureInstallerStaging Create(string purpose)
		{
			EnsureElevatedAdministrator();
			using (TokenPrivilegeScope restore = TokenPrivilegeScope.Enable("SeRestorePrivilege"))
			using (TokenPrivilegeScope security = MandatoryIntegritySupported
				? TokenPrivilegeScope.Enable("SeSecurityPrivilege")
				: null)
			{
				string commonData = System.IO.Path.GetFullPath(
					Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
				VerifyExistingAncestorsHaveNoReparsePoints(commonData);

				// Legacy releases used an Admin-owned shared folder. Never depend on
				// or change that folder: reserve a new private root atomically per run.
				string safePurpose = MakeSafeName(purpose);
				for (int attempt = 0; attempt < 10; attempt++)
				{
					string candidate = System.IO.Path.Combine(
						commonData, "TurboramaStaging_" + safePurpose + "_" + Guid.NewGuid().ToString("N"));
					VerifyExistingAncestorsHaveNoReparsePoints(System.IO.Path.GetDirectoryName(candidate));
					int error;
					if (!TryCreateDirectoryAtomic(candidate, DirectorySecurityDescriptor, out error))
					{
						if (error == ErrorAlreadyExists || error == ErrorFileExists)
						{
							continue;
						}
						throw NewWin32Exception(error, "Não foi possível criar o staging privado.");
					}

					VerifyDirectoryPolicy(candidate);
					return new SecureInstallerStaging(candidate);
				}
			}

			throw new IOException("Não foi possível reservar um nome privado para o staging.");
		}

		public string CreateSubdirectory(string name)
		{
			ThrowIfDisposed();
			string childPath = System.IO.Path.Combine(Path, MakeSafeName(name));
			EnsurePathInsideRoot(childPath);

			using (TokenPrivilegeScope restore = TokenPrivilegeScope.Enable("SeRestorePrivilege"))
			using (TokenPrivilegeScope security = MandatoryIntegritySupported
				? TokenPrivilegeScope.Enable("SeSecurityPrivilege")
				: null)
			{
				VerifyDirectoryPolicy(Path);
				int error;
				if (!TryCreateDirectoryAtomic(childPath, DirectorySecurityDescriptor, out error))
				{
					if (error == ErrorAlreadyExists || error == ErrorFileExists)
					{
						throw new IOException("O staging já contém o subdiretório solicitado: " + name + ".");
					}
					throw NewWin32Exception(error, "Não foi possível criar subdiretório privado de staging.");
				}
				VerifyDirectoryPolicy(childPath);
			}
			return childPath;
		}

		public FileStream CreateFileForWrite(string filePath)
		{
			ThrowIfDisposed();
			string fullPath = System.IO.Path.GetFullPath(filePath);
			EnsurePathInsideRoot(fullPath);
			string parent = System.IO.Path.GetDirectoryName(fullPath);

			using (TokenPrivilegeScope restore = TokenPrivilegeScope.Enable("SeRestorePrivilege"))
			using (TokenPrivilegeScope security = MandatoryIntegritySupported
				? TokenPrivilegeScope.Enable("SeSecurityPrivilege")
				: null)
			{
				VerifyDirectoryPolicy(parent);
				SafeFileHandle handle = CreateFileAtomic(fullPath, FileSecurityDescriptor);
				try
				{
					return new FileStream(handle, FileAccess.Write, 65536, false);
				}
				catch
				{
					handle.Dispose();
					throw;
				}
			}
		}

		public void HardenFile(string filePath)
		{
			ThrowIfDisposed();
			string fullPath = System.IO.Path.GetFullPath(filePath);
			EnsurePathInsideRoot(fullPath);
			RejectReparsePoint(fullPath);
			using (TokenPrivilegeScope restore = TokenPrivilegeScope.Enable("SeRestorePrivilege"))
			using (TokenPrivilegeScope security = MandatoryIntegritySupported
				? TokenPrivilegeScope.Enable("SeSecurityPrivilege")
				: null)
			{
				ApplyExactSecurityDescriptor(fullPath, FileSecurityDescriptor);
				VerifyFileSecurityPolicy(fullPath);
			}
		}

		public void VerifyFilePolicy(string filePath)
		{
			ThrowIfDisposed();
			string fullPath = System.IO.Path.GetFullPath(filePath);
			EnsurePathInsideRoot(fullPath);
			using (TokenPrivilegeScope security = MandatoryIntegritySupported
				? TokenPrivilegeScope.Enable("SeSecurityPrivilege")
				: null)
			{
				VerifySecurityPolicy(fullPath, false);
			}
		}

		public void HardenTreeContents()
		{
			ThrowIfDisposed();
			using (TokenPrivilegeScope restore = TokenPrivilegeScope.Enable("SeRestorePrivilege"))
			using (TokenPrivilegeScope security = MandatoryIntegritySupported
				? TokenPrivilegeScope.Enable("SeSecurityPrivilege")
				: null)
			{
				VerifyDirectoryPolicy(Path);
				HardenTreeContentsCore(Path);
			}
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			if (InstallerProcessQuarantine.TryDeferStagingCleanup(this))
			{
				Logger.Log("Private installer staging retained while a process remains quarantined: " + Path);
				return;
			}
			disposed = true;
			try
			{
				using (TokenPrivilegeScope restore = TokenPrivilegeScope.Enable("SeRestorePrivilege"))
				using (TokenPrivilegeScope security = MandatoryIntegritySupported
					? TokenPrivilegeScope.Enable("SeSecurityPrivilege")
					: null)
				{
					// Arquivos produzidos por um extrator filho podem nascer com owner
					// diferente. Normalize-os antes da remoção, mas pare imediatamente
					// diante de qualquer reparse point.
					VerifyDirectoryPolicy(Path);
					HardenTreeContentsCore(Path);
					DeleteTreeWithoutFollowingReparsePoints(Path);
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Could not safely remove private installer staging '" + Path + "': " + ex.Message);
			}
		}

		private static void HardenTreeContentsCore(string directoryPath)
		{
			foreach (string entry in Directory.GetFileSystemEntries(directoryPath))
			{
				RejectReparsePoint(entry);
				FileAttributes attributes = File.GetAttributes(entry);
				if ((attributes & FileAttributes.Directory) != 0)
				{
					ApplyExactSecurityDescriptor(entry, DirectorySecurityDescriptor);
					VerifyDirectoryPolicy(entry);
					HardenTreeContentsCore(entry);
				}
				else
				{
					ApplyExactSecurityDescriptor(entry, FileSecurityDescriptor);
					VerifyFileSecurityPolicy(entry);
				}
			}
		}

		private static void DeleteTreeWithoutFollowingReparsePoints(string directoryPath)
		{
			if (!Directory.Exists(directoryPath))
			{
				return;
			}
			RejectReparsePoint(directoryPath);
			VerifyDirectoryPolicy(directoryPath);

			foreach (string entry in Directory.GetFileSystemEntries(directoryPath))
			{
				FileAttributes attributes = File.GetAttributes(entry);
				if ((attributes & FileAttributes.ReparsePoint) != 0)
				{
					// Remove somente o link/reparse point; jamais enumere ou siga seu alvo.
					if ((attributes & FileAttributes.Directory) != 0)
					{
						Directory.Delete(entry, false);
					}
					else
					{
						File.Delete(entry);
					}
					continue;
				}

				if ((attributes & FileAttributes.Directory) != 0)
				{
					DeleteTreeWithoutFollowingReparsePoints(entry);
				}
				else
				{
					VerifyFileSecurityPolicy(entry);
					File.Delete(entry);
				}
			}
			Directory.Delete(directoryPath, false);
		}

		private static byte[] BuildSecurityDescriptor(bool directory)
		{
			string sddl;
			if (directory)
			{
				sddl = MandatoryIntegritySupported ? DirectorySddl : DirectorySddlWithoutIntegrity;
			}
			else
			{
				sddl = MandatoryIntegritySupported ? FileSddl : FileSddlWithoutIntegrity;
			}
			RawSecurityDescriptor descriptor = new RawSecurityDescriptor(sddl);
			byte[] binary = new byte[descriptor.BinaryLength];
			descriptor.GetBinaryForm(binary, 0);
			return binary;
		}

		private static bool TryCreateDirectoryAtomic(string path, byte[] descriptor, out int error)
		{
			GCHandle pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
			try
			{
				SecurityAttributes attributes = new SecurityAttributes
				{
					Length = Marshal.SizeOf(typeof(SecurityAttributes)),
					SecurityDescriptor = pinned.AddrOfPinnedObject(),
					InheritHandle = false
				};
				bool created = CreateDirectoryNative(path, ref attributes);
				error = created ? 0 : Marshal.GetLastWin32Error();
				return created;
			}
			finally
			{
				pinned.Free();
			}
		}

		private static SafeFileHandle CreateFileAtomic(string path, byte[] descriptor)
		{
			GCHandle pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
			try
			{
				SecurityAttributes attributes = new SecurityAttributes
				{
					Length = Marshal.SizeOf(typeof(SecurityAttributes)),
					SecurityDescriptor = pinned.AddrOfPinnedObject(),
					InheritHandle = false
				};
				SafeFileHandle handle = CreateFileNative(
					path, GenericWrite, 0, ref attributes, CreateNew, FileAttributeNormal, IntPtr.Zero);
				if (handle.IsInvalid)
				{
					int error = Marshal.GetLastWin32Error();
					handle.Dispose();
					throw NewWin32Exception(error, "Não foi possível criar payload com segurança atômica.");
				}
				return handle;
			}
			finally
			{
				pinned.Free();
			}
		}

		private static void ApplyExactSecurityDescriptor(string path, byte[] descriptor)
		{
			uint information = OwnerSecurityInformation | GroupSecurityInformation |
				DaclSecurityInformation | ProtectedDaclSecurityInformation;
			if (MandatoryIntegritySupported)
			{
				information |= SaclSecurityInformation | LabelSecurityInformation | ProtectedSaclSecurityInformation;
			}
			if (!SetFileSecurityNative(path, information, descriptor))
			{
				throw NewWin32Exception(Marshal.GetLastWin32Error(),
					"Não foi possível aplicar a política exata ao staging.");
			}
		}

		private static void VerifyDirectoryPolicy(string path)
		{
			VerifySecurityPolicy(path, true);
		}

		private static void VerifyFileSecurityPolicy(string path)
		{
			VerifySecurityPolicy(path, false);
		}

		private static void VerifySecurityPolicy(string path, bool directory)
		{
			RejectReparsePoint(path);
			RawSecurityDescriptor descriptor = ReadSecurityDescriptor(path);
			if (descriptor.Owner == null || !descriptor.Owner.Equals(LocalSystemSid))
			{
				throw new UnauthorizedAccessException("Owner inseguro no staging (exigido LocalSystem): " + path + ".");
			}
			if (descriptor.Group == null || !descriptor.Group.Equals(AdministratorsSid))
			{
				throw new UnauthorizedAccessException("Primary group inseguro no staging (exigido Administradores): " + path + ".");
			}
			if ((descriptor.ControlFlags & ControlFlags.DiscretionaryAclProtected) == 0 ||
				descriptor.DiscretionaryAcl == null || descriptor.DiscretionaryAcl.Count != 2)
			{
				throw new UnauthorizedAccessException("DACL do staging não é protegida/exata: " + path + ".");
			}

			AceFlags requiredFlags = directory
				? AceFlags.ObjectInherit | AceFlags.ContainerInherit
				: AceFlags.None;
			bool systemFound = false;
			bool administratorsFound = false;
			foreach (GenericAce genericAce in descriptor.DiscretionaryAcl)
			{
				QualifiedAce ace = genericAce as QualifiedAce;
				if (ace == null || ace.AceQualifier != AceQualifier.AccessAllowed ||
					ace.IsInherited || ace.AccessMask != (int)FileSystemRights.FullControl ||
					(ace.AceFlags & (AceFlags.ObjectInherit | AceFlags.ContainerInherit |
						AceFlags.NoPropagateInherit | AceFlags.InheritOnly)) != requiredFlags)
				{
					throw new UnauthorizedAccessException("ACE externa ou permissões divergentes no staging: " + path + ".");
				}
				if (ace.SecurityIdentifier.Equals(LocalSystemSid))
				{
					systemFound = true;
				}
				else if (ace.SecurityIdentifier.Equals(AdministratorsSid))
				{
					administratorsFound = true;
				}
				else
				{
					throw new UnauthorizedAccessException("Principal externo na DACL do staging: " + path + ".");
				}
			}
			if (!systemFound || !administratorsFound)
			{
				throw new UnauthorizedAccessException("DACL sem SYSTEM/Administradores no staging: " + path + ".");
			}

			if (MandatoryIntegritySupported)
			{
				VerifyMandatoryIntegrityLabel(descriptor, directory, path);
			}
		}

		private static void VerifyMandatoryIntegrityLabel(
			RawSecurityDescriptor descriptor,
			bool directory,
			string path)
		{
			if ((descriptor.ControlFlags & ControlFlags.SystemAclPresent) == 0 ||
				(descriptor.ControlFlags & ControlFlags.SystemAclProtected) == 0 ||
				descriptor.SystemAcl == null || descriptor.SystemAcl.Count != 1)
			{
				throw new UnauthorizedAccessException("Mandatory Integrity Label ausente ou não protegida: " + path + ".");
			}

			GenericAce ace = descriptor.SystemAcl[0];
			if ((int)ace.AceType != MandatoryLabelAceType)
			{
				throw new UnauthorizedAccessException("SACL contém ACE não permitida no staging: " + path + ".");
			}
			AceFlags requiredFlags = directory
				? AceFlags.ObjectInherit | AceFlags.ContainerInherit
				: AceFlags.None;
			if ((ace.AceFlags & (AceFlags.ObjectInherit | AceFlags.ContainerInherit |
				AceFlags.NoPropagateInherit | AceFlags.InheritOnly)) != requiredFlags)
			{
				throw new UnauthorizedAccessException("Herança do Mandatory Integrity Label divergente: " + path + ".");
			}

			byte[] binary = new byte[ace.BinaryLength];
			ace.GetBinaryForm(binary, 0);
			if (binary.Length < 16 || BitConverter.ToInt32(binary, 4) != MandatoryNoWriteUp)
			{
				throw new UnauthorizedAccessException("NoWriteUp ausente no Mandatory Integrity Label: " + path + ".");
			}
			SecurityIdentifier label = new SecurityIdentifier(binary, 8);
			if (!label.Equals(HighIntegritySid))
			{
				throw new UnauthorizedAccessException("Nível de integridade do staging não é High: " + path + ".");
			}
		}

		private static RawSecurityDescriptor ReadSecurityDescriptor(string path)
		{
			uint information = OwnerSecurityInformation | GroupSecurityInformation | DaclSecurityInformation;
			if (MandatoryIntegritySupported)
			{
				information |= SaclSecurityInformation | LabelSecurityInformation;
			}
			uint required;
			GetFileSecurityNative(path, information, null, 0, out required);
			int firstError = Marshal.GetLastWin32Error();
			if (required == 0)
			{
				throw NewWin32Exception(firstError, "Não foi possível dimensionar o descritor de segurança.");
			}
			byte[] binary = new byte[required];
			if (!GetFileSecurityNative(path, information, binary, (uint)binary.Length, out required))
			{
				throw NewWin32Exception(Marshal.GetLastWin32Error(), "Não foi possível ler a segurança do staging.");
			}
			return new RawSecurityDescriptor(binary, 0);
		}

		private static void VerifyExistingAncestorsHaveNoReparsePoints(string path)
		{
			string fullPath = System.IO.Path.GetFullPath(path);
			string root = System.IO.Path.GetPathRoot(fullPath);
			if (string.IsNullOrWhiteSpace(root))
			{
				throw new IOException("Caminho de staging sem raiz absoluta: " + fullPath + ".");
			}

			string current = root.TrimEnd(System.IO.Path.DirectorySeparatorChar);
			if (current.Length == 2 && current[1] == ':')
			{
				current += System.IO.Path.DirectorySeparatorChar;
			}
			RejectReparsePoint(current);
			string relative = fullPath.Substring(root.Length);
			foreach (string part in relative.Split(new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar },
				StringSplitOptions.RemoveEmptyEntries))
			{
				current = System.IO.Path.Combine(current, part);
				if (!Directory.Exists(current))
				{
					break;
				}
				RejectReparsePoint(current);
			}
		}

		private static void RejectReparsePoint(string path)
		{
			FileAttributes attributes;
			try
			{
				attributes = File.GetAttributes(path);
			}
			catch (Exception ex)
			{
				throw new IOException("Não foi possível inspecionar o caminho seguro: " + path + ".", ex);
			}
			if ((attributes & FileAttributes.ReparsePoint) != 0)
			{
				throw new IOException("Reparse point recusado no staging: " + path + ".");
			}
		}

		private void EnsurePathInsideRoot(string path)
		{
			string root = Path.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
			string fullPath = System.IO.Path.GetFullPath(path);
			if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException("Caminho fora do staging privado: " + fullPath + ".");
			}
			VerifyExistingAncestorsHaveNoReparsePoints(System.IO.Path.GetDirectoryName(fullPath));
		}

		private static string MakeSafeName(string value)
		{
			string safe = string.IsNullOrWhiteSpace(value) ? "Payload" : value.Trim();
			foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
			{
				safe = safe.Replace(invalid, '_');
			}
			if (safe == "." || safe == "..")
			{
				safe = "Payload";
			}
			return safe;
		}

		private static void EnsureElevatedAdministrator()
		{
			using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
			{
				WindowsPrincipal principal = new WindowsPrincipal(identity);
				if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
				{
					throw new UnauthorizedAccessException("O staging seguro exige processo elevado como Administrador.");
				}
			}
		}

		private static Win32Exception NewWin32Exception(int error, string message)
		{
			return new Win32Exception(error, message + " Win32=" + error + ".");
		}

		private void ThrowIfDisposed()
		{
			if (disposed)
			{
				throw new ObjectDisposedException("SecureInstallerStaging");
			}
		}

		private sealed class TokenPrivilegeScope : IDisposable
		{
			private IntPtr token;
			private TokenPrivileges previousState;
			private bool restorePrevious;

			private TokenPrivilegeScope(IntPtr tokenHandle, TokenPrivileges previous)
			{
				token = tokenHandle;
				previousState = previous;
				restorePrevious = true;
			}

			public static TokenPrivilegeScope Enable(string privilegeName)
			{
				IntPtr tokenHandle;
				if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out tokenHandle))
				{
					throw NewWin32Exception(Marshal.GetLastWin32Error(), "Não foi possível abrir o token do processo.");
				}

				try
				{
					Luid luid;
					if (!LookupPrivilegeValue(null, privilegeName, out luid))
					{
						throw NewWin32Exception(Marshal.GetLastWin32Error(), "Privilégio Windows desconhecido: " + privilegeName + ".");
					}
					TokenPrivileges requested = new TokenPrivileges
					{
						PrivilegeCount = 1,
						Privileges = new LuidAndAttributes { Luid = luid, Attributes = SePrivilegeEnabled }
					};
					TokenPrivileges previous;
					int returnedLength;
					if (!AdjustTokenPrivileges(tokenHandle, false, ref requested,
						Marshal.SizeOf(typeof(TokenPrivileges)), out previous, out returnedLength))
					{
						throw NewWin32Exception(Marshal.GetLastWin32Error(), "Não foi possível habilitar " + privilegeName + ".");
					}
					int adjustmentError = Marshal.GetLastWin32Error();
					if (adjustmentError == ErrorNotAllAssigned)
					{
						throw new UnauthorizedAccessException("O token elevado não possui " + privilegeName + ".");
					}
					if (adjustmentError != 0)
					{
						throw NewWin32Exception(adjustmentError, "Não foi possível habilitar " + privilegeName + ".");
					}
					return new TokenPrivilegeScope(tokenHandle, previous);
				}
				catch
				{
					CloseHandle(tokenHandle);
					throw;
				}
			}

			public void Dispose()
			{
				if (token == IntPtr.Zero)
				{
					return;
				}
				try
				{
					if (restorePrevious)
					{
						TokenPrivileges ignored;
						int ignoredLength;
						AdjustTokenPrivileges(token, false, ref previousState,
							Marshal.SizeOf(typeof(TokenPrivileges)), out ignored, out ignoredLength);
					}
				}
				finally
				{
					CloseHandle(token);
					token = IntPtr.Zero;
					restorePrevious = false;
				}
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct SecurityAttributes
		{
			public int Length;
			public IntPtr SecurityDescriptor;
			[MarshalAs(UnmanagedType.Bool)]
			public bool InheritHandle;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct Luid
		{
			public uint LowPart;
			public int HighPart;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct LuidAndAttributes
		{
			public Luid Luid;
			public uint Attributes;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct TokenPrivileges
		{
			public uint PrivilegeCount;
			public LuidAndAttributes Privileges;
		}

		[DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool CreateDirectoryNative(string path, ref SecurityAttributes securityAttributes);

		[DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern SafeFileHandle CreateFileNative(
			string fileName,
			uint desiredAccess,
			uint shareMode,
			ref SecurityAttributes securityAttributes,
			uint creationDisposition,
			uint flagsAndAttributes,
			IntPtr templateFile);

		[DllImport("advapi32.dll", EntryPoint = "SetFileSecurityW", CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetFileSecurityNative(string fileName, uint securityInformation, byte[] securityDescriptor);

		[DllImport("advapi32.dll", EntryPoint = "GetFileSecurityW", CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetFileSecurityNative(
			string fileName,
			uint requestedInformation,
			byte[] securityDescriptor,
			uint length,
			out uint lengthNeeded);

		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

		[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool LookupPrivilegeValue(string systemName, string name, out Luid luid);

		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool AdjustTokenPrivileges(
			IntPtr tokenHandle,
			[MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
			ref TokenPrivileges newState,
			int bufferLength,
			out TokenPrivileges previousState,
			out int returnLength);

		[DllImport("kernel32.dll")]
		private static extern IntPtr GetCurrentProcess();

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool CloseHandle(IntPtr handle);
	}
}
