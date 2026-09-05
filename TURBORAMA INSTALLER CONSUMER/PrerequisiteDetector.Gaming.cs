using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace InstallerHost
{
	public static partial class PrerequisiteDetector
	{
		private const int MinimumModernWindowsBuild = 19041;
		private const int NetFx35StandaloneInstallerBuild = 28000;
		private const long OneGigabyte = 1073741824L;

		public static GamingReadinessProfile CaptureGamingReadinessProfile()
		{
			GamingReadinessProfile profile = new GamingReadinessProfile();
			profile.CapturedAtUtc = DateTime.UtcNow;
			profile.ComputerName = Environment.MachineName;
			profile.Is64BitOperatingSystem = Environment.Is64BitOperatingSystem;
			profile.OsArchitecture = Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits";

			TryDetectOperatingSystem(profile);
			TryDetectCpuAndMemory(profile);
			TryDetectSystemDrive(profile);
			TryDetectGpus(profile);
			TryDetectGraphicsApis(profile);
			profile.PendingRestart = IsRestartPending();

			foreach (GamingRuntimeComponent component in GamingRuntimeManifest.GetComponents())
			{
				profile.MutableRuntimeStatuses.Add(DetectRuntimeComponent(profile, component));
			}

			BuildReadinessFindings(profile);
			CalculateReadinessScore(profile);
			return profile;
		}

		public static RuntimeComponentStatus DetectRuntimeComponent(GamingReadinessProfile profile, GamingRuntimeComponent component)
		{
			RuntimeComponentStatus result = new RuntimeComponentStatus
			{
				Component = component,
				State = GamingReadinessState.Unknown,
				DetectedVersion = string.Empty,
				Detail = "Não foi possível confirmar o estado.",
				BundleAvailable = component != null && component.CanInstallOffline &&
					!string.IsNullOrWhiteSpace(component.BundleFileName) && PrerequisiteBundle.HasBundledFile(component.BundleFileName)
			};

			if (component == null)
			{
				result.Detail = "Definição de componente inválida.";
				return result;
			}

			if (!GamingRuntimeManifest.IsApplicableToCurrentOs(component))
			{
				result.State = GamingReadinessState.NotApplicable;
				result.Detail = "Não se aplica à arquitetura deste Windows.";
				return result;
			}

			bool? installed = null;
			string version = string.Empty;
			string detail = string.Empty;

			try
			{
				switch (component.DetectionKey)
				{
					case "windows-update":
						installed = profile != null && profile.OsBuild >= MinimumModernWindowsBuild && !profile.PendingRestart;
						detail = profile != null && profile.PendingRestart
							? "O Windows informa reinicialização pendente; depois, procure atualizações novamente."
							: "A disponibilidade de novas atualizações deve ser confirmada no Windows Update.";
						break;
					case "gpu-driver":
						installed = profile != null && profile.Gpus.Any(gpu => !gpu.UsesBasicDisplayDriver && !gpu.IsLikelySoftwareAdapter && !string.IsNullOrWhiteSpace(gpu.DriverVersion));
						version = profile == null ? string.Empty : string.Join(", ", profile.Gpus.Where(gpu => !string.IsNullOrWhiteSpace(gpu.DriverVersion)).Select(gpu => gpu.DriverVersion));
						detail = installed == true ? "Driver de vídeo do fabricante detectado." : "Driver gráfico completo não confirmado.";
						break;
					case "vc-modern-x64":
						installed = IsVcRedist2015_2022Installed("x64");
						version = GetVcRuntimeVersion("x64");
						break;
					case "vc-modern-x86":
						installed = IsVcRedist2015_2022Installed("x86");
						version = GetVcRuntimeVersion("x86");
						break;
					case "dotnet-framework-48":
						installed = IsDotNet48Installed();
						version = GetDotNetFramework48Version();
						break;
					case "directx-june-2010":
						installed = IsDirectXJun2010Installed();
						break;
					case "dotnet-desktop-8-x64":
						installed = IsDotNetDesktopRuntimeInstalled(8, "x64", out version);
						break;
					case "dotnet-desktop-8-x86":
						installed = IsDotNetDesktopRuntimeInstalled(8, "x86", out version);
						break;
					case "dotnet-desktop-10-x64":
						installed = IsDotNetDesktopRuntimeInstalled(10, "x64", out version);
						detail = installed == true
							? "Runtime LTS moderno preferencial detectado."
							: "Use somente o instalador x64 atual publicado pela Microsoft.";
						break;
					case "dotnet-desktop-10-x86":
						installed = IsDotNetDesktopRuntimeInstalled(10, "x86", out version);
						break;
					case "webview2":
						installed = IsWebView2Installed();
						version = GetWebView2Version();
						break;
					case "dotnet-framework-35":
						installed = IsDotNet35Installed();
						detail = installed == true
							? ".NET Framework 3.5 detectado."
							: profile != null && profile.OsBuild >= NetFx35StandaloneInstallerBuild
								? "Windows 11 26H1/build 28000+: use o instalador oficial específico do .NET Framework 3.5."
								: "Windows 11 25H2 ou anterior: ative o recurso opcional NetFx3 do próprio Windows.";
						break;
					case "xna-framework-40":
						installed = IsXnaFrameworkInstalled();
						break;
					case "vulkan-loader":
						installed = profile != null && profile.VulkanLoaderPresent;
						version = profile == null ? string.Empty : profile.VulkanLoaderVersion;
						detail = installed == true ? "Loader Vulkan fornecido pelo driver foi detectado." : "Atualize o driver oficial; não use instalador Vulkan genérico.";
						break;
					case "directx-12":
						installed = profile != null && profile.DirectX12RuntimePresent && IsFeatureLevelAtLeast(profile.Direct3DFeatureLevel, 12, 0);
						version = profile == null ? string.Empty : profile.Direct3DFeatureLevel;
						detail = installed == true
							? "Runtime D3D12 e feature level 12_0 ou superior detectados."
							: "DirectX 12 completo depende do Windows, do driver e do feature level da GPU.";
						break;
					case "openal":
						installed = IsOpenAlInstalled();
						break;
					case "media-feature-pack":
						if (!IsWindowsMediaEdition(profile))
						{
							result.State = GamingReadinessState.NotApplicable;
							result.Detail = "A edição do Windows não aparenta ser N/KN.";
							return result;
						}
						installed = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mfplat.dll"));
						break;
					case "gaming-services":
						installed = RegistryKeyExists(RegistryView.Registry64, @"SYSTEM\CurrentControlSet\Services\GamingServices") ||
							RegistryKeyExists(RegistryView.Registry64, @"SYSTEM\CurrentControlSet\Services\GamingServicesNet");
						break;
					case "physx":
						installed = IsProductInstalled("NVIDIA PhysX");
						version = GetInstalledProductVersion("NVIDIA PhysX");
						break;
					case "java-runtime":
						installed = IsJavaInstalled(out version);
						break;
					case "dotnet-desktop-current":
						installed = IsDotNetDesktopRuntimeInstalledAtLeast(9, out version);
						break;
					case "dokany":
						installed = IsDokanyInstalled();
						break;
					case "winfsp":
						installed = IsWinFspInstalled();
						break;
					default:
						if (component.DetectionKey.StartsWith("vc-legacy-", StringComparison.OrdinalIgnoreCase))
						{
							string[] parts = component.DetectionKey.Split('-');
							if (parts.Length >= 4)
							{
								installed = IsLegacyVcRedistInstalled(parts[2], parts[3]);
							}
						}
						break;
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Runtime detection failed for " + component.Id + ": " + ex.Message);
				installed = null;
				detail = "Falha ao consultar este componente: " + ex.Message;
			}

			if (installed == true && RuntimeVersionPolicy.RequiresMinimumVersion(component))
			{
				try
				{
					string requiredProductVersion;
					RuntimeVersionComparison comparison = RuntimeVersionPolicy.Evaluate(
						component,
						version,
						out requiredProductVersion);
					if (comparison == RuntimeVersionComparison.Outdated)
					{
						installed = false;
						detail = "Versão detectada " + version + " é anterior ao mínimo aprovado no pacote (" + requiredProductVersion + ").";
					}
					else if (comparison != RuntimeVersionComparison.Current)
					{
						installed = null;
						detail = "A versão instalada não pôde ser comparada com o mínimo aprovado no pacote (" + requiredProductVersion + ").";
					}
				}
				catch (Exception ex)
				{
					Logger.Log("Runtime version policy failed for " + component.Id + ": " + ex.Message);
					installed = null;
					detail = "Não foi possível validar a versão instalada contra o catálogo incorporado.";
				}
			}

			result.DetectedVersion = version ?? string.Empty;
			if (installed == true)
			{
				result.State = GamingReadinessState.Ready;
				result.Detail = string.IsNullOrWhiteSpace(detail)
					? (string.IsNullOrWhiteSpace(version) ? "Detectado no sistema." : "Versão detectada: " + version + ".")
					: detail;
			}
			else if (installed == false)
			{
				result.State = component.Tier == GamingRuntimeTier.Required ? GamingReadinessState.Blocked : GamingReadinessState.Attention;
				result.Detail = string.IsNullOrWhiteSpace(detail)
					? (component.Tier == GamingRuntimeTier.Optional ? "Opcional; não detectado." : "Não detectado no sistema.")
					: detail;
			}
			else
			{
				result.State = GamingReadinessState.Unknown;
				result.Detail = string.IsNullOrWhiteSpace(detail) ? "Estado não confirmado." : detail;
			}

			return result;
		}

		public static string GetOfficialGpuDriverUrl(string vendor)
		{
			if (string.Equals(vendor, "NVIDIA", StringComparison.OrdinalIgnoreCase))
			{
				return "https://www.nvidia.com/Download/index.aspx";
			}
			if (string.Equals(vendor, "AMD", StringComparison.OrdinalIgnoreCase))
			{
				return "https://www.amd.com/support/download/drivers.html";
			}
			if (string.Equals(vendor, "Intel", StringComparison.OrdinalIgnoreCase))
			{
				return "https://www.intel.com/content/www/us/en/support/detect.html";
			}

			return "https://support.microsoft.com/windows/update-drivers-through-device-manager-in-windows";
		}

		public static bool IsDotNetDesktopRuntimeInstalled(int majorVersion, string architecture, out string detectedVersion)
		{
			detectedVersion = string.Empty;
			List<Version> versions = GetDotNetDesktopRuntimeVersions(architecture);
			Version selected = versions.Where(version => version.Major == majorVersion).OrderByDescending(version => version).FirstOrDefault();
			if (selected == null)
			{
				return false;
			}

			detectedVersion = selected.ToString();
			return true;
		}

		private static bool IsDotNetDesktopRuntimeInstalledAtLeast(int minimumMajor, out string detectedVersion)
		{
			detectedVersion = string.Empty;
			List<Version> versions = new List<Version>();
			versions.AddRange(GetDotNetDesktopRuntimeVersions("x64"));
			versions.AddRange(GetDotNetDesktopRuntimeVersions("x86"));
			Version selected = versions.Where(version => version.Major >= minimumMajor).OrderByDescending(version => version).FirstOrDefault();
			if (selected == null)
			{
				return false;
			}
			detectedVersion = selected.ToString();
			return true;
		}

		private static List<Version> GetDotNetDesktopRuntimeVersions(string architecture)
		{
			HashSet<Version> versions = new HashSet<Version>();
			RegistryView view = string.Equals(architecture, "x64", StringComparison.OrdinalIgnoreCase) && Environment.Is64BitOperatingSystem
				? RegistryView.Registry64
				: RegistryView.Registry32;

			try
			{
				using (RegistryKey key = OpenLocalMachineSubKey(view, @"SOFTWARE\dotnet\Setup\InstalledVersions\" + architecture + @"\sharedfx\Microsoft.WindowsDesktop.App"))
				{
					if (key != null)
					{
						foreach (string versionName in key.GetValueNames())
						{
							Version parsed;
							if (Version.TryParse(versionName, out parsed))
							{
								versions.Add(parsed);
							}
						}
						foreach (string versionName in key.GetSubKeyNames())
						{
							Version parsed;
							if (Version.TryParse(versionName, out parsed))
							{
								versions.Add(parsed);
							}
						}
					}
				}
			}
			catch
			{
			}

			foreach (string root in GetDotNetRoots(architecture))
			{
				try
				{
					string sharedFx = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
					if (!Directory.Exists(sharedFx))
					{
						continue;
					}
					foreach (string directory in Directory.GetDirectories(sharedFx))
					{
						Version parsed;
						if (Version.TryParse(Path.GetFileName(directory), out parsed))
						{
							versions.Add(parsed);
						}
					}
				}
				catch
				{
				}
			}

			return versions.OrderByDescending(version => version).ToList();
		}

		private static IEnumerable<string> GetDotNetRoots(string architecture)
		{
			HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string root = string.Equals(architecture, "x86", StringComparison.OrdinalIgnoreCase)
				? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
				: Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			if (!string.IsNullOrWhiteSpace(root))
			{
				roots.Add(Path.Combine(root, "dotnet"));
			}

			string configured = Environment.GetEnvironmentVariable(string.Equals(architecture, "x86", StringComparison.OrdinalIgnoreCase) ? "DOTNET_ROOT(x86)" : "DOTNET_ROOT");
			if (!string.IsNullOrWhiteSpace(configured))
			{
				roots.Add(configured);
			}
			return roots;
		}

		private static void TryDetectOperatingSystem(GamingReadinessProfile profile)
		{
			try
			{
				using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem"))
				using (ManagementObjectCollection results = searcher.Get())
				{
					foreach (ManagementObject item in results)
					{
						profile.OsCaption = GetManagementString(item, "Caption");
						profile.OsVersion = GetManagementString(item, "Version");
						profile.OsBuild = GetManagementInt32(item, "BuildNumber");
						string architecture = GetManagementString(item, "OSArchitecture");
						if (!string.IsNullOrWhiteSpace(architecture))
						{
							profile.OsArchitecture = architecture;
						}
						break;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("OS readiness detection failed: " + ex.Message);
			}

			if (string.IsNullOrWhiteSpace(profile.OsCaption))
			{
				profile.OsCaption = "Microsoft Windows";
				profile.OsVersion = Environment.OSVersion.Version.ToString();
				profile.OsBuild = Environment.OSVersion.Version.Build;
			}
		}

		private static void TryDetectCpuAndMemory(GamingReadinessProfile profile)
		{
			try
			{
				using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
					"SELECT Name, NumberOfCores, NumberOfLogicalProcessors, AddressWidth, VirtualizationFirmwareEnabled, SecondLevelAddressTranslationExtensions FROM Win32_Processor"))
				using (ManagementObjectCollection results = searcher.Get())
				{
					List<string> names = new List<string>();
					foreach (ManagementObject item in results)
					{
						string name = GetManagementString(item, "Name");
						if (!string.IsNullOrWhiteSpace(name))
						{
							names.Add(NormalizeWhitespace(name));
						}
						profile.PhysicalCoreCount += GetManagementInt32(item, "NumberOfCores");
						profile.LogicalProcessorCount += GetManagementInt32(item, "NumberOfLogicalProcessors");
						profile.CpuAddressWidth = Math.Max(profile.CpuAddressWidth, GetManagementInt32(item, "AddressWidth"));
						profile.VirtualizationFirmwareEnabled = MergeBoolean(profile.VirtualizationFirmwareEnabled, GetManagementNullableBoolean(item, "VirtualizationFirmwareEnabled"));
						profile.SecondLevelAddressTranslation = MergeBoolean(profile.SecondLevelAddressTranslation, GetManagementNullableBoolean(item, "SecondLevelAddressTranslationExtensions"));
					}
					profile.CpuName = string.Join(" + ", names.Distinct(StringComparer.OrdinalIgnoreCase));
				}
			}
			catch (Exception ex)
			{
				Logger.Log("CPU readiness detection failed: " + ex.Message);
			}

			try
			{
				using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
				using (ManagementObjectCollection results = searcher.Get())
				{
					foreach (ManagementObject item in results)
					{
						profile.PhysicalMemoryBytes = GetManagementInt64(item, "TotalPhysicalMemory");
						break;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Memory readiness detection failed: " + ex.Message);
			}

			if (profile.LogicalProcessorCount <= 0)
			{
				profile.LogicalProcessorCount = Environment.ProcessorCount;
			}
			if (string.IsNullOrWhiteSpace(profile.CpuName))
			{
				profile.CpuName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Processador não identificado";
			}
		}

		private static void TryDetectSystemDrive(GamingReadinessProfile profile)
		{
			try
			{
				string root = Path.GetPathRoot(Environment.SystemDirectory);
				DriveInfo drive = new DriveInfo(root);
				profile.SystemDrive = drive.Name;
				if (drive.IsReady)
				{
					profile.SystemDriveTotalBytes = drive.TotalSize;
					profile.SystemDriveFreeBytes = drive.AvailableFreeSpace;
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Disk readiness detection failed: " + ex.Message);
			}
		}

		private static void TryDetectGpus(GamingReadinessProfile profile)
		{
			try
			{
				using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
					"SELECT Name, AdapterCompatibility, DriverVersion, DriverDate, PNPDeviceID, AdapterRAM FROM Win32_VideoController"))
				using (ManagementObjectCollection results = searcher.Get())
				{
					foreach (ManagementObject item in results)
					{
						string name = GetManagementString(item, "Name");
						if (string.IsNullOrWhiteSpace(name))
						{
							continue;
						}
						string adapter = GetManagementString(item, "AdapterCompatibility");
						string pnp = GetManagementString(item, "PNPDeviceID");
						string searchable = name + " " + adapter + " " + pnp;
						profile.MutableGpus.Add(new GamingGpuInfo
						{
							Name = NormalizeWhitespace(name),
							Vendor = DetectGpuVendor(searchable),
							DriverVersion = GetManagementString(item, "DriverVersion"),
							DriverDate = GetManagementDateTime(item, "DriverDate"),
							PnpDeviceId = pnp,
							AdapterRamBytes = GetManagementInt64(item, "AdapterRAM"),
							UsesBasicDisplayDriver = searchable.IndexOf("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase) >= 0,
							IsLikelySoftwareAdapter = IsSoftwareVideoAdapter(searchable)
						});
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("GPU readiness detection failed: " + ex.Message);
			}
		}

		private static void TryDetectGraphicsApis(GamingReadinessProfile profile)
		{
			profile.OpenGlLoaderPresent = FindSystemFile("opengl32.dll") != null;
			profile.DirectX12RuntimePresent = FindSystemFile("d3d12.dll") != null;
			string vulkanPath = FindSystemFile("vulkan-1.dll");
			profile.VulkanLoaderPresent = !string.IsNullOrWhiteSpace(vulkanPath) || HasRegisteredVulkanDriver();
			if (!string.IsNullOrWhiteSpace(vulkanPath))
			{
				try
				{
					profile.VulkanLoaderVersion = FileVersionInfo.GetVersionInfo(vulkanPath).FileVersion;
				}
				catch
				{
				}
			}

			string featureLevel;
			profile.Direct3DProbeSucceeded = TryGetDirect3DFeatureLevel(out featureLevel);
			profile.Direct3DFeatureLevel = featureLevel;
		}

		private static void BuildReadinessFindings(GamingReadinessProfile profile)
		{
			if (!profile.Is64BitOperatingSystem)
			{
				AddFinding(profile, "os-32bit", "Windows de 32 bits", "Jogos e emuladores atuais normalmente exigem 64 bits.",
					"Instale uma edição Windows de 64 bits compatível com o processador.", GamingReadinessState.Blocked,
					"https://support.microsoft.com/windows/32-bit-and-64-bit-windows-frequently-asked-questions");
			}

			if (profile.OsBuild > 0 && profile.OsBuild < MinimumModernWindowsBuild)
			{
				AddFinding(profile, "windows-build", "Windows antigo", "Build detectado: " + profile.OsBuild + ".",
					"Atualize o Windows antes de instalar jogos modernos.", GamingReadinessState.Blocked,
					"https://support.microsoft.com/windows/windows-update");
			}

			if (profile.PendingRestart)
			{
				AddFinding(profile, "pending-restart", "Reinicialização pendente", "Uma instalação ou atualização aguarda reinicialização.",
					"Reinicie o PC e execute o diagnóstico novamente.", GamingReadinessState.Attention, string.Empty);
			}

			if (profile.PhysicalMemoryBytes > 0 && profile.PhysicalMemoryBytes < 4L * OneGigabyte)
			{
				AddFinding(profile, "ram-critical", "Memória insuficiente", profile.MemoryDisplay + " de RAM detectada.",
					"Use pelo menos 4 GB para emulação básica; jogos atuais geralmente precisam de 16 GB ou mais.", GamingReadinessState.Blocked, string.Empty);
			}
			else if (profile.PhysicalMemoryBytes > 0 && profile.PhysicalMemoryBytes < 16L * OneGigabyte)
			{
				AddFinding(profile, "ram-recommended", "RAM abaixo do recomendado para jogos atuais", profile.MemoryDisplay + " de RAM detectada.",
					"8 GB atendem muitos emuladores; 16 GB ou mais oferecem melhor compatibilidade com jogos novos.", GamingReadinessState.Attention, string.Empty);
			}

			if (profile.SystemDriveFreeBytes > 0 && profile.SystemDriveFreeBytes < 10L * OneGigabyte)
			{
				AddFinding(profile, "disk-critical", "Pouco espaço no disco do sistema", profile.SystemDriveFreeDisplay + " livres.",
					"Libere espaço antes de instalar runtimes, atualizações e caches.", GamingReadinessState.Blocked, string.Empty);
			}
			else if (profile.SystemDriveFreeBytes > 0 && profile.SystemDriveFreeBytes < 40L * OneGigabyte)
			{
				AddFinding(profile, "disk-recommended", "Espaço livre limitado", profile.SystemDriveFreeDisplay + " livres.",
					"Reserve espaço adicional para atualizações, shaders e caches; jogos devem usar armazenamento legal fornecido pelo usuário.", GamingReadinessState.Attention, string.Empty);
			}

			if (profile.LogicalProcessorCount > 0 && profile.LogicalProcessorCount < 4)
			{
				AddFinding(profile, "cpu-threads", "CPU limitada", profile.LogicalProcessorCount + " processadores lógicos detectados.",
					"Emuladores de gerações recentes e jogos novos normalmente precisam de quatro ou mais threads.", GamingReadinessState.Attention, string.Empty);
			}

			if (profile.Gpus.Count == 0)
			{
				AddFinding(profile, "gpu-unknown", "GPU não identificada", "O Windows não retornou um adaptador de vídeo.",
					"Conclua o Windows Update e instale o driver oficial da GPU.", GamingReadinessState.Blocked,
					"https://support.microsoft.com/windows/update-drivers-through-device-manager-in-windows");
			}
			else if (profile.Gpus.All(gpu => gpu.UsesBasicDisplayDriver || gpu.IsLikelySoftwareAdapter))
			{
				GamingGpuInfo gpu = profile.Gpus.First();
				AddFinding(profile, "gpu-basic-driver", "Driver gráfico básico", gpu.Name,
					"Instale o driver oficial da GPU; o TurboRama não escolhe nem instala drivers automaticamente.", GamingReadinessState.Blocked,
					GetOfficialGpuDriverUrl(gpu.Vendor));
			}

			foreach (GamingGpuInfo gpu in profile.Gpus.Where(item => item.DriverDate.HasValue && !item.IsLikelySoftwareAdapter))
			{
				if ((DateTime.Now - gpu.DriverDate.Value).TotalDays > 730)
				{
					AddFinding(profile, "gpu-driver-age-" + gpu.Vendor, "Driver de GPU antigo", gpu.Name + " — " + gpu.DriverDate.Value.ToShortDateString(),
						"Confira um driver compatível no site oficial do fabricante. Não force versões incompatíveis.", GamingReadinessState.Attention,
						GetOfficialGpuDriverUrl(gpu.Vendor));
				}
			}

			if (!profile.Direct3DProbeSucceeded)
			{
				AddFinding(profile, "d3d-probe", "Direct3D não confirmado", "Não foi possível criar um dispositivo Direct3D de hardware.",
					"Instale o driver oficial da GPU e repita o diagnóstico fora de uma sessão remota.", GamingReadinessState.Attention,
					"https://support.microsoft.com/windows/which-version-of-directx-is-on-your-pc");
			}
			else if (!IsFeatureLevelAtLeast(profile.Direct3DFeatureLevel, 11, 0))
			{
				AddFinding(profile, "d3d-feature-level", "Feature level Direct3D insuficiente", profile.Direct3DFeatureLevel,
					"Muitos emuladores e jogos atuais exigem D3D feature level 11_0 ou superior.", GamingReadinessState.Blocked,
					"https://support.microsoft.com/windows/which-version-of-directx-is-on-your-pc");
			}

			if (profile.VirtualizationFirmwareEnabled == false)
			{
				AddFinding(profile, "virtualization", "Virtualização desativada", "A CPU informa virtualização de firmware desativada.",
					"Ative VT-x/AMD-V no firmware somente se usar emuladores Android, máquinas virtuais ou recursos que a exijam.", GamingReadinessState.Attention,
					"https://support.microsoft.com/windows/enable-virtualization-on-windows");
			}

			foreach (RuntimeComponentStatus status in profile.RuntimeStatuses)
			{
				if (status.Component == null || status.State == GamingReadinessState.Ready || status.State == GamingReadinessState.NotApplicable)
				{
					continue;
				}
				if (status.Component.Tier == GamingRuntimeTier.Required || status.Component.Tier == GamingRuntimeTier.Recommended)
				{
					GamingReadinessState state = status.Component.Tier == GamingRuntimeTier.Required
						? GamingReadinessState.Blocked
						: GamingReadinessState.Attention;
					AddFinding(profile, "runtime-" + status.Component.Id, status.Component.DisplayName, status.Detail,
						status.BundleAvailable && status.Component.CanInstallOffline
							? "O pacote offline está disponível e será validado antes da execução."
							: "Use somente a fonte oficial indicada para este componente.",
						state, status.Component.OfficialUrl);
				}
			}
		}

		private static void CalculateReadinessScore(GamingReadinessProfile profile)
		{
			int score = 100;
			foreach (GamingReadinessFinding finding in profile.Findings)
			{
				if (finding.State == GamingReadinessState.Blocked)
				{
					score -= 14;
				}
				else if (finding.State == GamingReadinessState.Attention)
				{
					score -= 4;
				}
			}
			profile.Score = Math.Max(0, Math.Min(100, score));
			profile.OverallState = profile.Findings.Any(item => item.State == GamingReadinessState.Blocked)
				? GamingReadinessState.Blocked
				: profile.Findings.Any(item => item.State == GamingReadinessState.Attention)
					? GamingReadinessState.Attention
					: GamingReadinessState.Ready;
		}

		private static void AddFinding(
			GamingReadinessProfile profile,
			string code,
			string title,
			string detail,
			string recommendation,
			GamingReadinessState state,
			string officialUrl)
		{
			if (profile.MutableFindings.Any(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase)))
			{
				return;
			}
			profile.MutableFindings.Add(new GamingReadinessFinding
			{
				Code = code,
				Title = title,
				Detail = detail,
				Recommendation = recommendation,
				State = state,
				OfficialUrl = officialUrl
			});
		}

		private static string GetVcRuntimeVersion(string architecture)
		{
			return GetRegistryValueString(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\" + architecture, "Version");
		}

		private static string GetDotNetFramework48Version()
		{
			string value = GetRegistryValueString(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Version");
			return value;
		}

		private static string GetWebView2Version()
		{
			string[] paths =
			{
				@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
				@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
			};
			foreach (string path in paths)
			{
				string version = GetRegistryValueString(path, "pv");
				if (!string.IsNullOrWhiteSpace(version))
				{
					return version;
				}
			}
			return string.Empty;
		}

		private static string GetRegistryValueString(string path, string valueName)
		{
			foreach (RegistryView view in GetRegistryViews())
			{
				try
				{
					using (RegistryKey key = OpenLocalMachineSubKey(view, path))
					{
						object value = key == null ? null : key.GetValue(valueName);
						if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
						{
							return value.ToString();
						}
					}
				}
				catch
				{
				}
			}
			return string.Empty;
		}

		private static bool RegistryKeyExists(RegistryView view, string path)
		{
			try
			{
				using (RegistryKey key = OpenLocalMachineSubKey(view, path))
				{
					return key != null;
				}
			}
			catch
			{
				return false;
			}
		}

		private static bool IsProductInstalled(string productName)
		{
			return EnumerateUninstallDisplayNames().Any(name => name.IndexOf(productName, StringComparison.OrdinalIgnoreCase) >= 0);
		}

		private static string GetInstalledProductVersion(string productName)
		{
			string[] uninstallRoots =
			{
				@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
				@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
			};
			foreach (RegistryView view in GetRegistryViews())
			{
				foreach (string root in uninstallRoots)
				{
					try
					{
						using (RegistryKey rootKey = OpenLocalMachineSubKey(view, root))
						{
							if (rootKey == null)
							{
								continue;
							}
							foreach (string childName in rootKey.GetSubKeyNames())
							{
								using (RegistryKey child = rootKey.OpenSubKey(childName))
								{
									string name = Convert.ToString(child == null ? null : child.GetValue("DisplayName"));
									if (!string.IsNullOrWhiteSpace(name) && name.IndexOf(productName, StringComparison.OrdinalIgnoreCase) >= 0)
									{
										return Convert.ToString(child.GetValue("DisplayVersion"));
									}
								}
							}
						}
					}
					catch
					{
					}
				}
			}
			return string.Empty;
		}

		private static bool IsJavaInstalled(out string version)
		{
			version = GetRegistryValueString(@"SOFTWARE\JavaSoft\JDK", "CurrentVersion");
			if (string.IsNullOrWhiteSpace(version))
			{
				version = GetRegistryValueString(@"SOFTWARE\Eclipse Adoptium\JDK", "CurrentVersion");
			}
			if (!string.IsNullOrWhiteSpace(version))
			{
				return true;
			}
			return IsProductInstalled("Temurin") || IsProductInstalled("Java(TM)") || IsProductInstalled("OpenJDK");
		}

		private static bool IsWindowsMediaEdition(GamingReadinessProfile profile)
		{
			string edition = GetRegistryValueString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID");
			string text = (edition + " " + (profile == null ? string.Empty : profile.OsCaption)).ToUpperInvariant();
			return text.EndsWith("N", StringComparison.Ordinal) || text.Contains(" N ") || text.Contains("KN");
		}

		private static bool IsRestartPending()
		{
			foreach (RegistryView view in GetRegistryViews())
			{
				if (RegistryKeyExists(view, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") ||
					RegistryKeyExists(view, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
				{
					return true;
				}
				try
				{
					using (RegistryKey key = OpenLocalMachineSubKey(view, @"SYSTEM\CurrentControlSet\Control\Session Manager"))
					{
						if (key != null && key.GetValue("PendingFileRenameOperations") != null)
						{
							return true;
						}
					}
				}
				catch
				{
				}
			}
			return false;
		}

		private static bool HasRegisteredVulkanDriver()
		{
			foreach (RegistryView view in GetRegistryViews())
			{
				try
				{
					using (RegistryKey key = OpenLocalMachineSubKey(view, @"SOFTWARE\Khronos\Vulkan\Drivers"))
					{
						if (key != null && key.GetValueNames().Length > 0)
						{
							return true;
						}
					}
				}
				catch
				{
				}
			}
			return false;
		}

		private static string FindSystemFile(string fileName)
		{
			foreach (string folder in GetSystemDllSearchFolders())
			{
				try
				{
					string candidate = Path.Combine(folder, fileName);
					if (File.Exists(candidate))
					{
						return candidate;
					}
				}
				catch
				{
				}
			}
			return null;
		}

		private static string DetectGpuVendor(string text)
		{
			string lower = (text ?? string.Empty).ToLowerInvariant();
			if (lower.Contains("nvidia") || lower.Contains("ven_10de"))
			{
				return "NVIDIA";
			}
			if (lower.Contains("amd") || lower.Contains("ati") || lower.Contains("radeon") || lower.Contains("ven_1002"))
			{
				return "AMD";
			}
			if (lower.Contains("intel") || lower.Contains("iris") || lower.Contains("uhd") || lower.Contains("ven_8086"))
			{
				return "Intel";
			}
			if (lower.Contains("microsoft basic"))
			{
				return "Microsoft Basic Display";
			}
			return "Desconhecido";
		}

		private static bool IsSoftwareVideoAdapter(string text)
		{
			string lower = (text ?? string.Empty).ToLowerInvariant();
			return lower.Contains("remote display") || lower.Contains("indirect display") || lower.Contains("parsec") ||
				lower.Contains("virtualbox") || lower.Contains("vmware svga") || lower.Contains("hyper-v video");
		}

		private static bool IsFeatureLevelAtLeast(string value, int minimumMajor, int minimumMinor)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return false;
			}
			string cleaned = value.Replace("FL ", string.Empty).Replace('_', '.').Trim();
			Version parsed;
			if (!Version.TryParse(cleaned, out parsed))
			{
				return false;
			}
			return parsed.Major > minimumMajor || (parsed.Major == minimumMajor && parsed.Minor >= minimumMinor);
		}

		private static bool TryGetDirect3DFeatureLevel(out string featureLevel)
		{
			featureLevel = string.Empty;
			int[] preferredLevels = { 0xc100, 0xc000, 0xb100, 0xb000, 0xa100, 0xa000, 0x9300, 0x9200, 0x9100 };
			IntPtr device = IntPtr.Zero;
			IntPtr context = IntPtr.Zero;
			int selected;
			try
			{
				int result = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0, preferredLevels, (uint)preferredLevels.Length, 7, out device, out selected, out context);
				if (result < 0)
				{
					return false;
				}
				featureLevel = FormatFeatureLevel(selected);
				return !string.IsNullOrWhiteSpace(featureLevel);
			}
			catch (DllNotFoundException)
			{
				return false;
			}
			catch (EntryPointNotFoundException)
			{
				return false;
			}
			catch (Exception ex)
			{
				Logger.Log("Direct3D feature-level probe failed: " + ex.Message);
				return false;
			}
			finally
			{
				if (context != IntPtr.Zero)
				{
					Marshal.Release(context);
				}
				if (device != IntPtr.Zero)
				{
					Marshal.Release(device);
				}
			}
		}

		private static string FormatFeatureLevel(int featureLevel)
		{
			switch (featureLevel)
			{
				case 0xc100: return "12_1";
				case 0xc000: return "12_0";
				case 0xb100: return "11_1";
				case 0xb000: return "11_0";
				case 0xa100: return "10_1";
				case 0xa000: return "10_0";
				case 0x9300: return "9_3";
				case 0x9200: return "9_2";
				case 0x9100: return "9_1";
				default: return string.Empty;
			}
		}

		private static string GetManagementString(ManagementBaseObject item, string propertyName)
		{
			try
			{
				object value = item[propertyName];
				return value == null ? string.Empty : value.ToString();
			}
			catch
			{
				return string.Empty;
			}
		}

		private static int GetManagementInt32(ManagementBaseObject item, string propertyName)
		{
			try
			{
				object value = item[propertyName];
				return value == null ? 0 : Convert.ToInt32(value);
			}
			catch
			{
				return 0;
			}
		}

		private static long GetManagementInt64(ManagementBaseObject item, string propertyName)
		{
			try
			{
				object value = item[propertyName];
				return value == null ? 0L : Convert.ToInt64(value);
			}
			catch
			{
				return 0L;
			}
		}

		private static bool? GetManagementNullableBoolean(ManagementBaseObject item, string propertyName)
		{
			try
			{
				object value = item[propertyName];
				return value == null ? (bool?)null : Convert.ToBoolean(value);
			}
			catch
			{
				return null;
			}
		}

		private static DateTime? GetManagementDateTime(ManagementBaseObject item, string propertyName)
		{
			try
			{
				string value = GetManagementString(item, propertyName);
				return string.IsNullOrWhiteSpace(value) ? (DateTime?)null : ManagementDateTimeConverter.ToDateTime(value);
			}
			catch
			{
				return null;
			}
		}

		private static bool? MergeBoolean(bool? current, bool? candidate)
		{
			if (current == true || candidate == true)
			{
				return true;
			}
			if (current == false || candidate == false)
			{
				return false;
			}
			return null;
		}

		private static string NormalizeWhitespace(string value)
		{
			return string.Join(" ", (value ?? string.Empty).Split(new char[0], StringSplitOptions.RemoveEmptyEntries));
		}

		[DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
		private static extern int D3D11CreateDevice(
			IntPtr adapter,
			int driverType,
			IntPtr software,
			uint flags,
			[In] int[] featureLevels,
			uint featureLevelCount,
			uint sdkVersion,
			out IntPtr device,
			out int selectedFeatureLevel,
			out IntPtr immediateContext);
	}
}
