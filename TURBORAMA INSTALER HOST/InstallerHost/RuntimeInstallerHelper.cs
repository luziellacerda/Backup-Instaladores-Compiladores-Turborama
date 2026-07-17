using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Principal;

namespace InstallerHost
{
	internal static class RuntimeInstallerHelper
	{
		public static void InstallCompleteGamingRuntimeStack(
			Action<string, string> progressCallback,
			Func<string, string, string> normalizeArgs,
			Action<string, string, Action<int>> extractZip)
		{
			PrerequisiteBundle.EnsureBundleAvailable();

			if (!IsRunningAsAdministrator())
			{
				throw new Exception(
					"O instalador precisa ser executado como Administrador para instalar Visual C++, DirectX e demais runtimes." + Environment.NewLine +
					"Clique com o botao direito em InstallerHost.exe ou setup.exe e escolha 'Executar como administrador'.");
			}

			progressCallback("Preparando ambiente", "Instalacao completa de runtimes para jogos e emuladores...");
			InstallDotNet35(progressCallback);
			InstallDotNet48(progressCallback);
			InstallDotNetDesktop8(progressCallback);
			InstallVcRedistModern(progressCallback);
			InstallLegacyVcRedistFromBundle(GamingRuntimeManifest.GetLegacyVcRedistPackages(), progressCallback, normalizeArgs, extractZip);
			InstallDirectXJune2010(progressCallback);
			InstallBundledZipInstaller("DokanSetup.zip", "DokanSetup.exe", "/quiet /norestart", progressCallback, normalizeArgs, extractZip);
			InstallBundledWinFsp(progressCallback, extractZip);
			InstallWebView2(progressCallback);
			InstallXnaFramework(progressCallback);
			InstallOpenAl(progressCallback);
			VerifyCompleteGamingRuntimeStack();
		}

		public static bool IsDotNet48Installed()
		{
			return PrerequisiteDetector.IsDotNet48Installed();
		}

		public static void InstallDotNet35(Action<string, string> progressCallback)
		{
			if (PrerequisiteDetector.IsDotNet35Installed())
			{
				Logger.Log(".NET Framework 3.5 already installed.");
				return;
			}

			progressCallback("Instalando .NET Framework 3.5", "Pacote offline para jogos e emuladores antigos...");
			string installerPath = PrerequisiteBundle.ExtractBundledFile("dotNetFx35_WX_10_x86_x64.exe");
			RunInstaller(installerPath, "/y /q /norestart", true, ".NET Framework 3.5");

			if (!PrerequisiteDetector.IsDotNet35Installed())
			{
				Logger.Log("[AVISO] .NET Framework 3.5 nao confirmado apos instalador offline. Tentando DISM...");
				RunCommand("dism.exe", "/Online /Enable-Feature /FeatureName:NetFx3 /All /NoRestart", false, ".NET Framework 3.5 (DISM)");
			}
		}

		public static void InstallDotNet48(Action<string, string> progressCallback)
		{
			if (IsDotNet48Installed())
			{
				Logger.Log(".NET Framework 4.8 already installed.");
				return;
			}

			progressCallback("Instalando .NET Framework 4.8", "Instalador offline completo do TurboRama...");
			string installerPath = PrerequisiteBundle.ExtractBundledFile("NDP48-x86-x64-AllOS-ENU.exe");
			RunInstaller(installerPath, "/q /norestart", true, ".NET Framework 4.8");

			if (!IsDotNet48Installed())
			{
				throw new Exception(".NET Framework 4.8 nao foi detectado apos a instalacao.");
			}
		}

		public static void InstallDotNetDesktop8(Action<string, string> progressCallback)
		{
			InstallDirectBundledRuntime("windowsdesktop-runtime-8.0-win-x64.exe", "/install /quiet /norestart", progressCallback, ".NET Desktop Runtime 8.0 x64");
			InstallDirectBundledRuntime("windowsdesktop-runtime-8.0-win-x86.exe", "/install /quiet /norestart", progressCallback, ".NET Desktop Runtime 8.0 x86");
		}

