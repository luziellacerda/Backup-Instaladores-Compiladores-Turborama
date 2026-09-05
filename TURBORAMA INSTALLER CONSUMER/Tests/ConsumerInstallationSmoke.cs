using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;

/// <summary>
/// Destructive, GitHub-hosted Windows smoke test for the real offline runtime
/// installation path. This executable must never be invoked by the consumer
/// build or on a persistent/self-hosted machine.
/// </summary>
internal static class ConsumerInstallationSmoke
{
	private const string RequiredRepository =
		"luziellacerda/Backup-Instaladores-Compiladores-Turborama";
	private const string NonceEnvironmentVariable =
		"TURBORAMA_GITHUB_INSTALLATION_SMOKE_NONCE";

	private static readonly string[] AllowedComponentIds =
	{
		"dotnet-framework-48",
		"dotnet-desktop-10-x64",
		"dotnet-desktop-8-x64",
		"dotnet-desktop-8-x86",
		"vc-modern-x64",
		"vc-modern-x86",
		"vc-legacy-2005-x86",
		"vc-legacy-2005-x64",
		"vc-legacy-2008-x86",
		"vc-legacy-2008-x64",
		"vc-legacy-2010-x86",
		"vc-legacy-2010-x64",
		"vc-legacy-2012-x86",
		"vc-legacy-2012-x64",
		"vc-legacy-2013-x86",
		"vc-legacy-2013-x64",
		"directx-june-2010",
		"webview2-x64",
		"dotnet-desktop-10-x86",
		"xna-framework-40",
		"java-8-x64",
		"java-17-x64",
		"java-21-x64",
		"java-25-x64"
	};

	private static int Main(string[] args)
	{
		try
		{
			string productPath = DemandGitHubHostedInstallationContext(args);
			RunInstallationSmoke(productPath);
			Console.WriteLine("INSTALLATION SMOKE PASS: every runtime scheduled on this disposable Server image completed and was confirmed; no runtime restart blocker remains.");
			return 0;
		}
		catch (Exception error)
		{
			Console.Error.WriteLine("INSTALLATION SMOKE FAIL");
			Console.Error.WriteLine(Unwrap(error));
			return 1;
		}
	}

	private static string DemandGitHubHostedInstallationContext(string[] args)
	{
		// These checks intentionally happen before loading the product assembly.
		// The nonce is tied to one immutable GitHub run/attempt and must be supplied
		// both through the environment and the command line, preventing an accidental
		// invocation by a consumer or by an ordinary local test command.
		if (args == null || args.Length != 2)
		{
			throw new InvalidOperationException(
				"Usage (GitHub-hosted Windows only): ConsumerInstallationSmoke.exe <InstallerHost.exe> <run-nonce>");
		}
		DemandEnvironmentValue("GITHUB_ACTIONS", "true");
		DemandEnvironmentValue("CI", "true");
		DemandEnvironmentValue("RUNNER_OS", "Windows");
		DemandEnvironmentValue("RUNNER_ENVIRONMENT", "github-hosted");
		DemandEnvironmentValue("GITHUB_REPOSITORY", RequiredRepository);

		string runId = DemandDecimalEnvironmentValue("GITHUB_RUN_ID");
		string runAttempt = DemandDecimalEnvironmentValue("GITHUB_RUN_ATTEMPT");
		string expectedNonce = "turborama-runtime-smoke:" + runId + ":" + runAttempt;
		string environmentNonce = Environment.GetEnvironmentVariable(NonceEnvironmentVariable);
		if (!string.Equals(environmentNonce, expectedNonce, StringComparison.Ordinal) ||
			!string.Equals(args[1], expectedNonce, StringComparison.Ordinal))
		{
			throw new InvalidOperationException(
				"Installation smoke nonce is absent or does not belong to this GitHub run attempt.");
		}

		if (!Environment.Is64BitOperatingSystem || !Environment.Is64BitProcess)
		{
			throw new PlatformNotSupportedException(
				"The installation smoke requires a native 64-bit process on 64-bit Windows.");
		}
		DemandElevatedAdministratorToken();

		string workspaceValue = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
		if (string.IsNullOrWhiteSpace(workspaceValue))
		{
			throw new InvalidOperationException("GITHUB_WORKSPACE is not defined.");
		}
		string workspace = Path.GetFullPath(workspaceValue).TrimEnd(
			Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string productPath = Path.GetFullPath(args[0]);
		string workspacePrefix = workspace + Path.DirectorySeparatorChar;
		if (!productPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				"InstallerHost.exe must be a regular file inside GITHUB_WORKSPACE.");
		}
		if (!File.Exists(productPath) ||
			!string.Equals(Path.GetFileName(productPath), "InstallerHost.exe", StringComparison.OrdinalIgnoreCase))
		{
			throw new FileNotFoundException("The built InstallerHost.exe was not found in GITHUB_WORKSPACE.", productPath);
		}
		if ((File.GetAttributes(productPath) & FileAttributes.ReparsePoint) != 0)
		{
			throw new InvalidOperationException("A reparse-point InstallerHost.exe is not accepted.");
		}

		Console.WriteLine("INSTALLATION SMOKE ARMED: GitHub run " + runId +
			", attempt " + runAttempt + ", elevated github-hosted Windows runner.");
		return productPath;
	}

