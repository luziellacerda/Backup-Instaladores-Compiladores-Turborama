using System;

namespace RetroBat
{
	// Token: 0x02000006 RID: 6
	public class RetroBatConfig
	{
		// Token: 0x17000004 RID: 4
		// (get) Token: 0x0600002B RID: 43 RVA: 0x00002CB9 File Offset: 0x00000EB9
		// (set) Token: 0x0600002C RID: 44 RVA: 0x00002CC1 File Offset: 0x00000EC1
		public bool LanguageDetection { get; set; }

		// Token: 0x17000005 RID: 5
		// (get) Token: 0x0600002D RID: 45 RVA: 0x00002CCA File Offset: 0x00000ECA
		// (set) Token: 0x0600002E RID: 46 RVA: 0x00002CD2 File Offset: 0x00000ED2
		public bool ResetConfigMode { get; set; }

		// Token: 0x17000006 RID: 6
		// (get) Token: 0x0600002F RID: 47 RVA: 0x00002CDB File Offset: 0x00000EDB
		// (set) Token: 0x06000030 RID: 48 RVA: 0x00002CE3 File Offset: 0x00000EE3
		public int Autostart { get; set; }

		// Token: 0x17000007 RID: 7
		// (get) Token: 0x06000031 RID: 49 RVA: 0x00002CEC File Offset: 0x00000EEC
		// (set) Token: 0x06000032 RID: 50 RVA: 0x00002CF4 File Offset: 0x00000EF4
		public int AutoStartDelay { get; set; }

		// Token: 0x17000008 RID: 8
		// (get) Token: 0x06000033 RID: 51 RVA: 0x00002CFD File Offset: 0x00000EFD
		// (set) Token: 0x06000034 RID: 52 RVA: 0x00002D05 File Offset: 0x00000F05
		public bool WiimoteGun { get; set; }

		// Token: 0x17000009 RID: 9
		// (get) Token: 0x06000035 RID: 53 RVA: 0x00002D0E File Offset: 0x00000F0E
		// (set) Token: 0x06000036 RID: 54 RVA: 0x00002D16 File Offset: 0x00000F16
		public bool EnableIntro { get; set; }

		// Token: 0x1700000A RID: 10
		// (get) Token: 0x06000037 RID: 55 RVA: 0x00002D1F File Offset: 0x00000F1F
		// (set) Token: 0x06000038 RID: 56 RVA: 0x00002D27 File Offset: 0x00000F27
		public string FileName { get; set; }

		// Token: 0x1700000B RID: 11
		// (get) Token: 0x06000039 RID: 57 RVA: 0x00002D30 File Offset: 0x00000F30
		// (set) Token: 0x0600003A RID: 58 RVA: 0x00002D38 File Offset: 0x00000F38
		public string FilePath { get; set; }

		// Token: 0x1700000C RID: 12
		// (get) Token: 0x0600003B RID: 59 RVA: 0x00002D41 File Offset: 0x00000F41
		// (set) Token: 0x0600003C RID: 60 RVA: 0x00002D49 File Offset: 0x00000F49
		public bool RandomVideo { get; set; }

		// Token: 0x1700000D RID: 13
		// (get) Token: 0x0600003D RID: 61 RVA: 0x00002D52 File Offset: 0x00000F52
		// (set) Token: 0x0600003E RID: 62 RVA: 0x00002D5A File Offset: 0x00000F5A
		public int VideoDelay { get; set; }

		// Token: 0x1700000E RID: 14
		// (get) Token: 0x0600003F RID: 63 RVA: 0x00002D63 File Offset: 0x00000F63
		// (set) Token: 0x06000040 RID: 64 RVA: 0x00002D6B File Offset: 0x00000F6B
		public bool KillVideoWhenESReady { get; set; }

		// Token: 0x1700000F RID: 15
		// (get) Token: 0x06000041 RID: 65 RVA: 0x00002D74 File Offset: 0x00000F74
		// (set) Token: 0x06000042 RID: 66 RVA: 0x00002D7C File Offset: 0x00000F7C
		public bool WaitForVideoEnd { get; set; }

		// Token: 0x17000010 RID: 16
		// (get) Token: 0x06000043 RID: 67 RVA: 0x00002D85 File Offset: 0x00000F85
		// (set) Token: 0x06000044 RID: 68 RVA: 0x00002D8D File Offset: 0x00000F8D
		public bool GamepadVideoKill { get; set; }

