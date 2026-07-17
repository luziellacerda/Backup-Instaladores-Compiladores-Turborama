using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TurboramaRomLinker.Models;

namespace TurboramaRomLinker.Services
{
    public sealed class RomLinkService
    {
        private const string SistemaFolder = "sistema";
        private const string RomsFolder = "roms";
        private const string ExtraRomsFolder = "TurboRoms";
        private const string ReferenceCatalogRelativePath = "Docs\\es_systems_reference.cfg";
        private const int MasterSearchMaxDepth = 12;

        // Regra solicitada: a pasta mestre pode estar dentro de qualquer pasta,
        // mas a busca profunda do es_systems.cfg mestre fica limitada a C: e D:.
        private static readonly char[] MasterSearchDriveLetters = new char[] { 'C', 'D' };

        private static readonly string[] ConfigRelativePaths = new string[]
        {
            "sistema\\emulationstation\\.emulationstation\\es_systems.cfg",
            "sistema\\emulationstation\\es_systems.cfg",
            "sistema\\.emulationstation\\es_systems.cfg",
            "emulationstation\\.emulationstation\\es_systems.cfg",
            ".emulationstation\\es_systems.cfg"
        };

        public DriveScanResult BuildPlan()
        {
            DriveScanResult result = new DriveScanResult();
            List<string> romScanRoots = GetCandidateDriveRoots().ToList();
            List<string> masterSearchRoots = GetMasterSearchDriveRoots().ToList();

            if (romScanRoots.Count == 0)
            {
                result.Messages.Add("Nenhuma unidade pronta foi detectada pelo Windows.");
                return result;
            }

            result.Messages.Add("Unidades analisadas para TurboRoms\\roms: " + string.Join(", ", romScanRoots.ToArray()));
            result.Messages.Add("Busca principal da mestre: raiz do executável + sistema\\emulationstation\\.emulationstation\\es_systems.cfg.");
            result.Messages.Add("Busca reserva da mestre em C: e D: somente se o arquivo não estiver ao lado do executável.");

            MasterLocation master = FindMasterLocation(masterSearchRoots, result.Messages);
            if (master == null || string.IsNullOrWhiteSpace(master.Root))
            {
                result.Messages.Add("Pasta mestre não encontrada.");
                result.Messages.Add("O .exe deve ficar na raiz do Turborama, uma pasta acima de sistema.");
                result.Messages.Add("Caminho esperado: <raiz>\\sistema\\emulationstation\\.emulationstation\\es_systems.cfg");
                result.Messages.Add("Coloque o .exe nessa mesma raiz para a análise automática funcionar.");
                result.Messages.Add("As ROMs extras são procuradas em: <unidade>:\\TurboRoms\\roms.");
                return result;
            }

            result.MasterRoot = EnsureTrailingSlash(master.Root);
            result.MasterConfigPath = master.ConfigPath;
            result.MasterRomsPath = Path.Combine(result.MasterRoot, SistemaFolder, RomsFolder);

            Dictionary<string, EsSystemInfo> systemCatalog = LoadValidSystems(result, master, result.Messages);

            if (result.ValidSystems.Count == 0)
            {
                result.Messages.Add("Nenhum sistema válido foi carregado do es_systems.cfg. Nenhum link será criado.");
                return result;
            }

            if (!Directory.Exists(result.MasterRomsPath)) Directory.CreateDirectory(result.MasterRomsPath);

            result.Messages.Add("Pasta mestre: " + result.MasterRoot);
            result.Messages.Add("Pasta de ROMs mestre: " + result.MasterRomsPath);
            result.Messages.Add("Catálogo carregado: " + result.ValidSystems.Count + " sistemas válidos.");
            if (master.UsingReferenceCatalog)
            {
                result.Messages.Add("Aviso: es_systems.cfg mestre não foi encontrado. Foi usado o catálogo interno Docs\\es_systems_reference.cfg para conseguir analisar automaticamente.");
            }

            List<string> romRoots = FindTurboRomsRoots(romScanRoots).ToList();
            if (romRoots.Count == 0)
            {
                result.Messages.Add("Nenhuma pasta <unidade>:\\TurboRoms\\roms foi encontrada nas unidades analisadas.");
                return result;
            }

            foreach (string romRoot in romRoots)
            {
                result.Messages.Add("Pasta TurboRoms encontrada: " + romRoot);
            }

            HashSet<string> plannedSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string sourceRomsPath in romRoots)
            {
                string driveRoot = EnsureTrailingSlash(Path.GetPathRoot(sourceRomsPath));

                foreach (DirectoryInfo romDirectory in SafeGetDirectories(sourceRomsPath))
                {
                    string systemName = romDirectory.Name;
                    string linkPath = Path.Combine(result.MasterRomsPath, systemName);
                    string sourcePath = romDirectory.FullName;

                    RomLinkPlanItem item = new RomLinkPlanItem();
                    item.SystemName = systemName;
                    item.SourceDrive = driveRoot;
                    item.SourcePath = sourcePath;
                    item.LinkPath = linkPath;
                    item.Success = false;

                    if (!result.ValidSystems.Contains(systemName))
                    {
                        item.Action = RomLinkAction.SkippedInvalidSystem;
                        item.Message = "Ignorado: pasta não existe no catálogo es_systems.cfg.";
                        result.Items.Add(item);
                        continue;
                    }

                    EsSystemInfo systemInfo;
                    systemCatalog.TryGetValue(systemName, out systemInfo);
                    if (!HasCompatibleGame(sourcePath, systemInfo))
                    {
                        item.Action = RomLinkAction.SkippedInvalidSystem;
                        item.Message = "Ignorado: pasta sem jogos compatíveis. Extensões aceitas no es_systems.cfg.";
                        result.Items.Add(item);
                        result.Messages.Add("Ignorado: " + sourcePath + " -> sem jogos compatíveis para " + systemName + ".");
                        continue;
                    }

                    if (PathsEqual(sourcePath, linkPath))
                    {
                        item.Action = RomLinkAction.SkippedMasterFolder;
                        item.Message = "Ignorado: origem e destino são a mesma pasta.";
                        plannedSystems.Add(systemName);
                        result.Items.Add(item);
                        continue;
                    }

                    if (plannedSystems.Contains(systemName))
                    {
                        item.Action = RomLinkAction.SkippedDuplicate;
                        item.Message = "Ignorado: já existe uma pasta/link planejado para este sistema.";
                        result.Items.Add(item);
                        continue;
                    }

                    if (Directory.Exists(linkPath))
                    {
                        if (JunctionService.IsReparsePoint(linkPath))
                        {
                            item.Action = RomLinkAction.AlreadyExists;
                            item.Message = "Link já existe na pasta mestre.";
                        }
                        else
                        {
                            item.Action = RomLinkAction.PreserveRealFolder;
                            item.Message = "Já existe pasta real na mestre. Preservada para evitar perda de dados.";
                        }

                        plannedSystems.Add(systemName);
                        result.Items.Add(item);
                        continue;
                    }

                    item.Action = RomLinkAction.CreateJunction;
                    item.Message = "Pronto para criar junction: " + linkPath + " -> " + sourcePath;
                    plannedSystems.Add(systemName);
                    result.Items.Add(item);
                }
            }

