using System;
using System.Windows.Forms;
namespace InstallerHost
{
    public partial class FinishControl : UserControl
    {
        public FinishControl(MainForm main, string path)
        {
            InitializeComponent(); lblMessage.Text = ConsumerText.GetString("InstallComplete");
            lblWelcomeDesc.Text = ConsumerText.GetString("InstallCompleteDescription"); lblInstallPath.Text = path;
        }
        private void BtnFinish_Click(object sender, EventArgs e)
        {
            // Preserve the audited original: never launch the product elevated.
            Application.Exit();
        }
    }
}
