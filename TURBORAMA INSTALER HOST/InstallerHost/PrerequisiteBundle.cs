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

			foreach (string candidate in GetCandidateFileNames(fileName))
			{
				string localPath = Path.Combine(GetLocalPrerequisitesFolder(), candidate);
				if (File.Exists(localPath) && new FileInfo(localPath).Length > 1000L)
				{
					Logger.Log("Using local prerequisite file: " + localPath);
					return localPath;
				}

				string resourceName = ResourcePrefix + candidate;
				Assembly assembly = Assembly.GetExecutingAssembly();
				using (Stream stream = assembly.GetManifestResourceStream(resourceName))
				{
					if (stream == null)
					{
						continue;
					}

					string tempDir = Path.Combine(Path.GetTempPath(), "TurboramaPrerequisites");
					Directory.CreateDirectory(tempDir);
					string outputPath = Path.Combine(tempDir, candidate);

					using (FileStream fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
					{
						stream.CopyTo(fileStream);
					}

					if (!File.Exists(outputPath) || new FileInfo(outputPath).Length < 1000L)
					{
						throw new IOException("Falha ao extrair pre-requisito embutido: " + candidate);
					}

					Logger.Log("Extracted embedded prerequisite: " + candidate);
					return outputPath;
				}
			}

			return null;
		}

		public static void EnsureBundleAvailable()
		{
			foreach (string fileName in GamingRuntimeManifest.RequiredBundleFiles)
			{
				if (!HasBundledFile(fileName))
				{
					throw new FileNotFoundException(
						"Pacote offline incompleto: falta o pre-requisito '" + fileName + "'. " +
						"Execute Baixar_Prerequisitos_Instalador.ps1 e recompile o InstallerHost.");
				}
			}
		}

		public static bool HasBundledFile(string fileName)
		{
			foreach (string candidate in GetCandidateFileNames(fileName))
			{
				string localPath = Path.Combine(GetLocalPrerequisitesFolder(), candidate);
				if (File.Exists(localPath) && new FileInfo(localPath).Length > 1000L)
				{
					return true;
				}

				string resourceName = ResourcePrefix + candidate;
				if (Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName) != null)
				{
					return true;
				}
			}

			return false;
		}

		private static string[] GetCandidateFileNames(string fileName)
		{
			if (GamingRuntimeManifest.BundleFileAliases == null)
			{
				return new string[] { fileName };
			}

			foreach (string[] aliases in GamingRuntimeManifest.BundleFileAliases)
			{
				if (aliases != null && aliases.Length > 0 && string.Equals(aliases[0], fileName, StringComparison.OrdinalIgnoreCase))
				{
					return aliases;
				}
			}

			return new string[] { fileName };
		}

		private static string GetLocalPrerequisitesFolder()
		{
			return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "resources", "prerequisites");
		}
	}
}