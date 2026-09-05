using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace InstallerHost
{
	internal static class PrerequisiteBundle
	{
		private const string ResourcePrefix = "InstallerHost.resources.prerequisites.";
		private static readonly object ExtractionSync = new object();
		private static readonly Dictionary<string, string> ExtractedFiles =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private static SecureInstallerStaging extractionStaging;

		static PrerequisiteBundle()
		{
			AppDomain.CurrentDomain.ProcessExit += delegate { CleanupExtractedFiles(); };
		}

		public static string ExtractBundledFile(GamingRuntimeComponent component)
		{
			if (component == null || !component.CanInstallOffline || string.IsNullOrWhiteSpace(component.BundleFileName))
			{
				throw new InvalidDataException("Componente sem payload offline aprovado.");
			}

			PrerequisitePayloadLock payload =
				PrerequisiteIntegrityCatalog.GetRequiredPayload(component.BundleFileName);
			lock (ExtractionSync)
			{
				string existing;
				if (ExtractedFiles.TryGetValue(payload.name, out existing) && File.Exists(existing))
				{
					using (TrustedInstallerFile verified =
						InstallerPackageSecurity.OpenTrustedPayload(existing, component, component.DisplayName + " (incorporado)"))
					{
						return existing;
					}
				}

				string resourceName = ResourcePrefix + payload.name;
				Assembly assembly = Assembly.GetExecutingAssembly();
				using (Stream source = assembly.GetManifestResourceStream(resourceName))
				{
					if (source == null)
					{
						throw new FileNotFoundException(
							"Payload incorporado não encontrado: " + payload.name + ".", payload.name);
					}
					if (source.Length != payload.length)
					{
						throw new InvalidDataException("Tamanho do recurso incorporado diverge do catálogo: " + payload.name + ".");
					}

					if (extractionStaging == null)
					{
						extractionStaging = SecureInstallerStaging.Create("EmbeddedPrerequisites");
					}
					string componentFolder = extractionStaging.CreateSubdirectory(Guid.NewGuid().ToString("N"));
					string outputPath = Path.Combine(componentFolder, payload.name);
					using (FileStream output = extractionStaging.CreateFileForWrite(outputPath))
					{
						source.CopyTo(output);
						output.Flush(true);
					}
					extractionStaging.VerifyFilePolicy(outputPath);

					using (TrustedInstallerFile verified =
						InstallerPackageSecurity.OpenTrustedPayload(outputPath, component, component.DisplayName + " (incorporado)"))
					{
					}
					ExtractedFiles[payload.name] = outputPath;
					Logger.Log("Extracted and verified embedded prerequisite: " + payload.name);
					return outputPath;
				}
			}
		}

		public static void EnsureBundleAvailable()
		{
			EnsureBundleFilesAvailable(GamingRuntimeManifest.RequiredBundleFiles);
		}

		public static void EnsureBundleAvailable(IEnumerable<GamingRuntimeComponent> components)
		{
			if (components == null)
			{
				return;
			}

			EnsureBundleFilesAvailable(components
				.Where(component => component != null && component.CanInstallOffline)
				.Select(component => component.BundleFileName));
		}

		public static bool HasBundledFile(string fileName)
		{
			GamingRuntimeComponent component = GamingRuntimeManifest.FindByBundleFile(fileName);
			if (component == null || !component.CanInstallOffline)
			{
				return false;
			}

			PrerequisitePayloadLock payload;
			try
			{
				payload = PrerequisiteIntegrityCatalog.GetRequiredPayload(component.BundleFileName);
			}
			catch (Exception ex)
			{
				Logger.Log("Integrity catalog rejected " + fileName + ": " + ex.Message);
				return false;
			}

			using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + payload.name))
			{
				return stream != null && stream.Length == payload.length;
			}
		}

		public static void CleanupExtractedFiles()
		{
			lock (ExtractionSync)
			{
				ExtractedFiles.Clear();
				SecureInstallerStaging staging = extractionStaging;
				extractionStaging = null;
				if (staging != null)
				{
					staging.Dispose();
				}
			}
		}

		private static void EnsureBundleFilesAvailable(IEnumerable<string> fileNames)
		{
			List<string> missing = (fileNames ?? new string[0])
				.Where(item => !string.IsNullOrWhiteSpace(item))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Where(item => !HasBundledFile(item))
				.ToList();
			if (missing.Count > 0)
			{
				throw new FileNotFoundException(
					"Pacote offline incompleto ou não catalogado:" + Environment.NewLine +
					string.Join(Environment.NewLine, missing.Select(item => " - " + item)));
			}
		}
	}
}
