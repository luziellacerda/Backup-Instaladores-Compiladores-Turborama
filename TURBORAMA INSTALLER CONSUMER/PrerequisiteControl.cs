using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TurboRama.Next;

namespace InstallerHost
{
	public partial class PrerequisiteControl : UserControl
	{
		private readonly MainForm mainForm;
		private CheckBox chkNvidiaApp;
		private BackgroundWorker installerWorker;
		private BackgroundWorker gamingReadinessWorker;
		private GamingReadinessProfile gamingReadinessProfile;
		private DateTime gamingReadinessCapturedAtUtc;
		private int gamingReadinessRevision;
		private bool gamingReadinessScanPending;
		private bool installationComplete;
		private bool prerequisiteRestartRequired;
		private bool selectionInitialized;
		private int? observedSelectionMask;
		private bool selectionLocked;
		private bool restoringLockedSelection;
		private int lockedCheckedMask;
		private int unlockedEnabledMask;
		private bool nvidiaAppOpened;
		private Label progressTitleLabel;
		private Label progressCountLabel;
		private Label progressPercentLabel;
		private Label progressHintLabel;
		private Label readinessLabel;
		private Button readinessButton;
		private Label diskSpaceLabel;
		private string progressTitleText = "Pronto para iniciar";
		private string progressDetailText = "Marque os grupos desejados. Avançar executa somente as opções selecionadas.";

		public PrerequisiteControl(MainForm main)
		{
			mainForm = main;
			InitializeComponent();

			wizardHeader.Text = ConsumerText.GetString("PrerequisiteIntro", Array.Empty<object>());
			btnCancel.Text = ConsumerText.GetString("Cancel", Array.Empty<object>());
			btnNext.Text = ConsumerText.GetString("Next >", Array.Empty<object>());
			btnBack.Text = ConsumerText.GetString("< Back", Array.Empty<object>());

			UpdateNvidiaDriverCheckbox();
			UpdatePrerequisiteOptions();
			BeginGamingReadinessScan(false);

			Load += delegate
			{
				if (!IsInstallationRunning())
				{
					UpdatePrerequisiteOptions();
				}
				BeginGamingReadinessScan(false);
			};

			VisibleChanged += delegate
			{
				if (!Visible)
				{
					return;
				}
				if (!IsInstallationRunning())
				{
					UpdatePrerequisiteOptions();
				}
				BeginGamingReadinessScan(false);
			};

		}

		public bool SkipIfAllInstalled()
		{
			if (IsInstallationRunning() || !HasCurrentReadinessProfile() || prerequisiteRestartRequired ||
				gamingReadinessProfile.PendingRestart) return false;
			GamingRuntimeInstallSelection selection = GetPrerequisiteSelection().RuntimeSelection;
			GamingRuntimeComponent[] installable = GamingRuntimeManifest.GetComponents()
				.Where(component => component.CanInstallOffline &&
					(component.Tier != GamingRuntimeTier.Optional || RuntimeInstallerHelper.IsSelectedOfflineComponent(component, selection)) &&
					GamingRuntimeManifest.IsApplicableToCurrentOs(component))
				.ToArray();
			return installable.Length > 0 && installable.All(component =>
			{
				RuntimeComponentStatus status = gamingReadinessProfile.RuntimeStatuses.Single(item =>
					item.Component != null && string.Equals(item.Component.Id, component.Id, StringComparison.OrdinalIgnoreCase));
				return status.State == GamingReadinessState.Ready || status.State == GamingReadinessState.NotApplicable;
			});
		}

		internal bool IsPrerequisiteInstallInProgress
		{
			get { return IsInstallationRunning(); }
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			if (!IsInstallationRunning())
			{
				UpdatePrerequisiteOptions();
			}
			BeginGamingReadinessScan(false);
			ActiveControl = btnNext;
		}

