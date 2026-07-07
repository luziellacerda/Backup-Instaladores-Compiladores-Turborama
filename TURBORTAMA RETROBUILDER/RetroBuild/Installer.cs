using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace RetroBuild
{
	internal class Installer
	{
		// Cada parte fica com aproximadamente 1900 MB.
		// Pode mudar para 1024 MB, 2000 MB etc., se quiser partes menores/maiores.
		private const long SplitPartSizeBytes = 1900L * 1024L * 1024L;

		public static void CreateInstaller(BuilderOptions options)
		{
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			string buildFolder = Path.Combine(baseDirectory, "build");
			string outputDirectory = ArchiveOutputHelper.ResolveOutputDirectory(options, baseDirectory, buildFolder);
			string zipPath = ArchiveOutputHelper.FindZipArchivePath(options, baseDirectory);

			if (string.IsNullOrEmpty(zipPath))
			{
				Logger.Log("[INFO] zip not found, creating zip first.");
				try
				{
					Program.CreateZipFolderSharpZip(options);
				}
				catch (Exception ex)
				{
					Logger.Log("[ERROR] Exception creating ZIP: " + ex.Message);
				}

				zipPath = ArchiveOutputHelper.FindZipArchivePath(options, baseDirectory);
			}

			if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
			{
				Logger.Log("[ERROR] No .zip file found for installer creation.");
				return;
			}

			Logger.LogInfo("Found zip file: " + zipPath);

			string installerHostPath = Path.Combine(baseDirectory, "InstallerHost.exe");
			if (!File.Exists(installerHostPath))
			{
				Logger.Log("[ERROR] InstallerHost.exe not found at: " + installerHostPath);
				return;
			}

			Logger.LogInfo("Found InstallerHost.exe at: " + installerHostPath);

			try
			{
				string setupFileName = ArchiveOutputHelper.GetSetupFileName(options);
				string setupPath = Path.Combine(outputDirectory, setupFileName);

				DeleteOldSplitParts(setupPath);

				if (File.Exists(setupPath))
				{
					File.Delete(setupPath);
				}

				// SPLIT: o setup fica pequeno. O ZIP NÃO é anexado dentro do EXE.
				File.Copy(installerHostPath, setupPath, true);
				Logger.LogInfo("Created small installer executable: " + setupPath);

				List<string> parts = SplitFile(zipPath, setupPath + ".pkg", SplitPartSizeBytes);
				WriteSha256List(setupPath, zipPath, parts);

				LzGamesConsoleUi.Success("Instalador criado com sucesso:");
				LzGamesConsoleUi.Info(setupPath);
				foreach (string part in parts)
				{
					LzGamesConsoleUi.Info(part);
				}
				LzGamesConsoleUi.Warning("Mantenha o .exe e todos os .pkg.### na mesma pasta.");
				Logger.LogInfo("Created split installer package:");
				Logger.LogInfo(" - " + setupPath);
				foreach (string part in parts)
				{
					Logger.LogInfo(" - " + part);
				}
				Logger.LogInfo("Keep the .exe and all .pkg.### files together in the same folder.");
			}
			catch (Exception ex)
			{
				Logger.Log("[ERROR] Exception creating split installer: " + ex.Message);
			}
		}

		private static string FormatBytes(long bytes)
		{
			string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
			double value = bytes;
			int unit = 0;
			while (value >= 1024.0 && unit < units.Length - 1)
			{
				value /= 1024.0;
				unit++;
			}
			return string.Format("{0:0.00} {1}", value, units[unit]);
		}

		private static string ShortConsolePath(string path, int maxLength)
		{
			if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
			{
				return path;
			}
			if (maxLength <= 3)
			{
				return path.Substring(0, maxLength);
			}
			return "..." + path.Substring(path.Length - (maxLength - 3));
		}

		private static string FormatRemainingTime(TimeSpan time)
		{
			int totalHours = (int)Math.Floor(time.TotalHours);
			return string.Format("{0:D2}:{1:D2}:{2:D2}", totalHours, time.Minutes, time.Seconds);
		}

		private static void PrintSplitProgress(long processedBytes, long totalBytes, DateTime startTime, int partNumber, string currentPart)
		{
			int percent = totalBytes > 0L ? (int)(processedBytes * 100L / totalBytes) : 100;
			TimeSpan elapsed = DateTime.Now - startTime;
			double speed = elapsed.TotalSeconds > 0.0 ? processedBytes / elapsed.TotalSeconds : 0.0;
			long remainingBytes = Math.Max(0L, totalBytes - processedBytes);
			TimeSpan remaining = speed > 0.0 ? TimeSpan.FromSeconds(remainingBytes / speed) : TimeSpan.Zero;
			int barWidth = Math.Max(10, Math.Min(34, LzGamesConsoleUi.GetSafeConsoleWidth() - 95));

			string line = string.Format(
				"{0,3}% {1} {2} / {3} | Vel {4}/s | ETA {5} | Parte {6:000}",
				percent,
				LzGamesConsoleUi.BuildProgressBar(percent, barWidth),
				FormatBytes(processedBytes),
				FormatBytes(totalBytes),
				FormatBytes((long)speed),
				FormatRemainingTime(remaining),
				partNumber
			);

			LzGamesConsoleUi.WriteProgressLine(line);
		}

		private static List<string> SplitFile(string sourceFilePath, string outputBasePath, long partSizeBytes)
		{
			List<string> parts = new List<string>();

			byte[] buffer = new byte[1024 * 1024];
			int partNumber = 1;
			long processedBytes = 0L;
			DateTime startTime = DateTime.Now;

			using (FileStream input = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				long totalBytes = input.Length;
				LzGamesConsoleUi.ShowInstallerSplitHeader(totalBytes, partSizeBytes);

				while (input.Position < input.Length)
				{
					string partPath = outputBasePath + "." + partNumber.ToString("000");
					long bytesRemainingInPart = partSizeBytes;

					using (FileStream output = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
					{
						while (bytesRemainingInPart > 0 && input.Position < input.Length)
						{
							int bytesToRead = (int)Math.Min(buffer.Length, bytesRemainingInPart);
							int bytesRead = input.Read(buffer, 0, bytesToRead);
							if (bytesRead <= 0)
							{
								break;
							}

							output.Write(buffer, 0, bytesRead);
							bytesRemainingInPart -= bytesRead;
							processedBytes += bytesRead;
							PrintSplitProgress(processedBytes, totalBytes, startTime, partNumber, partPath);
						}
					}

					Console.WriteLine();
					parts.Add(partPath);
					Logger.LogInfo("Created package part: " + partPath);
					LzGamesConsoleUi.Success("Parte criada: " + partPath);
					partNumber++;
				}
			}

			return parts;
		}

		private static void DeleteOldSplitParts(string setupPath)
		{
			string folder = Path.GetDirectoryName(setupPath);
			string fileName = Path.GetFileName(setupPath);

			if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(fileName) || !Directory.Exists(folder))
			{
				return;
			}

			foreach (string oldPart in Directory.GetFiles(folder, fileName + ".pkg.*"))
			{
				try
				{
					File.Delete(oldPart);
					Logger.LogInfo("Deleted old package part: " + oldPart);
				}
				catch (Exception ex)
				{
					Logger.Log("[WARNING] Could not delete old package part " + oldPart + ": " + ex.Message);
				}
			}
		}

		private static void WriteSha256List(string setupPath, string zipPath, List<string> parts)
		{
			string shaFile = setupPath + ".sha256.txt";

			using (StreamWriter writer = new StreamWriter(shaFile, false))
			{
				writer.WriteLine(ComputeSha256(setupPath) + "  " + Path.GetFileName(setupPath));
				foreach (string part in parts)
				{
					writer.WriteLine(ComputeSha256(part) + "  " + Path.GetFileName(part));
				}

				// Hash do ZIP original, útil para conferir o pacote antes do split.
				writer.WriteLine(ComputeSha256(zipPath) + "  " + Path.GetFileName(zipPath));
			}
		}

		private static string ComputeSha256(string filePath)
		{
			using (FileStream stream = File.OpenRead(filePath))
			{
				using (SHA256 sha = SHA256.Create())
				{
					return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
				}
			}
		}
	}
}
