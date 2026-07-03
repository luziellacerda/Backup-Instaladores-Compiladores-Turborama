namespace Allegoria.Controls
{
	// Token: 0x02000003 RID: 3
	[global::System.Drawing.ToolboxBitmap(typeof(global::System.Windows.Forms.Panel))]
	public partial class WizardPanel : global::System.Windows.Forms.UserControl
	{
		// Token: 0x0600000C RID: 12 RVA: 0x0000228E File Offset: 0x0000048E
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x0600000D RID: 13 RVA: 0x000022B0 File Offset: 0x000004B0
		private void InitializeComponent()
		{
			this.label1 = new global::System.Windows.Forms.Label();
			base.SuspendLayout();
			this.label1.Anchor = global::System.Windows.Forms.AnchorStyles.Top | global::System.Windows.Forms.AnchorStyles.Left | global::System.Windows.Forms.AnchorStyles.Right;
			this.label1.BackColor = global::System.Drawing.Color.Transparent;
			this.label1.Location = new global::System.Drawing.Point(14, 5);
			this.label1.Name = "label1";
			this.label1.Size = new global::System.Drawing.Size(469, 49);
			this.label1.TabIndex = 0;
			this.label1.Text = "Title";
			this.label1.TextAlign = global::System.Drawing.ContentAlignment.MiddleLeft;
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Inherit;
			this.BackColor = global::System.Drawing.SystemColors.Window;
			base.Controls.Add(this.label1);
			base.Name = "WizardPanel";
			base.Size = new global::System.Drawing.Size(576, 60);
			base.ResumeLayout(false);
		}

		// Token: 0x04000003 RID: 3
		private global::System.ComponentModel.IContainer components;

		// Token: 0x04000004 RID: 4
		private global::System.Windows.Forms.Label label1;
	}
}
