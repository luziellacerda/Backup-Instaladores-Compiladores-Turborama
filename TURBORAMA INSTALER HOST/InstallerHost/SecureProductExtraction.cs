using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace InstallerHost
{
	/// <summary>
	/// Extracts the already authenticated logical product ZIP without following
	/// reparse points or overwriting any pre-existing file.
	/// </summary>
	internal static class SecureProductExtractor
	{
		private const int MaxEntryCount = 250000;
		private const long MaxTotalUncompressedBytes = 2L * 1024L * 1024L * 1024L * 1024L;
		private const long MaxSingleEntryBytes = 256L * 1024L * 1024L * 1024L;
		private const long CompressionRatioCheckFloor = 16L * 1024L * 1024L;
		private const long MaxCompressionRatio = 1000L;
		private const int CopyBufferBytes = 1024 * 1024;

		internal static void Extract(
			Stream packageStream,
			SecureExtractionGuard destination,
			Action<int> reportProgress)
		{
			if (packageStream == null)
			{
				throw new ArgumentNullException("packageStream");
			}
			if (destination == null)
			{
				throw new ArgumentNullException("destination");
			}

			using (ZipArchive archive = new ZipArchive(packageStream, ZipArchiveMode.Read, true))
			{
				List<PlannedEntry> plan = BuildPlan(archive, destination.RootPath);
				long totalBytes = plan.Where(item => !item.IsDirectory).Sum(item => item.Length);
				long copiedBytes = 0L;
				int lastProgress = -1;
				byte[] buffer = new byte[CopyBufferBytes];

				foreach (PlannedEntry item in plan)
				{
					if (item.IsDirectory)
					{
						destination.EnsureDirectory(item.OutputPath);
						continue;
					}

					string parent = Path.GetDirectoryName(item.OutputPath);
					destination.EnsureDirectory(parent);
					destination.ValidateDirectoryForWrite(parent);

					using (Stream input = item.Entry.Open())
					using (FileStream output = destination.CreateNewFile(item.OutputPath))
					{
						long entryCopied = 0L;
						int read;
						while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
						{
							entryCopied = checked(entryCopied + read);
							copiedBytes = checked(copiedBytes + read);
							if (entryCopied > item.Length || copiedBytes > totalBytes)
							{
								throw new InvalidDataException("ZIP entry expanded beyond its declared length: " + item.Entry.FullName);
							}

							output.Write(buffer, 0, read);
							destination.ValidateDirectoryForWrite(parent);

							if (reportProgress != null && totalBytes > 0L)
							{
								int progress = (int)Math.Min(100L, copiedBytes * 100L / totalBytes);
								if (progress != lastProgress)
								{
									reportProgress(progress);
									lastProgress = progress;
								}
							}
						}

						if (entryCopied != item.Length)
						{
							throw new InvalidDataException("ZIP entry length mismatch: " + item.Entry.FullName);
						}
						output.Flush(true);
					}
				}

				if (copiedBytes != totalBytes)
				{
					throw new InvalidDataException("Logical ZIP extraction length mismatch.");
				}
				destination.ValidateHeldDirectories();
				if (reportProgress != null && lastProgress != 100)
				{
					reportProgress(100);
				}
			}
		}

		private static List<PlannedEntry> BuildPlan(ZipArchive archive, string rootPath)
		{
			if (archive.Entries.Count > MaxEntryCount)
			{
				throw new InvalidDataException("Product ZIP exceeds the entry-count limit.");
			}

			List<PlannedEntry> plan = new List<PlannedEntry>(archive.Entries.Count);
			Dictionary<string, bool> pathKinds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			long totalBytes = 0L;

			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				bool isDirectory = string.IsNullOrEmpty(entry.Name);
				RejectLinkOrReparseEntry(entry);
				if (entry.Length < 0L || entry.CompressedLength < 0L)
				{
					throw new InvalidDataException("ZIP entry has an invalid length: " + entry.FullName);
				}
				if (isDirectory && entry.Length != 0L)
				{
					throw new InvalidDataException("ZIP directory entry contains data: " + entry.FullName);
				}
				if (entry.Length > MaxSingleEntryBytes)
				{
					throw new InvalidDataException("ZIP entry exceeds the per-file extraction limit: " + entry.FullName);
				}
				if (entry.Length >= CompressionRatioCheckFloor &&
					(entry.CompressedLength == 0L || entry.Length / entry.CompressedLength > MaxCompressionRatio))
				{
					throw new InvalidDataException("ZIP entry has an unsafe compression ratio: " + entry.FullName);
				}

				totalBytes = checked(totalBytes + entry.Length);
				if (totalBytes > MaxTotalUncompressedBytes)
				{
					throw new InvalidDataException("Product ZIP exceeds the total extraction-size limit.");
				}

				string outputPath = GetSafeOutputPath(rootPath, entry.FullName, isDirectory);
				bool previousKind;
				if (pathKinds.TryGetValue(outputPath, out previousKind))
				{
					throw new InvalidDataException("ZIP contains a duplicate output path: " + entry.FullName);
				}
				pathKinds.Add(outputPath, isDirectory);
				plan.Add(new PlannedEntry(entry, outputPath, isDirectory));
			}

			HashSet<string> files = new HashSet<string>(
				plan.Where(item => !item.IsDirectory).Select(item => item.OutputPath),
				StringComparer.OrdinalIgnoreCase);
			foreach (PlannedEntry item in plan)
			{
				string parent = Path.GetDirectoryName(item.OutputPath);
				while (!string.IsNullOrEmpty(parent) &&
					parent.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				{
					if (files.Contains(parent))
					{
						throw new InvalidDataException("ZIP maps a file and child entry to the same path tree: " + item.Entry.FullName);
					}
					parent = Path.GetDirectoryName(parent);
				}
			}

			return plan;
		}

		private static string GetSafeOutputPath(string rootPath, string entryName, bool isDirectory)
		{
			if (string.IsNullOrEmpty(entryName) || entryName.Length > 32760 ||
				entryName[0] == '/' || entryName[0] == '\\' || Path.IsPathRooted(entryName))
			{
				throw new InvalidDataException("Unsafe ZIP entry path: " + entryName);
			}

			string normalized = entryName.Replace('\\', '/');
			if (isDirectory)
			{
				normalized = normalized.TrimEnd('/');
			}
			string[] segments = normalized.Split('/');
			if (segments.Length == 0)
			{
				throw new InvalidDataException("Unsafe empty ZIP entry path.");
			}

			foreach (string segment in segments)
			{
				ValidatePathSegment(segment, entryName);
			}

			string relativePath = string.Join(Path.DirectorySeparatorChar.ToString(), segments);
			string outputPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
			string rootPrefix = rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (!outputPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("ZIP entry escapes the destination root: " + entryName);
			}
			return outputPath;
		}

		private static void ValidatePathSegment(string segment, string originalEntry)
		{
			if (string.IsNullOrEmpty(segment) || segment == "." || segment == ".." ||
				segment.EndsWith(" ", StringComparison.Ordinal) ||
				segment.EndsWith(".", StringComparison.Ordinal) ||
				segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
			{
				throw new InvalidDataException("Unsafe ZIP entry path segment: " + originalEntry);
			}

			string deviceStem = segment;
			int dot = deviceStem.IndexOf('.');
			if (dot >= 0)
			{
				deviceStem = deviceStem.Substring(0, dot);
			}
			string upper = deviceStem.ToUpperInvariant();
			if (upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL" ||
				(upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) ||
				 upper.StartsWith("LPT", StringComparison.Ordinal)) && upper[3] >= '1' && upper[3] <= '9'))
			{
				throw new InvalidDataException("ZIP entry uses a reserved Windows device name: " + originalEntry);
			}
		}

		private static void RejectLinkOrReparseEntry(ZipArchiveEntry entry)
		{
			int attributes = entry.ExternalAttributes;
			int unixFileType = (attributes >> 16) & 0xF000;
			bool unixSymlink = unixFileType == 0xA000;
			bool windowsReparse = (attributes & 0x0400) != 0 || ((attributes >> 16) & 0x0400) != 0;
			if (unixSymlink || windowsReparse)
			{
				throw new InvalidDataException("Links/reparse entries are not accepted in the product ZIP: " + entry.FullName);
			}
		}

		private sealed class PlannedEntry
		{
			internal PlannedEntry(ZipArchiveEntry entry, string outputPath, bool isDirectory)
			{
				this.Entry = entry;
				this.OutputPath = outputPath;
				this.IsDirectory = isDirectory;
			}

			internal ZipArchiveEntry Entry { get; private set; }
			internal string OutputPath { get; private set; }
			internal bool IsDirectory { get; private set; }
			internal long Length { get { return this.Entry.Length; } }
		}
	}

	/// <summary>
	/// Pins the destination directory tree with no delete sharing and temporarily
	/// protects the root DACL while elevated extraction is in progress.
	/// </summary>
	internal sealed class SecureExtractionGuard : IDisposable
	{
		private const uint FileReadAttributes = 0x00000080U;
		private const uint GenericWrite = 0x40000000U;
		private const uint FileShareRead = 0x00000001U;
		private const uint FileShareWrite = 0x00000002U;
		private const uint FileShareDelete = 0x00000004U;
		private const uint CreateNew = 1U;
		private const uint OpenExisting = 3U;
		private const uint FileAttributeNormal = 0x00000080U;
		private const uint FileFlagOpenReparsePoint = 0x00200000U;
		private const uint FileFlagBackupSemantics = 0x02000000U;
		private const uint FileFlagSequentialScan = 0x08000000U;
		private const uint FileAttributeDirectory = 0x00000010U;
		private const uint FileAttributeReparsePoint = 0x00000400U;
		private const int ErrorAlreadyExists = 183;
		private const int ErrorFileNotFound = 2;
		private const int ErrorPathNotFound = 3;

		private readonly Dictionary<string, DirectoryLease> _directories =
			new Dictionary<string, DirectoryLease>(StringComparer.OrdinalIgnoreCase);
		private string _originalRootDacl;
		private bool _rootDaclProtected;
		private bool _disposed;

		private SecureExtractionGuard(string rootPath)
		{
			this.RootPath = rootPath;
		}

		internal string RootPath { get; private set; }

		internal static string ValidateDestinationSelection(string destinationPath)
		{
			string rootPath = CanonicalizeDestination(destinationPath);
			ValidateExistingAncestorsReadOnly(rootPath);
			if (Directory.Exists(rootPath) && Directory.EnumerateFileSystemEntries(rootPath).Any())
			{
				throw new IOException("A pasta de destino deve estar vazia. Arquivos existentes nunca são sobrescritos.");
			}
			return rootPath;
		}

		internal static SecureExtractionGuard Create(string destinationPath)
		{
			return CreateCore(destinationPath, true);
		}

#if PRODUCT_PACKAGE_SECURITY_TESTS
		internal static SecureExtractionGuard CreateForSecurityTest(string destinationPath)
		{
			// The production executable is requireAdministrator. The isolated test
			// harness intentionally has no UAC manifest, so it exercises all handle,
			// path and reparse defenses without applying the administrator-only DACL.
			return CreateCore(destinationPath, false);
		}
#endif

		private static SecureExtractionGuard CreateCore(string destinationPath, bool protectRootDacl)
		{
			string rootPath = CanonicalizeDestination(destinationPath);
			SecureExtractionGuard guard = new SecureExtractionGuard(rootPath);
			try
			{
				guard.AcquireDestinationTree();
				if (protectRootDacl)
				{
					guard.ProtectRootDacl();
				}
				guard.ValidateHeldDirectories();
				if (Directory.EnumerateFileSystemEntries(rootPath).Any())
				{
					throw new IOException("A pasta de destino deixou de estar vazia. A instalação foi abortada sem sobrescrever arquivos.");
				}
				return guard;
			}
			catch
			{
				guard.Dispose();
				throw;
			}
		}

		internal void EnsureDirectory(string directoryPath)
		{
			this.ThrowIfDisposed();
			if (string.IsNullOrEmpty(directoryPath))
			{
				throw new ArgumentException("Directory path is required.", "directoryPath");
			}

			string fullPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar);
			if (!IsPathInsideOrEqual(this.RootPath, fullPath))
			{
				throw new IOException("Directory escapes the protected extraction root: " + fullPath);
			}

			this.ValidateDirectoryForWrite(this.RootPath);
			if (this._directories.ContainsKey(fullPath))
			{
				this.ValidateDirectoryForWrite(fullPath);
				return;
			}

			string relative = fullPath.Substring(this.RootPath.Length).TrimStart(Path.DirectorySeparatorChar);
			string current = this.RootPath;
			foreach (string segment in relative.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
			{
				current = Path.Combine(current, segment);
				if (this._directories.ContainsKey(current))
				{
					this.ValidateDirectoryForWrite(current);
					continue;
				}

				if (!NativeMethods.CreateDirectory(current, IntPtr.Zero))
				{
					int error = Marshal.GetLastWin32Error();
					if (error != ErrorAlreadyExists)
					{
						throw new IOException("Unable to create protected extraction directory: " + current, new Win32Exception(error));
					}
				}
				this.AddDirectoryLease(current);
				this.ValidateDirectoryForWrite(current);
			}
		}

		internal FileStream CreateNewFile(string filePath)
		{
			this.ThrowIfDisposed();
			string fullPath = Path.GetFullPath(filePath);
			if (!IsPathInside(this.RootPath, fullPath))
			{
				throw new IOException("File escapes the protected extraction root: " + fullPath);
			}

			string parent = Path.GetDirectoryName(fullPath);
			this.EnsureDirectory(parent);
			this.ValidateDirectoryForWrite(parent);

			SafeFileHandle handle = NativeMethods.CreateFile(
				fullPath,
				GenericWrite,
				0U,
				IntPtr.Zero,
				CreateNew,
				FileAttributeNormal | FileFlagOpenReparsePoint | FileFlagSequentialScan,
				IntPtr.Zero);
			if (handle.IsInvalid)
			{
				int error = Marshal.GetLastWin32Error();
				handle.Dispose();
				throw new IOException(
					"Unable to create a new extraction file (existing files are never overwritten): " + fullPath,
					new Win32Exception(error));
			}

			NativeMethods.ByHandleFileInformation information = GetInformation(handle, fullPath);
			NativeMethods.FileAttributeTagInformation tagInformation = GetTagInformation(handle, fullPath);
			if ((tagInformation.FileAttributes & (FileAttributeDirectory | FileAttributeReparsePoint)) != 0U ||
				tagInformation.ReparseTag != 0U)
			{
				handle.Dispose();
				throw new IOException("Created extraction target is not a regular file: " + fullPath);
			}

			return new FileStream(handle, FileAccess.Write, 1024 * 1024, false);
		}

		internal void ValidateHeldDirectories()
		{
			this.ThrowIfDisposed();
			foreach (DirectoryLease lease in this._directories.Values)
			{
				ValidateLease(lease);
			}
		}

		internal void ValidateDirectoryForWrite(string directoryPath)
		{
			this.ThrowIfDisposed();
			string current = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar);
			if (!IsPathInsideOrEqual(this.RootPath, current))
			{
				throw new IOException("Directory escapes the protected extraction root: " + current);
			}

			while (true)
			{
				DirectoryLease lease;
				if (!this._directories.TryGetValue(current, out lease))
				{
					throw new IOException("Extraction directory is not pinned by a trusted handle: " + current);
				}
				ValidateLease(lease);
				if (current.Equals(this.RootPath, StringComparison.OrdinalIgnoreCase))
				{
					break;
				}
				current = Path.GetDirectoryName(current);
			}
		}

		public void Dispose()
		{
			if (this._disposed)
			{
				return;
			}

			if (this._rootDaclProtected)
			{
				try
				{
					DirectorySecurity security = new DirectorySecurity();
					security.SetSecurityDescriptorSddlForm(this._originalRootDacl, AccessControlSections.Access);
					new DirectoryInfo(this.RootPath).SetAccessControl(security);
				}
				catch
				{
					// Preserve fail-closed extraction behavior; ACL restoration failure is
					// intentionally not allowed to mask the original installation error.
				}
			}

			foreach (DirectoryLease lease in this._directories.Values.Reverse())
			{
				lease.Handle.Dispose();
			}
			this._directories.Clear();
			this._disposed = true;
		}

		private void AcquireDestinationTree()
		{
			string volumeRoot = Path.GetPathRoot(this.RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			this.AddDirectoryLease(volumeRoot);

			string relative = this.RootPath.Substring(volumeRoot.Length);
			string current = volumeRoot.TrimEnd(Path.DirectorySeparatorChar);
			string[] segments = relative.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < segments.Length; i++)
			{
				current = Path.Combine(current + Path.DirectorySeparatorChar, segments[i]);
				if (!NativeMethods.CreateDirectory(current, IntPtr.Zero))
				{
					int error = Marshal.GetLastWin32Error();
					if (error != ErrorAlreadyExists)
					{
						throw new IOException("Unable to create destination directory: " + current, new Win32Exception(error));
					}
				}
				this.AddDirectoryLease(current);
			}
		}

		private void ProtectRootDacl()
		{
			DirectoryInfo directory = new DirectoryInfo(this.RootPath);
			DirectorySecurity original = directory.GetAccessControl(AccessControlSections.Access);
			this._originalRootDacl = original.GetSecurityDescriptorSddlForm(AccessControlSections.Access);

			DirectorySecurity protectedSecurity = new DirectorySecurity();
			protectedSecurity.SetSecurityDescriptorSddlForm(
				"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)",
				AccessControlSections.Access);
			directory.SetAccessControl(protectedSecurity);
			this._rootDaclProtected = true;
		}

		private void AddDirectoryLease(string directoryPath)
		{
			string fullPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar);
			if (fullPath.Length == 2 && fullPath[1] == ':')
			{
				fullPath += Path.DirectorySeparatorChar;
			}
			if (this._directories.ContainsKey(fullPath))
			{
				return;
			}

			SafeFileHandle handle = NativeMethods.CreateFile(
				fullPath,
				FileReadAttributes,
				FileShareRead | FileShareWrite,
				IntPtr.Zero,
				OpenExisting,
				FileFlagBackupSemantics | FileFlagOpenReparsePoint,
				IntPtr.Zero);
			if (handle.IsInvalid)
			{
				int error = Marshal.GetLastWin32Error();
				handle.Dispose();
				throw new IOException("Unable to lock extraction directory: " + fullPath, new Win32Exception(error));
			}

			NativeMethods.ByHandleFileInformation information = GetInformation(handle, fullPath);
			NativeMethods.FileAttributeTagInformation tagInformation = GetTagInformation(handle, fullPath);
			if ((tagInformation.FileAttributes & FileAttributeDirectory) == 0U ||
				(tagInformation.FileAttributes & FileAttributeReparsePoint) != 0U ||
				tagInformation.ReparseTag != 0U)
			{
				handle.Dispose();
				throw new IOException("Extraction path contains a reparse point or non-directory component: " + fullPath);
			}

			this._directories.Add(fullPath, new DirectoryLease(fullPath, handle, information));
		}

		private static string CanonicalizeDestination(string destinationPath)
		{
			if (string.IsNullOrWhiteSpace(destinationPath))
			{
				throw new ArgumentException("Destination path is required.", "destinationPath");
			}
			if (destinationPath.StartsWith("\\\\", StringComparison.Ordinal) ||
				destinationPath.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
				destinationPath.StartsWith("\\\\.\\", StringComparison.Ordinal))
			{
				throw new IOException("Network and device namespace destinations are not accepted.");
			}

			string fullPath = Path.GetFullPath(destinationPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string volumeRoot = Path.GetPathRoot(fullPath);
			if (string.IsNullOrEmpty(volumeRoot) || fullPath.Equals(volumeRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException("Choose a folder below a local drive root; installing directly to the drive root is not accepted.");
			}

			string withoutRoot = fullPath.Substring(volumeRoot.Length);
			if (withoutRoot.IndexOf(':') >= 0)
			{
				throw new IOException("Alternate data stream syntax is not accepted in the destination path.");
			}
			return fullPath;
		}

		private static void ValidateExistingAncestorsReadOnly(string destinationPath)
		{
			string volumeRoot = Path.GetPathRoot(destinationPath);
			string relative = destinationPath.Substring(volumeRoot.Length);
			string current = volumeRoot.TrimEnd(Path.DirectorySeparatorChar);
			foreach (string segment in relative.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
			{
				current = Path.Combine(current + Path.DirectorySeparatorChar, segment);
				SafeFileHandle handle = NativeMethods.CreateFile(
					current,
					FileReadAttributes,
					FileShareRead | FileShareWrite | FileShareDelete,
					IntPtr.Zero,
					OpenExisting,
					FileFlagBackupSemantics | FileFlagOpenReparsePoint,
					IntPtr.Zero);
				if (handle.IsInvalid)
				{
					int error = Marshal.GetLastWin32Error();
					handle.Dispose();
					if (error == ErrorFileNotFound || error == ErrorPathNotFound)
					{
						break;
					}
					throw new IOException("Unable to inspect destination path component: " + current, new Win32Exception(error));
				}

				try
				{
					NativeMethods.FileAttributeTagInformation tagInformation = GetTagInformation(handle, current);
					if ((tagInformation.FileAttributes & FileAttributeReparsePoint) != 0U ||
						tagInformation.ReparseTag != 0U ||
						(tagInformation.FileAttributes & FileAttributeDirectory) == 0U)
					{
						throw new IOException("Destination path contains a reparse point or non-directory component: " + current);
					}
				}
				finally
				{
					handle.Dispose();
				}
			}
		}

		private static bool IsPathInsideOrEqual(string root, string candidate)
		{
			return candidate.Equals(root, StringComparison.OrdinalIgnoreCase) || IsPathInside(root, candidate);
		}

		private static bool IsPathInside(string root, string candidate)
		{
			return candidate.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
		}

		private static NativeMethods.ByHandleFileInformation GetInformation(SafeFileHandle handle, string path)
		{
			NativeMethods.ByHandleFileInformation information;
			if (!NativeMethods.GetFileInformationByHandle(handle, out information))
			{
				throw new IOException("Unable to inspect locked path: " + path, new Win32Exception(Marshal.GetLastWin32Error()));
			}
			return information;
		}

		private static void ValidateLease(DirectoryLease lease)
		{
			NativeMethods.ByHandleFileInformation current = GetInformation(lease.Handle, lease.Path);
			NativeMethods.FileAttributeTagInformation tagInformation = GetTagInformation(lease.Handle, lease.Path);
			if ((tagInformation.FileAttributes & FileAttributeDirectory) == 0U ||
				(tagInformation.FileAttributes & FileAttributeReparsePoint) != 0U ||
				tagInformation.ReparseTag != 0U ||
				current.VolumeSerialNumber != lease.VolumeSerialNumber ||
				current.FileIndexHigh != lease.FileIndexHigh ||
				current.FileIndexLow != lease.FileIndexLow)
			{
				throw new IOException("Protected extraction directory changed or became a reparse point: " + lease.Path);
			}
		}

		private static NativeMethods.FileAttributeTagInformation GetTagInformation(SafeFileHandle handle, string path)
		{
			NativeMethods.FileAttributeTagInformation information;
			if (!NativeMethods.GetFileInformationByHandleEx(
				handle,
				NativeMethods.FileAttributeTagInfo,
				out information,
				Marshal.SizeOf(typeof(NativeMethods.FileAttributeTagInformation))))
			{
				throw new IOException("Unable to inspect reparse metadata for locked path: " + path,
					new Win32Exception(Marshal.GetLastWin32Error()));
			}
			return information;
		}

		private void ThrowIfDisposed()
		{
			if (this._disposed)
			{
				throw new ObjectDisposedException("SecureExtractionGuard");
			}
		}

		private sealed class DirectoryLease
		{
			internal DirectoryLease(string path, SafeFileHandle handle, NativeMethods.ByHandleFileInformation information)
			{
				this.Path = path;
				this.Handle = handle;
				this.VolumeSerialNumber = information.VolumeSerialNumber;
				this.FileIndexHigh = information.FileIndexHigh;
				this.FileIndexLow = information.FileIndexLow;
			}

			internal string Path { get; private set; }
			internal SafeFileHandle Handle { get; private set; }
			internal uint VolumeSerialNumber { get; private set; }
			internal uint FileIndexHigh { get; private set; }
			internal uint FileIndexLow { get; private set; }
		}

		private static class NativeMethods
		{
			internal const int FileAttributeTagInfo = 9;

			[StructLayout(LayoutKind.Sequential)]
			internal struct FileTime
			{
				internal uint LowDateTime;
				internal uint HighDateTime;
			}

			[StructLayout(LayoutKind.Sequential)]
			internal struct ByHandleFileInformation
			{
				internal uint FileAttributes;
				internal FileTime CreationTime;
				internal FileTime LastAccessTime;
				internal FileTime LastWriteTime;
				internal uint VolumeSerialNumber;
				internal uint FileSizeHigh;
				internal uint FileSizeLow;
				internal uint NumberOfLinks;
				internal uint FileIndexHigh;
				internal uint FileIndexLow;
			}

			[StructLayout(LayoutKind.Sequential)]
			internal struct FileAttributeTagInformation
			{
				internal uint FileAttributes;
				internal uint ReparseTag;
			}

			[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
			[return: MarshalAs(UnmanagedType.Bool)]
			internal static extern bool CreateDirectory(string path, IntPtr securityAttributes);

			[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
			internal static extern SafeFileHandle CreateFile(
				string fileName,
				uint desiredAccess,
				uint shareMode,
				IntPtr securityAttributes,
				uint creationDisposition,
				uint flagsAndAttributes,
				IntPtr templateFile);

			[DllImport("kernel32.dll", SetLastError = true)]
			[return: MarshalAs(UnmanagedType.Bool)]
			internal static extern bool GetFileInformationByHandle(
				SafeFileHandle file,
				out ByHandleFileInformation fileInformation);

			[DllImport("kernel32.dll", SetLastError = true)]
			[return: MarshalAs(UnmanagedType.Bool)]
			internal static extern bool GetFileInformationByHandleEx(
				SafeFileHandle file,
				int fileInformationClass,
				out FileAttributeTagInformation fileInformation,
				int bufferSize);
		}
	}
}
