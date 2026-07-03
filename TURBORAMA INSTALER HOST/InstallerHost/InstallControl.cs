using System;
using System.Collections.Generic;
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
			this.txtFolder.Text = "C:\\Turborama";
			TurboramaPremiumUi.ApplyInstallV3(this);
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
				MessageBox.Show("Selecione uma pasta de instalação válida.");
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
					MessageBox.Show("Falha ao criar a pasta de instalação: " + ex.Message);
					return;
				}
			}
			this.txtInfo.Visible = false;
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
					using (Stream installerZipStream = this.GetInstallerZipStream())
					{
						this.ExtractZipStreamToFolder(installerZipStream, text2);
					}
					this.EnsureTurboRamaExecutable(text2);
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
				this.txtInfo.Visible = true;
				Exception ex3 = completeArgs.Result as Exception;
				if (ex3 != null)
				{
					MessageBox.Show(this, "Falha na instalação: " + ex3.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Hand);
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
			if (MessageBox.Show("Tem certeza que deseja cancelar a instalação?", "Cancelar instalação", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				Application.Exit();
			}
		}

		// Token: 0x0600001F RID: 31 RVA: 0x000038B0 File Offset: 0x00001AB0
		private Stream GetInstallerZipStream()
		{
			Stream splitStream = this.TryGetSplitPackageStream();
			if (splitStream != null)
			{
				return splitStream;
			}

			// Compatibilidade: se não existir pacote split, tenta o modo antigo embutido.
			return this.GetEmbeddedZipStream();
		}

		private Stream TryGetSplitPackageStream()
		{
			string exePath = Application.ExecutablePath;
			string folder = Path.GetDirectoryName(exePath);
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(exePath);

			string[] packageBases = new string[]
			{
				exePath,
				Path.Combine(folder, fileNameWithoutExtension)
			};

			foreach (string packageBase in packageBases)
			{
				List<string> parts = new List<string>();

				for (int i = 1; i <= 999; i++)
				{
					string partPath = packageBase + ".pkg." + i.ToString("000");

					if (!File.Exists(partPath))
					{
						break;
					}

					parts.Add(partPath);
				}

				if (parts.Count > 0)
				{
					Logger.Log("Using split installer package with " + parts.Count + " part(s). Base: " + packageBase);
					return new InstallControl.MultiPartFileStream(parts);
				}
			}

			return null;
		}

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
				zipFile.UseZip64 = ICSharpCode.SharpZipLib.Zip.UseZip64.On;
				long num = (from ZipEntry e in zipFile
					where e.IsFile && e.Size > 0L
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
		
		private void EnsureTurboRamaExecutable(string destinationFolder)
		{
			string turboRamaExe = Path.Combine(destinationFolder, "TurboRama.exe");
			string turboramaExe = Path.Combine(destinationFolder, "Turborama.exe");
			string retroBatExe1 = Path.Combine(destinationFolder, "RetroBat.exe");
			string retroBatExe2 = Path.Combine(destinationFolder, "retrobat.exe");
			string tempRename = Path.Combine(destinationFolder, "TurboRama.rename.tmp");

			try
			{
				if (File.Exists(tempRename))
				{
					File.Delete(tempRename);
				}

				if (File.Exists(turboramaExe))
				{
					File.Move(turboramaExe, tempRename);
					File.Move(tempRename, turboRamaExe);
					Logger.Log("Executable renamed from Turborama.exe to TurboRama.exe");
				}
				else if (File.Exists(retroBatExe1))
				{
					File.Move(retroBatExe1, turboRamaExe);
					Logger.Log("Executable renamed from RetroBat.exe to TurboRama.exe");
				}
				else if (File.Exists(retroBatExe2))
				{
					File.Move(retroBatExe2, turboRamaExe);
					Logger.Log("Executable renamed from retrobat.exe to TurboRama.exe");
				}

				if (File.Exists(retroBatExe1))
				{
					File.Delete(retroBatExe1);
					Logger.Log("Removed old RetroBat.exe");
				}
				if (File.Exists(retroBatExe2))
				{
					File.Delete(retroBatExe2);
					Logger.Log("Removed old retrobat.exe");
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to create TurboRama.exe: " + ex.ToString());
				throw new Exception("Failed to create TurboRama.exe: " + ex.Message, ex);
			}

			if (!File.Exists(turboRamaExe))
			{
				throw new FileNotFoundException("TurboRama.exe was not created. The installer archive does not contain RetroBat.exe, retrobat.exe, Turborama.exe or TurboRama.exe.", turboRamaExe);
			}
		}

		private class MultiPartFileStream : Stream
		{
			private readonly List<string> _partPaths;
			private readonly long[] _partLengths;
			private readonly long _length;
			private long _position;
			private int _currentPartIndex = -1;
			private FileStream _currentStream;

			public MultiPartFileStream(List<string> partPaths)
			{
				if (partPaths == null || partPaths.Count == 0)
				{
					throw new ArgumentException("No split package parts found.", "partPaths");
				}

				this._partPaths = new List<string>(partPaths);
				this._partLengths = new long[this._partPaths.Count];

				long total = 0L;
				for (int i = 0; i < this._partPaths.Count; i++)
				{
					FileInfo fileInfo = new FileInfo(this._partPaths[i]);
					if (!fileInfo.Exists)
					{
						throw new FileNotFoundException("Split package part not found.", this._partPaths[i]);
					}

					this._partLengths[i] = fileInfo.Length;
					total += fileInfo.Length;
				}

				this._length = total;
				this._position = 0L;
			}

			public override bool CanRead
			{
				get { return true; }
			}

			public override bool CanSeek
			{
				get { return true; }
			}

			public override bool CanWrite
			{
				get { return false; }
			}

			public override long Length
			{
				get { return this._length; }
			}

			public override long Position
			{
				get { return this._position; }
				set { this.Seek(value, SeekOrigin.Begin); }
			}

			public override int Read(byte[] buffer, int offset, int count)
			{
				if (buffer == null)
				{
					throw new ArgumentNullException("buffer");
				}
				if (offset < 0 || count < 0 || offset + count > buffer.Length)
				{
					throw new ArgumentOutOfRangeException("offset");
				}

				if (count == 0 || this._position >= this._length)
				{
					return 0;
				}

				int totalRead = 0;

				while (count > 0 && this._position < this._length)
				{
					long partStart;
					int partIndex = this.GetPartIndexForPosition(this._position, out partStart);
					if (partIndex < 0)
					{
						break;
					}

					this.OpenPart(partIndex);

					long positionInsidePart = this._position - partStart;
					long remainingInPart = this._partLengths[partIndex] - positionInsidePart;
					if (remainingInPart <= 0L)
					{
						this._position = partStart + this._partLengths[partIndex];
						continue;
					}

					this._currentStream.Position = positionInsidePart;
					int bytesToRead = (int)Math.Min((long)count, remainingInPart);
					int bytesRead = this._currentStream.Read(buffer, offset, bytesToRead);

					if (bytesRead <= 0)
					{
						break;
					}

					this._position += bytesRead;
					offset += bytesRead;
					count -= bytesRead;
					totalRead += bytesRead;
				}

				return totalRead;
			}

			public override long Seek(long offset, SeekOrigin origin)
			{
				long newPosition;

				if (origin == SeekOrigin.Begin)
				{
					newPosition = offset;
				}
				else if (origin == SeekOrigin.Current)
				{
					newPosition = this._position + offset;
				}
				else if (origin == SeekOrigin.End)
				{
					newPosition = this._length + offset;
				}
				else
				{
					throw new ArgumentOutOfRangeException("origin");
				}

				if (newPosition < 0L || newPosition > this._length)
				{
					throw new IOException("Seek outside split package stream.");
				}

				this._position = newPosition;
				return this._position;
			}

			public override void Flush()
			{
			}

			public override void SetLength(long value)
			{
				throw new NotSupportedException();
			}

			public override void Write(byte[] buffer, int offset, int count)
			{
				throw new NotSupportedException();
			}

			protected override void Dispose(bool disposing)
			{
				if (disposing)
				{
					this.CloseCurrentStream();
				}

				base.Dispose(disposing);
			}

			private int GetPartIndexForPosition(long position, out long partStart)
			{
				partStart = 0L;

				for (int i = 0; i < this._partLengths.Length; i++)
				{
					long partEnd = partStart + this._partLengths[i];
					if (position < partEnd)
					{
						return i;
					}

					partStart = partEnd;
				}

				return -1;
			}

			private void OpenPart(int partIndex)
			{
				if (this._currentPartIndex == partIndex && this._currentStream != null)
				{
					return;
				}

				this.CloseCurrentStream();
				this._currentStream = new FileStream(this._partPaths[partIndex], FileMode.Open, FileAccess.Read, FileShare.Read);
				this._currentPartIndex = partIndex;
			}

			private void CloseCurrentStream()
			{
				if (this._currentStream != null)
				{
					this._currentStream.Dispose();
					this._currentStream = null;
				}

				this._currentPartIndex = -1;
			}
		}

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