		private void BtnBack_Click(object sender, EventArgs e)
		{
			if (IsInstallationRunning()) return;
			mainForm.ShowLicense();
		}

		private void BtnNext_Click(object sender, EventArgs e)
		{
			if (IsInstallationRunning())
			{
				return;
			}
			if (installationComplete)
			{
				mainForm.ShowInstall();
				return;
			}
			PrerequisiteSelection selection = GetPrerequisiteSelection();
			if (HasSelectedRuntimeGroup(selection.RuntimeSelection) &&
				!HasCurrentReadinessProfile())
			{
				SetProgressHeaderSafe("Diagnóstico necessário", "Aguarde a análise do PC e clique em Avançar novamente. Nenhuma instalação foi iniciada.");
				BeginGamingReadinessScan(true);
				return;
			}
			int totalSteps = GetSelectedStepCount(selection);
			int runtimeSteps = totalSteps - (selection.OpenNvidiaOfficialSource ? 1 : 0);
			if (HasSelectedRuntimeGroup(selection.RuntimeSelection) &&
				(prerequisiteRestartRequired || gamingReadinessProfile.PendingRestart))
			{
				SetProgressHeaderSafe("Reinicialização pendente",
					"Salve seus arquivos e reinicie o Windows manualmente antes de preparar mais componentes. Nenhum reinício será solicitado por esta tela.");
				return;
			}
			string preflightBlock = RuntimeInstallerHelper.GetInstallationPreflightBlockReason(
				gamingReadinessProfile, selection.RuntimeSelection, runtimeSteps > 0);
			if (preflightBlock != null)
			{
				SetProgressHeaderSafe("Não foi possível preparar os componentes", preflightBlock);
				BeginGamingReadinessScan(true);
				return;
			}
			if (totalSteps <= 0)
			{
				Logger.Log("No prerequisite action selected or required; continuing to Install screen.");
				installationComplete = true;
				mainForm.ShowInstall();
				return;
			}

			progressBar.Maximum = Math.Max(1, totalSteps);
			progressBar.Value = 0;
			SetButtonsInstallingState(true);
			SetProgressHeaderSafe("Instalando componentes", "Validando o catálogo incorporado antes de cada etapa...");
			UpdateProgressVisualsSafe();

			installerWorker = new BackgroundWorker();
			installerWorker.DoWork += InstallerWorker_DoWork;
			installerWorker.RunWorkerCompleted += InstallerWorker_RunWorkerCompleted;
			try
			{
				installerWorker.RunWorkerAsync(selection);
			}
			catch
			{
				SetButtonsInstallingState(false);
				throw;
			}
		}

		private void InstallerWorker_DoWork(object sender, DoWorkEventArgs e)
		{
			PrerequisiteSelection selection = e.Argument as PrerequisiteSelection;
			if (selection == null)
			{
				e.Result = new InvalidOperationException("A seleção de pré-requisitos não pôde ser lida.");
				return;
			}

			try
			{
				GamingReadinessProfile profile = RuntimeInstallerHelper.InstallCompleteGamingRuntimeStack(
					selection.RuntimeSelection,
					SetProgressHeaderSafe,
					delegate(int plannedCount)
					{
						SetProgressMaximumSafe(plannedCount + (selection.OpenNvidiaOfficialSource ? 1 : 0));
					},
					delegate { UpdateProgressBarSafe(); });
				e.Result = new PrerequisiteInstallationResult(selection, profile);
			}
			catch (Exception ex)
			{
				e.Result = ex;
			}
		}

		private void InstallerWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			InvalidateGamingReadinessScan();
			SetButtonsInstallingState(false);
			Exception failure = e.Error ?? e.Result as Exception;
			if (failure != null)
			{
				string maskedError = DownloadDisplayMask.Apply(failure.Message);
				Logger.Log("Prerequisite installation error: " + failure);
				SetProgressHeaderSafe("Falha em uma etapa selecionada", maskedError);
				installationComplete = false;
				MessageBox.Show(
					"Uma etapa selecionada falhou ou não pôde ser confirmada. Nenhum código de erro foi ignorado." +
					Environment.NewLine + Environment.NewLine + maskedError + Environment.NewLine + Environment.NewLine +
					"Execute novamente como Administrador e confira o diagnóstico do PC.",
					"Pré-requisitos",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);
				return;
			}

