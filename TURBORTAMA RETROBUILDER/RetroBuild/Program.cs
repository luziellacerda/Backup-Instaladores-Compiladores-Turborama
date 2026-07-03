using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Xml;
using ICSharpCode.SharpZipLib.Zip;

namespace RetroBuild
{
	// Token: 0x02000007 RID: 7
	internal class Program
	{
		private static readonly string[] BlockedBuildFiles = new string[]
		{
			"license.txt",
			"notice.pdf",
			"notice_french.pdf",
			"GDrive.exe",
			"TurboRama.log"
		};

		private static bool IsBlockedBuildFile(string path)
		{
			string fileName = Path.GetFileName(path);
			return Program.BlockedBuildFiles.Any((string blocked) => blocked.Equals(fileName, StringComparison.OrdinalIgnoreCase));
		}

		private static void RemoveBlockedBuildFiles(string buildPath)
		{
			if (!Directory.Exists(buildPath))
			{
				return;
			}

			foreach (string file in Directory.GetFiles(buildPath, "*", SearchOption.AllDirectories))
			{
				if (!Program.IsBlockedBuildFile(file))
				{
					continue;
				}

				try
				{
					File.SetAttributes(file, FileAttributes.Normal);
					File.Delete(file);
					Logger.LogInfo("Arquivo bloqueado removido da build: " + file);
				}
				catch (Exception ex)
				{
					Logger.Log("[WARNING] Nao foi possivel remover arquivo bloqueado " + file + ": " + ex.Message);
				}
			}
		}

