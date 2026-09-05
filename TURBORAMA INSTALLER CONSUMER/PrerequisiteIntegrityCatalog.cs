using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace InstallerHost
{
	internal sealed class PrerequisiteArchiveEntryLock
	{
		public string name { get; set; }
		public long length { get; set; }
		public string sha256 { get; set; }
		public string signerSubject { get; set; }
		public string signerThumbprint { get; set; }
		public string certificatePublicKeySha256 { get; set; }
	}

	internal sealed class PrerequisitePayloadLock
	{
		public string name { get; set; }
		public long length { get; set; }
		public string sha256 { get; set; }
		public string fileType { get; set; }
		public string installTier { get; set; }
		public string productVersion { get; set; }
		public string signerSubject { get; set; }
		public string signerThumbprint { get; set; }
		public string certificatePublicKeySha256 { get; set; }
		public string[] sourceUrls { get; set; }
		public PrerequisiteArchiveEntryLock[] archiveEntries { get; set; }
	}

	internal sealed class PrerequisiteLockDocument
	{
		public int schemaVersion { get; set; }
		public string catalogId { get; set; }
		public string releaseTag { get; set; }
		public string policy { get; set; }
		public PrerequisitePayloadLock[] payloads { get; set; }
	}

	/// <summary>
	/// Catálogo imutável incorporado. Este é o único local com hashes e âncoras
	/// dos payloads aprovados; manifesto e executor apenas o consultam.
	/// </summary>
	internal static class PrerequisiteIntegrityCatalog
	{
		private const string ResourceName = "InstallerHost.prerequisites.lock.json";
		private static readonly Lazy<Dictionary<string, PrerequisitePayloadLock>> Payloads =
			new Lazy<Dictionary<string, PrerequisitePayloadLock>>(LoadAndValidate, true);

		public static PrerequisitePayloadLock GetRequiredPayload(string fileName)
		{
			string safeName = RequireSafeBaseName(fileName, "payload");
			PrerequisitePayloadLock payload;
			if (!Payloads.Value.TryGetValue(safeName, out payload))
			{
				throw new InvalidDataException("Payload não registrado em prerequisites.lock.json: " + safeName + ".");
			}
			return payload;
		}

		public static PrerequisiteArchiveEntryLock GetRequiredArchiveEntry(
			PrerequisitePayloadLock payload,
			string entryName)
		{
			if (payload == null)
			{
				throw new ArgumentNullException("payload");
			}
			string normalized = NormalizeArchiveName(entryName);
			PrerequisiteArchiveEntryLock entry = (payload.archiveEntries ?? new PrerequisiteArchiveEntryLock[0])
				.SingleOrDefault(item => string.Equals(NormalizeArchiveName(item.name), normalized, StringComparison.OrdinalIgnoreCase));
			if (entry == null)
			{
				throw new InvalidDataException("Entrada não registrada para " + payload.name + ": " + normalized + ".");
			}
			return entry;
		}

		public static IList<PrerequisitePayloadLock> GetPayloads()
		{
			return Payloads.Value.Values.OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
		}

		private static Dictionary<string, PrerequisitePayloadLock> LoadAndValidate()
		{
			Assembly assembly = Assembly.GetExecutingAssembly();
			string json;
			using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
			{
				if (stream == null)
				{
					throw new InvalidDataException("Catálogo de integridade incorporado ausente: " + ResourceName + ".");
				}
				if (stream.Length <= 0L || stream.Length > 1048576L)
				{
					throw new InvalidDataException("Tamanho inválido do catálogo de integridade.");
				}
				using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, false))
				{
					json = reader.ReadToEnd();
				}
			}

			PrerequisiteLockDocument document;
			try
			{
				JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 1048576 };
				document = serializer.Deserialize<PrerequisiteLockDocument>(json);
			}
			catch (Exception ex)
			{
				throw new InvalidDataException("prerequisites.lock.json inválido.", ex);
			}

			if (document == null || document.schemaVersion != 1 ||
				string.IsNullOrWhiteSpace(document.catalogId) ||
				document.payloads == null || document.payloads.Length == 0)
			{
				throw new InvalidDataException("Schema obrigatório do catálogo de integridade não foi atendido.");
			}

			Dictionary<string, PrerequisitePayloadLock> result =
				new Dictionary<string, PrerequisitePayloadLock>(StringComparer.OrdinalIgnoreCase);
			foreach (PrerequisitePayloadLock payload in document.payloads)
			{
				ValidatePayload(payload);
				if (result.ContainsKey(payload.name))
				{
					throw new InvalidDataException("Payload duplicado no catálogo: " + payload.name + ".");
				}
				result.Add(payload.name, payload);
			}

			string[] expected = GamingRuntimeManifest.GetComponents()
				.Where(item => item.CanInstallOffline && !string.IsNullOrWhiteSpace(item.BundleFileName))
				.Select(item => item.BundleFileName)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
			if (result.Count != expected.Length)
			{
				throw new InvalidDataException(
					"O catálogo incorporado possui " + result.Count + " payloads, mas o manifesto declara " + expected.Length + ".");
			}
			foreach (string expectedName in expected)
			{
				PrerequisitePayloadLock payload;
				if (!result.TryGetValue(expectedName, out payload))
				{
					throw new InvalidDataException("Payload do manifesto ausente no catálogo: " + expectedName + ".");
				}
				GamingRuntimeComponent component = GamingRuntimeManifest.FindByBundleFile(expectedName);
				if (component == null || !string.Equals(payload.installTier, component.Tier.ToString(), StringComparison.Ordinal))
				{
					throw new InvalidDataException("Tier divergente entre manifesto e catálogo: " + expectedName + ".");
				}
			}

			return result;
		}

		private static void ValidatePayload(PrerequisitePayloadLock payload)
		{
			if (payload == null)
			{
				throw new InvalidDataException("Entrada nula no catálogo de integridade.");
			}
			payload.name = RequireSafeBaseName(payload.name, "payload");
			RequirePositiveLength(payload.length, payload.name);
			payload.sha256 = RequireHex(payload.sha256, 64, payload.name + " SHA-256");
			if (payload.fileType != "Exe" && payload.fileType != "Msi" && payload.fileType != "Zip")
			{
				throw new InvalidDataException("fileType não permitido para " + payload.name + ".");
			}
			if (payload.installTier != "Required" && payload.installTier != "Recommended" && payload.installTier != "Optional")
			{
				throw new InvalidDataException("installTier inválido para " + payload.name + ".");
			}

			if (payload.sourceUrls == null || payload.sourceUrls.Length == 0 || payload.sourceUrls.Any(url => !IsHttpsUrl(url)))
			{
				throw new InvalidDataException("Fonte HTTPS oficial ausente ou inválida para " + payload.name + ".");
			}

			if (payload.fileType == "Exe" || payload.fileType == "Msi")
			{
				ValidateSignerAnchor(payload.signerSubject, payload.signerThumbprint,
					payload.certificatePublicKeySha256, payload.name, payload.name);
			}

			PrerequisiteArchiveEntryLock[] entries = payload.archiveEntries ?? new PrerequisiteArchiveEntryLock[0];
			HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (PrerequisiteArchiveEntryLock entry in entries)
			{
				if (entry == null)
				{
					throw new InvalidDataException("Entrada nula no arquivo " + payload.name + ".");
				}
				entry.name = NormalizeArchiveName(entry.name);
				if (!names.Add(entry.name))
				{
					throw new InvalidDataException("Entrada duplicada em " + payload.name + ": " + entry.name + ".");
				}
				RequirePositiveLength(entry.length, payload.name + "/" + entry.name);
				entry.sha256 = RequireHex(entry.sha256, 64, entry.name + " SHA-256");
				ValidateSignerAnchor(entry.signerSubject, entry.signerThumbprint,
					entry.certificatePublicKeySha256, payload.name, payload.name + "/" + entry.name);
			}

			if (payload.fileType == "Zip" && entries.Length != 1)
			{
				throw new InvalidDataException("ZIP deve declarar exatamente um instalador interno: " + payload.name + ".");
			}
		}

		private static void ValidateSignerAnchor(
			string subject,
			string thumbprint,
			string publicKeyHash,
			string payloadName,
			string label)
		{
			string approvedThirdPartySubject = GetApprovedThirdPartySubject(payloadName);
			bool thirdPartyPayload = !string.IsNullOrEmpty(approvedThirdPartySubject);
			bool approvedMicrosoft = !thirdPartyPayload && !string.IsNullOrWhiteSpace(subject) &&
				subject.StartsWith("CN=", StringComparison.Ordinal) &&
				subject.IndexOf("O=Microsoft Corporation", StringComparison.Ordinal) >= 0;
			bool approvedThirdParty = thirdPartyPayload &&
				string.Equals(payloadName, label, StringComparison.Ordinal) &&
				string.Equals(subject, approvedThirdPartySubject, StringComparison.Ordinal);
			if (!approvedMicrosoft && !approvedThirdParty)
			{
				throw new InvalidDataException("Editor exato ausente ou não aprovado para " + label + ".");
			}
			RequireHex(thumbprint, 40, label + " thumbprint");
			RequireHex(publicKeyHash, 64, label + " chave pública");
		}

		internal static void ValidateSignerAnchorForTest(
			string payloadName,
			string label,
			string subject,
			string thumbprint,
			string publicKeyHash)
		{
			ValidateSignerAnchor(subject, thumbprint, publicKeyHash, payloadName, label);
		}

		private static string GetApprovedThirdPartySubject(string payloadName)
		{
			// Exceções são ligadas ao nome do payload de nível superior. Elas não
			// relaxam os payloads Microsoft nem permitem reutilizar o editor em ZIPs.
			if (string.Equals(payloadName, "DokanSetup.exe", StringComparison.OrdinalIgnoreCase))
			{
				return "CN=LEOSAC, O=LEOSAC, STREET=39 rue Principale, PostalCode=67220, L=Breitenau, S=Bas-Rhin, C=FR, SERIALNUMBER=919 690 420 00014, OID.1.3.6.1.4.1.311.60.2.1.1=Colmar, OID.1.3.6.1.4.1.311.60.2.1.2=Haut-Rhin, OID.1.3.6.1.4.1.311.60.2.1.3=FR, OID.2.5.4.15=Private Organization";
			}
			if (string.Equals(payloadName, "winfsp-2.2.26215.msi", StringComparison.OrdinalIgnoreCase))
			{
				return "CN=NAVIMATICS LLC, O=NAVIMATICS LLC, L=KIRKLAND, S=Washington, C=US, SERIALNUMBER=604 419 559, OID.2.5.4.15=Private Organization, OID.1.3.6.1.4.1.311.60.2.1.2=Washington, OID.1.3.6.1.4.1.311.60.2.1.3=US";
			}
			return string.Empty;
		}

		private static string RequireSafeBaseName(string value, string label)
		{
			if (string.IsNullOrWhiteSpace(value) || Path.GetFileName(value) != value ||
				value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
			{
				throw new InvalidDataException("Nome inválido de " + label + ": " + (value ?? "<nulo>") + ".");
			}
			return value;
		}

		private static string NormalizeArchiveName(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				throw new InvalidDataException("Nome vazio de entrada de arquivo.");
			}
			string normalized = value.Replace('\\', '/').TrimStart('/');
			if (normalized.Length == 0 || normalized.Contains("../") || normalized.Contains("..\\") ||
				Path.IsPathRooted(normalized) || normalized.IndexOf(':') >= 0)
			{
				throw new InvalidDataException("Caminho inseguro em entrada de arquivo: " + value + ".");
			}
			return normalized;
		}

		private static void RequirePositiveLength(long length, string label)
		{
			if (length <= 0L || length > 1073741824L)
			{
				throw new InvalidDataException("Tamanho inválido para " + label + ".");
			}
		}

		private static string RequireHex(string value, int length, string label)
		{
			string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
			if (normalized.Length != length || normalized.Any(ch => !Uri.IsHexDigit(ch)))
			{
				throw new InvalidDataException(label + " inválido.");
			}
			return normalized;
		}

		private static bool IsHttpsUrl(string value)
		{
			Uri uri;
			return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
				string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
				!string.IsNullOrWhiteSpace(uri.Host);
		}
	}
}
