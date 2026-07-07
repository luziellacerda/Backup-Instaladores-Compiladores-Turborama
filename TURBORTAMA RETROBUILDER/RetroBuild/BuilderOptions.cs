using System;

namespace RetroBuild
{
	// Token: 0x02000004 RID: 4
	public class BuilderOptions
	{
		// Token: 0x17000001 RID: 1
		// (get) Token: 0x06000006 RID: 6 RVA: 0x00002512 File Offset: 0x00000712
		// (set) Token: 0x06000007 RID: 7 RVA: 0x0000251A File Offset: 0x0000071A
		public string RetrobatVersion { get; set; }

		// Token: 0x17000002 RID: 2
		// (get) Token: 0x06000008 RID: 8 RVA: 0x00002523 File Offset: 0x00000723
		// (set) Token: 0x06000009 RID: 9 RVA: 0x0000252B File Offset: 0x0000072B
		public string RetroarchVersion { get; set; }

		// Token: 0x17000003 RID: 3
		// (get) Token: 0x0600000A RID: 10 RVA: 0x00002534 File Offset: 0x00000734
		// (set) Token: 0x0600000B RID: 11 RVA: 0x0000253C File Offset: 0x0000073C
		public string Branch { get; set; }

		// Token: 0x17000004 RID: 4
		// (get) Token: 0x0600000C RID: 12 RVA: 0x00002545 File Offset: 0x00000745
		// (set) Token: 0x0600000D RID: 13 RVA: 0x0000254D File Offset: 0x0000074D
		public string Architecture { get; set; }

		// Token: 0x17000005 RID: 5
		// (get) Token: 0x0600000E RID: 14 RVA: 0x00002556 File Offset: 0x00000756
		// (set) Token: 0x0600000F RID: 15 RVA: 0x0000255E File Offset: 0x0000075E
		public bool GetBatgui { get; set; }

		// Token: 0x17000006 RID: 6
		// (get) Token: 0x06000010 RID: 16 RVA: 0x00002567 File Offset: 0x00000767
		// (set) Token: 0x06000011 RID: 17 RVA: 0x0000256F File Offset: 0x0000076F
		public bool GetBatoceraPorts { get; set; }

		// Token: 0x17000007 RID: 7
		// (get) Token: 0x06000012 RID: 18 RVA: 0x00002578 File Offset: 0x00000778
		// (set) Token: 0x06000013 RID: 19 RVA: 0x00002580 File Offset: 0x00000780
		public bool GetBios { get; set; }

		// Token: 0x17000008 RID: 8
		// (get) Token: 0x06000014 RID: 20 RVA: 0x00002589 File Offset: 0x00000789
		// (set) Token: 0x06000015 RID: 21 RVA: 0x00002591 File Offset: 0x00000791
		public bool GetDecorations { get; set; }

		// Token: 0x17000009 RID: 9
		// (get) Token: 0x06000016 RID: 22 RVA: 0x0000259A File Offset: 0x0000079A
		// (set) Token: 0x06000017 RID: 23 RVA: 0x000025A2 File Offset: 0x000007A2
		public bool GetDefaultTheme { get; set; }

		// Token: 0x1700000A RID: 10
		// (get) Token: 0x06000018 RID: 24 RVA: 0x000025AB File Offset: 0x000007AB
		// (set) Token: 0x06000019 RID: 25 RVA: 0x000025B3 File Offset: 0x000007B3
		public bool GetEmulationstation { get; set; }

		// Token: 0x1700000B RID: 11
		// (get) Token: 0x0600001A RID: 26 RVA: 0x000025BC File Offset: 0x000007BC
		// (set) Token: 0x0600001B RID: 27 RVA: 0x000025C4 File Offset: 0x000007C4
		public bool GetEmulators { get; set; }

		// Token: 0x1700000C RID: 12
		// (get) Token: 0x0600001C RID: 28 RVA: 0x000025CD File Offset: 0x000007CD
		// (set) Token: 0x0600001D RID: 29 RVA: 0x000025D5 File Offset: 0x000007D5
		public bool GetLrcores { get; set; }

