using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace InstallerHost
{
	public enum GamingReadinessState
	{
		Ready,
		Attention,
		Blocked,
		Unknown,
		NotApplicable
	}

	public enum GamingRuntimeTier
	{
		Required,
		Recommended,
		Optional,
		Guidance
	}

	public enum GamingRuntimeCategory
	{
		Windows,
		GraphicsDriver,
		GraphicsApi,
		MicrosoftRuntime,
		LegacyGameRuntime,
		EmulatorSupport,
		FileSystemSupport
	}

	public enum RuntimeInstallDisposition
	{
		AlreadyInstalled,
		InstallFromVerifiedBundle,
		OpenOfficialInstructions,
		MissingBundle,
		NotApplicable,
		ManualChoiceRequired
	}

	public sealed class GamingRuntimeComponent
	{
		public GamingRuntimeComponent(
			string id,
			string displayName,
			GamingRuntimeCategory category,
			GamingRuntimeTier tier,
			string architecture,
			string detectionKey,
			string publisher,
			string officialUrl,
			string description,
			string bundleFileName,
			string installerFileName,
			bool canInstallOffline,
			bool includedByDefault,
			bool legacy,
			string[] bundleAliases,
			string[] publisherTokens)
		{
			Id = id ?? string.Empty;
			DisplayName = displayName ?? string.Empty;
			Category = category;
			Tier = tier;
			Architecture = architecture ?? "any";
			DetectionKey = detectionKey ?? string.Empty;
			Publisher = publisher ?? string.Empty;
			OfficialUrl = officialUrl ?? string.Empty;
			Description = description ?? string.Empty;
			BundleFileName = bundleFileName ?? string.Empty;
			InstallerFileName = installerFileName ?? string.Empty;
			CanInstallOffline = canInstallOffline;
			IncludedByDefault = includedByDefault;
			IsLegacy = legacy;
			BundleAliases = bundleAliases ?? new string[0];
			PublisherTokens = publisherTokens ?? new string[0];
		}

		public string Id { get; private set; }
		public string DisplayName { get; private set; }
		public GamingRuntimeCategory Category { get; private set; }
		public GamingRuntimeTier Tier { get; private set; }
		public string Architecture { get; private set; }
		public string DetectionKey { get; private set; }
		public string Publisher { get; private set; }
		public string OfficialUrl { get; private set; }
		public string Description { get; private set; }
		public string BundleFileName { get; private set; }
		public string InstallerFileName { get; private set; }
		public bool CanInstallOffline { get; private set; }
		public bool IncludedByDefault { get; private set; }
		public bool IsLegacy { get; private set; }
		public string[] BundleAliases { get; private set; }
		public string[] PublisherTokens { get; private set; }
	}

	public sealed class RuntimeComponentStatus
	{
		public GamingRuntimeComponent Component { get; internal set; }
		public GamingReadinessState State { get; internal set; }
		public string DetectedVersion { get; internal set; }
		public string Detail { get; internal set; }
		public bool BundleAvailable { get; internal set; }

		public bool NeedsAction
		{
			get { return State == GamingReadinessState.Attention || State == GamingReadinessState.Blocked; }
		}
	}

	public sealed class GamingGpuInfo
	{
		public string Name { get; internal set; }
		public string Vendor { get; internal set; }
		public string DriverVersion { get; internal set; }
		public DateTime? DriverDate { get; internal set; }
		public string PnpDeviceId { get; internal set; }
		public long AdapterRamBytes { get; internal set; }
		public bool UsesBasicDisplayDriver { get; internal set; }
		public bool IsLikelySoftwareAdapter { get; internal set; }

		public string AdapterRamDisplay
		{
			get
			{
				return AdapterRamBytes > 0
					? string.Format("{0:0.0} GB", AdapterRamBytes / 1073741824.0)
					: "não informado";
			}
		}
	}

	public sealed class GamingReadinessFinding
	{
		public string Code { get; internal set; }
		public string Title { get; internal set; }
		public string Detail { get; internal set; }
		public string Recommendation { get; internal set; }
		public string OfficialUrl { get; internal set; }
		public GamingReadinessState State { get; internal set; }
	}

	public sealed class GamingReadinessProfile
	{
		private readonly List<GamingGpuInfo> gpus = new List<GamingGpuInfo>();
		private readonly List<RuntimeComponentStatus> runtimeStatuses = new List<RuntimeComponentStatus>();
		private readonly List<GamingReadinessFinding> findings = new List<GamingReadinessFinding>();

		public DateTime CapturedAtUtc { get; internal set; }
		public string ComputerName { get; internal set; }
		public string OsCaption { get; internal set; }
		public string OsVersion { get; internal set; }
		public int OsBuild { get; internal set; }
		public string OsArchitecture { get; internal set; }
		public bool Is64BitOperatingSystem { get; internal set; }
		public bool PendingRestart { get; internal set; }
		public string CpuName { get; internal set; }
		public int PhysicalCoreCount { get; internal set; }
		public int LogicalProcessorCount { get; internal set; }
		public int CpuAddressWidth { get; internal set; }
		public bool? VirtualizationFirmwareEnabled { get; internal set; }
		public bool? SecondLevelAddressTranslation { get; internal set; }
		public long PhysicalMemoryBytes { get; internal set; }
		public string SystemDrive { get; internal set; }
		public long SystemDriveTotalBytes { get; internal set; }
		public long SystemDriveFreeBytes { get; internal set; }
		public string Direct3DFeatureLevel { get; internal set; }
		public bool Direct3DProbeSucceeded { get; internal set; }
		public bool DirectX12RuntimePresent { get; internal set; }
		public bool VulkanLoaderPresent { get; internal set; }
		public string VulkanLoaderVersion { get; internal set; }
		public bool OpenGlLoaderPresent { get; internal set; }
		public int Score { get; internal set; }
		public GamingReadinessState OverallState { get; internal set; }

		public ReadOnlyCollection<GamingGpuInfo> Gpus
		{
			get { return gpus.AsReadOnly(); }
		}

		public ReadOnlyCollection<RuntimeComponentStatus> RuntimeStatuses
		{
			get { return runtimeStatuses.AsReadOnly(); }
		}

		public ReadOnlyCollection<GamingReadinessFinding> Findings
		{
			get { return findings.AsReadOnly(); }
		}

		internal List<GamingGpuInfo> MutableGpus
		{
			get { return gpus; }
		}

		internal List<RuntimeComponentStatus> MutableRuntimeStatuses
		{
			get { return runtimeStatuses; }
		}

		internal List<GamingReadinessFinding> MutableFindings
		{
			get { return findings; }
		}

		public string MemoryDisplay
		{
			get
			{
				return PhysicalMemoryBytes > 0
					? string.Format("{0:0.0} GB", PhysicalMemoryBytes / 1073741824.0)
					: "não informado";
			}
		}

		public string SystemDriveFreeDisplay
		{
			get
			{
				return SystemDriveFreeBytes > 0
					? string.Format("{0:0.0} GB", SystemDriveFreeBytes / 1073741824.0)
					: "não informado";
			}
		}

		public string BuildSummary()
		{
			int installed = runtimeStatuses.Count(item => item.State == GamingReadinessState.Ready);
			int actionable = runtimeStatuses.Count(item => item.NeedsAction && item.Component != null &&
				(item.Component.Tier == GamingRuntimeTier.Required || item.Component.Tier == GamingRuntimeTier.Recommended));
			return string.Format("Prontidão {0}/100 · {1} componentes detectados · {2} ações sugeridas", Score, installed, actionable);
		}

		public string BuildDetailedReport()
		{
			StringBuilder report = new StringBuilder();
			report.AppendLine("TURBORAMA — DIAGNÓSTICO DE PRONTIDÃO");
			report.AppendLine("Somente diagnóstico. Nenhum jogo, ROM ou BIOS é fornecido.");
			report.AppendLine();
			report.AppendLine(BuildSummary());
			report.AppendLine(string.Format("Windows: {0} {1} (build {2}, {3})", OsCaption, OsVersion, OsBuild, OsArchitecture));
			report.AppendLine(string.Format("CPU: {0} · {1} núcleos / {2} processadores lógicos", CpuName, PhysicalCoreCount, LogicalProcessorCount));
			report.AppendLine(string.Format("RAM: {0} · Disco do sistema livre: {1}", MemoryDisplay, SystemDriveFreeDisplay));
			report.AppendLine(string.Format("Gráficos: D3D {0} · D3D12 runtime {1} · Vulkan {2} · OpenGL loader {3}",
				string.IsNullOrWhiteSpace(Direct3DFeatureLevel) ? "não confirmado" : Direct3DFeatureLevel,
				DirectX12RuntimePresent ? "presente" : "não detectado",
				VulkanLoaderPresent ? (string.IsNullOrWhiteSpace(VulkanLoaderVersion) ? "presente" : VulkanLoaderVersion) : "não detectado",
				OpenGlLoaderPresent ? "presente" : "não detectado"));

			foreach (GamingGpuInfo gpu in gpus)
			{
				report.AppendLine(string.Format("GPU: {0} · driver {1} · VRAM {2}", gpu.Name, string.IsNullOrWhiteSpace(gpu.DriverVersion) ? "não informado" : gpu.DriverVersion, gpu.AdapterRamDisplay));
			}

			report.AppendLine();
			report.AppendLine("COMPONENTES");
			foreach (RuntimeComponentStatus status in runtimeStatuses)
			{
				report.AppendLine(string.Format("[{0}] {1} — {2}", GetStateLabel(status.State), status.Component.DisplayName, status.Detail));
			}

			if (findings.Count > 0)
			{
				report.AppendLine();
				report.AppendLine("RECOMENDAÇÕES");
				foreach (GamingReadinessFinding finding in findings)
				{
					report.AppendLine(string.Format("[{0}] {1}: {2}", GetStateLabel(finding.State), finding.Title, finding.Recommendation));
					if (!string.IsNullOrWhiteSpace(finding.OfficialUrl))
					{
						report.AppendLine("  Fonte oficial: " + finding.OfficialUrl);
					}
				}
			}

			return report.ToString();
		}

		private static string GetStateLabel(GamingReadinessState state)
		{
			switch (state)
			{
				case GamingReadinessState.Ready:
					return "OK";
				case GamingReadinessState.Blocked:
					return "BLOQUEIO";
				case GamingReadinessState.Attention:
					return "ATENÇÃO";
				case GamingReadinessState.NotApplicable:
					return "N/A";
				default:
					return "DESCONHECIDO";
			}
		}
	}

	public sealed class RuntimeInstallPlanItem
	{
		public GamingRuntimeComponent Component { get; internal set; }
		public RuntimeComponentStatus Status { get; internal set; }
		public RuntimeInstallDisposition Disposition { get; internal set; }
		public string Reason { get; internal set; }
	}

	/// <summary>
	/// Opções que o usuário realmente vê na tela de pré-requisitos. O plano de
	/// instalação não pode incluir silenciosamente um grupo que esteja desmarcado.
	/// Dokany e WinFsp não fazem parte deste modelo porque não são distribuídos no
	/// pacote offline; a tela apresenta apenas orientação para esses componentes.
	/// </summary>
	public sealed class GamingRuntimeInstallSelection
	{
		public bool InstallMicrosoftRuntimeStack { get; set; }
		public bool InstallDirectXLegacy { get; set; }
		public bool OpenNvidiaOfficialSource { get; set; }

		public static GamingRuntimeInstallSelection RecommendedDefaults()
		{
			return new GamingRuntimeInstallSelection
			{
				InstallMicrosoftRuntimeStack = true,
				InstallDirectXLegacy = true,
				OpenNvidiaOfficialSource = false
			};
		}
	}
}