		public static void InstallDirectXJune2010(Action<string, string> progressCallback)
		{
			if (PrerequisiteDetector.IsDirectXJun2010Installed())
			{
				Logger.Log("DirectX June 2010 already installed.");
				return;
			}

			progressCallback("Instalando DirectX", "DirectX End-User Runtime June 2010 (d3dx9, XInput legado)...");
			string tempDir = Path.Combine(Path.GetTempPath(), "TurboramaDirectX");
			Directory.CreateDirectory(tempDir);

			try
			{
				string redistExe = PrerequisiteBundle.ExtractBundledFile("directx_Jun2010_redist.exe");
				string extractDir = Path.Combine(tempDir, "dxextract");
				Directory.CreateDirectory(extractDir);
				RunInstaller(redistExe, "/Q /T:\"" + extractDir + "\"", true, "DirectX extractor");

				string dxSetup = Path.Combine(extractDir, "DXSETUP.exe");
				if (!File.Exists(dxSetup))
				{
					throw new FileNotFoundException("DXSETUP.exe nao encontrado apos extracao do DirectX.");
				}

				RunInstaller(dxSetup, "/silent", true, "DirectX June 2010");
			}
			finally
			{
				TryDeleteDirectory(tempDir);
			}
		}

		public static void InstallVcRedistModern(Action<string, string> progressCallback)
		{
			InstallDirectVcRedist("vc_redist.x64.exe", "/install /quiet /norestart", progressCallback);
			InstallDirectVcRedist("vc_redist.x86.exe", "/install /quiet /norestart", progressCallback);
		}

		private static void InstallDirectVcRedist(string fileName, string arguments, Action<string, string> progressCallback)
		{
			progressCallback("Instalando Visual C++", fileName + " (MSVCP140 / VCRUNTIME140 / ucrtbase)");
			string installerPath = PrerequisiteBundle.ExtractBundledFile(fileName);
			RunInstaller(installerPath, arguments, false, fileName);
		}

		private static void InstallDirectBundledRuntime(string fileName, string arguments, Action<string, string> progressCallback, string label)
		{
			progressCallback("Instalando runtime moderno", label);
			string installerPath = PrerequisiteBundle.ExtractBundledFile(fileName);
			RunInstaller(installerPath, arguments, false, label);
		}

		public static void InstallLegacyVcRedistFromBundle(
			Dictionary<string, InstallerInfo> resources,
			Action<string, string> progressCallback,
			Func<string, string, string> normalizeArgs,
			Action<string, string, Action<int>> extractZip)
		{
			string workDir = Path.Combine(Path.GetTempPath(), "TurboramaVCBundle");
			Directory.CreateDirectory(workDir);

			try
			{
				foreach (KeyValuePair<string, InstallerInfo> entry in resources)
				{
					string packageName = entry.Key;
					string legacyVersion;
					string legacyArch;
					ParseLegacyVcPackageName(packageName, out legacyVersion, out legacyArch);

					if (PrerequisiteDetector.IsLegacyVcRedistInstalled(legacyVersion, legacyArch))
					{
						Logger.Log("Visual C++ " + legacyVersion + " " + legacyArch + " already installed.");
						continue;
					}

					string zipPath = PrerequisiteBundle.ExtractBundledFile(packageName);
					string extractDir = Path.Combine(workDir, Path.GetFileNameWithoutExtension(packageName));
					Directory.CreateDirectory(extractDir);

					progressCallback("Instalando Visual C++ legado", packageName);
					extractZip(zipPath, extractDir, null);

					string installerExe = Path.Combine(extractDir, packageName.Replace(".zip", ".exe"));
					if (!File.Exists(installerExe))
					{
						throw new FileNotFoundException("Instalador nao encontrado: " + installerExe);
					}

					string[] argumentSets = GetLegacyVcRedistArgumentSets(packageName, normalizeArgs(packageName, entry.Value.Arguments));
					foreach (string args in argumentSets)
					{
						if (PrerequisiteDetector.IsLegacyVcRedistInstalled(legacyVersion, legacyArch))
						{
							break;
						}

						RunInstaller(installerExe, args, false, packageName + " " + args);
					}

					if (!PrerequisiteDetector.WaitForLegacyVcRedistInstalled(legacyVersion, legacyArch, 15000))
					{
						throw new Exception("Visual C++ " + legacyVersion + " " + legacyArch + " nao foi confirmado apos a instalacao.");
					}

					TryDeleteDirectory(extractDir);
				}
			}
			finally
			{
				TryDeleteDirectory(workDir);
			}
		}

