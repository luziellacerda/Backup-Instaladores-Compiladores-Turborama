using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using TurboramaRomLinker.Models;
using TurboramaRomLinker.Services;

namespace TurboramaRomLinker
{
    public sealed class MainForm : Form
    {
        private readonly RomLinkService _service = new RomLinkService();

        private PictureBox _sidebarPicture;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private NeonButton _scanButton;
        private NeonButton _browseButton;
        private NeonButton _applyButton;
        private NeonButton _cleanButton;
        private readonly List<string> _manualRomRoots = new List<string>();

        private Panel _masterCard;
        private PictureBox _masterIcon;
        private Label _masterTitleLabel;
        private Label _masterInfoLabel;

        private Panel _gridCard;
        private Label _gridTitleLabel;
        private Label _gridHintLabel;
        private Button _btnSelectAll;
        private Button _btnDeselectAll;
        private DataGridView _grid;

        private Panel _logCard;
        private Label _logTitleLabel;
        private LinkLabel _clearLogLink;
        private TextBox _logBox;

        private Panel _footerPanel;
        private Label _footerLeftLabel;
        private Label _footerRightLabel;

        private Panel _loadingOverlay;
        private ProgressBar _loadingBar;
        private Label _loadingLabel;

        private readonly List<RomLinkPlanItem> _currentCreatableItems = new List<RomLinkPlanItem>();
        private bool _busy;
        private bool _masterDetected;
        private int _hoverRow = -1;
        private static readonly Color GridSelBack = Color.FromArgb(0, 115, 200);
        private static readonly Color GridHoverBack = Color.FromArgb(28, 55, 110);
        private static readonly Color GridZebraA = Color.FromArgb(7, 14, 33);
        private static readonly Color GridZebraB = Color.FromArgb(12, 19, 43);
        private static readonly Color DriveGreen = Color.FromArgb(84, 255, 115);

        public MainForm()
        {
            Text = "LZ Games - Turborama ROM Linker";
            ShowIcon = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            WindowState = FormWindowState.Maximized;
            MinimumSize = new Size(1100, 700);
            BackColor = Color.FromArgb(3, 8, 20);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            Icon = TryLoadIcon();
            DoubleBuffered = true;

            BuildUi();
            Resize += delegate { PerformLayouting(); };
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            PerformLayouting();
            await Task.Delay(80);
            await ScanAsync();
        }

        private void BuildUi()
        {
            SuspendLayout();

            _sidebarPicture = new PictureBox();
            _sidebarPicture.Left = 0;
            _sidebarPicture.Top = 0;
            _sidebarPicture.Width = 300;
            _sidebarPicture.Height = ClientSize.Height;
            _sidebarPicture.SizeMode = PictureBoxSizeMode.StretchImage;
            _sidebarPicture.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;
            _sidebarPicture.Image = TryLoadSidebarImage();
            Controls.Add(_sidebarPicture);

            _titleLabel = new Label();
            _titleLabel.Text = "GERENCIADOR DE LINKS DE ROMS";
            _titleLabel.Font = new Font("Segoe UI Semibold", 15.8F, FontStyle.Bold, GraphicsUnit.Point);
            _titleLabel.ForeColor = Color.FromArgb(244, 244, 246);
            _titleLabel.AutoEllipsis = false;
            _titleLabel.AutoSize = true;
            _titleLabel.Height = 44;
            Controls.Add(_titleLabel);

            _subtitleLabel = new Label();
            _subtitleLabel.Text = "";
            _subtitleLabel.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point);
            _subtitleLabel.ForeColor = Color.FromArgb(63, 200, 255);
            _subtitleLabel.AutoSize = true;
            _subtitleLabel.Visible = false;
            Controls.Add(_subtitleLabel);

            _scanButton = new NeonButton("ANALISAR", NeonStyle.Blue);
            _scanButton.Click += async delegate { await ScanAsync(); };
            Controls.Add(_scanButton);

            _browseButton = new NeonButton("PROCURAR", NeonStyle.Blue);
            _browseButton.Click += async delegate { await BrowseManualFolderAsync(); };
            Controls.Add(_browseButton);

            _applyButton = new NeonButton("CRIAR", NeonStyle.Green);
            _applyButton.Click += async delegate { await ApplySelectedAsync(); };
            Controls.Add(_applyButton);

            _cleanButton = new NeonButton("LIMPAR", NeonStyle.Red);
            _cleanButton.Click += async delegate { await CleanLinksAsync(); };
            Controls.Add(_cleanButton);

            _masterCard = BuildCard();
            Controls.Add(_masterCard);

            _masterIcon = new PictureBox();
            _masterIcon.BackColor = Color.FromArgb(4, 10, 24);
            _masterIcon.SizeMode = PictureBoxSizeMode.Zoom;
            _masterCard.Controls.Add(_masterIcon);

            _masterTitleLabel = new Label();
            _masterTitleLabel.AutoSize = true;
            _masterTitleLabel.Font = new Font("Segoe UI Semibold", 13.2F, FontStyle.Bold, GraphicsUnit.Point);
            _masterTitleLabel.ForeColor = Color.FromArgb(84, 255, 115);
            _masterCard.Controls.Add(_masterTitleLabel);

            _masterInfoLabel = new Label();
            _masterInfoLabel.AutoSize = false;
            _masterInfoLabel.Font = new Font("Segoe UI", 10.2F, FontStyle.Regular, GraphicsUnit.Point);
            _masterInfoLabel.ForeColor = Color.FromArgb(234, 236, 242);
            _masterCard.Controls.Add(_masterInfoLabel);

            _gridCard = BuildCard();
            Controls.Add(_gridCard);

            _gridTitleLabel = new Label();
            _gridTitleLabel.Text = "Pastas com ROMs válidas";
            _gridTitleLabel.AutoSize = true;
            _gridTitleLabel.Font = new Font("Segoe UI Semibold", 13.2F, FontStyle.Bold, GraphicsUnit.Point);
            _gridTitleLabel.ForeColor = Color.FromArgb(241, 243, 247);
            _gridCard.Controls.Add(_gridTitleLabel);

            _gridHintLabel = new Label();
            _gridHintLabel.Text = "Clique no ☑ de cada HD ou use os botões ao lado";
            _gridHintLabel.AutoSize = true;
            _gridHintLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _gridHintLabel.ForeColor = Color.FromArgb(142, 154, 184);
            _gridCard.Controls.Add(_gridHintLabel);

            // Botões claros (não links flutuantes) — marcar / desmarcar todos os HDs
            _btnSelectAll = CreateSelectToggleButton("☑  Marcar todos", Color.FromArgb(40, 180, 90), Color.FromArgb(12, 45, 28));
            _btnSelectAll.Click += delegate { SetAllUnitChecks(true); };
            _gridCard.Controls.Add(_btnSelectAll);

            _btnDeselectAll = CreateSelectToggleButton("☐  Desmarcar todos", Color.FromArgb(220, 70, 110), Color.FromArgb(45, 18, 30));
            _btnDeselectAll.Click += delegate { SetAllUnitChecks(false); };
            _gridCard.Controls.Add(_btnDeselectAll);

            // Tabela clássica (design original organizado) + multi-HD / loading / PROCURAR
            _grid = new DataGridView();
            _grid.BackgroundColor = Color.FromArgb(6, 12, 30);
            _grid.BorderStyle = BorderStyle.None;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.EnableHeadersVisualStyles = false;
            _grid.GridColor = Color.FromArgb(18, 35, 68);
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _grid.RowTemplate.Height = 72;
            _grid.ScrollBars = ScrollBars.Vertical;
            // Texto bem legível (branco puro, sem cinza apagado)
            Color nameWhite = Color.FromArgb(255, 255, 255);
            Color driveGreen = Color.FromArgb(84, 255, 115); // verde neon — letras da unidade

