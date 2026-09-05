using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

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

this.wizardHeader.Text = ConsumerText.GetString("InstallTitle", Array.Empty<object>());
			this.txtInfo.Text = ConsumerText.GetString("InstallInfo", Array.Empty<object>());
			this.lblSelectFolder.Text = ConsumerText.GetString("SelectFolder", Array.Empty<object>());
			this.btnBrowse.Text = ConsumerText.GetString("Browse...", Array.Empty<object>());
			this.lblFolderHint.Text = ConsumerText.GetString("InstallFolderHint", Array.Empty<object>());
			this.btnCancel.Text = ConsumerText.GetString("Cancel", Array.Empty<object>());
			this.btnInstall.Text = ConsumerText.GetString("Install", Array.Empty<object>());
			this.btnBack.Text = ConsumerText.GetString("< Back", Array.Empty<object>());
		}

		// Token: 0x0600001A RID: 26 RVA: 0x000036AC File Offset: 0x000018AC
		private void BtnBack_Click(object sender, EventArgs e)
		{
			if (IsExtractionInProgress) return;
			this.mainForm.ShowPrerequisites(false);
		}

		// Token: 0x0600001B RID: 27 RVA: 0x000036BA File Offset: 0x000018BA
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			if (string.IsNullOrWhiteSpace(this.txtFolder.Text)) this.txtFolder.Text = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"TurboRama");
			base.ActiveControl = this.btnInstall;
		}

		// Token: 0x0600001C RID: 28 RVA: 0x000036E0 File Offset: 0x000018E0
		private void BtnBrowse_Click(object sender, EventArgs e)
		{
			if (IsExtractionInProgress) return;
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
			if (IsExtractionInProgress) return;
			if (string.IsNullOrWhiteSpace(this.txtFolder.Text))
			{
				Logger.Log("[WARNING] No installation folder selected.");
				MessageBox.Show("Selecione uma pasta de instalação válida.");
				return;
			}
			// Capture and canonicalize on the UI thread. BackgroundWorker must never
			// read a WinForms control directly, and limited-token extraction never
			// overwrites an existing file/directory tree.
			string destinationFolder;
			try
			{
				destinationFolder = SecureExtractionGuard.ValidateDestinationSelection(this.txtFolder.Text);
			}
			catch (Exception ex)
			{
				Logger.Log("[WARNING] Unsafe or non-empty installation folder: " + ex.Message);
				MessageBox.Show(this, "Selecione uma pasta local vazia e segura: " + ex.Message,
					"Destino não aceito", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			this.txtInfo.Visible = false;
			this.progressBar.Visible = true;
			this.btnBack.Enabled = false;
			this.btnInstall.Enabled = false;
			this.btnBrowse.Enabled = false;
			this.txtFolder.ReadOnly = true;
			this.btnCancel.Enabled = false;
			this.worker = new BackgroundWorker
			{
				WorkerReportsProgress = true
			};
			this.worker.DoWork += delegate(object workSender, DoWorkEventArgs workArgs)
			{
				try
				{
					// The host stays elevated for prerequisite installers, but every read,
					// destination mutation, validation and rollback of the product package
					// happens under the linked standard/Medium token.
					LimitedUserImpersonation.Run(delegate
					{
						using (Stream installerZipStream = this.GetInstallerZipStream())
						{
							SecureExtractionGuard extractionGuard = null;
							try
							{
								extractionGuard = SecureExtractionGuard.Create(destinationFolder);
								try
								{
									SecureProductExtractor.Extract(installerZipStream, extractionGuard, delegate(int progress)
									{
										BackgroundWorker activeWorker = this.worker;
										if (activeWorker != null)
										{
											activeWorker.ReportProgress(progress);
										}
									});
									this.ValidateExtractedInstallation(destinationFolder);
									extractionGuard.Commit();
								}
								catch (Exception extractionError)
								{
									try
									{
										extractionGuard.RollbackCreatedEntries();
									}
									catch (Exception rollbackError)
									{
										throw new AggregateException(
											"A instalação falhou e a reversão segura ficou incompleta. Não reutilize esta pasta.",
											extractionError,
											rollbackError);
									}
									throw;
								}
							}
							finally
							{
								if (extractionGuard != null)
								{
									extractionGuard.Dispose();
								}
							}
						}
					});
				}
				catch (UnauthorizedAccessException accessError)
				{
					workArgs.Result = new UnauthorizedAccessException(
						"A extração segura roda sem privilégios administrativos. Selecione uma pasta local vazia gravável pela conta padrão. " +
						accessError.Message,
						accessError);
				}
				catch (Exception ex2)
				{
					workArgs.Result = ex2;
				}
			};
			this.worker.ProgressChanged += delegate(object progressSender, ProgressChangedEventArgs progressArgs)
			{
				int pct = progressArgs.ProgressPercentage;
				if (pct < 0) pct = 0;
				if (pct > 100) pct = 100;
				if (this.progressBar.InvokeRequired)
				{
					this.progressBar.Invoke(new Action(delegate
					{
						this.progressBar.Value = pct;
					}));
					return;
				}
				this.progressBar.Value = pct;
			};
			this.worker.RunWorkerCompleted += delegate(object completeSender, RunWorkerCompletedEventArgs completeArgs)
			{
				this.worker = null;
				this.btnInstall.Enabled = true;
				this.btnBrowse.Enabled = true;
				this.txtFolder.ReadOnly = false;
				this.btnBack.Enabled = true;
				this.btnCancel.Enabled = true;
				this.progressBar.Visible = false;
				this.txtInfo.Visible = true;
				// Superficie erros nao capturados no DoWork (e.Error) e Result
				Exception ex3 = completeArgs.Error ?? (completeArgs.Result as Exception);
				if (ex3 != null)
				{
					MessageBox.Show(this, "Falha na instalação: " + DownloadDisplayMask.Apply(ex3.Message), null, MessageBoxButtons.OK, MessageBoxIcon.Hand);
					return;
				}
				Logger.Log("Installation successful, showing finish screen.");
				this.mainForm.ShowFinish(destinationFolder);
			};
			this.worker.RunWorkerAsync();
		}

		// Token: 0x0600001E RID: 30 RVA: 0x00003880 File Offset: 0x00001A80
		public void BtnCancel_Click(object sender, EventArgs e)
		{
			if (this.IsExtractionInProgress)
			{
				MessageBox.Show(this,
					"Aguarde a extração e a validação terminarem. O instalador não pode ser fechado durante a transação.",
					"Instalação em andamento",
					MessageBoxButtons.OK,
					MessageBoxIcon.Information);
				return;
			}
			if (MessageBox.Show("Tem certeza que deseja cancelar a instalação?", "Cancelar instalação", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				Application.Exit();
			}
		}

		// Token: 0x0600001F RID: 31 RVA: 0x000038B0 File Offset: 0x00001AB0
		private Stream GetInstallerZipStream()
		{
			try
			{
				VerifiedProductPackageStream verifiedPackage =
					ProductPackageSecurity.OpenVerifiedPackage(Application.ExecutablePath);
				Logger.Log(
					"Using SHA-256 verified split product package with " +
					verifiedPackage.PartPaths.Length + " locked part(s); logical archive: " +
					verifiedPackage.LogicalArchiveName);
				return verifiedPackage;
			}
			catch (Exception ex)
			{
				string setupExe = Application.ExecutablePath;
				string setupFolder = Path.GetDirectoryName(setupExe);
				string setupName = Path.GetFileName(setupExe);
				throw new Exception(
					"Pacote de instalação não encontrado." + Environment.NewLine + Environment.NewLine +
					"O instalador TurboRama precisa de TODOS estes arquivos na mesma pasta:" + Environment.NewLine +
					"  " + setupName + Environment.NewLine +
					"  " + setupName + ".pkg.001" + Environment.NewLine +
					"  " + setupName + ".pkg.002 (se existir)" + Environment.NewLine +
					"  ..." + Environment.NewLine +
					"  " + setupName + ".sha256.txt" + Environment.NewLine + Environment.NewLine +
					"O sidecar deve conter os hashes SHA-256 do próprio setup, de cada parte e do ZIP lógico." + Environment.NewLine +
					"O formato legado sem sidecar não é aceito por segurança." + Environment.NewLine + Environment.NewLine +
					"Pasta atual: " + setupFolder + Environment.NewLine + Environment.NewLine +
					"Detalhe técnico: " + ex.Message,
					ex);
			}
		}

		// Token: 0x04000018 RID: 24
		private MainForm mainForm;

		// Token: 0x04000019 RID: 25
		private BackgroundWorker worker;

		internal bool IsExtractionInProgress
		{
			get
			{
				BackgroundWorker activeWorker = this.worker;
				return activeWorker != null && activeWorker.IsBusy;
			}
		}

		// Token: 0x02000012 RID: 18

		private void ValidateExtractedInstallation(string destinationFolder)
		{
			List<string> missing = new List<string>();
			List<string> warnings = new List<string>();

			string esDir = Path.Combine(destinationFolder, "emulationstation");
			string esExe = Path.Combine(esDir, "emulationstation.exe");
			string esLauncher = Path.Combine(esDir, "emulatorlauncher.exe");
			// DLL pode ser EmulatorLauncher.Common.dll (case real no Windows)
			string esDllA = Path.Combine(esDir, "emulatorlauncher.common.dll");
			string esDllB = Path.Combine(esDir, "EmulatorLauncher.Common.dll");
			string sdl3 = Path.Combine(esDir, "SDL3.dll");
			string esCfg = Path.Combine(esDir, "emulatorLauncher.cfg");
			string decoRoot = Path.Combine(destinationFolder, "system", "decorations");
			string esSettings = Path.Combine(esDir, ".emulationstation", "es_settings.cfg");
			string esSystems = Path.Combine(esDir, ".emulationstation", "es_systems.cfg");

			if (!File.Exists(esExe))
				missing.Add("emulationstation\\emulationstation.exe");
			if (!File.Exists(esLauncher))
				missing.Add("emulationstation\\emulatorlauncher.exe");
			if (!File.Exists(esDllA) && !File.Exists(esDllB))
				missing.Add("emulationstation\\EmulatorLauncher.Common.dll");
			if (!File.Exists(sdl3))
				missing.Add("emulationstation\\SDL3.dll");
			if (!File.Exists(esCfg))
				warnings.Add("emulationstation\\emulatorLauncher.cfg (paths do sistema)");

			bool hasLauncherAtRoot = File.Exists(Path.Combine(destinationFolder, "TurboRama.exe"));

			if (!hasLauncherAtRoot)
			{
				missing.Add("TurboRama.exe (launcher .NET na raiz - NÃO renomeie emulationstation.exe)");
			}

			// ES TurboRama actual e grande (VLC embutido). ES stock RetroBat ~8MB e insuficiente para kiosk actual.
			if (File.Exists(esExe))
			{
				long esSize = new FileInfo(esExe).Length;
				const long MinTurboRamaEsBytes = 50L * 1024L * 1024L; // 50 MB
				if (esSize < MinTurboRamaEsBytes)
				{
					warnings.Add(
						"emulationstation.exe parece ser a versao stock (" +
						Math.Round(esSize / (1024.0 * 1024.0), 1) +
						" MB). O TurboRama kiosk actual usa ~700+ MB. Regenere o pacote com o ES compilado.");
					Logger.Log("[WARNING] " + warnings[warnings.Count - 1]);
				}
			}

			if (!Directory.Exists(decoRoot))
				warnings.Add("system\\decorations (bezels de sistema)");
			if (!File.Exists(esSettings))
				warnings.Add("emulationstation\\.emulationstation\\es_settings.cfg");
			if (!File.Exists(esSystems))
				warnings.Add("emulationstation\\.emulationstation\\es_systems.cfg");

			// Never mutate a validated package after extraction. A development-only
			// launcher makes the package invalid and triggers transactional rollback.
			string devOnly = Path.Combine(destinationFolder, "TurboRama.exe.DEV-ONLY-NAO-USAR-NO-KIOSK");
			if (File.Exists(devOnly))
			{
				missing.Add("remover TurboRama.exe.DEV-ONLY-NAO-USAR-NO-KIOSK do pacote de produção");
			}

			foreach (string w in warnings)
				Logger.Log("[WARNING] Pacote: " + w);

			if (missing.Count > 0)
			{
				throw new Exception(
					"O pacote extraído está incompleto. Arquivos ausentes:" + Environment.NewLine +
					string.Join(Environment.NewLine, missing.Select(item => "  - " + item)) + Environment.NewLine + Environment.NewLine +
					"IMPORTANTE: TurboRama.exe na raiz é o LAUNCHER (.NET). " +
					"O emulationstation.exe deve ficar dentro da pasta emulationstation\\ e NÃO deve ser renomeado.");
			}

			Logger.Log("Installation package validation passed.");
		}

	}
}
