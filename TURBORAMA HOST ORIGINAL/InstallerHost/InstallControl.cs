using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Allegoria.Controls;
using ICSharpCode.SharpZipLib.Zip;
using InstallerHost.Properties;

namespace InstallerHost
{
	// Token: 0x02000007 RID: 7
	public partial class InstallControl : UserControl
	{
		// Token: 0x06000018 RID: 24 RVA: 0x00002F1C File Offset: 0x0000111C
		public InstallControl(MainForm main)
		{
			this.mainForm = main;
			this.InitializeComponent();
			this.wizardHeader.Text = Texts.GetString("InstallTitle", Array.Empty<object>());
			this.txtInfo.Text = Texts.GetString("InstallInfo", Array.Empty<object>());
			this.lblSelectFolder.Text = Texts.GetString("SelectFolder", Array.Empty<object>());
			this.btnBrowse.Text = Texts.GetString("Browse...", Array.Empty<object>());
			this.lblFolderHint.Text = Texts.GetString("InstallFolderHint", Array.Empty<object>());
			this.btnCancel.Text = Texts.GetString("Cancel", Array.Empty<object>());
			this.btnInstall.Text = Texts.GetString("Install", Array.Empty<object>());
			this.btnBack.Text = Texts.GetString("< Back", Array.Empty<object>());
		}

		// Token: 0x0600001A RID: 26 RVA: 0x000036AC File Offset: 0x000018AC
		private void BtnBack_Click(object sender, EventArgs e)
		{
			this.mainForm.ShowPrerequisites(false);
		}

		// Token: 0x0600001B RID: 27 RVA: 0x000036BA File Offset: 0x000018BA
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			this.txtFolder.Text = "C:\\RetroBat";
			base.ActiveControl = this.btnInstall;
		}

		// Token: 0x0600001C RID: 28 RVA: 0x000036E0 File Offset: 0x000018E0
		private void BtnBrowse_Click(object sender, EventArgs e)
		{
			using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
			{
				if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
				{
					this.txtFolder.Text = folderBrowserDialog.SelectedPath;
				}
			}
		}

		// Token: 0x0600001D RID: 29 RVA: 0x0000372C File Offset: 0x0000192C
		private void BtnInstall_Click(object sender, EventArgs e)
		{
			if (string.IsNullOrWhiteSpace(this.txtFolder.Text))
			{
				Logger.Log("[WARNING] No installation folder selected.");
				MessageBox.Show(Texts.GetString("ValidFolder", Array.Empty<object>()));
				return;
			}
			string text = this.txtFolder.Text;
			if (Directory.Exists(text))
			{
				if (Directory.EnumerateFileSystemEntries(text).Any<string>())
				{
					Logger.Log("[WARNING] Installation folder not empty.");
				}
			}
			else
			{
				try
				{
					Logger.Log("[INFO] Creating installation folder.");
					Directory.CreateDirectory(text);
				}
				catch (Exception ex)
				{
					Logger.Log("[WARNING] Unable to create installation folder.");
					MessageBox.Show(Texts.GetString("FailedFolder", Array.Empty<object>()) + ex.Message);
					return;
				}
			}
			this.progressBar.Visible = true;
			this.btnBack.Enabled = false;
			this.btnInstall.Enabled = false;
			this.btnBrowse.Enabled = false;
			this.worker = new BackgroundWorker
			{
				WorkerReportsProgress = true
			};
			this.worker.DoWork += delegate(object workSender, DoWorkEventArgs workArgs)
			{
				try
				{
					string text2 = this.txtFolder.Text;
					using (Stream embeddedZipStream = this.GetEmbeddedZipStream())
					{
						this.ExtractZipStreamToFolder(embeddedZipStream, text2);
					}
				}
				catch (Exception ex2)
				{
					workArgs.Result = ex2;
				}
			};
			this.worker.ProgressChanged += delegate(object progressSender, ProgressChangedEventArgs progressArgs)
			{
				if (this.progressBar.InvokeRequired)
				{
					this.progressBar.Invoke(new Action(delegate
					{
						this.progressBar.Value = progressArgs.ProgressPercentage;
					}));
					return;
				}
				this.progressBar.Value = progressArgs.ProgressPercentage;
			};
			this.worker.RunWorkerCompleted += delegate(object completeSender, RunWorkerCompletedEventArgs completeArgs)
			{
				this.btnInstall.Enabled = true;
				this.btnBrowse.Enabled = true;
				this.btnBack.Enabled = true;
				this.progressBar.Visible = false;
				Exception ex3 = completeArgs.Result as Exception;
				if (ex3 != null)
				{
					MessageBox.Show(this, Texts.GetString("InstallFail", Array.Empty<object>()) + ex3.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Hand);
					return;
				}
				Logger.Log("Installation successful, showing finish screen.");
				this.mainForm.ShowFinish(this.txtFolder.Text);
			};
			this.worker.RunWorkerAsync();
		}

