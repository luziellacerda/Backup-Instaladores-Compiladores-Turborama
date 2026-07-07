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
		private Label premiumProgressTitleLabel;
		private Label premiumProgressDetailLabel;
		private Label premiumProgressCountLabel;
		private Label premiumProgressPercentLabel;
		private Label premiumProgressHintLabel;
		private Panel premiumProgressTrackPanel;
		private Panel premiumProgressFillPanel;
		private string premiumProgressTitleText = "Pronto para iniciar";
		private string premiumProgressDetailText = "Selecione os componentes e clique em Next para continuar.";
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

			this.UpdateStatusLabelSafe("Aguardando início da instalação dos componentes.");
			this.UpdatePrerequisiteCheckboxes();
			this.CreateNvidiaDriverCheckbox();
			this.TurboramaBuildPremiumPrerequisites();

			this.Load += delegate(object s, EventArgs e)
			{
				if (!this.IsInstallationRunning())
				{
					this.UpdatePrerequisiteCheckboxes();
					this.CreateNvidiaDriverCheckbox();
				}
				this.TurboramaBuildPremiumPrerequisites();
			};

			this.VisibleChanged += delegate(object s, EventArgs e)
			{
				if (this.Visible)
				{
					if (!this.IsInstallationRunning())
					{
						this.UpdatePrerequisiteCheckboxes();
						this.CreateNvidiaDriverCheckbox();
					}
					this.TurboramaBuildPremiumPrerequisites();
				}
			};

			this.Resize += delegate(object s, EventArgs e)
			{
				if (this.Visible)
				{
					this.TurboramaBuildPremiumPrerequisites();
				}
			};
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
			bool hasNvidia = NvidiaAppInstallerHelper.HasNvidiaGpu();

			if (this.chkNvidiaApp == null)
			{
				this.chkNvidiaApp = new CheckBox();
				this.chkNvidiaApp.AutoSize = true;
				this.chkNvidiaApp.Location = new Point(24, 221);
				this.chkNvidiaApp.Name = "chkNvidiaApp";
				this.chkNvidiaApp.Size = new Size(420, 17);
				this.chkNvidiaApp.TabIndex = 6;
				base.Controls.Add(this.chkNvidiaApp);
			}

			this.chkNvidiaApp.Checked = hasNvidia;
			this.chkNvidiaApp.Enabled = hasNvidia;
			this.chkNvidiaApp.Text = hasNvidia ? "NVIDIA App (detectar/atualizar drivers GeForce)" : "NVIDIA App (GPU NVIDIA não detectada)";
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
			if (!this.IsInstallationRunning())
			{
				this.UpdatePrerequisiteCheckboxes();
				this.CreateNvidiaDriverCheckbox();
			}
			this.TurboramaBuildPremiumPrerequisites();
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
			if (this.installationComplete)
			{
				this.mainForm.ShowInstall();
				return;
			}

			if (this.IsInstallationRunning())
			{
				return;
			}

			PrerequisiteSelection selection = this.GetPrerequisiteSelection();
			int totalSteps = this.GetSelectedStepCount(selection);

			if (totalSteps <= 0)
			{
				Logger.Log("No prerequisites selected, showing Install screen.");
				this.installationComplete = true;
				this.mainForm.ShowInstall();
				return;
			}

			this.progressBar.Maximum = Math.Max(1, totalSteps);
			this.progressBar.Value = 0;
			this.progressBar.Visible = false;
			this.statusLabel.Visible = false;

			this.SetPremiumButtonsInstallingState(true);
			this.SetPremiumProgressHeaderSafe("Instalando componentes", "Preparando downloads e instalações...");
			this.UpdatePremiumProgressVisualsSafe();

			this.installerWorker = new BackgroundWorker();
			this.installerWorker.DoWork += this.InstallerWorker_DoWork;
			this.installerWorker.RunWorkerCompleted += this.InstallerWorker_RunWorkerCompleted;
			this.installerWorker.RunWorkerAsync(selection);
		}

		// Token: 0x06000042 RID: 66 RVA: 0x00005514 File Offset: 0x00003714
		private void InstallerWorker_DoWork(object sender, DoWorkEventArgs e)
		{
			PrerequisiteSelection selection = e.Argument as PrerequisiteSelection;
			if (selection == null)
			{
				selection = new PrerequisiteSelection();
			}

			try
			{
				Logger.Log("Installing complete offline gaming runtime stack...");
				RuntimeInstallerHelper.InstallCompleteGamingRuntimeStack(
					this.SetPremiumProgressHeaderSafe,
					this.NormalizeSilentArguments,
					delegate(string zipPath, string destination, Action<int> progress)
					{
						this.ExtractZipToFolder(zipPath, destination, progress);
					});

				while (this.progressBar.Value < this.progressBar.Maximum - 1)
				{
					this.UpdateProgressBarSafe();
				}

				if (selection.InstallNvidiaApp)
				{
					Logger.Log("Launching NVIDIA App installer...");
					NvidiaAppInstallerHelper.InstallOrOpenNvidiaApp();
					this.UpdateProgressBarSafe();
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
			this.SetPremiumButtonsInstallingState(false);

			Exception ex = e.Result as Exception;
			if (ex != null)
			{
				Logger.Log("Prerequisite installation error: " + ex.Message);
				this.SetPremiumProgressHeaderSafe("Falha na instalação", ex.Message);
				this.installationComplete = false;
				MessageBox.Show(
					"A instalação dos componentes obrigatórios falhou. O TurboRama NÃO pode ser instalado sem eles." + Environment.NewLine + Environment.NewLine +
					"Detalhes: " + ex.Message + Environment.NewLine + Environment.NewLine +
					"Execute o instalador novamente como Administrador. Se persistir, recompile o InstallerHost com Baixar_Prerequisitos_Instalador.bat.",
					"Componentes obrigatórios",
					MessageBoxButtons.OK,
					MessageBoxIcon.Hand);
				return;
			}

			this.UpdateStatusLabelSafe(Texts.GetString("InstallComplete", Array.Empty<object>()));
			this.installationComplete = true;
			if (this.progressBar.Maximum > 0)
			{
				this.progressBar.Value = this.progressBar.Maximum;
			}
			this.SetPremiumProgressHeaderSafe("Componentes concluídos", "Todos os requisitos foram concluídos. Avançando para a instalação principal...");
			this.UpdatePremiumProgressVisualsSafe();

			Logger.Log("Prerequisites completed, showing Install screen.");
			this.mainForm.ShowInstall();
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
					this.SetPremiumProgressHeaderSafe("Baixando componente", key);
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
					this.SetPremiumProgressHeaderSafe("Extraindo componente", key);
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
					this.SetPremiumProgressHeaderSafe("Instalando DirectX", "DirectX Legacy June 2010");
					this.UpdateStatusLabelSafe(Texts.GetString("InstallDX", Array.Empty<object>()));
					Process process2 = new Process();
					process2.StartInfo.FileName = text10;
					process2.StartInfo.Arguments = "/silent";
					process2.StartInfo.UseShellExecute = false;
					process2.StartInfo.CreateNoWindow = true;
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
			Logger.Log("DirectX 11/12 prompt skipped to keep prerequisite flow silent and professional.");
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
					this.SetPremiumProgressHeaderSafe("Baixando componente", key);
					this.UpdateStatusLabelSafe(Texts.GetString("Downloading", Array.Empty<object>()) + " " + key + "...");
					Logger.Log("Downloading ZIP from " + text5 + "...");
					this.DownloadWithWget(text, text5, text3);
					Logger.Log("Download and extraction complete.");
					this.ExtractZipToFolder(text3, text4, null);
					Logger.Log("Extraction complete.");
					this.SetPremiumProgressHeaderSafe("Instalando componente", key);
					this.UpdateStatusLabelSafe(Texts.GetString("Installing", Array.Empty<object>()) + " " + key + "...");
					string text6 = Path.Combine(text4, key.Replace(".zip", ".exe"));
					if (!File.Exists(text6))
					{
						throw new FileNotFoundException("Installer not found: " + text6);
					}
					Process process = new Process();
					process.StartInfo.FileName = text6;
					process.StartInfo.Arguments = this.NormalizeSilentArguments(key, value.Arguments);
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.CreateNoWindow = true;
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
					this.SetPremiumProgressHeaderSafe("Baixando componente", key);
					this.UpdateStatusLabelSafe(Texts.GetString("Downloading", Array.Empty<object>()) + " " + key + "...");
					Logger.Log("Downloading ZIP from " + text5 + "...");
					this.DownloadWithWget(text, text5, text3);
					Logger.Log("Download and extraction complete.");
					this.ExtractZipToFolder(text3, text4, null);
					Logger.Log("Extraction complete.");
					this.SetPremiumProgressHeaderSafe("Instalando componente", key);
					this.UpdateStatusLabelSafe(Texts.GetString("Installing", Array.Empty<object>()) + " " + key + "...");
					string text6 = Path.Combine(text4, key.Replace(".zip", ".exe"));
					if (!File.Exists(text6))
					{
						throw new FileNotFoundException("Installer not found: " + text6);
					}
					Process process = new Process();
					process.StartInfo.FileName = text6;
					process.StartInfo.Arguments = this.NormalizeSilentArguments(key, value.Arguments);
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.CreateNoWindow = true;
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
						this.SetPremiumProgressHeaderSafe("Baixando componente", key);
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
						this.SetPremiumProgressHeaderSafe("Instalando WinFsp", Path.GetFileName(text6));
						this.UpdateStatusLabelSafe(Texts.GetString("Installing", Array.Empty<object>()) + " " + Path.GetFileName(text6) + "...");
						Logger.Log("Running WinFsp MSI installer: " + text6);
						Process process = new Process();
						process.StartInfo.FileName = "msiexec.exe";
						process.StartInfo.Arguments = "/i \"" + text6 + "\" /qn /norestart";
						process.StartInfo.UseShellExecute = false;
						process.StartInfo.CreateNoWindow = true;
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
				this.progressBar.Invoke(new Action(this.UpdateProgressBarSafe));
				return;
			}

			if (this.progressBar.Value < this.progressBar.Maximum)
			{
				this.progressBar.Value++;
			}

			this.UpdatePremiumProgressVisualsSafe();
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
			string destinationRoot = Path.GetFullPath(destinationFolder);
			if (!destinationRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
			{
				destinationRoot += Path.DirectorySeparatorChar;
			}

			using (FileStream fileStream = File.OpenRead(zipFilePath))
			{
				using (ZipFile zipFile = new ZipFile(fileStream))
				{
					long totalSize = 0L;
					foreach (object obj in zipFile)
					{
						ZipEntry zipEntry = (ZipEntry)obj;
						if (zipEntry.IsFile && zipEntry.Size > 0L)
						{
							totalSize += zipEntry.Size;
						}
					}

					long extractedSize = 0L;
					foreach (object obj2 in zipFile)
					{
						ZipEntry zipEntry2 = (ZipEntry)obj2;
						if (!zipEntry2.IsFile)
						{
							continue;
						}

						string safeEntryName = zipEntry2.Name.Replace('/', Path.DirectorySeparatorChar);
						string outputFile = Path.GetFullPath(Path.Combine(destinationRoot, safeEntryName));
						if (!outputFile.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
						{
							throw new IOException("Unsafe ZIP entry path: " + zipEntry2.Name);
						}

						string directoryName = Path.GetDirectoryName(outputFile);
						if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
						{
							Directory.CreateDirectory(directoryName);
						}

						using (Stream inputStream = zipFile.GetInputStream(zipEntry2))
						{
							using (FileStream fileStream2 = File.Create(outputFile))
							{
								byte[] array = new byte[8192];
								int bytesRead;
								while ((bytesRead = inputStream.Read(array, 0, array.Length)) > 0)
								{
									fileStream2.Write(array, 0, bytesRead);
									extractedSize += (long)bytesRead;
									if (progress != null && totalSize > 0L)
									{
										int percent = (int)(extractedSize * 100L / totalSize);
										progress(percent);
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

			if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0L)
			{
				throw new IOException("Download failed or created an empty file: " + outputPath);
			}
		}

		// Token: 0x0600004E RID: 78 RVA: 0x000068F0 File Offset: 0x00004AF0
		private void UpdateStatusLabelSafe(string text)
		{
			if (this.statusLabel.InvokeRequired)
			{
				this.statusLabel.Invoke(new Action<string>(this.UpdateStatusLabelSafe), text);
				return;
			}

			this.statusLabel.Text = text;
			this.statusLabel.Visible = false;
			this.SetPremiumProgressHeaderSafe(this.premiumProgressTitleText, text);
		}

		// Token: 0x0600004F RID: 79 RVA: 0x00006948 File Offset: 0x00004B48
		private void UpdatePrerequisiteCheckboxes()
		{
			try
			{
				if (this.IsInstallationRunning())
				{
					return;
				}

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

				this.CreateNvidiaDriverCheckbox();
				this.lblAllInstalled.Visible = false;
				this.statusLabel.Visible = false;
				this.progressBar.Visible = false;
				this.progressBar.Value = 0;
				this.progressBar.Maximum = Math.Max(1, this.GetSelectedStepCount(this.GetPrerequisiteSelection()));

				this.btnBack.Visible = true;
				this.btnNext.Visible = true;
				this.btnCancel.Visible = true;
				this.SetPremiumButtonsInstallingState(false);
				this.SetPremiumProgressHeaderSafe("Pronto para iniciar", "Selecione os componentes desejados e clique em Next para continuar.");
				this.UpdatePremiumProgressVisualsSafe();
			}
			catch (Exception ex)
			{
				Logger.Log("Error setting prerequisite checkboxes: " + ex.Message);
			}
		}


		private bool IsInstallationRunning()
		{
			return this.installerWorker != null && this.installerWorker.IsBusy;
		}

		private void SetPremiumButtonsInstallingState(bool installing)
		{
			this.btnBack.Visible = true;
			this.btnNext.Visible = true;
			this.btnCancel.Visible = true;
			this.btnBack.Enabled = !installing;
			this.btnCancel.Enabled = !installing;
			this.btnNext.Enabled = !installing;
			this.btnNext.Text = installing ? "Instalando..." : Texts.GetString("Next >", Array.Empty<object>());
			this.btnBack.BringToFront();
			this.btnNext.BringToFront();
			this.btnCancel.BringToFront();
		}

		private void SetPremiumProgressHeaderSafe(string title, string detail)
		{
			if (string.IsNullOrWhiteSpace(title))
			{
				title = "Instalando componentes";
			}
			if (string.IsNullOrWhiteSpace(detail))
			{
				detail = "Aguardando processamento...";
			}

			this.premiumProgressTitleText = title;
			this.premiumProgressDetailText = detail;

			if (this.InvokeRequired)
			{
				this.Invoke(new Action<string, string>(this.SetPremiumProgressHeaderSafe), title, detail);
				return;
			}

			if (this.premiumProgressTitleLabel != null)
			{
				this.premiumProgressTitleLabel.Text = title;
			}

			if (this.premiumProgressDetailLabel != null)
			{
				this.premiumProgressDetailLabel.Text = detail;
			}
		}

		private void UpdatePremiumProgressVisualsSafe()
		{
			if (this.InvokeRequired)
			{
				this.Invoke(new Action(this.UpdatePremiumProgressVisualsSafe));
				return;
			}

			int maximum = Math.Max(1, this.progressBar.Maximum);
			int value = Math.Max(0, Math.Min(this.progressBar.Value, maximum));
			int percent = (int)Math.Round((double)value * 100.0 / (double)maximum);

			if (this.premiumProgressCountLabel != null)
			{
				this.premiumProgressCountLabel.Text = string.Format("Concluídos: {0} de {1}", value, maximum);
			}

			if (this.premiumProgressPercentLabel != null)
			{
				this.premiumProgressPercentLabel.Text = percent.ToString() + "%";
			}

			if (this.premiumProgressHintLabel != null)
			{
				this.premiumProgressHintLabel.Text = value >= maximum ? "Todos os componentes foram processados." : "O instalador mantém o fundo ativo enquanto processa cada etapa.";
			}

			if (this.premiumProgressFillPanel != null && this.premiumProgressTrackPanel != null)
			{
				int trackWidth = Math.Max(1, this.premiumProgressTrackPanel.ClientSize.Width);
				int fillWidth = (int)Math.Round((double)trackWidth * (double)value / (double)maximum);
				this.premiumProgressFillPanel.Width = Math.Max(0, Math.Min(trackWidth, fillWidth));
				this.premiumProgressFillPanel.Height = this.premiumProgressTrackPanel.Height;
			}

			if (this.premiumProgressTitleLabel != null)
			{
				this.premiumProgressTitleLabel.Text = this.premiumProgressTitleText;
			}

			if (this.premiumProgressDetailLabel != null)
			{
				this.premiumProgressDetailLabel.Text = this.premiumProgressDetailText;
			}
		}

		private string NormalizeSilentArguments(string packageName, string originalArguments)
		{
			string name = (packageName ?? string.Empty).ToLowerInvariant();
			string args = (originalArguments ?? string.Empty).Trim();
			if (name.Contains("2005") || name.Contains("2008"))
			{
				return "/q";
			}
			if (name.Contains("2010") || name.Contains("2012") || name.Contains("2013") || name.Contains("2015"))
			{
				return "/passive /norestart";
			}
			if (name.Contains("dokan"))
			{
				return "/quiet /norestart";
			}
			return args;
		}


		private PrerequisiteSelection GetPrerequisiteSelection()
		{
			PrerequisiteSelection selection = new PrerequisiteSelection();
			selection.InstallVCpp = this.chkVCpp.Enabled && this.chkVCpp.Checked;
			selection.InstallDirectX = this.chkDirectX.Enabled && this.chkDirectX.Checked;
			selection.InstallDokany = this.chkDokany.Enabled && this.chkDokany.Checked;
			selection.InstallWinFsp = this.chkwinFSP.Enabled && this.chkwinFSP.Checked;
			selection.InstallNvidiaApp = this.chkNvidiaApp != null && this.chkNvidiaApp.Enabled && this.chkNvidiaApp.Checked;
			return selection;
		}

		private Dictionary<string, InstallerInfo> GetLegacyVcRedistResources()
		{
			Dictionary<string, InstallerInfo> legacy = new Dictionary<string, InstallerInfo>();
			foreach (KeyValuePair<string, InstallerInfo> entry in this.vcRedistResources)
			{
				if (entry.Key.IndexOf("2015", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					continue;
				}
				legacy.Add(entry.Key, entry.Value);
			}
			return legacy;
		}

		private int GetSelectedStepCount(PrerequisiteSelection selection)
		{
			int total = 24;
			if (selection.InstallNvidiaApp)
			{
				total++;
			}
			return total;
		}

		private class PrerequisiteSelection
		{
			public bool InstallVCpp;
			public bool InstallDirectX;
			public bool InstallDokany;
			public bool InstallWinFsp;
			public bool InstallNvidiaApp;
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

				if (nvidia == null && this.chkNvidiaApp != null)
				{
					nvidia = this.chkNvidiaApp;
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

				bool hasNvidiaForRow = this.chkNvidiaApp != null && this.chkNvidiaApp.Enabled && this.chkNvidiaApp.Checked;

					this.TurboramaAddPremiumCheckRow(vc, contentLeft, 112, contentWidth, "Visual C++ Complete", "Runtimes Microsoft 2005-2022 x86 + x64", true);
				this.TurboramaAddPremiumCheckRow(dx, contentLeft, 157, contentWidth, "DirectX Complete", "Legacy June 2010 + DirectX 11/12 pelo Windows Update", true);
				this.TurboramaAddPremiumCheckRow(nvidia, contentLeft, 202, contentWidth, "NVIDIA App", hasNvidiaForRow ? "Detectar e atualizar drivers GeForce" : "GPU NVIDIA não detectada", hasNvidiaForRow, hasNvidiaForRow);
				this.TurboramaAddPremiumCheckRow(dokan, contentLeft, 247, contentWidth, "Dokan / WinFsp", "Suporte para montagem de arquivos e sistemas virtuais", true);

				this.turboramaPremiumPanel.Controls.Add(TurboramaPremiumUi.MakeLabel("Configuracao recomendada pronta para continuar.", contentLeft, 296, contentWidth, 26, TurboramaPremiumUi.Green, 9f, true));

				this.premiumProgressTitleLabel = TurboramaPremiumUi.MakeLabel(this.premiumProgressTitleText, contentLeft, 338, contentWidth - 110, 24, Color.White, 10.2f, true);
				this.turboramaPremiumPanel.Controls.Add(this.premiumProgressTitleLabel);

				this.premiumProgressPercentLabel = TurboramaPremiumUi.MakeLabel("0%", contentLeft + contentWidth - 90, 338, 90, 24, TurboramaPremiumUi.Green, 10.2f, true);
				this.premiumProgressPercentLabel.TextAlign = ContentAlignment.MiddleRight;
				this.turboramaPremiumPanel.Controls.Add(this.premiumProgressPercentLabel);

				this.premiumProgressDetailLabel = TurboramaPremiumUi.MakeLabel(this.premiumProgressDetailText, contentLeft, 364, contentWidth, 28, TurboramaPremiumUi.Muted, 8.6f, false);
				this.turboramaPremiumPanel.Controls.Add(this.premiumProgressDetailLabel);

				this.premiumProgressTrackPanel = new Panel();
				this.premiumProgressTrackPanel.Left = contentLeft;
				this.premiumProgressTrackPanel.Top = 400;
				this.premiumProgressTrackPanel.Width = Math.Max(160, contentWidth);
				this.premiumProgressTrackPanel.Height = 18;
				this.premiumProgressTrackPanel.BackColor = Color.FromArgb(48, 48, 48);
				this.premiumProgressTrackPanel.BorderStyle = BorderStyle.FixedSingle;
				this.turboramaPremiumPanel.Controls.Add(this.premiumProgressTrackPanel);

				this.premiumProgressFillPanel = new Panel();
				this.premiumProgressFillPanel.Left = 0;
				this.premiumProgressFillPanel.Top = 0;
				this.premiumProgressFillPanel.Height = this.premiumProgressTrackPanel.Height;
				this.premiumProgressFillPanel.Width = 0;
				this.premiumProgressFillPanel.BackColor = TurboramaPremiumUi.Green;
				this.premiumProgressTrackPanel.Controls.Add(this.premiumProgressFillPanel);

				this.premiumProgressCountLabel = TurboramaPremiumUi.MakeLabel("Concluídos: 0 de " + Math.Max(1, this.progressBar.Maximum).ToString(), contentLeft, 426, contentWidth, 22, Color.White, 8.8f, true);
				this.turboramaPremiumPanel.Controls.Add(this.premiumProgressCountLabel);

				this.premiumProgressHintLabel = TurboramaPremiumUi.MakeLabel("O instalador mantém o fundo ativo enquanto processa cada etapa.", contentLeft, 448, contentWidth, 22, TurboramaPremiumUi.Muted, 8.4f, false);
				this.turboramaPremiumPanel.Controls.Add(this.premiumProgressHintLabel);

				this.UpdatePremiumProgressVisualsSafe();

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

		private void TurboramaAddPremiumCheckRow(CheckBox checkBox, int left, int top, int width, string title, string subtitle, bool checkedState, bool enabledState = true)
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
			stripe.BackColor = enabledState ? TurboramaPremiumUi.Green : TurboramaPremiumUi.Muted;
			row.Controls.Add(stripe);

			checkBox.Parent = row;
			checkBox.Left = 14;
			checkBox.Top = 8;
			checkBox.Width = 220;
			checkBox.Height = 20;
			checkBox.AutoSize = false;
			checkBox.Text = title;
			checkBox.Visible = true;
			checkBox.Enabled = enabledState;
			checkBox.ThreeState = false;
			checkBox.Checked = checkedState && enabledState;
			checkBox.CheckState = (checkedState && enabledState) ? CheckState.Checked : CheckState.Unchecked;
			TurboramaPremiumUi.StyleCheckBox(checkBox);
			row.Controls.Add(checkBox);
			checkBox.BringToFront();

			Label sub = TurboramaPremiumUi.MakeLabel(subtitle, 245, 8, row.Width - 255, 20, enabledState ? TurboramaPremiumUi.Muted : Color.FromArgb(110, 110, 110), 8.2f, false);
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
							info.Contains("directx") || info.Contains("dokan") || info.Contains("winfsp"))
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