namespace InstallerHost
{
	// Token: 0x0200000B RID: 11
	public partial class PrerequisiteControl : global::System.Windows.Forms.UserControl
	{
		// Token: 0x0600003F RID: 63 RVA: 0x00004BEC File Offset: 0x00002DEC
		private void InitializeComponent()
		{
			this.lblAllInstalled = new global::System.Windows.Forms.Label();
			this.chkVCpp = new global::System.Windows.Forms.CheckBox();
			this.chkDirectX = new global::System.Windows.Forms.CheckBox();
			this.chkDokany = new global::System.Windows.Forms.CheckBox();
			this.chkwinFSP = new global::System.Windows.Forms.CheckBox();
			this.progressBar = new global::System.Windows.Forms.ProgressBar();
			this.statusLabel = new global::System.Windows.Forms.Label();
			this.btnCancel = new global::System.Windows.Forms.Button();
			this.btnNext = new global::System.Windows.Forms.Button();
			this.btnBack = new global::System.Windows.Forms.Button();
			this.horizontalLineCtrl1 = new global::InstallerHost.HorizontalLineCtrl();
			this.wizardHeader = new global::Allegoria.Controls.WizardPanel();
			base.SuspendLayout();
			this.lblAllInstalled.AutoSize = true;
			this.lblAllInstalled.Font = new global::System.Drawing.Font("Segoe UI", 11f);
			this.lblAllInstalled.Location = new global::System.Drawing.Point(20, 81);
			this.lblAllInstalled.Name = "lblAllInstalled";
			this.lblAllInstalled.Size = new global::System.Drawing.Size(176, 20);
			this.lblAllInstalled.TabIndex = 1;
			this.lblAllInstalled.Text = "All prerequisites installed";
			this.lblAllInstalled.Visible = false;
			this.chkVCpp.AutoSize = true;
			this.chkVCpp.Checked = true;
			this.chkVCpp.CheckState = global::System.Windows.Forms.CheckState.Checked;
			this.chkVCpp.Location = new global::System.Drawing.Point(24, 117);
			this.chkVCpp.Name = "chkVCpp";
			this.chkVCpp.Size = new global::System.Drawing.Size(59, 17);
			this.chkVCpp.TabIndex = 2;
			this.chkVCpp.Text = "vcText";
			this.chkDirectX.AutoSize = true;
			this.chkDirectX.Checked = true;
			this.chkDirectX.CheckState = global::System.Windows.Forms.CheckState.Checked;
			this.chkDirectX.Location = new global::System.Drawing.Point(24, 143);
			this.chkDirectX.Name = "chkDirectX";
			this.chkDirectX.Size = new global::System.Drawing.Size(60, 17);
			this.chkDirectX.TabIndex = 3;
			this.chkDirectX.Text = "dx9text";
			this.chkDokany.AutoSize = true;
			this.chkDokany.Location = new global::System.Drawing.Point(24, 169);
			this.chkDokany.Name = "chkDokany";
			this.chkDokany.Size = new global::System.Drawing.Size(82, 17);
			this.chkDokany.TabIndex = 4;
			this.chkDokany.Text = "dokanyText";
			this.chkwinFSP.AutoSize = true;
			this.chkwinFSP.Location = new global::System.Drawing.Point(24, 195);
			this.chkwinFSP.Name = "chkwinFSP";
			this.chkwinFSP.Size = new global::System.Drawing.Size(82, 17);
			this.chkwinFSP.TabIndex = 5;
			this.chkwinFSP.Text = "winFSPtext";
			this.progressBar.Anchor = global::System.Windows.Forms.AnchorStyles.Top | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.progressBar.Location = new global::System.Drawing.Point(23, 300);
			this.progressBar.Name = "progressBar";
			this.progressBar.Size = new global::System.Drawing.Size(510, 22);
			this.progressBar.TabIndex = 8;
			this.progressBar.Visible = false;
			this.statusLabel.Anchor = global::System.Windows.Forms.AnchorStyles.Top | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.statusLabel.Location = new global::System.Drawing.Point(21, 325);
			this.statusLabel.Name = "statusLabel";
			this.statusLabel.Size = new global::System.Drawing.Size(512, 23);
			this.statusLabel.TabIndex = 9;
			this.statusLabel.Text = "status";
			this.statusLabel.Visible = false;
			this.btnCancel.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnCancel.FlatStyle = global::System.Windows.Forms.FlatStyle.System;
			this.btnCancel.Location = new global::System.Drawing.Point(458, 439);
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.Size = new global::System.Drawing.Size(75, 26);
			this.btnCancel.TabIndex = 5;
			this.btnCancel.Text = "Cancel";
			this.btnCancel.Click += new global::System.EventHandler(this.BtnCancel_Click);
			this.btnNext.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnNext.FlatStyle = global::System.Windows.Forms.FlatStyle.System;
			this.btnNext.Location = new global::System.Drawing.Point(377, 439);
			this.btnNext.Name = "btnNext";
			this.btnNext.Size = new global::System.Drawing.Size(75, 26);
			this.btnNext.TabIndex = 6;
			this.btnNext.Text = "Next >";
			this.btnNext.Click += new global::System.EventHandler(this.BtnNext_Click);
			this.btnBack.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnBack.FlatStyle = global::System.Windows.Forms.FlatStyle.System;
			this.btnBack.Location = new global::System.Drawing.Point(296, 439);
			this.btnBack.Name = "btnBack";
			this.btnBack.Size = new global::System.Drawing.Size(75, 26);
			this.btnBack.TabIndex = 7;
			this.btnBack.Text = "< Back";
			this.btnBack.Click += new global::System.EventHandler(this.BtnBack_Click);
			this.horizontalLineCtrl1.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.horizontalLineCtrl1.Location = new global::System.Drawing.Point(0, 427);
			this.horizontalLineCtrl1.Name = "horizontalLineCtrl1";
			this.horizontalLineCtrl1.Size = new global::System.Drawing.Size(547, 2);
			this.horizontalLineCtrl1.TabIndex = 11;
			this.horizontalLineCtrl1.Text = "horizontalLineCtrl1";
			this.wizardHeader.BackColor = global::System.Drawing.SystemColors.Window;
			this.wizardHeader.Dock = global::System.Windows.Forms.DockStyle.Top;
			this.wizardHeader.Image = global::InstallerHost.Properties.Resources.logo_icon;
			this.wizardHeader.Location = new global::System.Drawing.Point(0, 0);
			this.wizardHeader.Name = "wizardHeader";
			this.wizardHeader.Size = new global::System.Drawing.Size(548, 60);
			this.wizardHeader.TabIndex = 10;
			this.wizardHeader.Title = "PrerequisiteIntro";
			base.AutoScaleDimensions = new global::System.Drawing.SizeF(96f, 96f);
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Dpi;
			base.Controls.Add(this.horizontalLineCtrl1);
			base.Controls.Add(this.wizardHeader);
			base.Controls.Add(this.lblAllInstalled);
			base.Controls.Add(this.chkVCpp);
			base.Controls.Add(this.chkDirectX);
			base.Controls.Add(this.chkDokany);
			base.Controls.Add(this.chkwinFSP);
			base.Controls.Add(this.btnCancel);
			base.Controls.Add(this.btnNext);
			base.Controls.Add(this.btnBack);
			base.Controls.Add(this.progressBar);
			base.Controls.Add(this.statusLabel);
			base.Name = "PrerequisiteControl";
			base.Size = new global::System.Drawing.Size(548, 479);
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x04000035 RID: 53
		private global::System.Windows.Forms.Label lblAllInstalled;

		// Token: 0x04000036 RID: 54
		private global::System.Windows.Forms.Label statusLabel;

		// Token: 0x04000037 RID: 55
		private global::System.Windows.Forms.CheckBox chkVCpp;

		// Token: 0x04000038 RID: 56
		private global::System.Windows.Forms.CheckBox chkDirectX;

		// Token: 0x04000039 RID: 57
		private global::System.Windows.Forms.CheckBox chkDokany;

		// Token: 0x0400003A RID: 58
		private global::System.Windows.Forms.CheckBox chkwinFSP;

		// Token: 0x0400003B RID: 59
		private global::System.Windows.Forms.Button btnCancel;

		// Token: 0x0400003C RID: 60
		private global::System.Windows.Forms.Button btnNext;

		// Token: 0x0400003D RID: 61
		private global::System.Windows.Forms.Button btnBack;

		// Token: 0x0400003F RID: 63
		private global::System.Windows.Forms.ProgressBar progressBar;

		// Token: 0x04000041 RID: 65
		private global::Allegoria.Controls.WizardPanel wizardHeader;

		// Token: 0x04000042 RID: 66
		private global::InstallerHost.HorizontalLineCtrl horizontalLineCtrl1;
	}
}