		// Token: 0x06000059 RID: 89 RVA: 0x000036F0 File Offset: 0x000018F0
		private static void Main()
		{
			AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
			{
				if (new AssemblyName(args.Name).Name == "ICSharpCode.SharpZipLib")
				{
					Assembly executingAssembly = Assembly.GetExecutingAssembly();
					string text4 = "RetroBuild.resources.ICSharpCode.SharpZipLib.dll";
					using (Stream manifestResourceStream = executingAssembly.GetManifestResourceStream(text4))
					{
						if (manifestResourceStream == null)
						{
							return null;
						}
						byte[] array = new byte[manifestResourceStream.Length];
						manifestResourceStream.Read(array, 0, array.Length);
						return Assembly.Load(array);
					}
				}
				return null;
			};
			Logger.LogStart(AppDomain.CurrentDomain.FriendlyName);
			string text = "build.ini";
			BuilderOptions builderOptions = null;
			try
			{
				Logger.LogInfo("Reading build.ini file for options.");
				builderOptions = BuilderOptions.LoadBuilderOptions(text);
				foreach (PropertyInfo propertyInfo in builderOptions.GetType().GetProperties())
				{
					string name = propertyInfo.Name;
					object value = propertyInfo.GetValue(builderOptions, null);
					Console.WriteLine("{0} = {1}", name, value);
					Logger.LogInfo(name + " = " + ((value != null) ? value.ToString() : null));
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error loading config: " + ex.Message);
				Logger.Log("[ERROR] Error loading config file: " + ex.Message);
				return;
			}
			if (!File.Exists(builderOptions.SevenZipPath))
			{
				Logger.Log("[ERROR] 7za.exe not found at: " + builderOptions.SevenZipPath);
				return;
			}
			if (!File.Exists(builderOptions.WgetPath))
			{
				Logger.Log("[ERROR] wget.exe not found at: " + builderOptions.WgetPath);
				return;
			}
			if (!File.Exists(builderOptions.CurlPath))
			{
				Logger.Log("[ERROR] curl.exe not found at: " + builderOptions.CurlPath);
				return;
			}
			Console.Clear();
			Console.WriteLine("TurboRama Builder Menu");
			Console.WriteLine("---------------------------------------------------");
			Console.WriteLine("This executable is made to help download all the required software for TurboRama.");
			Console.WriteLine("Use the 'build.ini' file to set options for building.");
			Console.WriteLine("Option 1 must always be done first, as it will download all the required files.");
			Console.WriteLine("---------------------------------------------------\n");
			Console.WriteLine("=====================\n");
			Console.WriteLine("1 - Download and configure");
			Console.WriteLine("2 - Create archive");
			Console.WriteLine("3 - Create installer (need archive created first)");
			Console.WriteLine("Q - Quit\n");
			Console.Write("Please type your choice here: ");
			string text2 = Console.ReadLine();
			string text3 = ((text2 != null) ? text2.Trim().ToUpper() : null);
			if (!(text3 == "1"))
			{
				if (!(text3 == "2"))
				{
					if (!(text3 == "3"))
					{
						if (text3 == "Q")
						{
							Console.WriteLine("Exiting...");
							return;
						}
					}
					else
					{
						Logger.Log("Option selected: Create installer.");
						Logger.Log("Starting log.\n");
						Console.WriteLine("=====================");
						Installer.CreateInstaller(builderOptions);
					}
				}
				else
				{
					Logger.Log("Option selected: Create archive.");
					Logger.Log("Starting log.\n");
					Console.WriteLine("=====================");
					Program.CreateZipFolderSharpZip(builderOptions);
				}
			}
			else
			{
				Logger.Log("Option selected: Download and configure.");
				Logger.Log("Starting log.\n");
				Console.WriteLine("=====================");
				Program.GetPackages(builderOptions);
				Console.WriteLine("=====================");
				Program.CreateTree(builderOptions);
				Console.WriteLine("=====================");
				Program.CreateEmulatorFolders(builderOptions);
				Console.WriteLine("=====================");
				Program.CreateSystemFolders(builderOptions);
				Console.WriteLine("=====================");
				Program.GetLibretroCores(builderOptions);
				Console.WriteLine("=====================");
				Program.GetEmulators(builderOptions);
				Console.WriteLine("=====================");
				Program.CopyESFiles(builderOptions);
				Console.WriteLine("=====================");
				Program.CreateVersionFile(builderOptions);
				Console.WriteLine("=====================");
				Program.CopyTemplateFiles(builderOptions);
				Console.WriteLine("=====================");
				Program.SetVersion(builderOptions);
			}
			Logger.Log("[INFO] Build finished succesfully.\n");
			Console.WriteLine("Press any key to exit...");
			Console.ReadKey();
		}

		// Token: 0x0600005A RID: 90 RVA: 0x00003A5C File Offset: 0x00001C5C
		private static void GetPackages(BuilderOptions options)
		{
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			string text = Path.Combine(baseDirectory, "build");
			if (Directory.Exists(text))
			{
				if (Directory.GetFiles(text).Length == 0 && Directory.GetDirectories(text).Length == 0)
				{
					goto IL_00C8;
				}
				Logger.Log("[WARNING] Build path was not empty, deleting content.");
				try
				{
					Directory.Delete(text, true);
				}
				catch (Exception ex)
				{
					Logger.Log("[ERROR] Failed to delete content of build path: " + ex.Message);
					Console.ReadKey();
					return;
				}
				try
				{
					Directory.CreateDirectory(text);
					goto IL_00C8;
				}
				catch (Exception ex2)
				{
					Logger.Log("[ERROR] Failed to create build directory: " + ex2.Message);
					Console.ReadKey();
					return;
				}
			}
			try
			{
				Directory.CreateDirectory(text);
			}
			catch (Exception ex3)
			{
				Logger.Log("[ERROR] Failed to create build directory: " + ex3.Message);
				Console.ReadKey();
				return;
			}
			IL_00C8:
			Logger.LogLabel("get_packages");
			Console.WriteLine(":: GETTING REQUIRED PACKAGES...");
			string text2;
			if (Methods.RunProcess("git", "submodule update --init", baseDirectory, out text2) != 0)
			{
				Logger.Log("[WARNING] Failed to initialize git submodules");
			}
			if (options.GetRetrobatBinaries)
			{
				foreach (string text3 in Directory.GetFiles(baseDirectory, "*.txt"))
				{
					if (Program.IsBlockedBuildFile(text3))
					{
						Logger.LogInfo("Arquivo TXT bloqueado, nao sera copiado para a build: " + text3);
						continue;
					}

					string text4 = Path.Combine(text, Path.GetFileName(text3));
					File.Copy(text3, text4, true);
				}
				string turboramaBinariesUrl = "https://github.com/luziellacerda/TurboramaBinarios/releases/download/continuous-master/turborama_binaries.7z";
				Methods.DownloadAndExtractArchive_WebClient(turboramaBinariesUrl, text, options);
				Logger.LogInfo("turborama binaries copied to " + text);
			}
			if (options.GetEmulationstation)
			{
				string text6 = options.EmulationstationUrl;
				if (!text6.EndsWith("/") && !text6.EndsWith(".zip"))
				{
					text6 += "/";
				}
				if (options.Architecture == "win32")
				{
					text6 += "EmulationStation-Win32.zip";
				}
				else
				{
					text6 += "EmulationStation-Win64.zip";
				}
				string text7 = Path.Combine(text, "emulationstation");
				Methods.DownloadAndExtractArchive_Wget(text6, text7, options);
				Logger.LogInfo("Emulationstation copied to " + text7);
			}
			if (options.GetBatoceraPorts)
			{
				string text8 = options.EmulatorlauncherUrl;
				if (!text8.EndsWith("/") && !text8.EndsWith(".zip"))
				{
					text8 += "/";
				}
				if (options.Architecture == "win32")
				{
					text8 += "batocera-ports.zip";
				}
				else
				{
					text8 += "batocera-ports-x64.zip";
				}
				string text9 = Path.Combine(text, "emulationstation");
				Methods.DownloadAndExtractArchive_Wget(text8, text9, options);
				Logger.LogInfo("Emulatorlauncher copied to " + text9);
			}
			if (options.GetBios)
			{
				string text10 = Path.Combine(text, "bios");
				Methods.CloneOrUpdateGitRepo(options.BiosGitUrl, text10);
				Path.Combine(text10, ".git");
				Methods.DeleteGitFiles(text10);
			}
			if (options.GetDefaultTheme)
			{
				string text11 = Path.Combine(new string[] { text, "emulationstation", ".emulationstation", "themes", "es-theme-carbon" });
				Methods.CloneOrUpdateGitRepo(options.ThemePath, text11);
				Path.Combine(text11, ".git");
				Methods.DeleteGitFiles(text11);
			}
			if (options.GetDecorations)
			{
				string text12 = Path.Combine(text, "system", "decorations");
				Methods.CloneOrUpdateGitRepo(options.DecorationsPath, text12);
				Path.Combine(text12, ".git");
				Methods.DeleteGitFiles(text12);
			}
			if (options.GetSystem)
			{
				string text13 = Path.Combine(baseDirectory, "system");
				if (Directory.Exists(text13))
				{
					Logger.LogInfo("Copying system folder.");
					string text14 = Path.Combine(text, "system");
					foreach (string text15 in Directory.GetDirectories(text13, "*", SearchOption.AllDirectories))
					{
						if (!text15.EndsWith(".git", StringComparison.OrdinalIgnoreCase) && !text15.EndsWith("decorations", StringComparison.OrdinalIgnoreCase))
						{
							string text16 = text15.Replace(text13, text14);
							if (!Directory.Exists(text16))
							{
								Directory.CreateDirectory(text16);
							}
						}
					}
					foreach (string text17 in Directory.GetFiles(text13, "*.*", SearchOption.AllDirectories))
					{
						if (!text17.Contains(Path.Combine(".git", "")) && !text17.EndsWith(".git", StringComparison.OrdinalIgnoreCase) && !text17.Contains("system\\decorations"))
						{
							string text18 = text17.Replace(text13, text14);
							File.Copy(text17, text18, true);
						}
					}
					Logger.LogInfo("System folder copied.");
				}
				else
				{
					string text19 = Path.Combine(text, "system");
					Methods.CloneOrUpdateGitRepo(options.SystemPath, text19);
					Path.Combine(text19, ".git");
					Methods.DeleteGitFiles(text19);
				}
			}
			if (options.GetRetroarch)
			{
				string retroarchVersion = options.RetroarchVersion;
				string text20 = Path.Combine(text, "emulators", "retroarch");
				Methods.DownloadAndExtractArchive_Wget(options.RetroArchURL + "/stable/" + retroarchVersion + "/windows/x86_64/RetroArch.7z", text20, options);
				string text21 = Directory.GetDirectories(text20).FirstOrDefault<string>((string d) => Path.GetFileName(d).Contains("RetroArch-Win64"));
				if (Directory.Exists(text21))
				{
					foreach (string text22 in Directory.GetFiles(text21, "*", SearchOption.AllDirectories))
					{
						string text23 = text22.Substring(text21.Length + 1);
						string text24 = Path.Combine(text20, text23);
						Directory.CreateDirectory(Path.GetDirectoryName(text24));
						File.Copy(text22, text24, true);
					}
					Console.WriteLine("All files copied successfully.");
				}
				else
				{
					Console.WriteLine("Source directory does not exist.");
				}
				try
				{
					Directory.Delete(text21, true);
					Logger.LogInfo("RetroArch succesfully downloaded to " + text20);
				}
				catch
				{
					Logger.LogInfo("[ERROR] Not able to delete RetroArch temp folder: " + text21);
				}
			}
			if (options.GetWiimotegun)
			{
				string wiimoteGunURL = options.WiimoteGunURL;
				string text25 = Path.Combine(text, "emulationstation");
				Methods.DownloadAndExtractArchive_Wget(wiimoteGunURL, text25, options);
				Logger.LogInfo("WiimoteGun copied to " + text25);
			}
			if (options.GetBatgui)
			{
				string batGUIURL = options.BatGUIURL;
				string text26 = Path.Combine(new string[] { text });
				Methods.DownloadAndExtractArchive_Wget(batGUIURL, text26, options);
				Logger.LogInfo("BatGui copied to " + text26);
			}
		}

		// Token: 0x0600005B RID: 91 RVA: 0x000040F0 File Offset: 0x000022F0
		private static void CreateTree(BuilderOptions options)
		{
			string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "build");
			if (!Directory.Exists(text))
			{
				try
				{
					Directory.CreateDirectory(text);
				}
				catch (Exception ex)
				{
					Logger.Log("[ERROR] Failed to create build directory: " + ex.Message);
					Console.ReadKey();
					return;
				}
			}
			Logger.LogLabel("build_tree");
			Console.WriteLine(":: BUILDING TURBORAMA TREE...");
			string text2 = Path.Combine(text, "system", "configgen", "retrobat_tree.lst");
			if (!File.Exists(text2))
			{
				Logger.LogInfo("Missing 'retrobat_tree.lst' file.");
				return;
			}
			foreach (string text3 in File.ReadAllLines(text2))
			{
				if (!string.IsNullOrWhiteSpace(text3))
				{
					string text4 = Path.Combine(text, text3.Trim());
					if (Directory.Exists(text4))
					{
						Logger.LogInfo("Directory already exists: " + text4);
					}
					else
					{
						try
						{
							Directory.CreateDirectory(text4);
							Logger.LogInfo("Created: " + text4);
						}
						catch (Exception ex2)
						{
							Logger.LogInfo("Failed to create " + text4 + ": " + ex2.Message);
						}
					}
				}
			}
			Logger.LogInfo("All folders processed.");
		}

