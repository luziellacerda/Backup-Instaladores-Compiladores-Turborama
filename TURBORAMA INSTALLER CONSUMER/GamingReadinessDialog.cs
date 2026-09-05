using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TurboRama.Next;

namespace InstallerHost
{
	internal sealed class GamingReadinessDialog : Form
	{
		private readonly GamingReadinessProfile profile;
		private readonly GamingReadinessRepairPlan repairPlan;
		private readonly Label repairStatus;
		internal bool RepairRequested { get; private set; }
		private readonly ListView recommendationsList;
		private readonly TabControl tabs;
		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			NativeWindowTheme.Apply(Handle);
		}

		public GamingReadinessDialog(GamingReadinessProfile readinessProfile)
		{
			if (readinessProfile == null) throw new ArgumentNullException("readinessProfile");
			profile = readinessProfile;
			repairPlan = GamingReadinessRepairPlanner.Create(profile);
			Name = "GamingReadinessDialog";
			Text = "TurboRama — Diagnóstico de prontidão";
			StartPosition = FormStartPosition.CenterParent;
			AutoScaleDimensions = new SizeF(96F, 96F);
			AutoScaleMode = AutoScaleMode.Dpi;
			MinimumSize = new Size(760, 540);
			Size = new Size(940, 680);
			BackColor = Palette.Background;
			ForeColor = Palette.Text;
			Font = Ui.Font(10F);
			ShowIcon = false;

			TableLayoutPanel layout = new TableLayoutPanel
			{
				Name = "DiagnosticLayout", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
				Padding = new Padding(20), Margin = Padding.Empty, BackColor = Palette.Background
			};
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(layout);

			TableLayoutPanel header = new TableLayoutPanel
			{
				Name = "DiagnosticHeader", Dock = DockStyle.Fill, AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1,
				BackColor = Palette.Surface, Padding = new Padding(18),
				Margin = new Padding(0, 0, 0, 14)
			};
			header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			TableLayoutPanel headingText = Ui.Vertical();
			headingText.Name = "DiagnosticHeadingText";
			Label title = ConsumerLayout.Label("DIAGNÓSTICO DO PC PARA JOGOS E EMULAÇÃO", 15F, true);
			title.Name = "DiagnosticTitle";
			Ui.AddRow(headingText, title);
			Label summary = ConsumerLayout.Label(profile.BuildSummary());
			summary.Name = "DiagnosticSummary";
			summary.ForeColor = GetStateColor(profile.OverallState);
			summary.Margin = Padding.Empty;
			Ui.AddRow(headingText, summary);
			header.Controls.Add(headingText, 0, 0);
			Label score = ConsumerLayout.Label(profile.Score + "/100", 24F, true);
			score.Name = "DiagnosticScore";
			score.ForeColor = GetStateColor(profile.OverallState);
			score.Margin = new Padding(20, 0, 0, 0);
			score.Anchor = AnchorStyles.Right;
			header.Controls.Add(score, 1, 0);
			layout.Controls.Add(header, 0, 0);

			tabs = new DiagnosticTabControl
			{
				Name = "DiagnosticTabs", Dock = DockStyle.Fill, Margin = Padding.Empty,
				Padding = new Point(14, 10), DrawMode = TabDrawMode.OwnerDrawFixed
			};
			tabs.DrawItem += DrawDiagnosticTab;
			tabs.TabPages.Add(BuildHardwarePage());
			tabs.TabPages.Add(BuildComponentsPage());
			TabPage recommendationsPage;
			recommendationsList = BuildRecommendationsList(out recommendationsPage);
			tabs.TabPages.Add(recommendationsPage);
			tabs.TabPages.Add(BuildReportPage());
			layout.Controls.Add(tabs, 0, 1);

			TableLayoutPanel footer = Ui.Vertical();
			footer.Name = "DiagnosticFooter";
			footer.BackColor = Palette.Surface;
			footer.Padding = new Padding(16);
			footer.Margin = new Padding(0, 14, 0, 0);
			Label legal = ConsumerLayout.Label("O TurboRama prepara dependências. Jogos, ROMs, firmware e BIOS devem ser obtidos legalmente pelo usuário.");
			legal.Name = "DiagnosticLegal";
			legal.ForeColor = Palette.Muted;
			Ui.AddRow(footer, legal);
			repairStatus = ConsumerLayout.Label("O reparo instala ou atualiza as dependências indicadas após sua confirmação.");
			repairStatus.Name = "RepairStatus";
			repairStatus.ForeColor = Palette.Muted;
			Ui.AddRow(footer, repairStatus);
			FlowLayoutPanel actions = new FlowLayoutPanel
			{
				Name = "DiagnosticActions", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
				WrapContents = false, FlowDirection = FlowDirection.RightToLeft,
				Dock = DockStyle.Top, Margin = new Padding(0, 8, 0, 0), Padding = Padding.Empty
			};
			Button officialButton = ConsumerLayout.Action("CopyOfficialSource", "COPIAR LINK OFICIAL");
			officialButton.Width = 200;
			officialButton.Margin = new Padding(0, 0, 10, 0);
			officialButton.Click += OpenSelectedOfficialSource;
			Button repairButton = ConsumerLayout.Action("RepairReadiness", repairPlan.CanRepair
				? "REPARAR " + repairPlan.RepairableComponentCount + " PROBLEMAS"
				: "SEM REPARO AUTOMÁTICO", true);
			repairButton.Width = 250;
			repairButton.Margin = new Padding(0, 0, 10, 0);
			repairButton.Enabled = repairPlan.CanRepair;
			repairButton.AccessibleDescription = repairPlan.CanRepair
				? "Prepara a correção das dependências oficiais incorporadas que estão ausentes ou precisam de reparo."
				: "Nenhuma dependência compatível com reparo automático foi detectada.";
			repairButton.Click += ConfirmRepair;
			Button closeButton = ConsumerLayout.Action("CloseDiagnostic", "FECHAR");
			closeButton.Margin = Padding.Empty;
			closeButton.DialogResult = DialogResult.OK;
			actions.Controls.Add(closeButton);
			actions.Controls.Add(repairButton);
			actions.Controls.Add(officialButton);
			Ui.AddRow(footer, actions);
			layout.Controls.Add(footer, 0, 2);
			AcceptButton = closeButton;
			CancelButton = closeButton;
		}

