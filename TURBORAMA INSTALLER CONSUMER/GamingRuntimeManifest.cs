using System;
using System.Collections.Generic;
using System.Linq;

namespace InstallerHost
{
	internal static class GamingRuntimeManifest
	{
		private static readonly string[] MicrosoftPublisherTokens =
		{
			"Microsoft Corporation",
			"Microsoft Windows"
		};

		private static readonly string[] DokanyPublisherTokens =
		{
			"LEOSAC"
		};

		private static readonly string[] WinFspPublisherTokens =
		{
			"NAVIMATICS LLC"
		};

		private static readonly string[] AdoptiumPublisherTokens = { "Eclipse Foundation" };

		private static readonly List<GamingRuntimeComponent> Components = BuildComponents();

		// Mantidos para compatibilidade com o empacotador existente. A lista obrigatória
		// agora contém somente componentes seguros que fazem parte do plano padrão.
		public static readonly string[] RequiredBundleFiles = BuildBundleFileList(true);
		public static readonly string[] OptionalBundleFiles = BuildBundleFileList(false);
		public static readonly string[][] BundleFileAliases = BuildBundleAliases();

		public static IList<GamingRuntimeComponent> GetComponents()
		{
			return Components.AsReadOnly();
		}

		public static GamingRuntimeComponent FindById(string id)
		{
			return Components.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
		}

		public static GamingRuntimeComponent FindByBundleFile(string fileName)
		{
			string name = PathFileName(fileName);
			return Components.FirstOrDefault(item =>
				!string.IsNullOrWhiteSpace(item.BundleFileName) &&
				string.Equals(item.BundleFileName, name, StringComparison.OrdinalIgnoreCase));
		}

		public static Dictionary<string, InstallerInfo> GetLegacyVcRedistPackages()
		{
			Dictionary<string, InstallerInfo> packages = new Dictionary<string, InstallerInfo>(StringComparer.OrdinalIgnoreCase);
			foreach (GamingRuntimeComponent component in Components.Where(item => item.IsLegacy && item.CanInstallOffline))
			{
				string arguments = component.Id.IndexOf("2005", StringComparison.OrdinalIgnoreCase) >= 0 ? "/q" :
					component.Id.IndexOf("2008", StringComparison.OrdinalIgnoreCase) >= 0 ? "/qb" : "/passive /norestart";
				packages[component.BundleFileName] = new InstallerInfo(string.Empty, arguments);
			}

			return packages;
		}

		public static bool IsApplicableToCurrentOs(GamingRuntimeComponent component)
		{
			if (component == null)
			{
				return false;
			}

			return !string.Equals(component.Architecture, "x64", StringComparison.OrdinalIgnoreCase) || Environment.Is64BitOperatingSystem;
		}

