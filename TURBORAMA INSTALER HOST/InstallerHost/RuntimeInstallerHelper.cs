using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;

namespace InstallerHost
{
	/// <summary>
	/// Executa somente os pacotes que pertencem ao plano explícito da tela. Cada
	/// payload é identificado pelo componente do catálogo e validado imediatamente
	/// antes de iniciar o processo.
	/// </summary>
	internal static class RuntimeInstallerHelper
	{
		private const int InstallerTimeoutMilliseconds = 30 * 60 * 1000;

		private static readonly string[] InstallOrder =
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
			"webview2-x64"
		};

		public static GamingReadinessProfile InstallCompleteGamingRuntimeStack(
			GamingRuntimeInstallSelection selection,
			Action<string, string> progressCallback,
			Action<int> plannedCountCallback,
			Action<GamingRuntimeComponent> componentCompleted)
		{
			if (selection == null)
			{
				throw new ArgumentNullException("selection");
			}

			GamingReadinessProfile before = PrerequisiteDetector.CaptureGamingReadinessProfile();
			List<RuntimeInstallPlanItem> plan = BuildInstallationPlan(before, selection);
			List<RuntimeInstallPlanItem> planned = plan
				.Where(item => item.Disposition == RuntimeInstallDisposition.InstallFromVerifiedBundle)
				.OrderBy(item => GetInstallOrder(item.Component))
				.ToList();
			if (plannedCountCallback != null)
			{
				plannedCountCallback(planned.Count);
			}

			List<RuntimeInstallPlanItem> unavailable = plan
				.Where(item => item.Disposition == RuntimeInstallDisposition.MissingBundle &&
					item.Component != null && IsSelectedOfflineComponent(item.Component, selection))
				.ToList();
			if (unavailable.Count > 0)
			{
				throw new FileNotFoundException(
					"O pacote offline não contém todos os componentes selecionados:" + Environment.NewLine +
					string.Join(Environment.NewLine, unavailable.Select(item => " - " + item.Component.DisplayName)) + Environment.NewLine +
					"Recrie o instalador somente com os payloads oficiais registrados no catálogo de integridade.");
			}

			PrerequisiteBundle.EnsureBundleAvailable(planned.Select(item => item.Component));
			if (planned.Count > 0 && !IsRunningAsAdministrator())
			{
				throw new InvalidOperationException(
					"Execute o InstallerHost como Administrador para instalar os runtimes selecionados.");
			}

			try
			{
				ReportProgress(progressCallback, "Preparando ambiente", before.BuildSummary());
				foreach (RuntimeInstallPlanItem item in planned)
				{
					InstallPlannedComponent(item.Component, progressCallback);
					if (componentCompleted != null)
					{
						componentCompleted(item.Component);
					}
				}

				LogManualPlanItems(plan);
				GamingReadinessProfile after = PrerequisiteDetector.CaptureGamingReadinessProfile();
				VerifyPlannedComponents(planned, after);
				return after;
			}
			finally
			{
				// Todos os processos já terminaram (ou foram interrompidos por timeout),
				// portanto os payloads grandes podem ser removidos imediatamente.
				PrerequisiteBundle.CleanupExtractedFiles();
			}
		}