		internal GamingRuntimeInstallSelection RepairSelection
		{
			get { return repairPlan.Selection; }
		}

		private void ConfirmRepair(object sender, EventArgs e)
		{
			if (!repairPlan.CanRepair) return;
			if (!CheckRepairPrerequisites()) return;

			string manualNotice = repairPlan.ManualActionCount > 0
				? Environment.NewLine + Environment.NewLine + repairPlan.ManualActionCount +
					" outra(s) pendência(s) exigem orientação manual e não serão alteradas."
				: string.Empty;
			DialogResult confirmation = MessageBox.Show(this,
				"O TurboRama encontrou " + repairPlan.RepairableComponentCount +
				" dependência(s) que podem ser reparadas com os pacotes oficiais incorporados e verificados." +
				Environment.NewLine + Environment.NewLine +
				"O reparo não altera BIOS, virtualização, memória, espaço em disco, Windows Update ou drivers de vídeo." +
				manualNotice + Environment.NewLine + Environment.NewLine +
				"Deseja iniciar o reparo seguro agora?",
				"Reparar problemas de compatibilidade", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			if (confirmation != DialogResult.Yes) return;

			RepairRequested = true;
			DialogResult = DialogResult.Retry;
			Close();
		}

		internal bool CheckRepairPrerequisites()
		{
			string block = RuntimeInstallerHelper.GetInstallationPreflightBlockReason(profile, repairPlan.CanRepair);
			if (block == null) return true;
			repairStatus.Text = "Reparo não iniciado: " + block;
			repairStatus.ForeColor = Palette.Warning;
			Logger.Log(repairStatus.Text);
			return false;
		}

		private TabPage BuildHardwarePage()
		{
			TabPage page = CreatePage("Hardware e APIs");
			ListView list = CreateListView();
			list.Name = "DiagnosticHardwareList";
			list.Columns.Add("Item", 210);
			list.Columns.Add("Detectado", 650);
			MakeColumnsResponsive(list, new[] { 0.25F, 0.75F }, new[] { 140, 240 });
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
			list.Name = "DiagnosticComponentsList";
			list.Columns.Add("Estado", 90);
			list.Columns.Add("Nível", 100);
			list.Columns.Add("Componente", 275);
			list.Columns.Add("Detalhe", 330);
			list.Columns.Add("Offline", 80);
			MakeColumnsResponsive(list, new[] { 0.12F, 0.14F, 0.28F, 0.37F, 0.09F }, new[] { 74, 88, 152, 200, 58 });

			foreach (RuntimeComponentStatus status in profile.RuntimeStatuses)
			{
				ListViewItem item = new ListViewItem(GetStateText(status.State));
				item.ForeColor = GetStateColor(status.State);
				item.SubItems.Add(GetTierText(status.Component.Tier));
				item.SubItems.Add(status.Component.DisplayName);
				item.SubItems.Add(status.Detail);
				item.SubItems.Add(status.BundleAvailable ? "sim" : "—");
				item.Tag = status.Component.OfficialUrl;
				item.ToolTipText = string.Join(Environment.NewLine, item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(part => part.Text));
				list.Items.Add(item);
			}
			page.Controls.Add(list);
			return page;
		}

		private ListView BuildRecommendationsList(out TabPage page)
		{
			page = CreatePage("Recomendações");
			ListView list = CreateListView();
			list.Name = "DiagnosticRecommendationsList";
			list.Columns.Add("Estado", 90);
			list.Columns.Add("Diagnóstico", 245);
			list.Columns.Add("Recomendação", 510);
			MakeColumnsResponsive(list, new[] { 0.13F, 0.29F, 0.58F }, new[] { 74, 170, 260 });
			foreach (GamingReadinessFinding finding in profile.Findings)
			{
				ListViewItem item = new ListViewItem(GetStateText(finding.State));
				item.ForeColor = GetStateColor(finding.State);
				item.SubItems.Add(finding.Title);
				item.SubItems.Add(finding.Recommendation);
				item.Tag = finding.OfficialUrl;
				item.ToolTipText = string.Join(Environment.NewLine, item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(part => part.Text));
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
				Name = "DiagnosticReport",
				Dock = DockStyle.Fill,
				Multiline = true,
				ReadOnly = true,
				ScrollBars = ScrollBars.Both,
				WordWrap = false,
				BackColor = Palette.Surface,
				ForeColor = Palette.Text,
				BorderStyle = BorderStyle.None,
				Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point),
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
				BackColor = Palette.Surface,
				ForeColor = Palette.Text,
				Padding = new Padding(8)
			};
		}