			PrerequisiteInstallationResult result = e.Result as PrerequisiteInstallationResult;
			if (result == null)
			{
				SetProgressHeaderSafe("Resultado indisponível", "A instalação terminou sem um diagnóstico verificável.");
				installationComplete = false;
				return;
			}

			if (result.Selection.OpenNvidiaOfficialSource && !nvidiaAppOpened)
			{
				nvidiaAppOpened = true;
				NvidiaAppInstallerHelper.InstallOrOpenNvidiaApp();
				UpdateProgressBarSafe();
			}

			gamingReadinessProfile = result.Profile;
			gamingReadinessCapturedAtUtc = DateTime.UtcNow;
			prerequisiteRestartRequired = prerequisiteRestartRequired || (result.Profile != null && result.Profile.PendingRestart);
			ApplyGamingReadinessProfileToUi();
			if (prerequisiteRestartRequired)
			{
				installationComplete = false;
				SetProgressHeaderSafe("Reinicialização pendente",
					"O processamento foi pausado. Salve seus arquivos, reinicie o Windows manualmente e abra o instalador novamente para confirmar os componentes e concluir as etapas restantes.");
				UpdateProgressVisualsSafe();
				return;
			}
			installationComplete = true;
			SetProgressHeaderSafe(
				"Etapas selecionadas processadas",
				"Diagnóstico atualizado: " + (result.Profile == null ? "indisponível" : result.Profile.BuildSummary()) +
				". Confira o resultado e clique em Avançar para continuar.");
			UpdateProgressVisualsSafe();
			Logger.Log("Selected prerequisites processed; waiting for Next on the Prerequisites screen.");
		}

		public void BtnCancel_Click(object sender, EventArgs e)
		{
			if (IsInstallationRunning()) return;
			if (MessageBox.Show(
				ConsumerText.GetString("CancelSure", Array.Empty<object>()),
				ConsumerText.GetString("CancelButtonTitle", Array.Empty<object>()),
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Question) == DialogResult.Yes)
			{
				Application.Exit();
			}
		}

		private void UpdateNvidiaDriverCheckbox()
		{
#if CONSUMER_UI_TESTS
			if (SuppressReadinessForUiTest) return;
#endif
			// Hardware queries run only in the background scanner, never during layout.
			bool hasNvidia = gamingReadinessProfile != null && gamingReadinessProfile.Gpus.Any(gpu =>
				(gpu.Vendor ?? string.Empty).IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
				(gpu.Name ?? string.Empty).IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0);

			chkNvidiaApp.Enabled = hasNvidia && !IsInstallationRunning();
			if (!hasNvidia)
			{
				chkNvidiaApp.Checked = false;
			}
			chkNvidiaApp.Text = hasNvidia
				? "Copiar link oficial do driver NVIDIA"
				: "Driver NVIDIA (GPU NVIDIA não detectada)";
		}

		private void UpdatePrerequisiteOptions()
		{
			if (IsInstallationRunning())
			{
				return;
			}

			if (!selectionInitialized)
			{
				chkVCpp.Checked = true;
				chkDirectX.Checked = true;
				chkDokany.Checked = false;
				chkwinFSP.Checked = false;
				if (chkNvidiaApp != null)
				{
					chkNvidiaApp.Checked = false;
				}
				selectionInitialized = true;
			}

			chkVCpp.Enabled = true;
			chkDirectX.Enabled = true;
			chkDokany.Enabled = IsOfflineOptionApplicable("dokany");
			chkwinFSP.Enabled = IsOfflineOptionApplicable("winfsp");
			UpdateNvidiaDriverCheckbox();
			SetButtonsInstallingState(false);
			UpdateProgressMaximumFromSelection();
		}

