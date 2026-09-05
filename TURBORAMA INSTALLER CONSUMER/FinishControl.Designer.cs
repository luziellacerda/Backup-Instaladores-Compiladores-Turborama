using System.Drawing;
using System.Windows.Forms;
using TurboRama.Next;
namespace InstallerHost
{
    public partial class FinishControl
    {
        private Button btnFinish;
        private Label lblMessage, lblWelcomeDesc, lblInstallPath;
        private void InitializeComponent()
        {
            FlowLayoutPanel actions;
            Panel body = ConsumerLayout.Build(this, 4, out lblMessage, out actions);
            FlowLayoutPanel stack = Ui.Stack(); Ui.FillStackWidth(stack); body.Controls.Add(stack);
            TableLayoutPanel card = Ui.Vertical(); card.Padding = new Padding(28); card.BackColor = Palette.Surface;
            Label complete = ConsumerLayout.Label("Tudo pronto para começar.", 22, true); complete.ForeColor = Palette.Accent; Ui.AddRow(card, complete);
            lblWelcomeDesc = ConsumerLayout.Label("", 12); Ui.AddRow(card, lblWelcomeDesc);
            Ui.AddRow(card, ConsumerLayout.Label("PASTA INSTALADA", 9, true));
            lblInstallPath = ConsumerLayout.Label("", 11); lblInstallPath.Name = "InstalledPath"; Ui.AddRow(card, lblInstallPath);
            stack.Controls.Add(card);
            btnFinish = ConsumerLayout.Action("btnFinish", "Concluir", true); btnFinish.Click += BtnFinish_Click;
            actions.Controls.Add(btnFinish); ConsumerLayout.BindDefault(this, btnFinish);
        }
    }
}
