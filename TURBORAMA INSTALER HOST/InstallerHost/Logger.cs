using System;
using System.Diagnostics;
using System.IO;

// Token: 0x02000002 RID: 2
public static class Logger
{
	// Token: 0x06000002 RID: 2 RVA: 0x00002078 File Offset: 0x00000278
	public static void Log(string message)
	{
		try
		{
			string text = string.Format("{0:yyyy-MM-dd HH:mm:ss} - {1}", DateTime.Now, message);
			File.AppendAllText(Logger.logFilePath, text + Environment.NewLine);
		}
		catch
		{
		}
	}

	// Token: 0x04000001 RID: 1
	private static readonly string logFilePath = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "turborama-install.log");
}