		private static List<GamingRuntimeComponent> BuildComponents()
		{
			List<GamingRuntimeComponent> items = new List<GamingRuntimeComponent>();

			items.Add(CreateGuidance(
				"windows-update", "Windows Update", GamingRuntimeCategory.Windows, GamingRuntimeTier.Required,
				"windows-update", "Microsoft", "https://support.microsoft.com/windows/windows-update",
				"Atualizações de segurança, DirectX, certificados raiz e correções de compatibilidade."));

			items.Add(CreateGuidance(
				"gpu-driver", "Driver oficial da GPU", GamingRuntimeCategory.GraphicsDriver, GamingRuntimeTier.Required,
				"gpu-driver", "Fabricante da GPU", "https://support.microsoft.com/windows/update-drivers-through-device-manager-in-windows",
				"Driver do fabricante para Direct3D, Vulkan e OpenGL. Nunca é instalado genericamente pelo TurboRama."));

			items.Add(CreatePackage(
				"vc-modern-x64", "Microsoft Visual C++ v14 x64 (atual)", GamingRuntimeCategory.MicrosoftRuntime, GamingRuntimeTier.Required,
				"x64", "vc-modern-x64", "Microsoft Corporation", "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist",
				"Runtime atual de jogos e emuladores nativos de 64 bits.", "vc_redist.x64.exe", "vc_redist.x64.exe", true, false,
				new string[0], MicrosoftPublisherTokens));

			items.Add(CreatePackage(
				"vc-modern-x86", "Microsoft Visual C++ v14 x86 (atual)", GamingRuntimeCategory.MicrosoftRuntime, GamingRuntimeTier.Required,
				"x86", "vc-modern-x86", "Microsoft Corporation", "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist",
				"Runtime de 32 bits ainda usado por launchers, jogos e emuladores.", "vc_redist.x86.exe", "vc_redist.x86.exe", true, false,
				new string[0], MicrosoftPublisherTokens));

			items.Add(CreatePackage(
				"dotnet-framework-48", ".NET Framework 4.8", GamingRuntimeCategory.MicrosoftRuntime, GamingRuntimeTier.Required,
				"any", "dotnet-framework-48", "Microsoft Corporation", "https://dotnet.microsoft.com/download/dotnet-framework/net48",
				"Compatibilidade com aplicações Windows clássicas e com o próprio instalador.", "NDP48-x86-x64-AllOS-ENU.exe", "NDP48-x86-x64-AllOS-ENU.exe", true, false,
				new string[] { "NDP48-Web.exe" }, MicrosoftPublisherTokens));

			items.Add(CreatePackage(
				"directx-june-2010", "DirectX End-User Runtimes (June 2010)", GamingRuntimeCategory.LegacyGameRuntime, GamingRuntimeTier.Recommended,
				"any", "directx-june-2010", "Microsoft Corporation", "https://www.microsoft.com/download/details.aspx?id=8109",
				"Bibliotecas D3DX9/10/11 e XInput 1.3 usadas por jogos antigos; não substitui o DirectX do Windows.", "directx_Jun2010_redist.exe", "DXSETUP.exe", true, false,
				new string[0], MicrosoftPublisherTokens));

			items.Add(CreatePackage(
				"dotnet-desktop-8-x64", ".NET Desktop Runtime 8 x64", GamingRuntimeCategory.MicrosoftRuntime, GamingRuntimeTier.Recommended,
				"x64", "dotnet-desktop-8-x64", "Microsoft Corporation", "https://dotnet.microsoft.com/download/dotnet/8.0",
				"Runtime LTS usado por front-ends e ferramentas modernas de 64 bits.", "windowsdesktop-runtime-8.0-win-x64.exe", "windowsdesktop-runtime-8.0-win-x64.exe", true, false,
				new string[0], MicrosoftPublisherTokens));

			items.Add(CreatePackage(
				"dotnet-desktop-8-x86", ".NET Desktop Runtime 8 x86", GamingRuntimeCategory.MicrosoftRuntime, GamingRuntimeTier.Recommended,
				"x86", "dotnet-desktop-8-x86", "Microsoft Corporation", "https://dotnet.microsoft.com/download/dotnet/8.0",
				"Runtime para front-ends e utilitários .NET de 32 bits.", "windowsdesktop-runtime-8.0-win-x86.exe", "windowsdesktop-runtime-8.0-win-x86.exe", true, false,
				new string[0], MicrosoftPublisherTokens));

			items.Add(CreatePackage(
				"dotnet-desktop-10-x64", ".NET Desktop Runtime 10 LTS x64", GamingRuntimeCategory.MicrosoftRuntime, GamingRuntimeTier.Recommended,
				"x64", "dotnet-desktop-10-x64", "Microsoft", "https://dotnet.microsoft.com/download/dotnet/10.0",
				"Runtime LTS moderno preferencial. O pacote oficial é validado antes da instalação.",
				"windowsdesktop-runtime-10.0-win-x64.exe", "windowsdesktop-runtime-10.0-win-x64.exe", true, false,
				new string[0], MicrosoftPublisherTokens));

			items.Add(CreatePackage(
				"dotnet-desktop-10-x86", ".NET Desktop Runtime 10 LTS x86", GamingRuntimeCategory.MicrosoftRuntime, GamingRuntimeTier.Optional,
				"x86", "dotnet-desktop-10-x86", "Microsoft", "https://dotnet.microsoft.com/download/dotnet/10.0",
				"Compatibilidade de 32 bits sob demanda; não é necessária para a maioria dos PCs novos de 64 bits.",
				"windowsdesktop-runtime-10.0-win-x86.exe", "windowsdesktop-runtime-10.0-win-x86.exe", true, false,
				new string[0], MicrosoftPublisherTokens));

			items.Add(CreatePackage(
				"webview2-x64", "Microsoft Edge WebView2 Runtime", GamingRuntimeCategory.MicrosoftRuntime, GamingRuntimeTier.Recommended,
				"x64", "webview2", "Microsoft Corporation", "https://developer.microsoft.com/microsoft-edge/webview2/",
				"Interface web incorporada usada por launchers e aplicativos modernos.", "MicrosoftEdgeWebView2RuntimeInstallerX64.exe", "MicrosoftEdgeWebView2RuntimeInstallerX64.exe", true, false,
				new string[] { "MicrosoftEdgeWebview2Setup.exe" }, MicrosoftPublisherTokens));

			AddLegacyVisualCpp(items, "2005", "x86");
			AddLegacyVisualCpp(items, "2005", "x64");
			AddLegacyVisualCpp(items, "2008", "x86");
			AddLegacyVisualCpp(items, "2008", "x64");
			AddLegacyVisualCpp(items, "2010", "x86");
			AddLegacyVisualCpp(items, "2010", "x64");
			AddLegacyVisualCpp(items, "2012", "x86");
			AddLegacyVisualCpp(items, "2012", "x64");
			AddLegacyVisualCpp(items, "2013", "x86");
			AddLegacyVisualCpp(items, "2013", "x64");

			items.Add(CreateGuidance(
				"dotnet-framework-35", ".NET Framework 3.5", GamingRuntimeCategory.LegacyGameRuntime, GamingRuntimeTier.Optional,
				"dotnet-framework-35", "Microsoft", "https://learn.microsoft.com/dotnet/framework/install/dotnet-35-windows",
				"No Windows 11 25H2 e anteriores é um recurso do Windows. No Windows 11 26H1 (build 28000+) exige o instalador oficial específico."));

			items.Add(CreatePackage(
				"xna-framework-40", "Microsoft XNA Framework 4.0 Refresh", GamingRuntimeCategory.LegacyGameRuntime, GamingRuntimeTier.Optional,
				"any", "xna-framework-40", "Microsoft Corporation", "https://www.microsoft.com/download/details.aspx?id=27598",
				"Necessário apenas para alguns jogos XNA antigos.", "xnafx40_redist.msi", "xnafx40_redist.msi", true, false,
				new string[0], MicrosoftPublisherTokens));

			items.Add(CreateGuidance(
				"vulkan-loader", "Vulkan Runtime", GamingRuntimeCategory.GraphicsApi, GamingRuntimeTier.Recommended,
				"vulkan-loader", "Fabricante da GPU", "https://www.khronos.org/vulkan/",
				"É fornecido pelo driver oficial da GPU; não deve ser instalado por um pacote genérico."));

			items.Add(CreateGuidance(
				"directx-12", "DirectX 12 / Direct3D feature level", GamingRuntimeCategory.GraphicsApi, GamingRuntimeTier.Recommended,
				"directx-12", "Microsoft", "https://support.microsoft.com/windows/which-version-of-directx-is-on-your-pc",
				"A disponibilidade depende do Windows, do driver e do hardware da GPU."));

			items.Add(CreateGuidance(
				"openal", "OpenAL", GamingRuntimeCategory.LegacyGameRuntime, GamingRuntimeTier.Optional,
				"openal", "OpenAL Soft", "https://github.com/kcat/openal-soft/releases",
				"Alguns jogos antigos precisam de OpenAL. Prefira a versão incluída pelo próprio jogo ou uma distribuição oficial verificada."));

			items.Add(CreateGuidance(
				"media-feature-pack", "Media Feature Pack", GamingRuntimeCategory.Windows, GamingRuntimeTier.Optional,
				"media-feature-pack", "Microsoft", "https://support.microsoft.com/windows/media-feature-pack-for-windows-n",
				"Necessário somente nas edições Windows N/KN para codecs e APIs de mídia."));

			items.Add(CreateGuidance(
				"gaming-services", "Microsoft Gaming Services", GamingRuntimeCategory.EmulatorSupport, GamingRuntimeTier.Optional,
				"gaming-services", "Microsoft", "https://apps.microsoft.com/detail/9mwpm2cqnlhn",
				"Componente da Microsoft Store para jogos Xbox/PC Game Pass; requer conta e instalação interativa."));

			items.Add(CreateGuidance(
				"physx", "NVIDIA PhysX System Software", GamingRuntimeCategory.LegacyGameRuntime, GamingRuntimeTier.Optional,
				"physx", "NVIDIA Corporation", "https://www.nvidia.com/drivers/physx/physx-9-23-1019-driver/",
				"Necessário somente para alguns jogos antigos que usam PhysX legado."));

			AddJavaPackage(items, 8);
			AddJavaPackage(items, 17);
			AddJavaPackage(items, 21);
			AddJavaPackage(items, 25);

			items.Add(CreateGuidance(
				"dotnet-desktop-current", ".NET Desktop Runtime atual", GamingRuntimeCategory.MicrosoftRuntime, GamingRuntimeTier.Optional,
				"dotnet-desktop-current", "Microsoft", "https://dotnet.microsoft.com/download",
				"Versões futuras devem ser adicionadas sob demanda; instalar todos os runtimes sem necessidade aumenta manutenção e superfície de ataque."));

			items.Add(CreatePackage(
				"dokany", "Dokany 2.3.1 (opcional)", GamingRuntimeCategory.FileSystemSupport, GamingRuntimeTier.Optional,
				"any", "dokany", "LEOSAC", "https://github.com/dokan-dev/dokany/releases/tag/v2.3.1.1000",
				"Driver de sistema de arquivos usado somente por ferramentas compatíveis com Dokany. Instalação sempre opt-in e sem reinício automático.",
				"DokanSetup.exe", "DokanSetup.exe", true, false, new string[0], DokanyPublisherTokens));

			items.Add(CreatePackage(
				"winfsp", "WinFsp 2026 Beta 4 (opcional / pré-lançamento)", GamingRuntimeCategory.FileSystemSupport, GamingRuntimeTier.Optional,
				"any", "winfsp", "Navimatics LLC", "https://github.com/winfsp/winfsp/releases/tag/v2.2B4",
				"Pré-lançamento que corrige falhas de segurança publicadas após a versão estável 2025. Instale somente por escolha explícita; nunca reinicia o PC automaticamente.",
				"winfsp-2.2.26215.msi", "winfsp-2.2.26215.msi", true, false, new string[0], WinFspPublisherTokens));

			return items;
		}

