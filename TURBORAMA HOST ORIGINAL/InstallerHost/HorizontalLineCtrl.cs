using System;
using System.Drawing;
using System.Windows.Forms;

namespace InstallerHost
{
	// Token: 0x02000006 RID: 6
	[ToolboxBitmap(typeof(LinkLabel))]
	public class HorizontalLineCtrl : Control
	{
		// Token: 0x06000016 RID: 22 RVA: 0x00002E10 File Offset: 0x00001010
		public HorizontalLineCtrl()
		{
			base.SetStyle(ControlStyles.ResizeRedraw, true);
			base.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
			base.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
		}

		// Token: 0x06000017 RID: 23 RVA: 0x00002E3C File Offset: 0x0000103C
		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.DrawLine(SystemPens.ControlDark, base.ClientRectangle.Left, base.ClientRectangle.Top + base.ClientRectangle.Height / 2, base.ClientRectangle.Right, base.ClientRectangle.Top + base.ClientRectangle.Height / 2);
			graphics.DrawLine(SystemPens.ControlLightLight, base.ClientRectangle.Left, base.ClientRectangle.Top + base.ClientRectangle.Height / 2 + 1, base.ClientRectangle.Right, base.ClientRectangle.Top + base.ClientRectangle.Height / 2 + 1);
		}
	}
}