		public static List<RuntimeInstallPlanItem> BuildInstallationPlan(
			GamingReadinessProfile profile,
			GamingRuntimeInstallSelection selection)
		{
			if (profile == null)
			{
				profile = PrerequisiteDetector.CaptureGamingReadinessProfile();
			}
			if (selection == null)
			{
				selection = GamingRuntimeInstallSelection.RecommendedDefaults();
			}

			List<RuntimeInstallPlanItem> plan = new List<RuntimeInstallPlanItem>();
			foreach (GamingRuntimeComponent component in GamingRuntimeManifest.GetComponents())
			{
				RuntimeComponentStatus status = profile.RuntimeStatuses.FirstOrDefault(statusItem =>
					statusItem.Component != null && string.Equals(statusItem.Component.Id, component.Id, StringComparison.OrdinalIgnoreCase));
				if (status == null)
				{
					status = PrerequisiteDetector.DetectRuntimeComponent(profile, component);
				}

				RuntimeInstallPlanItem item = new RuntimeInstallPlanItem
				{
					Component = component,
					Status = status,
					Disposition = RuntimeInstallDisposition.ManualChoiceRequired,
					Reason = component.Description
				};

				if (status.State == GamingReadinessState.Ready)
				{
					item.Disposition = RuntimeInstallDisposition.AlreadyInstalled;
					item.Reason = status.Detail;
				}
				else if (status.State == GamingReadinessState.NotApplicable)
				{
					item.Disposition = RuntimeInstallDisposition.NotApplicable;
					item.Reason = status.Detail;
				}
				else if (!component.CanInstallOffline)
				{
					item.Disposition = RuntimeInstallDisposition.OpenOfficialInstructions;
					item.Reason = "Disponível somente por orientação ou instalação interativa na fonte oficial.";
				}
				else if (!IsSelectedOfflineComponent(component, selection))
				{
					item.Disposition = RuntimeInstallDisposition.ManualChoiceRequired;
					item.Reason = "Grupo não selecionado pelo usuário nesta execução.";
				}
				else if (!status.BundleAvailable)
				{
					item.Disposition = RuntimeInstallDisposition.MissingBundle;
					item.Reason = "Payload oficial não encontrado no pacote incorporado.";
				}
				else
				{
					item.Disposition = RuntimeInstallDisposition.InstallFromVerifiedBundle;
					item.Reason = "Payload incorporado; hash, tamanho e assinatura serão verificados antes da execução.";
				}

				plan.Add(item);
			}

			return plan;
		}

		/// <summary>Compatibilidade com diagnósticos existentes.</summary>
		public static List<RuntimeInstallPlanItem> BuildInstallationPlan(GamingReadinessProfile profile, bool includeOptional)
		{
			return BuildInstallationPlan(profile, GamingRuntimeInstallSelection.RecommendedDefaults());
		}

		public static void InstallDotNet35(Action<string, string> progressCallback)
		{
			if (PrerequisiteDetector.IsDotNet35Installed())
			{
				return;
			}

			GamingReadinessProfile profile = PrerequisiteDetector.CaptureGamingReadinessProfile();
			if (profile.OsBuild >= 28000)
			{
				throw new InvalidOperationException(
					"No Windows build " + profile.OsBuild + ", use exclusivamente o instalador oficial específico do .NET Framework 3.5: " +
					"https://learn.microsoft.com/dotnet/framework/install/dotnet-35-windows");
			}

			ReportProgress(progressCallback, "Ativando .NET Framework 3.5", "Recurso NetFx3 do próprio Windows");
			RunSystemCommand("dism.exe", "/Online /Enable-Feature /FeatureName:NetFx3 /All /NoRestart", ".NET Framework 3.5");
			if (!PrerequisiteDetector.IsDotNet35Installed())
			{
				throw new InvalidOperationException(
					"O recurso .NET Framework 3.5 não foi confirmado. Use uma mídia Windows da mesma versão e a fonte SxS oficial.");
			}
		}

		private static bool IsSelectedOfflineComponent(
			GamingRuntimeComponent component,
			GamingRuntimeInstallSelection selection)
		{
			if (component == null || selection == null || !component.CanInstallOffline)
			{
				return false;
			}

			if (string.Equals(component.Id, "directx-june-2010", StringComparison.OrdinalIgnoreCase))
			{
				return selection.InstallDirectXLegacy;
			}

			// XNA e futuros payloads opcionais exigem uma opção própria; não são
			// incluídos implicitamente pelo checkbox do stack recomendado.
			if (component.Tier == GamingRuntimeTier.Optional)
			{
				return false;
			}

			return selection.InstallMicrosoftRuntimeStack &&
				(component.Category == GamingRuntimeCategory.MicrosoftRuntime || component.IsLegacy);
		}