		// Token: 0x0600005C RID: 92 RVA: 0x0000423C File Offset: 0x0000243C
		private static void CreateEmulatorFolders(BuilderOptions options)
		{
			string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "build");
			if (!Directory.Exists(text))
			{
				try
				{
					Directory.CreateDirectory(text);
				}
				catch (Exception ex)
				{
					Logger.Log("[ERROR] Failed to create build directory: " + ex.Message);
					Console.ReadKey();
					return;
				}
			}
			Logger.LogLabel("emulator_folders");
			Console.WriteLine(":: CREATING EMULATOR FOLDERS...");
			string text2 = Path.Combine(text, "system", "configgen", "emulators_names.lst");
			if (!File.Exists(text2))
			{
				Logger.LogInfo("Missing 'emulators_names.lst' file.");
				return;
			}
			foreach (string text3 in File.ReadAllLines(text2))
			{
				if (!string.IsNullOrWhiteSpace(text3))
				{
					string text4 = Path.Combine(text, "emulators", text3.Trim());
					if (Directory.Exists(text4))
					{
						Logger.LogInfo("Directory already exists: " + text4);
					}
					else
					{
						try
						{
							Directory.CreateDirectory(text4);
							Logger.LogInfo("Created: " + text4);
						}
						catch (Exception ex2)
						{
							Logger.LogInfo("Failed to create " + text4 + ": " + ex2.Message);
						}
					}
				}
			}
			Logger.LogInfo("All emulator folders processed.");
		}

