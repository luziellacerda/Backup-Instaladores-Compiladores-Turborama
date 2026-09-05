using System.Drawing;
using System.Windows.Forms;
using TurboRama.Next;
namespace InstallerHost
{
    public partial class FinishControl
    {
        private Button btnFinish;
        private Label lblMessage, lblWelcomeDesc, lblInstallPath, lblComplete, lblPathTitle;
        private void InitializeComponent()
        {
            FlowLayoutPanel actions;
            Panel body = ConsumerLayout.Build(this, 4, out lblMessage, out actions);
            FlowLayoutPanel stack = Ui.Stack(); Ui.FillStackWidth(stack); body.Controls.Add(stack);
            TableLayoutPanel card = Ui.Vertical(); card.Padding = new Padding(28); card.BackColor = Palette.Surface;
            lblComplete = ConsumerLayout.Label("Tudo pronto para começar.", 22, true); lblComplete.Name = "CompletionTitle";
            lblComplete.ForeColor = Palette.Accent; Ui.AddRow(card, lblComplete);
            lblWelcomeDesc = ConsumerLayout.Label("", 12); lblWelcomeDesc.Name = "CompletionDescription"; Ui.AddRow(card, lblWelcomeDesc);
            lblPathTitle = ConsumerLayout.Label("PASTA INSTALADA", 9, true); lblPathTitle.Name = "InstalledPathTitle"; Ui.AddRow(card, lblPathTitle);
            lblInstallPath = ConsumerLayout.Label("", 11); lblInstallPath.Name = "InstalledPath"; Ui.AddRow(card, lblInstallPath);
            stack.Controls.Add(card);
            btnFinish = ConsumerLayout.Action("btnFinish", "Concluir", true); btnFinish.Click += BtnFinish_Click;
            actions.Controls.Add(btnFinish); ConsumerLayout.BindDefault(this, btnFinish);
        }
    }
}
