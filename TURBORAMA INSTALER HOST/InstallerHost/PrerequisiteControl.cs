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
		private CheckBox chkNvidiaApp;

		
		private bool turboramaPremiumLayoutRunning;
private Panel turboramaPremiumPanel;
// Token: 0x0600003C RID: 60 RVA: 0x000048C4 File Offset: 0x00002AC4
		public PrerequisiteControl(MainForm main)
		{
			this.mainForm = main;
			this.InitializeComponent();
			this.TurboramaEnsurePrerequisitesReadyV15();
			this.Load += delegate(object s, EventArgs e) { this.TurboramaEnsurePrerequisitesReadyV15(); };
			this.VisibleChanged += delegate(object s, EventArgs e) { if (this.Visible) { this.TurboramaEnsurePrerequisitesReadyV15(); } };
this.TurboramaStartForcePrereqV13();
			this.Load += delegate(object s, EventArgs e) { this.TurboramaStartForcePrereqV13(); };
			this.VisibleChanged += delegate(object s, EventArgs e)
			{
				if (this.Visible)
				{
					this.TurboramaStartForcePrereqV13();
				}
			};this.TurboramaBuildPremiumPrerequisites();
			this.Load += delegate(object s, EventArgs e)
			{
				this.BeginInvoke(new MethodInvoker(this.TurboramaBuildPremiumPrerequisites));
			};
			this.VisibleChanged += delegate(object s, EventArgs e)
			{
				if (this.Visible)
				{
					this.BeginInvoke(new MethodInvoker(this.TurboramaBuildPremiumPrerequisites));
				}
			};this.TurboramaBuildPremiumPrerequisites();
			this.Load += delegate(object s, EventArgs e)
			{
				this.BeginInvoke(new MethodInvoker(this.TurboramaBuildPremiumPrerequisites));
			};
			this.VisibleChanged += delegate(object s, EventArgs e)
			{
				if (this.Visible)
				{
					this.BeginInvoke(new MethodInvoker(this.TurboramaBuildPremiumPrerequisites));
				}
			};this.Load += delegate(object s, EventArgs e)
			{
				this.BeginInvoke(new MethodInvoker(this.TurboramaBuildPremiumPrerequisites));
			};
			this.VisibleChanged += delegate(object s, EventArgs e)
			{
				if (this.Visible)
				{
					this.BeginInvoke(new MethodInvoker(this.TurboramaBuildPremiumPrerequisites));
				}
			};

this.Load += delegate(object s, EventArgs e)
			{
				this.BeginInvoke(new MethodInvoker(this.TurboramaBuildPremiumPrerequisites));
			};
			this.VisibleChanged += delegate(object s, EventArgs e)
			{
				if (this.Visible)
				{
					this.BeginInvoke(new MethodInvoker(this.TurboramaBuildPremiumPrerequisites));
				}
			};this.wizardHeader.Text = Texts.GetString("PrerequisiteIntro", Array.Empty<object>());
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
			this.ForceRuntimeCheckboxes();
			this.CreateNvidiaDriverCheckbox();
		}

		// Token: 0x0600003D RID: 61 RVA: 0x00004B9B File Offset: 0x00002D9B
				private void ForceRuntimeCheckboxes()
		{
			this.chkVCpp.Enabled = true;
			this.chkVCpp.Checked = true;

			this.chkDirectX.Enabled = true;
			this.chkDirectX.Checked = true;

			// Dokan e WinFsp ficam marcados se ja estiverem instalados/desabilitados.
			// Se estiverem habilitados, tambem ficam marcados por padrao.
			this.chkDokany.Checked = true;
			this.chkwinFSP.Checked = true;

			this.lblAllInstalled.Visible = false;

			int total = 0;
			if (this.chkDirectX.Enabled && this.chkDirectX.Checked)
			{
				total++;
			}
			if (this.chkVCpp.Enabled && this.chkVCpp.Checked)
			{
				total += this.vcRedistResources.Count;
			}
			if (this.chkDokany.Enabled && this.chkDokany.Checked)
			{
				total++;
			}
			if (this.chkwinFSP.Enabled && this.chkwinFSP.Checked)
			{
				total++;
			}

			this.progressBar.Maximum = Math.Max(1, total);
			this.progressBar.Value = 0;
		}
		private void CreateNvidiaDriverCheckbox()
		{
			if (this.chkNvidiaApp != null)
			{
				return;
			}

			bool hasNvidia = NvidiaAppInstallerHelper.HasNvidiaGpu();

			this.chkNvidiaApp = new CheckBox();
			this.chkNvidiaApp.AutoSize = true;
			this.chkNvidiaApp.Checked = hasNvidia;
			this.chkNvidiaApp.Enabled = hasNvidia;
			this.chkNvidiaApp.Location = new Point(24, 221);
			this.chkNvidiaApp.Name = "chkNvidiaApp";
			this.chkNvidiaApp.Size = new Size(360, 17);
			this.chkNvidiaApp.TabIndex = 6;
			this.chkNvidiaApp.Text = hasNvidia ? "NVIDIA App (detect/update GeForce drivers)" : "NVIDIA App (NVIDIA GPU not detected)";
			base.Controls.Add(this.chkNvidiaApp);
			this.chkNvidiaApp.BringToFront();
		}
		private void HandleNvidiaAppCheckbox()
		{
			if (this.nvidiaAppOpened)
			{
				return;
			}

			if (this.chkNvidiaApp == null || !this.chkNvidiaApp.Enabled || !this.chkNvidiaApp.Checked)
			{
				return;
			}

			this.nvidiaAppOpened = true;

			try
			{
				Logger.Log("Opening official NVIDIA App page.");
				MessageBox.Show(
					"The official NVIDIA App page will be opened now." + Environment.NewLine + Environment.NewLine +
					"Use NVIDIA App to detect and update GeForce Game Ready / Studio drivers.",
					"NVIDIA App",
					MessageBoxButtons.OK,
					MessageBoxIcon.Information
				);

				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = "https://www.nvidia.com/pt-br/software/nvidia-app/",
					UseShellExecute = true
				};
				Process.Start(startInfo);
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to open NVIDIA App page: " + ex.ToString());
				MessageBox.Show("Failed to open NVIDIA App page: " + ex.Message, "NVIDIA App", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}
				public bool SkipIfAllInstalled()
		{
			return false;
		}

		// Token: 0x0600003E RID: 62 RVA: 0x00004BD4 File Offset: 0x00002DD4
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			this.ForceRuntimeCheckboxes();
			this.CreateNvidiaDriverCheckbox();
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
			this.UpdatePrerequisiteCheckboxes();
			this.mainForm.ShowInstall();
		}

		// Token: 0x06000042 RID: 66 RVA: 0x00005514 File Offset: 0x00003714
		private void InstallerWorker_DoWork(object sender, DoWorkEventArgs e)
		{
			try
			{
				if (this.chkDirectX.Enabled && this.chkDirectX.Checked)
				{
					Logger.Log("Launching DirectX Legacy Runtime June 2010 installer...");
					this.InstallDirectX();
					this.OfferDirectX11And12Update();
				}
				if (this.chkVCpp.Enabled && this.chkVCpp.Checked)
				{
					Logger.Log("Launching all Microsoft Visual C++ runtimes 2005-2022 x86/x64...");
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
				if (this.chkNvidiaApp != null && this.chkNvidiaApp.Enabled && this.chkNvidiaApp.Checked)
				{
					Logger.Log("Launching NVIDIA App installer...");
					NvidiaAppInstallerHelper.InstallOrOpenNvidiaApp();
					this.UpdateProgressBarSafe();
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
				if (this.chkNvidiaApp != null && this.chkNvidiaApp.Enabled && this.chkNvidiaApp.Checked)
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
						File.Delete(text3);
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
				private void OfferDirectX11And12Update()
		{
			try
			{
				string message = "DirectX Legacy June 2010 was installed or checked." + Environment.NewLine + Environment.NewLine +
					"DirectX 11 and DirectX 12 do not have a separate offline installer on Windows 10/11." + Environment.NewLine +
					"They are part of Windows and are updated through Windows Update and video drivers." + Environment.NewLine + Environment.NewLine +
					"Do you want to open Windows Update now to check DirectX 11/12 system updates?";

				DialogResult result = MessageBox.Show(
					message,
					"DirectX 11/12",
					MessageBoxButtons.YesNo,
					MessageBoxIcon.Information
				);

				if (result == DialogResult.Yes)
				{
					ProcessStartInfo info = new ProcessStartInfo
					{
						FileName = "ms-settings:windowsupdate",
						UseShellExecute = true
					};
					Process.Start(info);
					Logger.Log("Opened Windows Update for DirectX 11/12 updates.");
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to open Windows Update for DirectX 11/12: " + ex.ToString());
			}
		}
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
						File.Delete(text3);
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
						File.Delete(text3);
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
				this.chkVCpp.Enabled = true;
				this.chkVCpp.Checked = true;
				this.chkVCpp.CheckState = CheckState.Checked;

				this.chkDirectX.Enabled = true;
				this.chkDirectX.Checked = true;
				this.chkDirectX.CheckState = CheckState.Checked;

				this.chkDokany.Enabled = true;
				this.chkDokany.Checked = true;
				this.chkDokany.CheckState = CheckState.Checked;

				this.chkwinFSP.Enabled = true;
				this.chkwinFSP.Checked = true;
				this.chkwinFSP.CheckState = CheckState.Checked;

				this.lblAllInstalled.Visible = false;
				this.statusLabel.Visible = false;
				this.progressBar.Visible = false;
				this.progressBar.Value = 0;
				this.progressBar.Maximum = 1;

				this.btnBack.Enabled = true;
				this.btnBack.Visible = true;

				this.btnNext.Enabled = true;
				this.btnNext.Visible = true;

				this.btnCancel.Enabled = true;
				this.btnCancel.Visible = true;

				this.btnBack.BringToFront();
				this.btnNext.BringToFront();
				this.btnCancel.BringToFront();
			}
			catch (Exception ex)
			{
				Logger.Log("Error setting prerequisite checkboxes: " + ex.Message);
			}
		}

		// Token: 0x04000034 RID: 52
		private MainForm mainForm;

		private bool nvidiaAppOpened;

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
		private void TurboramaBuildPremiumPrerequisites()
		{
			if (this.turboramaPremiumLayoutRunning)
			{
				return;
			}

			this.turboramaPremiumLayoutRunning = true;

			try
			{
				base.SuspendLayout();
base.BackColor = TurboramaPremiumUi.Background;

				List<CheckBox> checkBoxes = new List<CheckBox>();
				this.TurboramaCollectCheckBoxes(this, checkBoxes);

				CheckBox vc = null;
				CheckBox dx = null;
				CheckBox nvidia = null;
				CheckBox dokan = null;
				CheckBox winfsp = null;

				foreach (CheckBox checkBox in checkBoxes)
				{
					string search = ((checkBox.Name ?? string.Empty) + " " + (checkBox.Text ?? string.Empty)).ToLowerInvariant();

					if (search.Contains("visual") || search.Contains("c++") || search.Contains("vc++") || search.Contains("vcredist"))
					{
						if (vc == null) vc = checkBox; else checkBox.Visible = false;
					}
					else if (search.Contains("directx"))
					{
						if (dx == null) dx = checkBox; else checkBox.Visible = false;
					}
					else if (search.Contains("nvidia") || search.Contains("geforce"))
					{
						if (nvidia == null) nvidia = checkBox; else checkBox.Visible = false;
					}
					else if (search.Contains("dokan"))
					{
						if (dokan == null) dokan = checkBox; else checkBox.Visible = false;
					}
					else if (search.Contains("winfsp"))
					{
						if (winfsp == null) winfsp = checkBox; else checkBox.Visible = false;
					}
				}

				if (nvidia == null)
				{
					nvidia = new CheckBox();
					nvidia.Name = "chkNvidiaApp";
				}

				this.chkNvidiaApp = nvidia;

				foreach (Control control in base.Controls)
				{
					if (control != this.turboramaPremiumPanel && !(control is Button))
					{
						control.Visible = false;
					}
				}

				if (this.turboramaPremiumPanel == null)
				{
					this.turboramaPremiumPanel = new Panel();
					this.turboramaPremiumPanel.Name = "turboramaPremiumPanel";
					base.Controls.Add(this.turboramaPremiumPanel);
				}

				this.turboramaPremiumPanel.SuspendLayout();
				this.turboramaPremiumPanel.Controls.Clear();

				int footerHeight = 68;
				int panelHeight = Math.Max(300, base.Height - footerHeight);

				this.turboramaPremiumPanel.Left = 0;
				this.turboramaPremiumPanel.Top = 0;
				this.turboramaPremiumPanel.Width = Math.Max(630, base.Width);
				this.turboramaPremiumPanel.Height = panelHeight;
				this.turboramaPremiumPanel.BackColor = TurboramaPremiumUi.Background;
				this.turboramaPremiumPanel.ForeColor = Color.White;
				this.turboramaPremiumPanel.BorderStyle = BorderStyle.None;
				this.turboramaPremiumPanel.Visible = true;

				Panel left = new Panel();
				left.Left = 0;
				left.Top = 0;
				left.Width = 190;
				left.Height = this.turboramaPremiumPanel.Height;
				left.BackColor = Color.FromArgb(2, 12, 5);
				this.turboramaPremiumPanel.Controls.Add(left);

				Label logo = TurboramaPremiumUi.MakeLabel("LZ", 20, 18, 60, 42, TurboramaPremiumUi.Green, 20f, true);
				logo.TextAlign = ContentAlignment.MiddleCenter;
				logo.BorderStyle = BorderStyle.FixedSingle;
				left.Controls.Add(logo);

				left.Controls.Add(TurboramaPremiumUi.MakeLabel("TURBORAMA", 20, 70, 150, 26, Color.White, 12f, true));
				left.Controls.Add(TurboramaPremiumUi.MakeLabel("SYSTEM CHECK", 20, 98, 150, 20, TurboramaPremiumUi.Green, 8.5f, true));

				Panel accent = new Panel();
				accent.Left = 20;
				accent.Top = 130;
				accent.Width = 140;
				accent.Height = 3;
				accent.BackColor = TurboramaPremiumUi.Green;
				left.Controls.Add(accent);

				left.Controls.Add(TurboramaPremiumUi.MakeLabel("PREMIUM INSTALLER", 20, 150, 150, 22, TurboramaPremiumUi.Muted, 8.5f, false));
				left.Controls.Add(TurboramaPremiumUi.MakeLabel("Ready for setup", 20, 174, 150, 22, TurboramaPremiumUi.Green, 8.5f, true));

				int contentLeft = 220;
				int contentWidth = this.turboramaPremiumPanel.Width - contentLeft - 25;

				this.turboramaPremiumPanel.Controls.Add(TurboramaPremiumUi.MakeLabel("Requisitos do sistema", contentLeft, 24, contentWidth, 34, Color.White, 16f, true));
				this.turboramaPremiumPanel.Controls.Add(TurboramaPremiumUi.MakeLabel("Componentes essenciais selecionados para compatibilidade maxima.", contentLeft, 58, contentWidth, 24, TurboramaPremiumUi.Muted, 9f, false));

				Panel greenLine = new Panel();
				greenLine.Left = contentLeft;
				greenLine.Top = 88;
				greenLine.Width = 120;
				greenLine.Height = 3;
				greenLine.BackColor = TurboramaPremiumUi.Green;
				this.turboramaPremiumPanel.Controls.Add(greenLine);

				this.TurboramaAddPremiumCheckRow(vc, contentLeft, 112, contentWidth, "Visual C++ Complete", "Runtimes Microsoft 2005-2022 x86 + x64", true);
				this.TurboramaAddPremiumCheckRow(dx, contentLeft, 157, contentWidth, "DirectX Complete", "Legacy June 2010 + DirectX 11/12 pelo Windows Update", true);
				this.TurboramaAddPremiumCheckRow(nvidia, contentLeft, 202, contentWidth, "NVIDIA App", "Detectar e atualizar drivers GeForce", true);
				this.TurboramaAddPremiumCheckRow(dokan, contentLeft, 247, contentWidth, "Dokan / WinFsp", "Suporte para montagem de arquivos e sistemas virtuais", true);

				this.turboramaPremiumPanel.Controls.Add(TurboramaPremiumUi.MakeLabel("Configuracao recomendada pronta para continuar.", contentLeft, 296, contentWidth, 26, TurboramaPremiumUi.Green, 9f, true));

				this.turboramaPremiumPanel.SendToBack();
				this.turboramaPremiumPanel.BringToFront();
				TurboramaPremiumUi.BringNavigationButtonsToFront(this);

				this.turboramaPremiumPanel.ResumeLayout(false);
				base.ResumeLayout(false);
			}
			catch (Exception ex)
			{
				try
				{
					Logger.Log("Failed to build premium prerequisite screen: " + ex.ToString());
				}
				catch
				{
				}
			}
			finally
			{
				this.turboramaPremiumLayoutRunning = false;
			}
		}

		private void TurboramaCollectCheckBoxes(Control parent, List<CheckBox> checkBoxes)
		{
			foreach (Control control in parent.Controls)
			{
				if (control == this.turboramaPremiumPanel)
				{
					continue;
				}

				CheckBox checkBox = control as CheckBox;
				if (checkBox != null)
				{
					checkBoxes.Add(checkBox);
				}

				if (control.HasChildren)
				{
					this.TurboramaCollectCheckBoxes(control, checkBoxes);
				}
			}
		}

		private void TurboramaAddPremiumCheckRow(CheckBox checkBox, int left, int top, int width, string title, string subtitle, bool checkedState)
		{
			if (checkBox == null)
			{
				checkBox = new CheckBox();
			}

			Panel row = new Panel();
			row.Left = left;
			row.Top = top;
			row.Width = width;
			row.Height = 36;
			row.BackColor = TurboramaPremiumUi.PanelMid;
			row.BorderStyle = BorderStyle.FixedSingle;
			this.turboramaPremiumPanel.Controls.Add(row);

			Panel stripe = new Panel();
			stripe.Left = 0;
			stripe.Top = 0;
			stripe.Width = 4;
			stripe.Height = row.Height;
			stripe.BackColor = TurboramaPremiumUi.Green;
			row.Controls.Add(stripe);

			checkBox.Parent = row;
			checkBox.Left = 14;
			checkBox.Top = 8;
			checkBox.Width = 220;
			checkBox.Height = 20;
			checkBox.AutoSize = false;
			checkBox.Text = title;
			checkBox.Visible = true;
			checkBox.Enabled = true;
			checkBox.ThreeState = false;
			checkBox.Checked = checkedState;
			checkBox.CheckState = checkedState ? CheckState.Checked : CheckState.Unchecked;
			TurboramaPremiumUi.StyleCheckBox(checkBox);
			row.Controls.Add(checkBox);
			checkBox.BringToFront();

			Label sub = TurboramaPremiumUi.MakeLabel(subtitle, 245, 8, row.Width - 255, 20, TurboramaPremiumUi.Muted, 8.2f, false);
			row.Controls.Add(sub);

			if (title.ToLowerInvariant().Contains("nvidia"))
			{
				this.chkNvidiaApp = checkBox;
			}
		}
		private void TurboramaOrganizePrerequisiteLayout()
		{
			this.TurboramaBuildPremiumPrerequisites();
		}
		private void TurboramaStartForcePrereqV13()
		{
			try
			{
				this.TurboramaForcePrerequisiteStateV13();

				if (!this.IsDisposed)
				{
					this.BeginInvoke(new MethodInvoker(delegate()
					{
						this.TurboramaForcePrerequisiteStateV13();
					}));
				}

				Timer timer = new Timer();
				int count = 0;
				timer.Interval = 150;
				timer.Tick += delegate(object sender, EventArgs e)
				{
					count++;
					this.TurboramaForcePrerequisiteStateV13();

					if (count >= 15)
					{
						timer.Stop();
						timer.Dispose();
					}
				};
				timer.Start();
			}
			catch
			{
			}
		}

		private void TurboramaForcePrerequisiteStateV13()
		{
			try
			{
				this.TurboramaFixPremiumPanelHeightV13();

				this.TurboramaForceChecksV13(this);

				Form form = this.FindForm();
				if (form != null)
				{
					this.TurboramaForceChecksV13(form);
					this.TurboramaForceWizardButtonsV13(form);
				}

				this.TurboramaForceWizardButtonsV13(this);
			}
			catch (Exception ex)
			{
				try
				{
					Logger.Log("TurboramaForcePrerequisiteStateV13 failed: " + ex.ToString());
				}
				catch
				{
				}
			}
		}

		private void TurboramaFixPremiumPanelHeightV13()
		{
			try
			{
				foreach (Control control in this.Controls)
				{
					if (control != null && control.Name != null && control.Name.ToLowerInvariant().Contains("turboramapremiumpanel"))
					{
						control.Top = 0;
						control.Left = 0;
						control.Width = this.Width;
						control.Height = Math.Max(250, this.Height - 72);
						control.Visible = true;
					}
				}
			}
			catch
			{
			}
		}

		private void TurboramaForceChecksV13(Control parent)
		{
			if (parent == null)
			{
				return;
			}

			foreach (Control control in parent.Controls)
			{
				CheckBox checkBox = control as CheckBox;
				if (checkBox != null)
				{
					try
					{
						string info = ((checkBox.Name ?? string.Empty) + " " + (checkBox.Text ?? string.Empty)).ToLowerInvariant();

						if (info.Contains("visual") || info.Contains("c++") || info.Contains("vc++") || info.Contains("vcredist") ||
							info.Contains("directx") || info.Contains("nvidia") || info.Contains("geforce") ||
							info.Contains("dokan") || info.Contains("winfsp"))
						{
							checkBox.Enabled = true;
							checkBox.ThreeState = false;
							checkBox.Checked = true;
							checkBox.CheckState = CheckState.Checked;

							if (checkBox.Visible)
							{
								checkBox.ForeColor = Color.White;
								checkBox.BackColor = Color.FromArgb(18, 24, 20);
							}

							if (info.Contains("nvidia") || info.Contains("geforce"))
							{
								this.chkNvidiaApp = checkBox;
							}
						}
					}
					catch
					{
					}
				}

				if (control.HasChildren)
				{
					this.TurboramaForceChecksV13(control);
				}
			}
		}

		private void TurboramaForceWizardButtonsV13(Control parent)
		{
			if (parent == null)
			{
				return;
			}

			foreach (Control control in parent.Controls)
			{
				Button button = control as Button;
				if (button != null)
				{
					try
					{
						string info = ((button.Name ?? string.Empty) + " " + (button.Text ?? string.Empty)).ToLowerInvariant();

						if (info.Contains("next") || info.Contains("back") || info.Contains("cancel") ||
							info.Contains("avancar") || info.Contains("avanÃ§ar") || info.Contains("voltar") || info.Contains("cancelar"))
						{
							button.Visible = true;
							button.Enabled = true;
							button.FlatStyle = FlatStyle.Flat;
							button.FlatAppearance.BorderColor = Color.FromArgb(112, 255, 32);
							button.FlatAppearance.BorderSize = 1;
							button.BackColor = Color.FromArgb(16, 21, 18);
							button.ForeColor = Color.White;
							button.BringToFront();
						}
					}
					catch
					{
					}
				}

				if (control.HasChildren)
				{
					this.TurboramaForceWizardButtonsV13(control);
				}
			}
		}
		// TURBORAMA_FORCE_PREREQ_V13_END

		private void TurboramaEnsurePrerequisitesReadyV15()
		{
			try
			{
				this.TurboramaMarkChecksAndButtonsV15(this);

				Form form = this.FindForm();
				if (form != null)
				{
					this.TurboramaMarkChecksAndButtonsV15(form);
				}
			}
			catch
			{
			}
		}

		private void TurboramaMarkChecksAndButtonsV15(Control parent)
		{
			if (parent == null)
			{
				return;
			}

			foreach (Control control in parent.Controls)
			{
				CheckBox checkBox = control as CheckBox;
				if (checkBox != null)
				{
					string info = ((checkBox.Name ?? string.Empty) + " " + (checkBox.Text ?? string.Empty)).ToLowerInvariant();

					if (info.Contains("visual") || info.Contains("c++") || info.Contains("vc++") || info.Contains("vcredist") ||
						info.Contains("directx") || info.Contains("nvidia") || info.Contains("geforce") ||
						info.Contains("dokan") || info.Contains("winfsp"))
					{
						checkBox.Enabled = true;
						checkBox.ThreeState = false;
						checkBox.Checked = true;
						checkBox.CheckState = CheckState.Checked;

						if (checkBox.Visible)
						{
							checkBox.ForeColor = Color.White;
							checkBox.BackColor = Color.FromArgb(18, 24, 20);
						}

						if (info.Contains("nvidia") || info.Contains("geforce"))
						{
							this.chkNvidiaApp = checkBox;
						}
					}
				}

				Button button = control as Button;
				if (button != null)
				{
					string info = ((button.Name ?? string.Empty) + " " + (button.Text ?? string.Empty)).ToLowerInvariant();

					if (info.Contains("next") || info.Contains("back") || info.Contains("cancel") ||
						info.Contains("avancar") || info.Contains("avanÃ§ar") || info.Contains("voltar") || info.Contains("cancelar"))
					{
						button.Visible = true;
						button.Enabled = true;
						button.BringToFront();
					}
				}

				if (control.HasChildren)
				{
					this.TurboramaMarkChecksAndButtonsV15(control);
				}
			}
		}
	}
}