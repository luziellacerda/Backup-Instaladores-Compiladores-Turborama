using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Allegoria.Controls
{
	// Token: 0x02000003 RID: 3
	[ToolboxBitmap(typeof(Panel))]
	public partial class WizardPanel : UserControl
	{
		// Token: 0x06000003 RID: 3 RVA: 0x000020C8 File Offset: 0x000002C8
		public WizardPanel()
		{
			base.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
			base.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
			base.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
			base.SetStyle(ControlStyles.ResizeRedraw, true);
			this.InitializeComponent();
			this.BackColor = SystemColors.Window;
			this.label1.Text = "";
		}

		// Token: 0x06000004 RID: 4 RVA: 0x00002129 File Offset: 0x00000329
		protected override void OnFontChanged(EventArgs e)
		{
			base.OnFontChanged(e);
			this.label1.Font = new Font(this.Font.FontFamily, this.Font.Size + 4f, FontStyle.Bold);
			this.Refresh();
		}

		// Token: 0x06000005 RID: 5 RVA: 0x00002168 File Offset: 0x00000368
		protected override void OnPaintBackground(PaintEventArgs e)
		{
			base.OnPaintBackground(e);
			if (this._image != null)
			{
				int num = 8;
				Rectangle clientRectangle = base.ClientRectangle;
				clientRectangle.X = clientRectangle.Right - clientRectangle.Height;
				clientRectangle.Width = clientRectangle.Height;
				clientRectangle.Inflate(-num, -num);
				e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
				e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
				e.Graphics.DrawImage(this._image, clientRectangle);
			}
			e.Graphics.DrawLine(SystemPens.ControlDark, base.ClientRectangle.Left, base.ClientRectangle.Bottom - 1, base.ClientRectangle.Right, base.ClientRectangle.Bottom - 1);
		}

		// Token: 0x17000001 RID: 1
		// (get) Token: 0x06000006 RID: 6 RVA: 0x00002232 File Offset: 0x00000432
		// (set) Token: 0x06000007 RID: 7 RVA: 0x0000223F File Offset: 0x0000043F
		public override string Text
		{
			get
			{
				return this.label1.Text;
			}
			set
			{
				this.label1.Text = value;
			}
		}

		// Token: 0x17000002 RID: 2
		// (get) Token: 0x06000008 RID: 8 RVA: 0x0000224D File Offset: 0x0000044D
		// (set) Token: 0x06000009 RID: 9 RVA: 0x0000225A File Offset: 0x0000045A
		[DefaultValue("")]
		public string Title
		{
			get
			{
				return this.label1.Text;
			}
			set
			{
				this.label1.Text = value;
				base.Invalidate();
			}
		}

		// Token: 0x17000003 RID: 3
		// (get) Token: 0x0600000A RID: 10 RVA: 0x0000226E File Offset: 0x0000046E
		// (set) Token: 0x0600000B RID: 11 RVA: 0x00002276 File Offset: 0x00000476
		[DefaultValue(null)]
		public Image Image
		{
			get
			{
				return this._image;
			}
			set
			{
				if (this._image != value)
				{
					this._image = value;
					base.Invalidate();
				}
			}
		}

		// Token: 0x04000002 RID: 2
		private Image _image;
	}
}
