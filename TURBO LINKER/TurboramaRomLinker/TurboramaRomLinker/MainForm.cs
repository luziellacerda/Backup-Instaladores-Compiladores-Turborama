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
        private NeonButton _applyButton;
        private NeonButton _cleanButton;

        private Panel _masterCard;
        private PictureBox _masterIcon;
        private Label _masterTitleLabel;
        private Label _masterInfoLabel;

        private Panel _gridCard;
        private Label _gridTitleLabel;
        private Label _gridHintLabel;
        private DataGridView _grid;

        private Panel _logCard;
        private Label _logTitleLabel;
        private LinkLabel _clearLogLink;
        private TextBox _logBox;

        private Panel _footerPanel;
        private Label _footerLeftLabel;
        private Label _footerRightLabel;

        private readonly List<RomLinkPlanItem> _currentCreatableItems = new List<RomLinkPlanItem>();
        private bool _busy;
        private bool _masterDetected;

        public MainForm()
        {
            Text = "LZ Games - Turborama ROM Linker";
            ShowIcon = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            WindowState = FormWindowState.Normal;
            MinimumSize = new Size(1280, 720);
            MaximumSize = new Size(1280, 720);
            Size = new Size(1280, 720);
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
            WindowState = FormWindowState.Normal;
            CenterToScreen();
            await Task.Delay(120);
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
            _gridHintLabel.Text = "Só aparece aqui o que realmente vai criar link";
            _gridHintLabel.AutoSize = true;
            _gridHintLabel.Font = new Font("Segoe UI", 9.2F, FontStyle.Regular, GraphicsUnit.Point);
            _gridHintLabel.ForeColor = Color.FromArgb(142, 154, 184);
            _gridCard.Controls.Add(_gridHintLabel);

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
            _grid.RowTemplate.Height = 44;
            _grid.ScrollBars = ScrollBars.Vertical;
            _grid.DefaultCellStyle.BackColor = Color.FromArgb(7, 15, 35);
            _grid.DefaultCellStyle.ForeColor = Color.White;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(27, 35, 77);
            _grid.DefaultCellStyle.SelectionForeColor = Color.White;
            _grid.DefaultCellStyle.Font = new Font("Segoe UI", 9.6F, FontStyle.Regular, GraphicsUnit.Point);
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(12, 22, 54);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(231, 234, 241);
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.6F, FontStyle.Bold, GraphicsUnit.Point);
            _grid.ColumnHeadersHeight = 40;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.RowTemplate.DividerHeight = 1;
            _grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Use", HeaderText = "Usar", Width = 56, TrueValue = true, FalseValue = false });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Ação", Width = 135 });
            _grid.Columns.Add(new DataGridViewImageColumn { Name = "Icon", HeaderText = "Ícone", Width = 72, ImageLayout = DataGridViewImageCellLayout.Zoom });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "System", HeaderText = "Sistema", Width = 140 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Origem (TurboRoms)", Width = 320 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Destination", HeaderText = "Destino (sistema\\roms)", Width = 270 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 150 });
            _grid.Columns[2].DefaultCellStyle.NullValue = null;
            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty)
                {
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            _grid.CellFormatting += GridCellFormatting;
            _gridCard.Controls.Add(_grid);

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

            int buttonWidth = 118;
            int buttonHeight = 30;
            int buttonGap = 12;
            int buttonsTotal = buttonWidth * 3 + buttonGap * 2;
            int buttonsLeft = areaLeft + areaWidth - buttonsTotal - 4;

            // Layout compacto: sobe título/botões e libera mais altura para a lista de ROMs.
            int topY = 24;

            int titleWidth = Math.Max(330, buttonsLeft - areaLeft - 24);
            _titleLabel.SetBounds(areaLeft + 8, topY + 4, titleWidth, 30);
            _subtitleLabel.Visible = false;

            _scanButton.SetBounds(buttonsLeft, topY, buttonWidth, buttonHeight);
            _applyButton.SetBounds(buttonsLeft + buttonWidth + buttonGap, topY, buttonWidth, buttonHeight);
            _cleanButton.SetBounds(buttonsLeft + 2 * (buttonWidth + buttonGap), topY, buttonWidth, buttonHeight);

            int masterY = topY + 50;
            int masterH = 76;
            _masterCard.SetBounds(areaLeft, masterY, areaWidth, masterH);
            _masterIcon.SetBounds(18, 12, 72, 52);
            _masterTitleLabel.Left = 108;
            _masterTitleLabel.Top = 14;
            _masterInfoLabel.SetBounds(108, 42, _masterCard.Width - 132, 28);

            int footerH = 24;
            int bottomMargin = 10;
            int footerY = ClientSize.Height - footerH - bottomMargin;
            _footerPanel.SetBounds(areaLeft, footerY, areaWidth, footerH);
            _footerLeftLabel.Left = 16;
            _footerLeftLabel.Top = 3;
            _footerRightLabel.Left = _footerPanel.Width - _footerRightLabel.PreferredWidth - 20;
            _footerRightLabel.Top = 3;

            // Log menor para aumentar o container "Pastas com ROMs válidas".
            int logH = 70;
            int logY = footerY - logH - 8;
            _logCard.SetBounds(areaLeft, logY, areaWidth, logH);
            _logTitleLabel.Left = 26;
            _logTitleLabel.Top = 8;
            _clearLogLink.Left = _logCard.Width - 110;
            _clearLogLink.Top = 10;
            _logBox.SetBounds(16, 30, _logCard.Width - 32, _logCard.Height - 36);

            int gridY = masterY + masterH + 10;
            int gridH = Math.Max(260, logY - gridY - 12);
            _gridCard.SetBounds(areaLeft, gridY, areaWidth, gridH);
            _gridTitleLabel.Left = 26;
            _gridTitleLabel.Top = 10;
            _gridHintLabel.Left = _gridCard.Width - _gridHintLabel.PreferredWidth - 26;
            _gridHintLabel.Top = 14;
            _grid.SetBounds(10, 40, _gridCard.Width - 20, _gridCard.Height - 50);
        }

        private Panel BuildCard()
        {
            return new NeonCard();
        }

        private async Task ScanAsync()
        {
            if (_busy) return;
            try
            {
                SetBusy(true);
                AppendLog("Iniciando análise de unidades...");
                DriveScanResult result = await Task.Run(delegate { return _service.BuildPlan(); });
                RenderScanResult(result);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Erro ao analisar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                AppendLog("Erro: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task ApplySelectedAsync()
        {
            if (_busy) return;
            List<RomLinkPlanItem> selected = GetSelectedItems();
            if (selected.Count == 0)
            {
                MessageBox.Show("Selecione ao menos uma pasta na coluna Usar.", "Nenhum item selecionado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show("Criar os links selecionados em sistema\\roms?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                SetBusy(true);
                int created = 0;
                int errors = 0;
                foreach (RomLinkPlanItem item in selected)
                {
                    JunctionService.CreateJunction(item);
                    if (item.Success) created++; else errors++;
                }
                AppendLog("Criação concluída. Criados: " + created + ". Erros: " + errors + ".");
                await ScanAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Erro ao criar links", MessageBoxButtons.OK, MessageBoxIcon.Error);
                AppendLog("Erro ao criar links: " + ex.Message);
            }
            finally
            {
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

                if (MessageBox.Show("Remover apenas os links/junctions dentro de sistema\\roms? Pastas reais serão preservadas.", "Confirmar limpeza", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }

                SetBusy(true);
                string romsRoot = Path.Combine(masterRoot, "sistema", "roms");
                int removed = 0;
                int preserved = 0;
                if (Directory.Exists(romsRoot))
                {
                    foreach (string dir in Directory.GetDirectories(romsRoot))
                    {
                        if (JunctionService.IsReparsePoint(dir))
                        {
                            Directory.Delete(dir);
                            removed++;
                        }
                        else
                        {
                            preserved++;
                        }
                    }
                }
                AppendLog("Limpeza concluída. Links removidos: " + removed + ". Pastas reais preservadas: " + preserved + ".");
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
            _currentCreatableItems.Clear();
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
            _footerRightLabel.Left = _footerPanel.Width - _footerRightLabel.PreferredWidth - 20;

            foreach (string message in result.Messages)
            {
                AppendLog(message);
            }

            if (!masterOk)
            {
                _applyButton.Enabled = false;
                _cleanButton.Enabled = false;
                return;
            }

            foreach (RomLinkPlanItem item in result.Items.Where(i => i.CanCreate).OrderBy(i => i.SystemName))
            {
                _currentCreatableItems.Add(item);
                int rowIndex = _grid.Rows.Add(true, "+ Criar link", SystemIconFactory.GetSystemIcon(item.SystemName), item.SystemName, item.SourcePath, MakeDestinationRelative(item.LinkPath, result.MasterRoot), "+ Criar link");
                DataGridViewRow row = _grid.Rows[rowIndex];
                row.Tag = item;
                row.DefaultCellStyle.BackColor = rowIndex % 2 == 0 ? Color.FromArgb(7, 14, 33) : Color.FromArgb(12, 19, 43);
            }

            AppendLog("Pastas com jogos válidos prontas para adicionar como link: " + _currentCreatableItems.Count);
            _applyButton.Enabled = _currentCreatableItems.Count > 0;
            _cleanButton.Enabled = _masterDetected;
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
                bool selected = row.Cells[0].Value is bool && (bool)row.Cells[0].Value;
                if (!selected) continue;
                RomLinkPlanItem item = row.Tag as RomLinkPlanItem;
                if (item != null) items.Add(item);
            }
            return items;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _scanButton.Enabled = !busy;
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

        private void GridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == _grid.Columns[6].Index)
            {
                e.CellStyle.ForeColor = Color.FromArgb(39, 211, 255);
                e.CellStyle.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
            }
            else if (e.ColumnIndex == _grid.Columns[1].Index)
            {
                e.CellStyle.ForeColor = Color.FromArgb(39, 211, 255);
            }
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

            Bitmap exact = LoadExactSystemEmbedded(systemName);
            if (exact != null)
            {
                Cache[key] = exact;
                return exact;
            }

            string family = GetFamily(systemName);
            Bitmap bmp = LoadFamilyEmbedded(family) ?? GenerateFallback(systemName, family);
            Cache[key] = bmp;
            return bmp;
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
