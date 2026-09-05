using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace InstallerHost
{
	/// <summary>
	/// Opens the split product package produced by RetroBuild and keeps every
	/// authenticated input locked against writes, renames and deletion until the
	/// caller finishes reading the logical ZIP stream.
	///
	/// RetroBuild package contract:
	///   setup.exe
	///   setup.exe.pkg.001 .. setup.exe.pkg.NNN
	///   setup.exe.sha256.txt
	///
	/// The sidecar contains exactly one strict line for the setup, one for every
	/// part and one for the original logical .zip:
	///   &lt;64 hexadecimal SHA-256 characters&gt;&lt;two spaces&gt;&lt;leaf file name&gt;
	///
	/// This is transport integrity, not publisher authentication. Production
	/// packages still need an Authenticode/CMS-signed manifest.
	/// </summary>
	internal static class ProductPackageSecurity
	{
		private const int MaxSidecarBytes = 1024 * 1024;
		private const int MaxPartCount = 999;
		private const uint GenericRead = 0x80000000U;
		private const uint FileShareRead = 0x00000001U;
		private const uint OpenExisting = 3U;
		private const uint FileAttributeNormal = 0x00000080U;
		private const uint FileFlagOpenReparsePoint = 0x00200000U;
		private const uint FileFlagSequentialScan = 0x08000000U;
		private const uint FileAttributeDirectory = 0x00000010U;
		private const uint FileAttributeReparsePoint = 0x00000400U;

		private static readonly Regex SidecarLinePattern = new Regex(
			"^(?<hash>[A-Fa-f0-9]{64})  (?<leaf>[^\\\\/:*?\"<>|\\r\\n]+)$",
			RegexOptions.CultureInvariant | RegexOptions.Compiled);

		internal static VerifiedProductPackageStream OpenVerifiedPackage(string executablePath)
		{
			if (string.IsNullOrWhiteSpace(executablePath))
			{
				throw new ArgumentException("Executable path is required.", "executablePath");
			}

			string setupPath = Path.GetFullPath(executablePath);
			string packageFolder = Path.GetDirectoryName(setupPath);
			string setupLeaf = Path.GetFileName(setupPath);
			if (string.IsNullOrEmpty(packageFolder) || string.IsNullOrEmpty(setupLeaf))
			{
				throw new IOException("Invalid installer executable path.");
			}

			List<PackagePart> parts = DiscoverParts(packageFolder, setupLeaf);
			string sidecarPath = setupPath + ".sha256.txt";

			FileStream sidecarStream = null;
			FileStream setupStream = null;
			List<FileStream> partStreams = new List<FileStream>();
			VerifiedProductPackageStream logicalStream = null;

			try
			{
				sidecarStream = OpenLockedRegularFile(sidecarPath, "SHA-256 sidecar");
				setupStream = OpenLockedRegularFile(setupPath, "installer executable");

				foreach (PackagePart part in parts)
				{
					FileStream partStream = OpenLockedRegularFile(part.Path, "package part " + part.LeafName);
					if (partStream.Length <= 0L)
					{
						partStream.Dispose();
						throw new InvalidDataException("Package part is empty: " + part.LeafName);
					}
					partStreams.Add(partStream);
				}

				Dictionary<string, SidecarEntry> entries = ParseSidecar(sidecarStream);
				ValidateSidecarShape(entries, setupLeaf, parts);

				VerifyStreamHash(setupStream, entries[setupLeaf].Hash, setupLeaf);
				for (int i = 0; i < parts.Count; i++)
				{
					VerifyStreamHash(partStreams[i], entries[parts[i].LeafName].Hash, parts[i].LeafName);
				}

				SidecarEntry logicalArchiveEntry = entries.Values.Single(entry =>
					entry.LeafName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

				logicalStream = new VerifiedProductPackageStream(
					sidecarStream,
					setupStream,
					partStreams,
					parts.Select(part => part.Path).ToArray(),
					logicalArchiveEntry.LeafName);
				sidecarStream = null;
				setupStream = null;
				partStreams = null;

				VerifyStreamHash(logicalStream, logicalArchiveEntry.Hash, logicalArchiveEntry.LeafName);
				logicalStream.Position = 0L;
				return logicalStream;
			}
			catch
			{
				if (logicalStream != null)
				{
					logicalStream.Dispose();
				}
				if (partStreams != null)
				{
					foreach (FileStream stream in partStreams)
					{
						stream.Dispose();
					}
				}
				if (setupStream != null)
				{
					setupStream.Dispose();
				}
				if (sidecarStream != null)
				{
					sidecarStream.Dispose();
				}
				throw;
			}
		}

		private static List<PackagePart> DiscoverParts(string folder, string setupLeaf)
		{
			string canonicalPrefix = setupLeaf + ".pkg.";
			string legacyPrefix = Path.GetFileNameWithoutExtension(setupLeaf) + ".pkg.";
			List<PackagePart> parts = new List<PackagePart>();
			List<string> unsupportedLegacy = new List<string>();

			foreach (string entryPath in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.TopDirectoryOnly))
			{
				string leaf = Path.GetFileName(entryPath);
				if (leaf.StartsWith(canonicalPrefix, StringComparison.OrdinalIgnoreCase))
				{
					string suffix = leaf.Substring(canonicalPrefix.Length);
					int number;
					if (suffix.Length != 3 || !IsAsciiDigits(suffix) ||
						!int.TryParse(suffix, out number) || number < 1 || number > MaxPartCount)
					{
						throw new InvalidDataException("Malformed or extra package part: " + leaf);
					}

					parts.Add(new PackagePart(number, Path.GetFullPath(entryPath), leaf));
				}
				else if (!legacyPrefix.Equals(canonicalPrefix, StringComparison.OrdinalIgnoreCase) &&
					leaf.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
				{
					unsupportedLegacy.Add(leaf);
				}
			}

			if (unsupportedLegacy.Count > 0)
			{
				throw new InvalidDataException(
					"Unsupported/ambiguous legacy package name(s): " + string.Join(", ", unsupportedLegacy.ToArray()) +
					". Rebuild with RetroBuild so parts are named " + setupLeaf + ".pkg.NNN.");
			}

			if (parts.Count == 0)
			{
				throw new FileNotFoundException(
					"Split product package not found. Expected " + setupLeaf + ".pkg.001 beside the installer.",
					Path.Combine(folder, setupLeaf + ".pkg.001"));
			}

			List<IGrouping<int, PackagePart>> duplicateNumbers = parts.GroupBy(part => part.Number)
				.Where(group => group.Count() != 1).ToList();
			if (duplicateNumbers.Count > 0)
			{
				throw new InvalidDataException("Duplicate split package part number detected.");
			}

			parts.Sort((left, right) => left.Number.CompareTo(right.Number));
			for (int i = 0; i < parts.Count; i++)
			{
				int expectedNumber = i + 1;
				if (parts[i].Number != expectedNumber)
				{
					throw new InvalidDataException(
						"Missing or out-of-order split package part. Expected " +
						setupLeaf + ".pkg." + expectedNumber.ToString("000") + ".");
				}
			}

			return parts;
		}

		private static Dictionary<string, SidecarEntry> ParseSidecar(FileStream sidecarStream)
		{
			if (sidecarStream.Length <= 0L || sidecarStream.Length > MaxSidecarBytes)
			{
				throw new InvalidDataException("SHA-256 sidecar must be between 1 byte and 1 MiB.");
			}

			sidecarStream.Position = 0L;
			string text;
			using (StreamReader reader = new StreamReader(
				sidecarStream,
				new UTF8Encoding(false, true),
				true,
				4096,
				true))
			{
				text = reader.ReadToEnd();
			}

			if (text.Length == 0 || text.IndexOf('\0') >= 0)
			{
				throw new InvalidDataException("SHA-256 sidecar is empty or contains NUL characters.");
			}

			string[] lines = Regex.Split(text, "\\r?\\n");
			if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
			{
				Array.Resize(ref lines, lines.Length - 1);
			}
			if (lines.Length == 0)
			{
				throw new InvalidDataException("SHA-256 sidecar contains no entries.");
			}

			Dictionary<string, SidecarEntry> entries = new Dictionary<string, SidecarEntry>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < lines.Length; i++)
			{
				Match match = SidecarLinePattern.Match(lines[i]);
				if (!match.Success)
				{
					throw new InvalidDataException("Invalid SHA-256 sidecar line " + (i + 1) + ".");
				}

				string leaf = match.Groups["leaf"].Value;
				if (!leaf.Equals(Path.GetFileName(leaf), StringComparison.Ordinal) ||
					leaf.EndsWith(" ", StringComparison.Ordinal) ||
					leaf.EndsWith(".", StringComparison.Ordinal))
				{
					throw new InvalidDataException("Invalid leaf name in SHA-256 sidecar line " + (i + 1) + ".");
				}

				SidecarEntry entry = new SidecarEntry(leaf, match.Groups["hash"].Value.ToUpperInvariant());
				if (entries.ContainsKey(leaf))
				{
					throw new InvalidDataException("Duplicate SHA-256 sidecar entry: " + leaf);
				}
				entries.Add(leaf, entry);
			}

			return entries;
		}

		private static void ValidateSidecarShape(
			Dictionary<string, SidecarEntry> entries,
			string setupLeaf,
			List<PackagePart> parts)
		{
			int expectedEntryCount = parts.Count + 2;
			if (entries.Count != expectedEntryCount)
			{
				throw new InvalidDataException(
					"SHA-256 sidecar must contain exactly the setup, every package part and one logical ZIP entry.");
			}

			if (!entries.ContainsKey(setupLeaf))
			{
				throw new InvalidDataException("SHA-256 sidecar does not contain the current setup executable: " + setupLeaf);
			}

			foreach (PackagePart part in parts)
			{
				if (!entries.ContainsKey(part.LeafName))
				{
					throw new InvalidDataException("SHA-256 sidecar does not contain package part: " + part.LeafName);
				}
			}

			List<SidecarEntry> zipEntries = entries.Values
				.Where(entry => entry.LeafName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (zipEntries.Count != 1)
			{
				throw new InvalidDataException("SHA-256 sidecar must contain exactly one logical .zip entry.");
			}
		}

		private static FileStream OpenLockedRegularFile(string path, string description)
		{
			string fullPath = Path.GetFullPath(path);
			SafeFileHandle handle = NativeMethods.CreateFile(
				fullPath,
				GenericRead,
				FileShareRead,
				IntPtr.Zero,
				OpenExisting,
				FileAttributeNormal | FileFlagOpenReparsePoint | FileFlagSequentialScan,
				IntPtr.Zero);

			if (handle.IsInvalid)
			{
				int error = Marshal.GetLastWin32Error();
				handle.Dispose();
				throw new IOException("Unable to lock " + description + ": " + fullPath, new Win32Exception(error));
			}

			NativeMethods.ByHandleFileInformation information;
			if (!NativeMethods.GetFileInformationByHandle(handle, out information))
			{
				int error = Marshal.GetLastWin32Error();
				handle.Dispose();
				throw new IOException("Unable to inspect " + description + ": " + fullPath, new Win32Exception(error));
			}

			NativeMethods.FileAttributeTagInformation tagInformation;
			if (!NativeMethods.GetFileInformationByHandleEx(
				handle,
				NativeMethods.FileAttributeTagInfo,
				out tagInformation,
				Marshal.SizeOf(typeof(NativeMethods.FileAttributeTagInformation))))
			{
				int error = Marshal.GetLastWin32Error();
				handle.Dispose();
				throw new IOException("Unable to inspect reparse metadata for " + description + ": " + fullPath,
					new Win32Exception(error));
			}

			if ((tagInformation.FileAttributes & FileAttributeReparsePoint) != 0U || tagInformation.ReparseTag != 0U)
			{
				handle.Dispose();
				throw new IOException("Reparse points are not accepted for " + description + ": " + fullPath);
			}
			if ((information.FileAttributes & FileAttributeDirectory) != 0U)
			{
				handle.Dispose();
				throw new IOException("Expected a regular file for " + description + ": " + fullPath);
			}

			return new FileStream(handle, FileAccess.Read, 1024 * 1024, false);
		}

		private static void VerifyStreamHash(Stream stream, string expectedHash, string displayName)
		{
			stream.Position = 0L;
			string actualHash;
			using (SHA256 algorithm = SHA256.Create())
			{
				actualHash = ToHex(algorithm.ComputeHash(stream));
			}
			stream.Position = 0L;

			if (!actualHash.Equals(expectedHash, StringComparison.Ordinal))
			{
				throw new InvalidDataException(
					"SHA-256 mismatch for " + displayName + ". Expected " + expectedHash + ", obtained " + actualHash + ".");
			}
		}

		private static string ToHex(byte[] value)
		{
			return BitConverter.ToString(value).Replace("-", string.Empty);
		}

		private static bool IsAsciiDigits(string value)
		{
			for (int i = 0; i < value.Length; i++)
			{
				if (value[i] < '0' || value[i] > '9')
				{
					return false;
				}
			}
			return true;
		}

		private sealed class PackagePart
		{
			internal PackagePart(int number, string path, string leafName)
			{
				this.Number = number;
				this.Path = path;
				this.LeafName = leafName;
			}

			internal int Number { get; private set; }
			internal string Path { get; private set; }
			internal string LeafName { get; private set; }
		}

		private sealed class SidecarEntry
		{
			internal SidecarEntry(string leafName, string hash)
			{
				this.LeafName = leafName;
				this.Hash = hash;
			}

			internal string LeafName { get; private set; }
			internal string Hash { get; private set; }
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

	internal sealed class VerifiedProductPackageStream : Stream
	{
		private readonly FileStream _sidecarStream;
		private readonly FileStream _setupStream;
		private readonly List<FileStream> _partStreams;
		private readonly long[] _partStarts;
		private readonly long _length;
		private bool _disposed;
		private long _position;

		internal VerifiedProductPackageStream(
			FileStream sidecarStream,
			FileStream setupStream,
			List<FileStream> partStreams,
			string[] partPaths,
			string logicalArchiveName)
		{
			if (sidecarStream == null || setupStream == null || partStreams == null || partStreams.Count == 0)
			{
				throw new ArgumentException("Verified package streams are required.");
			}

			this._sidecarStream = sidecarStream;
			this._setupStream = setupStream;
			this._partStreams = partStreams;
			this.PartPaths = partPaths;
			this.LogicalArchiveName = logicalArchiveName;
			this._partStarts = new long[partStreams.Count];

			long total = 0L;
			for (int i = 0; i < partStreams.Count; i++)
			{
				this._partStarts[i] = total;
				try
				{
					total = checked(total + partStreams[i].Length);
				}
				catch (OverflowException ex)
				{
					throw new InvalidDataException("Logical package length overflow.", ex);
				}
			}

			this._length = total;
			this._position = 0L;
		}

		internal string[] PartPaths { get; private set; }
		internal string LogicalArchiveName { get; private set; }

		public override bool CanRead { get { return !this._disposed; } }
		public override bool CanSeek { get { return !this._disposed; } }
		public override bool CanWrite { get { return false; } }
		public override long Length { get { this.ThrowIfDisposed(); return this._length; } }
		public override long Position
		{
			get { this.ThrowIfDisposed(); return this._position; }
			set { this.Seek(value, SeekOrigin.Begin); }
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			this.ThrowIfDisposed();
			if (buffer == null)
			{
				throw new ArgumentNullException("buffer");
			}
			if (offset < 0 || count < 0 || buffer.Length - offset < count)
			{
				throw new ArgumentOutOfRangeException("offset");
			}
			if (count == 0 || this._position >= this._length)
			{
				return 0;
			}

			int totalRead = 0;
			while (count > 0 && this._position < this._length)
			{
				int partIndex = this.FindPart(this._position);
				if (partIndex < 0)
				{
					throw new EndOfStreamException("Logical package position is not backed by a locked part.");
				}

				FileStream part = this._partStreams[partIndex];
				long positionInsidePart = this._position - this._partStarts[partIndex];
				long remaining = part.Length - positionInsidePart;
				part.Position = positionInsidePart;
				int requested = (int)Math.Min((long)count, remaining);
				int bytesRead = part.Read(buffer, offset, requested);
				if (bytesRead <= 0)
				{
					throw new EndOfStreamException("Unexpected end of locked package part: " + this.PartPaths[partIndex]);
				}

				this._position += bytesRead;
				offset += bytesRead;
				count -= bytesRead;
				totalRead += bytesRead;
			}

			return totalRead;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			this.ThrowIfDisposed();
			long newPosition;
			try
			{
				if (origin == SeekOrigin.Begin)
				{
					newPosition = offset;
				}
				else if (origin == SeekOrigin.Current)
				{
					newPosition = checked(this._position + offset);
				}
				else if (origin == SeekOrigin.End)
				{
					newPosition = checked(this._length + offset);
				}
				else
				{
					throw new ArgumentOutOfRangeException("origin");
				}
			}
			catch (OverflowException ex)
			{
				throw new IOException("Seek overflow in logical package stream.", ex);
			}

			if (newPosition < 0L || newPosition > this._length)
			{
				throw new IOException("Seek outside logical package stream.");
			}

			this._position = newPosition;
			return newPosition;
		}

		public override void Flush()
		{
			this.ThrowIfDisposed();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && !this._disposed)
			{
				this._disposed = true;
				foreach (FileStream stream in this._partStreams)
				{
					stream.Dispose();
				}
				this._setupStream.Dispose();
				this._sidecarStream.Dispose();
			}

			base.Dispose(disposing);
		}

		private int FindPart(long position)
		{
			for (int i = this._partStarts.Length - 1; i >= 0; i--)
			{
				if (position >= this._partStarts[i])
				{
					return i;
				}
			}
			return -1;
		}

		private void ThrowIfDisposed()
		{
			if (this._disposed)
			{
				throw new ObjectDisposedException("VerifiedProductPackageStream");
			}
		}
	}
}
