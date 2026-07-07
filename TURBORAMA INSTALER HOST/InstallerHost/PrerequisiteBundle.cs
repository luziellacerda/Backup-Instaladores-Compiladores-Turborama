using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace InstallerHost
{
	internal static class PrerequisiteBundle
	{
		private const string ResourcePrefix = "InstallerHost.resources.prerequisites.";

		public static string ExtractBundledFile(string fileName)
		{
			string path = TryExtractBundledFile(fileName);
			if (string.IsNullOrEmpty(path))
			{
				throw new FileNotFoundException(
					"Pre-requisito embutido nao encontrado: " + fileName + Environment.NewLine +
					"Execute Baixar_Prerequisitos_Instalador.ps1 e recompile o InstallerHost em Release.",
					fileName);
			}

			return path;
		}

		public static string TryExtractBundledFile(string fileName)
		{
			if (string.IsNullOrWhiteSpace(fileName))
			{
				return null;
			}

			string localPath = Path.Combine(GetLocalPrerequisitesFolder(), fileName);
			if (File.Exists(localPath) && new FileInfo(localPath).Length > 1000L)
			{
				Logger.Log("Using local prerequisite file: " + localPath);
				return localPath;
			}

			string resourceName = ResourcePrefix + fileName;
			Assembly assembly = Assembly.GetExecutingAssembly();
			using (Stream stream = assembly.GetManifestResourceStream(resourceName))
			{
				if (stream == null)
				{
					return null;
				}

				string tempDir = Path.Combine(Path.GetTempPath(), "TurboramaPrerequisites");
				Directory.CreateDirectory(tempDir);
				string outputPath = Path.Combine(tempDir, fileName);

				using (FileStream fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					stream.CopyTo(fileStream);
				}

				if (!File.Exists(outputPath) || new FileInfo(outputPath).Length < 1000L)
				{
					throw new IOException("Falha ao extrair pre-requisito embutido: " + fileName);
				}

				Logger.Log("Extracted embedded prerequisite: " + fileName);
				return outputPath;
			}
		}

		public static void EnsureBundleAvailable()
		{
			foreach (string fileName in GamingRuntimeManifest.RequiredBundleFiles)
			{
				string localPath = Path.Combine(GetLocalPrerequisitesFolder(), fileName);
				string resourceName = ResourcePrefix + fileName;
				bool hasLocal = File.Exists(localPath) && new FileInfo(localPath).Length > 1000L;
				bool hasEmbedded = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName) != null;
				if (!hasLocal && !hasEmbedded)
				{
					throw new FileNotFoundException(
						"Pacote comercial incompleto: falta o pre-requisito '" + fileName + "'. " +
						"Execute Baixar_Prerequisitos_Instalador.ps1 e recompile o InstallerHost.");
				}
			}
		}

		private static string GetLocalPrerequisitesFolder()
		{
			return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "resources", "prerequisites");
		}
	}
}