		private PrerequisiteSelection GetPrerequisiteSelection()
		{
			return new PrerequisiteSelection
			{
				RuntimeSelection = new GamingRuntimeInstallSelection
				{
					InstallMicrosoftRuntimeStack = chkVCpp.Enabled && chkVCpp.Checked,
					InstallDirectXLegacy = chkDirectX.Enabled && chkDirectX.Checked,
					InstallDokany = chkDokany.Enabled && chkDokany.Checked,
					InstallWinFsp = chkwinFSP.Enabled && chkwinFSP.Checked,
					OpenNvidiaOfficialSource = chkNvidiaApp != null && chkNvidiaApp.Enabled && chkNvidiaApp.Checked
				},
				OpenNvidiaOfficialSource = chkNvidiaApp != null && chkNvidiaApp.Enabled && chkNvidiaApp.Checked
			};
		}

		private int GetSelectedStepCount(PrerequisiteSelection selection)
		{
			if (selection == null || selection.RuntimeSelection == null)
			{
				return 0;
			}
			if (!HasSelectedRuntimeGroup(selection.RuntimeSelection))
				return selection.OpenNvidiaOfficialSource ? 1 : 0;
			// Never let BuildInstallationPlan fall back to a synchronous detector.
			// Next waits for a complete fresh scan when runtime groups are selected.
			if (!HasCurrentReadinessProfile()) return 0;
			int total = RuntimeInstallerHelper.BuildInstallationPlan(gamingReadinessProfile, selection.RuntimeSelection)
				.Count(item => item.Disposition == RuntimeInstallDisposition.InstallFromVerifiedBundle ||
					item.Disposition == RuntimeInstallDisposition.MissingBundle);
			if (selection.OpenNvidiaOfficialSource)
			{
				total++;
			}
			return total;
		}

		private void UpdateProgressMaximumFromSelection()
		{
			if (restoringLockedSelection) return;
			if (IsInstallationRunning())
			{
				RestoreLockedSelection();
				return;
			}
			PrerequisiteSelection selection = GetPrerequisiteSelection();
			int selectionMask = (selection.RuntimeSelection.InstallMicrosoftRuntimeStack ? 1 : 0) |
				(selection.RuntimeSelection.InstallDirectXLegacy ? 2 : 0) |
				(selection.OpenNvidiaOfficialSource ? 4 : 0) |
				(selection.RuntimeSelection.InstallDokany ? 8 : 0) |
				(selection.RuntimeSelection.InstallWinFsp ? 16 : 0);
			if (observedSelectionMask.HasValue && observedSelectionMask.Value != selectionMask)
			{
				bool previouslyComplete = installationComplete;
				installationComplete = false;
				if (previouslyComplete)
					SetProgressHeaderSafe("Seleção alterada", "Avançar processará a seleção atual antes de continuar.");
			}
			observedSelectionMask = selectionMask;
			int selected = GetSelectedStepCount(selection);
			progressBar.Maximum = Math.Max(1, selected);
			progressBar.Value = 0;
			UpdateProgressVisualsSafe();
		}

		private void UpdateProgressBarSafe()
		{
			if (IsDisposed || Disposing)
			{
				return;
			}
			if (InvokeRequired)
			{
				Invoke(new MethodInvoker(UpdateProgressBarSafe));
				return;
			}
			if (progressBar.Value < progressBar.Maximum)
			{
				progressBar.Value++;
			}
			UpdateProgressVisualsSafe();
		}

		private void SetProgressMaximumSafe(int maximum)
		{
			if (IsDisposed || Disposing)
			{
				return;
			}
			if (InvokeRequired)
			{
				Invoke(new Action<int>(SetProgressMaximumSafe), maximum);
				return;
			}
			progressBar.Maximum = Math.Max(1, maximum);
			progressBar.Value = Math.Min(progressBar.Value, progressBar.Maximum);
			UpdateProgressVisualsSafe();
		}

