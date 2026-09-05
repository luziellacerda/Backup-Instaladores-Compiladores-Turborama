using System.Drawing;
using System.Windows.Forms;
using TurboRama.Next;
namespace InstallerHost
{
    public partial class InstallControl
    {
        private TextBox txtFolder;
        private Button btnBrowse, btnInstall, btnCancel, btnBack;
        private ProgressBar progressBar;
        private Label txtInfo, lblFolderHint, wizardHeader, lblSelectFolder;
        private void InitializeComponent()
        {
            FlowLayoutPanel actions;
            Panel body = ConsumerLayout.Build(this, 3, out wizardHeader, out actions);
            FlowLayoutPanel stack = Ui.Stack(); Ui.FillStackWidth(stack); body.Controls.Add(stack);
            TableLayoutPanel card = Ui.Vertical(); card.Padding = new Padding(24); card.BackColor = Palette.Surface;
            txtInfo = ConsumerLayout.Label("", 12); txtInfo.Name = "txtInfo"; Ui.AddRow(card, txtInfo);
            lblSelectFolder = ConsumerLayout.Label("", 10, true); lblSelectFolder.Margin = new Padding(0, 20, 0, 10); Ui.AddRow(card, lblSelectFolder);
            TableLayoutPanel folder = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
            folder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); folder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            txtFolder = new TextBox { Name = "txtFolder", Dock = DockStyle.Fill, Font = Ui.Font(12), BorderStyle = BorderStyle.FixedSingle,
                BackColor = Palette.Raised, ForeColor = Palette.Text, Margin = new Padding(0, 12, 14, 0), AccessibleName = "Pasta de instalação" };
            btnBrowse = ConsumerLayout.Action("btnBrowse", "Procurar"); btnBrowse.Click += BtnBrowse_Click;
            folder.Controls.Add(txtFolder, 0, 0); folder.Controls.Add(btnBrowse, 1, 0); Ui.AddRow(card, folder);
            lblFolderHint = ConsumerLayout.Label("", 10); lblFolderHint.ForeColor = Palette.Muted; Ui.AddRow(card, lblFolderHint);
            progressBar = new ProgressBar { Name = "progressBar", Dock = DockStyle.Top, Height = 16,
                Margin = new Padding(0, 16, 0, 12), Minimum = 0, Maximum = 100, Visible = false };
            Ui.AddRow(card, progressBar); stack.Controls.Add(card);
            btnCancel = ConsumerLayout.Action("btnCancel", "Cancelar"); btnCancel.Click += BtnCancel_Click;
            btnBack = ConsumerLayout.Action("btnBack", "Voltar"); btnBack.Click += BtnBack_Click;
            btnInstall = ConsumerLayout.Action("btnInstall", "Instalar", true); btnInstall.Click += BtnInstall_Click;
            actions.Controls.AddRange(new Control[] { btnCancel, btnBack, btnInstall }); ConsumerLayout.BindDefault(this, btnInstall);
        }
    }
}
