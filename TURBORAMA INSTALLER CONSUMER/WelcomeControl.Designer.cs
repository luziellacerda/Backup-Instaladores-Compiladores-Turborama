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
            TurboRamaArtwork hero = new TurboRamaArtwork(false) { Name = "TurboRamaHero", Dock = DockStyle.Fill,
                Margin = Padding.Empty };
            TableLayoutPanel copy = Ui.Vertical(); copy.Dock = DockStyle.None;
            copy.Name = "WelcomeCopy";
            copy.Padding = new Padding(8, 12, 16, 12); copy.BackColor = Color.Transparent;
            Ui.AddRow(copy, Ui.Label("DO FLIPERAMA AO PC", 9, Palette.Accent, true));
            Label headline = ConsumerLayout.Label("Seu próximo jogo\ncomeça aqui.", 23, true);
            headline.Name = "WelcomeHeadline"; Ui.AddRow(copy, headline);
            lblWelcomeDesc = ConsumerLayout.Label("", 10.5f); lblWelcomeDesc.Name = "WelcomeDescription";
            lblWelcomeDesc.ForeColor = Palette.Muted;
            Ui.AddRow(copy, lblWelcomeDesc); hero.Controls.Add(copy);
            hero.SizeChanged += delegate
            {
                int width = (int)(hero.ClientSize.Width * .43f);
                copy.MinimumSize = new Size(width, 0); copy.MaximumSize = new Size(width, 0); copy.Width = width;
                copy.Top = System.Math.Max(0, (hero.ClientSize.Height - copy.PreferredSize.Height) / 2);
            };
            body.Controls.Add(hero);
            btnCancel = ConsumerLayout.Action("btnCancel", "Cancelar"); btnCancel.Click += BtnCancel_Click;
            btnNext = ConsumerLayout.Action("btnNext", "Avançar", true); btnNext.Click += BtnNext_Click;
            actions.Controls.Add(btnCancel); actions.Controls.Add(btnNext); ConsumerLayout.BindDefault(this, btnNext);
        }
    }
}