		private static ListView CreateListView()
		{
			ListView list = new ListView
			{
				Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
				HideSelection = false, GridLines = false, BackColor = Palette.Surface,
				ForeColor = Palette.Text, BorderStyle = BorderStyle.None,
				ShowItemToolTips = true, OwnerDraw = true
			};
			list.DrawItem += delegate(object sender, DrawListViewItemEventArgs args) { args.DrawDefault = true; };
			list.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs args)
			{
				using (Brush background = new SolidBrush(Palette.Raised)) args.Graphics.FillRectangle(background, args.Bounds);
				Rectangle text = Rectangle.Inflate(args.Bounds, -7, -2);
				TextRenderer.DrawText(args.Graphics, args.Header.Text, list.Font, text, Palette.Text,
					TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
			};
			return list;
		}

		private void DrawDiagnosticTab(object sender, DrawItemEventArgs e)
		{
			Rectangle bounds = tabs.GetTabRect(e.Index);
			bool selected = e.Index == tabs.SelectedIndex;
			using (Brush background = new SolidBrush(selected ? Palette.Raised : Palette.Surface))
				e.Graphics.FillRectangle(background, bounds);
			TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, bounds,
				selected ? Palette.Accent : Palette.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
			if (selected)
			{
				using (Pen accent = new Pen(Palette.Accent, Math.Max(2F, tabs.DeviceDpi / 48F)))
					e.Graphics.DrawLine(accent, bounds.Left + 8, bounds.Bottom - 2, bounds.Right - 8, bounds.Bottom - 2);
				if (tabs.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4), Palette.Text, Palette.Raised);
			}
		}

		private static void MakeColumnsResponsive(ListView list, float[] weights, int[] minimumWidths)
		{
			bool arranging = false;
			EventHandler arrange = delegate
			{
				if (arranging || list.IsDisposed || list.Columns.Count != weights.Length) return;
				arranging = true;
				try
				{
					int[] minimum = minimumWidths.Select(width => (int)Math.Ceiling(width * list.DeviceDpi / 96.0)).ToArray();
					int available = Math.Max(minimum.Sum(), list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
					int extra = available - minimum.Sum();
					int assigned = 0;
					for (int index = 0; index < list.Columns.Count; index++)
					{
						int width = index == list.Columns.Count - 1 ? available - assigned : minimum[index] + (int)Math.Floor(extra * weights[index]);
						list.Columns[index].Width = width;
						assigned += width;
					}
				}
				finally { arranging = false; }
			};
			list.ClientSizeChanged += arrange;
			list.HandleCreated += arrange;
			arrange(list, EventArgs.Empty);
		}

		private sealed class DiagnosticTabControl : TabControl
		{
			public DiagnosticTabControl()
			{
				SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
					ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			}

			protected override void OnPaint(PaintEventArgs e)
			{
                using (Brush surface = new SolidBrush(Palette.Surface))
                    e.Graphics.FillRectangle(surface, Rectangle.Intersect(ClientRectangle, e.ClipRectangle));
				for (int index = 0; index < TabCount; index++)
				{
					DrawItemState state = index == SelectedIndex ? DrawItemState.Selected : DrawItemState.None;
					OnDrawItem(new DrawItemEventArgs(e.Graphics, Font, GetTabRect(index), index, state));
				}
				Rectangle border = DisplayRectangle;
				border.Inflate(1, 1);
				using (Pen line = new Pen(Palette.Line)) e.Graphics.DrawRectangle(line, border);
				base.OnPaint(e);
			}

			protected override void OnSelectedIndexChanged(EventArgs e)
			{
				base.OnSelectedIndexChanged(e);
				Invalidate();
			}
		}

		private static void AddItem(ListView list, string name, string value)
		{
			ListViewItem item = new ListViewItem(name);
			item.SubItems.Add(value ?? string.Empty);
			item.ToolTipText = string.Join(Environment.NewLine, item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(part => part.Text));
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
				case GamingReadinessState.Ready: return Palette.Accent;
				case GamingReadinessState.Blocked: return Color.FromArgb(255, 94, 112);
				case GamingReadinessState.Attention: return Palette.Warning;
				default: return Palette.Muted;
			}
		}
	}
}
