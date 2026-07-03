namespace InstallerHost
{
	// Token: 0x02000005 RID: 5
	public partial class FinishControl : global::System.Windows.Forms.UserControl
	{
		// Token: 0x06000012 RID: 18 RVA: 0x000025D4 File Offset: 0x000007D4
		private void InitializeComponent()
		{
			this.chkRunApp = new global::System.Windows.Forms.CheckBox();
			this.linkPanel = new global::System.Windows.Forms.FlowLayoutPanel();
			this.btnFinish = new global::System.Windows.Forms.Button();
			this.panel1 = new global::System.Windows.Forms.Panel();
			this.lblWelcomeDesc = new global::System.Windows.Forms.Label();
			this.bannerPictureBox = new global::System.Windows.Forms.PictureBox();
			this.horizontalLineCtrl1 = new global::InstallerHost.HorizontalLineCtrl();
			this.lblMessage = new global::System.Windows.Forms.Label();
			this.btnCancel = new global::System.Windows.Forms.Button();
			this.btnBack = new global::System.Windows.Forms.Button();
			this.panel1.SuspendLayout();
			((global::System.ComponentModel.ISupportInitialize)this.bannerPictureBox).BeginInit();
			base.SuspendLayout();
			this.chkRunApp.Location = new global::System.Drawing.Point(246, 154);
			this.chkRunApp.Name = "chkRunApp";
			this.chkRunApp.Size = new global::System.Drawing.Size(406, 24);
			this.chkRunApp.TabIndex = 1;
			this.chkRunApp.Text = "Start retrobat.exe";
			this.linkPanel.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.linkPanel.Location = new global::System.Drawing.Point(243, 378);
			this.linkPanel.Name = "linkPanel";
			this.linkPanel.Size = new global::System.Drawing.Size(467, 30);
			this.linkPanel.TabIndex = 2;
			this.btnFinish.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnFinish.Location = new global::System.Drawing.Point(557, 428);
			this.btnFinish.Name = "btnFinish";
			this.btnFinish.Size = new global::System.Drawing.Size(75, 26);
			this.btnFinish.TabIndex = 3;
			this.btnFinish.Text = "Finish";
			this.btnFinish.Click += new global::System.EventHandler(this.BtnFinish_Click);
			this.panel1.Anchor = global::System.Windows.Forms.AnchorStyles.Top | global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.panel1.BackColor = global::System.Drawing.Color.White;
			this.panel1.Controls.Add(this.lblWelcomeDesc);
			this.panel1.Controls.Add(this.bannerPictureBox);
			this.panel1.Controls.Add(this.linkPanel);
			this.panel1.Controls.Add(this.chkRunApp);
			this.panel1.Controls.Add(this.horizontalLineCtrl1);
			this.panel1.Controls.Add(this.lblMessage);
			this.panel1.Location = new global::System.Drawing.Point(0, 0);
			this.panel1.Name = "panel1";
			this.panel1.Size = new global::System.Drawing.Size(728, 418);
			this.panel1.TabIndex = 5;
			this.lblWelcomeDesc.Anchor = global::System.Windows.Forms.AnchorStyles.Top | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.lblWelcomeDesc.Location = new global::System.Drawing.Point(243, 79);
			this.lblWelcomeDesc.Name = "lblWelcomeDesc";
			this.lblWelcomeDesc.Size = new global::System.Drawing.Size(467, 71);
			this.lblWelcomeDesc.TabIndex = 6;
			this.lblWelcomeDesc.Text = "Retrobat has been installer to your computer.\r\n\r\nPress finish to close this wizard.";
			this.bannerPictureBox.BackColor = global::System.Drawing.Color.Transparent;
			this.bannerPictureBox.Dock = global::System.Windows.Forms.DockStyle.Left;
			this.bannerPictureBox.Image = global::InstallerHost.Properties.Resources.retrobat_wizard;
			this.bannerPictureBox.Location = new global::System.Drawing.Point(0, 0);
			this.bannerPictureBox.Name = "bannerPictureBox";
			this.bannerPictureBox.Size = new global::System.Drawing.Size(224, 416);
			this.bannerPictureBox.SizeMode = global::System.Windows.Forms.PictureBoxSizeMode.StretchImage;
			this.bannerPictureBox.TabIndex = 5;
			this.bannerPictureBox.TabStop = false;
			this.horizontalLineCtrl1.Dock = global::System.Windows.Forms.DockStyle.Bottom;
			this.horizontalLineCtrl1.Location = new global::System.Drawing.Point(0, 416);
			this.horizontalLineCtrl1.Name = "horizontalLineCtrl1";
			this.horizontalLineCtrl1.Size = new global::System.Drawing.Size(728, 2);
			this.horizontalLineCtrl1.TabIndex = 5;
			this.horizontalLineCtrl1.Text = "horizontalLineCtrl1";
			this.lblMessage.Anchor = global::System.Windows.Forms.AnchorStyles.Top | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.lblMessage.Font = new global::System.Drawing.Font("Segoe UI", 14f, global::System.Drawing.FontStyle.Bold, global::System.Drawing.GraphicsUnit.Point, 0);
			this.lblMessage.Location = new global::System.Drawing.Point(238, 13);
			this.lblMessage.Name = "lblMessage";
			this.lblMessage.Size = new global::System.Drawing.Size(472, 60);
			this.lblMessage.TabIndex = 0;
			this.lblMessage.Text = "Installation complete";
			this.btnCancel.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnCancel.Enabled = false;
			this.btnCancel.Location = new global::System.Drawing.Point(638, 428);
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.Size = new global::System.Drawing.Size(75, 26);
			this.btnCancel.TabIndex = 10;
			this.btnCancel.Text = "Cancel";
			this.btnBack.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnBack.Enabled = false;
			this.btnBack.Location = new global::System.Drawing.Point(476, 428);
			this.btnBack.Name = "btnBack";
			this.btnBack.Size = new global::System.Drawing.Size(75, 26);
			this.btnBack.TabIndex = 11;
			this.btnBack.Text = "< Back";
			base.AutoScaleDimensions = new global::System.Drawing.SizeF(96f, 96f);
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Dpi;
			base.Controls.Add(this.btnCancel);
			base.Controls.Add(this.btnBack);
			base.Controls.Add(this.panel1);
			base.Controls.Add(this.btnFinish);
			base.Name = "FinishControl";
			base.Size = new global::System.Drawing.Size(728, 468);
			this.panel1.ResumeLayout(false);
			((global::System.ComponentModel.ISupportInitialize)this.bannerPictureBox).EndInit();
			base.ResumeLayout(false);
		}

		// Token: 0x0400000E RID: 14
		private global::System.Windows.Forms.CheckBox chkRunApp;

		// Token: 0x0400000F RID: 15
		private global::System.Windows.Forms.Button btnFinish;

		// Token: 0x04000010 RID: 16
		private global::System.Windows.Forms.Panel panel1;

		// Token: 0x04000011 RID: 17
		private global::System.Windows.Forms.PictureBox bannerPictureBox;

		// Token: 0x04000012 RID: 18
		private global::InstallerHost.HorizontalLineCtrl horizontalLineCtrl1;

		// Token: 0x04000013 RID: 19
		private global::System.Windows.Forms.Label lblMessage;

		// Token: 0x04000014 RID: 20
		private global::System.Windows.Forms.Button btnCancel;

		// Token: 0x04000015 RID: 21
		private global::System.Windows.Forms.Button btnBack;

		// Token: 0x04000016 RID: 22
		private global::System.Windows.Forms.Label lblWelcomeDesc;

		// Token: 0x04000017 RID: 23
		private global::System.Windows.Forms.FlowLayoutPanel linkPanel;
	}
}
