using System;
using System.Collections.Generic;
using System.IO;

namespace RetroBuild
{
	// Token: 0x02000003 RID: 3
	internal class IniParser
	{
		// Token: 0x06000003 RID: 3 RVA: 0x00002380 File Offset: 0x00000580
		public IniParser(string path)
		{
			this.Load(path);
		}

		// Token: 0x06000004 RID: 4 RVA: 0x000023A0 File Offset: 0x000005A0
		private void Load(string path)
		{
			if (!File.Exists(path))
			{
				throw new FileNotFoundException("INI file not found", path);
			}
			string text = "";
			string[] array = File.ReadAllLines(path);
			for (int i = 0; i < array.Length; i++)
			{
				string text2 = array[i].Trim();
				if (!text2.StartsWith(";") && !text2.StartsWith("#") && !string.IsNullOrEmpty(text2))
				{
					if (text2.StartsWith("[") && text2.EndsWith("]"))
					{
						text = text2.Substring(1, text2.Length - 2);
						if (!this.data.ContainsKey(text))
						{
							this.data[text] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
						}
					}
					else if (text2.Contains("="))
					{
						int num = text2.IndexOf('=');
						string text3 = text2.Substring(0, num).Trim();
						string text4 = text2.Substring(num + 1).Trim();
						if (!this.data.ContainsKey(text))
						{
							this.data[text] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
						}
						this.data[text][text3] = text4;
					}
				}
			}
		}

		// Token: 0x06000005 RID: 5 RVA: 0x000024DA File Offset: 0x000006DA
		public string Get(string section, string key, string defaultValue = "")
		{
			if (this.data.ContainsKey(section) && this.data[section].ContainsKey(key))
			{
				return this.data[section][key];
			}
			return defaultValue;
		}

		// Token: 0x04000001 RID: 1
		private Dictionary<string, Dictionary<string, string>> data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
	}
}
