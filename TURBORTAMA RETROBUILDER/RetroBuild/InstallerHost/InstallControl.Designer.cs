namespace InstallerHost
{
	// Token: 0x02000007 RID: 7
	public partial class InstallControl : global::System.Windows.Forms.UserControl
	{
		// Token: 0x06000019 RID: 25 RVA: 0x0000300C File Offset: 0x0000120C
		private void InitializeComponent()
		{
			this.txtInfo = new global::System.Windows.Forms.Label();
			this.lblSelectFolder = new global::System.Windows.Forms.Label();
			this.txtFolder = new global::System.Windows.Forms.TextBox();
			this.btnBrowse = new global::System.Windows.Forms.Button();
			this.progressBar = new global::System.Windows.Forms.ProgressBar();
			this.lblFolderHint = new global::System.Windows.Forms.Label();
			this.btnCancel = new global::System.Windows.Forms.Button();
			this.btnInstall = new global::System.Windows.Forms.Button();
			this.btnBack = new global::System.Windows.Forms.Button();
			this.horizontalLineCtrl1 = new global::InstallerHost.HorizontalLineCtrl();
			this.wizardHeader = new global::Allegoria.Controls.WizardPanel();
			base.SuspendLayout();
			this.txtInfo.Font = new global::System.Drawing.Font("Segoe UI", 8f);
			this.txtInfo.Location = new global::System.Drawing.Point(21, 80);
			this.txtInfo.Name = "txtInfo";
			this.txtInfo.Size = new global::System.Drawing.Size(515, 59);
			this.txtInfo.TabIndex = 1;
			this.txtInfo.Text = "The installer program will install Turborama in the folder below.\r\nTo continue, click Next. If you want to specify another folder, Click Browse.";
			this.lblSelectFolder.AutoSize = true;
			this.lblSelectFolder.Font = new global::System.Drawing.Font("Segoe UI", 8f);
			this.lblSelectFolder.Location = new global::System.Drawing.Point(21, 152);
			this.lblSelectFolder.Name = "lblSelectFolder";
			this.lblSelectFolder.Size = new global::System.Drawing.Size(155, 13);
			this.lblSelectFolder.TabIndex = 2;
			this.lblSelectFolder.Text = "Select the installation folder:";
			this.txtFolder.Anchor = global::System.Windows.Forms.AnchorStyles.Top | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.txtFolder.Location = new global::System.Drawing.Point(23, 178);
			this.txtFolder.Name = "txtFolder";
			this.txtFolder.Size = new global::System.Drawing.Size(427, 20);
			this.txtFolder.TabIndex = 3;
			this.btnBrowse.Anchor = global::System.Windows.Forms.AnchorStyles.Top | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnBrowse.Location = new global::System.Drawing.Point(453, 177);
			this.btnBrowse.Name = "btnBrowse";
			this.btnBrowse.Size = new global::System.Drawing.Size(80, 25);
			this.btnBrowse.TabIndex = 4;
			this.btnBrowse.Text = "Browse...";
			this.btnBrowse.Click += new global::System.EventHandler(this.BtnBrowse_Click);
			this.progressBar.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.progressBar.Location = new global::System.Drawing.Point(22, 389);
			this.progressBar.Name = "progressBar";
			this.progressBar.Size = new global::System.Drawing.Size(510, 20);
			this.progressBar.TabIndex = 5;
			this.progressBar.Visible = false;
			this.lblFolderHint.Anchor = global::System.Windows.Forms.AnchorStyles.Top | global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.lblFolderHint.Font = new global::System.Drawing.Font("Segoe UI", 8f);
			this.lblFolderHint.Location = new global::System.Drawing.Point(20, 214);
			this.lblFolderHint.Name = "lblFolderHint";
			this.lblFolderHint.Size = new global::System.Drawing.Size(512, 133);
			this.lblFolderHint.TabIndex = 6;
			this.lblFolderHint.Text = "The program requires at least 3.38 GB of free disk space.\r\n\r\nDo not use folders with spaces or special characters.";
			this.btnCancel.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnCancel.Location = new global::System.Drawing.Point(458, 439);
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.Size = new global::System.Drawing.Size(75, 26);
			this.btnCancel.TabIndex = 7;
			this.btnCancel.Text = "Cancel";
			this.btnCancel.Click += new global::System.EventHandler(this.BtnCancel_Click);
			this.btnInstall.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnInstall.Location = new global::System.Drawing.Point(377, 439);
			this.btnInstall.Name = "btnInstall";
			this.btnInstall.Size = new global::System.Drawing.Size(75, 26);
			this.btnInstall.TabIndex = 8;
			this.btnInstall.Text = "Install";
			this.btnInstall.Click += new global::System.EventHandler(this.BtnInstall_Click);
			this.btnBack.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnBack.Location = new global::System.Drawing.Point(296, 439);
			this.btnBack.Name = "btnBack";
			this.btnBack.Size = new global::System.Drawing.Size(75, 26);
			this.btnBack.TabIndex = 9;
			this.btnBack.Text = "< Back";
			this.btnBack.Click += new global::System.EventHandler(this.BtnBack_Click);
			this.horizontalLineCtrl1.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.horizontalLineCtrl1.Location = new global::System.Drawing.Point(0, 427);
			this.horizontalLineCtrl1.Name = "horizontalLineCtrl1";
			this.horizontalLineCtrl1.Size = new global::System.Drawing.Size(547, 2);
			this.horizontalLineCtrl1.TabIndex = 12;
			this.horizontalLineCtrl1.Text = "horizontalLineCtrl1";
			this.wizardHeader.BackColor = global::System.Drawing.SystemColors.Window;
			this.wizardHeader.Dock = global::System.Windows.Forms.DockStyle.Top;
			this.wizardHeader.Image = global::InstallerHost.Properties.Resources.logo_icon;
			this.wizardHeader.Location = new global::System.Drawing.Point(0, 0);
			this.wizardHeader.Name = "wizardHeader";
			this.wizardHeader.Size = new global::System.Drawing.Size(548, 60);
			this.wizardHeader.TabIndex = 11;
			this.wizardHeader.Title = "InstallTitle";
			base.AutoScaleDimensions = new global::System.Drawing.SizeF(96f, 96f);
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Dpi;
			base.Controls.Add(this.horizontalLineCtrl1);
			base.Controls.Add(this.wizardHeader);
			base.Controls.Add(this.txtInfo);
			base.Controls.Add(this.lblSelectFolder);
			base.Controls.Add(this.txtFolder);
			base.Controls.Add(this.btnBrowse);
			base.Controls.Add(this.progressBar);
			base.Controls.Add(this.lblFolderHint);
			base.Controls.Add(this.btnCancel);
			base.Controls.Add(this.btnInstall);
			base.Controls.Add(this.btnBack);
			base.Name = "InstallControl";
			base.Size = new global::System.Drawing.Size(548, 479);
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x0400001A RID: 26
		private global::System.Windows.Forms.TextBox txtFolder;

		// Token: 0x0400001B RID: 27
		private global::System.Windows.Forms.Button btnBrowse;

		// Token: 0x0400001C RID: 28
		private global::System.Windows.Forms.Button btnInstall;

		// Token: 0x0400001D RID: 29
		private global::System.Windows.Forms.ProgressBar progressBar;

		// Token: 0x0400001E RID: 30
		private global::System.Windows.Forms.Button btnCancel;

		// Token: 0x0400001F RID: 31
		private global::System.Windows.Forms.Button btnBack;

		// Token: 0x04000020 RID: 32
		private global::System.Windows.Forms.Label txtInfo;

		// Token: 0x04000021 RID: 33
		private global::System.Windows.Forms.Label lblFolderHint;

		// Token: 0x04000022 RID: 34
		private global::Allegoria.Controls.WizardPanel wizardHeader;

		// Token: 0x04000023 RID: 35
		private global::InstallerHost.HorizontalLineCtrl horizontalLineCtrl1;

		// Token: 0x04000024 RID: 36
		private global::System.Windows.Forms.Label lblSelectFolder;
	}
}
