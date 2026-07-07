using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RetroBuild
{
	internal static class ArchiveOutputHelper
	{
		public static string GetZipFileName(BuilderOptions options)
		{
			return string.Concat(new string[]
			{
				"lz-turbo-v",
				options.RetrobatVersion,
				"-",
				options.Branch,
				"-",
				options.Architecture,
				".zip"
			});
		}

		public static string GetSetupFileName(BuilderOptions options)
		{
			return string.Concat(new string[]
			{
				"LZ-TURBO-v",
				options.RetrobatVersion,
				"-",
				options.Branch,
				"-",
				options.Architecture,
				"-setup.exe"
			});
		}

		public static string ResolveOutputDirectory(BuilderOptions options, string retroBuildDirectory, string buildFolderPath)
		{
			if (!string.IsNullOrWhiteSpace(options.ArchiveOutputDirectory))
			{
				return options.ArchiveOutputDirectory;
			}

			if (!options.AskArchiveOutputDrive && !string.IsNullOrWhiteSpace(options.ArchiveOutputPath))
			{
				options.ArchiveOutputDirectory = EnsureOutputDirectory(options.ArchiveOutputPath);
				return options.ArchiveOutputDirectory;
			}

			options.ArchiveOutputDirectory = PromptForOutputDirectory(retroBuildDirectory, buildFolderPath, options.ArchiveOutputPath);
			return options.ArchiveOutputDirectory;
		}

		public static string FindZipArchivePath(BuilderOptions options, string retroBuildDirectory)
		{
			string zipFileName = GetZipFileName(options);
			List<string> candidates = new List<string>();

			if (!string.IsNullOrWhiteSpace(options.ArchiveOutputDirectory))
			{
				candidates.Add(Path.Combine(options.ArchiveOutputDirectory, zipFileName));
			}

			if (!string.IsNullOrWhiteSpace(options.ArchiveOutputPath))
			{
				candidates.Add(Path.Combine(EnsureOutputDirectory(options.ArchiveOutputPath), zipFileName));
			}

			candidates.Add(Path.Combine(retroBuildDirectory, zipFileName));

			foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}

			return null;
		}

		private static string PromptForOutputDirectory(string retroBuildDirectory, string buildFolderPath, string defaultPathFromIni)
		{
			string buildRoot = Path.GetPathRoot(Path.GetFullPath(buildFolderPath)) ?? string.Empty;
			string retroRoot = Path.GetPathRoot(Path.GetFullPath(retroBuildDirectory)) ?? string.Empty;

			List<DriveInfo> drives = DriveInfo.GetDrives()
				.Where(d => d.IsReady && d.DriveType != DriveType.CDRom)
				.OrderBy(d => d.Name)
				.ToList();

			LzGamesConsoleUi.ShowDriveSelectionHeader(buildFolderPath, buildRoot);
			LzGamesConsoleUi.ShowDriveList(retroBuildDirectory, retroRoot, drives, buildRoot, defaultPathFromIni);
			Console.Write("  Destino: ");

			string answer = Console.ReadLine();
			if (answer == null)
			{
				answer = string.Empty;
			}

			answer = answer.Trim().Trim('"');
			string selectedDirectory = retroBuildDirectory;

			if (answer.Length == 0)
			{
				selectedDirectory = retroBuildDirectory;
			}
			else if (int.TryParse(answer, out int index))
			{
				if (index == 0)
				{
					selectedDirectory = retroBuildDirectory;
				}
				else if (index >= 1 && index <= drives.Count)
				{
					selectedDirectory = drives[index - 1].Name;
				}
				else
				{
					LzGamesConsoleUi.Warning("Numero invalido. Usando pasta do RetroBuild.");
					selectedDirectory = retroBuildDirectory;
				}
			}
			else if ((answer.Length <= 3 && answer.IndexOf(':') >= 0) || answer.Length == 1)
			{
				if (!answer.EndsWith("\\", StringComparison.Ordinal) && !answer.EndsWith(":", StringComparison.Ordinal))
				{
					answer += ":";
				}
				if (!answer.EndsWith("\\", StringComparison.Ordinal))
				{
					answer += "\\";
				}
				selectedDirectory = answer;
			}
			else
			{
				selectedDirectory = answer;
			}

			selectedDirectory = EnsureOutputDirectory(selectedDirectory);
			string selectedRoot = Path.GetPathRoot(selectedDirectory) ?? string.Empty;

			LzGamesConsoleUi.ShowSelectedDestination(selectedDirectory, buildRoot, selectedRoot);

			Logger.LogInfo("Archive output directory selected: " + selectedDirectory);
			return selectedDirectory;
		}

		private static string EnsureOutputDirectory(string path)
		{
			string fullPath = Path.GetFullPath(path);
			if (!Directory.Exists(fullPath))
			{
				Directory.CreateDirectory(fullPath);
			}
			return fullPath;
		}

		private static string GetDriveRoot(string driveName)
		{
			if (string.IsNullOrWhiteSpace(driveName))
			{
				return string.Empty;
			}

			if (!driveName.EndsWith("\\", StringComparison.Ordinal))
			{
				return driveName + "\\";
			}

			return driveName;
		}

		private static string DescribeDrive(string driveRoot, List<DriveInfo> drives)
		{
			if (string.IsNullOrWhiteSpace(driveRoot))
			{
				return string.Empty;
			}

			DriveInfo drive = drives.FirstOrDefault(d => string.Equals(GetDriveRoot(d.Name), driveRoot, StringComparison.OrdinalIgnoreCase));
			if (drive == null)
			{
				return string.Empty;
			}

			return " | Livre: " + FormatBytes(drive.AvailableFreeSpace);
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
	}
}