		private static void InstallPlannedComponent(
			GamingRuntimeComponent component,
			Action<string, string> progressCallback)
		{
			if (component == null)
			{
				throw new ArgumentNullException("component");
			}

			ReportProgress(progressCallback, "Instalando componente verificado", component.DisplayName);
			if (component.IsLegacy)
			{
				InstallLegacyVisualCpp(component);
				return;
			}

			switch (component.Id.ToLowerInvariant())
			{
				case "dotnet-framework-48":
					RunBundledInstaller(component, "/q /norestart");
					break;
				case "vc-modern-x64":
				case "vc-modern-x86":
				case "dotnet-desktop-8-x64":
				case "dotnet-desktop-8-x86":
				case "dotnet-desktop-10-x64":
				case "dotnet-desktop-10-x86":
					RunBundledInstaller(component, "/install /quiet /norestart");
					break;
				case "directx-june-2010":
					InstallDirectXJune2010(component);
					break;
				case "webview2-x64":
					RunBundledInstaller(component, "/silent /install");
					break;
				case "xna-framework-40":
					RunBundledMsi(component, "/qn /norestart");
					break;
				default:
					throw new InvalidOperationException("Não há estratégia de instalação aprovada para " + component.DisplayName + ".");
			}
		}

		private static void RunBundledInstaller(GamingRuntimeComponent component, string arguments)
		{
			string installerPath = PrerequisiteBundle.ExtractBundledFile(component);
			RunInstaller(installerPath, arguments, component, component.DisplayName);
		}

		private static void RunBundledMsi(GamingRuntimeComponent component, string arguments)
		{
			string msiPath = PrerequisiteBundle.ExtractBundledFile(component);
			RunMsi(msiPath, arguments, component, component.DisplayName);
		}

		private static void InstallLegacyVisualCpp(GamingRuntimeComponent component)
		{
			string zipPath = PrerequisiteBundle.ExtractBundledFile(component);
			using (SecureInstallerStaging staging = SecureInstallerStaging.Create("TurboramaLegacyVC"))
			{
				string installerPath = InstallerPackageSecurity.ExtractAndVerifyArchiveInstaller(
					zipPath,
					component,
					staging);
				RunInstaller(installerPath, GetLegacyVisualCppArguments(component.Id), component, component.DisplayName);
			}
		}

		private static void InstallDirectXJune2010(GamingRuntimeComponent component)
		{
			using (SecureInstallerStaging staging = SecureInstallerStaging.Create("TurboramaDirectX"))
			{
				string redistPath = PrerequisiteBundle.ExtractBundledFile(component);
				string extractPath = staging.CreateSubdirectory("payload");
				RunInstaller(redistPath, "/Q /T:\"" + extractPath + "\"", component, component.DisplayName + " (extração)");
				staging.HardenTreeContents();

				string dxSetupPath = Path.Combine(extractPath, "DXSETUP.exe");
				if (!File.Exists(dxSetupPath))
				{
					throw new FileNotFoundException("DXSETUP.exe não foi produzido pelo payload oficial do DirectX.", dxSetupPath);
				}
				RunInstaller(dxSetupPath, "/silent", component, component.DisplayName);
			}
		}

		public static void RunInstaller(
			string installerPath,
			string arguments,
			GamingRuntimeComponent component,
			string label)
		{
			if (component == null)
			{
				throw new ArgumentNullException("component", "Todo instalador precisa de um componente explícito do catálogo.");
			}

			string resolvedPath = ResolveExecutablePath(installerPath);
			if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
			{
				throw new FileNotFoundException("Instalador não encontrado: " + (resolvedPath ?? installerPath), resolvedPath ?? installerPath);
			}

			using (TrustedInstallerFile lease = InstallerPackageSecurity.OpenTrustedInstaller(resolvedPath, component, label))
			{
				int exitCode = RunProcessAndWait(
					resolvedPath, arguments, label, GetAbsoluteWorkingDirectory(resolvedPath));
				EnsureSuccessfulInstallerExit(exitCode, label);
			}
		}

