using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using Microsoft.Win32;
using System.Runtime.InteropServices;

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
		internal const long MinimumSystemDriveFreeBytes = 2L * 1024L * 1024L * 1024L;

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
			"webview2-x64",
			"dotnet-desktop-10-x86",
			"xna-framework-40",
			"java-8-x64",
			"java-17-x64",
			"java-21-x64",
			"java-25-x64",
			"dokany",
			"winfsp"
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

			string preflightBlock = GetInstallationPreflightBlockReason(before, selection, planned.Count > 0);
			if (preflightBlock != null) throw new InvalidOperationException(preflightBlock);
			if (planned.Count > 0 && !IsRunningAsAdministrator())
			{
				throw new InvalidOperationException(
					"Execute o InstallerHost como Administrador para instalar os componentes selecionados.");
			}
			// Both checks precede extraction, staging creation and every installer process.
			VerifyStorageBudget(planned);
			PrerequisiteBundle.EnsureBundleAvailable(planned.Select(item => item.Component));

			try
			{
				ReportProgress(progressCallback, "Preparando ambiente", before.BuildSummary());
				List<RuntimeInstallPlanItem> completed = new List<RuntimeInstallPlanItem>();
				GamingRuntimeComponent restartComponent = null;
				int restartExitCode = 0;
				bool restartObserved = false;
				foreach (RuntimeInstallPlanItem item in planned)
				{
					// Windows Update and generic pending-file notifications are advisory.
					// An unfinished driver removal still needs a restart before another package.
					if (PrerequisiteDetector.IsRuntimeRestartRequired())
					{
						restartObserved = true;
						ReportProgress(progressCallback, "Reinicialização pendente",
							"Uma remoção de driver precisa de reinicialização para terminar. As próximas etapas foram suspensas.");
						break;
					}
					// Free space can change while earlier payloads are running.
					VerifyStorageBudget(new[] { item });
					int exitCode = InstallPlannedComponent(item.Component, progressCallback);
					completed.Add(item);
					if (componentCompleted != null)
					{
						componentCompleted(item.Component);
					}
					if (IsRestartExitCode(exitCode))
					{
						restartComponent = item.Component;
						restartExitCode = exitCode;
						ReportProgress(progressCallback, "Reinicialização pendente",
							item.Component.DisplayName + " solicitou reinicialização. Nenhuma etapa adicional será iniciada.");
						break;
					}
				}

				LogManualPlanItems(plan);
				GamingReadinessProfile after = PrerequisiteDetector.CaptureGamingReadinessProfile();
				if (restartComponent != null) MarkRestartRequired(after, restartComponent, restartExitCode);
				after.PendingRestart = after.PendingRestart || restartObserved;
				after.RuntimeRestartRequired = after.RuntimeRestartRequired || restartObserved;
				if (after.RuntimeRestartRequired)
				{
					// Keep every detector status intact; this is a paused result, not a
					// successful verification of all selected components. The UI blocks
					// resubmission until a new session following a manual restart.
					if (after.OverallState == GamingReadinessState.Ready) after.OverallState = GamingReadinessState.Attention;
					after.MutableFindings.Add(new GamingReadinessFinding
					{
						Code = "prerequisites-paused-for-restart", State = GamingReadinessState.Attention,
						Title = "Preparação pausada para reinicialização",
						Detail = completed.Count + " de " + planned.Count + " instaladores selecionados foram processados. A confirmação final está pendente.",
						Recommendation = "Salve seus arquivos, reinicie o Windows manualmente e execute uma nova análise antes de continuar os componentes restantes."
					});
				}
				else VerifyPlannedComponents(completed, after);
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

		internal static string GetInstallationPreflightBlockReason(GamingReadinessProfile profile, bool hasRuntimeWork)
		{
			if (!hasRuntimeWork) return null;
			if (profile == null) return "Diagnóstico indisponível. Nenhum instalador foi iniciado; execute uma nova análise.";
			if (profile.RuntimeRestartRequired)
				return "Um instalador ou uma remoção de driver exige reinicialização. Salve seus arquivos e reinicie antes de continuar os componentes.";
			if (profile.SystemDriveFreeBytes < MinimumSystemDriveFreeBytes)
				return "Espaço livre insuficiente ou não confirmado. Libere pelo menos 2 GB no disco do Windows e analise novamente. " +
					"Nenhum instalador foi iniciado. Esta reserva inicial não inclui o espaço necessário para o produto completo.";
			return null;
		}

		internal static string GetInstallationPreflightBlockReason(
			GamingReadinessProfile profile,
			GamingRuntimeInstallSelection selection,
			bool hasRuntimeWork)
		{
			string generalBlock = GetInstallationPreflightBlockReason(profile, hasRuntimeWork);
			if (generalBlock != null || !hasRuntimeWork || selection == null)
			{
				return generalBlock;
			}
			RuntimeComponentStatus unknown = profile.RuntimeStatuses.FirstOrDefault(item => item != null &&
				item.State == GamingReadinessState.Unknown && IsSelectedOfflineComponent(item.Component, selection));
			if (unknown != null)
				return "Não foi possível consultar " + unknown.Component.DisplayName + ": " + unknown.Detail +
					" Nenhum instalador foi iniciado. Corrija a consulta e execute uma nova análise.";
			if (!selection.InstallDokany || selection.InstallMicrosoftRuntimeStack) return null;

			string[] requiredVcIds = profile != null && profile.Is64BitOperatingSystem
				? new[] { "vc-modern-x64", "vc-modern-x86" }
				: new[] { "vc-modern-x86" };
			bool vcReady = profile != null && requiredVcIds.All(id =>
			{
				RuntimeComponentStatus status = profile.RuntimeStatuses.SingleOrDefault(item =>
					item != null && item.Component != null &&
					string.Equals(item.Component.Id, id, StringComparison.OrdinalIgnoreCase));
				return status != null && (status.State == GamingReadinessState.Ready ||
					status.State == GamingReadinessState.NotApplicable);
			});
			if (!vcReady)
			{
				return "DokanSetup não incorpora o Visual C++ Runtime exigido por seus binários. " +
					"Marque também 'Runtimes Microsoft' ou instale e confirme o Visual C++ v14 x86/x64 antes de continuar. " +
					"Nenhum componente foi selecionado automaticamente.";
			}

			return null;
		}

		internal static long GetRequiredWorkingSpaceBytes(IEnumerable<long> payloadLengths)
		{
			if (payloadLengths == null) throw new ArgumentNullException("payloadLengths");
			long total = 0L;
			checked
			{
				foreach (long length in payloadLengths)
				{
					if (length <= 0L) throw new InvalidDataException("Tamanho de payload inválido para reservar espaço.");
					total += length;
				}
				// Reserve for embedded-file extraction and installer expansion/cache.
				// This is a preflight budget, not a claim about the vendor's final disk usage.
				return MinimumSystemDriveFreeBytes + total * 2L;
			}
		}

		private static void VerifyStorageBudget(IEnumerable<RuntimeInstallPlanItem> planned)
		{
			GamingRuntimeComponent[] components = planned.Select(item => item.Component).ToArray();
			if (components.Length == 0) return;
			long required = GetRequiredWorkingSpaceBytes(components.Select(component =>
				PrerequisiteIntegrityCatalog.GetRequiredPayload(component.BundleFileName).length));
			string[] locations =
			{
				Environment.GetFolderPath(Environment.SpecialFolder.Windows),
				Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
				Path.GetTempPath()
			};
			foreach (string root in locations.Select(location =>
			{
				if (string.IsNullOrWhiteSpace(location) || !Path.IsPathRooted(location))
					throw new InvalidOperationException("Unidade de instalação não confirmada para verificar espaço.");
				return Path.GetPathRoot(Path.GetFullPath(location));
			}).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				DriveInfo drive = new DriveInfo(root);
				if (!drive.IsReady || drive.AvailableFreeSpace < required)
					throw new IOException(string.Format(
						"Espaço insuficiente ou não confirmado em {0}. Reserve pelo menos {1:0.0} GB para preparar os componentes selecionados; nenhum novo instalador foi iniciado.",
						root, required / 1073741824.0));
			}
		}

		internal static string GetOptionalDriverArguments(string componentId)
		{
			if (string.Equals(componentId, "dokany", StringComparison.OrdinalIgnoreCase))
				return "/quiet /norestart";
			if (string.Equals(componentId, "winfsp", StringComparison.OrdinalIgnoreCase))
				return "/qn /norestart REBOOT=ReallySuppress INSTALLLEVEL=1";
			throw new InvalidOperationException("Driver sem estratégia de instalação aprovada: " + componentId + ".");
		}

		internal static bool IsRestartExitCode(int exitCode)
		{
			return exitCode == 3010 || exitCode == 1641;
		}

		internal static void MarkRestartRequired(GamingReadinessProfile profile, GamingRuntimeComponent component, int exitCode)
		{
			if (profile == null) throw new ArgumentNullException("profile");
			if (component == null) throw new ArgumentNullException("component");
			if (!IsRestartExitCode(exitCode)) throw new ArgumentException("Código não indica reinicialização.", "exitCode");
			profile.PendingRestart = true;
			profile.RuntimeRestartRequired = true;
			if (profile.OverallState == GamingReadinessState.Ready) profile.OverallState = GamingReadinessState.Attention;
			string detail = exitCode == 1641
				? "O instalador informou reinicialização iniciada (1641), apesar da opção de supressão. As próximas etapas foram suspensas; confirme o driver após reiniciar."
				: "O instalador concluiu solicitando reinicialização (3010). Confirmação final pendente após reiniciar o Windows; as próximas etapas foram suspensas.";
			RuntimeComponentStatus status = profile.MutableRuntimeStatuses.FirstOrDefault(item => item.Component != null &&
				string.Equals(item.Component.Id, component.Id, StringComparison.OrdinalIgnoreCase));
			if (status == null)
			{
				status = new RuntimeComponentStatus { Component = component };
				profile.MutableRuntimeStatuses.Add(status);
			}
			status.State = GamingReadinessState.Attention;
			status.Detail = detail;
			profile.MutableFindings.Add(new GamingReadinessFinding
			{
				Code = "installer-restart-" + component.Id,
				Title = component.DisplayName + " — reinicialização pendente",
				Detail = detail,
				Recommendation = "Salve seus arquivos, reinicie o Windows manualmente e abra o instalador novamente para confirmar e concluir as etapas restantes.",
				OfficialUrl = component.OfficialUrl,
				State = GamingReadinessState.Attention
			});
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

		internal static bool IsSelectedOfflineComponent(
			GamingRuntimeComponent component,
			GamingRuntimeInstallSelection selection)
		{
			if (component == null || selection == null || !component.CanInstallOffline)
			{
				return false;
			}
			if (selection.AllowedComponentIds != null &&
				!selection.AllowedComponentIds.Any(id => string.Equals(id, component.Id, StringComparison.OrdinalIgnoreCase)))
			{
				return false;
			}

			if (string.Equals(component.Id, "directx-june-2010", StringComparison.OrdinalIgnoreCase))
			{
				return selection.InstallDirectXLegacy;
			}
			if (string.Equals(component.Id, "dokany", StringComparison.OrdinalIgnoreCase))
				return selection.InstallDokany;
			if (string.Equals(component.Id, "winfsp", StringComparison.OrdinalIgnoreCase))
				return selection.InstallWinFsp;

			if (component.Tier == GamingRuntimeTier.Optional)
			{
				return selection.InstallOptionalCompatibility && IsOptionalCompatibilityComponent(component.Id);
			}

			return selection.InstallMicrosoftRuntimeStack &&
				(component.Category == GamingRuntimeCategory.MicrosoftRuntime || component.IsLegacy);
		}

		internal static bool IsOptionalCompatibilityComponent(string componentId)
		{
			return new[] { "xna-framework-40", "dotnet-desktop-10-x86", "java-8-x64", "java-17-x64", "java-21-x64", "java-25-x64" }
				.Any(id => string.Equals(id, componentId, StringComparison.OrdinalIgnoreCase));
		}

		private static int InstallPlannedComponent(
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
				return InstallLegacyVisualCpp(component);
			}

			switch (component.Id.ToLowerInvariant())
			{
				case "dotnet-framework-48":
					return RunBundledInstaller(component, "/q /norestart");
				case "vc-modern-x64":
				case "vc-modern-x86":
				case "dotnet-desktop-8-x64":
				case "dotnet-desktop-8-x86":
				case "dotnet-desktop-10-x64":
				case "dotnet-desktop-10-x86":
					return RunBundledInstaller(component, "/install /quiet /norestart");
				case "directx-june-2010":
					return InstallDirectXJune2010(component);
				case "webview2-x64":
					return RunBundledInstaller(component, "/silent /install");
				case "xna-framework-40":
					return RunBundledMsi(component, "/qn /norestart");
				case "java-8-x64":
				case "java-17-x64":
				case "java-21-x64":
				case "java-25-x64":
					// MSI defaults are version-specific. FeatureMain excludes PATH,
					// JAVA_HOME, .jar associations and Oracle compatibility keys.
					return RunBundledMsi(component, GetJavaInstallerArguments(component));
				case "dokany":
					return RunBundledInstaller(component, GetOptionalDriverArguments(component.Id));
				case "winfsp":
					return RunBundledMsi(component, GetOptionalDriverArguments(component.Id));
				default:
					throw new InvalidOperationException("Não há estratégia de instalação aprovada para " + component.DisplayName + ".");
			}
		}

		private static string GetJavaInstallerArguments(GamingRuntimeComponent component)
		{
			if (!Environment.Is64BitOperatingSystem) throw new InvalidOperationException("O pacote Java x64 exige Windows de 64 bits.");
			string programFiles;
			using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
			using (RegistryKey key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion"))
			{
				programFiles = key == null ? null : key.GetValue("ProgramFilesDir") as string;
			}
			string registeredVersion;
			string registeredPath;
			JavaRuntimeDetector.TryGetRegisteredInstallation(int.Parse(component.Id.Split('-')[1]), out registeredVersion, out registeredPath);
			PrerequisitePayloadLock payload = PrerequisiteIntegrityCatalog.GetRequiredPayload(component.BundleFileName);
			int productState = MsiQueryProductState(payload.productCode);
			if (productState != 5 && productState != -1 && productState != 1 && productState != 2)
				throw new InvalidOperationException("O Windows Installer não confirmou o estado do Java (" + productState + "). Nenhuma manutenção foi iniciada.");
			return GetJavaMaintenanceArguments(programFiles,
				payload.productVersion, registeredVersion, registeredPath, productState == 5);
		}

		[DllImport("msi.dll", CharSet = CharSet.Unicode)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		private static extern int MsiQueryProductState(string productCode);

		internal static string GetJavaMaintenanceArguments(string programFiles, string productVersion, string registeredVersion, string registeredPath, bool sameMsiInstalled)
		{
			string install = GetJavaInstallerArguments(programFiles, productVersion);
			if (string.IsNullOrWhiteSpace(registeredVersion))
			{
				if (sameMsiInstalled) throw new InvalidOperationException("O Java está registrado no Windows Installer, mas seu caminho não foi confirmado. Repare-o em Aplicativos do Windows antes de continuar.");
				return install;
			}
			Version registered;
			Version required = Version.Parse(productVersion);
			if (!Version.TryParse(registeredVersion, out registered) || registered.Revision < 0 || registered.Major != required.Major)
				throw new InvalidDataException("A versão registrada do Java não pôde ser confirmada para manutenção.");
			if (registered > required)
				throw new InvalidOperationException("Existe um Java " + registered + " mais novo com arquivos não confirmados. Repare essa versão pelo instalador original; este pacote " + required + " não fará downgrade.");
			if (registered < required || !sameMsiInstalled) return install;
			// Repair only FeatureMain of this exact installed MSI in its registered
			// location. 'a' restores even damaged equal-version files; 'm' restores
			// machine registration. No environment/association feature is selected.
			return GetJavaArgumentsForDirectory(registeredPath) + " REINSTALL=FeatureMain REINSTALLMODE=am";
		}

		internal static string GetJavaInstallerArguments(string programFiles, string productVersion)
		{
			Version version;
			if (string.IsNullOrWhiteSpace(programFiles) || programFiles.Length < 3 ||
				!char.IsLetter(programFiles[0]) || programFiles[1] != ':' || programFiles[2] != '\\' ||
				programFiles.IndexOfAny(new[] { '"', '\r', '\n', '%' }) >= 0 ||
				!Version.TryParse(productVersion, out version) || version.Revision < 0)
				throw new InvalidDataException("Diretório ou versão do Java inválido; instalação não iniciada.");
			string destination = Path.Combine(programFiles, "Eclipse Adoptium", "jre-" + version + "-hotspot");
			// https://adoptium.net/installation/windows/ requires INSTALLDIR with
			// FeatureMain. A separate version folder keeps the four LTS lines apart.
			return GetJavaArgumentsForDirectory(destination);
		}

		private static string GetJavaArgumentsForDirectory(string destination)
		{
			if (string.IsNullOrWhiteSpace(destination) || destination.Length < 4 || !char.IsLetter(destination[0]) ||
				destination[1] != ':' || destination[2] != '\\' || destination.IndexOfAny(new[] { '"', '\r', '\n', '%' }) >= 0)
				throw new InvalidDataException("Caminho registrado do Java inválido; reparo não iniciado.");
			return "/qn /norestart ALLUSERS=1 ADDLOCAL=FeatureMain INSTALLDIR=\"" + Path.GetFullPath(destination).TrimEnd('\\') + "\"";
		}

		private static int RunBundledInstaller(GamingRuntimeComponent component, string arguments)
		{
			string installerPath = PrerequisiteBundle.ExtractBundledFile(component);
			return RunInstaller(installerPath, arguments, component, component.DisplayName);
		}

		private static int RunBundledMsi(GamingRuntimeComponent component, string arguments)
		{
			string msiPath = PrerequisiteBundle.ExtractBundledFile(component);
			return RunMsi(msiPath, arguments, component, component.DisplayName);
		}

		private static int InstallLegacyVisualCpp(GamingRuntimeComponent component)
		{
			string zipPath = PrerequisiteBundle.ExtractBundledFile(component);
			using (SecureInstallerStaging staging = SecureInstallerStaging.Create("TurboramaLegacyVC"))
			{
				string installerPath = InstallerPackageSecurity.ExtractAndVerifyArchiveInstaller(
					zipPath,
					component,
					staging);
				return RunInstaller(installerPath, GetLegacyVisualCppArguments(component.Id), component, component.DisplayName);
			}
		}

		private static int InstallDirectXJune2010(GamingRuntimeComponent component)
		{
			using (SecureInstallerStaging staging = SecureInstallerStaging.Create("TurboramaDirectX"))
			{
				string redistPath = PrerequisiteBundle.ExtractBundledFile(component);
				string extractPath = staging.CreateSubdirectory("payload");
				int extractionExitCode = RunInstaller(redistPath, "/Q /T:\"" + extractPath + "\"", component, component.DisplayName + " (extração)");
				if (IsRestartExitCode(extractionExitCode)) return extractionExitCode;
				staging.HardenTreeContents();

				string dxSetupPath = Path.Combine(extractPath, "DXSETUP.exe");
				if (!File.Exists(dxSetupPath))
				{
					throw new FileNotFoundException("DXSETUP.exe não foi produzido pelo payload oficial do DirectX.", dxSetupPath);
				}
				return RunInstaller(dxSetupPath, "/silent", component, component.DisplayName);
			}
		}

		public static int RunInstaller(
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
				return exitCode;
			}
		}

		public static int RunMsi(
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
				return exitCode;
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
			if (exitCode == 1618)
			{
				throw new InvalidOperationException(
					label + " não pôde iniciar porque o Windows Installer está ocupado (1618). " +
					"Aguarde o Windows Update ou a outra instalação terminar e tente novamente.");
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
