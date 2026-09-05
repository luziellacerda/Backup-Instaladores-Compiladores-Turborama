using System;
using System.Drawing;
using System.Windows.Forms;
using TurboRama.Next;

namespace InstallerHost
{
    public partial class PrerequisiteControl : UserControl
    {
        private void InitializeComponent()
        {
            SuspendLayout();
            Name = "PrerequisiteControl";
            AccessibleName = "Pré-requisitos";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Ui.Font(10F);
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            Size = new Size(1180, 720);

            chkVCpp = CreateOption("chkVCpp", "Stack Microsoft recomendado", true, 0);
            chkDirectX = CreateOption("chkDirectX", "DirectX legado June 2010", true, 1);
            chkNvidiaApp = CreateOption("chkNvidiaApp", "Copiar link oficial do driver NVIDIA", false, 2);
            chkDokany = CreateOption("chkDokany", "Instalar Dokany — driver opcional", false, 3);
            chkwinFSP = CreateOption("chkwinFSP", "WinFsp 2026 Beta4 — opcional (teste)", false, 4);

            progressBar = new ProgressBar
            {
                Name = "progressBar", Height = 16, Dock = DockStyle.Top,
                Margin = new Padding(0, 14, 0, 12), Style = ProgressBarStyle.Continuous,
                TabStop = false
            };
            progressTitleLabel = ConsumerLayout.Label(progressTitleText, 12F, true);
            progressTitleLabel.Name = "progressTitleLabel";
            statusLabel = ConsumerLayout.Label(progressDetailText);
            statusLabel.Name = "statusLabel";
            statusLabel.ForeColor = Palette.Muted;
            progressCountLabel = ConsumerLayout.Label("Processadas: 0");
            progressCountLabel.Name = "progressCountLabel";
            progressPercentLabel = ConsumerLayout.Label("0%", 11F, true);
            progressPercentLabel.Name = "progressPercentLabel";
            progressPercentLabel.ForeColor = Palette.Accent;
            progressHintLabel = ConsumerLayout.Label("Hash, tamanho, editor e revogação são verificados antes da execução.");
            progressHintLabel.Name = "progressHintLabel";
            progressHintLabel.ForeColor = Palette.Muted;
            readinessLabel = ConsumerLayout.Label("Analisando hardware e runtimes…", 11F, true);
            readinessLabel.Name = "readinessLabel";
            readinessLabel.ForeColor = Palette.Violet;
            diskSpaceLabel = ConsumerLayout.Label("O diagnóstico também informará o espaço livre no disco do Windows.");
            diskSpaceLabel.Name = "diskSpaceLabel";
            diskSpaceLabel.ForeColor = Palette.Muted;

            readinessButton = ConsumerLayout.Action("btnGamingReadiness", "Ver diagnóstico");
            readinessButton.Width = 196;
            readinessButton.Enabled = false;
            readinessButton.Margin = Padding.Empty;
            readinessButton.Click += ShowGamingReadinessDialog;
            btnCancel = ConsumerLayout.Action("btnCancel", "Cancelar");
            btnBack = ConsumerLayout.Action("btnBack", "Voltar");
            btnNext = ConsumerLayout.Action("btnNext", "Avançar", true);
            btnCancel.Click += BtnCancel_Click;
            btnBack.Click += BtnBack_Click;
            btnNext.Click += BtnNext_Click;
            btnCancel.TabIndex = 20;
            btnBack.TabIndex = 21;
            btnNext.TabIndex = 22;

            FlowLayoutPanel actions;
            Panel body = ConsumerLayout.Build(this, 2, out wizardHeader, out actions);
            wizardHeader.Name = "wizardHeader";
            actions.Controls.Add(btnCancel);
            actions.Controls.Add(btnBack);
            actions.Controls.Add(btnNext);

            contentStack = Ui.Stack();
            contentStack.Name = "prerequisiteContent";
            contentStack.Padding = new Padding(0, 0, 4, 0);
            body.Controls.Add(contentStack);

            Label introduction = ConsumerLayout.Label(
                "Escolha os componentes que deseja preparar. Avançar processa os grupos selecionados antes de abrir a instalação do produto.");
            introduction.Name = "prerequisiteIntroduction";
            introduction.ForeColor = Palette.Muted;
            contentStack.Controls.Add(introduction);

            TableLayoutPanel diagnostics = CreateSection("prerequisiteDiagnostics");
            TableLayoutPanel diagnosticHeading = CreateSplitRow();
            Label diagnosticTitle = ConsumerLayout.Label("Diagnóstico do computador", 12F, true);
            diagnosticTitle.Dock = DockStyle.Fill;
            diagnosticTitle.TextAlign = ContentAlignment.MiddleLeft;
            diagnosticHeading.Controls.Add(diagnosticTitle, 0, 0);
            readinessButton.Anchor = AnchorStyles.Right;
            diagnosticHeading.Controls.Add(readinessButton, 1, 0);
            Ui.AddRow(diagnostics, diagnosticHeading);
            Ui.AddRow(diagnostics, readinessLabel);
            Ui.AddRow(diagnostics, diskSpaceLabel);
            contentStack.Controls.Add(diagnostics);

            TableLayoutPanel selection = CreateSection("prerequisiteSelection");
            Label selectionHeading = ConsumerLayout.Label("Componentes disponíveis", 12F, true);
            Ui.AddRow(selection, selectionHeading);
            AddOptionRow(selection, chkVCpp,
                ".NET Desktop 10/8, Visual C++ v14 atualizado, bibliotecas VC++ 2005–2013 e WebView2. Versões atuais já detectadas são preservadas.");
            AddOptionRow(selection, chkDirectX,
                "Bibliotecas usadas por jogos antigos. Não substitui DirectX 11/12 nem os recursos fornecidos pelo driver de vídeo.");
            AddOptionRow(selection, chkNvidiaApp,
                "Copia a fonte oficial da NVIDIA. O navegador não será aberto pelo instalador.");
            AddOptionRow(selection, chkDokany,
                "Dokany 2.3.1: instala um driver de sistema de arquivos. Marque somente se um aplicativo exigir; pode precisar reiniciar o Windows.");
            AddOptionRow(selection, chkwinFSP,
                "WinFsp 2.2 Beta 4: pré-lançamento oficial com correções de segurança; instala somente o núcleo do driver. Marque apenas se necessário. Pode exigir reinício.");
            contentStack.Controls.Add(selection);

            progressSection = CreateSection("prerequisiteProgress");
            TableLayoutPanel progressHeading = CreateSplitRow();
            progressTitleLabel.Dock = DockStyle.Fill;
            progressTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            progressHeading.Controls.Add(progressTitleLabel, 0, 0);
            progressPercentLabel.Anchor = AnchorStyles.Right;
            progressHeading.Controls.Add(progressPercentLabel, 1, 0);
            Ui.AddRow(progressSection, progressHeading);
            Ui.AddRow(progressSection, statusLabel);
            Ui.AddRow(progressSection, progressBar);
            Ui.AddRow(progressSection, progressCountLabel);
            Ui.AddRow(progressSection, progressHintLabel);
            contentStack.Controls.Add(progressSection);

            Label scope = ConsumerLayout.Label("Esta etapa prepara dependências. Não inclui jogos, ROMs ou BIOS e não garante compatibilidade com todo emulador.");
            scope.Name = "prerequisiteScope";
            scope.ForeColor = Palette.Muted;
            contentStack.Controls.Add(scope);
            Ui.FillStackWidth(contentStack);

            chkVCpp.CheckedChanged += delegate { UpdateProgressMaximumFromSelection(); };
            chkDirectX.CheckedChanged += delegate { UpdateProgressMaximumFromSelection(); };
            chkNvidiaApp.CheckedChanged += delegate { UpdateProgressMaximumFromSelection(); };
            chkDokany.CheckedChanged += delegate { UpdateProgressMaximumFromSelection(); };
            chkwinFSP.CheckedChanged += delegate { UpdateProgressMaximumFromSelection(); };
            ResumeLayout(true);
        }