		public static void InstallBundledZipInstaller(
			string zipName,
			string exeName,
			string defaultArgs,
			Action<string, string> progressCallback,
			Func<string, string, string> normalizeArgs,
			Action<string, string, Action<int>> extractZip)
		{
			if (zipName.StartsWith("Dokan", StringComparison.OrdinalIgnoreCase) && PrerequisiteDetector.IsDokanyInstalled())
			{
				Logger.Log("Dokany already installed.");
				return;
			}

			progressCallback("Instalando componente de sistema", zipName);
			string workDir = Path.Combine(Path.GetTempPath(), "TurboramaZip_" + Path.GetFileNameWithoutExtension(zipName));
			Directory.CreateDirectory(workDir);

			try
			{
				string zipPath = PrerequisiteBundle.ExtractBundledFile(zipName);
				string extractDir = Path.Combine(workDir, "extract");
				Directory.CreateDirectory(extractDir);
				extractZip(zipPath, extractDir, null);

				string installerExe = Path.Combine(extractDir, exeName);
				if (!File.Exists(installerExe))
				{
					installerExe = Directory.GetFiles(extractDir, exeName, SearchOption.AllDirectories).FirstOrDefault();
				}

				if (string.IsNullOrEmpty(installerExe) || !File.Exists(installerExe))
				{
					throw new FileNotFoundException("Instalador nao encontrado em " + zipName + ": " + exeName);
				}

				string args = normalizeArgs(zipName, defaultArgs);
				RunInstaller(installerExe, args, false, zipName);
			}
			finally
			{
				TryDeleteDirectory(workDir);
			}
		}

		public static void InstallBundledWinFsp(Action<string, string> progressCallback, Action<string, string, Action<int>> extractZip)
		{
			if (PrerequisiteDetector.IsWinFspInstalled())
			{
				Logger.Log("WinFsp already installed.");
				return;
			}

			progressCallback("Instalando WinFsp", "Suporte a sistemas de arquivos virtuais...");
			string workDir = Path.Combine(Path.GetTempPath(), "TurboramaWinFsp");
			Directory.CreateDirectory(workDir);

			try
			{
				string zipPath = PrerequisiteBundle.ExtractBundledFile("winfsp.zip");
				string extractDir = Path.Combine(workDir, "extract");
				Directory.CreateDirectory(extractDir);
				extractZip(zipPath, extractDir, null);

				string msiPath = Directory.GetFiles(extractDir, "*.msi", SearchOption.AllDirectories).FirstOrDefault();
				if (string.IsNullOrEmpty(msiPath))
				{
					throw new FileNotFoundException("MSI do WinFsp nao encontrado.");
				}

				RunInstaller("msiexec.exe", "/i \"" + msiPath + "\" /qn /norestart", false, "WinFsp");
			}
			finally
			{
				TryDeleteDirectory(workDir);
			}
		}

		public static void InstallWebView2(Action<string, string> progressCallback)
		{
			if (PrerequisiteDetector.IsWebView2Installed())
			{
				Logger.Log("WebView2 already installed.");
				return;
			}

			progressCallback("Instalando WebView2", "Instalador offline Evergreen x64...");
			string installerPath = PrerequisiteBundle.ExtractBundledFile("MicrosoftEdgeWebView2RuntimeInstallerX64.exe");
			RunInstaller(installerPath, "/silent /install", false, "WebView2 Runtime");
		}

		public static void InstallXnaFramework(Action<string, string> progressCallback)
		{
			if (PrerequisiteDetector.IsXnaFrameworkInstalled())
			{
				Logger.Log("XNA Framework 4.0 already installed.");
				return;
			}

			progressCallback("Instalando XNA Framework 4.0", "Jogos indie e ports antigos...");
			string installerPath = PrerequisiteBundle.ExtractBundledFile("xnafx40_redist.msi");
			RunInstaller("msiexec.exe", "/i \"" + installerPath + "\" /qn /norestart", false, "XNA Framework 4.0");
		}

