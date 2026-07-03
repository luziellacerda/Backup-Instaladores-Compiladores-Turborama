using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using InstallerHost.Properties;

namespace InstallerHost
{
	// Token: 0x0200000E RID: 14
	public partial class WelcomeControl : UserControl
	{
		// Token: 0x06000058 RID: 88 RVA: 0x00008234 File Offset: 0x00006434
		public WelcomeControl(MainForm main)
		{
			this.mainForm = main;
			this.InitializeComponent();

this.lblWelcomeTitle.Text = Texts.GetString("Welcome", Array.Empty<object>());
			this.lblWelcomeDesc.Text = Texts.GetString("WelcomeText", new object[]
			{
				BaseForm.branch,
				BaseForm.version
			});
			this.btnCancel.Text = Texts.GetString("Cancel", Array.Empty<object>());
			this.btnNext.Text = Texts.GetString("Next >", Array.Empty<object>());
		}

		// Token: 0x06000059 RID: 89 RVA: 0x000082CD File Offset: 0x000064CD
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			TurboramaPremiumUi.ApplyWelcomeV3(this);
			base.ActiveControl = this.btnNext;
		}

		// Token: 0x0600005B RID: 91 RVA: 0x00008796 File Offset: 0x00006996
		private void BtnNext_Click(object sender, EventArgs e)
		{
			Logger.Log("Welcome screen, user clicked NEXT");
			this.mainForm.ShowLicense();
		}

		// Token: 0x0600005C RID: 92 RVA: 0x000087AD File Offset: 0x000069AD
		private void BtnCancel_Click(object sender, EventArgs e)
		{
			if (MessageBox.Show("Tem certeza que deseja cancelar a instalação?", "Cancelar instalação", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				Application.Exit();
			}
		}

		// Token: 0x04000050 RID: 80
		private MainForm mainForm;
	}
}
