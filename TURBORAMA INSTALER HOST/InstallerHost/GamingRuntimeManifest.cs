using System.Collections.Generic;

namespace InstallerHost
{
	internal static class GamingRuntimeManifest
	{
		public static readonly string[] RequiredBundleFiles =
		{
			"vc_redist.x64.exe",
			"vc_redist.x86.exe",
			"NDP48-x86-x64-AllOS-ENU.exe",
			"dotNetFx35_WX_10_x86_x64.exe",
			"directx_Jun2010_redist.exe",
			"vcredist2005_x64.zip",
			"vcredist2005_x86.zip",
			"vcredist2008_x64.zip",
			"vcredist2008_x86.zip",
			"vcredist2010_x64.zip",
			"vcredist2010_x86.zip",
			"vcredist2012_x64.zip",
			"vcredist2012_x86.zip",
			"vcredist2013_x64.zip",
			"vcredist2013_x86.zip",
			"DokanSetup.zip",
			"winfsp.zip",
			"MicrosoftEdgeWebView2RuntimeInstallerX64.exe",
			"windowsdesktop-runtime-8.0-win-x64.exe",
			"windowsdesktop-runtime-8.0-win-x86.exe",
			"xnafx40_redist.msi",
			"openal-offline.zip"
		};

		public static readonly string[] OptionalBundleFiles =
		{
		};

		public static readonly string[][] BundleFileAliases =
		{
			new string[] { "NDP48-x86-x64-AllOS-ENU.exe", "NDP48-Web.exe" },
			new string[] { "MicrosoftEdgeWebView2RuntimeInstallerX64.exe", "MicrosoftEdgeWebview2Setup.exe" }
		};

		public static Dictionary<string, InstallerInfo> GetLegacyVcRedistPackages()
		{
			return new Dictionary<string, InstallerInfo>
			{
				{ "vcredist2005_x64.zip", new InstallerInfo(string.Empty, "/q") },
				{ "vcredist2005_x86.zip", new InstallerInfo(string.Empty, "/q") },
				{ "vcredist2008_x64.zip", new InstallerInfo(string.Empty, "/qb") },
				{ "vcredist2008_x86.zip", new InstallerInfo(string.Empty, "/qb") },
				{ "vcredist2010_x64.zip", new InstallerInfo(string.Empty, "/passive /norestart") },
				{ "vcredist2010_x86.zip", new InstallerInfo(string.Empty, "/passive /norestart") },
				{ "vcredist2012_x64.zip", new InstallerInfo(string.Empty, "/passive /norestart") },
				{ "vcredist2012_x86.zip", new InstallerInfo(string.Empty, "/passive /norestart") },
				{ "vcredist2013_x64.zip", new InstallerInfo(string.Empty, "/passive /norestart") },
				{ "vcredist2013_x86.zip", new InstallerInfo(string.Empty, "/passive /norestart") }
			};
		}
	}
}