		// Token: 0x1700000D RID: 13
		// (get) Token: 0x0600001E RID: 30 RVA: 0x000025DE File Offset: 0x000007DE
		// (set) Token: 0x0600001F RID: 31 RVA: 0x000025E6 File Offset: 0x000007E6
		public bool GetRetroarch { get; set; }

		// Token: 0x1700000E RID: 14
		// (get) Token: 0x06000020 RID: 32 RVA: 0x000025EF File Offset: 0x000007EF
		// (set) Token: 0x06000021 RID: 33 RVA: 0x000025F7 File Offset: 0x000007F7
		public bool GetRetrobatBinaries { get; set; }

		// Token: 0x1700000F RID: 15
		// (get) Token: 0x06000022 RID: 34 RVA: 0x00002600 File Offset: 0x00000800
		// (set) Token: 0x06000023 RID: 35 RVA: 0x00002608 File Offset: 0x00000808
		public bool GetSystem { get; set; }

		// Token: 0x17000010 RID: 16
		// (get) Token: 0x06000024 RID: 36 RVA: 0x00002611 File Offset: 0x00000811
		// (set) Token: 0x06000025 RID: 37 RVA: 0x00002619 File Offset: 0x00000819
		public bool GetWiimotegun { get; set; }

		// Token: 0x17000011 RID: 17
		// (get) Token: 0x06000026 RID: 38 RVA: 0x00002622 File Offset: 0x00000822
		// (set) Token: 0x06000027 RID: 39 RVA: 0x0000262A File Offset: 0x0000082A
		public string SevenZipPath { get; set; }

		// Token: 0x17000012 RID: 18
		// (get) Token: 0x06000028 RID: 40 RVA: 0x00002633 File Offset: 0x00000833
		// (set) Token: 0x06000029 RID: 41 RVA: 0x0000263B File Offset: 0x0000083B
		public string WgetPath { get; set; }

		// Token: 0x17000013 RID: 19
		// (get) Token: 0x0600002A RID: 42 RVA: 0x00002644 File Offset: 0x00000844
		// (set) Token: 0x0600002B RID: 43 RVA: 0x0000264C File Offset: 0x0000084C
		public string CurlPath { get; set; }

		// Token: 0x17000014 RID: 20
		// (get) Token: 0x0600002C RID: 44 RVA: 0x00002655 File Offset: 0x00000855
		// (set) Token: 0x0600002D RID: 45 RVA: 0x0000265D File Offset: 0x0000085D
		public string RetrobatFTPPath { get; set; }

		// Token: 0x17000015 RID: 21
		// (get) Token: 0x0600002E RID: 46 RVA: 0x00002666 File Offset: 0x00000866
		// (set) Token: 0x0600002F RID: 47 RVA: 0x0000266E File Offset: 0x0000086E
		public string RetrobatBinariesBaseUrl { get; set; }

		// Token: 0x17000016 RID: 22
		// (get) Token: 0x06000030 RID: 48 RVA: 0x00002677 File Offset: 0x00000877
		// (set) Token: 0x06000031 RID: 49 RVA: 0x0000267F File Offset: 0x0000087F
		public string EmulationstationUrl { get; set; }

		// Token: 0x17000017 RID: 23
		// (get) Token: 0x06000032 RID: 50 RVA: 0x00002688 File Offset: 0x00000888
		// (set) Token: 0x06000033 RID: 51 RVA: 0x00002690 File Offset: 0x00000890
		public string EmulatorlauncherUrl { get; set; }

		// Token: 0x17000018 RID: 24
		// (get) Token: 0x06000034 RID: 52 RVA: 0x00002699 File Offset: 0x00000899
		// (set) Token: 0x06000035 RID: 53 RVA: 0x000026A1 File Offset: 0x000008A1
		public string BiosGitUrl { get; set; }

		// Token: 0x17000019 RID: 25
		// (get) Token: 0x06000036 RID: 54 RVA: 0x000026AA File Offset: 0x000008AA
		// (set) Token: 0x06000037 RID: 55 RVA: 0x000026B2 File Offset: 0x000008B2
		public string ThemePath { get; set; }

