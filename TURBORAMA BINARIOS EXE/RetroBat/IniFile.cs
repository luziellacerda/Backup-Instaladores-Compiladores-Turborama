using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RetroBat
{
	// Token: 0x02000004 RID: 4
	public class IniFile : IDisposable
	{
		// Token: 0x06000014 RID: 20 RVA: 0x00002366 File Offset: 0x00000566
		public static IniFile FromFile(string path, IniOptions options = (IniOptions)0)
		{
			return new IniFile(path, options);
		}

		// Token: 0x06000015 RID: 21 RVA: 0x00002370 File Offset: 0x00000570
		public static string GetOptionValue(IniFile ini, string section, string key, string defaultValue)
		{
			string value = ini.GetValue(section, key);
			if (!string.IsNullOrEmpty(value))
			{
				return value.Trim(new char[] { '"' });
			}
			if (value == null)
			{
				ini.WriteValue(section, key, defaultValue);
				return defaultValue;
			}
			return defaultValue;
		}

		// Token: 0x06000016 RID: 22 RVA: 0x000023AF File Offset: 0x000005AF
		public static string GetDefaultIniContent()
		{
			return "; TURBORAMA GLOBAL CONFIG FILE\r\n\r\n[TurboRama]\r\n\r\n; At startup TurboRama will detect or not the language used in Windows to set automatically the same language in the frontend and RetroArch emulator.\r\nLanguageDetection=1\r\n\r\n; At startup TurboRama will reset the default config files options of emulationstation and turborama.ini.\r\n; Use at your own risk.\t\r\nResetConfigMode=0\r\n\r\n; Run automatically TurboRama at Windows startup (0=NO 1=STARTUP 2=REGISTRY).\r\nAutostart=0\r\n\r\n; Set the Start Delay for TurboRama to start automatically at startup (in milliseconds).\r\nAutoStartDelay=0\r\n\r\n; Run WiimoteGun at TurboRama's startup. You can use your wiimote as a gun and navigate through EmulationStation.\r\nWiimoteGun=0\r\n\r\n[SplashScreen]\r\n\r\n; Set if video introduction is played before running the interface.\r\nEnableIntro=1\r\n\r\n; The name of the video file to play. RandomVideo must be set on 0 to take effect.\r\nFileName=\"turborama-neon.mp4\"\r\n\r\n; If 'default' is set, TurboRama will use the default video path where video files are stored.\r\n; Enter a full path to use a custom directory for video files.\r\nFilePath=\"default\"\r\n\r\n; Play video files randomly when TurboRama starts.\r\nRandomVideo=1\r\n\r\n; Set the delay between the start of the video and the start of the interface.\r\n; Setting a longer delay can help if the video is not displayed in the foreground\r\nVideoDelay=1000\r\n\r\n; By default RetroBat loads EmulationStation in parallel of the intro video, setting this to '1' tells TurboRama to wait for the video to finish before loading ES\r\nWaitForVideoEnd=1\r\n\r\n; Set this to stop when video automatically when the interface has loaded\r\nKillVideoWhenESReady=0\r\n\r\n; Allow killing intro video with Gamepad press (this only works with XInput controllers)\r\nGamepadVideoKill=1\r\n\r\n[EmulationStation]\r\n\r\n; Start the frontend in fullscreen or in windowed mode.\r\nFullscreen=1\r\n\r\n; Borderless Fullscreen\r\nFullscreenBorderless=1\r\n\r\n; Force the fullscreen resolution with the parameters set at WindowXSize and WindowYSize.\r\nForceFullscreenRes=0\r\n\r\n; Select EmulationStation theme randomly.\r\nRandomTheme=0\r\n\r\n; Force to retry to get focus after a certain amount of time (milliseconds).\r\nFocusDelay=2000\r\n\r\n; The frontend will parse only the gamelist.xml files in roms directories to display available games.\r\n; If files are added when this option is enabled, they will not appear in the gamelists of the frontend. The option must be enabled again to display new entries properly.\r\nGameListOnly=0\r\n \r\n; 0 = run the frontend normally.\r\n; 1 = run the frontend in kiosk mode.\r\n; 2 = run the frontend in kid mode.\r\nInterfaceMode=0\r\n\r\n; Set to which monitor index the frontend will be displayed.\r\nMonitorIndex=0\r\n\r\n; Disable to disable VSync in TurboRama interface.\r\nVSync=1\r\n\r\n; Set if the option to quit the frontend is displayed or not when the full menu is enabled.\r\nNoExitMenu=0\r\n\r\n; Set if you are using an old GPU not compatible with newest OpenGL version.\r\nOpenGL2_1=0\r\n\r\n; Set the windows width of the frontend.\r\nWindowXSize=1280\r\n\r\n; Set the windows height of the frontend.\r\nWindowYSize=720\r\n\r\n; Draw framerate in EmulationStation.\r\nDrawFramerate=0";
		}

		// Token: 0x06000017 RID: 23 RVA: 0x000023B6 File Offset: 0x000005B6
		public void SetOptions(IniOptions options)
		{
			this._options = options;
		}

		// Token: 0x06000018 RID: 24 RVA: 0x000023C0 File Offset: 0x000005C0
		public IniFile(string path, IniOptions options = (IniOptions)0)
		{
			this._options = options;
			this._path = path;
			this._dirty = false;
			if (!File.Exists(this._path))
			{
				return;
			}
			try
			{
				using (TextReader textReader = new StreamReader(this._path))
				{
					IniFile.Section section = null;
					HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					for (string text = textReader.ReadLine(); text != null; text = textReader.ReadLine())
					{
						text = text.Trim();
						if (text != "" || this._options.HasFlag(IniOptions.KeepEmptyLines))
						{
							if (text.StartsWith("["))
							{
								int num = text.IndexOf("]");
								if (num > 0)
								{
									hashSet.Clear();
									section = this._sections.GetOrAddSection(text.Substring(1, num - 1));
								}
							}
							else
							{
								string[] array = (this._options.HasFlag(IniOptions.UseDoubleEqual) ? text.Split(new string[] { "==" }, 2, StringSplitOptions.None) : text.Split(new char[] { '=' }, 2));
								if (section == null)
								{
									hashSet.Clear();
									section = this._sections.GetOrAddSection(null);
								}
								IniFile.Key key = new IniFile.Key();
								string text2 = array[0].Trim();
								if (this._options.HasFlag(IniOptions.ManageKeysWithQuotes) && text2.StartsWith("\"") && text2.EndsWith("\""))
								{
									text2 = text2.Substring(1, text2.Length - 2);
								}
								key.Name = text2;
								if (!key.IsComment && !this._options.HasFlag(IniOptions.AllowDuplicateValues) && hashSet.Contains(key.Name))
								{
									text = textReader.ReadLine();
									continue;
								}
								if (key.IsComment)
								{
									key.Name = text;
									key.Value = null;
								}
								else if (array.Length > 1)
								{
									hashSet.Add(key.Name);
									int num2 = array[1].IndexOf(";");
									if (num2 > 0)
									{
										key.Comment = array[1].Substring(num2);
										array[1] = array[1].Substring(0, num2);
									}
									key.Value = array[1].Trim();
								}
								section.Add(key);
							}
						}
					}
					textReader.Close();
				}
			}
			catch (Exception ex)
			{
				throw ex;
			}
		}

		// Token: 0x06000019 RID: 25 RVA: 0x0000266C File Offset: 0x0000086C
		public IniSection GetOrCreateSection(string key)
		{
			return new IniFile.PrivateIniSection(key, this);
		}

		// Token: 0x0600001A RID: 26 RVA: 0x00002675 File Offset: 0x00000875
		public string[] EnumerateSections()
		{
			return this._sections.Select<IniFile.Section, string>((IniFile.Section s) => s.Name).Distinct<string>().ToArray<string>();
		}

		// Token: 0x0600001B RID: 27 RVA: 0x000026AC File Offset: 0x000008AC
		public string[] EnumerateKeys(string sectionName)
		{
			IniFile.Section section = this._sections.Get(sectionName);
			if (section != null)
			{
				return section.Select<IniFile.Key, string>((IniFile.Key k) => k.Name).ToArray<string>();
			}
			return new string[0];
		}

		// Token: 0x0600001C RID: 28 RVA: 0x000026FC File Offset: 0x000008FC
		public KeyValuePair<string, string>[] EnumerateValues(string sectionName)
		{
			List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
			IniFile.Section section = this._sections.Get(sectionName);
			if (section != null)
			{
				foreach (IniFile.Key key in section)
				{
					if (!key.IsComment && !string.IsNullOrEmpty(key.Name))
					{
						list.Add(new KeyValuePair<string, string>(key.Name, key.Value));
					}
				}
			}
			return list.ToArray();
		}

		// Token: 0x0600001D RID: 29 RVA: 0x00002788 File Offset: 0x00000988
		public void ClearSection(string sectionName)
		{
			IniFile.Section section = this._sections.Get(sectionName);
			if (section != null && section.Any<IniFile.Key>())
			{
				this._dirty = true;
				section.Clear();
			}
		}

		// Token: 0x0600001E RID: 30 RVA: 0x000027BC File Offset: 0x000009BC
		public string GetValue(string sectionName, string key)
		{
			IniFile.Section section = this._sections.Get(sectionName);
			if (section != null)
			{
				return section.GetValue(key);
			}
			return null;
		}

		// Token: 0x0600001F RID: 31 RVA: 0x000027E4 File Offset: 0x000009E4
		public void WriteValue(string sectionName, string keyName, string value)
		{
			IniFile.Section orAddSection = this._sections.GetOrAddSection(sectionName);
			IniFile.Key key = orAddSection.Get(keyName);
			if (key != null && key.Value == value)
			{
				return;
			}
			if (key == null)
			{
				key = orAddSection.Add(keyName, null);
			}
			key.Value = value;
			this._dirty = true;
		}

		// Token: 0x06000020 RID: 32 RVA: 0x00002832 File Offset: 0x00000A32
		public void AppendValue(string sectionName, string keyName, string value)
		{
			if (!this._options.HasFlag(IniOptions.AllowDuplicateValues))
			{
				this.WriteValue(sectionName, keyName, value);
				return;
			}
			this._sections.GetOrAddSection(sectionName).Add(keyName, value);
			this._dirty = true;
		}

		// Token: 0x06000021 RID: 33 RVA: 0x00002874 File Offset: 0x00000A74
				public void Remove(string sectionName, string keyName)
		{
			IniFile.Section section = this._sections.Get(sectionName);
			if (section == null)
			{
				return;
			}

			foreach (IniFile.Key key in section.Where(k => k.Name.Equals(keyName, StringComparison.InvariantCultureIgnoreCase)).ToArray())
			{
				this._dirty = true;
				section.Remove(key);
			}
		}

		// Token: 0x17000001 RID: 1
		// (get) Token: 0x06000022 RID: 34 RVA: 0x000028ED File Offset: 0x00000AED
		public bool IsDirty
		{
			get
			{
				return this._dirty;
			}
		}

		// Token: 0x06000023 RID: 35 RVA: 0x000028F8 File Offset: 0x00000AF8
		public override string ToString()
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (IniFile.Section section in this._sections)
			{
				if (!string.IsNullOrEmpty(section.Name) && section.Name != "ROOT" && section.Any<IniFile.Key>())
				{
					stringBuilder.AppendLine("[" + section.Name + "]");
				}
				foreach (IniFile.Key key in section)
				{
					if (string.IsNullOrEmpty(key.Name))
					{
						if (!string.IsNullOrEmpty(key.Comment))
						{
							stringBuilder.AppendLine(key.Comment);
						}
						else if (this._options.HasFlag(IniOptions.KeepEmptyLines))
						{
							stringBuilder.AppendLine();
						}
					}
					else if (key.IsComment)
					{
						stringBuilder.AppendLine(key.Name);
					}
					else if ((!string.IsNullOrEmpty(key.Value) || this._options.HasFlag(IniOptions.KeepEmptyValues)) && !string.IsNullOrEmpty(key.Name))
					{
						if (this._options.HasFlag(IniOptions.ManageKeysWithQuotes))
						{
							stringBuilder.Append("\"" + key.Name + "\"");
						}
						else
						{
							stringBuilder.Append(key.Name);
						}
						if (this._options.HasFlag(IniOptions.UseSpaces))
						{
							stringBuilder.Append(" ");
						}
						if (this._options.HasFlag(IniOptions.UseDoubleEqual))
						{
							stringBuilder.Append("==");
						}
						else
						{
							stringBuilder.Append("=");
						}
						if (this._options.HasFlag(IniOptions.UseSpaces))
						{
							stringBuilder.Append(" ");
						}
						stringBuilder.Append(key.Value);
						if (!string.IsNullOrEmpty(key.Comment))
						{
							stringBuilder.Append("\t\t\t");
							stringBuilder.Append(key.Comment);
						}
						stringBuilder.AppendLine();
					}
				}
				if (!this._options.HasFlag(IniOptions.KeepEmptyLines))
				{
					stringBuilder.AppendLine();
				}
			}
			return stringBuilder.ToString();
		}

		// Token: 0x06000024 RID: 36 RVA: 0x00002BAC File Offset: 0x00000DAC
		public void Save()
		{
			if (!this._dirty)
			{
				return;
			}
			try
			{
				string directoryName = Path.GetDirectoryName(this._path);
				if (!Directory.Exists(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				using (TextWriter textWriter = new StreamWriter(this._path))
				{
					textWriter.Write(this.ToString());
					textWriter.Close();
				}
				this._dirty = false;
			}
			catch (Exception ex)
			{
				SimpleLogger.Instance.Error("[IniFile] Save failed " + ex.Message, ex);
			}
		}

		// Token: 0x06000025 RID: 37 RVA: 0x00002C4C File Offset: 0x00000E4C
		public void Dispose()
		{
			this.Save();
		}

		// Token: 0x04000013 RID: 19
		private IniOptions _options;

		// Token: 0x04000014 RID: 20
		private bool _dirty;

		// Token: 0x04000015 RID: 21
		private string _path;

		// Token: 0x04000016 RID: 22
		private IniFile.Sections _sections = new IniFile.Sections();

		// Token: 0x02000010 RID: 16
		private class PrivateIniSection : IniSection
		{
			// Token: 0x060000A3 RID: 163 RVA: 0x00005995 File Offset: 0x00003B95
			public PrivateIniSection(string name, IniFile ini)
				: base(name, ini)
			{
			}
		}

		// Token: 0x02000011 RID: 17
		private class Key
		{
			// Token: 0x17000021 RID: 33
			// (get) Token: 0x060000A4 RID: 164 RVA: 0x0000599F File Offset: 0x00003B9F
			// (set) Token: 0x060000A5 RID: 165 RVA: 0x000059A7 File Offset: 0x00003BA7
			public string Name { get; set; }

			// Token: 0x17000022 RID: 34
			// (get) Token: 0x060000A6 RID: 166 RVA: 0x000059B0 File Offset: 0x00003BB0
			// (set) Token: 0x060000A7 RID: 167 RVA: 0x000059B8 File Offset: 0x00003BB8
			public string Value { get; set; }

			// Token: 0x17000023 RID: 35
			// (get) Token: 0x060000A8 RID: 168 RVA: 0x000059C1 File Offset: 0x00003BC1
			// (set) Token: 0x060000A9 RID: 169 RVA: 0x000059C9 File Offset: 0x00003BC9
			public string Comment { get; set; }

			// Token: 0x17000024 RID: 36
			// (get) Token: 0x060000AA RID: 170 RVA: 0x000059D2 File Offset: 0x00003BD2
			public bool IsComment
			{
				get
				{
					return this.Name == null || this.Name.StartsWith(";") || this.Name.StartsWith("#");
				}
			}

			// Token: 0x060000AB RID: 171 RVA: 0x00005A00 File Offset: 0x00003C00
			public override string ToString()
			{
				if (string.IsNullOrEmpty(this.Name))
				{
					return "";
				}
				if (string.IsNullOrEmpty(this.Value))
				{
					return this.Name + "=";
				}
				return this.Name + "=" + this.Value;
			}
		}

		// Token: 0x02000012 RID: 18
		private class KeyList : List<IniFile.Key>
		{
		}

		// Token: 0x02000013 RID: 19
		private class Section : IEnumerable<IniFile.Key>, IEnumerable
		{
			// Token: 0x060000AE RID: 174 RVA: 0x00005A64 File Offset: 0x00003C64
			public Section()
			{
				this._keys = new IniFile.KeyList();
			}

			// Token: 0x17000025 RID: 37
			// (get) Token: 0x060000AF RID: 175 RVA: 0x00005A77 File Offset: 0x00003C77
			// (set) Token: 0x060000B0 RID: 176 RVA: 0x00005A7F File Offset: 0x00003C7F
			public string Name { get; set; }

			// Token: 0x060000B1 RID: 177 RVA: 0x00005A88 File Offset: 0x00003C88
			public override string ToString()
			{
				if (string.IsNullOrEmpty(this.Name))
				{
					return "";
				}
				return "[" + this.Name + "]";
			}

			// Token: 0x060000B2 RID: 178 RVA: 0x00005AB4 File Offset: 0x00003CB4
			public bool Exists(string keyName)
			{
				using (List<IniFile.Key>.Enumerator enumerator = this._keys.GetEnumerator())
				{
					while (enumerator.MoveNext())
					{
						if (enumerator.Current.Name.Equals(keyName, StringComparison.InvariantCultureIgnoreCase))
						{
							return true;
						}
					}
				}
				return false;
			}

			// Token: 0x060000B3 RID: 179 RVA: 0x00005B14 File Offset: 0x00003D14
			public IniFile.Key Get(string keyName)
			{
				foreach (IniFile.Key key in this._keys)
				{
					if (key.Name.Equals(keyName, StringComparison.InvariantCultureIgnoreCase))
					{
						return key;
					}
				}
				return null;
			}

			// Token: 0x060000B4 RID: 180 RVA: 0x00005B78 File Offset: 0x00003D78
			public string GetValue(string keyName)
			{
				foreach (IniFile.Key key in this._keys)
				{
					if (key.Name.Equals(keyName, StringComparison.InvariantCultureIgnoreCase))
					{
						return key.Value;
					}
				}
				return null;
			}

			// Token: 0x060000B5 RID: 181 RVA: 0x00005BE0 File Offset: 0x00003DE0
			public IEnumerator<IniFile.Key> GetEnumerator()
			{
				return this._keys.GetEnumerator();
			}

			// Token: 0x060000B6 RID: 182 RVA: 0x00005BF2 File Offset: 0x00003DF2
			IEnumerator IEnumerable.GetEnumerator()
			{
				return this._keys.GetEnumerator();
			}

			// Token: 0x060000B7 RID: 183 RVA: 0x00005C04 File Offset: 0x00003E04
			public IniFile.Key Add(string keyName, string value = null)
			{
				IniFile.Key key = new IniFile.Key
				{
					Name = keyName,
					Value = value
				};
				this._keys.Add(key);
				return key;
			}

			// Token: 0x060000B8 RID: 184 RVA: 0x00005C32 File Offset: 0x00003E32
			public IniFile.Key Add(IniFile.Key key)
			{
				this._keys.Add(key);
				return key;
			}

			// Token: 0x060000B9 RID: 185 RVA: 0x00005C41 File Offset: 0x00003E41
			internal void Clear()
			{
				this._keys.Clear();
			}

			// Token: 0x060000BA RID: 186 RVA: 0x00005C4E File Offset: 0x00003E4E
			internal void Remove(IniFile.Key key)
			{
				this._keys.Remove(key);
			}

			// Token: 0x04000072 RID: 114
			private IniFile.KeyList _keys;
		}

		// Token: 0x02000014 RID: 20
		private class Sections : IEnumerable<IniFile.Section>, IEnumerable
		{
			// Token: 0x060000BB RID: 187 RVA: 0x00005C5D File Offset: 0x00003E5D
			public Sections()
			{
				this._sections = new List<IniFile.Section>();
			}

			// Token: 0x060000BC RID: 188 RVA: 0x00005C70 File Offset: 0x00003E70
			public IniFile.Section Get(string sectionName)
			{
				if (sectionName == null)
				{
					sectionName = string.Empty;
				}
				return this._sections.FirstOrDefault<IniFile.Section>((IniFile.Section s) => s.Name.Equals(sectionName, StringComparison.InvariantCultureIgnoreCase));
			}

			// Token: 0x060000BD RID: 189 RVA: 0x00005CB4 File Offset: 0x00003EB4
			public IniFile.Section GetOrAddSection(string sectionName)
			{
				if (sectionName == null)
				{
					sectionName = string.Empty;
				}
				IniFile.Section section = this.Get(sectionName);
				if (section == null)
				{
					section = new IniFile.Section
					{
						Name = sectionName
					};
					if ((string.IsNullOrEmpty(sectionName) || sectionName == "ROOT") && this._sections.Count > 0)
					{
						this._sections.Insert(0, section);
					}
					else
					{
						this._sections.Add(section);
					}
				}
				return section;
			}

			// Token: 0x060000BE RID: 190 RVA: 0x00005D22 File Offset: 0x00003F22
			public IEnumerator<IniFile.Section> GetEnumerator()
			{
				return this._sections.GetEnumerator();
			}

			// Token: 0x060000BF RID: 191 RVA: 0x00005D34 File Offset: 0x00003F34
			IEnumerator IEnumerable.GetEnumerator()
			{
				return this._sections.GetEnumerator();
			}

			// Token: 0x04000073 RID: 115
			private List<IniFile.Section> _sections;
		}
	}
}


