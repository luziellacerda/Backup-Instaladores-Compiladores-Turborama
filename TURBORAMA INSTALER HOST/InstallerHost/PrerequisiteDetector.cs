using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace InstallerHost
{
	public static class PrerequisiteDetector
	{
		public static bool IsDotNet35Installed()
		{
			string[] keys =
			{
				@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5",
				@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5\1033"
			};

			foreach (RegistryView view in GetRegistryViews())
			{
				foreach (string keyPath in keys)
				{
					try
					{
						using (RegistryKey key = OpenLocalMachineSubKey(view, keyPath))
						{
							if (key == null)
							{
								continue;
							}

							object install = key.GetValue("Install");
							if (install != null && Convert.ToInt32(install) == 1)
							{
								return true;
							}
						}
					}
					catch
					{
					}
				}
			}

			return false;
		}

		public static bool IsWebView2Installed()
		{
			string[] paths =
			{
				@"C:\Program Files (x86)\Microsoft\EdgeWebView\Application",
				@"C:\Program Files\Microsoft\EdgeWebView\Application"
			};

			foreach (string path in paths)
			{
				if (Directory.Exists(path) && Directory.GetFiles(path, "msedgewebview2.exe", SearchOption.AllDirectories).Length > 0)
				{
					return true;
				}
			}

			string[] registryPaths =
			{
				@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
				@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
			};

			foreach (RegistryView view in GetRegistryViews())
			{
				foreach (string registryPath in registryPaths)
				{
					try
					{
						using (RegistryKey key = OpenLocalMachineSubKey(view, registryPath))
						{
							if (key != null && key.GetValue("pv") != null)
							{
								return true;
							}
						}
					}
					catch
					{
					}
				}
			}

			return false;
		}

		public static bool IsXnaFrameworkInstalled()
		{
			return EnumerateUninstallDisplayNames().Any(displayName =>
				!string.IsNullOrEmpty(displayName) &&
				displayName.IndexOf("Microsoft XNA Framework", StringComparison.OrdinalIgnoreCase) >= 0);
		}

		public static bool IsOpenAlInstalled()
		{
			List<string> candidates = new List<string>();
			foreach (string folder in GetSystemDllSearchFolders())
			{
				candidates.Add(Path.Combine(folder, "OpenAL32.dll"));
			}

			candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenAL", "oalinst.exe"));
			candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenAL", "oalinst.exe"));

			return candidates.Any(File.Exists);
		}

		public static bool IsDotNet48Installed()
		{
			foreach (RegistryView view in GetRegistryViews())
			{
				try
				{
					using (RegistryKey key = OpenLocalMachineSubKey(view, @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
					{
						if (key == null)
						{
							continue;
						}

						object release = key.GetValue("Release");
						if (release != null && Convert.ToInt32(release) >= 528040)
						{
							return true;
						}
					}
				}
				catch
				{
				}
			}

			return false;
		}

		public static bool IsVcRedist2015_2022Installed(string architecture)
		{
			return IsInstalledFromRuntimeKey("SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\" + architecture);
		}

		public static bool IsVCppFullyInstalled()
		{
			return GetMissingLegacyVcRedistVersions().Count == 0 &&
				IsVcRedist2015_2022Installed("x64") &&
				IsVcRedist2015_2022Installed("x86");
		}

		public static List<string> GetMissingLegacyVcRedistVersions()
		{
			List<string> missing = new List<string>();
			string[] legacyVersions = new string[] { "2005", "2008", "2010", "2012", "2013" };

			foreach (string version in legacyVersions)
			{
				foreach (string arch in Architectures)
				{
					if (!IsLegacyVcRedistInstalled(version, arch))
					{
						missing.Add("Visual C++ " + version + " " + arch);
					}
				}
			}

			return missing;
		}

		public static bool IsLegacyVcRedistInstalled(string version, string arch)
		{
			return IsInstalledFromUninstall(version, arch) ||
				IsLegacyVcRuntimeDllPresent(version, arch) ||
				IsLegacyVcSxSPresent(version, arch);
		}

		public static bool WaitForLegacyVcRedistInstalled(string version, string arch, int timeoutMilliseconds)
		{
			int elapsed = 0;
			const int interval = 500;

			while (elapsed < timeoutMilliseconds)
			{
				if (IsLegacyVcRedistInstalled(version, arch))
				{
					return true;
				}

				System.Threading.Thread.Sleep(interval);
				elapsed += interval;
			}

			return IsLegacyVcRedistInstalled(version, arch);
		}

		private static bool IsVersionInstalled(string version, string arch)
		{
			if (version == "2015_2022")
			{
				return IsVcRedist2015_2022Installed(arch);
			}

			return IsLegacyVcRedistInstalled(version, arch);
		}

		private static bool IsInstalledFromRuntimeKey(string path)
		{
			foreach (RegistryView registryView in GetRegistryViews())
			{
				using (RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
				{
					using (RegistryKey registryKey2 = registryKey.OpenSubKey(path, RegistryKeyPermissionCheck.ReadSubTree))
					{
						if (((int?)((registryKey2 != null) ? registryKey2.GetValue("Installed") : null)).GetValueOrDefault() == 1)
						{
							return true;
						}
					}
				}
			}

			return false;
		}

		private static bool IsInstalledFromUninstall(string version, string arch)
		{
			foreach (string displayName in EnumerateUninstallDisplayNames(GetPreferredRegistryViews(arch)))
			{
				if (MatchesLegacyVcRedistDisplayName(displayName, version, arch))
				{
					return true;
				}
			}

			return false;
		}

		private static IEnumerable<string> EnumerateUninstallDisplayNames()
		{
			return EnumerateUninstallDisplayNames(GetRegistryViews());
		}

		private static IEnumerable<string> EnumerateUninstallDisplayNames(IEnumerable<RegistryView> views)
		{
			List<string> displayNames = new List<string>();
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string[] uninstallRoots =
			{
				@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
				@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
			};

			foreach (RegistryView view in views)
			{
				foreach (string root in uninstallRoots)
				{
					CollectUninstallDisplayNames(view, root, seen, displayNames);
				}
			}

			return displayNames;
		}

		private static void CollectUninstallDisplayNames(RegistryView view, string root, HashSet<string> seen, List<string> displayNames)
		{
			try
			{
				using (RegistryKey registryKey = OpenLocalMachineSubKey(view, root))
				{
					if (registryKey == null)
					{
						return;
					}

					foreach (string subKeyName in registryKey.GetSubKeyNames())
					{
						using (RegistryKey subKey = registryKey.OpenSubKey(subKeyName))
						{
							object displayValue = subKey != null ? subKey.GetValue("DisplayName") : null;
							string displayName = displayValue != null ? displayValue.ToString() : null;
							if (!string.IsNullOrEmpty(displayName) && seen.Add(displayName))
							{
								displayNames.Add(displayName);
							}
						}
					}
				}
			}
			catch
			{
			}
		}

		private static bool MatchesLegacyVcRedistDisplayName(string displayName, string version, string arch)
		{
			if (string.IsNullOrEmpty(displayName))
			{
				return false;
			}

			if (displayName.IndexOf("Microsoft Visual C++", StringComparison.OrdinalIgnoreCase) < 0)
			{
				return false;
			}

			if (displayName.IndexOf(version, StringComparison.OrdinalIgnoreCase) < 0)
			{
				return false;
			}

			bool mentionsX64 = displayName.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0;
			bool mentionsX86 = displayName.IndexOf("x86", StringComparison.OrdinalIgnoreCase) >= 0;

			if (string.Equals(arch, "x64", StringComparison.OrdinalIgnoreCase))
			{
				return mentionsX64;
			}

			if (mentionsX64)
			{
				return false;
			}

			return mentionsX86 || string.Equals(version, "2005", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsLegacyVcRuntimeDllPresent(string version, string arch)
		{
			string dllName;
			switch (version)
			{
				case "2005":
					dllName = "msvcr80.dll";
					break;
				case "2008":
					dllName = "msvcr90.dll";
					break;
				case "2010":
					dllName = "msvcr100.dll";
					break;
				case "2012":
					dllName = "msvcr110.dll";
					break;
				case "2013":
					dllName = "msvcr120.dll";
					break;
				default:
					return false;
			}

			string folder = GetSystemDirectoryForArchitecture(arch);
			return !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, dllName));
		}

		private static bool IsLegacyVcSxSPresent(string version, string arch)
		{
			if (!(version == "2005") && !(version == "2008"))
			{
				return false;
			}

			try
			{
				string winSxS = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS");
				if (!Directory.Exists(winSxS))
				{
					return false;
				}

				string prefix;
				if (version == "2005")
				{
					prefix = string.Equals(arch, "x64", StringComparison.OrdinalIgnoreCase)
						? "amd64_microsoft.vc80.crt"
						: "x86_microsoft.vc80.crt";
				}
				else
				{
					prefix = string.Equals(arch, "x64", StringComparison.OrdinalIgnoreCase)
						? "amd64_microsoft.vc90.crt"
						: "x86_microsoft.vc90.crt";
				}

				return Directory.GetDirectories(winSxS, prefix + "_*").Length > 0;
			}
			catch
			{
				return false;
			}
		}

		public static bool IsDirectXJun2010Installed()
		{
			string[] dllNames = new string[] { "d3dx9_43.dll", "XInput1_3.dll" };
			foreach (string folder in GetSystemDllSearchFolders())
			{
				foreach (string dllName in dllNames)
				{
					if (File.Exists(Path.Combine(folder, dllName)))
					{
						return true;
					}
				}
			}

			foreach (RegistryView view in GetRegistryViews())
			{
				try
				{
					using (RegistryKey registryKey = OpenLocalMachineSubKey(view, @"SOFTWARE\Microsoft\DirectX"))
					{
						if (registryKey == null)
						{
							continue;
						}

						object value = registryKey.GetValue("Version");
						if (!string.IsNullOrEmpty((value != null) ? value.ToString() : null))
						{
							return true;
						}
					}
				}
				catch
				{
				}
			}

			return false;
		}

		public static bool IsDokanyInstalled()
		{
			string[] serviceKeys = new string[]
			{
				@"SYSTEM\CurrentControlSet\Services\dokan1",
				@"SYSTEM\CurrentControlSet\Services\dokan2"
			};

			foreach (RegistryView view in GetRegistryViews())
			{
				foreach (string serviceKey in serviceKeys)
				{
					try
					{
						using (RegistryKey registryKey = OpenLocalMachineSubKey(view, serviceKey))
						{
							if (registryKey != null)
							{
								return true;
							}
						}
					}
					catch
					{
					}
				}
			}

			return false;
		}

		public static bool IsWinFspInstalled()
		{
			string[] candidates =
			{
				@"C:\Program Files (x86)\WinFsp\bin\winfsp-x64.dll",
				@"C:\Program Files (x86)\WinFsp\bin\winfsp-x86.dll",
				@"C:\Program Files\WinFsp\bin\winfsp-x64.dll",
				@"C:\Program Files\WinFsp\bin\winfsp-x86.dll"
			};

			return candidates.Any(File.Exists);
		}

		private static RegistryView[] GetRegistryViews()
		{
			if (Environment.Is64BitOperatingSystem)
			{
				return new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 };
			}

			return new RegistryView[] { RegistryView.Registry32 };
		}

		private static RegistryView[] GetPreferredRegistryViews(string arch)
		{
			if (!Environment.Is64BitOperatingSystem)
			{
				return new RegistryView[] { RegistryView.Registry32 };
			}

			if (string.Equals(arch, "x64", StringComparison.OrdinalIgnoreCase))
			{
				return new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 };
			}

			return new RegistryView[] { RegistryView.Registry32, RegistryView.Registry64 };
		}

		private static RegistryKey OpenLocalMachineSubKey(RegistryView view, string subKeyPath)
		{
			RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
			return baseKey.OpenSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadSubTree);
		}

		private static string GetSystemDirectoryForArchitecture(string arch)
		{
			string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

			if (string.Equals(arch, "x64", StringComparison.OrdinalIgnoreCase))
			{
				if (Environment.Is64BitOperatingSystem)
				{
					if (Environment.Is64BitProcess)
					{
						return Environment.GetFolderPath(Environment.SpecialFolder.System);
					}

					string sysnative = Path.Combine(windows, "Sysnative");
					if (Directory.Exists(sysnative))
					{
						return sysnative;
					}

					return Path.Combine(windows, "System32");
				}

				return Environment.GetFolderPath(Environment.SpecialFolder.System);
			}

			if (Environment.Is64BitOperatingSystem)
			{
				return Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
			}

			return Environment.GetFolderPath(Environment.SpecialFolder.System);
		}

		private static IEnumerable<string> GetSystemDllSearchFolders()
		{
			HashSet<string> folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
			string system32 = Path.Combine(windows, "System32");
			string syswow64 = Path.Combine(windows, "SysWOW64");
			string sysnative = Path.Combine(windows, "Sysnative");

			if (Directory.Exists(system32))
			{
				folders.Add(system32);
			}

			if (Directory.Exists(syswow64))
			{
				folders.Add(syswow64);
			}

			if (!Environment.Is64BitProcess && Environment.Is64BitOperatingSystem && Directory.Exists(sysnative))
			{
				folders.Add(sysnative);
			}

			string processSystem = Environment.GetFolderPath(Environment.SpecialFolder.System);
			if (!string.IsNullOrEmpty(processSystem))
			{
				folders.Add(processSystem);
			}

			string processSystemX86 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
			if (!string.IsNullOrEmpty(processSystemX86))
			{
				folders.Add(processSystemX86);
			}

			return folders;
		}

		private static readonly string[] Architectures = new string[] { "x86", "x64" };

		private static readonly string[] Versions = new string[] { "2005", "2008", "2010", "2012", "2013", "2015_2022" };
	}
}