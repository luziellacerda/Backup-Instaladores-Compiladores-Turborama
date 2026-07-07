using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace RetroBuild
{
	internal static class LzGamesConsoleUi
	{
		private const string ProductName = "LZ GAMES";
		private const string ProductSubtitle = "PROFESSIONAL COMPILATION & DISTRIBUTION SYSTEM";
		private const string ProductTagline = "LZ TURBO Build Pipeline";
		private const string ProductVersion = "LZ TURBO-3.1";

		private static readonly ConsoleColor Brand = ConsoleColor.Green;
		private static readonly ConsoleColor Accent = ConsoleColor.Cyan;
		private static readonly ConsoleColor Muted = ConsoleColor.DarkGray;
		private static readonly ConsoleColor Text = ConsoleColor.Gray;
		private static readonly ConsoleColor Bright = ConsoleColor.White;
		private static readonly ConsoleColor Warn = ConsoleColor.Yellow;
		private static readonly ConsoleColor ErrorColor = ConsoleColor.Red;

		private static ConsoleColor _savedForeground;
		private static int _stepCounter;

		public static void ClearScreen()
		{
			try
			{
				Console.Clear();
			}
			catch (IOException)
			{
			}
		}

		public static void ShowSplash(BuilderOptions options)
		{
			ClearScreen();
			int width = GetInnerWidth();

			WriteColorLine(Brand, Center("+" + new string('=', width - 2) + "+", width));
			WriteColorLine(Brand, Center("|", width));

			string[] logo = new string[]
			{
				"██╗     ███████╗     ██████╗  █████╗ ███╗   ███╗███████╗███████╗",
				"██║     ╚══███╔╝    ██╔════╝ ██╔══██╗████╗ ████║██╔════╝██╔════╝",
				"██║       ███╔╝     ██║  ███╗███████║██╔████╔██║█████╗  ███████╗",
				"██║      ███╔╝      ██║   ██║██╔══██║██║╚██╔╝██║██╔══╝  ╚════██║",
				"███████╗███████╗    ╚██████╔╝██║  ██║██║ ╚═╝ ██║███████╗███████║",
				"╚══════╝╚══════╝     ╚═════╝ ╚═╝  ╚═╝╚═╝     ╚═╝╚══════╝╚══════╝"
			};

			foreach (string line in logo)
			{
				WriteColorLine(Brand, Center("| " + PadOrTrim(line, width - 4) + " |", width));
			}

			WriteColorLine(Brand, Center("|", width));
			WriteColorLine(Accent, Center("| " + PadOrTrim(ProductSubtitle, width - 4) + " |", width));

			string versionLine = ProductVersion;
			if (options != null)
			{
				versionLine += "  |  " + options.Branch + "  |  " + options.Architecture;
			}

			WriteColorLine(Muted, Center("| " + PadOrTrim(versionLine, width - 4) + " |", width));
			WriteColorLine(Brand, Center("+" + new string('=', width - 2) + "+", width));
			Console.WriteLine();
		}

		public static void ShowConfigSummary(BuilderOptions options)
		{
			if (options == null)
			{
				return;
			}

			DrawPanelHeader("SYSTEM CONFIG", "Parametros carregados de build.ini");

			string[][] rows = new string[][]
			{
				new string[] { "Versao", ProductVersion },
				new string[] { "Branch", options.Branch },
				new string[] { "Arquitetura", options.Architecture },
				new string[] { "7-Zip", File.Exists(options.SevenZipPath) ? "OK" : "AUSENTE" },
				new string[] { "Compressao ZIP", options.ZipCompressionLevel + " (0=rapido, 9=lento)" },
				new string[] { "Modo 7-Zip", options.Use7ZipForArchive ? "Ativado" : "Desativado" },
				new string[] { "Skip SHA256", options.SkipZipSha256 ? "Sim" : "Nao" },
				new string[] { "Skip ZIP existente", options.SkipRecreateZipIfExists ? "Sim" : "Nao" },
				new string[] { "Destino customizado", options.AskArchiveOutputDrive ? "Perguntar unidade" : "Pasta local" }
			};

			foreach (string[] row in rows)
			{
				WriteKeyValue(row[0], row[1]);
			}

			Console.WriteLine();
		}

		public static string ShowMainMenu()
		{
			DrawPanelHeader("BUILD PIPELINE", "Selecione a etapa do processo de compilacao");

			WriteMenuOption("1", "Download & Configure", "Baixa pacotes, emuladores, tema e monta a estrutura completa");
			WriteMenuOption("2", "Create Archive", "Compacta a pasta build em ZIP com barra de progresso em tempo real");
			WriteMenuOption("3", "Create Installer", "Gera setup.exe + pacotes .pkg (requer ZIP pronto)");
			WriteMenuOption("Q", "Quit", "Sair do sistema de compilacao");

			Console.WriteLine();
			DrawSeparator();
			WritePrompt("Digite sua escolha");
			return Console.ReadLine();
		}

		public static void BeginPipelineStep(string title, string description)
		{
			_stepCounter++;
			Console.WriteLine();
			DrawSeparator();
			WriteColor(Bright, string.Format("[ETAPA {0:D2}] ", _stepCounter));
			WriteColor(Brand, title);
			if (!string.IsNullOrWhiteSpace(description))
			{
				Console.WriteLine();
				WriteColor(Muted, "  > " + description);
			}
			DrawThinSeparator();
		}

		public static void Info(string message)
		{
			WriteColor(Accent, "  [i] ");
			WriteColor(Text, DownloadDisplayMask.Apply(message));
			Console.WriteLine();
		}

		public static void Success(string message)
		{
			WriteColor(Brand, "  [+] ");
			WriteColor(Bright, DownloadDisplayMask.Apply(message));
			Console.WriteLine();
		}

		public static void Warning(string message)
		{
			WriteColor(Warn, "  [!] ");
			WriteColor(Text, DownloadDisplayMask.Apply(message));
			Console.WriteLine();
		}

		public static void Error(string message)
		{
			WriteColor(ErrorColor, "  [X] ");
			WriteColor(Bright, DownloadDisplayMask.Apply(message));
			Console.WriteLine();
		}

		public static void ShowArchiveSummary(int fileCount, long totalBytes, int compressionLevel, bool use7Zip)
		{
			DrawPanelHeader("ARCHIVE ENGINE", "Preparando compactacao do pacote final");
			WriteKeyValue("Arquivos", fileCount.ToString("N0"));
			WriteKeyValue("Tamanho entrada", FormatBytes(totalBytes));
			WriteKeyValue("Compressao", compressionLevel + " (0=velocidade maxima)");
			WriteKeyValue("Motor", use7Zip ? "7-Zip multithread" : "SharpZipLib");
			Info("Barra de progresso fixa: a mesma linha sera atualizada ate terminar.");
			Console.WriteLine();
		}

		public static void ShowDriveSelectionHeader(string buildFolderPath, string buildRoot)
		{
			Console.WriteLine();
			DrawPanelHeader("OUTPUT DESTINATION", "Destino do ZIP e do instalador");
			WriteKeyValue("Origem (build)", buildFolderPath);
			WriteKeyValue("Unidade origem", string.IsNullOrWhiteSpace(buildRoot) ? "(desconhecida)" : buildRoot);
			Console.WriteLine();
			Info("Usar OUTRA unidade que a origem costuma acelerar a compactacao.");
			Console.WriteLine();
		}

		public static void ShowDriveList(string retroBuildDirectory, string retroRoot, List<DriveInfo> drives, string buildRoot, string defaultPathFromIni)
		{
			WriteColor(Bright, "  Unidades disponiveis:");
			Console.WriteLine();
			WriteColor(Accent, "  [0] ");
			WriteColor(Text, "Pasta atual do RetroBuild");
			Console.WriteLine();
			WriteColor(Muted, "      " + retroBuildDirectory + DescribeDrive(retroRoot, drives));

			for (int i = 0; i < drives.Count; i++)
			{
				DriveInfo drive = drives[i];
				string marker = string.Empty;
				if (string.Equals(GetDriveRoot(drive.Name), buildRoot, StringComparison.OrdinalIgnoreCase))
				{
					marker = " [origem build]";
				}
				else if (string.Equals(GetDriveRoot(drive.Name), retroRoot, StringComparison.OrdinalIgnoreCase))
				{
					marker = " [RetroBuild]";
				}

				WriteColor(Accent, string.Format("  [{0}] ", i + 1));
				WriteColor(Bright, drive.Name + " ");
				WriteColor(Text, "(" + drive.DriveType + ") Livre: " + FormatBytes(drive.AvailableFreeSpace));
				WriteColor(Warn, marker);
				Console.WriteLine();
			}

			if (!string.IsNullOrWhiteSpace(defaultPathFromIni))
			{
				Console.WriteLine();
				WriteKeyValue("Padrao build.ini", defaultPathFromIni);
			}

			Console.WriteLine();
			WriteColor(Muted, "  Entrada: numero | letra (D:) | caminho completo | ENTER = pasta atual");
			Console.WriteLine();
		}

		public static void ShowSelectedDestination(string selectedDirectory, string buildRoot, string selectedRoot)
		{
			Console.WriteLine();
			Success("Destino selecionado: " + selectedDirectory);
			if (!string.IsNullOrWhiteSpace(buildRoot) &&
				!string.Equals(selectedRoot, buildRoot, StringComparison.OrdinalIgnoreCase))
			{
				Info("Modo otimizado: origem em " + buildRoot + " e destino em " + selectedRoot + ".");
			}
			else
			{
				Warning("Origem e destino na mesma unidade (" + selectedRoot + "). Pode ser mais lento.");
			}
			Console.WriteLine();
		}

		public static void ShowInstallerSplitHeader(long totalBytes, long partSizeBytes)
		{
			DrawPanelHeader("INSTALLER PACKAGER", "Gerando setup.exe e partes .pkg");
			WriteKeyValue("Tamanho ZIP", FormatBytes(totalBytes));
			WriteKeyValue("Tamanho por parte", FormatBytes(partSizeBytes));
			Info("Barra de progresso fixa: a mesma linha sera atualizada ate terminar.");
			Console.WriteLine();
		}

		public static void ShowCompletion()
		{
			Console.WriteLine();
			DrawSeparator();
			WriteColor(Brand, "  ");
			for (int i = 0; i < 3; i++)
			{
				Console.Write("█");
			}
			WriteColor(Bright, " BUILD CONCLUIDO COM SUCESSO ");
			for (int i = 0; i < 3; i++)
			{
				WriteColor(Brand, "█");
			}
			Console.WriteLine();
			WriteColor(Muted, "  " + ProductName + " | " + ProductTagline);
			DrawSeparator();
			Console.WriteLine();
			WritePrompt("Pressione qualquer tecla para sair");
		}

		public static void ShowFatalError(string message)
		{
			Console.WriteLine();
			DrawSeparator();
			Error(message);
			DrawSeparator();
		}

		public static string BuildProgressBar(int percent, int barWidth)
		{
			percent = Math.Max(0, Math.Min(100, percent));
			barWidth = Math.Max(10, barWidth);
			int filled = percent * barWidth / 100;
			return "[" + new string('█', filled) + new string('░', barWidth - filled) + "]";
		}

		public static void WriteProgressLine(string line)
		{
			line = DownloadDisplayMask.Apply(line);
			int width = GetSafeConsoleWidth() - 1;
			if (width < 50)
			{
				width = 50;
			}
			if (line.Length > width)
			{
				line = line.Substring(0, width);
			}

			try
			{
				_savedForeground = Console.ForegroundColor;
				Console.ForegroundColor = Brand;
				Console.Write("\r" + line.PadRight(width));
				Console.ForegroundColor = _savedForeground;
			}
			catch
			{
				Console.Write("\r" + line.PadRight(width));
			}
		}

		public static string FormatBytes(long bytes)
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

		public static int GetSafeConsoleWidth()
		{
			try
			{
				int width = Console.WindowWidth;
				if (width < 70)
				{
					return 70;
				}
				return width;
			}
			catch
			{
				return 100;
			}
		}

		private static int GetInnerWidth()
		{
			return Math.Max(78, Math.Min(100, GetSafeConsoleWidth()));
		}

		private static void DrawPanelHeader(string title, string subtitle)
		{
			int width = GetInnerWidth();
			WriteColorLine(Brand, "  +" + new string('-', width - 4) + "+");
			WriteColor(Bright, "  | ");
			WriteColor(Brand, title.PadRight(width - 6));
			WriteColor(Bright, "|");
			Console.WriteLine();
			if (!string.IsNullOrWhiteSpace(subtitle))
			{
				WriteColor(Muted, "  | " + PadOrTrim(subtitle, width - 6));
				WriteColor(Bright, "|");
				Console.WriteLine();
				WriteColorLine(Brand, "  +" + new string('-', width - 4) + "+");
			}
			else
			{
				WriteColorLine(Brand, "  +" + new string('-', width - 4) + "+");
			}
		}

		private static void DrawSeparator()
		{
			WriteColorLine(Muted, "  " + new string('─', GetInnerWidth() - 2));
		}

		private static void DrawThinSeparator()
		{
			WriteColorLine(Muted, "  " + new string('·', Math.Min(60, GetInnerWidth() - 2)));
		}

		private static void WriteMenuOption(string key, string title, string description)
		{
			WriteColor(Accent, "  [" + key + "] ");
			WriteColor(Bright, title);
			Console.WriteLine();
			WriteColor(Muted, "      " + description);
			Console.WriteLine();
		}

		private static void WriteKeyValue(string key, string value)
		{
			WriteColor(Muted, "  " + key.PadRight(22));
			WriteColor(Bright, DownloadDisplayMask.Apply(value ?? string.Empty));
			Console.WriteLine();
		}

		private static void WritePrompt(string text)
		{
			WriteColor(Brand, "  " + text + ": ");
			WriteColor(Bright, string.Empty);
		}

		private static void WriteColor(ConsoleColor color, string text)
		{
			try
			{
				_savedForeground = Console.ForegroundColor;
				Console.ForegroundColor = color;
				Console.Write(text);
				Console.ForegroundColor = _savedForeground;
			}
			catch
			{
				Console.Write(text);
			}
		}

		private static void WriteColorLine(ConsoleColor color, string text)
		{
			WriteColor(color, text);
			Console.WriteLine();
		}

		private static string Center(string text, int width)
		{
			if (text.Length >= width)
			{
				return text.Substring(0, width);
			}
			int pad = (width - text.Length) / 2;
			return new string(' ', pad) + text + new string(' ', width - text.Length - pad);
		}

		private static string PadOrTrim(string text, int width)
		{
			if (text.Length > width)
			{
				return text.Substring(0, width);
			}
			return text.PadRight(width);
		}

		private static string GetDriveRoot(string driveName)
		{
			if (string.IsNullOrWhiteSpace(driveName))
			{
				return string.Empty;
			}
			if (!driveName.EndsWith("\\", StringComparison.Ordinal))
			{
				return driveName + "\\";
			}
			return driveName;
		}

		private static string DescribeDrive(string driveRoot, List<DriveInfo> drives)
		{
			if (string.IsNullOrWhiteSpace(driveRoot))
			{
				return string.Empty;
			}

			DriveInfo drive = drives.FirstOrDefault(d => string.Equals(GetDriveRoot(d.Name), driveRoot, StringComparison.OrdinalIgnoreCase));
			if (drive == null)
			{
				return string.Empty;
			}

			return " | Livre: " + FormatBytes(drive.AvailableFreeSpace);
		}
	}
}