		// Token: 0x1700001A RID: 26
		// (get) Token: 0x06000038 RID: 56 RVA: 0x000026BB File Offset: 0x000008BB
		// (set) Token: 0x06000039 RID: 57 RVA: 0x000026C3 File Offset: 0x000008C3
		public string DecorationsPath { get; set; }

		// Token: 0x1700001B RID: 27
		// (get) Token: 0x0600003A RID: 58 RVA: 0x000026CC File Offset: 0x000008CC
		// (set) Token: 0x0600003B RID: 59 RVA: 0x000026D4 File Offset: 0x000008D4
		public string SystemPath { get; set; }

		// Token: 0x1700001C RID: 28
		// (get) Token: 0x0600003C RID: 60 RVA: 0x000026DD File Offset: 0x000008DD
		// (set) Token: 0x0600003D RID: 61 RVA: 0x000026E5 File Offset: 0x000008E5
		public string RetroArchURL { get; set; }

		// Token: 0x1700001D RID: 29
		// (get) Token: 0x0600003E RID: 62 RVA: 0x000026EE File Offset: 0x000008EE
		// (set) Token: 0x0600003F RID: 63 RVA: 0x000026F6 File Offset: 0x000008F6
		public string WiimoteGunURL { get; set; }

		// Token: 0x1700001E RID: 30
		// (get) Token: 0x06000040 RID: 64 RVA: 0x000026FF File Offset: 0x000008FF
		// (set) Token: 0x06000041 RID: 65 RVA: 0x00002707 File Offset: 0x00000907
		public string BatGUIURL { get; set; }

		public int ZipCompressionLevel { get; set; }

		public bool Use7ZipForArchive { get; set; }

		public bool SkipZipSha256 { get; set; }

		public bool SkipRecreateZipIfExists { get; set; }

		public string ArchiveOutputPath { get; set; }

		public bool AskArchiveOutputDrive { get; set; }

		public string ArchiveOutputDirectory { get; set; }

