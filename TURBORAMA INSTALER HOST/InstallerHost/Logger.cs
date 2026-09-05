using System;
using System.Collections.Generic;
using System.Diagnostics;

// Token: 0x02000002 RID: 2
public static class Logger
{
	private static readonly object Sync = new object();
	private static readonly Queue<string> RecentEntries = new Queue<string>();
	private const int MaximumEntries = 512;

	// Token: 0x06000002 RID: 2 RVA: 0x00002078 File Offset: 0x00000278
	public static void Log(string message)
	{
		try
		{
			string text = string.Format("{0:yyyy-MM-dd HH:mm:ss} - {1}", DateTime.Now, message);
			// This process is elevated. Never append to a path beside the setup,
			// where an unprivileged process could plant a link to another file.
			lock (Sync)
			{
				RecentEntries.Enqueue(text);
				while (RecentEntries.Count > MaximumEntries) RecentEntries.Dequeue();
			}
			Debug.WriteLine(text);
		}
		catch
		{
		}
	}

	public static string[] GetRecentEntries()
	{
		lock (Sync) return RecentEntries.ToArray();
	}
}