		private bool IsInstallationRunning()
		{
			return selectionLocked || (installerWorker != null && installerWorker.IsBusy);
		}

		private void SetSelectionLocked(bool locked)
		{
			if (selectionLocked == locked) return;
			CheckBox[] options = { chkVCpp, chkDirectX, chkNvidiaApp, chkDokany, chkwinFSP };
			if (locked)
			{
				InvalidateGamingReadinessScan();
				gamingReadinessCapturedAtUtc = DateTime.MinValue;
				lockedCheckedMask = 0;
				unlockedEnabledMask = 0;
				for (int index = 0; index < options.Length; index++)
				{
					if (options[index].Checked) lockedCheckedMask |= 1 << index;
					if (options[index].Enabled) unlockedEnabledMask |= 1 << index;
				}
				selectionLocked = true;
				foreach (CheckBox option in options) option.Enabled = false;
				return;
			}
			RestoreLockedSelection();
			for (int index = 0; index < options.Length; index++)
				options[index].Enabled = (unlockedEnabledMask & (1 << index)) != 0;
			selectionLocked = false;
		}

		private void RestoreLockedSelection()
		{
			if (!selectionLocked || restoringLockedSelection) return;
			restoringLockedSelection = true;
			try
			{
				CheckBox[] options = { chkVCpp, chkDirectX, chkNvidiaApp, chkDokany, chkwinFSP };
				for (int index = 0; index < options.Length; index++)
					options[index].Checked = (lockedCheckedMask & (1 << index)) != 0;
			}
			finally { restoringLockedSelection = false; }
		}

		private bool HasCurrentReadinessProfile()
		{
			TimeSpan age = DateTime.UtcNow - gamingReadinessCapturedAtUtc;
			return !gamingReadinessScanPending && age >= TimeSpan.Zero && age.TotalMinutes <= 5.0 &&
				HasCompleteReadinessProfile(gamingReadinessProfile);
		}

		private static bool HasSelectedRuntimeGroup(GamingRuntimeInstallSelection selection)
		{
			return selection != null && (selection.InstallMicrosoftRuntimeStack || selection.InstallDirectXLegacy ||
				selection.InstallDokany || selection.InstallWinFsp);
		}

		private static bool IsOfflineOptionApplicable(string componentId)
		{
			return GamingRuntimeManifest.GetComponents().Any(component =>
				string.Equals(component.Id, componentId, StringComparison.OrdinalIgnoreCase) &&
				component.CanInstallOffline && GamingRuntimeManifest.IsApplicableToCurrentOs(component));
		}

		private static bool HasCompleteReadinessProfile(GamingReadinessProfile profile)
		{
			if (profile == null) return false;
			GamingRuntimeComponent[] components = GamingRuntimeManifest.GetComponents().ToArray();
			return components.Length > 0 && components.All(component =>
				profile.RuntimeStatuses.Count(status => status != null && status.Component != null &&
					string.Equals(status.Component.Id, component.Id, StringComparison.OrdinalIgnoreCase)) == 1);
		}

		private void InvalidateGamingReadinessScan()
		{
			gamingReadinessRevision++;
			gamingReadinessScanPending = false;
		}

