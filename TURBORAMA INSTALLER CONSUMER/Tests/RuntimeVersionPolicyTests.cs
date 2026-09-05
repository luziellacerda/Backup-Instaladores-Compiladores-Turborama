using System;
using System.Linq;

namespace InstallerHost
{
	internal static class RuntimeVersionPolicyTests
	{
		internal static int Run()
		{
			int passed = 0;
			Action<bool, string> verify = delegate(bool condition, string name)
			{
				if (!condition)
				{
					throw new InvalidOperationException("FAIL: " + name);
				}
				passed++;
				Console.WriteLine("PASS: " + name);
			};

			const string vcRequired = "14.51.36247.0";
			verify(Evaluate("vc-modern-x64", "v14.51.36247.00", vcRequired) == RuntimeVersionComparison.Current,
				"VC accepts the registry v-prefix and an equal four-part version");
			verify(Evaluate("vc-modern-x86", "V14.52.1.0", vcRequired) == RuntimeVersionComparison.Current,
				"VC accepts a newer four-part version");
			verify(Evaluate("vc-modern-x64", "v14.50.99999.999", vcRequired) == RuntimeVersionComparison.Outdated,
				"VC rejects an older four-part version");
			verify(Evaluate("vc-modern-x64", "v14.51.36247", vcRequired) == RuntimeVersionComparison.Unknown,
				"VC without all four comparable fields is unknown");
			verify(Evaluate("vc-modern-x64", "version unavailable", vcRequired) == RuntimeVersionComparison.Unknown,
				"Unknown VC text is never treated as current");

			const string dotNet8Required = "8.0.30.36323";
			verify(Evaluate("dotnet-desktop-8-x64", "8.0.30", dotNet8Required) == RuntimeVersionComparison.Current,
				".NET accepts the required runtime patch without a product revision");
			verify(Evaluate("dotnet-desktop-8-x86", "8.0.30.1", dotNet8Required) == RuntimeVersionComparison.Current,
				".NET ignores the fourth product-pack revision");
			verify(Evaluate("dotnet-desktop-8-x64", "8.0.29.99999", dotNet8Required) == RuntimeVersionComparison.Outdated,
				".NET rejects an older runtime patch even with a larger fourth revision");
			verify(Evaluate("dotnet-desktop-8-x64", "8.0", dotNet8Required) == RuntimeVersionComparison.Unknown,
				".NET without a patch field is unknown");
			verify(Evaluate("dotnet-desktop-10-x64", "10.0.12", "10.0.11.50000") == RuntimeVersionComparison.Current,
				".NET 10 accepts a newer runtime patch");

			verify(Evaluate("webview2", "124.0.2478.80", "1.3.265.7") == RuntimeVersionComparison.NotManaged,
				"WebView2 runtime is not compared with its bootstrapper product version");
			verify(Evaluate("dotnet-desktop-current", "11.0.0", "10.0.11.50000") == RuntimeVersionComparison.NotManaged,
				"Guidance-only future .NET versions are not bound to an offline payload");

			GamingRuntimeComponent vc = GamingRuntimeManifest.FindById("vc-modern-x64");
			GamingRuntimeComponent dotNet8 = GamingRuntimeManifest.FindById("dotnet-desktop-8-x64");
			GamingRuntimeComponent webView2 = GamingRuntimeManifest.FindById("webview2-x64");
			string required;
			verify(RuntimeVersionPolicy.Evaluate(vc, "v14.51.36247.00", out required) == RuntimeVersionComparison.Current &&
				string.Equals(required, vcRequired, StringComparison.Ordinal),
				"VC requirement is loaded from prerequisites.lock.json");
			verify(RuntimeVersionPolicy.Evaluate(dotNet8, "8.0.30", out required) == RuntimeVersionComparison.Current &&
				string.Equals(required, dotNet8Required, StringComparison.Ordinal),
				".NET requirement is loaded from prerequisites.lock.json");
			verify(!RuntimeVersionPolicy.RequiresMinimumVersion(webView2),
				"WebView2 is explicitly outside the minimum-version policy");
			verify(GamingRuntimeManifest.GetComponents().Where(RuntimeVersionPolicy.RequiresMinimumVersion).Count() == 6,
				"Only VC x64/x86 and .NET Desktop 8/10 x64/x86 are freshness-managed");

			return passed;
		}

		private static RuntimeVersionComparison Evaluate(string key, string detected, string required)
		{
			return RuntimeVersionPolicy.Evaluate(key, detected, required);
		}
	}
}
