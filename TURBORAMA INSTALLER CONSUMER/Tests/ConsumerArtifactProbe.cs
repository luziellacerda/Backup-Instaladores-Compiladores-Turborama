using System;
using System.Reflection;

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
            Type detector = product.GetType("InstallerHost.PrerequisiteDetector", true);
            Type manifest = product.GetType("InstallerHost.GamingRuntimeManifest", true);
            object profile = Activator.CreateInstance(product.GetType("InstallerHost.GamingReadinessProfile", true));
            MethodInfo find = manifest.GetMethod("FindById", BindingFlags.Static | BindingFlags.Public);
            MethodInfo detect = detector.GetMethod("DetectRuntimeComponent", BindingFlags.Static | BindingFlags.Public);
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
            Console.WriteLine("ARTIFACT PROBE PASS: version=" + product.GetName().Version + "; no installer entry point, payload extraction or package execution.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
