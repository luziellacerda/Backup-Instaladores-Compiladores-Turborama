using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;
using Allegoria.Controls;
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
			// Capture and canonicalize on the UI thread. BackgroundWorker must never
			// read a WinForms control directly, and elevated extraction never
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
			this.worker = new BackgroundWorker
			{
				WorkerReportsProgress = true
			};
			this.worker.DoWork += delegate(object workSender, DoWorkEventArgs workArgs)
			{
				try
				{
					using (Stream installerZipStream = this.GetInstallerZipStream())
					using (SecureExtractionGuard extractionGuard = SecureExtractionGuard.Create(destinationFolder))
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
						this.EnsureTurboRamaExecutable(destinationFolder);
					}
					this.CreateTurboRamaShortcuts(destinationFolder);
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
				this.btnInstall.Enabled = true;
				this.btnBrowse.Enabled = true;
				this.btnBack.Enabled = true;
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

			bool hasLauncherAtRoot =
				File.Exists(Path.Combine(destinationFolder, "TurboRama.exe")) ||
				File.Exists(Path.Combine(destinationFolder, "Turborama.exe")) ||
				File.Exists(Path.Combine(destinationFolder, "RetroBat.exe")) ||
				File.Exists(Path.Combine(destinationFolder, "retrobat.exe"));

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

			// DEV-ONLY nao deve ir para kiosk
			string devOnly = Path.Combine(destinationFolder, "TurboRama.exe.DEV-ONLY-NAO-USAR-NO-KIOSK");
			if (File.Exists(devOnly))
			{
				try { File.Delete(devOnly); Logger.Log("Removed DEV-ONLY launcher from install."); }
				catch (Exception ex) { Logger.Log("Could not remove DEV-ONLY: " + ex.Message); }
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


		private void CreateTurboRamaShortcuts(string destinationFolder)
		{
			string turboRamaExe = Path.Combine(destinationFolder, "TurboRama.exe");

			if (!File.Exists(turboRamaExe))
			{
				throw new FileNotFoundException("Cannot create shortcuts because TurboRama2026.exe was not found.", turboRamaExe);
			}

			try
			{
				string desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
				if (!string.IsNullOrWhiteSpace(desktopFolder))
				{
					string desktopShortcut = Path.Combine(desktopFolder, "TurboRama2026.lnk");
					this.CreateShortcut(desktopShortcut, turboRamaExe, destinationFolder, "Abrir TurboRama 2026", turboRamaExe);
					Logger.Log("Desktop shortcut created: " + desktopShortcut);
				}

				string driveRootShortcut = Path.Combine(Path.GetPathRoot(destinationFolder) ?? "D:\\", "TurboRama2026.lnk");
				if (!string.IsNullOrWhiteSpace(driveRootShortcut))
				{
					this.CreateShortcut(driveRootShortcut, turboRamaExe, destinationFolder, "Abrir TurboRama 2026", turboRamaExe);
					Logger.Log("Drive shortcut created: " + driveRootShortcut);
				}

				string programsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
				if (!string.IsNullOrWhiteSpace(programsFolder))
				{
					string startMenuFolder = Path.Combine(programsFolder, "TurboRama");
					Directory.CreateDirectory(startMenuFolder);
					string startMenuShortcut = Path.Combine(startMenuFolder, "TurboRama2026.lnk");
					this.CreateShortcut(startMenuShortcut, turboRamaExe, destinationFolder, "Abrir TurboRama 2026", turboRamaExe);
					Logger.Log("Start Menu shortcut created: " + startMenuShortcut);
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to create TurboRama shortcuts: " + ex.ToString());
				throw new Exception("Falha ao criar atalhos do TurboRama: " + ex.Message, ex);
			}
		}

		private void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string description, string iconPath)
		{
			IShellLinkW link = (IShellLinkW)new ShellLink();
			link.SetPath(targetPath);
			link.SetWorkingDirectory(workingDirectory);
			link.SetDescription(description);
			link.SetIconLocation(iconPath, 0);

			IPersistFile file = (IPersistFile)link;
			file.Save(shortcutPath, true);

			Marshal.ReleaseComObject(file);
		}

		[ComImport]
		[Guid("00021401-0000-0000-C000-000000000046")]
		private class ShellLink
		{
		}

		[ComImport]
		[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		[Guid("000214F9-0000-0000-C000-000000000046")]
		private interface IShellLinkW
		{
			void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] string pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
			void GetIDList(out IntPtr ppidl);
			void SetIDList(IntPtr pidl);
			void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] string pszName, int cchMaxName);
			void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
			void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] string pszDir, int cchMaxPath);
			void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
			void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] string pszArgs, int cchMaxPath);
			void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
			void GetHotkey(out short pwHotkey);
			void SetHotkey(short wHotkey);
			void GetShowCmd(out int piShowCmd);
			void SetShowCmd(int iShowCmd);
			void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int cchIconPath, out int piIcon);
			void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
			void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
			void Resolve(IntPtr hwnd, uint fFlags);
			void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
		}

	}
}