            result.Items.Sort(delegate(RomLinkPlanItem a, RomLinkPlanItem b)
            {
                int byAction = a.Action.CompareTo(b.Action);
                if (byAction != 0) return byAction;
                int bySystem = string.Compare(a.SystemName, b.SystemName, StringComparison.OrdinalIgnoreCase);
                if (bySystem != 0) return bySystem;
                return string.Compare(a.SourcePath, b.SourcePath, StringComparison.OrdinalIgnoreCase);
            });

            return result;
        }

        public DriveScanResult ApplyPlan()
        {
            DriveScanResult result = BuildPlan();
            foreach (RomLinkPlanItem item in result.Items)
            {
                if (item.CanCreate)
                {
                    JunctionService.CreateJunction(item);
                }
            }

            return result;
        }

        public string FindMasterRoot()
        {
            List<string> roots = GetMasterSearchDriveRoots().ToList();
            MasterLocation master = FindMasterLocation(roots, new List<string>());
            if (master == null) return null;
            return master.Root;
        }

        private static Dictionary<string, EsSystemInfo> LoadValidSystems(DriveScanResult result, MasterLocation master, List<string> messages)
        {
            Dictionary<string, EsSystemInfo> systems = new Dictionary<string, EsSystemInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                systems = EsSystemsCatalog.LoadSystems(master.ConfigPath);
                foreach (string systemName in systems.Keys)
                {
                    result.ValidSystems.Add(systemName);
                }
                return systems;
            }
            catch (Exception ex)
            {
                messages.Add("Não foi possível ler o catálogo principal: " + ex.Message);
            }