		// Token: 0x06000042 RID: 66 RVA: 0x00002710 File Offset: 0x00000910
		public static BuilderOptions LoadBuilderOptions(string iniFile)
		{
			IniParser iniParser = new IniParser(iniFile);
			BuilderOptions builderOptions = new BuilderOptions();
			string text = "BuilderOptions";
			builderOptions.RetrobatVersion = iniParser.Get(text, "retrobat_version", "");
			builderOptions.RetroarchVersion = iniParser.Get(text, "retroarch_version", "");
			builderOptions.Branch = iniParser.Get(text, "branch", "stable");
			builderOptions.Architecture = iniParser.Get(text, "architecture", "win64");
			builderOptions.GetBatgui = iniParser.Get(text, "get_batgui", "") == "1";
			builderOptions.GetBatoceraPorts = iniParser.Get(text, "get_batocera_ports", "") == "1";
			builderOptions.GetBios = iniParser.Get(text, "get_bios", "") == "1";
			builderOptions.GetDecorations = iniParser.Get(text, "get_decorations", "") == "1";
			builderOptions.GetDefaultTheme = iniParser.Get(text, "get_default_theme", "") == "1";
			builderOptions.GetEmulationstation = iniParser.Get(text, "get_emulationstation", "") == "1";
			builderOptions.GetEmulators = iniParser.Get(text, "get_emulators", "") == "1";
			builderOptions.GetLrcores = iniParser.Get(text, "get_lrcores", "") == "1";
			builderOptions.GetRetroarch = iniParser.Get(text, "get_retroarch", "") == "1";
			builderOptions.GetRetrobatBinaries = iniParser.Get(text, "get_retrobat_binaries", "") == "1";
			builderOptions.GetSystem = iniParser.Get(text, "get_system", "") == "1";
			builderOptions.GetWiimotegun = iniParser.Get(text, "get_wiimotegun", "") == "1";
			builderOptions.SevenZipPath = Methods.PathCombineExeDir(iniParser.Get(text, "7za_path", "system\\tools\\7za.exe"));
			builderOptions.WgetPath = Methods.PathCombineExeDir(iniParser.Get(text, "wget_path", "system\\tools\\wget.exe"));
			builderOptions.CurlPath = Methods.PathCombineExeDir(iniParser.Get(text, "curl_path", "system\\tools\\curl.exe"));
			builderOptions.RetrobatFTPPath = iniParser.Get(text, "retrobat_ftp", "http://www.retrobat.ovh/repo/");
			builderOptions.RetrobatBinariesBaseUrl = iniParser.Get(text, "retrobat_binaries_url", "https://github.com/luziellacerda/TurboramaBinarios/releases/download/");
			builderOptions.EmulationstationUrl = iniParser.Get(text, "emulationstation_url", "https://github.com/luziellacerda/TurboramaEmulationStation/releases/download/continuous-master/");
			builderOptions.EmulatorlauncherUrl = iniParser.Get(text, "emulatorlauncher_url", "https://github.com/RetroBat-Official/emulatorlauncher/releases/download/continuous/");
			builderOptions.BiosGitUrl = iniParser.Get(text, "bios_git_url", "https://github.com/RetroBat-Official/retrobat-bios");
			builderOptions.ThemePath = iniParser.Get(text, "theme_path", "https://github.com/luziellacerda/PC-RETRO-LZ-THEME-PC-NEW");
			builderOptions.DecorationsPath = iniParser.Get(text, "decorations_path", "https://github.com/RetroBat-Official/retrobat-bezels");
			builderOptions.SystemPath = iniParser.Get(text, "retrobat_system_path", "");
			if (string.IsNullOrWhiteSpace(builderOptions.SystemPath))
			{
				builderOptions.SystemPath = iniParser.Get(text, "system_path", "https://github.com/RetroBat-Official/retrobat-setup/tree/master/system");
			}
			builderOptions.RetroArchURL = iniParser.Get(text, "retroarch_url", "https://buildbot.libretro.com");
			builderOptions.WiimoteGunURL = iniParser.Get(text, "wiimotegun_url", "https://github.com/fabricecaruso/WiimoteGun/releases/download/v1.1/WiimoteGun.zip");
			builderOptions.BatGUIURL = iniParser.Get(text, "batgui_url", "https://reppa.internet-box.ch/BatGui/NewBatGui/lastest.zip");
			builderOptions.ZipCompressionLevel = ParseZipCompressionLevel(iniParser.Get(text, "zip_compression_level", "1"));
			builderOptions.Use7ZipForArchive = iniParser.Get(text, "use_7zip_for_archive", "1") == "1";
			builderOptions.SkipZipSha256 = iniParser.Get(text, "skip_zip_sha256", "1") == "1";
			builderOptions.SkipRecreateZipIfExists = iniParser.Get(text, "skip_recreate_zip_if_exists", "1") == "1";
			builderOptions.ArchiveOutputPath = iniParser.Get(text, "archive_output_path", "");
			builderOptions.AskArchiveOutputDrive = iniParser.Get(text, "ask_archive_output_drive", "1") == "1";
			return builderOptions;
		}

		private static int ParseZipCompressionLevel(string value)
		{
			int level;
			if (!int.TryParse(value, out level))
			{
				return 1;
			}

			if (level < 0)
			{
				return 0;
			}

			if (level > 9)
			{
				return 9;
			}

			return level;
		}

		// Token: 0x06000043 RID: 67 RVA: 0x00002A68 File Offset: 0x00000C68
		public static bool IsComponentEnabled(string key, BuilderOptions options)
		{
			if (key != null)
			{
				switch (key.Length)
				{
				case 4:
					if (key == "bios")
					{
						return options.GetBios;
					}
					break;
				case 9:
					if (key == "retroarch")
					{
						return options.GetRetroarch;
					}
					break;
				case 10:
					if (key == "wiimotegun")
					{
						return options.GetWiimotegun;
					}
					break;
				case 11:
					if (key == "decorations")
					{
						return options.GetDecorations;
					}
					break;
				case 13:
					if (key == "default_theme")
					{
						return options.GetDefaultTheme;
					}
					break;
				case 14:
					if (key == "batocera_ports")
					{
						return options.GetBatoceraPorts;
					}
					break;
				case 16:
					if (key == "emulationstation")
					{
						return options.GetEmulationstation;
					}
					break;
				case 17:
					if (key == "retrobat_binaries")
					{
						return options.GetRetrobatBinaries;
					}
					break;
				}
			}
			return false;
		}
	}
}