            _grid.DefaultCellStyle.BackColor = Color.FromArgb(7, 15, 35);
            _grid.DefaultCellStyle.ForeColor = nameWhite;
            // Destaque forte da linha selecionada (clique / setas)
            _grid.DefaultCellStyle.SelectionBackColor = GridSelBack;
            _grid.DefaultCellStyle.SelectionForeColor = Color.White;
            _grid.DefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            _grid.DefaultCellStyle.Padding = new Padding(6, 2, 6, 2);
            _grid.ReadOnly = true; // clica = seleciona linha (não edita célula)
            _grid.StandardTab = true;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(12, 22, 54);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = nameWhite;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.6F, FontStyle.Bold, GraphicsUnit.Point);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 0, 0);
            _grid.ColumnHeadersHeight = 40;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

            // Sem coluna "Usar" única: 1 checkbox real por HD. Ícone na resolução original (coluna Image).
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Ação", Width = 110 });
            // Ícones grandes e bem visíveis na linha
            DataGridViewImageColumn iconCol = new DataGridViewImageColumn
            {
                Name = "Icon",
                HeaderText = "Ícone",
                Width = 76,
                ImageLayout = DataGridViewImageCellLayout.Zoom
            };
            iconCol.DefaultCellStyle.NullValue = null;
            iconCol.DefaultCellStyle.Padding = new Padding(4, 4, 4, 4);
            iconCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _grid.Columns.Add(iconCol);
            DataGridViewTextBoxColumn systemCol = new DataGridViewTextBoxColumn
            {
                Name = "System",
                HeaderText = "Sistema",
                Width = 220,
                ReadOnly = true
            };
            systemCol.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 10.2F, FontStyle.Bold, GraphicsUnit.Point);
            systemCol.DefaultCellStyle.ForeColor = nameWhite;
            _grid.Columns.Add(systemCol);
            // Unidades: host com CheckBox real por HD (☑ D:  ☑ F:)
            DataGridViewTextBoxColumn driveCol = new DataGridViewTextBoxColumn
            {
                Name = "Drive",
                HeaderText = "Unidades (HD)",
                Width = 220,
                ReadOnly = true
            };
            driveCol.DefaultCellStyle.ForeColor = driveGreen;
            _grid.Columns.Add(driveCol);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Origem (TurboRoms)", Width = 320 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Destination", HeaderText = "Destino (sistema\\roms)", Width = 200 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 120 });

            _grid.Columns["Action"].DefaultCellStyle.ForeColor = Color.FromArgb(90, 220, 255);
            _grid.Columns["Action"].DefaultCellStyle.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point);
            _grid.Columns["Status"].DefaultCellStyle.ForeColor = Color.FromArgb(90, 220, 255);
            _grid.Columns["Source"].DefaultCellStyle.ForeColor = nameWhite;
            _grid.Columns["Destination"].DefaultCellStyle.ForeColor = nameWhite;
            _grid.Columns["System"].DefaultCellStyle.ForeColor = nameWhite;

            // Checkboxes de HD desenhados na célula (sem painéis flutuantes = sem pixel quebrado)
            _grid.CellPainting += Grid_DriveCellPainting;
            _grid.CellMouseClick += Grid_DriveCellMouseClick;
            _grid.SelectionChanged += delegate { SyncRowColors(); };
            _grid.CellMouseEnter += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0) return;
                if (_hoverRow == e.RowIndex) return;
                int old = _hoverRow;
                _hoverRow = e.RowIndex;
                if (old >= 0 && old < _grid.Rows.Count) ApplyRowVisual(old);
                ApplyRowVisual(e.RowIndex);
            };
            _grid.MouseLeave += delegate
            {
                int old = _hoverRow;
                _hoverRow = -1;
                if (old >= 0 && old < _grid.Rows.Count) ApplyRowVisual(old);
            };
            _grid.CellMouseDown += delegate(object s, DataGridViewCellMouseEventArgs e)
            {
                if (e.RowIndex < 0 || e.Button != MouseButtons.Left) return;
                // Clique em Unidades: só alterna checkbox (handler de click); não rouba seleção antes
                if (_grid.Columns[e.ColumnIndex].Name == "Drive") return;
                try
                {
                    _grid.ClearSelection();
                    _grid.Rows[e.RowIndex].Selected = true;
                    _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex >= 0 ? e.ColumnIndex : 0];
                }
                catch { }
            };
            _gridCard.Controls.Add(_grid);

            // Overlay de loading (barra animada enquanto pesquisa)
            _loadingOverlay = new Panel();
            _loadingOverlay.BackColor = Color.FromArgb(200, 4, 10, 24);
            _loadingOverlay.Visible = false;
            _loadingLabel = new Label();
            _loadingLabel.AutoSize = false;
            _loadingLabel.TextAlign = ContentAlignment.MiddleCenter;
            _loadingLabel.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold, GraphicsUnit.Point);
            _loadingLabel.ForeColor = Color.FromArgb(120, 220, 255);
            _loadingLabel.Text = "Pesquisando unidades...";
            _loadingOverlay.Controls.Add(_loadingLabel);
            _loadingBar = new ProgressBar();
            _loadingBar.Style = ProgressBarStyle.Marquee;
            _loadingBar.MarqueeAnimationSpeed = 28;
            _loadingOverlay.Controls.Add(_loadingBar);
            Controls.Add(_loadingOverlay);
            _loadingOverlay.BringToFront();

            _logCard = BuildCard();
            Controls.Add(_logCard);

            _logTitleLabel = new Label();
            _logTitleLabel.Text = "LOG";
            _logTitleLabel.AutoSize = true;
            _logTitleLabel.Font = new Font("Segoe UI Semibold", 12.5F, FontStyle.Bold, GraphicsUnit.Point);
            _logTitleLabel.ForeColor = Color.FromArgb(255, 47, 127);
            _logCard.Controls.Add(_logTitleLabel);

            _clearLogLink = new LinkLabel();
            _clearLogLink.Text = "Limpar log";
            _clearLogLink.AutoSize = true;
            _clearLogLink.LinkBehavior = LinkBehavior.NeverUnderline;
            _clearLogLink.LinkColor = Color.FromArgb(255, 77, 120);
            _clearLogLink.ActiveLinkColor = Color.FromArgb(255, 120, 160);
            _clearLogLink.VisitedLinkColor = Color.FromArgb(255, 77, 120);
            _clearLogLink.Click += delegate { _logBox.Clear(); };
            _logCard.Controls.Add(_clearLogLink);

            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.BorderStyle = BorderStyle.None;
            _logBox.BackColor = Color.FromArgb(5, 10, 24);
            _logBox.ForeColor = Color.FromArgb(249, 171, 37);
            _logBox.Font = new Font("Consolas", 9.2F, FontStyle.Regular, GraphicsUnit.Point);
            _logCard.Controls.Add(_logBox);

            _footerPanel = new Panel();
            _footerPanel.BackColor = Color.FromArgb(2, 7, 18);
            Controls.Add(_footerPanel);

            _footerLeftLabel = new Label();
            _footerLeftLabel.AutoSize = true;
            _footerLeftLabel.Font = new Font("Segoe UI Semibold", 8.6F, FontStyle.Bold, GraphicsUnit.Point);
            _footerLeftLabel.ForeColor = Color.FromArgb(90, 255, 101);
            _footerLeftLabel.Text = "●  PRONTO   TURBORAMA";
            _footerPanel.Controls.Add(_footerLeftLabel);

            _footerRightLabel = new Label();
            _footerRightLabel.AutoSize = true;
            _footerRightLabel.Font = new Font("Segoe UI", 9.2F, FontStyle.Regular, GraphicsUnit.Point);
            _footerRightLabel.ForeColor = Color.FromArgb(55, 182, 255);
            _footerRightLabel.Text = "Catálogo: -";
            _footerPanel.Controls.Add(_footerRightLabel);

            _applyButton.Enabled = false;
            _cleanButton.Enabled = false;
            PerformLayouting();
            ResumeLayout(false);
        }

        private void PerformLayouting()
        {
            int sidebarLeft = 0;
            int sidebarTop = 0;
            int sidebarWidth = 300;
            int sidebarHeight = ClientSize.Height;
            _sidebarPicture.SetBounds(sidebarLeft, sidebarTop, sidebarWidth, sidebarHeight);

            int areaLeft = sidebarLeft + sidebarWidth + 32;
            int areaWidth = ClientSize.Width - areaLeft - 18;
            if (areaWidth < 760) areaWidth = 760;

            int buttonWidth = 108;
            int buttonHeight = 32;
            int buttonGap = 10;
            int buttonsTotal = buttonWidth * 4 + buttonGap * 3;
            int buttonsLeft = areaLeft + areaWidth - buttonsTotal - 4;
            if (buttonsLeft < areaLeft + 200) buttonsLeft = areaLeft + 200;

            int topY = 16;
            int titleWidth = Math.Max(220, buttonsLeft - areaLeft - 16);
            _titleLabel.SetBounds(areaLeft + 4, topY + 2, titleWidth, 30);
            _subtitleLabel.Visible = false;

            _scanButton.SetBounds(buttonsLeft, topY, buttonWidth, buttonHeight);
            _browseButton.SetBounds(buttonsLeft + buttonWidth + buttonGap, topY, buttonWidth, buttonHeight);
            _applyButton.SetBounds(buttonsLeft + 2 * (buttonWidth + buttonGap), topY, buttonWidth, buttonHeight);
            _cleanButton.SetBounds(buttonsLeft + 3 * (buttonWidth + buttonGap), topY, buttonWidth, buttonHeight);

            int masterY = topY + 48;
            int masterH = 72;
            _masterCard.SetBounds(areaLeft, masterY, areaWidth, masterH);
            _masterIcon.SetBounds(16, 10, 64, 52);
            _masterTitleLabel.Left = 96;
            _masterTitleLabel.Top = 12;
            _masterInfoLabel.SetBounds(96, 40, _masterCard.Width - 120, 26);

            int footerH = 28;
            int bottomMargin = 10;
            int footerY = ClientSize.Height - footerH - bottomMargin;
            _footerPanel.SetBounds(areaLeft, footerY, areaWidth, footerH);
            _footerLeftLabel.Left = 16;
            _footerLeftLabel.Top = 5;
            _footerRightLabel.Left = Math.Max(200, _footerPanel.Width - _footerRightLabel.PreferredWidth - 16);
            _footerRightLabel.Top = 5;

            int logH = 78;
            int logY = footerY - logH - 8;
            _logCard.SetBounds(areaLeft, logY, areaWidth, logH);
            _logTitleLabel.Left = 18;
            _logTitleLabel.Top = 6;
            _clearLogLink.Left = _logCard.Width - 100;
            _clearLogLink.Top = 8;
            _logBox.SetBounds(14, 28, _logCard.Width - 28, _logCard.Height - 36);

            int gridY = masterY + masterH + 10;
            int gridH = Math.Max(240, logY - gridY - 10);
            _gridCard.SetBounds(areaLeft, gridY, areaWidth, gridH);
            // Barra superior fixa: título | hint | [Marcar todos] [Desmarcar todos]
            int barY = 10;
            int btnW = 148;
            int btnH = 30;
            int btnGap = 8;
            int rightPad = 16;
            _btnDeselectAll.SetBounds(_gridCard.Width - rightPad - btnW, barY, btnW, btnH);
            _btnSelectAll.SetBounds(_btnDeselectAll.Left - btnGap - btnW, barY, btnW, btnH);
            _gridTitleLabel.Left = 18;
            _gridTitleLabel.Top = barY + 4;
            int hintLeft = _gridTitleLabel.Left + _gridTitleLabel.PreferredWidth + 16;
            int hintMax = _btnSelectAll.Left - 12;
            _gridHintLabel.Left = hintLeft;
            _gridHintLabel.Top = barY + 7;
            _gridHintLabel.Visible = hintLeft + 80 < hintMax;

            _grid.SetBounds(12, 48, _gridCard.Width - 24, _gridCard.Height - 60);

            // Ajusta largura da coluna Origem ao espaço restante
            if (_grid.Columns.Count >= 5 && _grid.Columns.Contains("Source"))
            {
                int used = 0;
                for (int i = 0; i < _grid.Columns.Count; i++)
                {
                    if (_grid.Columns[i].Name == "Source") continue;
                    used += _grid.Columns[i].Width;
                }
                int sourceW = _grid.ClientSize.Width - used - 24;
                if (sourceW > 160)
                    _grid.Columns["Source"].Width = sourceW;
            }

            int overlayLeft = areaLeft;
            int overlayTop = masterY;
            int overlayW = areaWidth;
            int overlayH = Math.Max(100, logY - masterY);
            _loadingOverlay.SetBounds(overlayLeft, overlayTop, overlayW, overlayH);
            _loadingLabel.SetBounds(40, overlayH / 2 - 50, overlayW - 80, 36);
            _loadingBar.SetBounds(overlayW / 2 - 180, overlayH / 2, 360, 22);
        }

        private void ShowLoading(string message)
        {
            if (_loadingLabel != null)
                _loadingLabel.Text = string.IsNullOrEmpty(message) ? "Pesquisando..." : message;
            if (_loadingOverlay != null)
            {
                _loadingOverlay.Visible = true;
                _loadingOverlay.BringToFront();
            }
            if (_loadingBar != null)
            {
                _loadingBar.Style = ProgressBarStyle.Marquee;
                _loadingBar.MarqueeAnimationSpeed = 25;
            }
            Application.DoEvents();
        }

        private void HideLoading()
        {
            if (_loadingOverlay != null)
                _loadingOverlay.Visible = false;
            if (_loadingBar != null)
                _loadingBar.MarqueeAnimationSpeed = 0;
        }

        private Panel BuildCard()
        {
            return new NeonCard();
        }

        private async Task ScanAsync()
        {
            await ScanAsync(false, null);
        }

        private async Task ScanAsync(bool showSummaryDialog, string highlightPath)
        {
            if (_busy) return;
            try
            {
                SetBusy(true);
                ShowLoading("Pesquisando unidades e pastas TurboRoms...");
                AppendLog("Iniciando análise de unidades...");
                if (_manualRomRoots.Count > 0)
                    AppendLog("Pastas manuais ativas: " + string.Join(" | ", _manualRomRoots.ToArray()));
                List<string> manual = new List<string>(_manualRomRoots);
                DriveScanResult result = await Task.Run(delegate { return _service.BuildPlan(manual); });
                HideLoading();
                RenderScanResult(result, showSummaryDialog, highlightPath);
            }
            catch (Exception ex)
            {
                HideLoading();
                MessageBox.Show(ex.ToString(), "Erro ao analisar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                AppendLog("Erro: " + ex.Message);
            }
            finally
            {
                HideLoading();
                SetBusy(false);
            }
        }

        private async Task BrowseManualFolderAsync()
        {
            if (_busy) return;

            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Escolha a unidade (G:\\), TurboRoms, TurboRoms\\roms ou pasta de sistema (ps4, snes...).";
                dlg.ShowNewFolderButton = false;
                try
                {
                    if (Directory.Exists(@"G:\")) dlg.SelectedPath = @"G:\";
                    else if (Directory.Exists(@"F:\")) dlg.SelectedPath = @"F:\";
                }
                catch { }

                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                string path = dlg.SelectedPath;
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    MessageBox.Show("Pasta inválida.", "PROCURAR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                bool exists = false;
                foreach (string p in _manualRomRoots)
                {
                    if (string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    _manualRomRoots.Add(path);

                AppendLog("PROCURAR: pasta escolhida — " + path);
                await ScanAsync(true, path);
            }
        }

        private async Task ApplySelectedAsync()
        {
            if (_busy) return;
            List<RomLinkPlanItem> selected = GetSelectedItems();
            if (selected.Count == 0)
            {
                MessageBox.Show("Marque ao menos um HD (checkbox ☑ D: / ☑ F:) para criar o link.", "Nenhum item selecionado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(
                    "Linkar ARQUIVOS e subpastas para a mestre?\n\n"
                    + "O EmulationStation NÃO lê se a pasta ps5 for um único link.\n\n"
                    + "Será assim:\n"
                    + "  F:\\...\\ps5\\jogo.bat     →  sistema\\roms\\ps5\\jogo.bat\n"
                    + "  F:\\...\\ps5\\lista.xml    →  sistema\\roms\\ps5\\lista.xml\n"
                    + "  F:\\...\\ps5\\videos\\     →  sistema\\roms\\ps5\\videos\\\n\n"
                    + "A pasta sistema\\roms\\ps5 fica REAL; só o conteúdo é link.",
                    "Confirmar",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                SetBusy(true);
                ShowLoading("Linkando arquivos/pastas na mestre e corrigindo bats...");
                List<string> createLog = new List<string>();
                int created = await Task.Run(delegate
                {
                    return _service.CreateSelected(selected, createLog);
                });
                HideLoading();
                foreach (string line in createLog)
                    AppendLog(line);
                int batsFixed = createLog.FindAll(delegate(string s) { return s != null && s.StartsWith("BatFix OK:", StringComparison.OrdinalIgnoreCase); }).Count;
                AppendLog("Criação concluída. Itens linkados: " + created + ". Bats corrigidos: " + batsFixed + ".");
                if (createLog.Exists(delegate(string s) { return s != null && s.IndexOf("Modo de Programador", StringComparison.OrdinalIgnoreCase) >= 0; }))
                {
                    MessageBox.Show(
                        "Alguns links de ARQUIVO falharam.\n\n"
                        + "Ative: Windows → Definições → Para programadores → Modo de programador\n"
                        + "ou execute o Linker como Administrador.\n\n"
                        + "(Links de pastas tipo videos/ normalmente não precisam disso.)",
                        "Atenção",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                await ScanAsync();
            }
            catch (Exception ex)
            {
                HideLoading();
                MessageBox.Show(ex.ToString(), "Erro ao criar links", MessageBoxButtons.OK, MessageBoxIcon.Error);
                AppendLog("Erro ao criar links: " + ex.Message);
            }
            finally
            {
                HideLoading();
                SetBusy(false);
            }
        }

        private async Task CleanLinksAsync()
        {
            if (_busy) return;
            try
            {
                string masterRoot = _service.FindMasterRoot();
                if (string.IsNullOrWhiteSpace(masterRoot))
                {
                    MessageBox.Show("Pasta mestre não encontrada para limpar links.", "Limpar links", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (MessageBox.Show(
                        "Remover apenas LINKS (junctions/symlinks) em sistema\\roms?\n"
                        + "Pastas e arquivos reais na mestre são preservados.",
                        "Confirmar limpeza",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }

                SetBusy(true);
                string romsRoot = Path.Combine(masterRoot, "sistema", "roms");
                int removed = 0;
                int preserved = 0;
                if (Directory.Exists(romsRoot))
                {
                    // Link antigo da pasta inteira do sistema
                    foreach (string dir in Directory.GetDirectories(romsRoot))
                    {
                        if (JunctionService.IsDirectoryReparsePoint(dir))
                        {
                            try { Directory.Delete(dir); removed++; }
                            catch { preserved++; }
                            continue;
                        }

                        int p;
                        removed += JunctionService.CleanReparsePointsIn(dir, out p);
                        preserved += p;
                    }
                }
                AppendLog("Limpeza concluída. Links removidos: " + removed + ". Itens reais preservados: " + preserved + ".");
                await ScanAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Erro ao limpar links", MessageBoxButtons.OK, MessageBoxIcon.Error);
                AppendLog("Erro ao limpar links: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RenderScanResult(DriveScanResult result)
        {
            RenderScanResult(result, false, null);
        }

        private void RenderScanResult(DriveScanResult result, bool showSummaryDialog, string highlightPath)
        {
            _currentCreatableItems.Clear();
            _hoverRow = -1;
            _grid.Rows.Clear();
            _logBox.Clear();

            bool masterOk = !string.IsNullOrWhiteSpace(result.MasterRoot);
            _masterDetected = masterOk;
            _masterTitleLabel.Text = masterOk ? "PASTA MESTRE DETECTADA" : "PASTA MESTRE NÃO ENCONTRADA";
            _masterTitleLabel.ForeColor = masterOk ? Color.FromArgb(84, 255, 115) : Color.FromArgb(255, 77, 132);
            _masterInfoLabel.Text = masterOk
                ? "Mestre validada  •  Catálogo carregado  •  Links em sistema\\roms"
                : "Não encontrou sistema\\emulationstation\\.emulationstation\\es_systems.cfg ao lado do executável.";
            _masterIcon.Image = TryLoadMasterFolderIcon(masterOk);
            _footerRightLabel.Text = "Catálogo: " + result.ValidSystems.Count + " sistemas";
            _footerRightLabel.Left = Math.Max(200, _footerPanel.Width - _footerRightLabel.PreferredWidth - 16);

            foreach (string message in result.Messages)
                AppendLog(message);

            if (!masterOk)
            {
                _applyButton.Enabled = false;
                _cleanButton.Enabled = false;
                return;
            }

            // 1 sistema = 1 linha; HDs diferentes = checkboxes na mesma linha (snes → ☑ D: ☑ F:)
            List<RomLinkPlanItem> visible = result.Items
                .Where(i => i.CanCreate || i.Action == RomLinkAction.AlreadyExists)
                .OrderBy(i => i.SystemName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => GetDriveLetter(i.SourcePath))
                .ToList();

            Dictionary<string, List<RomLinkPlanItem>> bySystem = new Dictionary<string, List<RomLinkPlanItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (RomLinkPlanItem item in visible)
            {
                string key = item.SystemName ?? "";
                if (!bySystem.ContainsKey(key))
                    bySystem[key] = new List<RomLinkPlanItem>();
                bySystem[key].Add(item);
            }

            Dictionary<string, int> countByDrive = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int rowIndex = 0;
            foreach (KeyValuePair<string, List<RomLinkPlanItem>> group in bySystem.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                string systemName = group.Key;
                List<RomLinkPlanItem> sources = group.Value;
                SystemRowBind bind = new SystemRowBind();
                bind.SystemName = systemName;
                // Nome profissional (PlayStation, Super Nintendo...) — pasta técnica fica no path
                string displayName = null;
                if (sources.Count > 0 && sources[0] != null && !string.IsNullOrWhiteSpace(sources[0].DisplayName))
                    displayName = sources[0].DisplayName;
                else
                    displayName = SystemDisplayNames.Get(systemName);
                bind.DisplayName = displayName;
                bind.Icon = SystemIconFactory.GetSystemIcon(systemName);

                foreach (RomLinkPlanItem item in sources)
                {
                    string drive = GetDriveLetter(item.SourcePath);
                    UnitPick unit = new UnitPick();
                    unit.Item = item;
                    unit.Drive = drive;
                    unit.CanCreate = item.CanCreate;
                    // Já vem marcado se pode criar; se já linkado fica desmarcado/desabilitado
                    unit.Selected = item.CanCreate;
                    bind.Units.Add(unit);

                    if (item.CanCreate)
                        _currentCreatableItems.Add(item);

                    if (!countByDrive.ContainsKey(drive))
                        countByDrive[drive] = 0;
                    countByDrive[drive] = countByDrive[drive] + 1;
                }

                int creatableCount = bind.Units.Count(u => u.CanCreate);
                bool anyCreate = creatableCount > 0;

                string action = anyCreate
                    ? (sources.Count > 1 ? "+ Criar (" + creatableCount + " HD)" : "+ Criar link")
                    : "Já linkado";
                string status = anyCreate ? "+ Criar link" : "OK";

                // Origem: todas as pastas na mesma linha
                List<string> origins = new List<string>();
                foreach (UnitPick u in bind.Units)
                {
                    if (u.Item != null && !string.IsNullOrEmpty(u.Item.SourcePath))
                        origins.Add(u.Item.SourcePath);
                }
                string sourceText = string.Join("  ·  ", origins.ToArray());

                // Destino: base sistema\roms\<sys> (+ multi se >1)
                string dest = "";
                if (sources.Count > 0 && sources[0] != null)
                {
                    dest = MakeDestinationRelative(sources[0].LinkPath, result.MasterRoot);
                    if (sources.Count > 1)
                    {
                        // mostra pasta do sistema (sem nested) + multi
                        int slash = dest.IndexOf('\\');
                        // dest tipo sistema\roms\snes ou sistema\roms\snes\D_snes
                        string[] parts = dest.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                            dest = parts[0] + "\\" + parts[1] + "\\" + parts[2] + "  · multi";
                        else
                            dest = dest + "  · multi";
                    }
                }

                // Colunas: Action, Icon, System, Drive (☑ por HD desenhados), Source, Destination, Status
                // Texto na coluna Drive só como fallback; o desenho real é CellPainting
                string driveLabel = string.Join("  ", bind.Units.ConvertAll(u => (u.Selected ? "[x] " : "[ ] ") + u.Drive).ToArray());
                int r = _grid.Rows.Add(
                    action,
                    bind.Icon,
                    displayName,
                    driveLabel,
                    sourceText,
                    dest,
                    status);
                DataGridViewRow row = _grid.Rows[r];
                row.Tag = bind;
                row.Height = 72;
                row.DefaultCellStyle.ForeColor = Color.White;
                row.DefaultCellStyle.SelectionBackColor = GridSelBack;
                row.DefaultCellStyle.SelectionForeColor = Color.White;
                row.Cells["System"].Style.ForeColor = Color.White;
                row.Cells["System"].ToolTipText = "Pasta técnica: " + systemName;
                row.Cells["Source"].Style.ForeColor = Color.White;
                row.Cells["Destination"].Style.ForeColor = Color.White;
                row.Cells["Drive"].Style.ForeColor = DriveGreen;
                row.Cells["Drive"].Style.SelectionForeColor = DriveGreen;
                if (!anyCreate)
                {
                    row.Cells["Action"].Style.ForeColor = Color.FromArgb(160, 230, 255);
                    row.Cells["Status"].Style.ForeColor = Color.FromArgb(160, 230, 255);
                }

                ApplyRowVisual(row.Index);
                rowIndex++;
            }
            SyncRowColors();

            AppendLog("======== RESUMO POR UNIDADE ========");
            if (countByDrive.Count == 0)
                AppendLog("Nenhum sistema em nenhuma unidade.");
            else
            {
                foreach (KeyValuePair<string, int> kv in countByDrive.OrderBy(k => k.Key))
                    AppendLog("OK  " + kv.Key + "  →  " + kv.Value + " pasta(s)");
            }
            AppendLog("Pastas com jogos válidos prontas para adicionar como link: " + _currentCreatableItems.Count);
            AppendLog("====================================");

            _applyButton.Enabled = _currentCreatableItems.Count > 0;
            _cleanButton.Enabled = _masterDetected;

            if (countByDrive.Count > 0)
            {
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, int> kv in countByDrive.OrderBy(k => k.Key))
                    parts.Add(kv.Key + "=" + kv.Value);
                _masterInfoLabel.Text = "Mestre validada  •  Catálogo carregado  •  Links em sistema\\roms  •  "
                    + string.Join("  ", parts.ToArray()) + "  •  " + _currentCreatableItems.Count + " p/ criar";
            }

            if (showSummaryDialog && countByDrive.Count == 0)
            {
                MessageBox.Show(this,
                    "Nenhum sistema com jogos nesta pasta/unidade.\n\n"
                    + (string.IsNullOrEmpty(highlightPath) ? "" : ("Pedida: " + highlightPath + "\n\n"))
                    + "Use:\n  G:\\TurboRoms\\roms\\ps4\\\n  G:\\TurboRoms\\roms\\snes\\",
                    "PROCURAR — sem sistemas",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static string GetDriveLetter(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length < 2 || path[1] != ':')
                return "?";
            return char.ToUpperInvariant(path[0]) + ":";
        }

        private static Color ZebraColor(int rowIndex)
        {
            return (rowIndex % 2 == 0) ? GridZebraA : GridZebraB;
        }

        private void ApplyRowVisual(int rowIndex)
        {
            if (_grid == null || rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
            DataGridViewRow row = _grid.Rows[rowIndex];
            Color bg = row.Selected ? GridSelBack
                : (rowIndex == _hoverRow ? GridHoverBack : ZebraColor(rowIndex));
            row.DefaultCellStyle.BackColor = bg;
            row.DefaultCellStyle.SelectionBackColor = GridSelBack;
            row.DefaultCellStyle.SelectionForeColor = Color.White;
            if (_grid.Columns.Contains("Drive"))
                _grid.InvalidateCell(_grid.Columns["Drive"].Index, rowIndex);
        }

        private void SyncRowColors()
        {
            if (_grid == null) return;
            for (int i = 0; i < _grid.Rows.Count; i++)
                ApplyRowVisual(i);
        }

        private void Grid_DriveCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name != "Drive") return;

            e.Handled = true;
            e.PaintBackground(e.ClipBounds, true);

            DataGridViewRow row = _grid.Rows[e.RowIndex];
            SystemRowBind bind = row.Tag as SystemRowBind;
            if (bind == null || bind.Units == null || bind.Units.Count == 0)
            {
                e.Paint(e.ClipBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus);
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Font driveFont = new Font("Segoe UI Semibold", 11.5F, FontStyle.Bold, GraphicsUnit.Point);
            const int box = 18;
            const int chipW = 62;
            int x = e.CellBounds.X + 8;
            int midY = e.CellBounds.Y + e.CellBounds.Height / 2;

            foreach (UnitPick unit in bind.Units)
            {
                unit.HitBounds = new Rectangle(x - e.CellBounds.X, 4, chipW, e.CellBounds.Height - 8);
                bool enabled = unit.CanCreate;
                bool on = unit.Selected && enabled;
                Color border = enabled ? DriveGreen : Color.FromArgb(90, 100, 110);
                Color fill = on ? Color.FromArgb(20, 70, 45) : (enabled ? Color.FromArgb(10, 30, 40) : Color.FromArgb(25, 30, 40));
                Rectangle boxRect = new Rectangle(x, midY - box / 2, box, box);
                using (SolidBrush br = new SolidBrush(fill))
                using (Pen pen = new Pen(border, 2f))
                {
                    e.Graphics.FillRectangle(br, boxRect);
                    e.Graphics.DrawRectangle(pen, boxRect);
                }
                if (on)
                {
                    using (Pen check = new Pen(DriveGreen, 2.6f))
                    {
                        e.Graphics.DrawLines(check, new[]
                        {
                            new Point(boxRect.X + 3, boxRect.Y + 9),
                            new Point(boxRect.X + 7, boxRect.Y + 13),
                            new Point(boxRect.X + 15, boxRect.Y + 4)
                        });
                    }
                }
                Rectangle labelRect = new Rectangle(x + box + 5, e.CellBounds.Y, 36, e.CellBounds.Height);
                TextRenderer.DrawText(e.Graphics, unit.Drive, driveFont, labelRect,
                    enabled ? DriveGreen : Color.FromArgb(100, 110, 120),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                x += chipW;
                if (x > e.CellBounds.Right - 24) break;
            }
            driveFont.Dispose();
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus);
        }

        private void Grid_DriveCellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name != "Drive") return;
            if (e.Button != MouseButtons.Left) return;

            DataGridViewRow row = _grid.Rows[e.RowIndex];
            SystemRowBind bind = row.Tag as SystemRowBind;
            if (bind == null) return;
            try
            {
                _grid.ClearSelection();
                row.Selected = true;
                _grid.CurrentCell = row.Cells["Drive"];
            }
            catch { }

            Point pt = new Point(e.X, e.Y);
            foreach (UnitPick unit in bind.Units)
            {
                if (!unit.CanCreate) continue;
                if (!unit.HitBounds.Contains(pt)) continue;
                unit.Selected = !unit.Selected;
                row.Cells["Drive"].Value = string.Join("  ", bind.Units.ConvertAll(u => (u.Selected ? "[x] " : "[ ] ") + u.Drive).ToArray());
                _grid.InvalidateCell(e.ColumnIndex, e.RowIndex);
                break;
            }
        }

        private static Button CreateSelectToggleButton(string text, Color accent, Color fill)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = accent;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(Math.Min(255, fill.R + 25), Math.Min(255, fill.G + 25), Math.Min(255, fill.B + 25));
            b.FlatAppearance.MouseDownBackColor = accent;
            b.BackColor = fill;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point);
            b.Cursor = Cursors.Hand;
            b.TabStop = false;
            b.UseVisualStyleBackColor = false;
            return b;
        }

        private void SetAllUnitChecks(bool selected)
        {
            if (_grid == null || _busy) return;
            int count = 0;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                SystemRowBind bind = row.Tag as SystemRowBind;
                if (bind == null || bind.Units == null) continue;
                foreach (UnitPick unit in bind.Units)
                {
                    if (unit == null || !unit.CanCreate) continue;
                    unit.Selected = selected;
                    count++;
                }
                if (_grid.Columns.Contains("Drive"))
                {
                    row.Cells["Drive"].Value = string.Join("  ", bind.Units.ConvertAll(u => (u.Selected ? "[x] " : "[ ] ") + u.Drive).ToArray());
                    _grid.InvalidateCell(_grid.Columns["Drive"].Index, row.Index);
                }
            }
            if (_grid.Columns.Contains("Drive"))
                _grid.InvalidateColumn(_grid.Columns["Drive"].Index);
            AppendLog(selected ? "Todos os HDs marcados (" + count + ")." : "Todos os HDs desmarcados (" + count + ").");
        }

        private static Label MakeColHeader(string text, int left)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point);
            l.ForeColor = Color.FromArgb(120, 155, 200);
            l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleLeft;
            // Alinhado às colunas da linha: SISTEMA ~18 | UNIDADES ~180 | DESTINO ~420
            if (left <= 12)
                l.SetBounds(18, 6, 150, 20);
            else if (left < 200)
                l.SetBounds(180, 6, 220, 20);
            else
                l.SetBounds(420, 6, 220, 20);
            return l;
        }

        private static string MakeDestinationRelative(string fullPath, string masterRoot)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return string.Empty;
            try
            {
                if (!string.IsNullOrWhiteSpace(masterRoot) && fullPath.StartsWith(masterRoot, StringComparison.OrdinalIgnoreCase))
                {
                    string rel = fullPath.Substring(masterRoot.Length).TrimStart('\\');
                    return rel;
                }
            }
            catch
            {
            }
            return fullPath;
        }

        private List<RomLinkPlanItem> GetSelectedItems()
        {
            List<RomLinkPlanItem> items = new List<RomLinkPlanItem>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                SystemRowBind bind = row.Tag as SystemRowBind;
                if (bind == null) continue;
                foreach (UnitPick unit in bind.Units)
                {
                    if (unit != null && unit.Selected && unit.CanCreate && unit.Item != null)
                        items.Add(unit.Item);
                }
            }
            return items;
        }

        private sealed class SystemRowBind
        {
            public string SystemName;
            public string DisplayName;
            public Image Icon;
            public List<UnitPick> Units = new List<UnitPick>();
        }

        private sealed class UnitPick
        {
            public RomLinkPlanItem Item;
            public string Drive;
            public bool Selected;
            public bool CanCreate;
            public Rectangle HitBounds;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _scanButton.Enabled = !busy;
            _browseButton.Enabled = !busy;
            if (_btnSelectAll != null) _btnSelectAll.Enabled = !busy;
            if (_btnDeselectAll != null) _btnDeselectAll.Enabled = !busy;
            if (busy)
            {
                _applyButton.Enabled = false;
                _cleanButton.Enabled = false;
            }
            else
            {
                _applyButton.Enabled = _currentCreatableItems.Count > 0;
                _cleanButton.Enabled = _masterDetected;
            }
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void AppendLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            _logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
        }

        private Image TryLoadSidebarImage()
        {
            List<string> candidates = new List<string>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            candidates.Add(Path.Combine(baseDir, "Resources", "SidebarBanner.png"));
            candidates.Add(Path.Combine(baseDir, "SidebarBanner.png"));
            candidates.Add(Path.Combine(baseDir, "..", "..", "Resources", "SidebarBanner.png"));
            candidates.Add(Path.Combine(baseDir, "..", "..", "..", "Resources", "SidebarBanner.png"));
            candidates.Add(Path.Combine(Environment.CurrentDirectory, "Resources", "SidebarBanner.png"));

            foreach (string path in candidates)
            {
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath)) return Image.FromFile(fullPath);
                }
                catch
                {
                }
            }

            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                foreach (string resourceName in assembly.GetManifestResourceNames())
                {
                    if (resourceName.EndsWith("SidebarBanner.png", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                        {
                            if (stream != null)
                            {
                                using (Image img = Image.FromStream(stream))
                                {
                                    return new Bitmap(img);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            Bitmap bmp = new Bitmap(340, 900);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(10, 15, 25));
                using (Brush b = new SolidBrush(Color.White))
                {
                    g.DrawString("SIDEBAR\nNÃO ENCONTRADA", new Font("Segoe UI", 20, FontStyle.Bold), b, new RectangleF(24, 40, 300, 200));
                }
            }
            return bmp;
        }

        private Icon TryLoadIcon()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "AppIcon.ico");
                if (File.Exists(path)) return new Icon(path);
            }
            catch
            {
            }

            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) return exeIcon;
            }
            catch
            {
            }

            return SystemIcons.Application;
        }

        private Image TryLoadMasterFolderIcon(bool ok)
        {
            List<string> candidates = new List<string>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            candidates.Add(Path.Combine(baseDir, "Resources", "FolderMasterIcon.png"));
            candidates.Add(Path.Combine(baseDir, "FolderMasterIcon.png"));
            candidates.Add(Path.Combine(baseDir, "..", "..", "Resources", "FolderMasterIcon.png"));
            candidates.Add(Path.Combine(baseDir, "..", "..", "..", "Resources", "FolderMasterIcon.png"));
            candidates.Add(Path.Combine(Environment.CurrentDirectory, "Resources", "FolderMasterIcon.png"));

            foreach (string path in candidates)
            {
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        using (Image raw = Image.FromFile(fullPath))
                        {
                            return ok ? new Bitmap(raw) : CreateDimmedFolderImage(raw);
                        }
                    }
                }
                catch
                {
                }
            }

            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                foreach (string resourceName in assembly.GetManifestResourceNames())
                {
                    if (resourceName.EndsWith("FolderMasterIcon.png", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                        {
                            if (stream != null)
                            {
                                using (Image img = Image.FromStream(stream))
                                {
                                    return ok ? new Bitmap(img) : CreateDimmedFolderImage(img);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return CreateFolderStatusIcon(ok);
        }

        private static Bitmap CreateDimmedFolderImage(Image source)
        {
            Bitmap bmp = new Bitmap(source.Width, source.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            using (ImageAttributes attrs = new ImageAttributes())
            {
                ColorMatrix matrix = new ColorMatrix(new float[][]
                {
                    new float[] { 0.55f, 0, 0, 0, 0 },
                    new float[] { 0, 0.55f, 0, 0, 0 },
                    new float[] { 0, 0, 0.55f, 0, 0 },
                    new float[] { 0, 0, 0, 0.75f, 0 },
                    new float[] { 0, 0, 0, 0, 1 }
                });
                attrs.SetColorMatrix(matrix);
                g.DrawImage(source, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attrs);
            }
            return bmp;
        }

        private static Bitmap CreateFolderStatusIcon(bool ok)
        {
            Bitmap bmp = new Bitmap(120, 84);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(4, 10, 24));
                Color folder = ok ? Color.FromArgb(55, 255, 113) : Color.FromArgb(255, 77, 136);
                using (Pen pen = new Pen(folder, 4f))
                {
                    g.DrawLines(pen, new[]
                    {
                        new Point(10, 52), new Point(10, 26), new Point(36, 26), new Point(46, 14), new Point(84, 14), new Point(84, 52), new Point(10, 52)
                    });
                }
                Color badge = ok ? Color.FromArgb(84, 255, 115) : Color.FromArgb(255, 77, 132);
                using (Brush brush = new SolidBrush(badge))
                using (Pen pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2f))
                {
                    g.FillEllipse(brush, 70, 34, 34, 34);
                    g.DrawEllipse(pen, 70, 34, 34, 34);
                }
                using (Pen pen = new Pen(Color.White, 3f))
                {
                    if (ok)
                    {
                        g.DrawLines(pen, new[] { new Point(79, 51), new Point(86, 58), new Point(95, 43) });
                    }
                    else
                    {
                        g.DrawLine(pen, 80, 44, 95, 58);
                        g.DrawLine(pen, 95, 44, 80, 58);
                    }
                }
            }
            return bmp;
        }
    }

    internal enum NeonStyle
    {
        Blue,
        Green,
        Red
    }

    internal sealed class NeonButton : Control
    {
        private readonly NeonStyle _style;
        private bool _hover;
        private bool _pressed;

        public NeonButton(string text, NeonStyle style)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 8.6F, FontStyle.Bold, GraphicsUnit.Point);
            ForeColor = Color.White;
            Text = text;
            _style = style;
            Size = new Size(118, 30);
            MouseEnter += delegate { _hover = true; Invalidate(); };
            MouseLeave += delegate { _hover = false; _pressed = false; Invalidate(); };
            MouseDown += delegate { _pressed = true; Invalidate(); };
            MouseUp += delegate { _pressed = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

            Color accent;
            Color accent2;
            switch (_style)
            {
                case NeonStyle.Green:
                    accent = Color.FromArgb(52, 255, 155);
                    accent2 = Color.FromArgb(45, 215, 255);
                    break;
                case NeonStyle.Red:
                    accent = Color.FromArgb(255, 62, 126);
                    accent2 = Color.FromArgb(255, 202, 66);
                    break;
                default:
                    accent = Color.FromArgb(58, 216, 255);
                    accent2 = Color.FromArgb(255, 55, 222);
                    break;
            }

            Color textColor = Enabled ? Color.FromArgb(245, 248, 255) : Color.FromArgb(160, 168, 182);
            Color fillTop = Enabled ? Color.FromArgb(27, 35, 58) : Color.FromArgb(31, 38, 50);
            Color fillBottom = Enabled ? Color.FromArgb(12, 18, 34) : Color.FromArgb(24, 29, 39);
            if (_hover && Enabled)
            {
                fillTop = Color.FromArgb(35, 46, 75);
                fillBottom = Color.FromArgb(18, 26, 48);
            }
            if (_pressed && Enabled)
            {
                fillTop = Color.FromArgb(12, 18, 34);
                fillBottom = Color.FromArgb(27, 35, 58);
            }

            Color glowColor = Enabled ? Color.FromArgb(_hover ? 95 : 45, accent) : Color.FromArgb(25, 120, 130, 145);

            using (GraphicsPath outer = RoundedRect(rect, 12))
            using (PathGradientBrush glowBrush = new PathGradientBrush(outer))
            {
                glowBrush.CenterColor = glowColor;
                glowBrush.SurroundColors = new[] { Color.FromArgb(0, accent) };
                e.Graphics.FillPath(glowBrush, outer);
            }

            Rectangle body = new Rectangle(3, 3, Width - 7, Height - 7);
            using (GraphicsPath path = RoundedRect(body, 10))
            using (LinearGradientBrush fillBrush = new LinearGradientBrush(body, fillTop, fillBottom, LinearGradientMode.Vertical))
            using (Pen border = new Pen(Enabled ? accent : Color.FromArgb(93, 103, 120), 1.6f))
            {
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(border, path);
            }

            Rectangle inner = new Rectangle(7, 7, Width - 15, Height - 15);
            using (GraphicsPath innerPath = RoundedRect(inner, 8))
            using (Pen innerPen = new Pen(Color.FromArgb(Enabled ? 80 : 35, Color.White), 1f))
            {
                e.Graphics.DrawPath(innerPen, innerPath);
            }

            using (Pen line = new Pen(Enabled ? accent2 : Color.FromArgb(80, 110, 120, 135), 2f))
            {
                e.Graphics.DrawLine(line, 18, Height - 6, Width - 18, Height - 6);
            }

            DrawSymbol(e.Graphics, Enabled ? accent : Color.FromArgb(150, 160, 170));
            Rectangle textRect = new Rectangle(42, 0, Width - 50, Height - 3);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private void DrawSymbol(Graphics g, Color c)
        {
            Rectangle iconRect = new Rectangle(14, 9, 20, 20);
            using (Pen p = new Pen(c, 2.3f))
            {
                p.LineJoin = LineJoin.Round;
                switch (_style)
                {
                    case NeonStyle.Blue:
                        g.DrawEllipse(p, iconRect.X + 1, iconRect.Y + 1, 11, 11);
                        g.DrawLine(p, iconRect.X + 12, iconRect.Y + 12, iconRect.Right - 1, iconRect.Bottom - 1);
                        break;
                    case NeonStyle.Green:
                        g.DrawArc(p, iconRect.X + 1, iconRect.Y + 4, 10, 10, 45, 270);
                        g.DrawArc(p, iconRect.X + 9, iconRect.Y + 4, 10, 10, 225, 270);
                        g.DrawLine(p, iconRect.X + 7, iconRect.Y + 17, iconRect.X + 12, iconRect.Y + 13);
                        g.DrawLine(p, iconRect.X + 12, iconRect.Y + 3, iconRect.X + 17, iconRect.Y + 7);
                        break;
                    case NeonStyle.Red:
                        g.DrawRectangle(p, iconRect.X + 5, iconRect.Y + 6, 10, 12);
                        g.DrawLine(p, iconRect.X + 3, iconRect.Y + 6, iconRect.X + 17, iconRect.Y + 6);
                        g.DrawLine(p, iconRect.X + 7, iconRect.Y + 3, iconRect.X + 13, iconRect.Y + 3);
                        break;
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class NeonCard : Panel
    {
        public NeonCard()
        {
            BackColor = Color.FromArgb(4, 10, 24);
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedRect(rect, 16))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(4, 10, 24)))
            using (Pen pen = new Pen(Color.FromArgb(17, 73, 148), 2f))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal static class SystemIconFactory
    {
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> FamilyToFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "playstation", "playstation.png" },
            { "nintendo", "nintendo.png" },
            { "dreamcast", "dreamcast.png" },
            { "n64", "n64.png" },
            { "arcade", "arcade.png" },
            { "pcengine", "pcengine.png" },
            { "doom", "doom.png" },
            { "xbox", "xbox.png" },
            { "sega", "sega.png" },
            { "atari", "atari.png" },
            { "neogeo", "neogeo.png" },
            { "dos", "dos.png" },
            { "generic", "generic.png" }
        };

        public static Bitmap GetSystemIcon(string systemName)
        {
            string key = systemName ?? string.Empty;
            if (Cache.ContainsKey(key)) return Cache[key];

            // Garante ~64px para ficar bem visível na grade (sem esticar blur na célula)
            const int displaySize = 64;
            Bitmap exact = LoadExactSystemEmbedded(systemName);
            if (exact != null)
            {
                Bitmap display = EnsureDisplaySize(exact, displaySize);
                if (display != exact) exact.Dispose();
                Cache[key] = display;
                return display;
            }

            string family = GetFamily(systemName);
            Bitmap bmp = LoadFamilyEmbedded(family) ?? GenerateFallback(systemName, family);
            Bitmap outBmp = EnsureDisplaySize(bmp, displaySize);
            if (outBmp != bmp) bmp.Dispose();
            Cache[key] = outBmp;
            return outBmp;
        }

        /// <summary>Se o PNG for pequeno, amplia com qualidade; se já for grande, mantém.</summary>
        private static Bitmap EnsureDisplaySize(Bitmap source, int size)
        {
            if (source == null) return null;
            if (source.Width >= size && source.Height >= size)
                return source;

            Bitmap dest = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(dest))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
                int w = Math.Max(1, (int)(source.Width * scale));
                int h = Math.Max(1, (int)(source.Height * scale));
                int x = (size - w) / 2;
                int y = (size - h) / 2;
                g.DrawImage(source, x, y, w, h);
            }
            return dest;
        }

        private static Bitmap LoadExactSystemEmbedded(string systemName)
        {
            string safe = SafeFileName(systemName);
            if (string.IsNullOrWhiteSpace(safe)) return null;
            return LoadEmbeddedPng(".Resources.SystemIcons.Systems." + safe + ".png");
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            StringBuilder sb = new StringBuilder();
            foreach (char c in value.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.') sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString().Trim('_');
        }

        private static Bitmap LoadFamilyEmbedded(string family)
        {
            string fileName;
            if (!FamilyToFile.TryGetValue(family, out fileName)) return null;
            return LoadEmbeddedPng(".Resources.SystemIcons." + fileName);
        }

        private static Bitmap LoadEmbeddedPng(string resourceSuffix)
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                foreach (string resourceName in assembly.GetManifestResourceNames())
                {
                    if (resourceName.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                        {
                            if (stream == null) return null;
                            using (Image img = Image.FromStream(stream))
                            {
                                return new Bitmap(img);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static string GetFamily(string system)
        {
            string s = (system ?? string.Empty).ToLowerInvariant();
            if (s.Contains("psx") || s.Contains("ps1") || s.Contains("ps2") || s.Contains("ps3") || s.Contains("psp") || s.Contains("playstation")) return "playstation";
            if (s.Contains("dreamcast")) return "dreamcast";
            if (s.Contains("n64") || s.Contains("nintendo64")) return "n64";
            if (s.Contains("arcade") || s.Contains("mame") || s.Contains("fbneo") || s.Contains("fba") || s.Contains("cps") || s.Contains("cave") || s.Contains("naomi") || s.Contains("model") || s.Contains("daphne") || s.Contains("atomiswave")) return "arcade";
            if (s.Contains("pcengine") || s.Contains("tg16") || s.Contains("turbografx") || s.Contains("supergrafx") || s.Contains("pcfx")) return "pcengine";
            if (s.Contains("doom") || s.Contains("prboom") || s.Contains("gzdoom")) return "doom";
            if (s.Contains("xbox")) return "xbox";
            if (s.Contains("megadrive") || s.Contains("genesis") || s.Contains("sega") || s.Contains("saturn") || s.Contains("gamegear") || s.Contains("mastersystem")) return "sega";
            if (s.Contains("atari") || s.Contains("lynx") || s.Contains("jaguar")) return "atari";
            if (s.Contains("neogeo")) return "neogeo";
            if (s.Contains("dos") || s.Contains("windows") || s.Contains("pc") || s.Contains("scummvm")) return "dos";
            if (s.Contains("nes") || s.Contains("snes") || s.Contains("gba") || s.Contains("gbc") || s.Contains("gameboy") || s.Contains("nintendo") || s.Contains("nds") || s.Contains("3ds") || s.Contains("wii") || s.Contains("switch")) return "nintendo";
            return "generic";
        }

        private static Bitmap GenerateFallback(string systemName, string family)
        {
            Bitmap bmp = new Bitmap(64, 64);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(4, 10, 24));
                using (Brush brush = new SolidBrush(Color.FromArgb(10, 20, 42))) g.FillEllipse(brush, 4, 4, 56, 56);
                using (Pen pen = new Pen(Color.FromArgb(39, 211, 255), 2f)) g.DrawEllipse(pen, 4, 4, 56, 56);
                using (Pen pen = new Pen(Color.White, 3f))
                {
                    g.DrawRectangle(pen, 16, 25, 32, 20);
                    g.DrawLine(pen, 26, 35, 26, 43);
                    g.DrawLine(pen, 22, 39, 30, 39);
                    g.DrawEllipse(pen, 36, 33, 4, 4);
                    g.DrawEllipse(pen, 43, 39, 4, 4);
                }
            }
            return bmp;
        }
    }
}