		// Token: 0x17000011 RID: 17
		// (get) Token: 0x06000045 RID: 69 RVA: 0x00002D96 File Offset: 0x00000F96
		// (set) Token: 0x06000046 RID: 70 RVA: 0x00002D9E File Offset: 0x00000F9E
		public bool Fullscreen { get; set; }

		// Token: 0x17000012 RID: 18
		// (get) Token: 0x06000047 RID: 71 RVA: 0x00002DA7 File Offset: 0x00000FA7
		// (set) Token: 0x06000048 RID: 72 RVA: 0x00002DAF File Offset: 0x00000FAF
		public bool FullscreenBorderless { get; set; }

		// Token: 0x17000013 RID: 19
		// (get) Token: 0x06000049 RID: 73 RVA: 0x00002DB8 File Offset: 0x00000FB8
		// (set) Token: 0x0600004A RID: 74 RVA: 0x00002DC0 File Offset: 0x00000FC0
		public bool ForceFullscreenRes { get; set; }

		// Token: 0x17000014 RID: 20
		// (get) Token: 0x0600004B RID: 75 RVA: 0x00002DC9 File Offset: 0x00000FC9
		// (set) Token: 0x0600004C RID: 76 RVA: 0x00002DD1 File Offset: 0x00000FD1
		public int FocusDelay { get; set; }

		// Token: 0x17000015 RID: 21
		// (get) Token: 0x0600004D RID: 77 RVA: 0x00002DDA File Offset: 0x00000FDA
		// (set) Token: 0x0600004E RID: 78 RVA: 0x00002DE2 File Offset: 0x00000FE2
		public bool GameListOnly { get; set; }

		// Token: 0x17000016 RID: 22
		// (get) Token: 0x0600004F RID: 79 RVA: 0x00002DEB File Offset: 0x00000FEB
		// (set) Token: 0x06000050 RID: 80 RVA: 0x00002DF3 File Offset: 0x00000FF3
		public int InterfaceMode { get; set; }

		// Token: 0x17000017 RID: 23
		// (get) Token: 0x06000051 RID: 81 RVA: 0x00002DFC File Offset: 0x00000FFC
		// (set) Token: 0x06000052 RID: 82 RVA: 0x00002E04 File Offset: 0x00001004
		public int MonitorIndex { get; set; }

		// Token: 0x17000018 RID: 24
		// (get) Token: 0x06000053 RID: 83 RVA: 0x00002E0D File Offset: 0x0000100D
		// (set) Token: 0x06000054 RID: 84 RVA: 0x00002E15 File Offset: 0x00001015
		public bool NoExitMenu { get; set; }

		// Token: 0x17000019 RID: 25
		// (get) Token: 0x06000055 RID: 85 RVA: 0x00002E1E File Offset: 0x0000101E
		// (set) Token: 0x06000056 RID: 86 RVA: 0x00002E26 File Offset: 0x00001026
		public bool OpenGL2_1 { get; set; }

		// Token: 0x1700001A RID: 26
		// (get) Token: 0x06000057 RID: 87 RVA: 0x00002E2F File Offset: 0x0000102F
		// (set) Token: 0x06000058 RID: 88 RVA: 0x00002E37 File Offset: 0x00001037
		public bool VSync { get; set; }

		// Token: 0x1700001B RID: 27
		// (get) Token: 0x06000059 RID: 89 RVA: 0x00002E40 File Offset: 0x00001040
		// (set) Token: 0x0600005A RID: 90 RVA: 0x00002E48 File Offset: 0x00001048
		public bool RandomTheme { get; set; }

		// Token: 0x1700001C RID: 28
		// (get) Token: 0x0600005B RID: 91 RVA: 0x00002E51 File Offset: 0x00001051
		// (set) Token: 0x0600005C RID: 92 RVA: 0x00002E59 File Offset: 0x00001059
		public bool DrawFramerate { get; set; }

		// Token: 0x1700001D RID: 29
		// (get) Token: 0x0600005D RID: 93 RVA: 0x00002E62 File Offset: 0x00001062
		// (set) Token: 0x0600005E RID: 94 RVA: 0x00002E6A File Offset: 0x0000106A
		public int WindowXSize { get; set; }

		// Token: 0x1700001E RID: 30
		// (get) Token: 0x0600005F RID: 95 RVA: 0x00002E73 File Offset: 0x00001073
		// (set) Token: 0x06000060 RID: 96 RVA: 0x00002E7B File Offset: 0x0000107B
		public int WindowYSize { get; set; }
	}
}
