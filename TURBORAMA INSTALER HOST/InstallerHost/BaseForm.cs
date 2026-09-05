using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace InstallerHost
{
	// Token: 0x02000004 RID: 4
	public partial class BaseForm : Form
	{
		// Token: 0x17000004 RID: 4
		// (get) Token: 0x0600000E RID: 14 RVA: 0x00002395 File Offset: 0x00000595
		public int BannerWidth
		{
			get
			{
				return 210;
			}
		}

		// Token: 0x0600000F RID: 15 RVA: 0x0000239C File Offset: 0x0000059C
		public BaseForm()
		{
			this.InitializeComponent();

			if (base.DesignMode)
			{
				return;
			}
			this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			this.Font = TurboramaPremiumTheme.CreateFont(9f, FontStyle.Regular);
			this.BackColor = TurboramaPremiumUi.Background;
			this.ForeColor = TurboramaPremiumUi.Text;
			this.MaximizeBox = true;
			this.SizeGripStyle = SizeGripStyle.Show;
			this.KeyPreview = true;
			string[] array = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location).Split(new char[] { '-' });
			BaseForm.version = ((array.Length > 1) ? array[1] : "");
			BaseForm.branch = ((array.Length > 2) ? array[2] : "");
			this.Text = "LZ Games e Informática - Sistema Turborama";
		}

		// Token: 0x04000005 RID: 5
		public int buttonWidth = 80;

		// Token: 0x04000006 RID: 6
		public int buttonHeight = 30;

		// Token: 0x04000007 RID: 7
		public int spacing = 10;

		// Token: 0x04000008 RID: 8
		public int bottomMargin = 20;

		// Token: 0x04000009 RID: 9
		public int rightMargin = 20;

		// Token: 0x0400000A RID: 10
		public static string version;

		// Token: 0x0400000B RID: 11
		public static string branch;
	}
}
