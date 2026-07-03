using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Media;

namespace RetroBat
{
	// Token: 0x0200000C RID: 12
	public class VideoPlayerForm : Form
	{
		// Token: 0x0600008C RID: 140 RVA: 0x00005460 File Offset: 0x00003660
		public VideoPlayerForm(string videoPath, string path, bool gamepadKill = false, bool killVideoWhenESReady = false, Screen targetScreen = null, bool externalLauncher = false)
		{
			this._externalLauncher = externalLauncher;
			this._gamepadKill = gamepadKill;
			this._letVideoRun = !killVideoWhenESReady;
			this._path = Path.Combine(path, ".emulationstation", "tmp", "emulationstation.ready");
			if (File.Exists(this._path))
			{
				try
				{
					File.Delete(this._path);
				}
				catch
				{
				}
			}
			Screen screen = targetScreen ?? Screen.PrimaryScreen;
			this.BackColor = global::System.Drawing.Color.Black;
			base.FormBorderStyle = FormBorderStyle.None;
			base.StartPosition = FormStartPosition.Manual;
			base.Bounds = screen.Bounds;
			base.ShowInTaskbar = false;
			base.TopMost = true;
			base.TopLevel = true;
			base.Opacity = 0.0;
			base.KeyPreview = true;
			base.WindowState = FormWindowState.Normal;
			this._elementHost = new ElementHost
			{
				Dock = DockStyle.Fill
			};
			this._mediaElement = new MediaElement
			{
				LoadedBehavior = MediaState.Manual,
				UnloadedBehavior = MediaState.Manual,
				Stretch = Stretch.Uniform,
				HorizontalAlignment = global::System.Windows.HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Source = new Uri(videoPath, UriKind.Absolute)
			};
			this._mediaElement.Focusable = false;
			this._mediaElement.MediaOpened += delegate(object s, RoutedEventArgs e)
			{
				this.ForceForeground();
				SimpleLogger.Instance.Info("Media opened.");
				base.Opacity = 1.0;
				base.TopMost = true;
				base.Activate();
				base.BringToFront();
			};
			this._mediaElement.MediaEnded += delegate(object s, RoutedEventArgs e)
			{
				SimpleLogger.Instance.Info("Media Ended.");
				this._mediaEnded = true;
				this._timer.Stop();
				base.Close();
			};
			this._mediaElement.MediaFailed += delegate(object s, ExceptionRoutedEventArgs e)
			{
				SimpleLogger instance = SimpleLogger.Instance;
				string text = "Media failed: ";
				Exception errorException = e.ErrorException;
				instance.Warning(text + ((errorException != null) ? errorException.Message : null));
				this._timer.Stop();
				base.Close();
			};
			this._elementHost.Dock = DockStyle.Fill;
			this._elementHost.Child = this._mediaElement;
			base.Controls.Add(this._elementHost);
			base.Load += delegate(object s, EventArgs e)
			{
				try
				{
					this.StartPosition = FormStartPosition.Manual;
					this.FormBorderStyle = FormBorderStyle.None;
					this.Bounds = screen.Bounds;
					this.WindowState = FormWindowState.Normal;
					Thread.Sleep(100);
					this._mediaElement.Play();
					SimpleLogger.Instance.Info("Video started.");
					this.TopMost = true;
					this.Activate();
					this.BringToFront();
					this._timer = new global::System.Windows.Forms.Timer
					{
						Interval = 50
					};
					this._timer.Tick += this.OnTimer;
					this._timer.Start();
				}
				catch (Exception ex)
				{
					SimpleLogger instance2 = SimpleLogger.Instance;
					string text2 = "MediaElement failed to launch";
					Exception ex2 = ex;
					instance2.Warning(text2 + ((ex2 != null) ? ex2.ToString() : null));
				}
			};
			base.Shown += delegate(object s, EventArgs e)
			{
				this.ForceForeground();
				base.TopMost = true;
				base.BringToFront();
			};
		}

		// Token: 0x0600008D RID: 141 RVA: 0x00005658 File Offset: 0x00003858
		private void OnTimer(object sender, EventArgs e)
		{
			bool flag = this._gamepadKill && XInput.IsFaceButtonPressed();
			bool flag2 = this.keysToCheck.Any<int>((int k) => VideoPlayerForm.GetAsyncKeyState(k) < 0);
			bool flag3 = File.Exists(this._path) && !this._letVideoRun;
			if (flag2 || flag || flag3)
			{
				if (flag)
				{
					SimpleLogger.Instance.Info("Gamepad input detected, killing video process.");
				}
				else if (flag2)
				{
					SimpleLogger.Instance.Info("Keyboard or mouse input detected. Killing video process.");
				}
				else if (flag3)
				{
					SimpleLogger.Instance.Info("EmulationStation ready. Killing video process.");
					Thread.Sleep(200);
				}
				global::System.Windows.Forms.Timer timer = this._timer;
				if (timer != null)
				{
					timer.Dispose();
				}
				this._timer = null;
				MediaElement mediaElement = this._mediaElement;
				if (mediaElement != null)
				{
					mediaElement.Stop();
				}
				SimpleLogger.Instance.Info("Video stopped.");
				MediaElement mediaElement2 = this._mediaElement;
				if (mediaElement2 != null)
				{
					mediaElement2.Close();
				}
				this._mediaElement = null;
				base.Close();
			}
		}

		// Token: 0x0600008E RID: 142 RVA: 0x00005760 File Offset: 0x00003960
		protected override void Dispose(bool disposing)
		{
			global::System.Windows.Forms.Timer timer = this._timer;
			if (timer != null)
			{
				timer.Dispose();
			}
			this._timer = null;
			base.Dispose(disposing);
			if (!this._externalLauncher)
			{
				for (int i = 0; i < 5; i++)
				{
					Process process = Process.GetProcessesByName("emulationstation").FirstOrDefault<Process>();
					if (process != null)
					{
						SimpleLogger.Instance.Info("Restoring focus to EmulationStation...");
						FocusHelper.BringProcessWindowToFront(process, 5, 250);
						return;
					}
					Thread.Sleep(300);
				}
			}
		}

