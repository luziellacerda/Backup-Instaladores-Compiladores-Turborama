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
            TurboRamaArtwork hero = new TurboRamaArtwork(false) { Name = "TurboRamaHero", Height = 350,
                Margin = Padding.Empty };
            TableLayoutPanel copy = Ui.Vertical(); copy.Dock = DockStyle.None;
            copy.Padding = new Padding(24, 24, 16, 18); copy.BackColor = Palette.Background;
            Ui.AddRow(copy, Ui.Label("DO FLIPERAMA AO PC", 9, Palette.Accent, true));
            Ui.AddRow(copy, ConsumerLayout.Label("Seu próximo jogo\ncomeça aqui.", 25, true));
            lblWelcomeDesc = ConsumerLayout.Label("", 11); lblWelcomeDesc.Name = "WelcomeDescription";
            Ui.AddRow(copy, lblWelcomeDesc); hero.Controls.Add(copy);
            hero.SizeChanged += delegate
            {
                int width = System.Math.Max(280, (int)(hero.ClientSize.Width * .43f));
                copy.MinimumSize = new Size(width, 0); copy.MaximumSize = new Size(width, 0); copy.Width = width;
            };
            stack.Controls.Add(hero);
            btnCancel = ConsumerLayout.Action("btnCancel", "Cancelar"); btnCancel.Click += BtnCancel_Click;
            btnNext = ConsumerLayout.Action("btnNext", "Avançar", true); btnNext.Click += BtnNext_Click;
            actions.Controls.Add(btnCancel); actions.Controls.Add(btnNext); ConsumerLayout.BindDefault(this, btnNext);
        }
    }
}