		public static void RunMsi(
			string msiPath,
			string arguments,
			GamingRuntimeComponent component,
			string label)
		{
			if (component == null)
			{
				throw new ArgumentNullException("component");
			}
			if (string.IsNullOrWhiteSpace(msiPath) || !Path.IsPathRooted(msiPath) || !File.Exists(msiPath))
			{
				throw new FileNotFoundException("MSI não encontrado por caminho absoluto: " + msiPath, msiPath);
			}

			string msiexecPath = ResolveSystemExecutable("msiexec.exe");
			using (TrustedInstallerFile payloadLease = InstallerPackageSecurity.OpenTrustedInstaller(msiPath, component, label))
			using (TrustedInstallerFile systemLease = InstallerPackageSecurity.OpenTrustedSystemBinary(msiexecPath, "Windows Installer"))
			{
				string commandArguments = "/i \"" + Path.GetFullPath(msiPath) + "\" " + (arguments ?? string.Empty);
				int exitCode = RunProcessAndWait(
					msiexecPath, commandArguments, label, GetAbsoluteWorkingDirectory(msiexecPath));
				EnsureSuccessfulInstallerExit(exitCode, label);
			}
		}

		private static void RunSystemCommand(string fileName, string arguments, string label)
		{
			string resolvedPath = ResolveSystemExecutable(fileName);
			using (TrustedInstallerFile lease = InstallerPackageSecurity.OpenTrustedSystemBinary(resolvedPath, label))
			{
				int exitCode = RunProcessAndWait(
					resolvedPath, arguments, label, GetAbsoluteWorkingDirectory(resolvedPath));
				if (exitCode != 0 && exitCode != 3010)
				{
					throw new InvalidOperationException(label + " falhou com código de saída " + exitCode + ".");
				}
			}
		}

		private static int RunProcessAndWait(
			string executablePath,
			string arguments,
			string label,
			string workingDirectory)
		{
			if (string.IsNullOrWhiteSpace(workingDirectory) || !Path.IsPathRooted(workingDirectory))
			{
				throw new InvalidOperationException("Diretório de trabalho absoluto ausente para " + label + ".");
			}
			string fullWorkingDirectory = Path.GetFullPath(workingDirectory);
			if (!Directory.Exists(fullWorkingDirectory) ||
				(File.GetAttributes(fullWorkingDirectory) & FileAttributes.ReparsePoint) != 0)
			{
				throw new DirectoryNotFoundException(
					"Diretório de trabalho seguro indisponível para " + label + ": " + fullWorkingDirectory + ".");
			}

			using (Process process = new Process())
			{
				process.StartInfo.FileName = executablePath;
				process.StartInfo.Arguments = arguments ?? string.Empty;
				process.StartInfo.WorkingDirectory = fullWorkingDirectory;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;
				Logger.Log("Running verified installer: " + executablePath + " " + (arguments ?? string.Empty));
				if (!process.Start())
				{
					throw new InvalidOperationException("O processo de " + label + " não pôde ser iniciado.");
				}
				if (!process.WaitForExit(InstallerTimeoutMilliseconds))
				{
					try
					{
						process.Kill();
						process.WaitForExit(10000);
					}
					catch (Exception killError)
					{
						Logger.Log("Failed to stop timed-out installer '" + label + "': " + killError.Message);
					}
					throw new TimeoutException(
						label + " excedeu o limite seguro de " +
						(InstallerTimeoutMilliseconds / 60000) + " minutos e foi interrompido.");
				}
				Logger.Log(label + " finished with exit code " + process.ExitCode + ".");
				return process.ExitCode;
			}
		}