		// Token: 0x0600001E RID: 30 RVA: 0x00003880 File Offset: 0x00001A80
		public void BtnCancel_Click(object sender, EventArgs e)
		{
			if (MessageBox.Show(Texts.GetString("CancelSure", Array.Empty<object>()), Texts.GetString("CancelButtonTitle", Array.Empty<object>()), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				Application.Exit();
			}
		}

		// Token: 0x0600001F RID: 31 RVA: 0x000038B0 File Offset: 0x00001AB0
		private Stream GetEmbeddedZipStream()
		{
			FileStream fileStream = new FileStream(Application.ExecutablePath, FileMode.Open, FileAccess.Read);
			if (fileStream.Length < 8L)
			{
				throw new Exception("Invalid installer: file too small.");
			}
			fileStream.Seek(-8L, SeekOrigin.End);
			byte[] array = new byte[8];
			if (fileStream.Read(array, 0, 8) != 8)
			{
				throw new Exception("Failed to read zip length footer.");
			}
			long num = BitConverter.ToInt64(array, 0);
			long num2 = fileStream.Length - num - 8L;
			if (num <= 0L || num2 < 0L)
			{
				throw new Exception("Invalid ZIP length in installer footer.");
			}
			fileStream.Seek(num2, SeekOrigin.Begin);
			return new InstallControl.SubStream(fileStream, num2, num);
		}

		// Token: 0x06000020 RID: 32 RVA: 0x00003940 File Offset: 0x00001B40
		private void ExtractZipStreamToFolder(Stream fs, string destinationFolder)
		{
			using (ZipFile zipFile = new ZipFile(fs))
			{
				zipFile.IsStreamOwner = true;
				zipFile.UseZip64 = 1;
				long num = (from ZipEntry e in zipFile
					where e.IsFile
					select e).Sum<ZipEntry>((ZipEntry e) => e.Size);
				long num2 = 0L;
				int num3 = -1;
				foreach (object obj in zipFile)
				{
					ZipEntry zipEntry = (ZipEntry)obj;
					string name = zipEntry.Name;
					string text = Path.Combine(destinationFolder, name);
					if (zipEntry.IsDirectory)
					{
						Directory.CreateDirectory(text);
					}
					else
					{
						string directoryName = Path.GetDirectoryName(text);
						if (!string.IsNullOrEmpty(directoryName))
						{
							Directory.CreateDirectory(directoryName);
						}
						using (Stream inputStream = zipFile.GetInputStream(zipEntry))
						{
							using (FileStream fileStream = File.Create(text))
							{
								byte[] array = new byte[8192];
								int num4;
								while ((num4 = inputStream.Read(array, 0, array.Length)) > 0)
								{
									fileStream.Write(array, 0, num4);
									num2 += (long)num4;
									if (num > 0L)
									{
										int num5 = (int)(num2 * 100L / num);
										if (num5 != num3)
										{
											BackgroundWorker backgroundWorker = this.worker;
											if (backgroundWorker != null)
											{
												backgroundWorker.ReportProgress(num5);
											}
											num3 = num5;
										}
									}
								}
							}
						}
					}
				}
			}
		}

		// Token: 0x04000018 RID: 24
		private MainForm mainForm;

		// Token: 0x04000019 RID: 25
		private BackgroundWorker worker;

		// Token: 0x02000012 RID: 18
		private class SubStream : Stream
		{
			// Token: 0x0600006C RID: 108 RVA: 0x00008A1E File Offset: 0x00006C1E
			public SubStream(Stream baseStream, long start, long length)
			{
				this._baseStream = baseStream;
				this._start = start;
				this._length = length;
				this._position = 0L;
				this._baseStream.Seek(this._start, SeekOrigin.Begin);
			}

			// Token: 0x17000010 RID: 16
			// (get) Token: 0x0600006D RID: 109 RVA: 0x00008A56 File Offset: 0x00006C56
			public override bool CanRead
			{
				get
				{
					return this._baseStream.CanRead;
				}
			}

			// Token: 0x17000011 RID: 17
			// (get) Token: 0x0600006E RID: 110 RVA: 0x00008A63 File Offset: 0x00006C63
			public override bool CanSeek
			{
				get
				{
					return this._baseStream.CanSeek;
				}
			}

			// Token: 0x17000012 RID: 18
			// (get) Token: 0x0600006F RID: 111 RVA: 0x00008A70 File Offset: 0x00006C70
			public override bool CanWrite
			{
				get
				{
					return false;
				}
			}

			// Token: 0x17000013 RID: 19
			// (get) Token: 0x06000070 RID: 112 RVA: 0x00008A73 File Offset: 0x00006C73
			public override long Length
			{
				get
				{
					return this._length;
				}
			}

			// Token: 0x17000014 RID: 20
			// (get) Token: 0x06000071 RID: 113 RVA: 0x00008A7B File Offset: 0x00006C7B
			// (set) Token: 0x06000072 RID: 114 RVA: 0x00008A83 File Offset: 0x00006C83
			public override long Position
			{
				get
				{
					return this._position;
				}
				set
				{
					this.Seek(value, SeekOrigin.Begin);
				}
			}

			// Token: 0x06000073 RID: 115 RVA: 0x00008A90 File Offset: 0x00006C90
			public override int Read(byte[] buffer, int offset, int count)
			{
				long num = this._length - this._position;
				if (num <= 0L)
				{
					return 0;
				}
				if ((long)count > num)
				{
					count = (int)num;
				}
				int num2 = this._baseStream.Read(buffer, offset, count);
				this._position += (long)num2;
				return num2;
			}

			// Token: 0x06000074 RID: 116 RVA: 0x00008ADC File Offset: 0x00006CDC
			public override long Seek(long offset, SeekOrigin origin)
			{
				long num;
				if (origin == SeekOrigin.Begin)
				{
					num = offset;
				}
				else if (origin == SeekOrigin.Current)
				{
					num = this._position + offset;
				}
				else
				{
					if (origin != SeekOrigin.End)
					{
						throw new ArgumentOutOfRangeException("origin");
					}
					num = this._length + offset;
				}
				if (num < 0L || num > this._length)
				{
					throw new ArgumentOutOfRangeException("offset");
				}
				this._baseStream.Seek(this._start + num, SeekOrigin.Begin);
				this._position = num;
				return this._position;
			}

			// Token: 0x06000075 RID: 117 RVA: 0x00008B54 File Offset: 0x00006D54
			public override void Flush()
			{
				throw new NotSupportedException();
			}

			// Token: 0x06000076 RID: 118 RVA: 0x00008B5B File Offset: 0x00006D5B
			public override void SetLength(long value)
			{
				throw new NotSupportedException();
			}

			// Token: 0x06000077 RID: 119 RVA: 0x00008B62 File Offset: 0x00006D62
			public override void Write(byte[] buffer, int offset, int count)
			{
				throw new NotSupportedException();
			}

			// Token: 0x0400005B RID: 91
			private readonly Stream _baseStream;

			// Token: 0x0400005C RID: 92
			private readonly long _start;

			// Token: 0x0400005D RID: 93
			private readonly long _length;

			// Token: 0x0400005E RID: 94
			private long _position;
		}
	}
}
