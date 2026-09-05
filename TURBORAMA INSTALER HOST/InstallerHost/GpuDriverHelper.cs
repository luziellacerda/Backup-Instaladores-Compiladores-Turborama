using System;
using System.Collections.Generic;
using System.Management;
using System.Text;
using System.Windows.Forms;

namespace InstallerHost
{
	public static class GpuDriverHelper
	{
		private static bool alreadyPrompted = false;

		private class GpuInfo
		{
			public string Name;
			public string Vendor;
			public string Url;
		}

		public static void AskAndOpenOfficialDriverPage()
		{
			if (alreadyPrompted)
			{
				return;
			}
			alreadyPrompted = true;

			List<GpuInfo> gpus = DetectVideoControllers();
			if (gpus.Count == 0)
			{
				return;
			}

			Dictionary<string, string> vendorUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (GpuInfo gpu in gpus)
			{
				if (!string.IsNullOrEmpty(gpu.Url) && !vendorUrls.ContainsKey(gpu.Vendor))
				{
					vendorUrls.Add(gpu.Vendor, gpu.Url);
				}
			}

			if (vendorUrls.Count == 0)
			{
				return;
			}

			StringBuilder message = new StringBuilder();
			message.AppendLine("Placa(s) de video detectada(s):");
			message.AppendLine();

			foreach (GpuInfo gpu in gpus)
			{
				message.AppendLine("- " + gpu.Name + " [" + gpu.Vendor + "]");
			}

			message.AppendLine();
			message.AppendLine("Deseja copiar os links oficiais para abrir no navegador depois de fechar o instalador?");
			message.AppendLine();
			message.AppendLine("O Turborama nao instala driver de video automaticamente para evitar driver incorreto, tela preta ou conflito no Windows.");

			DialogResult result = MessageBox.Show(
				message.ToString(),
				"Driver de video recomendado",
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Information
			);

			if (result != DialogResult.Yes)
			{
				return;
			}

			OpenUrl(string.Join(Environment.NewLine, vendorUrls.Values));
		}

		private static List<GpuInfo> DetectVideoControllers()
		{
			List<GpuInfo> result = new List<GpuInfo>();

			try
			{
				using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility, DriverVersion, PNPDeviceID FROM Win32_VideoController"))
				{
					foreach (ManagementObject obj in searcher.Get())
					{
						string name = SafeGet(obj, "Name");
						string adapter = SafeGet(obj, "AdapterCompatibility");
						string pnp = SafeGet(obj, "PNPDeviceID");
						string driverVersion = SafeGet(obj, "DriverVersion");

						if (string.IsNullOrWhiteSpace(name))
						{
							continue;
						}

						string vendor = DetectVendor(name + " " + adapter + " " + pnp);
						string url = GetOfficialDriverUrl(vendor);

						GpuInfo gpu = new GpuInfo
						{
							Name = string.IsNullOrWhiteSpace(driverVersion) ? name : name + " - driver " + driverVersion,
							Vendor = vendor,
							Url = url
						};

						result.Add(gpu);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("GPU detection failed: " + ex.ToString());
			}

			return result;
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

		private static string DetectVendor(string text)
		{
			string lower = (text ?? string.Empty).ToLowerInvariant();

			if (lower.Contains("nvidia") || lower.Contains("ven_10de"))
			{
				return "NVIDIA";
			}

			if (lower.Contains("amd") || lower.Contains("ati") || lower.Contains("radeon") || lower.Contains("ven_1002") || lower.Contains("ven_1022"))
			{
				return "AMD";
			}

			if (lower.Contains("intel") || lower.Contains("uhd graphics") || lower.Contains("iris") || lower.Contains("ven_8086"))
			{
				return "Intel";
			}

			if (lower.Contains("microsoft basic display"))
			{
				return "Driver basico do Windows";
			}

			return "Desconhecido";
		}

		private static string GetOfficialDriverUrl(string vendor)
		{
			if (string.Equals(vendor, "NVIDIA", StringComparison.OrdinalIgnoreCase))
			{
				return "https://www.nvidia.com/pt-br/geforce/drivers/";
			}

			if (string.Equals(vendor, "AMD", StringComparison.OrdinalIgnoreCase))
			{
				return "https://www.amd.com/pt/support/download/drivers.html";
			}

			if (string.Equals(vendor, "Intel", StringComparison.OrdinalIgnoreCase))
			{
				return "https://www.intel.com.br/content/www/br/pt/support/detect.html";
			}

			if (string.Equals(vendor, "Driver basico do Windows", StringComparison.OrdinalIgnoreCase))
			{
				return "ms-settings:windowsupdate";
			}

			return string.Empty;
		}

		private static void OpenUrl(string url)
		{
			if (string.IsNullOrWhiteSpace(url))
			{
				return;
			}

			try
			{
				Clipboard.SetText(url);
				Logger.Log("Copied GPU driver URL: " + url);
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to copy GPU driver URL: " + url + " - " + ex.ToString());
			}
		}
	}
}
