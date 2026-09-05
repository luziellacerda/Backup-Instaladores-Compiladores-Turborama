using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace InstallerHost
{
	internal sealed class GamingReadinessDialog : Form
	{
		private readonly GamingReadinessProfile profile;
		private readonly ListView recommendationsList;
		private readonly TabControl tabs;

		public GamingReadinessDialog(GamingReadinessProfile readinessProfile)
		{
			if (readinessProfile == null)
			{
				throw new ArgumentNullException("readinessProfile");
			}
			profile = readinessProfile;
			Text = "TurboRama — Diagnóstico de prontidão";
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(760, 540);
			Size = new Size(940, 680);
			BackColor = Color.FromArgb(7, 11, 16);
			ForeColor = Color.White;
			Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
			ShowIcon = false;

			Panel header = new Panel
			{
				Dock = DockStyle.Top,
				Height = 82,
				BackColor = Color.FromArgb(11, 18, 25)
			};
			Controls.Add(header);

			Label title = new Label
			{
				AutoSize = false,
				Left = 22,
				Top = 14,
				Width = 650,
				Height = 30,
				Text = "DIAGNÓSTICO DO PC PARA JOGOS E EMULAÇÃO",
				Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold, GraphicsUnit.Point),
				ForeColor = Color.White
			};
			header.Controls.Add(title);

			Label summary = new Label
			{
				AutoSize = false,
				Left = 24,
				Top = 47,
				Width = 720,
				Height = 24,
				Text = profile.BuildSummary(),
				ForeColor = GetStateColor(profile.OverallState)
			};
			header.Controls.Add(summary);

			Label score = new Label
			{
				AutoSize = false,
				Dock = DockStyle.Right,
				Width = 145,
				TextAlign = ContentAlignment.MiddleCenter,
				Text = profile.Score + "/100",
				Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold, GraphicsUnit.Point),
				ForeColor = GetStateColor(profile.OverallState)
			};
			header.Controls.Add(score);

			tabs = new TabControl
			{
				Dock = DockStyle.Fill,
				Padding = new Point(16, 6)
			};
			Controls.Add(tabs);
			tabs.BringToFront();

			tabs.TabPages.Add(BuildHardwarePage());
			tabs.TabPages.Add(BuildComponentsPage());
			TabPage recommendationsPage;
			recommendationsList = BuildRecommendationsList(out recommendationsPage);
			tabs.TabPages.Add(recommendationsPage);
			tabs.TabPages.Add(BuildReportPage());

			Panel footer = new Panel
			{
				Dock = DockStyle.Bottom,
				Height = 56,
				BackColor = Color.FromArgb(11, 18, 25)
			};
			Controls.Add(footer);

			Label legal = new Label
			{
				Left = 18,
				Top = 11,
				Width = 610,
				Height = 35,
				Text = "O TurboRama prepara dependências. Jogos, ROMs, firmware e BIOS devem ser obtidos legalmente pelo usuário.",
				ForeColor = Color.FromArgb(168, 180, 190)
			};
			footer.Controls.Add(legal);

			Button officialButton = CreateButton("COPIAR LINK OFICIAL", 635, 12, 160);
			officialButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			officialButton.Click += OpenSelectedOfficialSource;
			footer.Controls.Add(officialButton);

			Button closeButton = CreateButton("FECHAR", 805, 12, 105);
			closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			closeButton.DialogResult = DialogResult.OK;
			footer.Controls.Add(closeButton);
			AcceptButton = closeButton;
			CancelButton = closeButton;
		}

		private TabPage BuildHardwarePage()
		{
			TabPage page = CreatePage("Hardware e APIs");
			ListView list = CreateListView();
			list.Columns.Add("Item", 210);
			list.Columns.Add("Detectado", 650);
			AddItem(list, "Windows", string.Format("{0} {1} · build {2} · {3}", profile.OsCaption, profile.OsVersion, profile.OsBuild, profile.OsArchitecture));
			AddItem(list, "CPU", string.Format("{0} · {1} núcleos / {2} lógicos", profile.CpuName, profile.PhysicalCoreCount, profile.LogicalProcessorCount));
			AddItem(list, "Virtualização", FormatNullableBoolean(profile.VirtualizationFirmwareEnabled));
			AddItem(list, "SLAT", FormatNullableBoolean(profile.SecondLevelAddressTranslation));
			AddItem(list, "Memória", profile.MemoryDisplay);
			AddItem(list, "Disco do sistema", profile.SystemDrive + " · " + profile.SystemDriveFreeDisplay + " livres");
			AddItem(list, "Direct3D feature level", string.IsNullOrWhiteSpace(profile.Direct3DFeatureLevel) ? "não confirmado" : profile.Direct3DFeatureLevel);
			AddItem(list, "DirectX 12 runtime", profile.DirectX12RuntimePresent ? "presente" : "não detectado");
			AddItem(list, "Vulkan", profile.VulkanLoaderPresent ? (string.IsNullOrWhiteSpace(profile.VulkanLoaderVersion) ? "loader presente" : profile.VulkanLoaderVersion) : "não detectado");
			AddItem(list, "OpenGL", profile.OpenGlLoaderPresent ? "loader do Windows presente; versão depende do driver" : "não detectado");
			foreach (GamingGpuInfo gpu in profile.Gpus)
			{
				AddItem(list, "GPU — " + gpu.Vendor, gpu.Name + " · driver " + (string.IsNullOrWhiteSpace(gpu.DriverVersion) ? "não informado" : gpu.DriverVersion) + " · VRAM " + gpu.AdapterRamDisplay);
			}
			page.Controls.Add(list);
			return page;
		}

		private TabPage BuildComponentsPage()
		{
			TabPage page = CreatePage("Componentes");
			ListView list = CreateListView();
			list.Columns.Add("Estado", 90);
			list.Columns.Add("Nível", 100);
			list.Columns.Add("Componente", 275);
			list.Columns.Add("Detalhe", 330);
			list.Columns.Add("Offline", 80);

			foreach (RuntimeComponentStatus status in profile.RuntimeStatuses)
			{
				ListViewItem item = new ListViewItem(GetStateText(status.State));
				item.ForeColor = GetStateColor(status.State);
				item.SubItems.Add(GetTierText(status.Component.Tier));
				item.SubItems.Add(status.Component.DisplayName);
				item.SubItems.Add(status.Detail);
				item.SubItems.Add(status.BundleAvailable ? "sim" : "—");
				item.Tag = status.Component.OfficialUrl;
				list.Items.Add(item);
			}
			page.Controls.Add(list);
			return page;
		}

		private ListView BuildRecommendationsList(out TabPage page)
		{
			page = CreatePage("Recomendações");
			ListView list = CreateListView();
			list.Columns.Add("Estado", 90);
			list.Columns.Add("Diagnóstico", 245);
			list.Columns.Add("Recomendação", 510);
			foreach (GamingReadinessFinding finding in profile.Findings)
			{
				ListViewItem item = new ListViewItem(GetStateText(finding.State));
				item.ForeColor = GetStateColor(finding.State);
				item.SubItems.Add(finding.Title);
				item.SubItems.Add(finding.Recommendation);
				item.Tag = finding.OfficialUrl;
				list.Items.Add(item);
			}
			page.Controls.Add(list);
			return list;
		}

		private TabPage BuildReportPage()
		{
			TabPage page = CreatePage("Relatório técnico");
			TextBox report = new TextBox
			{
				Dock = DockStyle.Fill,
				Multiline = true,
				ReadOnly = true,
				ScrollBars = ScrollBars.Both,
				WordWrap = false,
				BackColor = Color.FromArgb(7, 11, 16),
				ForeColor = Color.FromArgb(220, 228, 234),
				BorderStyle = BorderStyle.None,
				Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
				Text = profile.BuildDetailedReport()
			};
			page.Controls.Add(report);
			return page;
		}

		private void OpenSelectedOfficialSource(object sender, EventArgs e)
		{
			ListView selectedList = tabs.SelectedTab == null ? null : tabs.SelectedTab.Controls.OfType<ListView>().FirstOrDefault();
			string url = selectedList != null && selectedList.SelectedItems.Count > 0 ? selectedList.SelectedItems[0].Tag as string : null;
			if (string.IsNullOrWhiteSpace(url))
			{
				MessageBox.Show(this, "Selecione um componente ou recomendação que possua fonte oficial.", "Fonte oficial", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			Uri parsed;
			if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) || !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
			{
				MessageBox.Show(this, "O endereço não passou na validação de segurança.", "Fonte oficial", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			try
			{
				Clipboard.SetText(parsed.AbsoluteUri);
				MessageBox.Show(this, "Link copiado. Feche o instalador e cole o endereço no seu navegador.", "Fonte oficial", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, "Não foi possível copiar a fonte oficial: " + ex.Message, "Fonte oficial", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		private static TabPage CreatePage(string text)
		{
			return new TabPage
			{
				Text = text,
				BackColor = Color.FromArgb(7, 11, 16),
				ForeColor = Color.White,
				Padding = new Padding(8)
			};
		}

		private static ListView CreateListView()
		{
			return new ListView
			{
				Dock = DockStyle.Fill,
				View = View.Details,
				FullRowSelect = true,
				HideSelection = false,
				GridLines = false,
				BackColor = Color.FromArgb(7, 11, 16),
				ForeColor = Color.FromArgb(220, 228, 234),
				BorderStyle = BorderStyle.None
			};
		}

		private static Button CreateButton(string text, int left, int top, int width)
		{
			Button button = new Button
			{
				Text = text,
				Left = left,
				Top = top,
				Width = width,
				Height = 32,
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(15, 27, 36),
				ForeColor = Color.FromArgb(74, 238, 255)
			};
			button.FlatAppearance.BorderColor = Color.FromArgb(74, 238, 255);
			return button;
		}

		private static void AddItem(ListView list, string name, string value)
		{
			ListViewItem item = new ListViewItem(name);
			item.SubItems.Add(value ?? string.Empty);
			list.Items.Add(item);
		}

		private static string FormatNullableBoolean(bool? value)
		{
			return value.HasValue ? (value.Value ? "habilitada" : "desabilitada") : "não informado";
		}

		private static string GetTierText(GamingRuntimeTier tier)
		{
			switch (tier)
			{
				case GamingRuntimeTier.Required: return "obrigatório";
				case GamingRuntimeTier.Recommended: return "recomendado";
				case GamingRuntimeTier.Optional: return "opcional";
				default: return "orientação";
			}
		}

		private static string GetStateText(GamingReadinessState state)
		{
			switch (state)
			{
				case GamingReadinessState.Ready: return "OK";
				case GamingReadinessState.Blocked: return "BLOQUEIO";
				case GamingReadinessState.Attention: return "ATENÇÃO";
				case GamingReadinessState.NotApplicable: return "N/A";
				default: return "VERIFICAR";
			}
		}

		private static Color GetStateColor(GamingReadinessState state)
		{
			switch (state)
			{
				case GamingReadinessState.Ready: return Color.FromArgb(73, 245, 141);
				case GamingReadinessState.Blocked: return Color.FromArgb(255, 94, 112);
				case GamingReadinessState.Attention: return Color.FromArgb(255, 195, 66);
				default: return Color.FromArgb(168, 180, 190);
			}
		}
	}
}