		private static void AddJavaPackage(List<GamingRuntimeComponent> items, int major)
		{
			string id = "java-" + major + "-x64";
			string file = "temurin-jre-" + major + "-x64.msi";
			items.Add(CreatePackage(id, "Java " + major + " LTS x64 — Eclipse Temurin", GamingRuntimeCategory.EmulatorSupport,
				GamingRuntimeTier.Optional, "x64", id, "Eclipse Foundation", "https://adoptium.net/temurin/releases/?version=" + major,
				"Para jogos e ferramentas que exigem Java " + major + ". Instalação opcional lado a lado; não troca PATH, JAVA_HOME nem associações de arquivos.",
				file, file, true, false, new string[0], AdoptiumPublisherTokens));
		}

		private static void AddLegacyVisualCpp(List<GamingRuntimeComponent> items, string version, string architecture)
		{
			string id = "vc-legacy-" + version + "-" + architecture;
			string bundleName = "vcredist" + version + "_" + architecture + ".zip";
			string installerName = "vcredist" + version + "_" + architecture + ".exe";
			items.Add(CreatePackage(
				id, "Microsoft Visual C++ " + version + " " + architecture, GamingRuntimeCategory.LegacyGameRuntime, GamingRuntimeTier.Recommended,
				architecture, id, "Microsoft Corporation", "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist",
				"Compatibilidade com jogos e emuladores compilados com toolsets antigos.", bundleName, installerName, true, true,
				new string[0], MicrosoftPublisherTokens));
		}

