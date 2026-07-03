using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Allegoria.Controls;
using ICSharpCode.SharpZipLib.Zip;
using InstallerHost.Properties;

namespace InstallerHost
{
	// Token: 0x0200000B RID: 11
	public partial class PrerequisiteControl : UserControl
	{
		// Token: 0x0600003C RID: 60 RVA: 0x000048C4 File Offset: 0x00002AC4
		public PrerequisiteControl(MainForm main)
		{
			this.mainForm = main;
			this.InitializeComponent();
			this.wizardHeader.Text = Texts.GetString("PrerequisiteIntro", Array.Empty<object>());
			this.lblAllInstalled.Text = Texts.GetString("All prerequisites installed", Array.Empty<object>());
			this.chkVCpp.Text = Texts.GetString("vcText", Array.Empty<object>());
			this.chkDirectX.Text = Texts.GetString("dx9text", Array.Empty<object>());
			this.chkDokany.Text = Texts.GetString("dokanyText", Array.Empty<object>());
			this.chkwinFSP.Text = Texts.GetString("winFSPtext", Array.Empty<object>());
			this.btnCancel.Text = Texts.GetString("Cancel", Array.Empty<object>());
			this.btnNext.Text = Texts.GetString("Next >", Array.Empty<object>());
			this.btnBack.Text = Texts.GetString("< Back", Array.Empty<object>());
			this.UpdateStatusLabelSafe(Texts.GetString("WaitingSelect", Array.Empty<object>()));
			this.UpdatePrerequisiteCheckboxes();
		}

		// Token: 0x0600003D RID: 61 RVA: 0x00004B9B File Offset: 0x00002D9B
		public bool SkipIfAllInstalled()
		{
			return !this.chkVCpp.Enabled && !this.chkDirectX.Enabled && !this.chkDokany.Enabled && !this.chkwinFSP.Enabled;
		}

		// Token: 0x0600003E RID: 62 RVA: 0x00004BD4 File Offset: 0x00002DD4
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			base.ActiveControl = this.btnNext;
		}

		// Token: 0x06000040 RID: 64 RVA: 0x00005337 File Offset: 0x00003537
		private void BtnBack_Click(object sender, EventArgs e)
		{
			this.mainForm.ShowLicense();
		}

		// Token: 0x06000041 RID: 65 RVA: 0x00005344 File Offset: 0x00003544
		private void BtnNext_Click(object sender, EventArgs e)
		{
			if ((!this.chkDirectX.Enabled || !this.chkDirectX.Checked) && (!this.chkVCpp.Enabled || !this.chkVCpp.Checked) && (!this.chkDokany.Enabled || !this.chkDokany.Checked) && (!this.chkwinFSP.Enabled || !this.chkwinFSP.Checked))
			{
				this.mainForm.ShowInstall();
				return;
			}
			if (!this.installationComplete)
			{
				this.btnNext.Enabled = false;
				this.btnBack.Enabled = false;
				this.btnCancel.Enabled = false;
				this.statusLabel.Text = Texts.GetString("DownloadAndInstall", Array.Empty<object>());
				this.statusLabel.Visible = true;
				int num = 0;
				if (this.chkDirectX.Enabled && this.chkDirectX.Checked)
				{
					num++;
				}
				if (this.chkVCpp.Enabled && this.chkVCpp.Checked)
				{
					num += this.vcRedistResources.Count;
				}
				if (this.chkDokany.Enabled && this.chkDokany.Checked)
				{
					num++;
				}
				if (this.chkwinFSP.Enabled && this.chkwinFSP.Checked)
				{
					num++;
				}
				this.progressBar.Value = 0;
				this.progressBar.Maximum = num;
				this.progressBar.Visible = true;
				this.installerWorker = new BackgroundWorker();
				this.installerWorker.DoWork += this.InstallerWorker_DoWork;
				this.installerWorker.RunWorkerCompleted += this.InstallerWorker_RunWorkerCompleted;
				this.installerWorker.RunWorkerAsync();
				return;
			}
			this.mainForm.ShowInstall();
		}