		// Token: 0x0600005D RID: 93 RVA: 0x00004390 File Offset: 0x00002590
		private static void CreateSystemFolders(BuilderOptions options)
		{
			string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "build");
			if (!Directory.Exists(text))
			{
				try
				{
					Directory.CreateDirectory(text);
				}
				catch (Exception ex)
				{
					Logger.Log("[ERROR] Failed to create build directory: " + ex.Message);
					Console.ReadKey();
					return;
				}
			}
			Logger.LogLabel("system_folders");
			Console.WriteLine(":: CREATING ROMS AND SAVE FOLDERS...");
			string text2 = Path.Combine(text, "system", "configgen", "systems_names.lst");
			if (!File.Exists(text2))
			{
				Logger.LogInfo("Missing 'systems_names.lst' file.");
				return;
			}
			foreach (string text3 in File.ReadAllLines(text2))
			{
				if (!string.IsNullOrWhiteSpace(text3))
				{
					string text4 = Path.Combine(text, "roms", text3.Trim());
					if (Directory.Exists(text4))
					{
						Logger.LogInfo("Directory already exists: " + text4);
					}
					else
					{
						try
						{
							Directory.CreateDirectory(text4);
							Logger.LogInfo("Created: " + text4);
						}
						catch (Exception ex2)
						{
							Logger.LogInfo("Failed to create " + text4 + ": " + ex2.Message);
						}
						string text5 = Path.Combine(text, "saves", text3.Trim());
						if (Directory.Exists(text5))
						{
							Logger.LogInfo("Directory already exists: " + text5);
						}
						else
						{
							try
							{
								Directory.CreateDirectory(text5);
								Logger.LogInfo("Created: " + text5);
							}
							catch (Exception ex3)
							{
								Logger.LogInfo("Failed to create " + text5 + ": " + ex3.Message);
							}
						}
					}
				}
			}
			Logger.LogInfo("All roms folders processed.");
		}

		// Token: 0x0600005E RID: 94 RVA: 0x00004564 File Offset: 0x00002764
		private static void CopyESFiles(BuilderOptions options)
		{
			List<string> list = new List<string> { "es_input.cfg", "es_padtokey.cfg", "es_settings.cfg", "es_systems.cfg" };
			string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "build");
			if (!Directory.Exists(text))
			{
				try
				{
					Directory.CreateDirectory(text);
				}
				catch (Exception ex)
				{
					Logger.Log("[ERROR] Failed to create build directory: " + ex.Message);
					Console.ReadKey();
					return;
				}
			}
			Logger.LogLabel("emulationstation_config");
			Console.WriteLine(":: COPY EMULATIONSTATION FILES...");
			string text2 = Path.Combine(text, "system", "templates", "emulationstation");
			string text3 = Path.Combine(text, "emulationstation", ".emulationstation");
			foreach (string text4 in list)
			{
				string text5 = Path.Combine(text2, text4);
				string text6 = Path.Combine(text3, text4);
				if (!File.Exists(text5))
				{
					Logger.LogInfo("Source file not found: " + text5);
				}
				else
				{
					try
					{
						File.Copy(text5, text6, true);
						Logger.LogInfo("Copied " + text4 + " to " + text3);
					}
					catch (Exception ex2)
					{
						Logger.LogInfo("Failed to copy " + text4 + ": " + ex2.Message);
					}
				}
			}
			string text7 = Path.Combine(text, "system", "resources", "emulationstation");
			Logger.LogInfo("notice.pdf esta bloqueado e nao sera copiado para a pasta .emulationstation.");
			string text10 = Path.Combine(text7, "music");
			string text11 = Path.Combine(text7, "video");
			string text12 = Path.Combine(text3, "music");
			string text13 = Path.Combine(text3, "video");
			if (!Directory.Exists(text12))
			{
				try
				{
					Directory.CreateDirectory(text12);
					Logger.LogInfo("Created directory: " + text12);
				}
				catch (Exception ex4)
				{
					Logger.LogInfo("Failed to create music directory: " + ex4.Message);
					return;
				}
			}
			if (!Directory.Exists(text13))
			{
				try
				{
					Directory.CreateDirectory(text13);
					Logger.LogInfo("Created directory: " + text13);
				}
				catch (Exception ex5)
				{
					Logger.LogInfo("Failed to create video directory: " + ex5.Message);
					return;
				}
			}
			foreach (string text14 in Directory.GetFiles(text10, "*.*"))
			{
				string text15 = text14.Replace(text10, text12);
				try
				{
					File.Copy(text14, text15, true);
					Logger.LogInfo("Copid music file: " + text15);
				}
				catch (Exception ex6)
				{
					Logger.LogInfo(string.Concat(new string[] { "Failed to copy music file ", text14, " to ", text15, ": ", ex6.Message }));
					return;
				}
			}
			foreach (string text16 in Directory.GetFiles(text11, "*.*"))
			{
				string text17 = text16.Replace(text11, text13);
				try
				{
					File.Copy(text16, text17, true);
					Logger.LogInfo("Copid video file: " + text17);
				}
				catch (Exception ex7)
				{
					Logger.LogInfo(string.Concat(new string[] { "Failed to copy video file ", text16, " to ", text17, ": ", ex7.Message }));
					return;
				}
			}
			string text18 = Path.Combine(text2, "es_features.locale");
			string text19 = Path.Combine(text, "emulationstation", "es_features.locale");
			if (!Directory.Exists(text19))
			{
				try
				{
					Directory.CreateDirectory(text19);
				}
				catch (Exception ex8)
				{
					Logger.Log("[ERROR] Failed to create translations directory: " + ex8.Message);
					Console.ReadKey();
					return;
				}
			}
			string[] array = Directory.GetDirectories(text18, "*", SearchOption.AllDirectories);
			for (int i = 0; i < array.Length; i++)
			{
				string text20 = array[i].Replace(text18, text19);
				if (!Directory.Exists(text20))
				{
					Directory.CreateDirectory(text20);
				}
			}
			foreach (string text21 in Directory.GetFiles(text18, "*.*", SearchOption.AllDirectories))
			{
				string text22 = text21.Replace(text18, text19);
				File.Copy(text21, text22, true);
			}
			Logger.LogInfo("Create locale folder: " + text19);
		}

		// Token: 0x0600005F RID: 95 RVA: 0x00004A98 File Offset: 0x00002C98
		private static void CreateVersionFile(BuilderOptions options)
		{
			Logger.LogLabel("create_version");
			Console.WriteLine(":: CREATE VERSION FILES...");
			string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "build");
			string text2 = Path.Combine(text, "system", "version.info");
			string text3 = Path.Combine(text, "emulationstation", "version.info");
			string text4 = string.Concat(new string[] { options.RetrobatVersion, "-", options.Branch, "-", options.Architecture });
			File.WriteAllText(text2, text4);
			Logger.LogInfo("Created version file: " + text2);
			File.WriteAllText(text3, text4);
			Logger.LogInfo("Created version file: " + text3);
		}

		// Token: 0x06000060 RID: 96 RVA: 0x00004B54 File Offset: 0x00002D54
		private static void CopyTemplateFiles(BuilderOptions options)
		{
			Logger.LogLabel("copy_template");
			Console.WriteLine(":: COPY TEMPLATE FILES...");
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			string text = Path.Combine(baseDirectory, "build");
			string text2 = Path.Combine(baseDirectory, "system", "tools", "7za.exe");
			if (!File.Exists(text2))
			{
				Logger.Log("[ERROR] 7za.exe not found at: " + text2);
				return;
			}
			string text3 = Path.Combine(text, "system", "configgen", "templates_files.lst");
			if (!File.Exists(text3))
			{
				Logger.LogInfo("Missing 'templates_files.lst' file.");
				return;
			}
			foreach (string text4 in File.ReadAllLines(text3))
			{
				if (!string.IsNullOrWhiteSpace(text4) && text4.Contains("|"))
				{
					string[] array2 = text4.Split(new char[] { '|' });
					string text5 = Path.Combine(text, Methods.NormalizePath(array2[0]));
					string text6 = Path.Combine(text, Methods.NormalizePath(array2[1]));
					Logger.LogInfo("\nProcessing: " + text5 + " -> " + text6);
					try
					{
						if (File.Exists(text5))
						{
							if (Path.GetExtension(text5).ToLowerInvariant() == ".zip")
							{
								Logger.LogInfo(" - Extracting ZIP using 7z...");
								string text7 = (Directory.Exists(text6) ? text6 : Path.GetDirectoryName(text6));
								Directory.CreateDirectory(text7);
								Methods.ExtractZipWith7z(text2, text5, text7);
							}
							else
							{
								Logger.LogInfo(" - Copying file...");
								Directory.CreateDirectory(Path.GetDirectoryName(text6));
								File.Copy(text5, text6, true);
							}
						}
						else if (Directory.Exists(text5))
						{
							Logger.LogInfo(" - Copying folder contents...");
							Methods.CopyDirectory(text5, text6);
						}
						else
						{
							Logger.Log("[ERROR] Source not found: " + text5);
						}
					}
					catch (Exception ex)
					{
						Logger.Log("[ERROR] Error processing " + text5 + ": " + ex.Message);
					}
				}
			}
			string text8 = Path.Combine(baseDirectory, "system", "tools", "SDL3_x64.dll");
			if (options.Architecture == "win32")
			{
				text8 = Path.Combine(baseDirectory, "system", "tools", "SDL3_x86.dll");
			}
			string text9 = Path.Combine(text, "emulationstation", "SDL3.dll");
			Logger.LogInfo("Copying SDL3 from " + text8 + " to " + text9);
			File.Copy(text8, text9, true);
		}

		// Token: 0x06000061 RID: 97 RVA: 0x00004DC8 File Offset: 0x00002FC8
		private static void GetLibretroCores(BuilderOptions options)
		{
			if (!options.GetLrcores)
			{
				Logger.LogInfo("Skipping Libretro cores download as per options.");
				return;
			}
			Logger.LogLabel("get_lrcores");
			Console.WriteLine(":: GETTING LIBRETRO CORES...");
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			string text = Path.Combine(Path.Combine(baseDirectory, "build"), "emulators", "retroarch", "cores");
			string text2 = Path.Combine(baseDirectory, "system", "configgen", "lrcores_names.lst");
			if (!File.Exists(text2))
			{
				Logger.LogInfo("Missing 'lrcores_names.lst' file.");
				return;
			}
			string text3 = options.RetrobatFTPPath + "win64/" + options.Branch + "/emulators/cores/";
			foreach (string text4 in File.ReadAllLines(text2))
			{
				if (!string.IsNullOrWhiteSpace(text4))
				{
					string text5 = text3 + text4 + "_libretro.dll.zip";
					Thread.Sleep(3000);
					try
					{
						bool flag = false;
						for (int j = 0; j < 5; j++)
						{
							flag = Methods.DownloadAndExtractArchive_Wget(text5, text, options);
							if (flag)
							{
								Logger.LogInfo("Libretro Core " + text4 + " copied to: " + text);
								break;
							}
							Thread.Sleep(4000);
							j++;
						}
						if (!flag)
						{
							Logger.LogInfo("[WARNING] Failed to download or extract core: " + text4 + " from FTP, looking on RetroArch buildbot");
							text5 = options.RetroArchURL + "/nightly/windows/x86_64/latest/" + text4 + "_libretro.dll.zip";
							Methods.DownloadAndExtractArchive_Wget(text5, text, options);
						}
					}
					catch
					{
						Logger.Log("[ERROR] Error downloading RetroArch core.");
					}
				}
			}
		}

		// Token: 0x06000062 RID: 98 RVA: 0x00004F60 File Offset: 0x00003160
		private static void GetEmulators(BuilderOptions options)
		{
			if (!options.GetEmulators)
			{
				Logger.LogInfo("Skipping Emulators download as per options.");
				return;
			}
			Logger.LogLabel("get_emulators");
			Console.WriteLine(":: GETTING EMULATORS...");
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			string text = Path.Combine(Path.Combine(baseDirectory, "build"), "emulators");
			string text2 = Path.Combine(baseDirectory, "system", "configgen", "emulators_names.lst");
			if (!File.Exists(text2))
			{
				Logger.LogInfo("Missing 'emulators_names.lst' file.");
				return;
			}
			string text3 = options.RetrobatFTPPath + "win64/" + options.Branch + "/emulators/";
			foreach (string text4 in File.ReadAllLines(text2))
			{
				List<string> list = new List<string>
				{
					"retroarch", "eden", "3dsen", "teknoparrot", "citron", "yuzu", "pico8", "ryujinx", "steam", "sudachi",
					"suyu", "yuzu-early-access"
				};
				if (!string.IsNullOrWhiteSpace(text4) && !list.Contains(text4))
				{
					Thread.Sleep(3000);
					string text5 = text3 + text4 + ".7z";
					string text6 = Path.Combine(text, text4);
					try
					{
						bool flag = false;
						for (int j = 0; j < 5; j++)
						{
							flag = Methods.DownloadAndExtractArchive_Wget(text5, text6, options);
							if (flag)
							{
								Logger.LogInfo("Emulator " + text4 + " copied to: " + text6);
								break;
							}
							Thread.Sleep(4000);
							j++;
						}
						if (!flag)
						{
							Logger.LogInfo("[WARNING] Failed to download or extract emulator: " + text4 + " from FTP.");
						}
					}
					catch
					{
						Logger.Log("[ERROR] Error downloading Emulator.");
					}
				}
			}
		}

		// Token: 0x06000063 RID: 99 RVA: 0x00005170 File Offset: 0x00003370
		public static void CreateZipFolderSharpZip(BuilderOptions options)
		{
			Logger.LogLabel("create_ziparchive");
			Console.WriteLine(":: CREATE ZIP ARCHIVE...");
			string text = string.Concat(new string[] { "turborama-v", options.RetrobatVersion, "-", options.Branch, "-", options.Architecture, ".zip" });
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			string text2 = Path.Combine(baseDirectory, "build");
			string text3 = Path.Combine(baseDirectory, text);
			if (!Directory.Exists(text2))
			{
				Logger.LogInfo("[ERROR] Source folder does not exist.");
				return;
			}
			Program.RemoveBlockedBuildFiles(text2);
			if (File.Exists(text3))
			{
				try
				{
					File.Delete(text3);
				}
				catch (Exception ex)
				{
					Logger.LogInfo("[WARNING] Could not delete existing zip file: " + ex.Message);
				}
			}
			using (FileStream fileStream = new FileStream(text3, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				using (ZipOutputStream zipOutputStream = new ZipOutputStream(fileStream))
				{
					zipOutputStream.SetLevel(9);
					zipOutputStream.IsStreamOwner = true;
					foreach (string text4 in Directory.GetFiles(text2, "*", SearchOption.AllDirectories))
					{
						if (Program.IsBlockedBuildFile(text4))
						{
							Logger.LogInfo("Arquivo bloqueado, nao sera empacotado: " + text4);
							continue;
						}

						if (!(Path.GetFullPath(text4) == Path.GetFullPath(text3)))
						{
							ZipEntry zipEntry = new ZipEntry(Program.GetRelativePath(text2, text4).Replace("\\", "/"))
							{
								IsUnicodeText = true
							};
							zipOutputStream.PutNextEntry(zipEntry);
							using (FileStream fileStream2 = File.OpenRead(text4))
							{
								fileStream2.CopyTo(zipOutputStream);
							}
							zipOutputStream.CloseEntry();
						}
					}
					foreach (string text5 in Directory.GetDirectories(text2, "*", SearchOption.AllDirectories))
					{
						if (Directory.GetFiles(text5).Length == 0 && Directory.GetDirectories(text5).Length == 0)
						{
							ZipEntry zipEntry2 = new ZipEntry(Program.GetRelativePath(text2, text5).Replace("\\", "/") + "/");
							zipOutputStream.PutNextEntry(zipEntry2);
							zipOutputStream.CloseEntry();
						}
					}
					zipOutputStream.Finish();
				}
			}
			string text6 = text3 + ".sha256.txt";
			using (FileStream fileStream3 = File.OpenRead(text3))
			{
				using (SHA256 sha = SHA256.Create())
				{
					string text7 = BitConverter.ToString(sha.ComputeHash(fileStream3)).Replace("-", "").ToLowerInvariant();
					File.WriteAllText(text6, text7);
				}
			}
			Logger.LogInfo("ZIP created at: " + text3);
		}

		// Token: 0x06000064 RID: 100 RVA: 0x00005488 File Offset: 0x00003688
		public static string GetRelativePath(string basePath, string fullPath)
		{
			if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
			{
				basePath += Path.DirectorySeparatorChar.ToString();
			}
			Uri uri = new Uri(basePath);
			Uri uri2 = new Uri(fullPath);
			return Uri.UnescapeDataString(uri.MakeRelativeUri(uri2).ToString()).Replace('/', Path.DirectorySeparatorChar);
		}

		// Token: 0x06000065 RID: 101 RVA: 0x000054E8 File Offset: 0x000036E8
		private static void SetVersion(BuilderOptions options)
		{
			string branch = options.Branch;
			string text = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "build"), "emulationstation", ".emulationstation", "es_settings.cfg");
			if (!File.Exists(text))
			{
				return;
			}
			XmlDocument xmlDocument = new XmlDocument();
			xmlDocument.Load(text);
			XmlNode xmlNode = xmlDocument.SelectSingleNode("/config/string[@name='updates.type']");
			if (((xmlNode != null) ? xmlNode.Attributes : null) != null && branch != null)
			{
				xmlNode.Attributes["value"].Value = branch;
				xmlDocument.Save(text);
				Logger.LogInfo("Update type set to: " + branch);
			}
		}
	}
}
