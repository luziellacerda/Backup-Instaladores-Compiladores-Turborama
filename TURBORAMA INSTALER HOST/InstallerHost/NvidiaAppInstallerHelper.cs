using System;
using System.Diagnostics;
using System.Management;
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
				using (ManagementObjectCollection results = searcher.Get())
				{
					foreach (ManagementObject obj in results)
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

		// Nome preservado por compatibilidade com a interface existente. A operação é
		// deliberadamente manual: drivers dependem do modelo exato, do Windows e do OEM.
		public static void InstallOrOpenNvidiaApp()
		{
			if (!HasNvidiaGpu())
			{
				MessageBox.Show(
					"Nenhuma GPU NVIDIA foi detectada neste computador.",
					"Driver NVIDIA",
					MessageBoxButtons.OK,
					MessageBoxIcon.Information);
				return;
			}

			MessageBox.Show(
				"A página oficial da NVIDIA será aberta." + Environment.NewLine + Environment.NewLine +
				"O TurboRama não baixa nem executa drivers automaticamente, pois o pacote correto depende do modelo da GPU e do Windows.",
				"Driver NVIDIA",
				MessageBoxButtons.OK,
				MessageBoxIcon.Information);
			OpenOfficialUrl("https://www.nvidia.com/Download/index.aspx");
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

		private static void OpenOfficialUrl(string url)
		{
			Uri parsed;
			if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) || !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("Endereço oficial inválido.");
			}

			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = parsed.AbsoluteUri,
					UseShellExecute = true
				});
				Logger.Log("Opened official NVIDIA driver page: " + parsed.AbsoluteUri);
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to open official NVIDIA driver page: " + ex.ToString());
				MessageBox.Show(
					"Não foi possível abrir a página oficial: " + ex.Message,
					"Driver NVIDIA",
					MessageBoxButtons.OK,
					MessageBoxIcon.Warning);
			}
		}
	}
}
