using System;

namespace RetroBat
{
	// Token: 0x02000005 RID: 5
	public class IniSection
	{
		// Token: 0x06000026 RID: 38 RVA: 0x00002C54 File Offset: 0x00000E54
		protected IniSection(string name, IniFile ini)
		{
			this._ini = ini;
			this._sectionName = name;
		}

		// Token: 0x17000002 RID: 2
		public string this[string key]
		{
			get
			{
				return this._ini.GetValue(this._sectionName, key);
			}
			set
			{
				this._ini.WriteValue(this._sectionName, key, value);
			}
		}

		// Token: 0x06000029 RID: 41 RVA: 0x00002C93 File Offset: 0x00000E93
		public void Clear()
		{
			this._ini.ClearSection(this._sectionName);
		}

		// Token: 0x17000003 RID: 3
		// (get) Token: 0x0600002A RID: 42 RVA: 0x00002CA6 File Offset: 0x00000EA6
		public string[] Keys
		{
			get
			{
				return this._ini.EnumerateKeys(this._sectionName);
			}
		}

		// Token: 0x04000017 RID: 23
		private IniFile _ini;

		// Token: 0x04000018 RID: 24
		private string _sectionName;
	}
}