            string referencePath = GetReferenceCatalogPath();
            if (!string.IsNullOrWhiteSpace(referencePath) && File.Exists(referencePath) && !PathsEqual(referencePath, master.ConfigPath))
            {
                try
                {
                    systems = EsSystemsCatalog.LoadSystems(referencePath);
                    foreach (string systemName in systems.Keys)
                    {
                        result.ValidSystems.Add(systemName);
                    }
                    result.MasterConfigPath = referencePath;
                    messages.Add("Catálogo interno usado como reserva: " + referencePath);
                    return systems;
                }
                catch (Exception ex)
                {
                    messages.Add("Falha também ao ler o catálogo interno: " + ex.Message);
                }
            }

            return systems;
        }

        private static MasterLocation FindMasterLocation(List<string> masterSearchRoots, List<string> messages)
        {
            string exeRoot = EnsureTrailingSlash(AppDomain.CurrentDomain.BaseDirectory);

            // Regra principal atual: o .exe fica na raiz do Turborama.
            // Exemplo: <raiz>\TurboramaRomLinker.exe e <raiz>\sistema\emulationstation\.emulationstation\es_systems.cfg
            foreach (string relativePath in ConfigRelativePaths)
            {
                string markerPath = Path.Combine(exeRoot, relativePath);
                if (File.Exists(markerPath))
                {
                    messages.Add("es_systems.cfg mestre encontrado ao lado do executável.");
                    return new MasterLocation(exeRoot, markerPath, false);
                }
            }

            // Ajuda no Debug/Release do Visual Studio: sobe algumas pastas procurando a raiz real do projeto/instalação.
            string current = exeRoot;
            for (int i = 0; i < 8; i++)
            {
                DirectoryInfo parent = Directory.GetParent(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (parent == null) break;
                current = EnsureTrailingSlash(parent.FullName);

                foreach (string relativePath in ConfigRelativePaths)
                {
                    string markerPath = Path.Combine(current, relativePath);
                    if (File.Exists(markerPath))
                    {
                        messages.Add("es_systems.cfg mestre encontrado subindo pastas a partir do executável.");
                        return new MasterLocation(current, markerPath, false);
                    }
                }
            }

            List<string> orderedRoots = OrderRootsWithExecutableFirst(masterSearchRoots);

            // Reserva antiga: C: e D:, para não perder compatibilidade com seus testes anteriores.
            foreach (string root in orderedRoots)
            {
                foreach (string relativePath in ConfigRelativePaths)
                {
                    string markerPath = Path.Combine(root, relativePath);
                    if (File.Exists(markerPath))
                    {
                        messages.Add("es_systems.cfg mestre encontrado na raiz da unidade de reserva.");
                        return new MasterLocation(root, markerPath, false);
                    }
                }
            }

            foreach (string root in orderedRoots)
            {
                messages.Add("Busca reserva da mestre em " + root + " até " + MasterSearchMaxDepth + " níveis...");
                string found = FindMasterConfigInsideAnyFolder(root, MasterSearchMaxDepth);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    messages.Add("es_systems.cfg mestre encontrado em subpasta de reserva: " + found);
                    return CreateMasterLocationFromConfig(found, messages);
                }
            }

            string referenceCatalog = GetReferenceCatalogPath();
            if (!string.IsNullOrWhiteSpace(referenceCatalog) && File.Exists(referenceCatalog))
            {
                string fallbackRoot = ChooseFallbackMasterRoot(orderedRoots);
                if (!string.IsNullOrWhiteSpace(fallbackRoot))
                {
                    messages.Add("Pasta mestre escolhida automaticamente pelo sistema\\roms: " + fallbackRoot);
                    messages.Add("Catálogo interno encontrado: " + referenceCatalog);
                    return new MasterLocation(fallbackRoot, referenceCatalog, true);
                }
            }

            return null;
        }

        private static MasterLocation CreateMasterLocationFromConfig(string configPath, List<string> messages)
        {
            string masterRoot = DeriveMasterRootFromConfig(configPath);
            messages.Add("Raiz mestre calculada por ..\\..\\..: " + masterRoot);
            return new MasterLocation(masterRoot, configPath, false);
        }

        private static string DeriveMasterRootFromConfig(string configPath)
        {
            string configDirectory = Path.GetDirectoryName(configPath);
            if (string.IsNullOrWhiteSpace(configDirectory)) return EnsureTrailingSlash(Path.GetPathRoot(configPath));

            // Regra solicitada: a partir da pasta do es_systems.cfg, subir 3 níveis.
            string calculated = Path.GetFullPath(Path.Combine(configDirectory, "..\\..\\.."));
            return EnsureTrailingSlash(calculated);
        }

        private static string FindMasterConfigInsideAnyFolder(string driveRoot, int maxDepth)
        {
            if (string.IsNullOrWhiteSpace(driveRoot)) return null;
            if (!Directory.Exists(driveRoot)) return null;

            Queue<SearchNode> queue = new Queue<SearchNode>();
            queue.Enqueue(new SearchNode(driveRoot, 0));

            while (queue.Count > 0)
            {
                SearchNode node = queue.Dequeue();

                foreach (string relativePath in ConfigRelativePaths)
                {
                    string candidate = Path.Combine(node.Path, relativePath);
                    try
                    {
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch
                    {
                        // Ignora arquivo sem permissão.
                    }
                }

                // Caso a própria pasta analisada já seja .emulationstation.
                string directConfig = Path.Combine(node.Path, "es_systems.cfg");
                try
                {
                    if (File.Exists(directConfig) && LooksLikeEmulationStationConfigPath(directConfig)) return directConfig;
                }
                catch
                {
                    // Ignora arquivo sem permissão.
                }

                if (node.Depth >= maxDepth) continue;

                foreach (string subDirectory in SafeGetDirectoryPaths(node.Path))
                {
                    if (ShouldSkipDirectoryDuringMasterSearch(subDirectory)) continue;
                    queue.Enqueue(new SearchNode(subDirectory, node.Depth + 1));
                }
            }

            return null;
        }

        private static bool LooksLikeEmulationStationConfigPath(string configPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(configPath);
                if (string.IsNullOrWhiteSpace(directory)) return false;

                DirectoryInfo current = new DirectoryInfo(directory);
                if (!current.Name.Equals(".emulationstation", StringComparison.OrdinalIgnoreCase)) return false;

                DirectoryInfo parent = current.Parent;
                if (parent == null) return false;

                return parent.Name.Equals("emulationstation", StringComparison.OrdinalIgnoreCase)
                    || parent.Name.Equals(SistemaFolder, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldSkipDirectoryDuringMasterSearch(string path)
        {
            try
            {
                string name = new DirectoryInfo(path).Name;
                if (name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) return true;
                if (name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)) return true;
                if (name.Equals("Windows", StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static string ChooseFallbackMasterRoot(List<string> orderedRoots)
        {
            string executableRoot = EnsureTrailingSlash(AppDomain.CurrentDomain.BaseDirectory);
            if (!string.IsNullOrWhiteSpace(executableRoot)
                && Directory.Exists(Path.Combine(executableRoot, SistemaFolder, RomsFolder)))
            {
                return executableRoot;
            }

            foreach (string root in orderedRoots)
            {
                if (Directory.Exists(Path.Combine(root, SistemaFolder, RomsFolder))) return root;
            }

            return null;
        }

        private static IEnumerable<string> FindTurboRomsRoots(IEnumerable<string> driveRoots)
        {
            SortedSet<string> result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string root in driveRoots)
            {
                string turboRoms = Path.Combine(root, ExtraRomsFolder, RomsFolder);
                if (Directory.Exists(turboRoms)) result.Add(NormalizeForDisplay(turboRoms));
            }

            // Reforço direto para todas as letras: C:\TurboRoms\roms até Z:\TurboRoms\roms.
            for (char letter = 'C'; letter <= 'Z'; letter++)
            {
                string root = letter + @":\";
                string turboRoms = Path.Combine(root, ExtraRomsFolder, RomsFolder);
                if (Directory.Exists(turboRoms)) result.Add(NormalizeForDisplay(turboRoms));
            }

            return result;
        }

        private static IEnumerable<string> GetCandidateDriveRoots()
        {
            SortedSet<string> roots = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DriveInfo drive in DriveInfo.GetDrives().OrderBy(d => d.Name))
            {
                try
                {
                    if (!drive.IsReady) continue;
                    if (drive.DriveType == DriveType.CDRom || drive.DriveType == DriveType.NoRootDirectory || drive.DriveType == DriveType.Unknown) continue;
                    roots.Add(EnsureTrailingSlash(drive.RootDirectory.FullName));
                }
                catch
                {
                    // Unidade sem acesso ou sem mídia: ignora.
                }
            }

            // Algumas unidades externas aparecem de forma estranha no DriveInfo.
            // Este reforço testa todas as letras diretamente pela raiz.
            for (char letter = 'A'; letter <= 'Z'; letter++)
            {
                string root = letter + @":\";
                try
                {
                    if (Directory.Exists(root)) roots.Add(root);
                }
                catch
                {
                    // Ignora unidade sem acesso.
                }
            }

            return OrderRootsWithExecutableFirst(roots.ToList());
        }

        private static IEnumerable<string> GetMasterSearchDriveRoots()
        {
            SortedSet<string> roots = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (char letter in MasterSearchDriveLetters)
            {
                string root = letter + @":\";
                try
                {
                    if (Directory.Exists(root)) roots.Add(root);
                }
                catch
                {
                    // Ignora unidade sem acesso.
                }
            }

            return OrderRootsWithExecutableFirst(roots.ToList());
        }

        private static List<string> OrderRootsWithExecutableFirst(List<string> roots)
        {
            List<string> ordered = new List<string>(roots.OrderBy(r => r, StringComparer.OrdinalIgnoreCase));
            string executableRoot = EnsureTrailingSlash(Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory));

            if (!string.IsNullOrWhiteSpace(executableRoot))
            {
                int index = ordered.FindIndex(delegate(string r) { return PathsEqual(r, executableRoot); });
                if (index >= 0)
                {
                    string value = ordered[index];
                    ordered.RemoveAt(index);
                    ordered.Insert(0, value);
                }
            }

            return ordered;
        }

        private static bool HasCompatibleGame(string folderPath, EsSystemInfo systemInfo)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return false;

            SortedSet<string> extensions = systemInfo != null ? systemInfo.Extensions : null;
            bool acceptAnyFile = extensions == null || extensions.Count == 0 || extensions.Contains("*");

            foreach (string filePath in SafeEnumerateFiles(folderPath))
            {
                if (acceptAnyFile) return true;
                string extension = Path.GetExtension(filePath);
                if (!string.IsNullOrWhiteSpace(extension) && extensions.Contains(extension.ToLowerInvariant())) return true;
            }

            return false;
        }

        private static IEnumerable<string> SafeEnumerateFiles(string folderPath)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(folderPath);

            while (pending.Count > 0)
            {
                string current = pending.Pop();

                string[] files;
                try
                {
                    files = Directory.GetFiles(current);
                }
                catch
                {
                    files = new string[0];
                }

                foreach (string file in files)
                {
                    yield return file;
                }

                string[] subFolders;
                try
                {
                    subFolders = Directory.GetDirectories(current);
                }
                catch
                {
                    subFolders = new string[0];
                }

                foreach (string subFolder in subFolders)
                {
                    pending.Push(subFolder);
                }
            }
        }

        private static IEnumerable<DirectoryInfo> SafeGetDirectories(string path)
        {
            try
            {
                return new DirectoryInfo(path).GetDirectories();
            }
            catch
            {
                return new DirectoryInfo[0];
            }
        }

        private static IEnumerable<string> SafeGetDirectoryPaths(string path)
        {
            try
            {
                return Directory.GetDirectories(path);
            }
            catch
            {
                return new string[0];
            }
        }

        private static string GetReferenceCatalogPath()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string direct = Path.Combine(baseDirectory, ReferenceCatalogRelativePath);
            if (File.Exists(direct)) return direct;

            string projectRelative = Path.GetFullPath(Path.Combine(baseDirectory, "..\\..\\Docs\\es_systems_reference.cfg"));
            if (File.Exists(projectRelative)) return projectRelative;

            string current = baseDirectory;
            for (int i = 0; i < 6; i++)
            {
                if (string.IsNullOrWhiteSpace(current)) break;

                string candidate = Path.Combine(current, ReferenceCatalogRelativePath);
                if (File.Exists(candidate)) return candidate;

                DirectoryInfo parent = Directory.GetParent(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (parent == null) break;
                current = parent.FullName;
            }

            return direct;
        }

        private static bool PathsEqual(string a, string b)
        {
            string fullA = NormalizePath(a);
            string fullB = NormalizePath(b);
            return string.Equals(fullA, fullB, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string NormalizeForDisplay(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string EnsureTrailingSlash(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (path.EndsWith("\\") || path.EndsWith("/")) return path;
            return path + Path.DirectorySeparatorChar;
        }

        private static bool IsMasterSearchDriveRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return false;
            string normalized = EnsureTrailingSlash(root).ToUpperInvariant();
            foreach (char letter in MasterSearchDriveLetters)
            {
                if (normalized.Equals(letter + @":\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private sealed class MasterLocation
        {
            public MasterLocation(string root, string configPath, bool usingReferenceCatalog)
            {
                Root = EnsureTrailingSlash(root);
                ConfigPath = configPath;
                UsingReferenceCatalog = usingReferenceCatalog;
            }

            public string Root { get; private set; }
            public string ConfigPath { get; private set; }
            public bool UsingReferenceCatalog { get; private set; }
        }

        private sealed class SearchNode
        {
            public SearchNode(string path, int depth)
            {
                Path = path;
                Depth = depth;
            }

            public string Path { get; private set; }
            public int Depth { get; private set; }
        }
    }
}
