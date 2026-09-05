using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace InstallerHost
{
	internal static class ProductPackageSecurityTests
	{
		private static int _passed;

		private static int Main(string[] args)
		{
			if (args.Length != 1)
			{
				Console.Error.WriteLine("Usage: ProductPackageSecurityTests.exe <safe-work-root>");
				return 2;
			}

			string safeRoot = Path.GetFullPath(args[0]);
			Directory.CreateDirectory(safeRoot);
			string testRoot = Path.Combine(safeRoot, "product-package-security-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(testRoot);

			try
			{
				RunPackageTests(testRoot);
				RunExtractionTests(testRoot);
				Console.WriteLine("PRODUCT PACKAGE SECURITY TESTS: " + _passed + " PASS");
				return 0;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("PRODUCT PACKAGE SECURITY TEST FAILED: " + ex);
				return 1;
			}
			finally
			{
				if (testRoot.StartsWith(safeRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
					StringComparison.OrdinalIgnoreCase))
				{
					TryDeleteDirectory(testRoot);
				}
			}
		}

		private static void RunPackageTests(string testRoot)
		{
			string validFolder = NewCaseFolder(testRoot, "valid");
			PackageFixture valid = CreatePackage(validFolder, 2);
			using (VerifiedProductPackageStream stream = ProductPackageSecurity.OpenVerifiedPackage(valid.SetupPath))
			{
				Assert(stream.PartPaths.Length == 2, "two contiguous parts discovered");
				Assert(stream.LogicalArchiveName == valid.ZipLeaf, "logical ZIP sidecar entry selected");
				Assert(ReadAll(stream).SequenceEqual(valid.LogicalBytes), "logical multipart stream bytes verified");

				ExpectFailure(delegate
				{
					using (FileStream ignored = new FileStream(valid.PartPaths[0], FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
					{
					}
				}, "write sharing denied while package lease is alive");

				ExpectFailure(delegate { File.Delete(valid.PartPaths[1]); },
					"delete sharing denied while package lease is alive");
			}

			string setupTamperFolder = NewCaseFolder(testRoot, "setup-tamper");
			PackageFixture setupTamper = CreatePackage(setupTamperFolder, 1);
			File.AppendAllText(setupTamper.SetupPath, "tampered", Encoding.ASCII);
			ExpectOpenFailure(setupTamper.SetupPath, "tampered setup hash rejected");

			string partTamperFolder = NewCaseFolder(testRoot, "part-tamper");
			PackageFixture partTamper = CreatePackage(partTamperFolder, 1);
			File.AppendAllText(partTamper.PartPaths[0], "tampered", Encoding.ASCII);
			ExpectOpenFailure(partTamper.SetupPath, "tampered part hash rejected");

			string logicalTamperFolder = NewCaseFolder(testRoot, "logical-tamper");
			PackageFixture logicalTamper = CreatePackage(logicalTamperFolder, 2);
			ReplaceSidecarHash(logicalTamper.SidecarPath, logicalTamper.ZipLeaf, new string('0', 64));
			ExpectOpenFailure(logicalTamper.SetupPath, "logical concatenated ZIP hash rejected");

			string noSidecarFolder = NewCaseFolder(testRoot, "no-sidecar");
			PackageFixture noSidecar = CreatePackage(noSidecarFolder, 1);
			File.Delete(noSidecar.SidecarPath);
			ExpectOpenFailure(noSidecar.SetupPath, "missing mandatory sidecar rejected");

			string gapFolder = NewCaseFolder(testRoot, "gap");
			PackageFixture gap = CreatePackage(gapFolder, 2);
			File.Move(gap.PartPaths[1], gap.SetupPath + ".pkg.003");
			ExpectOpenFailure(gap.SetupPath, "missing/gapped part sequence rejected");

			string malformedFolder = NewCaseFolder(testRoot, "malformed");
			PackageFixture malformed = CreatePackage(malformedFolder, 1);
			File.WriteAllBytes(malformed.SetupPath + ".pkg.EXTRA", new byte[] { 1 });
			ExpectOpenFailure(malformed.SetupPath, "malformed extra package part rejected");

			string extraLineFolder = NewCaseFolder(testRoot, "extra-line");
			PackageFixture extraLine = CreatePackage(extraLineFolder, 1);
			File.AppendAllText(extraLine.SidecarPath,
				Sha256(new byte[] { 1 }) + "  unexpected.bin" + Environment.NewLine,
				new UTF8Encoding(false));
			ExpectOpenFailure(extraLine.SetupPath, "unexpected sidecar entry rejected");

			string strictLineFolder = NewCaseFolder(testRoot, "strict-line");
			PackageFixture strictLine = CreatePackage(strictLineFolder, 1);
			string strictText = File.ReadAllText(strictLine.SidecarPath, Encoding.UTF8)
				.Replace("  " + Path.GetFileName(strictLine.SetupPath), " " + Path.GetFileName(strictLine.SetupPath));
			File.WriteAllText(strictLine.SidecarPath, strictText, new UTF8Encoding(false));
			ExpectOpenFailure(strictLine.SetupPath, "sidecar requires exactly two separators");

			string legacyFolder = NewCaseFolder(testRoot, "legacy-name");
			PackageFixture legacy = CreatePackage(legacyFolder, 1);
			File.WriteAllBytes(Path.Combine(legacyFolder, "setup.pkg.001"), new byte[] { 1 });
			ExpectOpenFailure(legacy.SetupPath, "ambiguous legacy part naming rejected");

			string reparseFolder = NewCaseFolder(testRoot, "reparse-source");
			PackageFixture reparse = CreatePackage(reparseFolder, 1);
			File.Delete(reparse.PartPaths[0]);
			string realPartDirectory = reparse.PartPaths[0] + ".real-directory";
			Directory.CreateDirectory(realPartDirectory);
			CreateJunction(reparse.PartPaths[0], realPartDirectory);
			ExpectOpenFailure(reparse.SetupPath, "reparse package part rejected");
		}

		private static void RunExtractionTests(string testRoot)
		{
			byte[] zipBytes = CreateZip(new Dictionary<string, byte[]>
			{
				{ "folder/", null },
				{ "folder/game.txt", Encoding.UTF8.GetBytes("verified game data") },
				{ "TurboRama.exe", new byte[] { 0x4D, 0x5A, 1, 2, 3 } }
			});
			string destination = Path.Combine(NewCaseFolder(testRoot, "extract-positive"), "destination");
			using (SecureExtractionGuard guard = SecureExtractionGuard.CreateForSecurityTest(destination))
			using (MemoryStream stream = new MemoryStream(zipBytes, false))
			{
				SecureProductExtractor.Extract(stream, guard, null);
			}
			Assert(File.ReadAllText(Path.Combine(destination, "folder", "game.txt")) == "verified game data",
				"verified ZIP extracted through protected CreateNew path");

			string conflictParent = NewCaseFolder(testRoot, "extract-conflict");
			string conflictDestination = Path.Combine(conflictParent, "destination");
			using (SecureExtractionGuard guard = SecureExtractionGuard.CreateForSecurityTest(conflictDestination))
			{
				File.WriteAllText(Path.Combine(conflictDestination, "TurboRama.exe"), "existing", Encoding.ASCII);
				ExpectFailure(delegate
				{
					using (MemoryStream stream = new MemoryStream(zipBytes, false))
					{
						SecureProductExtractor.Extract(stream, guard, null);
					}
				}, "existing file/hardlink target is never overwritten");
			}

			byte[] traversalZip = CreateZip(new Dictionary<string, byte[]>
			{
				{ "../escape.bin", new byte[] { 1 } }
			});
			ExpectExtractionFailure(testRoot, "zip-slip", traversalZip, "ZIP traversal rejected");

			byte[] adsZip = CreateZip(new Dictionary<string, byte[]>
			{
				{ "safe.txt:evil", new byte[] { 1 } }
			});
			ExpectExtractionFailure(testRoot, "ads", adsZip, "alternate data stream ZIP path rejected");

			byte[] reservedZip = CreateZip(new Dictionary<string, byte[]>
			{
				{ "CON.txt", new byte[] { 1 } }
			});
			ExpectExtractionFailure(testRoot, "reserved", reservedZip, "reserved Windows device ZIP path rejected");

			string nonEmpty = NewCaseFolder(testRoot, "non-empty-destination");
			File.WriteAllText(Path.Combine(nonEmpty, "existing.txt"), "x", Encoding.ASCII);
			ExpectFailure(delegate { SecureExtractionGuard.ValidateDestinationSelection(nonEmpty); },
				"non-empty destination rejected before worker starts");

			string destinationLinkParent = NewCaseFolder(testRoot, "destination-reparse");
			string realDestination = Path.Combine(destinationLinkParent, "real");
			Directory.CreateDirectory(realDestination);
			string linkDestination = Path.Combine(destinationLinkParent, "link");
			CreateJunction(linkDestination, realDestination);
			ExpectFailure(delegate { SecureExtractionGuard.ValidateDestinationSelection(linkDestination); },
				"destination reparse point rejected");
		}

		private static PackageFixture CreatePackage(string folder, int partCount)
		{
			string setupPath = Path.Combine(folder, "setup.exe");
			byte[] setupBytes = Encoding.ASCII.GetBytes("MZ isolated test setup " + Guid.NewGuid().ToString("N"));
			File.WriteAllBytes(setupPath, setupBytes);

			byte[] logicalBytes = CreateZip(new Dictionary<string, byte[]>
			{
				{ "payload.bin", Encoding.UTF8.GetBytes(new string('A', 8192) + Guid.NewGuid().ToString("N")) }
			});
			List<string> partPaths = new List<string>();
			int offset = 0;
			for (int i = 1; i <= partCount; i++)
			{
				int remaining = logicalBytes.Length - offset;
				int length = i == partCount ? remaining : Math.Max(1, remaining / (partCount - i + 1));
				byte[] partBytes = new byte[length];
				Buffer.BlockCopy(logicalBytes, offset, partBytes, 0, length);
				offset += length;

				string partPath = setupPath + ".pkg." + i.ToString("000");
				File.WriteAllBytes(partPath, partBytes);
				partPaths.Add(partPath);
			}

			string zipLeaf = "turborama-test.zip";
			string sidecarPath = setupPath + ".sha256.txt";
			List<string> lines = new List<string>();
			// Deliberately use a historical order different from the current writer.
			lines.Add(Sha256(logicalBytes) + "  " + zipLeaf);
			lines.Add(Sha256(setupBytes) + "  " + Path.GetFileName(setupPath));
			foreach (string partPath in partPaths)
			{
				lines.Add(Sha256(File.ReadAllBytes(partPath)) + "  " + Path.GetFileName(partPath));
			}
			File.WriteAllText(sidecarPath, string.Join(Environment.NewLine, lines) + Environment.NewLine,
				new UTF8Encoding(false));

			return new PackageFixture(setupPath, sidecarPath, partPaths.ToArray(), zipLeaf, logicalBytes);
		}

		private static byte[] CreateZip(Dictionary<string, byte[]> entries)
		{
			using (MemoryStream result = new MemoryStream())
			{
				using (ZipArchive archive = new ZipArchive(result, ZipArchiveMode.Create, true))
				{
					foreach (KeyValuePair<string, byte[]> item in entries)
					{
						ZipArchiveEntry entry = archive.CreateEntry(item.Key, CompressionLevel.Optimal);
						if (item.Value != null)
						{
							using (Stream output = entry.Open())
							{
								output.Write(item.Value, 0, item.Value.Length);
							}
						}
					}
				}
				return result.ToArray();
			}
		}

		private static void ExpectExtractionFailure(string testRoot, string name, byte[] zipBytes, string message)
		{
			string destination = Path.Combine(NewCaseFolder(testRoot, name), "destination");
			using (SecureExtractionGuard guard = SecureExtractionGuard.CreateForSecurityTest(destination))
			{
				ExpectFailure(delegate
				{
					using (MemoryStream stream = new MemoryStream(zipBytes, false))
					{
						SecureProductExtractor.Extract(stream, guard, null);
					}
				}, message);
			}
		}

		private static void ReplaceSidecarHash(string sidecarPath, string leaf, string replacementHash)
		{
			string[] lines = File.ReadAllLines(sidecarPath, Encoding.UTF8);
			for (int i = 0; i < lines.Length; i++)
			{
				if (lines[i].EndsWith("  " + leaf, StringComparison.OrdinalIgnoreCase))
				{
					lines[i] = replacementHash + "  " + leaf;
				}
			}
			File.WriteAllText(sidecarPath, string.Join(Environment.NewLine, lines) + Environment.NewLine,
				new UTF8Encoding(false));
		}

		private static byte[] ReadAll(Stream stream)
		{
			stream.Position = 0L;
			using (MemoryStream output = new MemoryStream())
			{
				stream.CopyTo(output);
				return output.ToArray();
			}
		}

		private static string Sha256(byte[] bytes)
		{
			using (SHA256 algorithm = SHA256.Create())
			{
				return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty);
			}
		}

		private static string NewCaseFolder(string root, string name)
		{
			string path = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static void CreateJunction(string junctionPath, string targetPath)
		{
			string commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
			if (string.IsNullOrEmpty(commandProcessor))
			{
				commandProcessor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
			}
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = commandProcessor,
				Arguments = "/d /c mklink /J \"" + junctionPath + "\" \"" + targetPath + "\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = Path.GetDirectoryName(junctionPath)
			};
			using (Process process = Process.Start(startInfo))
			{
				string standardOutput = process.StandardOutput.ReadToEnd();
				string standardError = process.StandardError.ReadToEnd();
				process.WaitForExit();
				if (process.ExitCode != 0)
				{
					throw new IOException("Unable to create isolated junction fixture: " + standardOutput + standardError);
				}
			}
		}

		private static void ExpectOpenFailure(string setupPath, string message)
		{
			ExpectFailure(delegate
			{
				using (VerifiedProductPackageStream ignored = ProductPackageSecurity.OpenVerifiedPackage(setupPath))
				{
				}
			}, message);
		}

		private static void ExpectFailure(Action action, string message)
		{
			bool failed = false;
			try
			{
				action();
			}
			catch
			{
				failed = true;
			}
			Assert(failed, message);
		}

		private static void Assert(bool condition, string message)
		{
			if (!condition)
			{
				throw new InvalidOperationException("Assertion failed: " + message);
			}
			_passed++;
			Console.WriteLine("[PASS] " + message);
		}

		private static void TryDeleteDirectory(string path)
		{
			try
			{
				if (Directory.Exists(path))
				{
					DeleteDirectoryWithoutFollowingReparsePoints(new DirectoryInfo(path));
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("[WARN] Unable to remove isolated test directory: " + ex.Message);
			}
		}

		private static void DeleteDirectoryWithoutFollowingReparsePoints(DirectoryInfo directory)
		{
			foreach (FileSystemInfo item in directory.GetFileSystemInfos())
			{
				bool isReparse = (item.Attributes & FileAttributes.ReparsePoint) != 0;
				if (isReparse)
				{
					if ((item.Attributes & FileAttributes.Directory) != 0)
					{
						Directory.Delete(item.FullName, false);
					}
					else
					{
						File.Delete(item.FullName);
					}
					continue;
				}

				DirectoryInfo childDirectory = item as DirectoryInfo;
				if (childDirectory != null)
				{
					DeleteDirectoryWithoutFollowingReparsePoints(childDirectory);
				}
				else
				{
					item.Attributes = FileAttributes.Normal;
					File.Delete(item.FullName);
				}
			}
			directory.Attributes = FileAttributes.Directory;
			directory.Delete(false);
		}

		private sealed class PackageFixture
		{
			internal PackageFixture(string setupPath, string sidecarPath, string[] partPaths,
				string zipLeaf, byte[] logicalBytes)
			{
				this.SetupPath = setupPath;
				this.SidecarPath = sidecarPath;
				this.PartPaths = partPaths;
				this.ZipLeaf = zipLeaf;
				this.LogicalBytes = logicalBytes;
			}

			internal string SetupPath { get; private set; }
			internal string SidecarPath { get; private set; }
			internal string[] PartPaths { get; private set; }
			internal string ZipLeaf { get; private set; }
			internal byte[] LogicalBytes { get; private set; }
		}
	}
}