		public static void InstallOpenAl(Action<string, string> progressCallback)
		{
			if (PrerequisiteDetector.IsOpenAlInstalled())
			{
				Logger.Log("OpenAL already installed.");
				return;
			}

			progressCallback("Instalando OpenAL", "Implantando DLLs offline para jogos e emuladores...");
			string zipPath = PrerequisiteBundle.ExtractBundledFile("openal-offline.zip");
			string workDir = Path.Combine(Path.GetTempPath(), "TurboramaOpenAL");
			Directory.CreateDirectory(workDir);

			try
			{
				string extractDir = Path.Combine(workDir, "extract");
				Directory.CreateDirectory(extractDir);
				ZipFile.ExtractToDirectory(zipPath, extractDir);

				string x86Dll = Directory.GetFiles(extractDir, "OpenAL32.dll", SearchOption.AllDirectories)
					.FirstOrDefault(path => path.IndexOf("Win32", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("x86", StringComparison.OrdinalIgnoreCase) >= 0);
				string x64Dll = Directory.GetFiles(extractDir, "OpenAL32.dll", SearchOption.AllDirectories)
					.FirstOrDefault(path => path.IndexOf("Win64", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0);

				if (string.IsNullOrEmpty(x86Dll) || string.IsNullOrEmpty(x64Dll))
				{
					string[] allDlls = Directory.GetFiles(extractDir, "OpenAL32.dll", SearchOption.AllDirectories);
					if (allDlls.Length >= 2)
					{
						x86Dll = allDlls.OrderBy(path => new FileInfo(path).Length).First();
						x64Dll = allDlls.OrderBy(path => new FileInfo(path).Length).Last();
					}
				}

				if (string.IsNullOrEmpty(x86Dll) || string.IsNullOrEmpty(x64Dll))
				{
					throw new FileNotFoundException("OpenAL32.dll x86/x64 nao encontrado em openal-offline.zip.");
				}

				DeployOpenAlDll(x86Dll, Environment.GetFolderPath(Environment.SpecialFolder.SystemX86));
				DeployOpenAlDll(x64Dll, GetNativeSystemDirectory());
				Logger.Log("OpenAL offline deployed successfully.");
			}
			finally
			{
				TryDeleteDirectory(workDir);
			}
		}

		private static void DeployOpenAlDll(string sourceDll, string destinationFolder)
		{
			if (string.IsNullOrEmpty(sourceDll) || string.IsNullOrEmpty(destinationFolder))
			{
				return;
			}

			Directory.CreateDirectory(destinationFolder);
			string destinationPath = Path.Combine(destinationFolder, "OpenAL32.dll");
			File.Copy(sourceDll, destinationPath, true);
			Logger.Log("Deployed OpenAL32.dll to " + destinationPath);
		}

		private static string GetNativeSystemDirectory()
		{
			string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
			if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
			{
				string sysnative = Path.Combine(windows, "Sysnative");
				if (Directory.Exists(sysnative))
				{
					return sysnative;
				}
			}

			return Environment.GetFolderPath(Environment.SpecialFolder.System);
		}

		public static void VerifyCompleteGamingRuntimeStack()
		{
			List<string> missing = new List<string>();

			if (!IsDotNet48Installed())
			{
				missing.Add(".NET Framework 4.8");
			}

			if (!PrerequisiteDetector.IsVcRedist2015_2022Installed("x64"))
			{
				missing.Add("Visual C++ 2015-2022 x64 (MSVCP140.dll / VCRUNTIME140.dll / VCRUNTIME140_1.dll / ucrtbase.dll)");
			}

			if (!PrerequisiteDetector.IsVcRedist2015_2022Installed("x86"))
			{
				missing.Add("Visual C++ 2015-2022 x86");
			}

			foreach (string legacyVcMissing in PrerequisiteDetector.GetMissingLegacyVcRedistVersions())
			{
				missing.Add(legacyVcMissing);
			}

			if (!PrerequisiteDetector.IsDirectXJun2010Installed())
			{
				missing.Add("DirectX June 2010 (d3dx9_43.dll / XInput1_3.dll)");
			}

			if (!PrerequisiteDetector.IsDokanyInstalled())
			{
				missing.Add("Dokan (montagem de arquivos)");
			}

			if (!PrerequisiteDetector.IsWinFspInstalled())
			{
				missing.Add("WinFsp");
			}

			if (!PrerequisiteDetector.IsWebView2Installed())
			{
				missing.Add("WebView2 Runtime");
			}

			if (!PrerequisiteDetector.IsXnaFrameworkInstalled())
			{
				missing.Add("XNA Framework 4.0");
			}

			if (missing.Count > 0)
			{
				throw new Exception(
					"A instalacao completa de runtimes nao foi confirmada no sistema:" + Environment.NewLine +
					string.Join(Environment.NewLine, missing.Select(item => " - " + item)));
			}

			Logger.Log("Complete gaming runtime stack verified.");
		}

		public static void RunInstaller(string installerPath, string arguments, bool treatNonZeroAsFailure, string label)
		{
			installerPath = ResolveExecutablePath(installerPath);
			if (!File.Exists(installerPath))
			{
				throw new FileNotFoundException("Instalador nao encontrado: " + installerPath);
			}

			Process process = new Process();
			process.StartInfo.FileName = installerPath;
			process.StartInfo.Arguments = arguments ?? string.Empty;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			Logger.Log("Running installer: " + installerPath + " " + arguments);
			process.Start();
			process.WaitForExit();
			Logger.Log(string.Format("{0} finished with exit code {1}", label, process.ExitCode));

			if (process.ExitCode == 0 || process.ExitCode == 1638 || process.ExitCode == 3010 || process.ExitCode == 5100)
			{
				return;
			}

			if (treatNonZeroAsFailure)
			{
				throw new Exception(label + " falhou com codigo de saida " + process.ExitCode);
			}
		}

		public static void RunCommand(string fileName, string arguments, bool treatNonZeroAsFailure, string label)
		{
			fileName = ResolveExecutablePath(fileName);
			if (!File.Exists(fileName))
			{
				throw new FileNotFoundException("Comando do sistema nao encontrado: " + fileName);
			}

			Process process = new Process();
			process.StartInfo.FileName = fileName;
			process.StartInfo.Arguments = arguments ?? string.Empty;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.RedirectStandardError = true;
			Logger.Log("Running command: " + fileName + " " + arguments);
			process.Start();
			string output = process.StandardOutput.ReadToEnd();
			string error = process.StandardError.ReadToEnd();
			process.WaitForExit();

			if (!string.IsNullOrWhiteSpace(output))
			{
				Logger.Log(label + " stdout: " + output);
			}
			if (!string.IsNullOrWhiteSpace(error))
			{
				Logger.Log(label + " stderr: " + error);
			}

			Logger.Log(string.Format("{0} finished with exit code {1}", label, process.ExitCode));
			if (process.ExitCode == 0 || process.ExitCode == 3010)
			{
				return;
			}

			if (treatNonZeroAsFailure)
			{
				throw new Exception(label + " falhou com codigo de saida " + process.ExitCode);
			}
		}

		private static bool IsRunningAsAdministrator()
		{
			try
			{
				using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
				{
					WindowsPrincipal principal = new WindowsPrincipal(identity);
					return principal.IsInRole(WindowsBuiltInRole.Administrator);
				}
			}
			catch
			{
				return false;
			}
		}

		private static void ParseLegacyVcPackageName(string packageName, out string version, out string architecture)
		{
			version = string.Empty;
			architecture = packageName.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0 ? "x64" : "x86";

			string[] knownVersions = new string[] { "2005", "2008", "2010", "2012", "2013" };
			foreach (string knownVersion in knownVersions)
			{
				if (packageName.IndexOf(knownVersion, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					version = knownVersion;
					return;
				}
			}
		}

		private static string[] GetLegacyVcRedistArgumentSets(string packageName, string normalizedArgs)
		{
			if (packageName.IndexOf("2005", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return new string[] { "/Q:a", "/q", "/passive /norestart" };
			}

			if (packageName.IndexOf("2008", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return new string[] { "/qb", "/q", "/passive /norestart" };
			}

			if (!string.IsNullOrWhiteSpace(normalizedArgs))
			{
				return new string[] { normalizedArgs, "/passive /norestart", "/quiet /norestart" };
			}

			return new string[] { "/passive /norestart", "/quiet /norestart" };
		}

		private static string ResolveExecutablePath(string executablePath)
		{
			if (string.IsNullOrWhiteSpace(executablePath))
			{
				return executablePath;
			}

			if (File.Exists(executablePath))
			{
				return Path.GetFullPath(executablePath);
			}

			string fileName = Path.GetFileName(executablePath);
			if (string.IsNullOrEmpty(fileName))
			{
				return executablePath;
			}

			string[] searchRoots = new string[]
			{
				Environment.SystemDirectory,
				Environment.GetFolderPath(Environment.SpecialFolder.System),
				Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative")
			};

			foreach (string root in searchRoots.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (string.IsNullOrWhiteSpace(root))
				{
					continue;
				}

				string candidate = Path.Combine(root, fileName);
				if (File.Exists(candidate))
				{
					return Path.GetFullPath(candidate);
				}
			}

			string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
			foreach (string directory in pathVariable.Split(Path.PathSeparator))
			{
				if (string.IsNullOrWhiteSpace(directory))
				{
					continue;
				}

				try
				{
					string candidate = Path.Combine(directory.Trim(), fileName);
					if (File.Exists(candidate))
					{
						return Path.GetFullPath(candidate);
					}
				}
				catch
				{
				}
			}

			return executablePath;
		}

		private static void TryDeleteDirectory(string path)
		{
			try
			{
				if (Directory.Exists(path))
				{
					Directory.Delete(path, true);
				}
			}
			catch
			{
			}
		}
	}
}