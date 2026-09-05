using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using InstallerHost.Properties;

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
		private bool installationComplete;
		private bool selectionInitialized;
		private bool nvidiaAppOpened;
		private bool premiumLayoutBuilt;
		private bool premiumLayoutRunning;
		private Panel premiumPanel;
		private Panel premiumSidebarPanel;
		private Label premiumProgressTitleLabel;
		private Label premiumProgressDetailLabel;
		private Label premiumProgressCountLabel;
		private Label premiumProgressPercentLabel;
		private Label premiumProgressHintLabel;
		private Panel premiumProgressTrackPanel;
		private Panel premiumProgressFillPanel;
		private Label premiumReadinessLabel;
		private Button premiumReadinessButton;
		private string premiumProgressTitleText = "Pronto para iniciar";
		private string premiumProgressDetailText = "Escolha os grupos visíveis e clique em Next para continuar.";

		public PrerequisiteControl(MainForm main)
		{
			mainForm = main;
			InitializeComponent();

			wizardHeader.Text = Texts.GetString("PrerequisiteIntro", Array.Empty<object>());
			lblAllInstalled.Text = Texts.GetString("All prerequisites installed", Array.Empty<object>());
			btnCancel.Text = Texts.GetString("Cancel", Array.Empty<object>());
			btnNext.Text = Texts.GetString("Next >", Array.Empty<object>());
			btnBack.Text = Texts.GetString("< Back", Array.Empty<object>());

			CreateNvidiaDriverCheckbox();
			UpdatePrerequisiteOptions();
			BuildPremiumPrerequisitePanelOnce();
			BeginGamingReadinessScan(false);

			Load += delegate
			{
				if (!IsInstallationRunning())
				{
					UpdatePrerequisiteOptions();
				}
				BuildPremiumPrerequisitePanelOnce();
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
				BuildPremiumPrerequisitePanelOnce();
				BeginGamingReadinessScan(false);
			};

			// O resize altera somente bounds/anchors. Nunca recria o painel nem
			// substitui os checkboxes gerados pelo Designer.
			Resize += delegate { LayoutPremiumPrerequisitePanel(); };
		}

		public bool SkipIfAllInstalled()
		{
			return false;
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			if (!IsInstallationRunning())
			{
				UpdatePrerequisiteOptions();
			}
			BuildPremiumPrerequisitePanelOnce();
			BeginGamingReadinessScan(false);
			ActiveControl = btnNext;
		}

		private void BtnBack_Click(object sender, EventArgs e)
		{
			mainForm.ShowLicense();
		}

		private void BtnNext_Click(object sender, EventArgs e)
		{
			if (installationComplete)
			{
				mainForm.ShowInstall();
				return;
			}
			if (IsInstallationRunning())
			{
				return;
			}

			PrerequisiteSelection selection = GetPrerequisiteSelection();
			int totalSteps = GetSelectedStepCount(selection);
			if (totalSteps <= 0)
			{
				Logger.Log("No prerequisite action selected or required; continuing to Install screen.");
				installationComplete = true;
				mainForm.ShowInstall();
				return;
			}

			progressBar.Maximum = Math.Max(1, totalSteps);
			progressBar.Value = 0;
			progressBar.Visible = false;
			statusLabel.Visible = false;
			SetPremiumButtonsInstallingState(true);
			SetPremiumProgressHeaderSafe("Instalando componentes", "Validando o catálogo incorporado antes de cada etapa...");
			UpdatePremiumProgressVisualsSafe();

			installerWorker = new BackgroundWorker();
			installerWorker.DoWork += InstallerWorker_DoWork;
			installerWorker.RunWorkerCompleted += InstallerWorker_RunWorkerCompleted;
			installerWorker.RunWorkerAsync(selection);
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
					SetPremiumProgressHeaderSafe,
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
			SetPremiumButtonsInstallingState(false);
			Exception failure = e.Error ?? e.Result as Exception;
			if (failure != null)
			{
				string maskedError = DownloadDisplayMask.Apply(failure.Message);
				Logger.Log("Prerequisite installation error: " + failure);
				SetPremiumProgressHeaderSafe("Falha em uma etapa selecionada", maskedError);
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
				SetPremiumProgressHeaderSafe("Resultado indisponível", "A instalação terminou sem um diagnóstico verificável.");
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
			ApplyGamingReadinessProfileToUi();
			installationComplete = true;
			SetPremiumProgressHeaderSafe(
				"Etapas selecionadas processadas",
				"Diagnóstico atualizado: " + (result.Profile == null ? "indisponível" : result.Profile.BuildSummary()) +
				". Itens recomendados não confirmados continuam visíveis no diagnóstico.");
			UpdatePremiumProgressVisualsSafe();
			Logger.Log("Selected prerequisites processed; continuing to Install screen.");
			mainForm.ShowInstall();
		}

		public void BtnCancel_Click(object sender, EventArgs e)
		{
			if (MessageBox.Show(
				Texts.GetString("CancelSure", Array.Empty<object>()),
				Texts.GetString("CancelButtonTitle", Array.Empty<object>()),
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Question) == DialogResult.Yes)
			{
				Application.Exit();
			}
		}

		private void CreateNvidiaDriverCheckbox()
		{
			bool hasNvidia = NvidiaAppInstallerHelper.HasNvidiaGpu();
			if (chkNvidiaApp == null)
			{
				chkNvidiaApp = new CheckBox
				{
					AutoSize = true,
					Location = new Point(24, 221),
					Name = "chkNvidiaApp",
					TabIndex = 6,
					Checked = false
				};
				Controls.Add(chkNvidiaApp);
			}

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
			chkDokany.Enabled = false;
			chkDokany.Checked = false;
			chkwinFSP.Enabled = false;
			chkwinFSP.Checked = false;
			CreateNvidiaDriverCheckbox();
			lblAllInstalled.Visible = false;
			statusLabel.Visible = false;
			progressBar.Visible = false;
			SetPremiumButtonsInstallingState(false);
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
			int total = RuntimeInstallerHelper.BuildInstallationPlan(gamingReadinessProfile, selection.RuntimeSelection)
				.Count(item => item.Disposition == RuntimeInstallDisposition.InstallFromVerifiedBundle);
			if (selection.OpenNvidiaOfficialSource)
			{
				total++;
			}
			return total;
		}

		private void UpdateProgressMaximumFromSelection()
		{
			if (IsInstallationRunning())
			{
				return;
			}
			int selected = GetSelectedStepCount(GetPrerequisiteSelection());
			progressBar.Maximum = Math.Max(1, selected);
			progressBar.Value = 0;
			UpdatePremiumProgressVisualsSafe();
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
			UpdatePremiumProgressVisualsSafe();
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
			UpdatePremiumProgressVisualsSafe();
		}

		private bool IsInstallationRunning()
		{
			return installerWorker != null && installerWorker.IsBusy;
		}

		private void BeginGamingReadinessScan(bool force)
		{
			if (IsDisposed || Disposing)
			{
				return;
			}
			if (!force && gamingReadinessProfile != null &&
				(DateTime.UtcNow - gamingReadinessCapturedAtUtc).TotalMinutes < 5.0)
			{
				ApplyGamingReadinessProfileToUi();
				return;
			}
			if (gamingReadinessWorker != null && gamingReadinessWorker.IsBusy)
			{
				return;
			}

			if (premiumReadinessLabel != null)
			{
				premiumReadinessLabel.Text = "Analisando hardware e runtimes...";
				premiumReadinessLabel.ForeColor = TurboramaPremiumUi.Muted;
			}

			gamingReadinessWorker = new BackgroundWorker();
			gamingReadinessWorker.DoWork += delegate(object sender, DoWorkEventArgs args)
			{
				args.Result = PrerequisiteDetector.CaptureGamingReadinessProfile();
			};
			gamingReadinessWorker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs args)
			{
				if (IsDisposed || Disposing)
				{
					return;
				}
				if (args.Error != null)
				{
					Logger.Log("Gaming readiness scan failed: " + args.Error);
					if (premiumReadinessLabel != null)
					{
						premiumReadinessLabel.Text = "Diagnóstico indisponível";
						premiumReadinessLabel.ForeColor = Color.FromArgb(255, 195, 66);
					}
					return;
				}
				gamingReadinessProfile = args.Result as GamingReadinessProfile;
				gamingReadinessCapturedAtUtc = DateTime.UtcNow;
				ApplyGamingReadinessProfileToUi();
				if (!IsInstallationRunning())
				{
					UpdateProgressMaximumFromSelection();
				}
			};
			gamingReadinessWorker.RunWorkerAsync();
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
			if (premiumReadinessLabel != null)
			{
				premiumReadinessLabel.Text = gamingReadinessProfile.Score + "/100 · " +
					GetGamingReadinessStateText(gamingReadinessProfile.OverallState);
				premiumReadinessLabel.ForeColor = GetGamingReadinessStateColor(gamingReadinessProfile.OverallState);
			}
			if (premiumReadinessButton != null)
			{
				premiumReadinessButton.Enabled = true;
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

		private void SetPremiumButtonsInstallingState(bool installing)
		{
			btnBack.Visible = true;
			btnNext.Visible = true;
			btnCancel.Visible = true;
			btnBack.Enabled = !installing;
			btnCancel.Enabled = !installing;
			btnNext.Enabled = !installing;
			btnNext.Text = installing ? "Instalando..." : Texts.GetString("Next >", Array.Empty<object>());
			btnBack.BringToFront();
			btnNext.BringToFront();
			btnCancel.BringToFront();
		}

		private void SetPremiumProgressHeaderSafe(string title, string detail)
		{
			if (IsDisposed || Disposing)
			{
				return;
			}
			if (InvokeRequired)
			{
				Invoke(new Action<string, string>(SetPremiumProgressHeaderSafe), title, detail);
				return;
			}

			premiumProgressTitleText = DownloadDisplayMask.Apply(
				string.IsNullOrWhiteSpace(title) ? "Processando componentes" : title);
			premiumProgressDetailText = DownloadDisplayMask.Apply(
				string.IsNullOrWhiteSpace(detail) ? "Aguardando processamento..." : detail);
			if (premiumProgressTitleLabel != null)
			{
				premiumProgressTitleLabel.Text = premiumProgressTitleText;
			}
			if (premiumProgressDetailLabel != null)
			{
				premiumProgressDetailLabel.Text = premiumProgressDetailText;
			}
		}

		private void UpdatePremiumProgressVisualsSafe()
		{
			if (IsDisposed || Disposing)
			{
				return;
			}
			if (InvokeRequired)
			{
				Invoke(new MethodInvoker(UpdatePremiumProgressVisualsSafe));
				return;
			}

			int maximum = Math.Max(1, progressBar.Maximum);
			int value = Math.Max(0, Math.Min(progressBar.Value, maximum));
			int percent = (int)Math.Round((double)value * 100.0 / maximum);
			if (premiumProgressCountLabel != null)
			{
				premiumProgressCountLabel.Text = string.Format("Processadas: {0} de {1}", value, maximum);
			}
			if (premiumProgressPercentLabel != null)
			{
				premiumProgressPercentLabel.Text = percent + "%";
			}
			if (premiumProgressHintLabel != null)
			{
				premiumProgressHintLabel.Text = value >= maximum
					? "As etapas selecionadas foram processadas; confira o diagnóstico."
					: "Hash, tamanho, editor e revogação são verificados antes da execução.";
			}
			if (premiumProgressFillPanel != null && premiumProgressTrackPanel != null)
			{
				int trackWidth = Math.Max(1, premiumProgressTrackPanel.ClientSize.Width);
				premiumProgressFillPanel.Width = Math.Max(0, Math.Min(trackWidth,
					(int)Math.Round((double)trackWidth * value / maximum)));
				premiumProgressFillPanel.Height = premiumProgressTrackPanel.Height;
			}
		}

		private void BuildPremiumPrerequisitePanelOnce()
		{
			if (premiumLayoutBuilt)
			{
				LayoutPremiumPrerequisitePanel();
				return;
			}
			if (premiumLayoutRunning)
			{
				return;
			}

			premiumLayoutRunning = true;
			try
			{
				SuspendLayout();
				BackColor = TurboramaPremiumUi.Background;
				foreach (Control control in Controls)
				{
					if (!(control is Button))
					{
						control.Visible = false;
					}
				}

				premiumPanel = new Panel
				{
					Name = "turboramaPremiumPrerequisitePanel",
					BackColor = TurboramaPremiumUi.Background,
					ForeColor = Color.White,
					BorderStyle = BorderStyle.None
				};
				Controls.Add(premiumPanel);

				premiumSidebarPanel = new Panel
				{
					Name = "premiumPrerequisiteSidebar",
					Left = 0,
					Top = 0,
					Width = 190,
					BackColor = Color.FromArgb(2, 12, 5),
					Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
				};
				premiumPanel.Controls.Add(premiumSidebarPanel);

				Label logo = TurboramaPremiumUi.MakeLabel("LZ", 20, 18, 60, 42, TurboramaPremiumUi.Green, 20f, true);
				logo.TextAlign = ContentAlignment.MiddleCenter;
				logo.BorderStyle = BorderStyle.FixedSingle;
				premiumSidebarPanel.Controls.Add(logo);
				premiumSidebarPanel.Controls.Add(TurboramaPremiumUi.MakeLabel("TURBORAMA", 20, 70, 150, 26, Color.White, 12f, true));
				premiumSidebarPanel.Controls.Add(TurboramaPremiumUi.MakeLabel("SYSTEM CHECK", 20, 98, 150, 20, TurboramaPremiumUi.Green, 8.5f, true));

				Panel accent = new Panel { Left = 20, Top = 130, Width = 140, Height = 3, BackColor = TurboramaPremiumUi.Green };
				premiumSidebarPanel.Controls.Add(accent);
				premiumSidebarPanel.Controls.Add(TurboramaPremiumUi.MakeLabel("PACOTE VERIFICADO", 20, 150, 150, 22, TurboramaPremiumUi.Muted, 8.5f, false));
				premiumSidebarPanel.Controls.Add(TurboramaPremiumUi.MakeLabel("Sem ROMs ou BIOS", 20, 174, 150, 22, TurboramaPremiumUi.Green, 8.5f, true));
				premiumSidebarPanel.Controls.Add(TurboramaPremiumUi.MakeLabel("PRONTIDÃO DO PC", 20, 222, 150, 18, TurboramaPremiumUi.Muted, 8.2f, true));

				premiumReadinessLabel = TurboramaPremiumUi.MakeLabel(
					gamingReadinessProfile == null ? "Analisando..." : gamingReadinessProfile.Score + "/100 · " + GetGamingReadinessStateText(gamingReadinessProfile.OverallState),
					20, 242, 150, 23,
					gamingReadinessProfile == null ? TurboramaPremiumUi.Muted : GetGamingReadinessStateColor(gamingReadinessProfile.OverallState),
					9f, true);
				premiumSidebarPanel.Controls.Add(premiumReadinessLabel);
				premiumReadinessButton = new Button
				{
					Name = "btnGamingReadiness",
					Text = "VER DIAGNÓSTICO",
					Left = 20,
					Top = 271,
					Width = 148,
					Height = 31,
					FlatStyle = FlatStyle.Flat,
					BackColor = Color.FromArgb(11, 24, 31),
					ForeColor = Color.FromArgb(74, 238, 255),
					Font = new Font("Segoe UI", 7.6f, FontStyle.Bold),
					Enabled = gamingReadinessProfile != null
				};
				premiumReadinessButton.FlatAppearance.BorderColor = Color.FromArgb(74, 238, 255);
				premiumReadinessButton.FlatAppearance.BorderSize = 1;
				premiumReadinessButton.Click += ShowGamingReadinessDialog;
				premiumSidebarPanel.Controls.Add(premiumReadinessButton);

				int contentLeft = 220;
				int contentWidth = Math.Max(300, Math.Max(630, Width) - contentLeft - 25);
				AddWideControl(TurboramaPremiumUi.MakeLabel("Preparação para jogos e emulação", contentLeft, 20, contentWidth, 32, Color.White, 15f, true));
				AddWideControl(TurboramaPremiumUi.MakeLabel("Cada opção visível controla somente o grupo descrito; nada é baixado nesta tela.", contentLeft, 52, contentWidth, 22, TurboramaPremiumUi.Muted, 8.7f, false));

				Panel line = new Panel { Left = contentLeft, Top = 80, Width = 120, Height = 3, BackColor = TurboramaPremiumUi.Green };
				premiumPanel.Controls.Add(line);

				AddPremiumCheckRow(chkVCpp, contentLeft, 96, contentWidth,
					"Stack Microsoft recomendado", ".NET Desktop 10/8, VC++ 2005–2022 e WebView2", true);
				AddPremiumCheckRow(chkDirectX, contentLeft, 137, contentWidth,
					"DirectX legado June 2010", "Somente bibliotecas antigas; DirectX 11/12 vem do Windows/driver", true);
				AddPremiumCheckRow(chkNvidiaApp, contentLeft, 178, contentWidth,
					"Copiar link oficial NVIDIA", "Copia nvidia.com para abrir no navegador após fechar o instalador", chkNvidiaApp.Enabled);
				AddPremiumCheckRow(chkDokany, contentLeft, 219, contentWidth,
					"Dokany (não incluído)", "Opcional; obtenha manualmente na fonte oficial somente se necessário", false);
				AddPremiumCheckRow(chkwinFSP, contentLeft, 260, contentWidth,
					"WinFsp (não incluído)", "Opcional; obtenha manualmente em winfsp.dev somente se necessário", false);

				AddWideControl(TurboramaPremiumUi.MakeLabel(
					"Payloads selecionados serão aceitos apenas se coincidirem com o catálogo incorporado.",
					contentLeft, 305, contentWidth, 22, TurboramaPremiumUi.Green, 8.7f, true));

				premiumProgressTitleLabel = TurboramaPremiumUi.MakeLabel(premiumProgressTitleText, contentLeft, 331, contentWidth - 90, 22, Color.White, 9.8f, true);
				premiumProgressTitleLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
				premiumPanel.Controls.Add(premiumProgressTitleLabel);
				premiumProgressPercentLabel = TurboramaPremiumUi.MakeLabel("0%", contentLeft + contentWidth - 80, 331, 80, 22, TurboramaPremiumUi.Green, 9.8f, true);
				premiumProgressPercentLabel.TextAlign = ContentAlignment.MiddleRight;
				premiumProgressPercentLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
				premiumPanel.Controls.Add(premiumProgressPercentLabel);
				premiumProgressDetailLabel = TurboramaPremiumUi.MakeLabel(premiumProgressDetailText, contentLeft, 354, contentWidth, 26, TurboramaPremiumUi.Muted, 8.3f, false);
				premiumProgressDetailLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
				premiumPanel.Controls.Add(premiumProgressDetailLabel);

				premiumProgressTrackPanel = new Panel
				{
					Left = contentLeft,
					Top = 383,
					Width = contentWidth,
					Height = 16,
					BackColor = Color.FromArgb(48, 48, 48),
					BorderStyle = BorderStyle.FixedSingle,
					Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
				};
				premiumPanel.Controls.Add(premiumProgressTrackPanel);
				premiumProgressFillPanel = new Panel
				{
					Left = 0,
					Top = 0,
					Width = 0,
					Height = premiumProgressTrackPanel.Height,
					BackColor = TurboramaPremiumUi.Green
				};
				premiumProgressTrackPanel.Controls.Add(premiumProgressFillPanel);

				premiumProgressCountLabel = TurboramaPremiumUi.MakeLabel("Processadas: 0", contentLeft, 403, contentWidth, 20, Color.White, 8.4f, true);
				AddWideControl(premiumProgressCountLabel);
				premiumProgressHintLabel = TurboramaPremiumUi.MakeLabel(
					"Hash, tamanho, editor e revogação são verificados antes da execução.",
					contentLeft, 423, contentWidth, 22, TurboramaPremiumUi.Muted, 8.1f, false);
				AddWideControl(premiumProgressHintLabel);

				chkVCpp.CheckedChanged += delegate { UpdateProgressMaximumFromSelection(); };
				chkDirectX.CheckedChanged += delegate { UpdateProgressMaximumFromSelection(); };
				chkNvidiaApp.CheckedChanged += delegate { UpdateProgressMaximumFromSelection(); };
				premiumLayoutBuilt = true;
				LayoutPremiumPrerequisitePanel();
				UpdatePremiumProgressVisualsSafe();
				premiumPanel.BringToFront();
				TurboramaPremiumUi.BringNavigationButtonsToFront(this);
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to build prerequisite panel: " + ex);
			}
			finally
			{
				premiumLayoutRunning = false;
				ResumeLayout(false);
			}
		}

		private void AddWideControl(Control control)
		{
			control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			premiumPanel.Controls.Add(control);
		}

		private void AddPremiumCheckRow(
			CheckBox checkBox,
			int left,
			int top,
			int width,
			string title,
			string subtitle,
			bool enabled)
		{
			if (checkBox == null)
			{
				throw new InvalidOperationException("Checkbox original obrigatório não encontrado: " + title + ".");
			}

			Panel row = new Panel
			{
				Left = left,
				Top = top,
				Width = width,
				Height = 36,
				BackColor = TurboramaPremiumUi.PanelMid,
				BorderStyle = BorderStyle.FixedSingle,
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
			};
			premiumPanel.Controls.Add(row);
			Panel stripe = new Panel
			{
				Left = 0,
				Top = 0,
				Width = 4,
				Height = row.Height,
				BackColor = enabled ? TurboramaPremiumUi.Green : TurboramaPremiumUi.Muted
			};
			row.Controls.Add(stripe);

			checkBox.Parent = row;
			checkBox.Left = 14;
			checkBox.Top = 8;
			checkBox.Width = 210;
			checkBox.Height = 20;
			checkBox.AutoSize = false;
			checkBox.Text = title;
			checkBox.Visible = true;
			checkBox.Enabled = enabled;
			checkBox.ThreeState = false;
			if (!enabled)
			{
				checkBox.Checked = false;
			}
			TurboramaPremiumUi.StyleCheckBox(checkBox);
			row.Controls.Add(checkBox);
			checkBox.BringToFront();

			Label description = TurboramaPremiumUi.MakeLabel(
				subtitle, 230, 8, Math.Max(60, row.Width - 240), 20,
				enabled ? TurboramaPremiumUi.Muted : Color.FromArgb(120, 120, 120), 8f, false);
			description.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			row.Controls.Add(description);
		}

		private void LayoutPremiumPrerequisitePanel()
		{
			if (!premiumLayoutBuilt || premiumPanel == null || premiumLayoutRunning)
			{
				return;
			}
			premiumPanel.SetBounds(0, 0, Math.Max(630, Width), Math.Max(300, Height - 68));
			if (premiumSidebarPanel != null)
			{
				premiumSidebarPanel.Height = premiumPanel.Height;
			}
			premiumPanel.PerformLayout();
			TurboramaPremiumUi.BringNavigationButtonsToFront(this);
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
