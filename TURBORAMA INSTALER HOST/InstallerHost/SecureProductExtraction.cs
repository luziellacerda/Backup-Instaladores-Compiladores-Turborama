using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace InstallerHost
{
	/// <summary>
	/// Extracts the integrity-verified logical product ZIP without following
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
	/// Pins the destination tree without write/delete sharing and records every
	/// object created by this transaction for handle-based rollback. Production
	/// creation is accepted only while running under a limited, non-admin token.
	/// </summary>
	internal sealed class SecureExtractionGuard : IDisposable
	{
		private const uint FileReadAttributes = 0x00000080U;
		private const uint GenericWrite = 0x40000000U;
		private const uint DeleteAccess = 0x00010000U;
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
		private const uint DuplicateSameAccess = 0x00000002U;

		private readonly Dictionary<string, DirectoryLease> _directories =
			new Dictionary<string, DirectoryLease>(StringComparer.OrdinalIgnoreCase);
		private readonly List<DirectoryLease> _directoryOrder = new List<DirectoryLease>();
		private readonly List<FileLease> _createdFiles = new List<FileLease>();
		private bool _committed;
		private bool _rollbackAttempted;
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
			LimitedUserImpersonation.EnsureCurrentTokenIsLimited();
			return CreateCore(destinationPath);
		}

#if PRODUCT_PACKAGE_SECURITY_TESTS
		internal static SecureExtractionGuard CreateForSecurityTest(string destinationPath)
		{
			// The isolated harness has no UAC manifest. It must satisfy the same
			// limited-token invariant as production; only token acquisition differs.
			LimitedUserImpersonation.EnsureCurrentTokenIsLimited();
			return CreateCore(destinationPath);
		}
#endif

		private static SecureExtractionGuard CreateCore(string destinationPath)
		{
			string rootPath = CanonicalizeDestination(destinationPath);
			SecureExtractionGuard guard = new SecureExtractionGuard(rootPath);
			try
			{
				guard.AcquireDestinationTree();
				guard.ValidateHeldDirectories();
				if (Directory.EnumerateFileSystemEntries(rootPath).Any())
				{
					throw new IOException("A pasta de destino deixou de estar vazia. A instalação foi abortada sem sobrescrever arquivos.");
				}
				return guard;
			}
			catch (Exception creationError)
			{
				try
				{
					guard.RollbackCreatedEntries();
				}
				catch (Exception rollbackError)
				{
					guard.CloseAllHandles();
					throw new AggregateException(
						"Destination setup failed and its handle-based rollback was incomplete.",
						creationError,
						rollbackError);
				}
				guard.CloseAllHandles();
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

				bool created = NativeMethods.CreateDirectory(current, IntPtr.Zero);
				if (!created)
				{
					int error = Marshal.GetLastWin32Error();
					if (error != ErrorAlreadyExists)
					{
						throw new IOException("Unable to create protected extraction directory: " + current, new Win32Exception(error));
					}
				}
				this.AddDirectoryLease(current, created);
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
				GenericWrite | DeleteAccess | FileReadAttributes,
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

			try
			{
				NativeMethods.ByHandleFileInformation information = GetInformation(handle, fullPath);
				NativeMethods.FileAttributeTagInformation tagInformation = GetTagInformation(handle, fullPath);
				if ((tagInformation.FileAttributes & (FileAttributeDirectory | FileAttributeReparsePoint)) != 0U ||
					tagInformation.ReparseTag != 0U)
				{
					throw new IOException("Created extraction target is not a regular file: " + fullPath);
				}

				SafeFileHandle rollbackHandle = DuplicateHandle(handle, fullPath);
				this._createdFiles.Add(new FileLease(fullPath, rollbackHandle, information));
				return new FileStream(handle, FileAccess.Write, 1024 * 1024, false);
			}
			catch (Exception creationError)
			{
				try
				{
					MarkForDeletion(handle, fullPath);
				}
				catch (Exception cleanupError)
				{
					handle.Dispose();
					throw new AggregateException(
						"File creation failed and the new object could not be rolled back safely.",
						creationError,
						cleanupError);
				}
				handle.Dispose();
				throw;
			}
		}

		internal void Commit()
		{
			this.ThrowIfDisposed();
			if (this._rollbackAttempted)
			{
				throw new InvalidOperationException("A rolled-back extraction transaction cannot be committed.");
			}
			if (this._committed)
			{
				return;
			}

			this.ValidateHeldDirectories();
			foreach (FileLease lease in this._createdFiles)
			{
				ValidateFileLease(lease);
			}
			this._committed = true;
			this.CloseFileHandles();
		}

		internal void RollbackCreatedEntries()
		{
			this.ThrowIfDisposed();
			if (this._committed)
			{
				throw new InvalidOperationException("A committed extraction transaction cannot be rolled back.");
			}
			if (this._rollbackAttempted)
			{
				return;
			}
			this._rollbackAttempted = true;

			List<Exception> failures = new List<Exception>();
			for (int i = this._createdFiles.Count - 1; i >= 0; i--)
			{
				FileLease lease = this._createdFiles[i];
				try
				{
					ValidateFileLease(lease);
					MarkForDeletion(lease.Handle, lease.Path);
				}
				catch (Exception ex)
				{
					failures.Add(new IOException("Could not roll back created file: " + lease.Path, ex));
				}
				finally
				{
					lease.Handle.Dispose();
				}
			}
			this._createdFiles.Clear();

			for (int i = this._directoryOrder.Count - 1; i >= 0; i--)
			{
				DirectoryLease lease = this._directoryOrder[i];
				if (!lease.Created || lease.Handle.IsClosed)
				{
					continue;
				}
				try
				{
					ValidateDirectoryLease(lease);
					MarkForDeletion(lease.Handle, lease.Path);
				}
				catch (Exception ex)
				{
					failures.Add(new IOException("Could not roll back created directory: " + lease.Path, ex));
				}
				finally
				{
					lease.Handle.Dispose();
				}
			}

			if (failures.Count > 0)
			{
				throw new AggregateException("Handle-based extraction rollback was incomplete.", failures);
			}
		}

		internal void ValidateHeldDirectories()
		{
			this.ThrowIfDisposed();
			foreach (DirectoryLease lease in this._directories.Values)
			{
				if (!lease.Handle.IsClosed)
				{
					ValidateDirectoryLease(lease);
				}
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
				if (lease.Handle.IsClosed)
				{
					throw new IOException("Extraction directory lease is already closed: " + current);
				}
				ValidateDirectoryLease(lease);
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

			Exception rollbackFailure = null;
			try
			{
				if (!this._committed && !this._rollbackAttempted)
				{
					this.RollbackCreatedEntries();
				}
			}
			catch (Exception ex)
			{
				rollbackFailure = ex;
			}
			finally
			{
				this.CloseAllHandles();
			}

			if (rollbackFailure != null)
			{
				throw rollbackFailure;
			}
		}

		private void AcquireDestinationTree()
		{
			string volumeRoot = Path.GetPathRoot(this.RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			this.AddDirectoryLease(volumeRoot, false);

			string relative = this.RootPath.Substring(volumeRoot.Length);
			string current = volumeRoot.TrimEnd(Path.DirectorySeparatorChar);
			string[] segments = relative.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < segments.Length; i++)
			{
				current = Path.Combine(current + Path.DirectorySeparatorChar, segments[i]);
				bool created = NativeMethods.CreateDirectory(current, IntPtr.Zero);
				if (!created)
				{
					int error = Marshal.GetLastWin32Error();
					if (error != ErrorAlreadyExists)
					{
						throw new IOException("Unable to create destination directory: " + current, new Win32Exception(error));
					}
				}
				this.AddDirectoryLease(current, created);
			}
		}

		private void AddDirectoryLease(string directoryPath, bool created)
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
				FileReadAttributes | (created ? DeleteAccess : 0U),
				FileShareRead,
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

			DirectoryLease lease = new DirectoryLease(fullPath, handle, information, created);
			this._directories.Add(fullPath, lease);
			this._directoryOrder.Add(lease);
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

		private static SafeFileHandle DuplicateHandle(SafeFileHandle source, string path)
		{
			IntPtr duplicate;
			if (!NativeMethods.DuplicateHandle(
				NativeMethods.GetCurrentProcess(),
				source.DangerousGetHandle(),
				NativeMethods.GetCurrentProcess(),
				out duplicate,
				0U,
				false,
				DuplicateSameAccess))
			{
				throw new IOException("Unable to retain rollback handle for created file: " + path,
					new Win32Exception(Marshal.GetLastWin32Error()));
			}
			GC.KeepAlive(source);
			return new SafeFileHandle(duplicate, true);
		}

		private static void ValidateDirectoryLease(DirectoryLease lease)
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

		private static void ValidateFileLease(FileLease lease)
		{
			NativeMethods.ByHandleFileInformation current = GetInformation(lease.Handle, lease.Path);
			NativeMethods.FileAttributeTagInformation tagInformation = GetTagInformation(lease.Handle, lease.Path);
			if ((tagInformation.FileAttributes & (FileAttributeDirectory | FileAttributeReparsePoint)) != 0U ||
				tagInformation.ReparseTag != 0U ||
				current.VolumeSerialNumber != lease.VolumeSerialNumber ||
				current.FileIndexHigh != lease.FileIndexHigh ||
				current.FileIndexLow != lease.FileIndexLow)
			{
				throw new IOException("Created extraction file changed identity or became unsafe: " + lease.Path);
			}
		}

		private static void MarkForDeletion(SafeFileHandle handle, string path)
		{
			NativeMethods.FileDispositionInformation disposition = new NativeMethods.FileDispositionInformation
			{
				DeleteFile = 1
			};
			if (!NativeMethods.SetFileInformationByHandle(
				handle,
				NativeMethods.FileDispositionInfo,
				ref disposition,
				Marshal.SizeOf(typeof(NativeMethods.FileDispositionInformation))))
			{
				throw new IOException("Unable to mark created extraction object for deletion: " + path,
					new Win32Exception(Marshal.GetLastWin32Error()));
			}
		}

		private void CloseFileHandles()
		{
			foreach (FileLease lease in this._createdFiles)
			{
				lease.Handle.Dispose();
			}
			this._createdFiles.Clear();
		}

		private void CloseAllHandles()
		{
			if (this._disposed)
			{
				return;
			}
			this.CloseFileHandles();
			for (int i = this._directoryOrder.Count - 1; i >= 0; i--)
			{
				this._directoryOrder[i].Handle.Dispose();
			}
			this._directoryOrder.Clear();
			this._directories.Clear();
			this._disposed = true;
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
			internal DirectoryLease(
				string path,
				SafeFileHandle handle,
				NativeMethods.ByHandleFileInformation information,
				bool created)
			{
				this.Path = path;
				this.Handle = handle;
				this.Created = created;
				this.VolumeSerialNumber = information.VolumeSerialNumber;
				this.FileIndexHigh = information.FileIndexHigh;
				this.FileIndexLow = information.FileIndexLow;
			}

			internal string Path { get; private set; }
			internal SafeFileHandle Handle { get; private set; }
			internal bool Created { get; private set; }
			internal uint VolumeSerialNumber { get; private set; }
			internal uint FileIndexHigh { get; private set; }
			internal uint FileIndexLow { get; private set; }
		}

		private sealed class FileLease
		{
			internal FileLease(string path, SafeFileHandle handle, NativeMethods.ByHandleFileInformation information)
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
			internal const int FileDispositionInfo = 4;

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

			[StructLayout(LayoutKind.Sequential)]
			internal struct FileDispositionInformation
			{
				internal byte DeleteFile;
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

			[DllImport("kernel32.dll", SetLastError = true)]
			[return: MarshalAs(UnmanagedType.Bool)]
			internal static extern bool SetFileInformationByHandle(
				SafeFileHandle file,
				int fileInformationClass,
				ref FileDispositionInformation fileInformation,
				int bufferSize);

			[DllImport("kernel32.dll", SetLastError = true)]
			[return: MarshalAs(UnmanagedType.Bool)]
			internal static extern bool DuplicateHandle(
				IntPtr sourceProcessHandle,
				IntPtr sourceHandle,
				IntPtr targetProcessHandle,
				out IntPtr targetHandle,
				uint desiredAccess,
				[MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
				uint options);

			[DllImport("kernel32.dll")]
			internal static extern IntPtr GetCurrentProcess();
		}
	}
}
