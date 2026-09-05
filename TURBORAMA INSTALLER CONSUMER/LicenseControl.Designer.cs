using System.Drawing;
using System.Windows.Forms;
using TurboRama.Next;
namespace InstallerHost
{
    public partial class LicenseControl
    {
        private TextBox licenseTextBox;
        private CheckBox chkAgree;
        private Button btnCancel, btnNext, btnBack;
        private Label wizardHeader;
        private void InitializeComponent()
        {
            FlowLayoutPanel actions;
            Panel body = ConsumerLayout.Build(this, 1, out wizardHeader, out actions);
            TableLayoutPanel content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Panel card = new Panel { Name = "LicenseCard", Dock = DockStyle.Fill, BackColor = Palette.Surface,
                Padding = new Padding(20), Margin = Padding.Empty };
            licenseTextBox = new TextBox { Name = "licenseTextBox", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Top, BackColor = Palette.Surface, ForeColor = Palette.Text, Font = Ui.Font(11),
                BorderStyle = BorderStyle.None, Margin = Padding.Empty, TabStop = true };
            card.Controls.Add(licenseTextBox);
            // Show complete lines at the bottom while keeping native selectable
            // text, keyboard navigation and scrolling. Do not rewrite the license.
            System.EventHandler fitReadingArea = delegate
            {
                int available = System.Math.Max(0, card.ClientSize.Height - card.Padding.Vertical);
                int lineHeight = licenseTextBox.Font.Height;
                licenseTextBox.Height = available - available % lineHeight;
            };
            card.SizeChanged += fitReadingArea; licenseTextBox.FontChanged += fitReadingArea;
            chkAgree = new CheckBox { Name = "chkAgree", Text = "Li e aceito os termos da licença.", AutoSize = true,
                ForeColor = Palette.Text, Dock = DockStyle.Top, Margin = new Padding(0, 16, 0, 0) };
            chkAgree.CheckedChanged += chkAgree_CheckedChanged;
            content.Controls.Add(card, 0, 0); content.Controls.Add(chkAgree, 0, 1); body.Controls.Add(content);
            btnCancel = ConsumerLayout.Action("btnCancel", "Cancelar"); btnCancel.Click += BtnCancel_Click;
            btnBack = ConsumerLayout.Action("btnBack", "Voltar"); btnBack.Click += btnBack_Click;
            btnNext = ConsumerLayout.Action("btnNext", "Avançar", true); btnNext.Enabled = false; btnNext.Click += btnNext_Click;
            actions.Controls.AddRange(new Control[] { btnCancel, btnBack, btnNext }); ConsumerLayout.BindDefault(this, btnNext);
        }
    }
}
