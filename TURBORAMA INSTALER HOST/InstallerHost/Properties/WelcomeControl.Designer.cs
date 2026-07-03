namespace InstallerHost
{
	// Token: 0x0200000E RID: 14
	public partial class WelcomeControl : global::System.Windows.Forms.UserControl
	{
		// Token: 0x0600005A RID: 90 RVA: 0x000082E4 File Offset: 0x000064E4
		private void InitializeComponent()
		{
			this.lblWelcomeTitle = new global::System.Windows.Forms.Label();
			this.lblWelcomeDesc = new global::System.Windows.Forms.Label();
			this.btnCancel = new global::System.Windows.Forms.Button();
			this.btnNext = new global::System.Windows.Forms.Button();
			this.panel1 = new global::System.Windows.Forms.Panel();
			this.bannerPictureBox = new global::System.Windows.Forms.PictureBox();
			this.horizontalLineCtrl1 = new global::InstallerHost.HorizontalLineCtrl();
			this.panel1.SuspendLayout();
			((global::System.ComponentModel.ISupportInitialize)this.bannerPictureBox).BeginInit();
			base.SuspendLayout();
			this.lblWelcomeTitle.Anchor = global::System.Windows.Forms.AnchorStyles.Top | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.lblWelcomeTitle.Font = new global::System.Drawing.Font("Segoe UI", 14f, global::System.Drawing.FontStyle.Bold, global::System.Drawing.GraphicsUnit.Point, 0);
			this.lblWelcomeTitle.Location = new global::System.Drawing.Point(238, 13);
			this.lblWelcomeTitle.Name = "lblWelcomeTitle";
			this.lblWelcomeTitle.Size = new global::System.Drawing.Size(472, 59);
			this.lblWelcomeTitle.TabIndex = 0;
			this.lblWelcomeTitle.Text = "Welcome to the Turborama installation program";
			this.lblWelcomeDesc.Anchor = global::System.Windows.Forms.AnchorStyles.Top | global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.lblWelcomeDesc.Location = new global::System.Drawing.Point(243, 80);
			this.lblWelcomeDesc.Name = "lblWelcomeDesc";
			this.lblWelcomeDesc.Size = new global::System.Drawing.Size(467, 322);
			this.lblWelcomeDesc.TabIndex = 1;
			this.lblWelcomeDesc.Text = "This wizard will guide you through the installation of Turborama..";
			this.btnCancel.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnCancel.FlatStyle = global::System.Windows.Forms.FlatStyle.System;
			this.btnCancel.Location = new global::System.Drawing.Point(638, 428);
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.Size = new global::System.Drawing.Size(75, 26);
			this.btnCancel.TabIndex = 2;
			this.btnCancel.Text = "Cancel";
			this.btnCancel.Click += new global::System.EventHandler(this.BtnCancel_Click);
			this.btnNext.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnNext.FlatStyle = global::System.Windows.Forms.FlatStyle.System;
			this.btnNext.Location = new global::System.Drawing.Point(557, 428);
			this.btnNext.Name = "btnNext";
			this.btnNext.Size = new global::System.Drawing.Size(75, 26);
			this.btnNext.TabIndex = 3;
			this.btnNext.Text = "Next";
			this.btnNext.Click += new global::System.EventHandler(this.BtnNext_Click);
			this.panel1.Anchor = global::System.Windows.Forms.AnchorStyles.Top | global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.panel1.BackColor = global::System.Drawing.Color.White;
			this.panel1.Controls.Add(this.bannerPictureBox);
			this.panel1.Controls.Add(this.horizontalLineCtrl1);
			this.panel1.Controls.Add(this.lblWelcomeTitle);
			this.panel1.Controls.Add(this.lblWelcomeDesc);
			this.panel1.Location = new global::System.Drawing.Point(0, 0);
			this.panel1.Name = "panel1";
			this.panel1.Size = new global::System.Drawing.Size(728, 418);
			this.panel1.TabIndex = 4;
			this.bannerPictureBox.BackColor = global::System.Drawing.Color.Transparent;
			this.bannerPictureBox.Dock = global::System.Windows.Forms.DockStyle.Left;
			this.bannerPictureBox.Image = global::InstallerHost.Properties.Resources.turborama_wizard;
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
			base.AutoScaleDimensions = new global::System.Drawing.SizeF(96f, 96f);
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Dpi;
			base.Controls.Add(this.panel1);
			base.Controls.Add(this.btnCancel);
			base.Controls.Add(this.btnNext);
			base.Name = "WelcomeControl";
			base.Size = new global::System.Drawing.Size(728, 468);
			this.panel1.ResumeLayout(false);
			((global::System.ComponentModel.ISupportInitialize)this.bannerPictureBox).EndInit();
			base.ResumeLayout(false);
		}

		// Token: 0x04000051 RID: 81
		private global::System.Windows.Forms.Label lblWelcomeTitle;

		// Token: 0x04000052 RID: 82
		private global::System.Windows.Forms.Label lblWelcomeDesc;

		// Token: 0x04000053 RID: 83
		private global::System.Windows.Forms.Button btnCancel;

		// Token: 0x04000054 RID: 84
		private global::System.Windows.Forms.Panel panel1;

		// Token: 0x04000055 RID: 85
		private global::InstallerHost.HorizontalLineCtrl horizontalLineCtrl1;

		// Token: 0x04000056 RID: 86
		private global::System.Windows.Forms.PictureBox bannerPictureBox;

		// Token: 0x04000057 RID: 87
		private global::System.Windows.Forms.Button btnNext;
	}
}