	private static void RunInstallationSmoke(string productPath)
	{
		Assembly product = Assembly.LoadFrom(productPath);
		if (!string.Equals(product.GetName().Name, "InstallerHost", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Unexpected product assembly: " + product.GetName().Name + ".");
		}
		PortableExecutableKinds peKind;
		ImageFileMachine machine;
		product.ManifestModule.GetPEKind(out peKind, out machine);
		if (machine != ImageFileMachine.AMD64 ||
			(peKind & PortableExecutableKinds.PE32Plus) == 0)
		{
			throw new InvalidOperationException("The full offline InstallerHost.exe is not an x64 PE32+ artifact.");
		}

		Type selectionType = product.GetType("InstallerHost.GamingRuntimeInstallSelection", true);
		Type componentType = product.GetType("InstallerHost.GamingRuntimeComponent", true);
		Type profileType = product.GetType("InstallerHost.GamingReadinessProfile", true);
		Type helperType = product.GetType("InstallerHost.RuntimeInstallerHelper", true);
		Type manifestType = product.GetType("InstallerHost.GamingRuntimeManifest", true);
		Type detectorType = product.GetType("InstallerHost.PrerequisiteDetector", true);

		ValidateAllowedManifestComponents(manifestType, componentType);
		object selection = Activator.CreateInstance(selectionType);
		SetProperty(selectionType, selection, "InstallMicrosoftRuntimeStack", true);
		SetProperty(selectionType, selection, "InstallDirectXLegacy", true);
		SetProperty(selectionType, selection, "InstallOptionalCompatibility", true);
		SetProperty(selectionType, selection, "InstallDokany", false);
		SetProperty(selectionType, selection, "OpenNvidiaOfficialSource", false);
		SetProperty(selectionType, selection, "AllowedComponentIds", AllowedComponentIds);

		MethodInfo buildPlan = DemandBuildPlanMethod(helperType, profileType, selectionType);
		MethodInfo captureProfile = detectorType.GetMethod(
			"CaptureGamingReadinessProfile", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		if (captureProfile == null)
		{
			throw new MissingMethodException("The readiness-profile capture entry point is missing.");
		}
		InitialPlanCoverage initialCoverage = CaptureInitialPlanCoverage(
			(IEnumerable)buildPlan.Invoke(null, new[] { captureProfile.Invoke(null, null), selection }),
			componentType);
		if (initialCoverage.Scheduled.Count < 1)
		{
			throw new InvalidOperationException(
				"The installation smoke was vacuous: the GitHub image required no selected component.");
		}
		CompletionRecorder completionRecorder = new CompletionRecorder(componentType);
		Delegate completedCallback = completionRecorder.CreateCallback();

		MethodInfo restartProbe = detectorType.GetMethod(
			"IsRuntimeRestartRequired", BindingFlags.Static | BindingFlags.NonPublic);
		if (restartProbe == null)
		{
			throw new MissingMethodException("The runtime restart preflight probe is missing.");
		}
		if ((bool)restartProbe.Invoke(null, null))
		{
			throw new InvalidOperationException(
				"The runner already has a runtime restart blocker; no installer was invoked.");
		}

		MethodInfo install = helperType.GetMethod(
			"InstallCompleteGamingRuntimeStack",
			BindingFlags.Static | BindingFlags.Public,
			null,
			new[]
			{
				selectionType,
				typeof(Action<string, string>),
				typeof(Action<int>),
				typeof(Action<>).MakeGenericType(componentType)
			},
			null);
		if (install == null)
		{
			throw new MissingMethodException("The complete runtime installation entry point is missing.");
		}

		int plannedCount = -1;
		Action<string, string> progress = delegate(string title, string detail)
		{
			Console.WriteLine(DateTime.UtcNow.ToString("o") + " | " +
				SingleLine(title) + " | " + SingleLine(detail));
			Console.Out.Flush();
		};
		Action<int> planned = delegate(int count)
		{
			plannedCount = count;
			Console.WriteLine("INSTALLATION PLAN: " + count + " component(s) require installation.");
			Console.Out.Flush();
		};

		Console.WriteLine("INSTALLATION SMOKE START: version=" + product.GetName().Version +
			"; allowed-components=" + AllowedComponentIds.Length + ".");
		object after = install.Invoke(null, new object[] { selection, progress, planned, completedCallback });
		if (after == null || !profileType.IsInstanceOfType(after))
		{
			throw new InvalidOperationException("The runtime installer returned no readiness profile.");
		}
		if (plannedCount != initialCoverage.Scheduled.Count)
		{
			throw new InvalidOperationException(
				"The runtime plan changed between preflight and execution: expected " +
				initialCoverage.Scheduled.Count + ", worker reported " + plannedCount + ".");
		}
		if (!completionRecorder.Completed.SetEquals(initialCoverage.Scheduled) ||
			completionRecorder.CallbackCount != initialCoverage.Scheduled.Count)
		{
			throw new InvalidOperationException(
				"Scheduled/completed runtime coverage differs. Scheduled=[" +
				string.Join(",", Sorted(initialCoverage.Scheduled)) + "]; completed=[" +
				string.Join(",", Sorted(completionRecorder.Completed)) + "]; callbacks=" +
				completionRecorder.CallbackCount + ".");
		}
		Console.WriteLine(
			"INSTALLATION COVERAGE: scheduled-and-completed=" + completionRecorder.Completed.Count +
			"; already-installed=" + initialCoverage.AlreadyInstalled +
			"; not-applicable=" + initialCoverage.NotApplicable +
			"; selected-total=" + AllowedComponentIds.Length + ".");

		bool profileRestartRequired = (bool)profileType.GetProperty(
			"RuntimeRestartRequired", BindingFlags.Instance | BindingFlags.Public).GetValue(after, null);
		bool currentRestartRequired = (bool)restartProbe.Invoke(null, null);
		if (profileRestartRequired || currentRestartRequired)
		{
			throw new InvalidOperationException(
				"A runtime restart blocker remains after installation; this run cannot approve the artifact.");
		}

		VerifyFinalInstallationPlan(buildPlan, componentType, after, selection);
	}

	private static MethodInfo DemandBuildPlanMethod(
		Type helperType,
		Type profileType,
		Type selectionType)
	{
		MethodInfo buildPlan = helperType.GetMethod(
			"BuildInstallationPlan",
			BindingFlags.Static | BindingFlags.Public,
			null,
			new[] { profileType, selectionType },
			null);
		if (buildPlan == null)
		{
			throw new MissingMethodException("The runtime plan verification entry point is missing.");
		}
		return buildPlan;
	}

	private static InitialPlanCoverage CaptureInitialPlanCoverage(
		IEnumerable plan,
		Type componentType)
	{
		InitialPlanCoverage result = new InitialPlanCoverage();
		IDictionary<string, bool> allowed = CreateAllowedIdMap();
		foreach (object planItem in plan)
		{
			Type planItemType = planItem.GetType();
			object component = planItemType.GetProperty("Component").GetValue(planItem, null);
			string id = (string)componentType.GetProperty("Id").GetValue(component, null);
			if (!allowed.ContainsKey(id)) continue;
			string disposition = planItemType.GetProperty("Disposition").GetValue(planItem, null).ToString();
			if (string.Equals(disposition, "InstallFromVerifiedBundle", StringComparison.Ordinal))
			{
				if (!result.Scheduled.Add(id))
					throw new InvalidOperationException("Initial runtime plan contains a duplicate ID: " + id + ".");
			}
			else if (string.Equals(disposition, "AlreadyInstalled", StringComparison.Ordinal))
			{
				result.AlreadyInstalled++;
			}
			else if (string.Equals(disposition, "NotApplicable", StringComparison.Ordinal))
			{
				result.NotApplicable++;
			}
		}
		return result;
	}

	private static void ValidateAllowedManifestComponents(Type manifestType, Type componentType)
	{
		MethodInfo getComponents = manifestType.GetMethod("GetComponents", BindingFlags.Static | BindingFlags.Public);
		if (getComponents == null)
		{
			throw new MissingMethodException("The runtime manifest entry point is missing.");
		}
		IDictionary<string, bool> expected = CreateAllowedIdMap();
		IEnumerable components = (IEnumerable)getComponents.Invoke(null, null);
		PropertyInfo idProperty = componentType.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
		PropertyInfo offlineProperty = componentType.GetProperty("CanInstallOffline", BindingFlags.Instance | BindingFlags.Public);
		foreach (object component in components)
		{
			string id = (string)idProperty.GetValue(component, null);
			if (!expected.ContainsKey(id)) continue;
			if (!(bool)offlineProperty.GetValue(component, null))
			{
				throw new InvalidOperationException("Selected component is no longer offline-installable: " + id + ".");
			}
			expected[id] = true;
		}
		foreach (KeyValuePair<string, bool> item in expected)
		{
			if (!item.Value)
			{
				throw new InvalidOperationException("Selected component is absent from the built manifest: " + item.Key + ".");
			}
		}
	}

	private static void VerifyFinalInstallationPlan(
		MethodInfo buildPlan,
		Type componentType,
		object profile,
		object selection)
	{
		IDictionary<string, bool> confirmed = CreateAllowedIdMap();
		List<string> unresolved = new List<string>();
		IEnumerable finalPlan = (IEnumerable)buildPlan.Invoke(null, new[] { profile, selection });
		foreach (object planItem in finalPlan)
		{
			Type planItemType = planItem.GetType();
			object component = planItemType.GetProperty("Component").GetValue(planItem, null);
			string id = (string)componentType.GetProperty("Id").GetValue(component, null);
			string disposition = planItemType.GetProperty("Disposition").GetValue(planItem, null).ToString();

			if (!confirmed.ContainsKey(id))
			{
				if (string.Equals(disposition, "InstallFromVerifiedBundle", StringComparison.Ordinal) ||
					string.Equals(disposition, "MissingBundle", StringComparison.Ordinal))
				{
					throw new InvalidOperationException(
						"The smoke attempted to schedule an unapproved component: " + id + ".");
				}
				continue;
			}

			confirmed[id] = true;
			if (string.Equals(disposition, "InstallFromVerifiedBundle", StringComparison.Ordinal) ||
				string.Equals(disposition, "MissingBundle", StringComparison.Ordinal))
			{
				unresolved.Add(id + "=" + disposition);
			}
			else if (!string.Equals(disposition, "AlreadyInstalled", StringComparison.Ordinal) &&
				!string.Equals(disposition, "NotApplicable", StringComparison.Ordinal))
			{
				unresolved.Add(id + "=unexpected-" + disposition);
			}
		}

		foreach (KeyValuePair<string, bool> item in confirmed)
		{
			if (!item.Value) unresolved.Add(item.Key + "=absent-from-final-plan");
		}
		if (unresolved.Count > 0)
		{
			throw new InvalidOperationException(
				"Selected components remain unresolved after installation: " +
				string.Join(", ", unresolved.ToArray()) + ".");
		}
	}

	private static string[] Sorted(IEnumerable<string> values)
	{
		List<string> result = new List<string>(values);
		result.Sort(StringComparer.OrdinalIgnoreCase);
		return result.ToArray();
	}

	private sealed class InitialPlanCoverage
	{
		internal readonly HashSet<string> Scheduled =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		internal int AlreadyInstalled;
		internal int NotApplicable;
	}

	private sealed class CompletionRecorder
	{
		private readonly Type componentType;
		private readonly PropertyInfo idProperty;

		internal CompletionRecorder(Type componentType)
		{
			this.componentType = componentType;
			idProperty = componentType.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
			if (idProperty == null) throw new MissingMemberException(componentType.FullName, "Id");
		}

		internal readonly HashSet<string> Completed =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		internal int CallbackCount { get; private set; }

		internal Delegate CreateCallback()
		{
			MethodInfo record = typeof(CompletionRecorder).GetMethod(
				"Record", BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(componentType);
			return Delegate.CreateDelegate(
				typeof(Action<>).MakeGenericType(componentType), this, record);
		}

		private void Record<T>(T component)
		{
			CallbackCount++;
			if (component == null || !componentType.IsInstanceOfType(component))
				throw new InvalidOperationException("Completion callback returned an invalid component.");
			string id = (string)idProperty.GetValue(component, null);
			if (Array.IndexOf(AllowedComponentIds, id) < 0)
				throw new InvalidOperationException("Completion callback returned an unapproved component: " + id + ".");
			if (!Completed.Add(id))
				throw new InvalidOperationException("Completion callback returned a duplicate component: " + id + ".");
			Console.WriteLine("INSTALLATION COMPLETED: " + id);
			Console.Out.Flush();
		}
	}

	private static IDictionary<string, bool> CreateAllowedIdMap()
	{
		Dictionary<string, bool> result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		foreach (string id in AllowedComponentIds)
		{
			if (string.IsNullOrWhiteSpace(id) || result.ContainsKey(id))
			{
				throw new InvalidOperationException("The installation smoke allow-list contains an invalid or duplicate ID.");
			}
			result.Add(id, false);
		}
		return result;
	}

	private static void SetProperty(Type type, object instance, string name, object value)
	{
		PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
		if (property == null || !property.CanWrite)
		{
			throw new MissingMemberException(type.FullName, name);
		}
		property.SetValue(instance, value, null);
	}

	private static void DemandEnvironmentValue(string name, string expected)
	{
		string actual = Environment.GetEnvironmentVariable(name);
		if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(name + " must be exactly '" + expected + "'.");
		}
	}

	private static string DemandDecimalEnvironmentValue(string name)
	{
		string value = Environment.GetEnvironmentVariable(name);
		ulong parsed;
		if (string.IsNullOrWhiteSpace(value) || !ulong.TryParse(value, out parsed) || parsed == 0)
		{
			throw new InvalidOperationException(name + " must contain a positive GitHub numeric identifier.");
		}
		return parsed.ToString();
	}

	private static void DemandElevatedAdministratorToken()
	{
		using (WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query))
		{
			if (identity == null ||
				!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
			{
				throw new UnauthorizedAccessException("The GitHub runner token is not an administrator token.");
			}
			TokenElevation elevation;
			int returnedLength;
			if (!GetTokenInformation(identity.Token, TokenInformationClass.TokenElevation,
				out elevation, Marshal.SizeOf(typeof(TokenElevation)), out returnedLength))
			{
				throw new InvalidOperationException(
					"Could not query runner token elevation (Win32 " + Marshal.GetLastWin32Error() + ").");
			}
			if (returnedLength < Marshal.SizeOf(typeof(TokenElevation)) || elevation.TokenIsElevated == 0)
			{
				throw new UnauthorizedAccessException("The GitHub runner process is not elevated.");
			}
		}
	}

	private static string SingleLine(string value)
	{
		return (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
	}

	private static Exception Unwrap(Exception error)
	{
		Exception current = error;
		while (current is TargetInvocationException && current.InnerException != null)
		{
			current = current.InnerException;
		}
		return current;
	}

	private enum TokenInformationClass
	{
		TokenElevation = 20
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct TokenElevation
	{
		public int TokenIsElevated;
	}

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetTokenInformation(
		IntPtr tokenHandle,
		TokenInformationClass tokenInformationClass,
		out TokenElevation tokenInformation,
		int tokenInformationLength,
		out int returnLength);
}