		private void BeginGamingReadinessScan(bool force)
		{
#if CONSUMER_UI_TESTS
			if (SuppressReadinessForUiTest) return;
#endif
			if (IsDisposed || Disposing || !IsHandleCreated || IsInstallationRunning()) return;
			if (!force && HasCurrentReadinessProfile())
			{
				ApplyGamingReadinessProfileToUi();
				return;
			}
			if (gamingReadinessWorker != null && gamingReadinessWorker.IsBusy) return;

			int revision = ++gamingReadinessRevision;
			gamingReadinessScanPending = true;
			gamingReadinessCapturedAtUtc = DateTime.MinValue;
			if (readinessLabel != null)
			{
				readinessLabel.Text = "Analisando hardware e runtimes...";
				readinessLabel.ForeColor = Palette.Muted;
			}

			gamingReadinessWorker = new BackgroundWorker();
			gamingReadinessWorker.DoWork += delegate(object sender, DoWorkEventArgs args)
			{
				args.Result = PrerequisiteDetector.CaptureGamingReadinessProfile();
			};
			gamingReadinessWorker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs args)
			{
				Exception error = args.Error ?? (args.Cancelled ? new OperationCanceledException() : null);
				CompleteGamingReadinessScan(revision, error == null ? args.Result as GamingReadinessProfile : null, error);
			};
			try
			{
				gamingReadinessWorker.RunWorkerAsync();
			}
			catch (Exception error)
			{
				CompleteGamingReadinessScan(revision, null, error);
			}
		}

		private void CompleteGamingReadinessScan(int revision, GamingReadinessProfile profile, Exception error)
		{
			// A scan started before installation must never replace its newer result.
			if (IsDisposed || Disposing || revision != gamingReadinessRevision || IsInstallationRunning()) return;
			gamingReadinessScanPending = false;
			if (error != null || !HasCompleteReadinessProfile(profile))
			{
				gamingReadinessCapturedAtUtc = DateTime.MinValue;
				Logger.Log("Gaming readiness scan failed: " + (error == null ? "incomplete snapshot" : error.ToString()));
				if (readinessLabel != null)
				{
					readinessLabel.Text = "Diagnóstico indisponível — tente novamente";
					readinessLabel.ForeColor = Palette.Warning;
				}
				return;
			}
			gamingReadinessProfile = profile;
			gamingReadinessCapturedAtUtc = DateTime.UtcNow;
			ApplyGamingReadinessProfileToUi();
			UpdateProgressMaximumFromSelection();
		}

		private void ApplyGamingReadinessProfileToUi()
		{
			if (InvokeRequired)
			{
				BeginInvoke(new MethodInvoker(ApplyGamingReadinessProfileToUi));
				return;
			}
			if (gamingReadinessProfile == null)
			{
				return;
			}
			UpdateNvidiaDriverCheckbox();
			if (readinessLabel != null)
			{
				readinessLabel.Text = gamingReadinessProfile.Score + "/100 · " +
					GetGamingReadinessStateText(gamingReadinessProfile.OverallState);
				readinessLabel.ForeColor = GetGamingReadinessStateColor(gamingReadinessProfile.OverallState);
			}
			if (diskSpaceLabel != null)
			{
				bool lowSpace = gamingReadinessProfile.SystemDriveFreeBytes < RuntimeInstallerHelper.MinimumSystemDriveFreeBytes;
				diskSpaceLabel.Text = "Disco do Windows: " + gamingReadinessProfile.SystemDrive + " · " +
					gamingReadinessProfile.SystemDriveFreeDisplay + " livres." +
					(lowSpace ? " Pouco espaço disponível: libere espaço antes de instalar componentes." :
					" Confira também o espaço exigido pelo produto na unidade de destino.") +
					(prerequisiteRestartRequired || gamingReadinessProfile.PendingRestart ? " Reinicialização pendente: reinicie o Windows antes de instalar componentes." : string.Empty);
				diskSpaceLabel.ForeColor = lowSpace || prerequisiteRestartRequired || gamingReadinessProfile.PendingRestart ? Palette.Warning : Palette.Muted;
			}
			if (readinessButton != null)
			{
				readinessButton.Enabled = true;
			}
		}

		private void ShowGamingReadinessDialog(object sender, EventArgs e)
		{
			if (gamingReadinessProfile == null)
			{
				BeginGamingReadinessScan(true);
				MessageBox.Show(this, "O diagnóstico ainda está sendo preparado.", "Diagnóstico do PC",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			using (GamingReadinessDialog dialog = new GamingReadinessDialog(gamingReadinessProfile))
			{
				dialog.ShowDialog(FindForm());
			}
		}

		private static string GetGamingReadinessStateText(GamingReadinessState state)
		{
			switch (state)
			{
				case GamingReadinessState.Ready:
					return "PRONTO";
				case GamingReadinessState.Blocked:
					return "CORRIGIR";
				default:
					return "ATENÇÃO";
			}
		}

		private static Color GetGamingReadinessStateColor(GamingReadinessState state)
		{
			switch (state)
			{
				case GamingReadinessState.Ready:
					return Color.FromArgb(73, 245, 141);
				case GamingReadinessState.Blocked:
					return Color.FromArgb(255, 94, 112);
				default:
					return Color.FromArgb(255, 195, 66);
			}
		}

		private void SetButtonsInstallingState(bool installing)
		{
			SetSelectionLocked(installing);
			btnBack.Visible = true;
			btnNext.Visible = true;
			btnCancel.Visible = true;
			btnBack.Enabled = !installing;
			btnCancel.Enabled = !installing;
			btnNext.Enabled = !installing;
			btnNext.Text = installing ? "Instalando..." : ConsumerText.GetString("Next >", Array.Empty<object>());
			if (installing && contentStack != null && progressSection != null)
			{
				contentStack.ScrollControlIntoView(progressSection);
			}
		}

		private void SetProgressHeaderSafe(string title, string detail)
		{
			if (IsDisposed || Disposing)
			{
				return;
			}
			if (InvokeRequired)
			{
				Invoke(new Action<string, string>(SetProgressHeaderSafe), title, detail);
				return;
			}

			progressTitleText = DownloadDisplayMask.Apply(
				string.IsNullOrWhiteSpace(title) ? "Processando componentes" : title);
			progressDetailText = DownloadDisplayMask.Apply(
				string.IsNullOrWhiteSpace(detail) ? "Aguardando processamento..." : detail);
			if (progressTitleLabel != null)
			{
				progressTitleLabel.Text = progressTitleText;
			}
			if (statusLabel != null)
			{
				statusLabel.Text = progressDetailText;
			}
		}

		private void UpdateProgressVisualsSafe()
		{
			if (IsDisposed || Disposing)
			{
				return;
			}
			if (InvokeRequired)
			{
				Invoke(new MethodInvoker(UpdateProgressVisualsSafe));
				return;
			}

			int maximum = Math.Max(1, progressBar.Maximum);
			int value = Math.Max(0, Math.Min(progressBar.Value, maximum));
			int percent = (int)Math.Round((double)value * 100.0 / maximum);
			if (progressCountLabel != null)
			{
				progressCountLabel.Text = string.Format("Processadas: {0} de {1}", value, maximum);
			}
			if (progressPercentLabel != null)
			{
				progressPercentLabel.Text = percent + "%";
			}
			if (progressHintLabel != null)
			{
				progressHintLabel.Text = prerequisiteRestartRequired
					? "Pausado para reinicialização; as etapas restantes não foram executadas."
					: value >= maximum
					? "As etapas selecionadas foram processadas; confira o diagnóstico."
					: "Hash, tamanho, editor e revogação são verificados antes da execução.";
			}

		}

		private sealed class PrerequisiteSelection
		{
			public GamingRuntimeInstallSelection RuntimeSelection { get; set; }
			public bool OpenNvidiaOfficialSource { get; set; }
		}

		private sealed class PrerequisiteInstallationResult
		{
			public PrerequisiteInstallationResult(PrerequisiteSelection selection, GamingReadinessProfile profile)
			{
				Selection = selection;
				Profile = profile;
			}

			public PrerequisiteSelection Selection { get; private set; }
			public GamingReadinessProfile Profile { get; private set; }
		}
	}
}
