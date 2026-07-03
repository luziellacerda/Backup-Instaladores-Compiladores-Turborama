using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace InstallerHost
{
	// Token: 0x02000009 RID: 9
	public partial class MainForm : BaseForm
	{
		// Token: 0x0600002B RID: 43 RVA: 0x000042CC File Offset: 0x000024CC
		public MainForm()
		{
			this.DoubleBuffered = true;
		}

		// Token: 0x0600002C RID: 44 RVA: 0x000042DB File Offset: 0x000024DB
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			this.ShowWelcome();
		}

		// Token: 0x0600002D RID: 45 RVA: 0x000042EC File Offset: 0x000024EC
		private void ShowControl(UserControl control)
		{
			base.SuspendLayout();
			if (this.currentControl != null)
			{
				base.Focus();
				base.Controls.Remove(this.currentControl);
				this.currentControl = null;
			}
			this.currentControl = control;
			this.currentControl.Dock = DockStyle.Fill;
			base.Controls.Add(this.currentControl);
			this.currentControl.BringToFront();
			this.currentControl.Focus();
			base.ResumeLayout();
			this.currentControl.Invalidate();
		}

		// Token: 0x0600002E RID: 46 RVA: 0x00004372 File Offset: 0x00002572
		public void ShowWelcome()
		{
			if (this._welcome == null)
			{
				this._welcome = new WelcomeControl(this);
			}
			Logger.Log("Showing Welcome screen.");
			this.ShowControl(this._welcome);
		}

		// Token: 0x0600002F RID: 47 RVA: 0x0000439E File Offset: 0x0000259E
		public void ShowLicense()
		{
			if (this._license == null)
			{
				this._license = new LicenseControl(this);
			}
			Logger.Log("Showing License screen.");
			this.ShowControl(this._license);
		}

		// Token: 0x06000030 RID: 48 RVA: 0x000043CC File Offset: 0x000025CC
		public void ShowPrerequisites(bool goForward)
		{
			if (this._prerequisite == null)
			{
				this._prerequisite = new PrerequisiteControl(this);
			}
			if (!this._prerequisite.SkipIfAllInstalled())
			{
				Logger.Log("Showing Prerequisites screen.");
				this.ShowControl(this._prerequisite);
				return;
			}
			if (goForward)
			{
				this.ShowInstall();
				return;
			}
			this.ShowLicense();
		}

		// Token: 0x06000031 RID: 49 RVA: 0x00004421 File Offset: 0x00002621
		public void ShowInstall()
		{
			if (this._install == null)
			{
				this._install = new InstallControl(this);
			}
			Logger.Log("Showing Install screen.");
			this.ShowControl(this._install);
		}

		// Token: 0x06000032 RID: 50 RVA: 0x0000444D File Offset: 0x0000264D
		public void ShowFinish(string installPath)
		{
			Logger.Log("Showing Finish screen.");
			this.ShowControl(new FinishControl(this, installPath));
		}

		// Token: 0x0400002D RID: 45
		private UserControl currentControl;

		// Token: 0x0400002E RID: 46
		private WelcomeControl _welcome;

		// Token: 0x0400002F RID: 47
		private LicenseControl _license;

		// Token: 0x04000030 RID: 48
		private PrerequisiteControl _prerequisite;

		// Token: 0x04000031 RID: 49
		private InstallControl _install;
	}
}
