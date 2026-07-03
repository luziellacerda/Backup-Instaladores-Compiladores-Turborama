using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace InstallerHost
{
	public static class NvidiaAppInstallerHelper
	{
		public static bool HasNvidiaGpu()
		{
			try
			{
				using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility, PNPDeviceID FROM Win32_VideoController"))
				{
					foreach (ManagementObject obj in searcher.Get())
					{
						string name = SafeGet(obj, "Name");
						string adapter = SafeGet(obj, "AdapterCompatibility");
						string pnp = SafeGet(obj, "PNPDeviceID");

						string text = (name + " " + adapter + " " + pnp).ToLowerInvariant();

						if (text.Contains("nvidia") || text.Contains("ven_10de"))
						{
							Logger.Log("NVIDIA GPU detected: " + name);
							return true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to detect NVIDIA GPU: " + ex.ToString());
			}

			return false;
		}

		public static void InstallOrOpenNvidiaApp()
		{
			if (!HasNvidiaGpu())
			{
				MessageBox.Show(
					"NVIDIA GPU was not detected on this computer.",
					"NVIDIA App",
					MessageBoxButtons.OK,
					MessageBoxIcon.Information
				);
				return;
			}

			try
			{
				DownloadAndRunNvidiaAppInstaller();
			}
			catch (Exception ex)
			{
				Logger.Log("NVIDIA App installer auto-download failed: " + ex.ToString());

				MessageBox.Show(
					"Could not automatically download the NVIDIA App installer." + Environment.NewLine + Environment.NewLine +
					"The official NVIDIA App page will be opened instead.",
					"NVIDIA App",
					MessageBoxButtons.OK,
					MessageBoxIcon.Information
				);

				OpenUrl("https://www.nvidia.com/pt-br/software/nvidia-app/");
			}
		}

		private static string SafeGet(ManagementObject obj, string propertyName)
		{
			try
			{
				object value = obj[propertyName];
				return value == null ? string.Empty : value.ToString();
			}
			catch
			{
				return string.Empty;
			}
		}

		private static void DownloadAndRunNvidiaAppInstaller()
		{
			ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

			string pageUrl = "https://www.nvidia.com/pt-br/software/nvidia-app/";
			string html;

			using (WebClient client = CreateWebClient())
			{
				Logger.Log("Downloading NVIDIA App page: " + pageUrl);
				html = client.DownloadString(pageUrl);
			}

			string installerUrl = FindNvidiaAppInstallerUrl(html);

			if (string.IsNullOrWhiteSpace(installerUrl))
			{
				throw new Exception("NVIDIA App installer URL not found on official page.");
			}

			string installerPath = Path.Combine(Path.GetTempPath(), "NVIDIA_App_Installer.exe");

			if (File.Exists(installerPath))
			{
				try
				{
					File.Delete(installerPath);
				}
				catch
				{
				}
			}

			using (WebClient client = CreateWebClient())
			{
				Logger.Log("Downloading NVIDIA App installer: " + installerUrl);
				client.DownloadFile(installerUrl, installerPath);
			}

			if (!File.Exists(installerPath) || new FileInfo(installerPath).Length < 1024L * 1024L)
			{
				throw new Exception("Downloaded NVIDIA App installer is missing or too small.");
			}

			Logger.Log("Launching NVIDIA App installer: " + installerPath);

			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = installerPath,
				UseShellExecute = true
			};

			Process.Start(startInfo);
		}

		private static WebClient CreateWebClient()
		{
			WebClient client = new WebClient();
			client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) TurboramaInstaller");
			client.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
			return client;
		}

		private static string FindNvidiaAppInstallerUrl(string html)
		{
			if (string.IsNullOrWhiteSpace(html))
			{
				return string.Empty;
			}

			MatchCollection matches = Regex.Matches(
				html,
				@"https?:\\?/\\?/[^'""<>\\ ]+?\.exe",
				RegexOptions.IgnoreCase
			);

			foreach (Match match in matches)
			{
				string url = match.Value.Replace("\\/", "/").Replace("&amp;", "&");
				string lower = url.ToLowerInvariant();

				if ((lower.Contains("nvidia") || lower.Contains("nvapp")) &&
					(lower.Contains("app") || lower.Contains("nvidia_app") || lower.Contains("nvidia-app")) &&
					lower.EndsWith(".exe"))
				{
					return url;
				}
			}

			return string.Empty;
		}

		private static void OpenUrl(string url)
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = url,
				UseShellExecute = true
			};

			Process.Start(startInfo);
			Logger.Log("Opened URL: " + url);
		}
	}
}
