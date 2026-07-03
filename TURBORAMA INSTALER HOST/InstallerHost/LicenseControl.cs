using System;
using System.Drawing;
using System.Windows.Forms;
using Allegoria.Controls;
using InstallerHost.Properties;

namespace InstallerHost
{
	// Token: 0x02000008 RID: 8
	public partial class LicenseControl : UserControl
	{
		// Token: 0x06000024 RID: 36 RVA: 0x00003C80 File Offset: 0x00001E80
		public LicenseControl(MainForm main)
		{
			this.mainForm = main;
			this.InitializeComponent();
this.Load += delegate(object s, EventArgs e) {
};

this.wizardHeader.Text = Texts.GetString("LicenseIntro", Array.Empty<object>());
			this.licenseTextBox.Text = Texts.GetString("LicenseText", Array.Empty<object>());
			this.chkAgree.Text = Texts.GetString("AgreeText", Array.Empty<object>());
			this.btnCancel.Text = Texts.GetString("Cancel", Array.Empty<object>());
			this.btnNext.Text = Texts.GetString("Next >", Array.Empty<object>());
			this.btnBack.Text = Texts.GetString("< Back", Array.Empty<object>());
		}

		// Token: 0x06000025 RID: 37 RVA: 0x00003D3C File Offset: 0x00001F3C
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			TurboramaPremiumUi.ApplyLicenseV3(this);
			this.licenseTextBox.Select(0, 0);
			base.ActiveControl = this.licenseTextBox;
		}

		// Token: 0x06000027 RID: 39 RVA: 0x0000421C File Offset: 0x0000241C
		private void chkAgree_CheckedChanged(object sender, EventArgs e)
		{
			Logger.Log("Licence screen, licence accepted: " + this.chkAgree.Checked.ToString());
			this.btnNext.Enabled = this.chkAgree.Checked;
			base.ActiveControl = this.btnNext;
		}

		// Token: 0x06000028 RID: 40 RVA: 0x0000426D File Offset: 0x0000246D
		public void BtnCancel_Click(object sender, EventArgs e)
		{
			if (MessageBox.Show("Tem certeza que deseja cancelar a instalação?", "Cancelar instalação", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				Application.Exit();
			}
		}

		// Token: 0x06000029 RID: 41 RVA: 0x0000429D File Offset: 0x0000249D
		private void btnBack_Click(object sender, EventArgs e)
		{
			Logger.Log("Licence screen, user clicked BACK");
			this.mainForm.ShowWelcome();
		}

		// Token: 0x0600002A RID: 42 RVA: 0x000042B4 File Offset: 0x000024B4
		private void btnNext_Click(object sender, EventArgs e)
		{
			Logger.Log("Licence screen, user clicked NEXT");
			this.mainForm.ShowPrerequisites(true);
		}

		// Token: 0x04000025 RID: 37
		private MainForm mainForm;
	}
}