		private static GamingRuntimeComponent CreateGuidance(
			string id,
			string displayName,
			GamingRuntimeCategory category,
			GamingRuntimeTier tier,
			string detectionKey,
			string publisher,
			string officialUrl,
			string description)
		{
			return new GamingRuntimeComponent(
				id, displayName, category, tier, "any", detectionKey, publisher, officialUrl, description,
				string.Empty, string.Empty, false, false, false, new string[0], new string[0]);
		}

		private static GamingRuntimeComponent CreatePackage(
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
			bool legacy,
			string[] aliases,
			string[] publisherTokens)
		{
			bool includedByDefault = tier == GamingRuntimeTier.Required || tier == GamingRuntimeTier.Recommended;
			return new GamingRuntimeComponent(
				id, displayName, category, tier, architecture, detectionKey, publisher, officialUrl, description,
				bundleFileName, installerFileName, canInstallOffline, includedByDefault, legacy, aliases, publisherTokens);
		}

		private static string[] BuildBundleFileList(bool required)
		{
			return Components
				.Where(item => item.CanInstallOffline && !string.IsNullOrWhiteSpace(item.BundleFileName) && item.IncludedByDefault == required)
				.Select(item => item.BundleFileName)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}

		private static string[][] BuildBundleAliases()
		{
			return Components
				.Where(item => !string.IsNullOrWhiteSpace(item.BundleFileName) && item.BundleAliases.Length > 0)
				.Select(item => (new string[] { item.BundleFileName }).Concat(item.BundleAliases).ToArray())
				.ToArray();
		}

		private static string PathFileName(string path)
		{
			try
			{
				return System.IO.Path.GetFileName(path ?? string.Empty);
			}
			catch
			{
				return path ?? string.Empty;
			}
		}

	}
}
