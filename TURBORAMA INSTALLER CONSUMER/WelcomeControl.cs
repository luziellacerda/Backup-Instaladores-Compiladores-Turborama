using System;
using System.Windows.Forms;
namespace InstallerHost
{
    public partial class WelcomeControl : UserControl
    {
        private readonly MainForm mainForm;
        public WelcomeControl(MainForm main)
        {
            mainForm = main; InitializeComponent();
            lblWelcomeTitle.Text = ConsumerText.GetString("Welcome");
            lblWelcomeDesc.Text = ConsumerText.GetString("WelcomeText");
        }
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); ActiveControl = btnNext; }
        private void BtnNext_Click(object sender, EventArgs e)
        { Logger.Log("Welcome screen, user clicked NEXT"); mainForm.ShowLicense(); }
        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Tem certeza que deseja cancelar a instalação?", "Cancelar instalação",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Application.Exit();
        }
    }
}
