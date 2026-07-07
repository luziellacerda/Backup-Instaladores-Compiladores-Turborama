using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace InstallerHost
{
	// Token: 0x0200000A RID: 10
	public static class PrerequisiteDetector
	{
		// Token: 0x06000034 RID: 52 RVA: 0x000044C8 File Offset: 0x000026C8
		public static bool IsDotNet35Installed()
		{
			try
			{
				using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5"))
				{
					if (key != null)
					{
						object install = key.GetValue("Install");
						if (install != null && Convert.ToInt32(install) == 1)
						{
							return true;
						}
					}
				}

				using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5\1033"))
				{
					if (key != null)
					{
						object install = key.GetValue("Install");
						if (install != null && Convert.ToInt32(install) == 1)
						{
							return true;
						}
					}
				}
			}
			catch
			{
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

			try
			{
				using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"))
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

			return false;
		}

		public static bool IsXnaFrameworkInstalled()
		{
			string[] uninstallRoots =
			{
				@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
				@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
			};

			foreach (string root in uninstallRoots)
			{
				using (RegistryKey key = Registry.LocalMachine.OpenSubKey(root))
				{
					if (key == null)
					{
						continue;
					}

					foreach (string subKeyName in key.GetSubKeyNames())
					{
						using (RegistryKey subKey = key.OpenSubKey(subKeyName))
						{
							string displayName = subKey != null && subKey.GetValue("DisplayName") != null
								? subKey.GetValue("DisplayName").ToString()
								: string.Empty;

							if (!string.IsNullOrEmpty(displayName) &&
								displayName.IndexOf("Microsoft XNA Framework", StringComparison.OrdinalIgnoreCase) >= 0)
							{
								return true;
							}
						}
					}
				}
			}

			return false;
		}

		public static bool IsOpenAlInstalled()
		{
			string[] candidates =
			{
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenAL32.dll"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "OpenAL32.dll"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenAL", "oalinst.exe")
			};

			return candidates.Any(File.Exists);
		}

		public static bool IsDotNet48Installed()
		{
			try
			{
				using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
				{
					if (key == null)
					{
						return false;
					}

					object release = key.GetValue("Release");
					if (release == null)
					{
						return false;
					}

					return Convert.ToInt32(release) >= 528040;
				}
			}
			catch
			{
				return false;
			}
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

		public static System.Collections.Generic.List<string> GetMissingLegacyVcRedistVersions()
		{
			System.Collections.Generic.List<string> missing = new System.Collections.Generic.List<string>();
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

		// Token: 0x06000035 RID: 53 RVA: 0x00004518 File Offset: 0x00002718
		private static bool IsVersionInstalled(string version, string arch)
		{
			if (version == "2015_2022")
			{
				return IsVcRedist2015_2022Installed(arch);
			}

			return IsLegacyVcRedistInstalled(version, arch);
		}

		// Token: 0x06000036 RID: 54 RVA: 0x00004590 File Offset: 0x00002790
		private static bool IsInstalledFromRuntimeKey(string path)
		{
			foreach (RegistryView registryView in new RegistryView[]
			{
				RegistryView.Registry64,
				RegistryView.Registry32
			})
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

		// Token: 0x06000037 RID: 55 RVA: 0x00004640 File Offset: 0x00002840
		private static bool IsInstalledFromUninstall(string version, string arch)
		{
			string[] uninstallRoots = new string[]
			{
				"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
				"SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
			};

			foreach (string root in uninstallRoots)
			{
				using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(root, RegistryKeyPermissionCheck.ReadSubTree))
				{
					if (registryKey == null)
					{
						continue;
					}

					foreach (string subKeyName in registryKey.GetSubKeyNames())
					{
						using (RegistryKey subKey = registryKey.OpenSubKey(subKeyName))
						{
							object displayValue = subKey != null ? subKey.GetValue("DisplayName") : null;
							string displayName = displayValue != null ? displayValue.ToString() : null;
							if (MatchesLegacyVcRedistDisplayName(displayName, version, arch))
							{
								return true;
							}
						}
					}
				}
			}

			return false;
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

			string folder = string.Equals(arch, "x64", StringComparison.OrdinalIgnoreCase)
				? Environment.GetFolderPath(Environment.SpecialFolder.System)
				: Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);

			return File.Exists(Path.Combine(folder, dllName));
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

		// Token: 0x06000038 RID: 56 RVA: 0x00004758 File Offset: 0x00002958
		public static bool IsDirectXJun2010Installed()
		{
			string[] dllCandidates = new string[]
			{
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "d3dx9_43.dll"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "d3dx9_43.dll"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "XInput1_3.dll"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "XInput1_3.dll")
			};

			if (dllCandidates.Any(File.Exists))
			{
				return true;
			}

			try
			{
				using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\DirectX", RegistryKeyPermissionCheck.ReadSubTree))
				{
					if (registryKey == null)
					{
						return false;
					}

					object value = registryKey.GetValue("Version");
					return !string.IsNullOrEmpty((value != null) ? value.ToString() : null);
				}
			}
			catch
			{
				return false;
			}
		}

		// Token: 0x06000039 RID: 57 RVA: 0x000047CC File Offset: 0x000029CC
		public static bool IsDokanyInstalled()
		{
			foreach (string text in new string[] { "SYSTEM\\CurrentControlSet\\Services\\dokan1", "SYSTEM\\CurrentControlSet\\Services\\dokan2" })
			{
				using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(text, RegistryKeyPermissionCheck.ReadSubTree))
				{
					if (registryKey != null)
					{
						return true;
					}
				}
			}
			return false;
		}

		// Token: 0x0600003A RID: 58 RVA: 0x00004838 File Offset: 0x00002A38
		public static bool IsWinFspInstalled()
		{
			string text = "C:\\Program Files (x86)\\WinFsp\\bin\\winfsp-x64.dll";
			string text2 = "C:\\Program Files (x86)\\WinFsp\\bin\\winfsp-x86.dll";
			return File.Exists(text) || File.Exists(text2);
		}

		// Token: 0x04000032 RID: 50
		private static readonly string[] Architectures = new string[] { "x86", "x64" };

		// Token: 0x04000033 RID: 51
		private static readonly string[] Versions = new string[] { "2005", "2008", "2010", "2012", "2013", "2015_2022" };
	}
}