        private static CheckBox CreateOption(string name, string text, bool selected, int tabIndex)
        {
            return new CheckBox
            {
                Name = name, Text = text, AccessibleName = text, Checked = selected,
                AutoSize = true, Dock = DockStyle.Top, ThreeState = false,
                ForeColor = Palette.Text, BackColor = Color.Transparent,
                Font = Ui.Font(10.5F, true), MinimumSize = new Size(0, 28),
                Margin = new Padding(0, 0, 0, 5), TabIndex = tabIndex,
                UseVisualStyleBackColor = false
            };
        }

        private static TableLayoutPanel CreateSection(string name)
        {
            TableLayoutPanel section = Ui.Vertical();
            section.Name = name;
            section.BackColor = Palette.Surface;
            section.Padding = new Padding(18);
            section.Margin = new Padding(0, 0, 0, 14);
            return section;
        }

        private static TableLayoutPanel CreateSplitRow()
        {
            TableLayoutPanel row = new TableLayoutPanel
            {
                ColumnCount = 2, RowCount = 1, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 10), Padding = Padding.Empty
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return row;
        }

        private static void AddOptionRow(TableLayoutPanel section, CheckBox option, string detail)
        {
            TableLayoutPanel row = Ui.Vertical();
            row.Name = option.Name + "Row";
            row.Padding = new Padding(0, 10, 0, 12);
            row.Margin = Padding.Empty;
            Ui.AddRow(row, option);
            Label description = ConsumerLayout.Label(detail);
            description.Name = option.Name + "Description";
            description.ForeColor = Palette.Muted;
            description.Margin = new Padding(25, 0, 0, 0);
            Ui.AddRow(row, description);
            Ui.AddRow(section, row);
        }

        private Label wizardHeader;
        private Label statusLabel;
        private CheckBox chkVCpp;
        private CheckBox chkDirectX;
        private CheckBox chkDokany;
        private CheckBox chkwinFSP;
        private Button btnCancel;
        private Button btnNext;
        private Button btnBack;
        private ProgressBar progressBar;
        private FlowLayoutPanel contentStack;
        private TableLayoutPanel progressSection;
    }
}
