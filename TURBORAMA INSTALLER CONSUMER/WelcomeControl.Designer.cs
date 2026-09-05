using System.Drawing;
using System.Windows.Forms;
using TurboRama.Next;
namespace InstallerHost
{
    public partial class WelcomeControl
    {
        private Label lblWelcomeTitle, lblWelcomeDesc;
        private Button btnCancel, btnNext;
        private void InitializeComponent()
        {
            FlowLayoutPanel actions;
            Panel body = ConsumerLayout.Build(this, 0, out lblWelcomeTitle, out actions);
            FlowLayoutPanel stack = Ui.Stack(); Ui.FillStackWidth(stack); body.Controls.Add(stack);
            TableLayoutPanel hero = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top,
                BackColor = Palette.Surface, Padding = new Padding(26), Margin = Padding.Empty };
            hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63)); hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
            TableLayoutPanel copy = Ui.Vertical(); copy.Dock = DockStyle.Fill;
            Ui.AddRow(copy, ConsumerLayout.Label("Seu próximo jogo\ncomeça aqui.", 29, true));
            lblWelcomeDesc = ConsumerLayout.Label("", 11); lblWelcomeDesc.Name = "WelcomeDescription";
            Ui.AddRow(copy, lblWelcomeDesc); hero.Controls.Add(copy, 0, 0);
            hero.Controls.Add(new CoreArtwork { Dock = DockStyle.Fill, MinimumSize = new Size(150, 240) }, 1, 0);
            stack.Controls.Add(hero);
            btnCancel = ConsumerLayout.Action("btnCancel", "Cancelar"); btnCancel.Click += BtnCancel_Click;
            btnNext = ConsumerLayout.Action("btnNext", "Avançar", true); btnNext.Click += BtnNext_Click;
            actions.Controls.Add(btnCancel); actions.Controls.Add(btnNext); ConsumerLayout.BindDefault(this, btnNext);
        }
    }
}