		// Token: 0x06000042 RID: 66 RVA: 0x00005514 File Offset: 0x00003714
		private void InstallerWorker_DoWork(object sender, DoWorkEventArgs e)
		{
			try
			{
				if (this.chkDirectX.Enabled && this.chkDirectX.Checked)
				{
					Logger.Log("Launching DirectX installer...");
					this.InstallDirectX();
				}
				if (this.chkVCpp.Enabled && this.chkVCpp.Checked)
				{
					Logger.Log("Launching VC++ installer...");
					this.InstallVCppAll();
				}
				if (this.chkDokany.Enabled && this.chkDokany.Checked)
				{
					Logger.Log("Launching Dokany installer...");
					this.InstallDokany();
				}
				if (this.chkwinFSP.Enabled && this.chkwinFSP.Checked)
				{
					Logger.Log("Launching WinFsp installer...");
					this.InstallWinFsp();
				}
				int num = 0;
				if (this.chkDirectX.Enabled && this.chkDirectX.Checked)
				{
					num++;
				}
				if (this.chkVCpp.Enabled && this.chkVCpp.Checked)
				{
					num += this.vcRedistResources.Count;
				}
				if (this.chkDokany.Enabled && this.chkDokany.Checked)
				{
					num++;
				}
				if (this.chkwinFSP.Enabled && this.chkwinFSP.Checked)
				{
					num++;
				}
			}
			catch (Exception ex)
			{
				e.Result = ex;
			}
		}

		// Token: 0x06000043 RID: 67 RVA: 0x00005678 File Offset: 0x00003878
		private void InstallerWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			Exception ex = e.Result as Exception;
			if (ex != null)
			{
				Logger.Log("Installation error: " + ex.Message);
				MessageBox.Show(Texts.GetString("InstallFail", Array.Empty<object>()) + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				this.btnNext.Enabled = true;
				this.btnBack.Enabled = true;
				this.btnCancel.Enabled = true;
				return;
			}
			this.UpdateStatusLabelSafe(Texts.GetString("InstallComplete", Array.Empty<object>()));
			this.installationComplete = true;
			this.btnNext.Enabled = true;
			this.btnCancel.Enabled = true;
			this.progressBar.Value = this.progressBar.Maximum;
		}

