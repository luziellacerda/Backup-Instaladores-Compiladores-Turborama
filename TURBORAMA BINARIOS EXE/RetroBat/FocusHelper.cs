using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace RetroBat
{
	// Token: 0x02000002 RID: 2
	internal class FocusHelper
	{
		// Token: 0x06000001 RID: 1
		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

		// Token: 0x06000002 RID: 2
		[DllImport("kernel32.dll")]
		private static extern uint GetCurrentThreadId();

		// Token: 0x06000003 RID: 3
		[DllImport("user32.dll")]
		private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

		// Token: 0x06000004 RID: 4
		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		// Token: 0x06000005 RID: 5
		[DllImport("user32.dll")]
		private static extern bool SetActiveWindow(IntPtr hWnd);

		// Token: 0x06000006 RID: 6
		[DllImport("user32.dll")]
		private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		// Token: 0x06000007 RID: 7
		[DllImport("user32.dll")]
		private static extern bool BringWindowToTop(IntPtr hWnd);

		// Token: 0x06000008 RID: 8
		[DllImport("user32.dll")]
		private static extern IntPtr GetForegroundWindow();

		// Token: 0x06000009 RID: 9
		[DllImport("user32.dll")]
		private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

		// Token: 0x0600000A RID: 10
		[DllImport("user32.dll")]
		private static extern bool GetWindowRect(IntPtr hWnd, out FocusHelper.RECT lpRect);

		// Token: 0x0600000B RID: 11
		[DllImport("user32.dll")]
		private static extern bool SetCursorPos(int x, int y);

		// Token: 0x0600000C RID: 12
		[DllImport("user32.dll")]
		private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

		// Token: 0x0600000D RID: 13
		[DllImport("user32.dll")]
		private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

		// Token: 0x0600000E RID: 14 RVA: 0x00002050 File Offset: 0x00000250
		public static bool BringProcessWindowToFront(Process proc, int attempts = 5, int delayMs = 300)
		{
			if (proc == null)
			{
				return false;
			}
			try
			{
				if (!proc.WaitForInputIdle(5000))
				{
					SimpleLogger.Instance.Warning("WaitForInputIdle timed out.");
				}
				for (int i = 0; i < attempts; i++)
				{
					proc.Refresh();
					IntPtr mainWindowHandle = proc.MainWindowHandle;
					if (mainWindowHandle == IntPtr.Zero)
					{
						SimpleLogger.Instance.Warning(string.Format("Attempt #{0}: Window handle not yet available.", i + 1));
						Thread.Sleep(delayMs);
					}
					else
					{
						if (FocusHelper.ForceForeground(mainWindowHandle))
						{
							SimpleLogger.Instance.Info(string.Format("Window brought to front on attempt #{0}.", i + 1));
							return true;
						}
						Thread.Sleep(delayMs);
					}
				}
			}
			catch (Exception ex)
			{
				SimpleLogger.Instance.Warning("BringProcessWindowToFront exception: " + ex.Message);
			}
			SimpleLogger.Instance.Warning("Failed to bring process window to front after retries.");
			return false;
		}

		// Token: 0x0600000F RID: 15 RVA: 0x00002138 File Offset: 0x00000338
		public static bool ForceForeground(IntPtr hWnd)
		{
			if (hWnd == IntPtr.Zero)
			{
				return false;
			}
			bool flag;
			try
			{
				if (FocusHelper.GetForegroundWindow() == hWnd)
				{
					SimpleLogger.Instance.Info("Window already in foreground.");
					flag = true;
				}
				else
				{
					uint currentThreadId = FocusHelper.GetCurrentThreadId();
					uint num;
					uint windowThreadProcessId = FocusHelper.GetWindowThreadProcessId(hWnd, out num);
					if (windowThreadProcessId == 0U)
					{
						flag = false;
					}
					else
					{
						FocusHelper.AttachThreadInput(currentThreadId, windowThreadProcessId, true);
						try
						{
							FocusHelper.ShowWindow(hWnd, 9);
							bool flag2 = FocusHelper.SetForegroundWindow(hWnd);
							FocusHelper.BringWindowToTop(hWnd);
							FocusHelper.SetActiveWindow(hWnd);
							if (!flag2)
							{
								FocusHelper.ToggleTopMost(hWnd);
								flag2 = FocusHelper.SetForegroundWindow(hWnd);
							}
							FocusHelper.PostMessage(hWnd, 6U, new IntPtr(1), IntPtr.Zero);
							FocusHelper.PostMessage(hWnd, 7U, IntPtr.Zero, IntPtr.Zero);
							Thread.Sleep(50);
							FocusHelper.SendSyntheticClick(hWnd);
							SimpleLogger.Instance.Info(string.Format("ForceForeground result = {0}", flag2));
							flag = flag2;
						}
						finally
						{
							FocusHelper.AttachThreadInput(currentThreadId, windowThreadProcessId, false);
						}
					}
				}
			}
			catch (Exception ex)
			{
				SimpleLogger.Instance.Warning("ForceForeground exception: " + ex.Message);
				flag = false;
			}
			return flag;
		}

		// Token: 0x06000010 RID: 16 RVA: 0x00002268 File Offset: 0x00000468
		private static void SendSyntheticClick(IntPtr hWnd)
		{
			try
			{
				FocusHelper.RECT rect;
				if (FocusHelper.GetWindowRect(hWnd, out rect))
				{
					int num = (rect.Left + rect.Right) / 2;
					int num2 = (rect.Top + rect.Bottom) / 2;
					FocusHelper.SetCursorPos(num, num2);
					FocusHelper.mouse_event(2U, 0, 0, 0U, 0);
					Thread.Sleep(10);
					FocusHelper.mouse_event(4U, 0, 0, 0U, 0);
					SimpleLogger.Instance.Info(string.Format("Synthetic click sent to window center ({0},{1}).", num, num2));
				}
			}
			catch (Exception ex)
			{
				SimpleLogger.Instance.Warning("SendSyntheticClick exception: " + ex.Message);
			}
		}

		// Token: 0x06000011 RID: 17 RVA: 0x00002318 File Offset: 0x00000518
		public static void ToggleTopMost(IntPtr hWnd)
		{
			FocusHelper.SetWindowPos(hWnd, FocusHelper.HWND_TOPMOST, 0, 0, 0, 0, 67U);
			Thread.Sleep(50);
			FocusHelper.SetWindowPos(hWnd, FocusHelper.HWND_NOTOPMOST, 0, 0, 0, 0, 67U);
		}

		// Token: 0x04000001 RID: 1
		private const uint MOUSEEVENTF_LEFTDOWN = 2U;

		// Token: 0x04000002 RID: 2
		private const uint MOUSEEVENTF_LEFTUP = 4U;

		// Token: 0x04000003 RID: 3
		private const uint WM_ACTIVATE = 6U;

		// Token: 0x04000004 RID: 4
		private const uint WM_SETFOCUS = 7U;

		// Token: 0x04000005 RID: 5
		private const int WA_ACTIVE = 1;

		// Token: 0x04000006 RID: 6
		private const int SW_RESTORE = 9;

		// Token: 0x04000007 RID: 7
		private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

		// Token: 0x04000008 RID: 8
		private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

		// Token: 0x04000009 RID: 9
		private const uint SWP_NOMOVE = 2U;

		// Token: 0x0400000A RID: 10
		private const uint SWP_NOSIZE = 1U;

		// Token: 0x0400000B RID: 11
		private const uint SWP_SHOWWINDOW = 64U;

		// Token: 0x0200000F RID: 15
		public struct RECT
		{
			// Token: 0x0400006A RID: 106
			public int Left;

			// Token: 0x0400006B RID: 107
			public int Top;

			// Token: 0x0400006C RID: 108
			public int Right;

			// Token: 0x0400006D RID: 109
			public int Bottom;
		}
	}
}
