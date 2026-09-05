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
		private static readonly Dictionary<string, TrustedInstallerFile> ExtractedFileLeases =
			new Dictionary<string, TrustedInstallerFile>(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, string> ExtractedArchiveInstallers =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, TrustedInstallerFile> ExtractedArchiveInstallerLeases =
			new Dictionary<string, TrustedInstallerFile>(StringComparer.OrdinalIgnoreCase);
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

					EnsureExtractionStaging();
					string componentFolder = extractionStaging.CreateSubdirectory(Guid.NewGuid().ToString("N"));
					string outputPath = Path.Combine(componentFolder, payload.name);
					using (FileStream output = extractionStaging.CreateFileForWrite(outputPath))
					{
						source.CopyTo(output);
						output.Flush(true);
					}
					extractionStaging.VerifyFilePolicy(outputPath);

					TrustedInstallerFile verified = InstallerPackageSecurity.OpenTrustedPayload(
						outputPath, component, component.DisplayName + " (incorporado)");
					try
					{
						// Keep the verified file open without write/delete sharing until the
						// complete plan ends. Every execution verifies again and holds its own
						// lease, while this one prevents a later payload swap after preflight.
						ExtractedFiles[payload.name] = outputPath;
						ExtractedFileLeases.Add(payload.name, verified);
						verified = null;
					}
					finally
					{
						if (verified != null) verified.Dispose();
					}
					Logger.Log("Extracted and verified embedded prerequisite: " + payload.name);
					return outputPath;
				}
			}
		}

		/// <summary>
		/// Expands and validates a legacy Visual C++ ZIP without starting any child
		/// process. The verified inner executable stays open without write/delete
		/// sharing and is reused later by the execution phase.
		/// </summary>
		public static string PrepareLegacyArchiveInstaller(GamingRuntimeComponent component)
		{
			if (component == null || !component.CanInstallOffline || !component.IsLegacy ||
				string.IsNullOrWhiteSpace(component.BundleFileName))
			{
				throw new InvalidDataException("Componente legado sem arquivo offline aprovado.");
			}

			PrerequisitePayloadLock payload =
				PrerequisiteIntegrityCatalog.GetRequiredPayload(component.BundleFileName);
			if (!string.Equals(payload.fileType, "Zip", StringComparison.Ordinal))
			{
				throw new InvalidDataException(
					"Componente legado não está protegido por um ZIP catalogado: " + payload.name + ".");
			}
			PrerequisiteArchiveEntryLock[] entries =
				payload.archiveEntries ?? new PrerequisiteArchiveEntryLock[0];
			if (entries.Length != 1 ||
				!string.Equals(entries[0].name, component.InstallerFileName, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(Path.GetExtension(entries[0].name), ".exe", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException(
					"Executável interno do componente legado diverge do manifesto: " + payload.name + ".");
			}

			lock (ExtractionSync)
			{
				string existing;
				TrustedInstallerFile existingLease;
				bool hasPath = ExtractedArchiveInstallers.TryGetValue(payload.name, out existing);
				bool hasLease = ExtractedArchiveInstallerLeases.TryGetValue(payload.name, out existingLease);
				if (hasPath || hasLease)
				{
					if (!hasPath || !hasLease || existingLease == null ||
						!string.Equals(existingLease.Path, existing, StringComparison.OrdinalIgnoreCase) ||
						!File.Exists(existing))
					{
						throw new InvalidDataException(
							"Cache protegido do instalador legado está inconsistente: " + payload.name + ".");
					}
					return existing;
				}

				string archivePath = ExtractBundledFile(component);
				EnsureExtractionStaging();
				string installerPath = InstallerPackageSecurity.ExtractAndVerifyArchiveInstaller(
					archivePath,
					component,
					extractionStaging);

				TrustedInstallerFile verified = InstallerPackageSecurity.OpenTrustedInstaller(
					installerPath, component, component.DisplayName + " (interno pré-validado)");
				try
				{
					// This retained handle is the preflight-to-execution binding: after
					// length/hash/signer validation, the executable cannot be replaced,
					// overwritten or deleted before its installation turn.
					ExtractedArchiveInstallers.Add(payload.name, installerPath);
					try
					{
						ExtractedArchiveInstallerLeases.Add(payload.name, verified);
						verified = null;
					}
					catch
					{
						ExtractedArchiveInstallers.Remove(payload.name);
						throw;
					}
				}
				finally
				{
					if (verified != null) verified.Dispose();
				}
				Logger.Log("Extracted, verified and locked archive installer: " + payload.name);
				return installerPath;
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
			if (InstallerProcessQuarantine.DeferPrerequisiteCleanupIfRequired())
			{
				Logger.Log(
					"Embedded prerequisite cleanup deferred because an installer process remains quarantined.");
				return;
			}
			lock (ExtractionSync)
			{
				foreach (TrustedInstallerFile lease in ExtractedArchiveInstallerLeases.Values)
				{
					lease.Dispose();
				}
				ExtractedArchiveInstallerLeases.Clear();
				ExtractedArchiveInstallers.Clear();
				foreach (TrustedInstallerFile lease in ExtractedFileLeases.Values)
				{
					lease.Dispose();
				}
				ExtractedFileLeases.Clear();
				ExtractedFiles.Clear();
				SecureInstallerStaging staging = extractionStaging;
				extractionStaging = null;
				if (staging != null)
				{
					staging.Dispose();
				}
			}
		}

		private static void EnsureExtractionStaging()
		{
			if (extractionStaging == null)
			{
				extractionStaging = SecureInstallerStaging.Create("EmbeddedPrerequisites");
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
