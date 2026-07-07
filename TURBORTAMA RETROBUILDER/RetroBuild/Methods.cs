using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Net;
using System.Reflection;

namespace RetroBuild
{
	// Token: 0x02000006 RID: 6
	internal class Methods
	{
		private static readonly string[] RepoCopySkipSegments = new string[]
		{
			"\\.git\\",
			"\\venv\\",
			"\\node_modules\\",
			"\\__pycache__\\",
			"\\.venv\\"
		};

		public static string ToLongPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return path;
			}

			string fullPath = Path.GetFullPath(path);
			if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
			{
				return fullPath;
			}

			if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
			{
				return @"\\?\UNC\" + fullPath.Substring(2);
			}

			return @"\\?\" + fullPath;
		}

		public static void SafeCreateDirectory(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return;
			}

			Directory.CreateDirectory(ToLongPath(path));
		}

		public static bool SafeCopyFile(string sourceFile, string destinationFile, bool overwrite)
		{
			try
			{
				string destinationDirectory = Path.GetDirectoryName(destinationFile);
				if (!string.IsNullOrEmpty(destinationDirectory))
				{
					SafeCreateDirectory(destinationDirectory);
				}

				File.Copy(ToLongPath(sourceFile), ToLongPath(destinationFile), overwrite);
				return true;
			}
			catch (Exception ex)
			{
				Logger.Log("[WARNING] Falha ao copiar arquivo: " + sourceFile + " -> " + ex.Message);
				return false;
			}
		}

		private static bool ShouldSkipRepoPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return true;
			}

			string normalized = path.Replace('/', '\\');
			foreach (string segment in RepoCopySkipSegments)
			{
				if (normalized.IndexOf(segment, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}

			return normalized.EndsWith("\\.git", StringComparison.OrdinalIgnoreCase);
		}

		// Token: 0x0600004C RID: 76 RVA: 0x00002C5B File Offset: 0x00000E5B
		public static string PathCombineExeDir(string relativePath)
		{
			return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), relativePath));
		}

		// Token: 0x0600004D RID: 77 RVA: 0x00002C78 File Offset: 0x00000E78
		public static void CopyDirectory(string sourceDir, string destDir)
		{
			string[] array = Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories);
			for (int i = 0; i < array.Length; i++)
			{
				Directory.CreateDirectory(array[i].Replace(sourceDir, destDir));
			}
			foreach (string text in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
			{
				File.Copy(text, text.Replace(sourceDir, destDir), true);
			}
		}

		// Token: 0x0600004E RID: 78 RVA: 0x00002CDC File Offset: 0x00000EDC
		public static int RunProcess(string exe, string args, string workingDir, out string output)
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = exe,
				Arguments = args,
				WorkingDirectory = workingDir,
				RedirectStandardOutput = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			Process process = new Process();
			process.StartInfo = processStartInfo;
			int num;
			try
			{
				process.Start();
				output = process.StandardOutput.ReadToEnd();
				process.WaitForExit();
				num = process.ExitCode;
			}
			catch (Exception ex)
			{
				output = "[ERROR] Failed to run process: " + ex.Message;
				num = -1;
			}
			finally
			{
				process.Dispose();
			}
			return num;
		}

		// Token: 0x0600004F RID: 79 RVA: 0x00002D84 File Offset: 0x00000F84
		public static bool DownloadAndExtractArchive_WebClient(string url, string outputDir, BuilderOptions options)
		{
			string fileName = Path.GetFileName(new Uri(url).AbsolutePath);
			string text = Path.Combine(Path.GetTempPath(), fileName);
			Logger.LogInfo("Downloading (WebClient): " + url);
			bool flag;
			try
			{
				using (WebClient webClient = new WebClient())
				{
					webClient.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
					webClient.Headers.Add("Accept", "*/*");
					webClient.DownloadFile(url, text);
				}
				Logger.LogInfo("Download complete: " + text);
				flag = Methods.ExtractArchive(text, outputDir, options);
			}
			catch (Exception ex)
			{
				Logger.Log("[ERROR] Download or extract failed: " + ex.Message);
				flag = false;
			}
			finally
			{
				Methods.TryDeleteFile(text);
			}
			return flag;
		}

		// Token: 0x06000050 RID: 80 RVA: 0x00002E6C File Offset: 0x0000106C
		public static bool DownloadAndExtractArchive_Curl(string url, string outputDir, BuilderOptions options)
		{
			string fileName = Path.GetFileName(new Uri(url).AbsolutePath);
			string text = Path.Combine(Path.GetTempPath(), fileName);
			Logger.LogInfo("Downloading (curl): " + url);
			bool flag;
			try
			{
				string text2 = string.Concat(new string[] { "--silent --show-error --fail -L \"", url, "\" -o \"", text, "\"" });
				using (Process process = Process.Start(new ProcessStartInfo
				{
					FileName = options.CurlPath,
					Arguments = text2,
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				}))
				{
					process.WaitForExit();
					string text3 = process.StandardError.ReadToEnd();
					if (process.ExitCode != 0)
					{
						Logger.Log("[ERROR] Curl download failed: " + text3);
						return false;
					}
				}
				Logger.LogInfo("Download complete: " + text);
				flag = Methods.ExtractArchive(text, outputDir, options);
			}
			catch (Exception ex)
			{
				Logger.Log("[ERROR] Download or extract failed: " + ex.Message);
				flag = false;
			}
			finally
			{
				Methods.TryDeleteFile(text);
			}
			return flag;
		}

		// Token: 0x06000051 RID: 81 RVA: 0x00002FB4 File Offset: 0x000011B4
		public static bool DownloadAndExtractArchive_Wget(string url, string outputDir, BuilderOptions options)
		{
			string fileName = Path.GetFileName(new Uri(url).AbsolutePath);
			string text = Path.Combine(Path.GetTempPath(), fileName);
			Logger.LogInfo("Downloading (wget): " + url);
			bool flag;
			try
			{
				string text2 = string.Concat(new string[] { "--quiet --no-check-certificate --read-timeout=20 --timeout=15 -t 3 -O \"", text, "\" \"", url, "\"" });
				using (Process process = Process.Start(new ProcessStartInfo
				{
					FileName = options.WgetPath,
					Arguments = text2,
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				}))
				{
					process.WaitForExit();
					string text3 = process.StandardError.ReadToEnd();
					if (process.ExitCode != 0)
					{
						Logger.Log("[ERROR] Wget download failed: " + text3);
						return false;
					}
				}
				Logger.LogInfo("Download complete: " + text);
				flag = Methods.ExtractArchive(text, outputDir, options);
			}
			catch (Exception ex)
			{
				Logger.Log("[ERROR] Download or extract failed: " + ex.Message);
				flag = false;
			}
			finally
			{
				Methods.TryDeleteFile(text);
			}
			return flag;
		}

		// Token: 0x06000052 RID: 82 RVA: 0x000030FC File Offset: 0x000012FC
		private static bool ExtractArchive(string archivePath, string outputDir, BuilderOptions options)
		{
			if (!Directory.Exists(outputDir))
			{
				Directory.CreateDirectory(outputDir);
			}
			bool flag;
			using (Process process = Process.Start(new ProcessStartInfo
			{
				FileName = options.SevenZipPath,
				Arguments = string.Concat(new string[] { "x \"", archivePath, "\" -o\"", outputDir, "\" -y" }),
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			}))
			{
				process.WaitForExit();
				process.StandardOutput.ReadToEnd();
				string text = process.StandardError.ReadToEnd();
				if (process.ExitCode != 0)
				{
					Logger.Log("[ERROR] Extraction failed: " + text);
					flag = false;
				}
				else
				{
					Logger.LogInfo("Extraction complete to: " + outputDir);
					flag = true;
				}
			}
			return flag;
		}

		// Token: 0x06000053 RID: 83 RVA: 0x000031E8 File Offset: 0x000013E8
		public static bool CloneOrUpdateGitRepo(string repoUrl, string buildFolder)
		{
			string text = Path.Combine(Path.GetTempPath(), "retrobat_bios_temp");
			bool flag;
			try
			{
				Logger.LogInfo("Downloading (git): " + repoUrl);
				if (Directory.Exists(text) && Directory.Exists(text))
				{
					foreach (string text2 in Directory.GetFiles(text, "*", SearchOption.AllDirectories))
					{
						try
						{
							File.SetAttributes(text2, FileAttributes.Normal);
							File.Delete(text2);
						}
						catch (UnauthorizedAccessException)
						{
							Logger.WriteConsole("Access denied to file: " + text2);
						}
					}
					try
					{
						Directory.Delete(text, true);
					}
					catch (UnauthorizedAccessException)
					{
						Logger.WriteConsole("Access denied to directory: " + text);
					}
				}
				using (Process process = Process.Start(new ProcessStartInfo
				{
					FileName = "git",
					Arguments = string.Concat(new string[] { "clone ", repoUrl, " \"", text, "\"" }),
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				}))
				{
					process.WaitForExit();
					string text3 = process.StandardError.ReadToEnd();
					if (process.ExitCode != 0)
					{
						Logger.Log("[ERROR] Git clone failed: " + text3);
						return false;
					}
				}
				Logger.LogInfo("git downloaded from: " + repoUrl);
				if (!Directory.Exists(buildFolder))
				{
					Directory.CreateDirectory(buildFolder);
				}
				foreach (string text4 in Directory.GetDirectories(text, "*", SearchOption.AllDirectories))
				{
					if (ShouldSkipRepoPath(text4))
					{
						continue;
					}

					string text5 = text4.Replace(text, buildFolder);
					SafeCreateDirectory(text5);
				}
				foreach (string text6 in Directory.GetFiles(text, "*.*", SearchOption.AllDirectories))
				{
					if (ShouldSkipRepoPath(text6))
					{
						continue;
					}

					string text7 = text6.Replace(text, buildFolder);
					SafeCopyFile(text6, text7, true);
				}
				Logger.LogInfo("Repository copied to " + buildFolder + ".");
				flag = true;
			}
			catch (Exception ex)
			{
				Logger.Log("[ERROR] Failed to clone and copy repo: " + ex.Message);
				flag = false;
			}
			finally
			{
				try
				{
					if (Directory.Exists(text))
					{
						Directory.Delete(text, true);
					}
				}
				catch
				{
				}
			}
			return flag;
		}

		// Token: 0x06000054 RID: 84 RVA: 0x000034E4 File Offset: 0x000016E4
		public static void DeleteGitFiles(string path)
		{
			if (Directory.Exists(path))
			{
				string text = Path.Combine(path, ".git");
				if (Directory.Exists(text))
				{
					try
					{
						Directory.Delete(text, true);
						Logger.LogInfo("Deleted .git folder from " + path);
					}
					catch (Exception ex)
					{
						Logger.Log("[ERROR] Failed to delete .git folder: " + ex.Message);
					}
					foreach (string text2 in Directory.GetFiles(path, ".git", SearchOption.AllDirectories))
					{
						try
						{
							File.SetAttributes(text2, FileAttributes.Normal);
							File.Delete(text2);
							Logger.WriteConsole("Deleted file: " + text2);
							Logger.LogInfo("Deleted .git files from " + path);
						}
						catch (Exception ex2)
						{
							Logger.Log("[ERROR] Failed to delete .git files: " + ex2.Message);
						}
					}
				}
			}
		}

		// Token: 0x06000055 RID: 85 RVA: 0x000035D0 File Offset: 0x000017D0
		private static void TryDeleteFile(string path)
		{
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch
			{
			}
		}

		// Token: 0x06000056 RID: 86 RVA: 0x00003600 File Offset: 0x00001800
		public static bool CreateZipWith7z(
			string sevenZipExe,
			string zipFilePath,
			string sourceDirectory,
			int compressionLevel,
			long totalInputBytes,
			int totalFileCount,
			Action<long, long, DateTime, string> progressCallback)
		{
			if (!Directory.Exists(sourceDirectory))
			{
				throw new DirectoryNotFoundException("Pasta de origem nao encontrada: " + sourceDirectory);
			}

			if (File.Exists(zipFilePath))
			{
				File.Delete(zipFilePath);
			}

			string speedFlags = compressionLevel <= 0 ? "-mtc=off -mtm=off -mta=off" : string.Empty;
			string arguments = string.Format(
				"a -tzip \"{0}\" \"{1}\\*\" -r -mx{2} -mmt=on -bb1 {3} -y",
				zipFilePath,
				sourceDirectory.TrimEnd('\\'),
				compressionLevel,
				speedFlags).Replace("  ", " ").Trim();

			Logger.LogInfo("Creating ZIP with 7-Zip: " + arguments);
			Logger.WriteConsole("Compactando com 7-Zip (nivel " + compressionLevel + ", todos os nucleos da CPU)...");
			Logger.WriteConsole("Arquivos: " + totalFileCount + " | Tamanho de entrada: " + FormatBytes(totalInputBytes));

			DateTime startTime = DateTime.Now;
			string currentFile = "Iniciando...";
			bool running = true;

			Thread progressThread = new Thread(delegate()
			{
				while (running)
				{
					try
					{
						long processedBytes = 0L;
						if (File.Exists(zipFilePath))
						{
							processedBytes = new FileInfo(zipFilePath).Length;
						}

						if (totalInputBytes > 0L)
						{
							processedBytes = Math.Min(totalInputBytes, processedBytes);
						}

						if (progressCallback != null)
						{
							progressCallback(processedBytes, totalInputBytes, startTime, currentFile);
						}
					}
					catch
					{
					}

					Thread.Sleep(250);
				}
			});
			progressThread.IsBackground = true;
			progressThread.Start();

			Process process = new Process();
			process.StartInfo.FileName = sevenZipExe;
			process.StartInfo.Arguments = arguments;
			process.StartInfo.CreateNoWindow = true;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.RedirectStandardError = true;
			process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
			process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
			process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
			{
				if (!string.IsNullOrWhiteSpace(e.Data))
				{
					currentFile = e.Data.Trim();
				}
			};
			process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
			{
				if (!string.IsNullOrWhiteSpace(e.Data))
				{
					currentFile = e.Data.Trim();
				}
			};
			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			process.WaitForExit();
			running = false;
			progressThread.Join(1000);

			if (progressCallback != null)
			{
				progressCallback(totalInputBytes, totalInputBytes, startTime, "Concluido");
			}

			if (process.ExitCode != 0 || !File.Exists(zipFilePath))
			{
				Logger.Log("[ERROR] 7-Zip archive failed with exit code " + process.ExitCode);
				return false;
			}

			Logger.LogInfo("ZIP created with 7-Zip at: " + zipFilePath);
			return true;
		}

		private static string FormatBytes(long bytes)
		{
			string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
			double value = bytes;
			int unit = 0;
			while (value >= 1024.0 && unit < units.Length - 1)
			{
				value /= 1024.0;
				unit++;
			}

			return string.Format("{0:0.00} {1}", value, units[unit]);
		}

		public static void ExtractZipWith7z(string sevenZipExe, string zipFilePath, string outputDir)
		{
			Process process = new Process();
			process.StartInfo.FileName = sevenZipExe;
			process.StartInfo.Arguments = string.Concat(new string[] { "x \"", zipFilePath, "\" -o\"", outputDir, "\" -y" });
			process.StartInfo.CreateNoWindow = true;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.RedirectStandardError = true;
			process.Start();
			process.StandardOutput.ReadToEnd();
			string text = process.StandardError.ReadToEnd();
			process.WaitForExit();
			if (process.ExitCode != 0)
			{
				throw new Exception("7z extraction failed: " + text);
			}
		}

		// Token: 0x06000057 RID: 87 RVA: 0x000036C3 File Offset: 0x000018C3
		public static string NormalizePath(string path)
		{
			return path.Trim().TrimStart(new char[] { '\\' }).Replace('\\', Path.DirectorySeparatorChar);
		}
	}
}
