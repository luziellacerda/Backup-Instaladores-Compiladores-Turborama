using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace InstallerHost.Properties
{
	// Token: 0x02000010 RID: 16
	[GeneratedCode("System.Resources.Tools.StronglyTypedResourceBuilder", "16.0.0.0")]
	[DebuggerNonUserCode]
	[CompilerGenerated]
	internal class Resources
	{
		// Token: 0x0600005F RID: 95 RVA: 0x00008914 File Offset: 0x00006B14
		internal Resources()
		{
		}

		// Token: 0x17000007 RID: 7
		// (get) Token: 0x06000060 RID: 96 RVA: 0x0000891C File Offset: 0x00006B1C
		[EditorBrowsable(EditorBrowsableState.Advanced)]
		internal static ResourceManager ResourceManager
		{
			get
			{
				if (Resources.resourceMan == null)
				{
					Resources.resourceMan = new ResourceManager("InstallerHost.Properties.Resources", typeof(Resources).Assembly);
				}
				return Resources.resourceMan;
			}
		}

		// Token: 0x17000008 RID: 8
		// (get) Token: 0x06000061 RID: 97 RVA: 0x00008948 File Offset: 0x00006B48
		// (set) Token: 0x06000062 RID: 98 RVA: 0x0000894F File Offset: 0x00006B4F
		[EditorBrowsable(EditorBrowsableState.Advanced)]
		internal static CultureInfo Culture
		{
			get
			{
				return Resources.resourceCulture;
			}
			set
			{
				Resources.resourceCulture = value;
			}
		}

		// Token: 0x17000009 RID: 9
		// (get) Token: 0x06000063 RID: 99 RVA: 0x00008957 File Offset: 0x00006B57
		internal static Bitmap discord
		{
			get
			{
				return (Bitmap)Resources.ResourceManager.GetObject("discord", Resources.resourceCulture);
			}
		}

		// Token: 0x1700000A RID: 10
		// (get) Token: 0x06000064 RID: 100 RVA: 0x00008972 File Offset: 0x00006B72
		internal static Bitmap forum
		{
			get
			{
				return (Bitmap)Resources.ResourceManager.GetObject("forum", Resources.resourceCulture);
			}
		}

		// Token: 0x1700000B RID: 11
		// (get) Token: 0x06000065 RID: 101 RVA: 0x0000898D File Offset: 0x00006B8D
		internal static Bitmap logo_icon
		{
			get
			{
				return (Bitmap)Resources.ResourceManager.GetObject("logo_icon", Resources.resourceCulture);
			}
		}

		// Token: 0x1700000C RID: 12
		// (get) Token: 0x06000066 RID: 102 RVA: 0x000089A8 File Offset: 0x00006BA8
		internal static Bitmap retrobat_wizard
		{
			get
			{
				return (Bitmap)Resources.ResourceManager.GetObject("retrobat_wizard", Resources.resourceCulture);
			}
		}

		// Token: 0x1700000D RID: 13
		// (get) Token: 0x06000067 RID: 103 RVA: 0x000089C3 File Offset: 0x00006BC3
		internal static Bitmap website
		{
			get
			{
				return (Bitmap)Resources.ResourceManager.GetObject("website", Resources.resourceCulture);
			}
		}

		// Token: 0x1700000E RID: 14
		// (get) Token: 0x06000068 RID: 104 RVA: 0x000089DE File Offset: 0x00006BDE
		internal static Bitmap wiki
		{
			get
			{
				return (Bitmap)Resources.ResourceManager.GetObject("wiki", Resources.resourceCulture);
			}
		}

		// Token: 0x04000058 RID: 88
		private static ResourceManager resourceMan;

		// Token: 0x04000059 RID: 89
		private static CultureInfo resourceCulture;
	}
}
