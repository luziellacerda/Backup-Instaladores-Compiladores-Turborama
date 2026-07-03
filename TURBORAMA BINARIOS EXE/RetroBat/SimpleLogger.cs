using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace RetroBat
{
	// Token: 0x0200000A RID: 10
	public class SimpleLogger
	{
		// Token: 0x17000020 RID: 32
		// (get) Token: 0x0600007C RID: 124 RVA: 0x00004DC8 File Offset: 0x00002FC8
		public static SimpleLogger Instance
		{
			get
			{
				if (SimpleLogger._instance == null)
				{
					SimpleLogger._instance = new SimpleLogger();
				}
				return SimpleLogger._instance;
			}
		}

		// Token: 0x0600007D RID: 125 RVA: 0x00004DE0 File Offset: 0x00002FE0
		private SimpleLogger()
		{
			this.datetimeFormat = "yyyy-MM-dd HH:mm:ss.fff";
			string fileName = Process.GetCurrentProcess().MainModule.FileName;
			this.logFilename = Path.Combine(Path.GetDirectoryName(fileName), Path.GetFileNameWithoutExtension(fileName) + ".log");
			if (File.Exists(this.logFilename) && new FileInfo(this.logFilename).Length > 1048576L)
			{
				string text = this.logFilename + ".old";
				if (File.Exists(text))
				{
					File.Delete(text);
				}
				File.Move(this.logFilename, text);
			}
		}

		// Token: 0x0600007E RID: 126 RVA: 0x00004E7F File Offset: 0x0000307F
		public void Debug(string text)
		{
			this.WriteFormattedLog(SimpleLogger.LogLevel.DEBUG, text, null);
		}

		// Token: 0x0600007F RID: 127 RVA: 0x00004E8A File Offset: 0x0000308A
		public void Error(string text, Exception ex = null)
		{
			this.WriteFormattedLog(SimpleLogger.LogLevel.ERROR, text, ex);
		}

		// Token: 0x06000080 RID: 128 RVA: 0x00004E95 File Offset: 0x00003095
		public void Fatal(string text)
		{
			this.WriteFormattedLog(SimpleLogger.LogLevel.FATAL, text, null);
		}

		// Token: 0x06000081 RID: 129 RVA: 0x00004EA0 File Offset: 0x000030A0
		public void Info(string text)
		{
			this.WriteFormattedLog(SimpleLogger.LogLevel.INFO, text, null);
		}

		// Token: 0x06000082 RID: 130 RVA: 0x00004EAB File Offset: 0x000030AB
		public void Trace(string text)
		{
			this.WriteFormattedLog(SimpleLogger.LogLevel.TRACE, text, null);
		}

		// Token: 0x06000083 RID: 131 RVA: 0x00004EB6 File Offset: 0x000030B6
		public void Warning(string text)
		{
			this.WriteFormattedLog(SimpleLogger.LogLevel.WARNING, text, null);
		}

		// Token: 0x06000084 RID: 132 RVA: 0x00004EC4 File Offset: 0x000030C4
		private void WriteLine(string text, bool append = true)
		{
			int num = 0;
			for (;;)
			{
				try
				{
					using (StreamWriter streamWriter = new StreamWriter(this.logFilename, append, Encoding.UTF8))
					{
						if (!string.IsNullOrEmpty(text))
						{
							streamWriter.WriteLine(text);
						}
					}
				}
				catch (IOException ex)
				{
					num++;
					if (num < 5)
					{
						Thread.Sleep(5 * num);
						continue;
					}
					throw ex;
				}
				catch
				{
					throw;
				}
				break;
			}
		}

		// Token: 0x06000085 RID: 133 RVA: 0x00004F44 File Offset: 0x00003144
		private void WriteFormattedLog(SimpleLogger.LogLevel level, string text, Exception exception = null)
		{
			string text2;
			switch (level)
			{
			case SimpleLogger.LogLevel.TRACE:
				text2 = DateTime.Now.ToString(this.datetimeFormat) + " [TRACE]     ";
				break;
			case SimpleLogger.LogLevel.INFO:
				text2 = DateTime.Now.ToString(this.datetimeFormat) + " [INFO]      ";
				break;
			case SimpleLogger.LogLevel.DEBUG:
				text2 = DateTime.Now.ToString(this.datetimeFormat) + " [DEBUG]     ";
				break;
			case SimpleLogger.LogLevel.WARNING:
				text2 = DateTime.Now.ToString(this.datetimeFormat) + " [WARNING]   ";
				break;
			case SimpleLogger.LogLevel.ERROR:
				text2 = DateTime.Now.ToString(this.datetimeFormat) + " [ERROR]     ";
				break;
			case SimpleLogger.LogLevel.FATAL:
				text2 = DateTime.Now.ToString(this.datetimeFormat) + " [FATAL]     ";
				break;
			default:
				text2 = "";
				break;
			}
			this.WriteLine(text2 + text, true);
			for (Exception ex = exception; ex != null; ex = ex.InnerException)
			{
				this.WriteLine(string.Concat(new string[]
				{
					DateTime.Now.ToString(this.datetimeFormat),
					" [EXCEPTION] [",
					ex.GetType().Name,
					"] ",
					ex.Message
				}), true);
			}
			if (level == SimpleLogger.LogLevel.ERROR && exception != null && !string.IsNullOrEmpty(exception.StackTrace))
			{
				this.WriteLine(DateTime.Now.ToString(this.datetimeFormat) + " [STACK]     [StackTrace] " + exception.StackTrace.Trim(), true);
			}
		}

		// Token: 0x0400003A RID: 58
		private static SimpleLogger _instance;

		// Token: 0x0400003B RID: 59
		private const string FILE_EXT = ".log";

		// Token: 0x0400003C RID: 60
		private readonly string datetimeFormat;

		// Token: 0x0400003D RID: 61
		private readonly string logFilename;

		// Token: 0x0200001D RID: 29
		[Flags]
		private enum LogLevel
		{
			// Token: 0x04000089 RID: 137
			TRACE = 0,
			// Token: 0x0400008A RID: 138
			INFO = 1,
			// Token: 0x0400008B RID: 139
			DEBUG = 2,
			// Token: 0x0400008C RID: 140
			WARNING = 3,
			// Token: 0x0400008D RID: 141
			ERROR = 4,
			// Token: 0x0400008E RID: 142
			FATAL = 5
		}
	}
}
