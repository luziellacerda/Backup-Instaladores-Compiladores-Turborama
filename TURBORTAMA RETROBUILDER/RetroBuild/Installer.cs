using System;
using System.IO;
using System.Security.Cryptography;

namespace RetroBuild
{
	// Token: 0x02000002 RID: 2
	internal class Installer
	{
		// Token: 0x06000001 RID: 1 RVA: 0x00002050 File Offset: 0x00000250
		public static void CreateInstaller(BuilderOptions options)
		{
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			string text = baseDirectory;
			string text2 = string.Concat(new string[] { "turborama-v", options.RetrobatVersion, "-", options.Branch, "-", options.Architecture, ".zip" });
			string text3 = Path.Combine(baseDirectory, text2);
			if (!File.Exists(text3))
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
			}
			if (!File.Exists(text3))
			{
				Logger.Log("[ERROR] No .zip file found at: " + text3);
				return;
			}
			Logger.LogInfo("Found zip file: " + text3);
			string text4 = Path.Combine(baseDirectory, "InstallerHost.exe");
			if (!File.Exists(text4))
			{
				Logger.Log("[ERROR] InstallerHost.exe not found at: " + text4);
				return;
			}
			Logger.LogInfo("Found InstallerHost.exe at: " + text4);
			try
			{
				string text5 = string.Concat(new string[] { "TurboRama-v", options.RetrobatVersion, "-", options.Branch, "-", options.Architecture, "-setup.exe" });
				string text6 = Path.Combine(text, text5);
				using (FileStream fileStream = new FileStream(text6, FileMode.Create, FileAccess.Write))
				{
					using (FileStream fileStream2 = new FileStream(text4, FileMode.Open, FileAccess.Read))
					{
						fileStream2.CopyTo(fileStream);
					}
					using (FileStream fileStream3 = new FileStream(text3, FileMode.Open, FileAccess.Read))
					{
						fileStream3.CopyTo(fileStream);
					}
					byte[] bytes = BitConverter.GetBytes(new FileInfo(text3).Length);
					fileStream.Write(bytes, 0, bytes.Length);
				}
				string text7 = "";
				string text8 = text6 + ".sha256.txt";
				if (File.Exists(text6))
				{
					using (FileStream fileStream4 = File.OpenRead(text6))
					{
						using (SHA256 sha = SHA256.Create())
						{
							text7 = BitConverter.ToString(sha.ComputeHash(fileStream4)).Replace("-", "").ToLowerInvariant();
						}
					}
					if (!string.IsNullOrEmpty(text7))
					{
						File.WriteAllText(text8, text7);
					}
				}
				Logger.LogInfo("Created final installer executable: " + text6);
			}
			catch (Exception ex2)
			{
				Logger.Log("[ERROR] Exception creating installer: " + ex2.Message);
			}
		}
	}
}