		// Token: 0x06000044 RID: 68 RVA: 0x00005740 File Offset: 0x00003940
		private void InstallDirectX()
		{
			string text = this.ExtractWgetExecutable();
			string text2 = Path.Combine(Path.GetTempPath(), "DXDownloads");
			if (!Directory.Exists(text2))
			{
				Directory.CreateDirectory(text2);
			}
			foreach (KeyValuePair<string, InstallerInfo> keyValuePair in this.dxRedistResources)
			{
				string key = keyValuePair.Key;
				InstallerInfo value = keyValuePair.Value;
				string text3 = Path.Combine(text2, key);
				string text4 = Path.Combine(text2, Path.GetFileNameWithoutExtension(key));
				string text5 = keyValuePair.Value.Url + key;
				try
				{
					string text6 = Path.Combine(Path.GetTempPath(), "DirectXRedistTemp_" + Guid.NewGuid().ToString());
					if (Directory.Exists(text6))
					{
						try
						{
							Directory.Delete(text6, true);
						}
						catch
						{
						}
					}
					string text7 = Path.Combine(text6, "directx_Jun2010_redist.exe");
					if (File.Exists(text7))
					{
						try
						{
							File.Delete(text7);
						}
						catch
						{
						}
					}
					string text8 = Path.Combine(Path.GetTempPath(), "DirectXRedist");
					if (Directory.Exists(text8))
					{
						try
						{
							Directory.Delete(text8, true);
						}
						catch
						{
						}
					}
					try
					{
						Directory.CreateDirectory(text6);
						Directory.CreateDirectory(text8);
					}
					catch
					{
					}
					this.UpdateStatusLabelSafe(Texts.GetString("Downloading", Array.Empty<object>()) + " " + key + "...");
					Logger.Log("Downloading ZIP from " + text5 + "...");
					this.DownloadWithWget(text, text5, text3);
					Logger.Log("Download complete.");
					this.ExtractZipToFolder(text3, text4, null);
					Logger.Log("Extraction complete.");
					string text9 = Path.Combine(text4, key.Replace(".zip", ".exe"));
					if (!File.Exists(text9))
					{
						throw new FileNotFoundException("Installer not found: " + text9);
					}
					this.UpdateStatusLabelSafe(Texts.GetString("Extracting", Array.Empty<object>()) + " " + key + "...");
					Process process = new Process();
					process.StartInfo.FileName = text9;
					process.StartInfo.Arguments = "/Q /T:\"" + text8 + "\"";
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.CreateNoWindow = true;
					process.Start();
					process.WaitForExit();
					if (process.ExitCode != 0)
					{
						throw new Exception(string.Format("Extraction failed with exit code {0}", process.ExitCode));
					}
					string text10 = Path.Combine(text8, "DXSETUP.exe");
					if (!File.Exists(text10))
					{
						throw new FileNotFoundException("DXSETUP.exe not found after extraction.");
					}
					this.UpdateStatusLabelSafe(Texts.GetString("InstallDX", Array.Empty<object>()));
					Process process2 = new Process();
					process2.StartInfo.FileName = text10;
					process2.StartInfo.Arguments = "";
					process2.StartInfo.UseShellExecute = true;
					process2.Start();
					process2.WaitForExit();
					Logger.Log(string.Format("DirectX installation finished with exit code: {0}", process2.ExitCode));
					try
					{
						Directory.Delete(text3, true);
					}
					catch
					{
					}
					try
					{
						Directory.Delete(text8, true);
					}
					catch
					{
					}
					this.UpdateProgressBarSafe();
				}
				catch (Exception ex)
				{
					Logger.Log("DirectX installation failed: " + ex.Message);
					MessageBox.Show("DirectX installation failed:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				}
				try
				{
					Directory.Delete(text2, true);
				}
				catch
				{
				}
			}
		}

		// Token: 0x06000045 RID: 69 RVA: 0x00005BA4 File Offset: 0x00003DA4
		private void InstallVCppAll()
		{
			string text = this.ExtractWgetExecutable();
			string text2 = Path.Combine(Path.GetTempPath(), "VCDownloads");
			if (!Directory.Exists(text2))
			{
				Directory.CreateDirectory(text2);
			}
			foreach (KeyValuePair<string, InstallerInfo> keyValuePair in this.vcRedistResources)
			{
				string key = keyValuePair.Key;
				InstallerInfo value = keyValuePair.Value;
				string text3 = Path.Combine(text2, key);
				string text4 = Path.Combine(text2, Path.GetFileNameWithoutExtension(key));
				string text5 = keyValuePair.Value.Url + key;
				try
				{
					this.UpdateStatusLabelSafe(Texts.GetString("Downloading", Array.Empty<object>()) + " " + key + "...");
					Logger.Log("Downloading ZIP from " + text5 + "...");
					this.DownloadWithWget(text, text5, text3);
					Logger.Log("Download and extraction complete.");
					this.ExtractZipToFolder(text3, text4, null);
					Logger.Log("Extraction complete.");
					this.UpdateStatusLabelSafe(Texts.GetString("Installing", Array.Empty<object>()) + " " + key + "...");
					string text6 = Path.Combine(text4, key.Replace(".zip", ".exe"));
					if (!File.Exists(text6))
					{
						throw new FileNotFoundException("Installer not found: " + text6);
					}
					Process process = new Process();
					process.StartInfo.FileName = text6;
					process.StartInfo.Arguments = value.Arguments;
					process.StartInfo.UseShellExecute = true;
					process.StartInfo.CreateNoWindow = false;
					Logger.Log("Running installer: " + text6 + " " + value.Arguments);
					process.Start();
					process.WaitForExit();
					try
					{
						Directory.Delete(text3, true);
					}
					catch
					{
					}
					try
					{
						Directory.Delete(text4, true);
					}
					catch
					{
					}
					Logger.Log(string.Format("Installer finished with code {0}", process.ExitCode));
				}
				catch (Exception ex)
				{
					Logger.Log("Failed to install " + key + ": " + ex.Message);
					MessageBox.Show("Error installing " + key + ":\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				}
				this.UpdateProgressBarSafe();
			}
			try
			{
				Directory.Delete(text2, true);
			}
			catch
			{
			}
		}

		// Token: 0x06000046 RID: 70 RVA: 0x00005E8C File Offset: 0x0000408C
		private void InstallDokany()
		{
			string text = this.ExtractWgetExecutable();
			string text2 = Path.Combine(Path.GetTempPath(), "dokanyDownloads");
			if (!Directory.Exists(text2))
			{
				Directory.CreateDirectory(text2);
			}
			foreach (KeyValuePair<string, InstallerInfo> keyValuePair in this.dokanResources)
			{
				string key = keyValuePair.Key;
				InstallerInfo value = keyValuePair.Value;
				string text3 = Path.Combine(text2, key);
				string text4 = Path.Combine(text2, Path.GetFileNameWithoutExtension(key));
				string text5 = keyValuePair.Value.Url + key;
				try
				{
					this.UpdateStatusLabelSafe(Texts.GetString("Downloading", Array.Empty<object>()) + " " + key + "...");
					Logger.Log("Downloading ZIP from " + text5 + "...");
					this.DownloadWithWget(text, text5, text3);
					Logger.Log("Download and extraction complete.");
					this.ExtractZipToFolder(text3, text4, null);
					Logger.Log("Extraction complete.");
					this.UpdateStatusLabelSafe(Texts.GetString("Installing", Array.Empty<object>()) + " " + key + "...");
					string text6 = Path.Combine(text4, key.Replace(".zip", ".exe"));
					if (!File.Exists(text6))
					{
						throw new FileNotFoundException("Installer not found: " + text6);
					}
					Process process = new Process();
					process.StartInfo.FileName = text6;
					process.StartInfo.Arguments = value.Arguments;
					process.StartInfo.UseShellExecute = true;
					process.StartInfo.CreateNoWindow = false;
					Logger.Log("Running installer: " + text6 + " " + value.Arguments);
					process.Start();
					process.WaitForExit();
					try
					{
						Directory.Delete(text3, true);
					}
					catch
					{
					}
					try
					{
						Directory.Delete(text4, true);
					}
					catch
					{
					}
					Logger.Log(string.Format("Installer finished with code {0}", process.ExitCode));
				}
				catch (Exception ex)
				{
					Logger.Log("Failed to install " + key + ": " + ex.Message);
					MessageBox.Show("Error installing " + key + ":\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				}
				this.UpdateProgressBarSafe();
			}
			try
			{
				Directory.Delete(text2, true);
			}
			catch
			{
			}
		}

		// Token: 0x06000047 RID: 71 RVA: 0x00006174 File Offset: 0x00004374
		private void InstallWinFsp()
		{
			string text = this.ExtractWgetExecutable();
			string text2 = Path.Combine(Path.GetTempPath(), "winfspDownloads");
			if (!Directory.Exists(text2))
			{
				Directory.CreateDirectory(text2);
			}
			try
			{
				foreach (KeyValuePair<string, InstallerInfo> keyValuePair in this.winFSPResources)
				{
					string key = keyValuePair.Key;
					string text3 = Path.Combine(text2, key);
					string text4 = Path.Combine(text2, Path.GetFileNameWithoutExtension(key));
					string text5 = keyValuePair.Value.Url + key;
					try
					{
						this.UpdateStatusLabelSafe(Texts.GetString("Downloading", Array.Empty<object>()) + " " + key + "...");
						Logger.Log("Downloading " + text5 + "...");
						this.DownloadWithWget(text, text5, text3);
						this.ExtractZipToFolder(text3, text4, null);
						Logger.Log("Extraction complete.");
						string text6 = Directory.EnumerateFiles(text4, "*.msi", SearchOption.AllDirectories).FirstOrDefault<string>();
						if (text6 == null)
						{
							throw new FileNotFoundException("No MSI found inside " + key);
						}
						this.UpdateStatusLabelSafe(Texts.GetString("Installing", Array.Empty<object>()) + " " + Path.GetFileName(text6) + "...");
						Logger.Log("Running WinFsp MSI installer: " + text6);
						Process process = new Process();
						process.StartInfo.FileName = "msiexec.exe";
						process.StartInfo.Arguments = "/i \"" + text6 + "\" /quiet /norestart";
						process.StartInfo.UseShellExecute = true;
						process.Start();
						process.WaitForExit();
						Logger.Log(string.Format("WinFsp installer finished with code {0}", process.ExitCode));
					}
					catch (Exception ex)
					{
						Logger.Log("WinFsp installation failed: " + ex.Message);
						MessageBox.Show("WinFsp installation failed:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
					}
				}
			}
			finally
			{
				try
				{
					Directory.Delete(text2, true);
				}
				catch
				{
				}
			}
			this.UpdateProgressBarSafe();
		}

		// Token: 0x06000048 RID: 72 RVA: 0x000063F8 File Offset: 0x000045F8
		public static string ExtractEmbeddedFile(string resourceName, string outputFile)
		{
			using (Stream manifestResourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
			{
				if (manifestResourceStream == null)
				{
					throw new Exception("Resource not found: " + resourceName);
				}
				using (FileStream fileStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
				{
					manifestResourceStream.CopyTo(fileStream);
				}
			}
			return outputFile;
		}

		// Token: 0x06000049 RID: 73 RVA: 0x00006468 File Offset: 0x00004668
		public void BtnCancel_Click(object sender, EventArgs e)
		{
			if (MessageBox.Show(Texts.GetString("CancelSure", Array.Empty<object>()), Texts.GetString("CancelButtonTitle", Array.Empty<object>()), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				Application.Exit();
			}
		}

		// Token: 0x0600004A RID: 74 RVA: 0x00006498 File Offset: 0x00004698
		private void UpdateProgressBarSafe()
		{
			if (this.progressBar.InvokeRequired)
			{
				this.progressBar.Invoke(new Action(delegate
				{
					if (this.progressBar.Value < this.progressBar.Maximum)
					{
						ProgressBar progressBar2 = this.progressBar;
						int value2 = progressBar2.Value;
						progressBar2.Value = value2 + 1;
					}
				}));
				return;
			}
			if (this.progressBar.Value < this.progressBar.Maximum)
			{
				ProgressBar progressBar = this.progressBar;
				int value = progressBar.Value;
				progressBar.Value = value + 1;
			}
		}

		// Token: 0x0600004B RID: 75 RVA: 0x000064F8 File Offset: 0x000046F8
		private string ExtractWgetExecutable()
		{
			string text = Path.Combine(Path.GetTempPath(), "wget.exe");
			string text2 = "InstallerHost.resources.wget.exe";
			if (!File.Exists(text))
			{
				using (Stream manifestResourceStream = typeof(PrerequisiteControl).Assembly.GetManifestResourceStream(text2))
				{
					if (manifestResourceStream == null)
					{
						throw new Exception("wget.exe resource not found.");
					}
					using (FileStream fileStream = new FileStream(text, FileMode.Create, FileAccess.Write))
					{
						manifestResourceStream.CopyTo(fileStream);
					}
				}
			}
			return text;
		}

		// Token: 0x0600004C RID: 76 RVA: 0x0000658C File Offset: 0x0000478C
		private void ExtractZipToFolder(string zipFilePath, string destinationFolder, Action<int> progress = null)
		{
			using (FileStream fileStream = File.OpenRead(zipFilePath))
			{
				using (ZipFile zipFile = new ZipFile(fileStream))
				{
					long num = 0L;
					foreach (object obj in zipFile)
					{
						ZipEntry zipEntry = (ZipEntry)obj;
						if (zipEntry.IsFile)
						{
							num += zipEntry.Size;
						}
					}
					long num2 = 0L;
					foreach (object obj2 in zipFile)
					{
						ZipEntry zipEntry2 = (ZipEntry)obj2;
						if (zipEntry2.IsFile)
						{
							string name = zipEntry2.Name;
							string text = Path.Combine(destinationFolder, name);
							string directoryName = Path.GetDirectoryName(text);
							if (!Directory.Exists(directoryName))
							{
								Directory.CreateDirectory(directoryName);
							}
							using (Stream inputStream = zipFile.GetInputStream(zipEntry2))
							{
								using (FileStream fileStream2 = File.Create(text))
								{
									byte[] array = new byte[4096];
									int num3;
									while ((num3 = inputStream.Read(array, 0, array.Length)) > 0)
									{
										fileStream2.Write(array, 0, num3);
										num2 += (long)num3;
										if (progress != null)
										{
											int num4 = (int)(num2 * 100L / num);
											progress(num4);
										}
									}
								}
							}
						}
					}
				}
			}
		}

		// Token: 0x0600004D RID: 77 RVA: 0x00006790 File Offset: 0x00004990
		private void DownloadWithWget(string wgetPath, string url, string outputPath)
		{
			try
			{
				using (Process process = new Process())
				{
					process.StartInfo.FileName = wgetPath;
					process.StartInfo.Arguments = string.Concat(new string[] { "\"", url, "\" -O \"", outputPath, "\" --no-check-certificate" });
					process.StartInfo.CreateNoWindow = true;
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.RedirectStandardOutput = true;
					process.StartInfo.RedirectStandardError = true;
					process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
					{
						if (e.Data != null)
						{
							Logger.Log("wget stdout: " + e.Data);
						}
					};
					process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
					{
						if (e.Data != null)
						{
							Logger.Log("wget stderr: " + e.Data);
						}
					};
					process.Start();
					process.BeginOutputReadLine();
					process.BeginErrorReadLine();
					process.WaitForExit();
					if (process.ExitCode != 0)
					{
						throw new Exception(string.Format("wget failed with exit code {0}", process.ExitCode));
					}
				}
			}
			catch
			{
				Logger.Log("Error Downloading" + url);
			}
		}

		// Token: 0x0600004E RID: 78 RVA: 0x000068F0 File Offset: 0x00004AF0
		private void UpdateStatusLabelSafe(string text)
		{
			if (this.statusLabel.InvokeRequired)
			{
				this.statusLabel.Invoke(new Action(delegate
				{
					this.statusLabel.Text = text;
				}));
				return;
			}
			this.statusLabel.Text = text;
		}

		// Token: 0x0600004F RID: 79 RVA: 0x00006948 File Offset: 0x00004B48
		private void UpdatePrerequisiteCheckboxes()
		{
			try
			{
				this.chkVCpp.Enabled = !PrerequisiteDetector.IsVCppFullyInstalled();
				if (!this.chkVCpp.Enabled)
				{
					this.chkVCpp.Checked = true;
				}
				this.chkDirectX.Enabled = !PrerequisiteDetector.IsDirectXJun2010Installed();
				if (!this.chkDirectX.Enabled)
				{
					this.chkDirectX.Checked = true;
				}
				this.chkDokany.Enabled = !PrerequisiteDetector.IsDokanyInstalled();
				if (!this.chkDokany.Enabled)
				{
					this.chkDokany.Checked = true;
				}
				this.chkwinFSP.Enabled = !PrerequisiteDetector.IsWinFspInstalled();
				if (!this.chkwinFSP.Enabled)
				{
					this.chkwinFSP.Checked = true;
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Error detecting prerequisites: " + ex.Message);
				this.chkVCpp.Enabled = true;
				this.chkVCpp.Checked = true;
				this.chkDirectX.Enabled = true;
				this.chkDirectX.Checked = true;
				this.chkDokany.Enabled = true;
				this.chkDokany.Checked = false;
				this.chkwinFSP.Enabled = true;
				this.chkwinFSP.Checked = false;
			}
			int num = 0;
			if (this.chkDirectX.Enabled && this.chkDirectX.Checked)
			{
				num++;
			}
			if (this.chkVCpp.Enabled && this.chkVCpp.Checked)
			{
				num += this.vcRedistResources.Count;
			}
			if (this.chkDokany.Enabled && this.chkDokany.Checked)
			{
				num++;
			}
			if (this.chkwinFSP.Enabled && this.chkwinFSP.Checked)
			{
				num++;
			}
			this.progressBar.Maximum = Math.Max(1, num);
			this.progressBar.Value = 0;
			this.lblAllInstalled.Visible = !this.chkVCpp.Enabled && !this.chkDirectX.Enabled && !this.chkDokany.Enabled && !this.chkwinFSP.Enabled;
		}

		// Token: 0x04000034 RID: 52
		private MainForm mainForm;

		// Token: 0x0400003E RID: 62
		private BackgroundWorker installerWorker;

		// Token: 0x04000040 RID: 64
		private bool installationComplete;

		// Token: 0x04000043 RID: 67
		private readonly Dictionary<string, InstallerInfo> vcRedistResources = new Dictionary<string, InstallerInfo>
		{
			{
				"vcredist2005_x64.zip",
				new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/q")
			},
			{
				"vcredist2005_x86.zip",
				new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/q")
			},
			{
				"vcredist2008_x64.zip",
				new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/qb")
			},
			{
				"vcredist2008_x86.zip",
				new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/qb")
			},
			{
				"vcredist2010_x64.zip",
				new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/passive /norestart")
			},
			{
				"vcredist2010_x86.zip",
				new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/passive /norestart")
			},
			{
				"vcredist2012_x64.zip",
				new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/passive /norestart")
			},
			{
				"vcredist2012_x86.zip",
				new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/passive /norestart")
			},
			{
				"vcredist2013_x64.zip",
				new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/passive /norestart")
			},
			{
				"vcredist2013_x86.zip",
				new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/passive /norestart")
			},
			{
				"vcredist2015_2017_2019_2022_x64.zip",
				new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/passive /norestart")
			},
			{
				"vcredist2015_2017_2019_2022_x86.zip",
				new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/passive /norestart")
			}
		};

		// Token: 0x04000044 RID: 68
		private readonly Dictionary<string, InstallerInfo> dxRedistResources = new Dictionary<string, InstallerInfo> { 
		{
			"directx_Jun2010_redist.zip",
			new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/q")
		} };

		// Token: 0x04000045 RID: 69
		private readonly Dictionary<string, InstallerInfo> dokanResources = new Dictionary<string, InstallerInfo> { 
		{
			"DokanSetup.zip",
			new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "/quiet")
		} };

		// Token: 0x04000046 RID: 70
		private readonly Dictionary<string, InstallerInfo> winFSPResources = new Dictionary<string, InstallerInfo> { 
		{
			"winfsp.zip",
			new InstallerInfo("http://retrobat.ovh/repo/win64/prerequisites/", "")
		} };
	}
}
