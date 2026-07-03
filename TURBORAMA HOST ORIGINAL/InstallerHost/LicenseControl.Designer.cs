namespace InstallerHost
{
	// Token: 0x02000008 RID: 8
	public partial class LicenseControl : global::System.Windows.Forms.UserControl
	{
		// Token: 0x06000026 RID: 38 RVA: 0x00003D60 File Offset: 0x00001F60
		private void InitializeComponent()
		{
			this.licenseTextBox = new global::System.Windows.Forms.TextBox();
			this.chkAgree = new global::System.Windows.Forms.CheckBox();
			this.btnCancel = new global::System.Windows.Forms.Button();
			this.btnNext = new global::System.Windows.Forms.Button();
			this.btnBack = new global::System.Windows.Forms.Button();
			this.horizontalLineCtrl1 = new global::InstallerHost.HorizontalLineCtrl();
			this.wizardHeader = new global::Allegoria.Controls.WizardPanel();
			base.SuspendLayout();
			this.licenseTextBox.Anchor = global::System.Windows.Forms.AnchorStyles.Top | global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.licenseTextBox.BackColor = global::System.Drawing.SystemColors.Window;
			this.licenseTextBox.Location = new global::System.Drawing.Point(22, 79);
			this.licenseTextBox.Multiline = true;
			this.licenseTextBox.Name = "licenseTextBox";
			this.licenseTextBox.ReadOnly = true;
			this.licenseTextBox.ScrollBars = global::System.Windows.Forms.ScrollBars.Vertical;
			this.licenseTextBox.Size = new global::System.Drawing.Size(511, 307);
			this.licenseTextBox.TabIndex = 1;
			this.chkAgree.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.chkAgree.Location = new global::System.Drawing.Point(22, 396);
			this.chkAgree.Name = "chkAgree";
			this.chkAgree.Size = new global::System.Drawing.Size(507, 24);
			this.chkAgree.TabIndex = 2;
			this.chkAgree.Text = "I accept the terms of the license agreement";
			this.chkAgree.CheckedChanged += new global::System.EventHandler(this.chkAgree_CheckedChanged);
			this.btnCancel.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnCancel.FlatStyle = global::System.Windows.Forms.FlatStyle.System;
			this.btnCancel.Location = new global::System.Drawing.Point(458, 439);
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.Size = new global::System.Drawing.Size(75, 26);
			this.btnCancel.TabIndex = 3;
			this.btnCancel.Text = "Cancel";
			this.btnCancel.Click += new global::System.EventHandler(this.BtnCancel_Click);
			this.btnNext.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnNext.Enabled = false;
			this.btnNext.FlatStyle = global::System.Windows.Forms.FlatStyle.System;
			this.btnNext.Location = new global::System.Drawing.Point(377, 439);
			this.btnNext.Name = "btnNext";
			this.btnNext.Size = new global::System.Drawing.Size(75, 26);
			this.btnNext.TabIndex = 4;
			this.btnNext.Text = "Next";
			this.btnNext.Click += new global::System.EventHandler(this.btnNext_Click);
			this.btnBack.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Right;
			this.btnBack.FlatStyle = global::System.Windows.Forms.FlatStyle.System;
			this.btnBack.Location = new global::System.Drawing.Point(296, 439);
			this.btnBack.Name = "btnBack";
			this.btnBack.Size = new global::System.Drawing.Size(75, 26);
			this.btnBack.TabIndex = 5;
			this.btnBack.Text = "Back";
			this.btnBack.Click += new global::System.EventHandler(this.btnBack_Click);
			this.horizontalLineCtrl1.Anchor = global::System.Windows.Forms.AnchorStyles.Bottom | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.horizontalLineCtrl1.Location = new global::System.Drawing.Point(0, 427);
			this.horizontalLineCtrl1.Name = "horizontalLineCtrl1";
			this.horizontalLineCtrl1.Size = new global::System.Drawing.Size(548, 2);
			this.horizontalLineCtrl1.TabIndex = 7;
			this.horizontalLineCtrl1.Text = "horizontalLineCtrl1";
			this.wizardHeader.BackColor = global::System.Drawing.SystemColors.Window;
			this.wizardHeader.Dock = global::System.Windows.Forms.DockStyle.Top;
			this.wizardHeader.Image = global::InstallerHost.Properties.Resources.logo_icon;
			this.wizardHeader.Location = new global::System.Drawing.Point(0, 0);
			this.wizardHeader.Name = "wizardHeader";
			this.wizardHeader.Size = new global::System.Drawing.Size(548, 60);
			this.wizardHeader.TabIndex = 6;
			this.wizardHeader.Title = "Licence Agreement";
			base.AutoScaleDimensions = new global::System.Drawing.SizeF(96f, 96f);
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Dpi;
			base.Controls.Add(this.horizontalLineCtrl1);
			base.Controls.Add(this.wizardHeader);
			base.Controls.Add(this.licenseTextBox);
			base.Controls.Add(this.chkAgree);
			base.Controls.Add(this.btnCancel);
			base.Controls.Add(this.btnNext);
			base.Controls.Add(this.btnBack);
			base.Name = "LicenseControl";
			base.Size = new global::System.Drawing.Size(548, 479);
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x04000026 RID: 38
		private global::System.Windows.Forms.TextBox licenseTextBox;

		// Token: 0x04000027 RID: 39
		private global::System.Windows.Forms.CheckBox chkAgree;

		// Token: 0x04000028 RID: 40
		private global::System.Windows.Forms.Button btnCancel;

		// Token: 0x04000029 RID: 41
		private global::System.Windows.Forms.Button btnNext;

		// Token: 0x0400002A RID: 42
		private global::Allegoria.Controls.WizardPanel wizardHeader;

		// Token: 0x0400002B RID: 43
		private global::InstallerHost.HorizontalLineCtrl horizontalLineCtrl1;

		// Token: 0x0400002C RID: 44
		private global::System.Windows.Forms.Button btnBack;
	}
}
