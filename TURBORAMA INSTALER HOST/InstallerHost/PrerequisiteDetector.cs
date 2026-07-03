using System;
using System.IO;
using Microsoft.Win32;

namespace InstallerHost
{
	// Token: 0x0200000A RID: 10
	public static class PrerequisiteDetector
	{
		// Token: 0x06000034 RID: 52 RVA: 0x000044C8 File Offset: 0x000026C8
		public static bool IsVCppFullyInstalled()
		{
			foreach (string text in PrerequisiteDetector.Versions)
			{
				foreach (string text2 in PrerequisiteDetector.Architectures)
				{
					if (!PrerequisiteDetector.IsVersionInstalled(text, text2))
					{
						return false;
					}
				}
			}
			return true;
		}

		// Token: 0x06000035 RID: 53 RVA: 0x00004518 File Offset: 0x00002718
		private static bool IsVersionInstalled(string version, string arch)
		{
			if (!(version == "2005") && !(version == "2008") && !(version == "2010") && !(version == "2012") && !(version == "2013"))
			{
				return version == "2015_2022" && PrerequisiteDetector.IsInstalledFromRuntimeKey("SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\" + arch);
			}
			return PrerequisiteDetector.IsInstalledFromUninstall(version, arch);
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
			string[] array = new string[] { "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall" };
			if (version == "2005")
			{
				version = "";
			}
			foreach (string text in array)
			{
				using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(text, RegistryKeyPermissionCheck.ReadSubTree))
				{
					if (registryKey != null)
					{
						foreach (string text2 in registryKey.GetSubKeyNames())
						{
							using (RegistryKey registryKey2 = registryKey.OpenSubKey(text2))
							{
								string text3;
								if (registryKey2 == null)
								{
									text3 = null;
								}
								else
								{
									object value = registryKey2.GetValue("DisplayName");
									text3 = ((value != null) ? value.ToString() : null);
								}
								string text4 = text3;
								if (!string.IsNullOrEmpty(text4) && text4.Contains("Microsoft Visual C++ " + version) && text4.Contains(arch))
								{
									return true;
								}
							}
						}
					}
				}
			}
			return false;
		}

		// Token: 0x06000038 RID: 56 RVA: 0x00004758 File Offset: 0x00002958
		public static bool IsDirectXJun2010Installed()
		{
			bool flag;
			try
			{
				using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\DirectX", RegistryKeyPermissionCheck.ReadSubTree))
				{
					if (registryKey == null)
					{
						flag = false;
					}
					else
					{
						object value = registryKey.GetValue("Version");
						flag = !string.IsNullOrEmpty((value != null) ? value.ToString() : null);
					}
				}
			}
			catch
			{
				flag = false;
			}
			return flag;
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
