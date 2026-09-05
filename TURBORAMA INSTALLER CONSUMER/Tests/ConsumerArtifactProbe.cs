using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

internal static class ConsumerArtifactProbe
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3) throw new ArgumentException("Usage: probe <built-exe> <process-bits> <required-dotnet10-or-dash>");
            int bits = Environment.Is64BitProcess ? 64 : 32;
            if (bits.ToString() != args[1]) throw new InvalidOperationException("Probe process architecture mismatch.");
            // Loading an assembly never calls the installer entry point. These
            // explicitly invoked methods only inspect runtime state/resources.
            Assembly product = Assembly.LoadFrom(args[0]);
            PortableExecutableKinds peKind;
            ImageFileMachine machine;
            product.ManifestModule.GetPEKind(out peKind, out machine);
            if (machine != ImageFileMachine.AMD64 || (peKind & PortableExecutableKinds.PE32Plus) == 0)
                throw new InvalidOperationException("The full offline artifact must be x64 to avoid 32-bit address-space exhaustion.");
            Type detector = product.GetType("InstallerHost.PrerequisiteDetector", true);
            Type manifest = product.GetType("InstallerHost.GamingRuntimeManifest", true);
            object profile = Activator.CreateInstance(product.GetType("InstallerHost.GamingReadinessProfile", true));
            MethodInfo find = manifest.GetMethod("FindById", BindingFlags.Static | BindingFlags.Public);
            MethodInfo detect = detector.GetMethod("DetectRuntimeComponent", BindingFlags.Static | BindingFlags.Public);
            if (find.Invoke(null, new object[] { "winfsp" }) != null ||
                Array.Exists(product.GetManifestResourceNames(), name =>
                    name.IndexOf("winfsp", StringComparison.OrdinalIgnoreCase) >= 0))
                throw new InvalidOperationException("Prerelease WinFsp must not be exposed or embedded in the artifact.");
            foreach (string id in new[] { "dotnet-desktop-10-x64", "java-8-x64", "java-17-x64", "java-21-x64", "java-25-x64" })
            {
                object component = find.Invoke(null, new object[] { id });
                object status = detect.Invoke(null, new[] { profile, component });
                Type type = status.GetType();
                string state = type.GetProperty("State").GetValue(status, null).ToString();
                string version = (string)type.GetProperty("DetectedVersion").GetValue(status, null);
                bool bundled = (bool)type.GetProperty("BundleAvailable").GetValue(status, null);
                if (!bundled) throw new InvalidOperationException("Final EXE did not confirm embedded package: " + id);
                if (id == "dotnet-desktop-10-x64" && args[2] != "-" && (state != "Ready" || version != args[2]))
                    throw new InvalidOperationException("Final EXE .NET regression: " + state + " / " + version);
                Console.WriteLine("ARTIFACT " + bits + "bit: " + id + " | " + state + " | version=" + version + " | embedded=True");
            }
            int prevalidated = VerifyCompletePlanWithoutExecutingPackages(product);
            Console.WriteLine("ARTIFACT PRE-EXECUTION PASS: " + prevalidated +
                " top-level payloads plus 10 legacy ZIP inner installers extracted, verified and write-locked; " +
                "Windows Installer catalog trust confirmed; no package process started.");
            Console.WriteLine("ARTIFACT PROBE PASS: version=" + product.GetName().Version +
                "; installer entry point was not called and no package was executed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static int VerifyCompletePlanWithoutExecutingPackages(Assembly product)
    {
        Type manifest = product.GetType("InstallerHost.GamingRuntimeManifest", true);
        Type componentType = product.GetType("InstallerHost.GamingRuntimeComponent", true);
        Type planItemType = product.GetType("InstallerHost.RuntimeInstallPlanItem", true);
        Type helper = product.GetType("InstallerHost.RuntimeInstallerHelper", true);
        Type bundle = product.GetType("InstallerHost.PrerequisiteBundle", true);
        Type listType = typeof(List<>).MakeGenericType(planItemType);
        IList planned = (IList)Activator.CreateInstance(listType);

        IEnumerable components = (IEnumerable)manifest.GetMethod(
            "GetComponents", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
        PropertyInfo canInstallOffline = componentType.GetProperty("CanInstallOffline");
        PropertyInfo componentProperty = planItemType.GetProperty("Component");
        PropertyInfo bundleFileName = componentType.GetProperty("BundleFileName");
        PropertyInfo displayName = componentType.GetProperty("DisplayName");
        PropertyInfo description = componentType.GetProperty("Description");
        PropertyInfo installerFileName = componentType.GetProperty("InstallerFileName");
        PropertyInfo idProperty = componentType.GetProperty("Id");
        HashSet<string> payloadNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (object component in components)
        {
            foreach (PropertyInfo textProperty in new[]
            {
                idProperty, displayName, description, bundleFileName, installerFileName
            })
            {
                string value = textProperty.GetValue(component, null) as string;
                if (ContainsPrereleaseMarker(value))
                    throw new InvalidOperationException(
                        "Production artifact exposes prerelease component metadata: " + value + ".");
            }
            if (!(bool)canInstallOffline.GetValue(component, null)) continue;
            string payloadName = bundleFileName.GetValue(component, null) as string;
            if (string.IsNullOrWhiteSpace(payloadName) || !payloadNames.Add(payloadName))
                throw new InvalidOperationException("Offline manifest has a missing or duplicate payload name: " + payloadName);
            object item = Activator.CreateInstance(planItemType);
            componentProperty.SetValue(item, component, null);
            planned.Add(item);
        }
        if (planned.Count != 25)
            throw new InvalidOperationException("Expected all 25 production-eligible offline payloads, found " + planned.Count + ".");

        MethodInfo validate = helper.GetMethod(
            "ValidateCompletePlanBeforeExecution", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo cleanup = bundle.GetMethod(
            "CleanupExtractedFiles", BindingFlags.Static | BindingFlags.Public);
        if (validate == null || cleanup == null)
            throw new MissingMethodException("Final EXE does not expose the pre-execution validation path.");
        List<string> stagedPaths = new List<string>();
        int validatedCount = 0;
        try
        {
            validate.Invoke(null, new object[] { planned });
            IDictionary extracted = (IDictionary)bundle.GetField(
                "ExtractedFiles", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            if (extracted.Count != planned.Count)
                throw new InvalidOperationException("Pre-execution staging did not retain every verified payload.");
			foreach (DictionaryEntry entry in extracted)
			{
				string path = entry.Value as string;
				if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
					throw new InvalidOperationException("A prevalidated payload disappeared before execution.");
				stagedPaths.Add(path);
				RequireSharingWriteLock(path, "top-level payload");
			}

			IDictionary archiveInstallers = (IDictionary)bundle.GetField(
				"ExtractedArchiveInstallers", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
			int legacyCount = 0;
			PropertyInfo isLegacy = componentType.GetProperty("IsLegacy");
			foreach (object component in components)
			{
				if ((bool)canInstallOffline.GetValue(component, null) &&
					(bool)isLegacy.GetValue(component, null)) legacyCount++;
			}
			if (legacyCount != 10 || archiveInstallers.Count != legacyCount)
				throw new InvalidOperationException("Preflight did not prepare all 10 legacy ZIP installers.");
			foreach (DictionaryEntry entry in archiveInstallers)
			{
				string path = entry.Value as string;
				if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
					!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException("A prevalidated legacy ZIP installer is missing or invalid.");
				stagedPaths.Add(path);
				RequireSharingWriteLock(path, "legacy ZIP installer");
			}
            VerifyWindowsInstallerCatalogFallback(product);
            validatedCount = planned.Count;
        }
        finally
        {
            cleanup.Invoke(null, null);
        }

		IDictionary extractedAfterCleanup = (IDictionary)bundle.GetField(
			"ExtractedFiles", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
		IDictionary archiveAfterCleanup = (IDictionary)bundle.GetField(
			"ExtractedArchiveInstallers", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
		if (extractedAfterCleanup.Count != 0 || archiveAfterCleanup.Count != 0)
			throw new InvalidOperationException("Preflight cleanup retained cached installer paths.");
		foreach (string stagedPath in stagedPaths)
		{
			if (File.Exists(stagedPath))
				throw new InvalidOperationException(
					"Preflight cleanup retained a staged payload: " + Path.GetFileName(stagedPath));
		}
		return validatedCount;
    }

    private static bool ContainsPrereleaseMarker(string value)
    {
        return Regex.IsMatch(value ?? string.Empty,
            @"(?i)(?:\b(?:alpha|beta|preview|prerelease)\b|pré-lançamento|\brelease candidate\b|(?:^|[-_.\s])rc(?:[-_.\s]?\d|$))",
            RegexOptions.CultureInvariant);
    }

	private static void RequireSharingWriteLock(string path, string label)
	{
		try
		{
			using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
		}
		catch (IOException error)
		{
			int nativeError = error.HResult & 0xFFFF;
			if (nativeError == 32 || nativeError == 33) return;
			throw new InvalidOperationException(
				"Write probe failed for a reason other than the retained sharing lock (" +
				nativeError + "): " + Path.GetFileName(path), error);
		}
		throw new InvalidOperationException(
			"A prevalidated " + label + " was writable before execution: " + Path.GetFileName(path));
	}

    private static void VerifyWindowsInstallerCatalogFallback(Assembly product)
    {
        Type security = product.GetType("InstallerHost.InstallerPackageSecurity", true);
        MethodInfo verifyCatalog = security.GetMethod(
            "VerifyCatalogAuthenticode", BindingFlags.Static | BindingFlags.NonPublic);
        if (verifyCatalog == null)
            throw new MissingMethodException("Final EXE does not contain Windows catalog-member verification.");
        string path = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            object[] invocation = { path, stream.SafeFileHandle.DangerousGetHandle(), null };
            int status = (int)verifyCatalog.Invoke(null, invocation);
            if (status != 0 || string.IsNullOrWhiteSpace(invocation[2] as string))
                throw new InvalidOperationException("Windows Installer catalog-member verification failed: 0x" +
                    status.ToString("X8") + ".");
        }
    }
}
