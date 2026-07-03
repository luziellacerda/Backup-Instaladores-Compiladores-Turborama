using System;
using System.CodeDom.Compiler;
using System.Configuration;
using System.Runtime.CompilerServices;

namespace InstallerHost.Properties
{
	// Token: 0x02000011 RID: 17
	[CompilerGenerated]
	[GeneratedCode("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "17.14.0.0")]
	internal sealed partial class Settings : ApplicationSettingsBase
	{
		// Token: 0x1700000F RID: 15
		// (get) Token: 0x06000069 RID: 105 RVA: 0x000089F9 File Offset: 0x00006BF9
		public static Settings Default
		{
			get
			{
				return Settings.defaultInstance;
			}
		}

		// Token: 0x0400005A RID: 90
		private static Settings defaultInstance = (Settings)SettingsBase.Synchronized(new Settings());
	}
}