		private static string GetAbsoluteWorkingDirectory(string executablePath)
		{
			if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathRooted(executablePath))
			{
				throw new InvalidOperationException("Executável sem caminho absoluto para definir WorkingDirectory.");
			}
			string fullExecutablePath = Path.GetFullPath(executablePath);
			string directory = Path.GetDirectoryName(fullExecutablePath);
			if (string.IsNullOrWhiteSpace(directory))
			{
				throw new InvalidOperationException("Executável sem diretório absoluto: " + fullExecutablePath + ".");
			}
			return Path.GetFullPath(directory);
		}

		private static void EnsureSuccessfulInstallerExit(int exitCode, string label)
		{
			// 1638 = outra versão já instalada; 1641/3010 = reinicialização exigida.
			if (exitCode == 0 || exitCode == 1638 || exitCode == 1641 || exitCode == 3010)
			{
				return;
			}

			throw new InvalidOperationException(label + " falhou com código de saída " + exitCode + ".");
		}

		private static string ResolveExecutablePath(string executablePath)
		{
			if (string.IsNullOrWhiteSpace(executablePath))
			{
				return executablePath;
			}

			// Um caminho absoluto ausente permanece ausente. Nunca convertemos seu
			// basename em uma busca pelo PATH, o que poderia executar outro arquivo.
			if (Path.IsPathRooted(executablePath))
			{
				return Path.GetFullPath(executablePath);
			}

			if (executablePath.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
				executablePath.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
			{
				return Path.GetFullPath(executablePath);
			}

			return ResolveSystemExecutable(executablePath);
		}

		private static string ResolveSystemExecutable(string fileName)
		{
			if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
			{
				throw new ArgumentException("Comando do sistema inválido.", "fileName");
			}

			string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
			string[] roots =
			{
				Environment.SystemDirectory,
				Environment.GetFolderPath(Environment.SpecialFolder.System),
				Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
				Path.Combine(windows, "Sysnative")
			};
			foreach (string root in roots.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				string candidate = Path.Combine(root, fileName);
				if (File.Exists(candidate))
				{
					return Path.GetFullPath(candidate);
				}
			}

			throw new FileNotFoundException("Comando oficial do Windows não encontrado: " + fileName, fileName);
		}

		private static string GetLegacyVisualCppArguments(string componentId)
		{
			if (componentId.IndexOf("2005", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return "/q";
			}
			if (componentId.IndexOf("2008", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return "/qb /norestart";
			}
			return "/passive /norestart";
		}

		private static void VerifyPlannedComponents(
			IEnumerable<RuntimeInstallPlanItem> planned,
			GamingReadinessProfile profile)
		{
			List<string> unconfirmed = new List<string>();
			foreach (RuntimeInstallPlanItem plannedItem in planned)
			{
				RuntimeComponentStatus status = profile.RuntimeStatuses.FirstOrDefault(item =>
					item.Component != null && plannedItem.Component != null &&
					string.Equals(item.Component.Id, plannedItem.Component.Id, StringComparison.OrdinalIgnoreCase));
				if (status == null || (status.State != GamingReadinessState.Ready && status.State != GamingReadinessState.NotApplicable))
				{
					unconfirmed.Add(plannedItem.Component.DisplayName + " — " + (status == null ? "detecção indisponível" : status.Detail));
				}
			}

			if (unconfirmed.Count > 0)
			{
				throw new InvalidOperationException(
					"Os seguintes componentes selecionados não foram confirmados após a instalação:" + Environment.NewLine +
					string.Join(Environment.NewLine, unconfirmed.Select(item => " - " + item)));
			}

			Logger.Log("Selected gaming runtime packages verified. " + profile.BuildSummary());
		}

		private static int GetInstallOrder(GamingRuntimeComponent component)
		{
			int index = component == null ? -1 : Array.FindIndex(InstallOrder,
				item => string.Equals(item, component.Id, StringComparison.OrdinalIgnoreCase));
			return index < 0 ? int.MaxValue : index;
		}

		private static void LogManualPlanItems(IEnumerable<RuntimeInstallPlanItem> plan)
		{
			foreach (RuntimeInstallPlanItem item in plan.Where(candidate =>
				candidate.Disposition == RuntimeInstallDisposition.OpenOfficialInstructions ||
				candidate.Disposition == RuntimeInstallDisposition.MissingBundle))
			{
				Logger.Log("Manual runtime guidance: " + item.Component.DisplayName + " | " + item.Component.OfficialUrl + " | " + item.Reason);
			}
		}

		private static void ReportProgress(Action<string, string> callback, string title, string detail)
		{
			if (callback != null)
			{
				callback(title, detail);
			}
		}

		private static bool IsRunningAsAdministrator()
		{
			try
			{
				using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
				{
					return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
				}
			}
			catch
			{
				return false;
			}
		}
	}
}