		// Token: 0x0600008F RID: 143
		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		// Token: 0x06000090 RID: 144
		[DllImport("user32.dll")]
		private static extern bool SetActiveWindow(IntPtr hWnd);

		// Token: 0x06000091 RID: 145
		[DllImport("user32.dll")]
		private static extern IntPtr GetForegroundWindow();

		// Token: 0x06000092 RID: 146
		[DllImport("user32.dll")]
		private static extern short GetAsyncKeyState(int vKey);

		// Token: 0x06000093 RID: 147
		[DllImport("user32.dll")]
		private static extern bool AllowSetForegroundWindow(int dwProcessId);

		// Token: 0x06000094 RID: 148
		[DllImport("user32.dll")]
		private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		// Token: 0x06000095 RID: 149
		[DllImport("user32.dll")]
		private static extern bool BringWindowToTop(IntPtr hWnd);

		// Token: 0x06000096 RID: 150
		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

		// Token: 0x06000097 RID: 151
		[DllImport("kernel32.dll")]
		private static extern uint GetCurrentThreadId();

		// Token: 0x06000098 RID: 152
		[DllImport("user32.dll")]
		private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

		// Token: 0x06000099 RID: 153 RVA: 0x000057DC File Offset: 0x000039DC
		private void ForceForeground()
		{
			IntPtr handle = base.Handle;
			if (handle == IntPtr.Zero)
			{
				return;
			}
			IntPtr foregroundWindow = VideoPlayerForm.GetForegroundWindow();
			if (handle == foregroundWindow)
			{
				return;
			}
			uint num;
			uint windowThreadProcessId = VideoPlayerForm.GetWindowThreadProcessId(foregroundWindow, out num);
			uint currentThreadId = VideoPlayerForm.GetCurrentThreadId();
			VideoPlayerForm.AttachThreadInput(currentThreadId, windowThreadProcessId, true);
			VideoPlayerForm.ShowWindow(handle, 5);
			VideoPlayerForm.SetWindowPos(handle, VideoPlayerForm.HWND_TOPMOST, 0, 0, 0, 0, 67U);
			VideoPlayerForm.SetForegroundWindow(handle);
			VideoPlayerForm.SetActiveWindow(handle);
			VideoPlayerForm.BringWindowToTop(handle);
			VideoPlayerForm.AttachThreadInput(currentThreadId, windowThreadProcessId, false);
		}

		// Token: 0x0600009A RID: 154
		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

		// Token: 0x0600009B RID: 155 RVA: 0x0000585C File Offset: 0x00003A5C
		private void ToggleTopMost()
		{
			VideoPlayerForm.SetWindowPos(base.Handle, VideoPlayerForm.HWND_TOPMOST, 0, 0, 0, 0, 67U);
			Thread.Sleep(10);
			VideoPlayerForm.SetWindowPos(base.Handle, VideoPlayerForm.HWND_NOTOPMOST, 0, 0, 0, 0, 67U);
		}

		// Token: 0x0400003F RID: 63
		private ElementHost _elementHost;

		// Token: 0x04000040 RID: 64
		private MediaElement _mediaElement;

		// Token: 0x04000041 RID: 65
		private string _path;

		// Token: 0x04000042 RID: 66
		private bool _gamepadKill;

		// Token: 0x04000043 RID: 67
		private bool _letVideoRun;

		// Token: 0x04000044 RID: 68
		private bool _externalLauncher;

		// Token: 0x04000045 RID: 69
		public bool _mediaEnded;

		// Token: 0x04000046 RID: 70
		private global::System.Windows.Forms.Timer _timer;

		// Token: 0x04000047 RID: 71
		private const int SW_SHOW = 5;

		// Token: 0x04000048 RID: 72
		private const int VK_LBUTTON = 1;

		// Token: 0x04000049 RID: 73
		private const int VK_RBUTTON = 2;

		// Token: 0x0400004A RID: 74
		private const int VK_SPACE = 32;

		// Token: 0x0400004B RID: 75
		private const int VK_ESCAPE = 27;

		// Token: 0x0400004C RID: 76
		private const int VK_ENTER = 13;

		// Token: 0x0400004D RID: 77
		private const int VK_UP = 38;

		// Token: 0x0400004E RID: 78
		private const int VK_DOWN = 40;

		// Token: 0x0400004F RID: 79
		private const int VK_LEFT = 37;

		// Token: 0x04000050 RID: 80
		private const int VK_RIGHT = 39;

		// Token: 0x04000051 RID: 81
		private const int VK_W = 87;

		// Token: 0x04000052 RID: 82
		private const int VK_A = 65;

		// Token: 0x04000053 RID: 83
		private const int VK_S = 83;

		// Token: 0x04000054 RID: 84
		private const int VK_D = 68;

		// Token: 0x04000055 RID: 85
		private int[] keysToCheck = new int[]
		{
			1, 2, 32, 27, 13, 38, 40, 37, 39, 87,
			65, 83, 68
		};

		// Token: 0x04000056 RID: 86
		private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

		// Token: 0x04000057 RID: 87
		private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

		// Token: 0x04000058 RID: 88
		private const uint SWP_NOMOVE = 2U;

		// Token: 0x04000059 RID: 89
		private const uint SWP_NOSIZE = 1U;

		// Token: 0x0400005A RID: 90
		private const uint SWP_SHOWWINDOW = 64U;
	}
}
