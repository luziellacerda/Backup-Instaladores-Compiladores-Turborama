using System;
using System.IO;
using System.Reflection;

namespace RetroBuild
{
	// Token: 0x02000005 RID: 5
	internal class Logger
	{
		// Token: 0x06000045 RID: 69 RVA: 0x00002B89 File Offset: 0x00000D89
		static Logger()
		{
			if (File.Exists(Logger.logFilePath))
			{
				File.Delete(Logger.logFilePath);
			}
		}

		// Token: 0x06000046 RID: 70 RVA: 0x00002BC0 File Offset: 0x00000DC0
		public static void Log(string message)
		{
			string text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message;
			Console.WriteLine(text);
			File.AppendAllText(Logger.logFilePath, text + Environment.NewLine);
		}

		// Token: 0x06000047 RID: 71 RVA: 0x00002C06 File Offset: 0x00000E06
		public static void LogLabel(string label)
		{
			Logger.Log("[LABEL] :" + label);
		}

		// Token: 0x06000048 RID: 72 RVA: 0x00002C18 File Offset: 0x00000E18
		public static void LogInfo(string message)
		{
			Logger.Log("[INFO] " + message);
		}

		// Token: 0x06000049 RID: 73 RVA: 0x00002C2A File Offset: 0x00000E2A
		public static void LogExit(int code)
		{
			Logger.Log(string.Format("[EXIT] {0}", code));
		}

		// Token: 0x0600004A RID: 74 RVA: 0x00002C41 File Offset: 0x00000E41
		public static void LogStart(string scriptName)
		{
			Logger.Log("[START] Run: " + scriptName);
		}

		// Token: 0x04000020 RID: 32
		private static readonly string logFilePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "build.log");
	}
}
