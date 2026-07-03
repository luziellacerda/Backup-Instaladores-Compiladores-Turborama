using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace RetroBat
{
	internal class Program
	{
		private const string AppName = "TurboRama";
		private const string AppExe = "TurboRama.exe";
		private const string OldAppName = "TurboRama";
		private const string OldAppExe = "TurboRama.exe";
		private const string IniFileName = "turborama.ini";
		private const string OldIniFileName = "turborama.ini";
		private const string IniSection = "TurboRama";
		private const string OldIniSection = "TurboRama";
		private const string LogFileName = "TurboRama.log";
		private const string IntroVideoFileName = "turborama-neon.mp4";

		private static readonly Random _rand = new Random();

		[STAThread]
		private static void Main(string[] args)
		{
			if (Process.GetProcessesByName("emulationstation").FirstOrDefault<Process>() != null)
			{
				SimpleLogger.Instance.Warning("EmulationStation already running");
				if (MessageBox.Show("TurboRama already running! Do you want to continue?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.No)
				{
					return;
				}
			}

			bool externalLauncher = args.Contains("--external-launcher", StringComparer.OrdinalIgnoreCase);
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			Directory.SetCurrentDirectory(baseDirectory);

			File.WriteAllText(Path.Combine(baseDirectory, LogFileName), string.Empty);
			SimpleLogger.Instance.Info("--------------------------------------------------------------");

			string actualExeName = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName).Trim().Normalize(NormalizationForm.FormC);
			SimpleLogger.Instance.Info("Actual executable name: " + actualExeName);

			if (!actualExeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !string.Equals(actualExeName, AppExe, StringComparison.OrdinalIgnoreCase))
			{
				MessageBox.Show("Executable name has been changed! Expected: " + AppExe, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}

			SimpleLogger.Instance.Info("[Startup] " + AppExe);

			CultureInfo currentUICulture = CultureInfo.CurrentUICulture;
			SimpleLogger.Instance.Info("Current culture: " + currentUICulture.ToString());

			string esPath = Path.Combine(baseDirectory, "emulationstation");
			SimpleLogger.Instance.Info("Check ini file");

			string iniPath = Path.Combine(baseDirectory, IniFileName);
			string oldIniPath = Path.Combine(baseDirectory, OldIniFileName);

			if (!File.Exists(iniPath))
			{
				if (File.Exists(oldIniPath))
				{
					SimpleLogger.Instance.Info("Old RetroBat ini found. Migrating to TurboRama ini.");
					try
					{
						string oldIni = File.ReadAllText(oldIniPath);
						oldIni = ConvertLegacyIniContent(oldIni);
						File.WriteAllText(iniPath, oldIni);
						SimpleLogger.Instance.Info("ini file migrated to " + iniPath);
					}
					catch
					{
						SimpleLogger.Instance.Warning("Impossible to migrate old ini file.");
					}
				}

				if (!File.Exists(iniPath))
				{
					SimpleLogger.Instance.Info("ini file does not exist yet, creating default file.");
					string defaultIniContent = IniFile.GetDefaultIniContent();
					try
					{
						File.WriteAllText(iniPath, defaultIniContent);
						SimpleLogger.Instance.Info("ini file written to " + iniPath);
					}
					catch
					{
						SimpleLogger.Instance.Warning("Impossible to create ini file.");
					}
				}
			}

			SimpleLogger.Instance.Info("Checking availability of necessary files.");
			string esTemplatePath = Path.Combine(baseDirectory, "system", "templates", "emulationstation");
			Path.Combine(baseDirectory, "system", "version.info");

			HashSet<string> esRootFiles = new HashSet<string>(Directory.EnumerateFiles(esPath).Select<string, string>(new Func<string, string>(Path.GetFileName)), StringComparer.OrdinalIgnoreCase);

			if (!esRootFiles.Contains("about.info"))
			{
				SimpleLogger.Instance.Warning("Creating file 'about.info'");
				try
				{
					File.WriteAllText(Path.Combine(esPath, "about.info"), "TURBORAMA");
				}
				catch
				{
					SimpleLogger.Instance.Warning("Impossible to create about.info file.");
				}
			}

			if (!esRootFiles.Contains("emulationstation.exe"))
			{
				SimpleLogger.Instance.Error("EmulationStation cannot be found at: " + Path.Combine(esPath, "emulationstation.exe"), null);
				throw new FileNotFoundException("EmulationStation executable not found.");
			}
			if (!esRootFiles.Contains("emulatorlauncher.exe"))
			{
				SimpleLogger.Instance.Error("EmulatorLauncher cannot be found at: " + Path.Combine(esPath, "emulatorlauncher.exe"), null);
				throw new FileNotFoundException("EmulatorLauncher executable not found.");
			}
			if (!esRootFiles.Contains("batocera-store.exe"))
			{
				SimpleLogger.Instance.Warning("Batocera-store executable not found, continuing without it.");
			}
			if (!esRootFiles.Contains("batocera-systems.exe"))
			{
				SimpleLogger.Instance.Warning("Batocera-systems executable not found, continuing without it.");
			}
			if (!esRootFiles.Contains("es-update.exe"))
			{
				SimpleLogger.Instance.Warning("es-update executable not found, continuing without it.");
			}
			if (!esRootFiles.Contains("es-checkversion.exe"))
			{
				SimpleLogger.Instance.Warning("es-checkversion executable not found, continuing without it.");
			}
			if (!esRootFiles.Contains("emulatorlauncher.common.dll"))
			{
				SimpleLogger.Instance.Error("emulatorlauncher common DLL does not exist", null);
				throw new FileNotFoundException("emulatorlauncher common DLL not found.");
			}

			if (!File.Exists(Path.Combine(esPath, ".emulationstation", "es_features.cfg")))
			{
				SimpleLogger.Instance.Error("es_features cannot be found at: " + Path.Combine(esPath, ".emulationstation", "es_features.cfg"), null);
				throw new FileNotFoundException("es_features not found.");
			}

			if (!File.Exists(Path.Combine(esPath, ".emulationstation", "es_systems.cfg")))
			{
				SimpleLogger.Instance.Warning("es_systems cannot be found, trying to copy template.");
				try
				{
					File.Copy(Path.Combine(esTemplatePath, "es_systems.cfg"), Path.Combine(esPath, ".emulationstation", "es_systems.cfg"), true);
				}
				catch
				{
				}
				if (!File.Exists(Path.Combine(esPath, ".emulationstation", "es_systems.cfg")))
				{
					SimpleLogger.Instance.Error("es_systems cannot be found at: " + Path.Combine(esPath, ".emulationstation", "es_systems.cfg"), null);
					throw new FileNotFoundException("es_systems not found.");
				}
			}

			if (!File.Exists(Path.Combine(esPath, "emulatorLauncher.cfg")))
			{
				SimpleLogger.Instance.Warning("emulatorLauncher.cfg cannot be found, trying to copy template.");
				try
				{
					File.Copy(Path.Combine(esTemplatePath, "emulatorLauncher.cfg"), Path.Combine(esPath, "emulatorLauncher.cfg"), true);
				}
				catch
				{
				}
				if (!File.Exists(Path.Combine(esPath, "emulatorLauncher.cfg")))
				{
					SimpleLogger.Instance.Error("emulatorLauncher.cfg cannot be found at: " + Path.Combine(esPath, "emulatorLauncher.cfg"), null);
					throw new FileNotFoundException("emulatorLauncher.cfg not found.");
				}
			}

			SimpleLogger.Instance.Info("All necessary files exist.");

			RegistryTools.SetRegistryKey(baseDirectory);

			RetroBatConfig config = new RetroBatConfig();
			using (IniFile iniFile = new IniFile(iniPath, (IniOptions)0))
			{
				SimpleLogger.Instance.Info("Reading values from inifile: " + iniPath);
				config = Program.GetConfigValues(iniFile);
				foreach (PropertyInfo propertyInfo in config.GetType().GetProperties())
				{
					try
					{
						SimpleLogger.Instance.Info(string.Format("{0} = {1}", propertyInfo.Name, propertyInfo.GetValue(config, null)));
					}
					catch
					{
					}
				}
			}

			string esExePath = Path.Combine(esPath, "emulationstation.exe");
			if (!File.Exists(esExePath))
			{
				SimpleLogger.Instance.Error("Emulationstation executable not found in: " + esExePath, null);
				return;
			}

			SimpleLogger.Instance.Info("EmulationStation.exe found.");

			if (Program.HasDpiScaling())
			{
				string dpiList = Path.Combine(baseDirectory, "system", "tools", "dpi_awareness.txt");
				if (File.Exists(dpiList))
				{
					try
					{
						string[] lines = File.ReadAllLines(dpiList);
						if (lines.Length != 0)
						{
							foreach (string line in lines)
							{
								string exeToPatch = Path.Combine(baseDirectory, line.Trim());
								if (File.Exists(exeToPatch))
								{
									Program.SetDpiAwarenessOverride(exeToPatch, true);
								}
							}
						}
					}
					catch
					{
					}
				}
			}

			if (config.LanguageDetection)
			{
				Program.WriteLanguageToES(esPath, currentUICulture);
			}

			Program.SetGLVersion(esPath, config.OpenGL2_1);
			Program.SetRandomTheme(esPath, config.RandomTheme);
			Program.CleanupStartup();

			if (config.Autostart == 1)
			{
				Program.AddToStartupFolder(baseDirectory, AppExe);
				Program.RemoveFromStartupReg();
				Program.RemoveFromStartupFolder(OldAppName);
			}
			else if (config.Autostart == 2)
			{
				Program.AddToStartupReg(baseDirectory, AppExe);
				Program.RemoveFromStartupFolder(AppName);
				Program.RemoveFromStartupFolder(OldAppName);
			}
			else
			{
				Program.RemoveFromStartupReg();
				Program.RemoveFromStartupFolder(AppName);
				Program.RemoveFromStartupFolder(OldAppName);
			}

			if (config.ResetConfigMode)
			{
				Program.ResetESConfig(baseDirectory);
			}

			Screen[] allScreens = Screen.AllScreens;
			Screen screen = Screen.PrimaryScreen;

			if (config.MonitorIndex > 0 && config.MonitorIndex < allScreens.Length)
			{
				screen = allScreens[config.MonitorIndex];
				SimpleLogger.Instance.Info(string.Format("Using monitor index {0} ({1}).", config.MonitorIndex, screen.DeviceName));
			}
			else
			{
				SimpleLogger.Instance.Info("Monitor index out of range or 0, using primary screen.");
			}

			bool canRunIntro = SplashVideo.CanRunIntroVideo(config, esPath);

			try
			{
				if (canRunIntro)
				{
					SplashVideo.ShowBlackSplash(screen);
					DateTime start = DateTime.UtcNow;
					ManualResetEvent introDone = SplashVideo.RunIntroVideo(config, esPath, screen, false);

					if (config.WaitForVideoEnd)
					{
						introDone.WaitOne();
					}
					else if (config.VideoDelay > 0)
					{
						introDone.WaitOne(config.VideoDelay);
					}

					int elapsed = (int)(DateTime.UtcNow - start).TotalMilliseconds;
					int remaining = config.VideoDelay - elapsed;
					if (remaining > 0)
					{
						Thread.Sleep(remaining);
					}
				}

				SimpleLogger.Instance.Info("Setting up arguments to run EmulationStation.");
				List<string> arguments = new List<string>();

				bool fullscreenBorderless = config.FullscreenBorderless;
				if (config.Fullscreen && config.ForceFullscreenRes)
				{
					arguments.Add("--resolution");
					arguments.Add(config.WindowXSize.ToString());
					arguments.Add(config.WindowYSize.ToString());
				}
				else if (!config.Fullscreen && !fullscreenBorderless)
				{
					arguments.Add("--windowed");
					arguments.Add("--resolution");
					arguments.Add(config.WindowXSize.ToString());
					arguments.Add(config.WindowYSize.ToString());
				}
				else if (fullscreenBorderless)
				{
					arguments.Add("--fullscreen-borderless");
				}
				else
				{
					arguments.Add("--fullscreen");
				}

				if (config.GameListOnly)
				{
					arguments.Add("--gamelist-only");
				}
				if (config.InterfaceMode == 2)
				{
					arguments.Add("--force-kid");
				}
				else if (config.InterfaceMode == 1)
				{
					arguments.Add("--force-kiosk");
				}
				if (config.MonitorIndex > 0 && config.MonitorIndex < allScreens.Length)
				{
					arguments.Add("--monitor");
					arguments.Add(config.MonitorIndex.ToString());
				}
				if (config.NoExitMenu)
				{
					arguments.Add("--no-exit");
				}

				arguments.Add("--vsync");
				arguments.Add(config.VSync ? "1" : "0");

				if (config.DrawFramerate)
				{
					arguments.Add("--draw-framerate");
				}

				arguments.Add("--home");
				arguments.Add(esPath);

				string esArguments = string.Join(" ", arguments.Select<string, string>(delegate(string a)
				{
					if (!a.Contains(" "))
					{
						return a;
					}
					return "\"" + a + "\"";
				}));

				if (config.WiimoteGun)
				{
					Program.RunWiimoteGun(esPath);
				}

				SimpleLogger.Instance.Info("Preparing to run emulationstation.");
				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = esExePath,
					WorkingDirectory = esPath,
					Arguments = esArguments,
					UseShellExecute = false
				};

				TimeSpan uptime = TimeSpan.FromMilliseconds((double)Environment.TickCount);
				if (config.Autostart != 0 && uptime.TotalSeconds < 10.0 && config.AutoStartDelay > 0)
				{
					SimpleLogger.Instance.Info("TurboRama set to run at startup, adding a delay.");
					Thread.Sleep(config.AutoStartDelay);
				}

				try
				{
					SimpleLogger.Instance.Info("Launching " + esExePath + " " + esArguments);
					Process process = Process.Start(startInfo);
					if (process == null)
					{
						SimpleLogger.Instance.Error("Failed to start EmulationStation process.", null);
						return;
					}

					int timeoutMs = 10000;
					int delayMs = 50;
					int waitedMs = 0;
					IntPtr windowHandle = IntPtr.Zero;

					SimpleLogger.Instance.Info("Waiting for EmulationStation main window...");
					while (!process.HasExited && windowHandle == IntPtr.Zero && waitedMs < timeoutMs)
					{
						Thread.Sleep(delayMs);
						waitedMs += delayMs;
						process.Refresh();
						windowHandle = process.MainWindowHandle;
						if (waitedMs % 1000 == 0)
						{
							SimpleLogger.Instance.Info(string.Format("...still waiting ({0}s)", waitedMs / 1000));
						}
					}

					if (windowHandle == IntPtr.Zero)
					{
						SimpleLogger.Instance.Warning("EmulationStation window handle not detected (likely exclusive fullscreen). Skipping focus.");
					}

					if (windowHandle != IntPtr.Zero && !externalLauncher)
					{
						SplashVideo.CloseBlackSplash();
						Thread.Sleep(300);
						if (config.FocusDelay > 0)
						{
							Thread.Sleep(config.FocusDelay);
						}
						FocusHelper.BringProcessWindowToFront(process, 5, 300);
					}
					else if (process.HasExited)
					{
						SimpleLogger.Instance.Error("EmulationStation process exited before creating a window.", null);
					}
					else
					{
						SimpleLogger.Instance.Warning("EmulationStation process is running but no main window detected.");
					}
				}
				catch (Exception ex)
				{
					SimpleLogger.Instance.Warning("Failed to start EmulationStation: " + ex.Message);
				}
			}
			finally
			{
				SplashVideo.CloseBlackSplash();
			}

			SimpleLogger.Instance.Info("All is good, enjoy, quitting TurboRama launcher.");
		}

		private static string ConvertLegacyIniContent(string content)
		{
			if (string.IsNullOrEmpty(content))
			{
				return content;
			}

			return content
				.Replace("; RETROBAT GLOBAL CONFIG FILE", "; TURBORAMA GLOBAL CONFIG FILE")
				.Replace("[RetroBat]", "[TurboRama]")
				.Replace("RetroBat's", "TurboRama's")
				.Replace("TurboRama", "TurboRama")
				.Replace("TURBORAMA", "TURBORAMA")
				.Replace("turborama-neon.mp4", IntroVideoFileName)
				.Replace("turborama.ini", IniFileName)
				.Replace("retrobat", "turborama");
		}

		private static RetroBatConfig GetConfigValues(IniFile ini)
		{
			RetroBatConfig config = new RetroBatConfig
			{
				LanguageDetection = Program.GetOptBoolean(Program.GetAppOptionValue(ini, "LanguageDetection", "true")),
				ResetConfigMode = Program.GetOptBoolean(Program.GetAppOptionValue(ini, "ResetConfigMode", "false")),
				WiimoteGun = Program.GetOptBoolean(Program.GetAppOptionValue(ini, "WiimoteGun", "false")),
				EnableIntro = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "SplashScreen", "EnableIntro", "true")),
				RandomVideo = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "SplashScreen", "RandomVideo", "true")),
				GamepadVideoKill = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "SplashScreen", "GamepadVideoKill", "true")),
				KillVideoWhenESReady = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "SplashScreen", "KillVideoWhenESReady", "false")),
				WaitForVideoEnd = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "SplashScreen", "WaitForVideoEnd", "true")),
				FileName = IniFile.GetOptionValue(ini, "SplashScreen", "FileName", IntroVideoFileName),
				FilePath = IniFile.GetOptionValue(ini, "SplashScreen", "FilePath", "default"),
				Fullscreen = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "EmulationStation", "Fullscreen", "true")),
				FullscreenBorderless = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "EmulationStation", "FullscreenBorderless", "true")),
				ForceFullscreenRes = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "EmulationStation", "ForceFullscreenRes", "false")),
				GameListOnly = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "EmulationStation", "GameListOnly", "false")),
				NoExitMenu = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "EmulationStation", "NoExitMenu", "false")),
				OpenGL2_1 = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "EmulationStation", "OpenGL2_1", "false")),
				VSync = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "EmulationStation", "VSync", "true")),
				DrawFramerate = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "EmulationStation", "DrawFramerate", "false")),
				RandomTheme = Program.GetOptBoolean(IniFile.GetOptionValue(ini, "EmulationStation", "RandomTheme", "false"))
			};

			int value;
			if (int.TryParse(Program.GetAppOptionValue(ini, "Autostart", "0"), out value))
			{
				config.Autostart = value;
			}
			else
			{
				config.Autostart = 0;
			}

			if (int.TryParse(Program.GetAppOptionValue(ini, "AutoStartDelay", "0"), out value))
			{
				config.AutoStartDelay = value;
			}
			else
			{
				config.AutoStartDelay = 0;
			}

			if (int.TryParse(IniFile.GetOptionValue(ini, "EmulationStation", "FocusDelay", "2000"), out value))
			{
				config.FocusDelay = value;
			}
			else
			{
				config.FocusDelay = 1000;
			}

			if (int.TryParse(IniFile.GetOptionValue(ini, "SplashScreen", "VideoDelay", "5000"), out value))
			{
				config.VideoDelay = value;
			}
			else
			{
				config.VideoDelay = 1000;
			}

			if (int.TryParse(IniFile.GetOptionValue(ini, "EmulationStation", "InterfaceMode", "0"), out value))
			{
				config.InterfaceMode = value;
			}
			else
			{
				config.InterfaceMode = 0;
			}

			if (int.TryParse(IniFile.GetOptionValue(ini, "EmulationStation", "MonitorIndex", "0"), out value))
			{
				config.MonitorIndex = value;
			}
			else
			{
				config.MonitorIndex = 0;
			}

			if (int.TryParse(IniFile.GetOptionValue(ini, "EmulationStation", "WindowXSize", "1280"), out value))
			{
				config.WindowXSize = value;
			}
			else
			{
				config.WindowXSize = 1280;
			}

			if (int.TryParse(IniFile.GetOptionValue(ini, "EmulationStation", "WindowYSize", "720"), out value))
			{
				config.WindowYSize = value;
			}
			else
			{
				config.WindowYSize = 720;
			}

			return config;
		}

		private static string GetAppOptionValue(IniFile ini, string key, string defaultValue)
		{
			string value = ini.GetValue(IniSection, key);
			if (!string.IsNullOrEmpty(value))
			{
				return value.Trim(new char[] { '"' });
			}

			string oldValue = ini.GetValue(OldIniSection, key);
			if (!string.IsNullOrEmpty(oldValue))
			{
				ini.WriteValue(IniSection, key, oldValue);
				return oldValue.Trim(new char[] { '"' });
			}

			ini.WriteValue(IniSection, key, defaultValue);
			return defaultValue;
		}

		public static bool GetOptBoolean(string input)
		{
			return string.Equals(input, "1", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(input, "true", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(input, "yes", StringComparison.OrdinalIgnoreCase);
		}

		private static void AddToStartupReg(string appPath, string appExe)
		{
			SimpleLogger.Instance.Info("Setting TurboRama to launch at startup.");
			string exePath = Path.Combine(appPath, appExe);
			string command = string.Format("cmd.exe /c \"cd /d {0} && start \"\" \"{1}\"\"\"", appPath, exePath);

			try
			{
				Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true).SetValue(AppName, command);
				SimpleLogger.Instance.Info("TurboRama set in registry to startup.");
			}
			catch (Exception ex)
			{
				SimpleLogger.Instance.Warning("Failed to set startup registry key: " + ex.Message);
			}
		}

		private static void AddToStartupFolder(string exePath, string shortcutName)
		{
			try
			{
				string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(shortcutName);
				string batchPath = Path.Combine(startupFolder, fileNameWithoutExtension + ".bat");
				string exeFullPath = Path.Combine(exePath, shortcutName);
				string batchContent = string.Concat(new string[]
				{
					"@echo off",
					Environment.NewLine,
					"cd /d \"",
					exePath,
					"\"",
					Environment.NewLine,
					"\"",
					exeFullPath,
					"\""
				});

				File.WriteAllText(batchPath, batchContent);
				SimpleLogger.Instance.Info("TurboRama batch added to Startup folder: " + batchPath);
			}
			catch (Exception ex)
			{
				SimpleLogger.Instance.Warning("Failed to add TurboRama to Startup folder: " + ex.Message);
			}
		}

		private static void RemoveFromStartupFolder(string shortcutName)
		{
			try
			{
				string batchPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), shortcutName + ".bat");
				if (File.Exists(batchPath))
				{
					File.Delete(batchPath);
					SimpleLogger.Instance.Info(shortcutName + " removed from Startup folder: " + batchPath);
				}
				else
				{
					SimpleLogger.Instance.Info(shortcutName + " startup batch not found, nothing to remove.");
				}
			}
			catch (Exception ex)
			{
				SimpleLogger.Instance.Warning("Failed to remove " + shortcutName + " from Startup folder: " + ex.Message);
			}
		}

		private static void CleanupStartup()
		{
			try
			{
				string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
				foreach (string linkName in new string[] { "RetroBat.lnk", "TurboRama.lnk" })
				{
					string linkPath = Path.Combine(startupFolder, linkName);
					if (File.Exists(linkPath))
					{
						try
						{
							File.Delete(linkPath);
						}
						catch
						{
						}
					}
				}
			}
			catch
			{
			}
		}

		private static void RemoveFromStartupReg()
		{
			SimpleLogger.Instance.Info("Ensuring TurboRama does not launch at startup.");
			try
			{
				using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
				{
					if (runKey != null)
					{
						runKey.DeleteValue(AppName, false);
						runKey.DeleteValue(OldAppName, false);
					}
				}
			}
			catch (Exception ex)
			{
				SimpleLogger.Instance.Warning("Failed to remove startup registry key: " + ex.Message);
			}
		}

		private static void RunWiimoteGun(string esPath)
		{
			SimpleLogger.Instance.Info("Running WiimoteGun.");
			string wiimotePath = Path.Combine(esPath, "WiimoteGun.exe");
			if (!File.Exists(wiimotePath))
			{
				SimpleLogger.Instance.Warning("WiimoteGun executable not found at: " + wiimotePath);
				return;
			}

			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = wiimotePath,
					WorkingDirectory = esPath,
					UseShellExecute = false,
					CreateNoWindow = true
				});
				SimpleLogger.Instance.Info("WiimoteGun started successfully.");
			}
			catch (Exception ex)
			{
				SimpleLogger.Instance.Warning("Failed to start WiimoteGun: " + ex.Message);
			}
		}

		private static void ResetESConfig(string path)
		{
			SimpleLogger.Instance.Info("Resetting configuration.");

			List<string> files = new List<string>();
			files.Add("es_input.cfg");
			files.Add("es_padtokey.cfg");
			files.Add("es_settings.cfg");
			files.Add("es_systems.cfg");

			string templatePath = Path.Combine(path, "system", "templates", "emulationstation");
			string esConfigPath = Path.Combine(Path.Combine(path, "emulationstation"), ".emulationstation");

			foreach (string fileName in files)
			{
				string sourceFile = Path.Combine(templatePath, fileName);
				string destinationFile = Path.Combine(esConfigPath, fileName);

				if (File.Exists(sourceFile))
				{
					try
					{
						string oldFile = destinationFile + ".old";
						File.Delete(oldFile);
						File.Move(destinationFile, oldFile);
						File.Copy(sourceFile, destinationFile, true);
						SimpleLogger.Instance.Info("Reset " + fileName + " to default.");
						continue;
					}
					catch (Exception ex)
					{
						SimpleLogger.Instance.Warning("Could not reset " + fileName + ": " + ex.Message);
						continue;
					}
				}

				SimpleLogger.Instance.Warning("Template file " + sourceFile + " does not exist.");
			}

			string iniPath = Path.Combine(path, IniFileName);
			string oldIniPath = Path.Combine(path, OldIniFileName);

			try
			{
				foreach (string file in new string[] { iniPath, oldIniPath })
				{
					if (File.Exists(file))
					{
						try
						{
							File.Delete(file);
						}
						catch (Exception ex)
						{
							SimpleLogger.Instance.Warning("Could not delete ini file: " + ex.Message);
						}
					}
				}

				try
				{
					string defaultIniContent = IniFile.GetDefaultIniContent();
					File.WriteAllText(iniPath, defaultIniContent);
					SimpleLogger.Instance.Info("ini file regenerated with default values.");
				}
				catch
				{
					SimpleLogger.Instance.Warning("Impossible to create ini file.");
				}
			}
			catch
			{
				SimpleLogger.Instance.Warning("Could not reinitialize ini file.");
			}
		}

		private static void WriteLanguageToES(string esPath, CultureInfo culture)
		{
			string language = culture.Name.ToString().Replace('-', '_');
			string settingsPath = Path.Combine(esPath, ".emulationstation", "es_settings.cfg");
			if (!File.Exists(settingsPath))
			{
				SimpleLogger.Instance.Error("es_settings.cfg cannot be found at: " + settingsPath, null);
				throw new FileNotFoundException("es_settings.cfg not found.");
			}

			SimpleLogger.Instance.Info("es_settings.cfg path: " + settingsPath);
			SimpleLogger.Instance.Info("Updating EmulationStation language.");

			try
			{
				XmlDocument xmlDocument = new XmlDocument();
				xmlDocument.Load(settingsPath);
				XmlNode xmlNode = xmlDocument.SelectSingleNode("//string[@name='Language']");
				if (xmlNode != null && xmlNode.Attributes != null)
				{
					xmlNode.Attributes["value"].Value = language;
				}
				else
				{
					XmlElement xmlElement = xmlDocument.CreateElement("string");
					xmlElement.SetAttribute("name", "Language");
					xmlElement.SetAttribute("value", language);
					XmlNode root = xmlDocument.SelectSingleNode("/config");
					if (root != null)
					{
						root.AppendChild(xmlElement);
					}
					else
					{
						SimpleLogger.Instance.Warning("Could not update EmulationStation language.");
					}
				}
				xmlDocument.Save(settingsPath);
			}
			catch (Exception ex)
			{
				SimpleLogger.Instance.Warning("Could not update EmulationStation language: " + ex.Message);
			}
		}

		private static void SetGLVersion(string esPath, bool oldOpenGL)
		{
			string settingsPath = Path.Combine(esPath, ".emulationstation", "es_settings.cfg");
			if (!File.Exists(settingsPath))
			{
				SimpleLogger.Instance.Error("es_settings.cfg cannot be found at: " + settingsPath, null);
				throw new FileNotFoundException("es_settings.cfg not found.");
			}

			SimpleLogger.Instance.Info("es_settings.cfg path: " + settingsPath);

			try
			{
				XmlDocument xmlDocument = new XmlDocument();
				xmlDocument.Load(settingsPath);
				XmlNode xmlNode = xmlDocument.SelectSingleNode("//string[@name='Renderer']");
				if (xmlNode != null && xmlNode.Attributes != null)
				{
					if (oldOpenGL)
					{
						SimpleLogger.Instance.Info("es_settings.cfg, setting old renderer");
						xmlNode.Attributes["value"].Value = "OPENGL 2.1";
					}
					else
					{
						xmlNode.RemoveAll();
					}
				}
				else if (oldOpenGL)
				{
					XmlElement xmlElement = xmlDocument.CreateElement("string");
					xmlElement.SetAttribute("name", "Renderer");
					xmlElement.SetAttribute("value", "OPENGL 2.1");
					XmlNode root = xmlDocument.SelectSingleNode("/config");
					if (root != null)
					{
						root.AppendChild(xmlElement);
					}
					else
					{
						SimpleLogger.Instance.Warning("Could not update EmulationStation renderer.");
					}
				}
				xmlDocument.Save(settingsPath);
			}
			catch (Exception ex)
			{
				SimpleLogger.Instance.Warning("Could not update EmulationStation renderer: " + ex.Message);
			}
		}

		private static void SetRandomTheme(string esPath, bool randomTheme)
		{
			if (!randomTheme)
			{
				return;
			}

			bool changed = false;
			string settingsPath = Path.Combine(esPath, ".emulationstation", "es_settings.cfg");
			if (!File.Exists(settingsPath))
			{
				SimpleLogger.Instance.Error("es_settings.cfg cannot be found at: " + settingsPath, null);
				throw new FileNotFoundException("es_settings.cfg not found.");
			}

			SimpleLogger.Instance.Info("es_settings.cfg path: " + settingsPath);

			try
			{
				XmlDocument xmlDocument = new XmlDocument();
				xmlDocument.Load(settingsPath);
				XmlNode xmlNode = xmlDocument.SelectSingleNode("//string[@name='ThemeSet']");
				if (xmlNode != null && xmlNode.Attributes != null)
				{
					XmlAttribute valueAttribute = xmlNode.Attributes["value"];
					string currentTheme = valueAttribute != null ? valueAttribute.Value : null;
					string themesPath = Path.Combine(esPath, ".emulationstation", "themes");

					if (Directory.Exists(themesPath))
					{
						string[] themes = Directory.GetDirectories(themesPath)
							.Select<string, string>(new Func<string, string>(Path.GetFileName))
							.Where<string>(delegate(string t)
							{
								return !string.Equals(t, currentTheme, StringComparison.OrdinalIgnoreCase);
							})
							.ToArray<string>();

						if (themes.Length != 0)
						{
							string newTheme = themes[Program._rand.Next(themes.Length)];
							SimpleLogger.Instance.Info("es_settings.cfg, setting random theme: " + newTheme);
							xmlNode.Attributes["value"].Value = newTheme;
							changed = true;
						}
						else
						{
							SimpleLogger.Instance.Warning("No themes found in themes directory.");
						}
					}
					else
					{
						SimpleLogger.Instance.Warning("Themes directory not found at: " + themesPath);
					}
				}

				if (changed)
				{
					xmlDocument.Save(settingsPath);
				}
			}
			catch (Exception ex)
			{
				SimpleLogger.Instance.Warning("Could not update EmulationStation theme: " + ex.Message);
			}
		}

		public static bool HasDpiScaling()
		{
			using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\FontDPI"))
			{
				object obj = registryKey != null ? registryKey.GetValue("LogPixels") : null;
				if (obj is int)
				{
					int dpi = (int)obj;
					return dpi != 96;
				}
			}

			using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop"))
			{
				object obj = registryKey != null ? registryKey.GetValue("LogPixels") : null;
				if (obj is int)
				{
					int dpi = (int)obj;
					return dpi != 96;
				}
			}

			return false;
		}

		public static void SetDpiAwarenessOverride(string exePath, bool enable)
		{
			RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers", true) ?? Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers");
			if (registryKey == null)
			{
				return;
			}

			using (registryKey)
			{
				HashSet<string> values = new HashSet<string>(((registryKey.GetValue(exePath) as string) ?? string.Empty).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
				if (enable)
				{
					if (values.Contains("HIGHDPIAWARE"))
					{
						return;
					}
					values.Add("HIGHDPIAWARE");
				}
				else
				{
					if (!values.Contains("HIGHDPIAWARE"))
					{
						return;
					}
					values.Remove("HIGHDPIAWARE");
				}

				if (values.Count == 0)
				{
					registryKey.DeleteValue(exePath, false);
				}
				else
				{
					registryKey.SetValue(exePath, string.Join(" ", values), RegistryValueKind.String);
				}
			}
		}
	}
}

