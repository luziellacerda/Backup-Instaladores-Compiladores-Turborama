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
        /// <summary>Maximo de pastas/HDs por sistema (ex.: D+E+F+G snes). Sem limite de 1 HD.</summary>
        public const int MaxSourcesPerSystem = 8;

        // Busca da mestre em todas as unidades locais (nao so C:/D:).

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
            return BuildPlan(null);
        }

        /// <param name="extraRomRoots">
        /// Pastas extras escolhidas manualmente (botão PROCURAR): unidade, TurboRoms, TurboRoms\roms ou pasta de um sistema.
        /// </param>
        public DriveScanResult BuildPlan(IEnumerable<string> extraRomRoots)
        {
            DriveScanResult result = new DriveScanResult();
            List<string> romScanRoots = GetCandidateDriveRoots().ToList();
            List<string> masterSearchRoots = GetMasterSearchDriveRoots().ToList();

            if (romScanRoots.Count == 0)
            {
                result.Messages.Add("Nenhuma unidade pronta foi detectada pelo Windows.");
                // ainda permite so pastas manuais
            }

            result.Messages.Add("Unidades detectadas: " + (romScanRoots.Count == 0 ? "(nenhuma)" : string.Join(", ", romScanRoots.ToArray())));
            result.Messages.Add("Fontes auto: X:\\TurboRoms\\roms | X:\\TurboRoms\\<sistema> | X:\\roms\\<sistema>");
            result.Messages.Add("Multi-HD: até " + MaxSourcesPerSystem + " pastas por sistema. Use PROCURAR para USB/HD que a auto não achar.");
            result.Messages.Add("Busca mestre: ao lado do .exe + todas as unidades. Bats com cd relativo viram caminho absoluto no CRIAR.");

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

            List<string> romRoots = FindRomSourceRoots(romScanRoots, result.ValidSystems, result.Messages).ToList();

            // Pastas escolhidas no botão PROCURAR (USB, G:, etc.)
            if (extraRomRoots != null)
            {
                foreach (string raw in extraRomRoots)
                {
                    foreach (string expanded in ExpandManualRomRoot(raw, result.ValidSystems, result.Messages))
                    {
                        string display = NormalizeForDisplay(expanded);
                        bool already = false;
                        foreach (string existing in romRoots)
                        {
                            if (string.Equals(existing, display, StringComparison.OrdinalIgnoreCase))
                            {
                                already = true;
                                break;
                            }
                        }
                        if (!already)
                        {
                            romRoots.Add(display);
                            result.Messages.Add("Pasta MANUAL adicionada: " + display);
                        }
                    }
                }
            }

            if (romRoots.Count == 0)
            {
                result.Messages.Add("Nenhuma pasta de ROMs com sistemas do catálogo foi encontrada.");
                result.Messages.Add("Coloque os jogos em:  X:\\TurboRoms\\roms\\<sistema>  ou use o botão PROCURAR e aponte a pasta/unidade.");
                return result;
            }

            foreach (string romRoot in romRoots)
            {
                int sysFolders = 0;
                try { sysFolders = Directory.GetDirectories(romRoot).Length; }
                catch { }
                result.Messages.Add("Pasta de ROMs: " + romRoot + "  (" + sysFolders + " pastas de sistema)");
            }

            // Contagem por sistema: permite 4+ HDs (MaxSourcesPerSystem), nao so 1.
            Dictionary<string, int> plannedCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string sourceRomsPath in romRoots)
            {
                string driveRoot = EnsureTrailingSlash(Path.GetPathRoot(sourceRomsPath));

                foreach (DirectoryInfo romDirectory in SafeGetDirectories(sourceRomsPath))
                {
                    string systemName = romDirectory.Name;
                    string systemLinkPath = Path.Combine(result.MasterRomsPath, systemName);
                    string sourcePath = romDirectory.FullName;

                    RomLinkPlanItem item = new RomLinkPlanItem();
                    item.SystemName = systemName;
                    item.SourceDrive = driveRoot;
                    item.SourcePath = sourcePath;
                    item.LinkPath = systemLinkPath;
                    item.Success = false;

                    EsSystemInfo systemInfo;
                    systemCatalog.TryGetValue(systemName, out systemInfo);
                    item.DisplayName = SystemDisplayNames.Get(
                        systemName,
                        systemInfo != null ? systemInfo.FullName : null);

                    if (!result.ValidSystems.Contains(systemName))
                    {
                        item.Action = RomLinkAction.SkippedInvalidSystem;
                        item.Message = "Ignorado: pasta não existe no catálogo es_systems.cfg.";
                        result.Items.Add(item);
                        continue;
                    }
                    if (!HasCompatibleGame(sourcePath, systemInfo))
                    {
                        item.Action = RomLinkAction.SkippedInvalidSystem;
                        item.Message = "Ignorado: pasta sem jogos compatíveis. Extensões aceitas no es_systems.cfg.";
                        result.Items.Add(item);
                        result.Messages.Add("Ignorado: " + sourcePath + " -> sem jogos compatíveis para " + systemName + ".");
                        continue;
                    }

                    if (PathsEqual(sourcePath, systemLinkPath))
                    {
                        item.Action = RomLinkAction.SkippedMasterFolder;
                        item.Message = "Ignorado: origem e destino são a mesma pasta.";
                        result.Items.Add(item);
                        continue;
                    }

                    string sourceKey = NormalizeForDisplay(sourcePath);
                    if (seenSourcePaths.Contains(sourceKey))
                    {
                        item.Action = RomLinkAction.SkippedDuplicate;
                        item.Message = "Ignorado: mesma pasta de origem já listada.";
                        result.Items.Add(item);
                        continue;
                    }

                    int count;
                    plannedCount.TryGetValue(systemName, out count);
                    if (count >= MaxSourcesPerSystem)
                    {
                        item.Action = RomLinkAction.SkippedDuplicate;
                        item.Message = "Ignorado: máximo " + MaxSourcesPerSystem + " HDs/pastas por sistema.";
                        result.Items.Add(item);
                        continue;
                    }

                    // ES NÃO lê jogos se a pasta do sistema for um junction único.
                    // Destino = pasta REAL sistema\roms\ps5 e dentro: link de CADA arquivo/subpasta.
                    item.LinkPath = systemLinkPath;

                    if (Directory.Exists(systemLinkPath)
                        && !JunctionService.IsReparsePoint(systemLinkPath)
                        && LooksAlreadyMirrored(systemLinkPath, sourcePath))
                    {
                        item.Action = RomLinkAction.AlreadyExists;
                        item.Message = "Arquivos já linkados em " + systemLinkPath;
                        result.Items.Add(item);
                        seenSourcePaths.Add(sourceKey);
                        continue;
                    }

                    item.Action = RomLinkAction.CreateJunction;
                    item.Message = "Linkar ARQUIVOS/pastas de " + sourcePath
                        + "  →  dentro de " + systemLinkPath
                        + "  (.bat, .xml, videos, foot...)";

                    plannedCount[systemName] = count + 1;
                    seenSourcePaths.Add(sourceKey);
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
            List<RomLinkPlanItem> creatable = result.Items.Where(i => i.CanCreate).ToList();
            CreateSelected(creatable, result.Messages);
            return result;
        }

        /// <summary>
        /// Cria junctions das pastas selecionadas (1:1 ou multi-HD aninhado) e corrige .bat.
        /// </summary>
        public int CreateSelected(IList<RomLinkPlanItem> selected, ICollection<string> log)
        {
            if (selected == null || selected.Count == 0) return 0;

            string masterRoot = FindMasterRoot();
            if (string.IsNullOrWhiteSpace(masterRoot))
            {
                if (log != null) log.Add("Pasta mestre não encontrada.");
                return 0;
            }

            string romsRoot = Path.Combine(masterRoot, SistemaFolder, RomsFolder);
            if (!Directory.Exists(romsRoot)) Directory.CreateDirectory(romsRoot);

            Dictionary<string, List<RomLinkPlanItem>> bySystem =
                new Dictionary<string, List<RomLinkPlanItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (RomLinkPlanItem item in selected)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.SystemName)) continue;
                List<RomLinkPlanItem> list;
                if (!bySystem.TryGetValue(item.SystemName, out list))
                {
                    list = new List<RomLinkPlanItem>();
                    bySystem[item.SystemName] = list;
                }
                list.Add(item);
            }

            int created = 0;
            int batFixed = 0;
            if (log != null)
            {
                log.Add("Modo ES: pasta mestre REAL + LINK de cada item de dentro do sistema.");
                log.Add("  NÃO usa junction da pasta ps5 inteira (ES não lista jogos assim).");
                log.Add("  Ex.: F:\\TurboRoms\\roms\\ps5\\jogo.bat  →  sistema\\roms\\ps5\\jogo.bat");
                log.Add("       F:\\TurboRoms\\roms\\ps5\\videos\\   →  sistema\\roms\\ps5\\videos\\");
            }
            foreach (KeyValuePair<string, List<RomLinkPlanItem>> kv in bySystem)
            {
                created += CreateSystemSources(kv.Key, romsRoot, kv.Value, log);
                foreach (RomLinkPlanItem item in kv.Value)
                {
                    if (!string.IsNullOrWhiteSpace(item.SourcePath) && Directory.Exists(item.SourcePath))
                        batFixed += BatLaunchFixer.FixDirectory(item.SourcePath, log);
                }
            }

            if (log != null)
            {
                log.Add("Total de itens linkados (arquivos + subpastas): " + created);
                if (batFixed > 0)
                    log.Add("Total de arquivos .bat/.cmd corrigidos: " + batFixed);
            }
            return created;
        }

        /// <summary>Compat: um item (CreateJunction + bat fix).</summary>
        public RomLinkPlanItem CreateJunctionAndFixBats(RomLinkPlanItem item, ICollection<string> log)
        {
            if (item == null) throw new ArgumentNullException("item");
            List<RomLinkPlanItem> one = new List<RomLinkPlanItem>();
            one.Add(item);
            CreateSelected(one, log);
            return item;
        }

        /// <summary>
        /// EmulationStation NÃO identifica jogos se sistema\roms\ps5 for um junction da pasta.
        /// Cria pasta REAL e linka CADA arquivo e subpasta que estão DENTRO da origem:
        ///   F:\TurboRoms\roms\ps5\*.bat  →  mestre\sistema\roms\ps5\*.bat
        ///   F:\TurboRoms\roms\ps5\videos →  mestre\sistema\roms\ps5\videos
        /// </summary>
        private int CreateSystemSources(string systemName, string romsRoot, List<RomLinkPlanItem> sources, ICollection<string> log)
        {
            if (sources == null || sources.Count == 0) return 0;
            string systemPath = Path.Combine(romsRoot, systemName);
            int created = 0;

            // Se a mestre ainda tem junction da pasta inteira (modo antigo), remove e cria pasta real
            if (Directory.Exists(systemPath) && JunctionService.IsReparsePoint(systemPath))
            {
                string oldTarget = JunctionService.ResolveFinalPath(systemPath);
                try
                {
                    Directory.Delete(systemPath);
                    if (log != null)
                        log.Add(systemName + ": removeu junction da pasta inteira (ES não via jogos).");
                }
                catch (Exception ex)
                {
                    if (log != null) log.Add("ERRO ao remover junction antigo de " + systemName + ": " + ex.Message);
                    return 0;
                }
                Directory.CreateDirectory(systemPath);
                // Se o junction antigo apontava para um HD que não está na seleção, espelha esse também
                if (!string.IsNullOrWhiteSpace(oldTarget) && Directory.Exists(oldTarget))
                {
                    bool inSelection = false;
                    foreach (RomLinkPlanItem s in sources)
                    {
                        if (s != null && PathsEqual(s.SourcePath, oldTarget)) { inSelection = true; break; }
                    }
                    if (!inSelection)
                    {
                        if (log != null) log.Add(systemName + ": re-espelhando origem antiga " + oldTarget);
                        created += JunctionService.MirrorFolderContents(oldTarget, systemPath, log);
                    }
                }
            }

            if (!Directory.Exists(systemPath))
                Directory.CreateDirectory(systemPath);

            if (JunctionService.IsReparsePoint(systemPath))
            {
                if (log != null) log.Add("ERRO " + systemName + ": destino ainda é link; aborte.");
                return created;
            }

            if (log != null)
                log.Add("--- " + systemName + " (pasta real: " + systemPath + ") ---");

            foreach (RomLinkPlanItem src in sources)
            {
                if (src == null || string.IsNullOrWhiteSpace(src.SourcePath) || !Directory.Exists(src.SourcePath))
                {
                    if (log != null) log.Add("ERRO origem inválida.");
                    continue;
                }

                src.LinkPath = systemPath;
                if (log != null)
                    log.Add("Origem: " + src.SourcePath + "  →  itens DENTRO de " + systemPath);

                int n = JunctionService.MirrorFolderContents(src.SourcePath, systemPath, log);
                created += n;
                src.Success = n > 0 || LooksAlreadyMirrored(systemPath, src.SourcePath);
                src.Message = n > 0
                    ? (n + " item(ns) linkados em " + systemPath)
                    : (src.Success ? "Já estava linkado." : "Nenhum item novo.");

                if (log != null)
                    log.Add(systemName + " [" + GetDriveLetter(src.SourcePath) + "]: " + n + " link(s) criados.");
            }

            return created;
        }

        /// <summary>Já existe pelo menos um item no destino que aponta para a origem.</summary>
        private static bool LooksAlreadyMirrored(string destRoot, string sourceRoot)
        {
            if (!Directory.Exists(destRoot) || !Directory.Exists(sourceRoot)) return false;
            try
            {
                string sourceFull = Path.GetFullPath(sourceRoot).TrimEnd('\\');
                foreach (string name in Directory.GetFileSystemEntries(sourceRoot))
                {
                    string leaf = Path.GetFileName(name);
                    if (string.IsNullOrEmpty(leaf)) continue;

                    string dest = Path.Combine(destRoot, leaf);
                    if (!Directory.Exists(dest) && !File.Exists(dest))
                    {
                        string drive = "";
                        if (sourceRoot.Length >= 2 && sourceRoot[1] == ':')
                            drive = char.ToUpperInvariant(sourceRoot[0]) + "_";
                        dest = Path.Combine(destRoot, drive + leaf);
                    }
                    if (!Directory.Exists(dest) && !File.Exists(dest)) continue;

                    string resolved = JunctionService.ResolveFinalPath(dest);
                    if (string.IsNullOrWhiteSpace(resolved)) continue;
                    if (resolved.StartsWith(sourceFull, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static string GetDriveLetter(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length < 2 || path[1] != ':') return "?";
            return char.ToUpperInvariant(path[0]) + ":";
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

        /// <summary>
        /// Encontra pastas de ROMs em CADA HD (A-Z), independentemente:
        /// - X:\TurboRoms\roms\&lt;sistema&gt;  (preferido)
        /// - X:\TurboRoms\&lt;sistema&gt;       (sem subpasta roms)
        /// - X:\roms\&lt;sistema&gt;             (sem pasta TurboRoms)
        /// Nao inclui a pasta mestre sistema\roms.
        /// </summary>
        private static IEnumerable<string> FindRomSourceRoots(
            IEnumerable<string> driveRoots,
            ICollection<string> validSystems,
            ICollection<string> messages)
        {
            SortedSet<string> result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> warnedEmpty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Action<string, string> tryAddIfHasSystems = delegate(string candidate, string label)
            {
                if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
                    return;

                string display = NormalizeForDisplay(candidate);
                if (result.Contains(display))
                    return;

                if (LooksLikeRomSystemsFolder(candidate, validSystems))
                {
                    result.Add(display);
                    return;
                }

                // Pasta existe mas sem sistemas do catalogo — avisa (ex.: G:\TurboRoms\roms vazia)
                if (messages != null && !warnedEmpty.Contains(display))
                {
                    warnedEmpty.Add(display);
                    int n = 0;
                    try { n = Directory.GetDirectories(candidate).Length; }
                    catch { }
                    messages.Add("AVISO: " + label + " existe em " + display
                        + " mas NAO tem pastas de sistema do catálogo (subpastas=" + n
                        + "). Esperado: " + display + "\\ps4 , \\snes , etc.");
                }
            };

            // Todas as letras A-Z (nao depende de DriveInfo.IsReady falhar em USB)
            SortedSet<string> allRoots = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (driveRoots != null)
            {
                foreach (string r in driveRoots)
                {
                    if (!string.IsNullOrWhiteSpace(r))
                        allRoots.Add(EnsureTrailingSlash(r));
                }
            }
            for (char letter = 'A'; letter <= 'Z'; letter++)
            {
                string root = letter + @":\";
                try
                {
                    if (Directory.Exists(root))
                        allRoots.Add(root);
                }
                catch
                {
                }
            }

            foreach (string root in allRoots)
            {
                string turbo = Path.Combine(root, ExtraRomsFolder);
                string turboRoms = Path.Combine(turbo, RomsFolder);
                string plainRoms = Path.Combine(root, RomsFolder);

                // 1) X:\TurboRoms\roms  (estrutura oficial)
                tryAddIfHasSystems(turboRoms, "TurboRoms\\roms");

                // 2) X:\TurboRoms  com sistemas direto (sem pasta roms)
                //    so se NAO usamos turboRoms (evita listar pai e filho juntos)
                if (!result.Contains(NormalizeForDisplay(turboRoms)))
                    tryAddIfHasSystems(turbo, "TurboRoms");

                // 3) X:\roms  sem TurboRoms
                tryAddIfHasSystems(plainRoms, "roms");
            }

            return result;
        }

        private static bool LooksLikeRomSystemsFolder(string romsPath, ICollection<string> validSystems)
        {
            if (validSystems == null || validSystems.Count == 0) return false;
            try
            {
                foreach (string dir in Directory.GetDirectories(romsPath))
                {
                    string name = Path.GetFileName(dir);
                    // ignora pastas auxiliares
                    if (name.Equals("media", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("images", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("videos", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("roms", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (string sys in validSystems)
                    {
                        if (string.Equals(sys, name, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        /// <summary>
        /// Interpreta escolha do FolderBrowser:
        /// - X:\  → tenta TurboRoms\roms, TurboRoms, roms
        /// - X:\TurboRoms  → turbo + turbo\roms
        /// - X:\TurboRoms\roms  → usa direto
        /// - X:\...\ps4  (pasta de um sistema) → usa o pai (roms)
        /// </summary>
        private static IEnumerable<string> ExpandManualRomRoot(
            string path,
            ICollection<string> validSystems,
            ICollection<string> messages)
        {
            List<string> found = new List<string>();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                if (messages != null) messages.Add("PROCURAR: pasta invalida — " + path);
                return found;
            }

            string full;
            try { full = Path.GetFullPath(path); }
            catch { full = path; }

            Action<string> add = delegate(string p)
            {
                if (string.IsNullOrWhiteSpace(p) || !Directory.Exists(p)) return;
                string n = NormalizeForDisplay(p);
                bool has = false;
                foreach (string x in found)
                {
                    if (string.Equals(x, n, StringComparison.OrdinalIgnoreCase)) { has = true; break; }
                }
                if (!has) found.Add(n);
            };

            // Se o usuario apontou a pasta de um sistema (nome no catalogo), sobe 1 nivel
            string folderName = Path.GetFileName(full.TrimEnd('\\', '/'));
            bool isSystemFolder = false;
            if (validSystems != null)
            {
                foreach (string sys in validSystems)
                {
                    if (string.Equals(sys, folderName, StringComparison.OrdinalIgnoreCase))
                    {
                        isSystemFolder = true;
                        break;
                    }
                }
            }
            if (isSystemFolder)
            {
                string parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    add(parent);
                    if (messages != null)
                        messages.Add("PROCURAR: pasta de sistema \"" + folderName + "\" — usando pai: " + parent);
                    return found;
                }
            }

            // Unidade raiz C:\
            string root = Path.GetPathRoot(full);
            if (!string.IsNullOrWhiteSpace(root)
                && string.Equals(NormalizeForDisplay(full), NormalizeForDisplay(root), StringComparison.OrdinalIgnoreCase))
            {
                add(Path.Combine(root, ExtraRomsFolder, RomsFolder));
                add(Path.Combine(root, ExtraRomsFolder));
                add(Path.Combine(root, RomsFolder));
                // filtra so as que tem sistemas
                List<string> filtered = new List<string>();
                foreach (string c in found)
                {
                    if (LooksLikeRomSystemsFolder(c, validSystems))
                        filtered.Add(c);
                    else if (messages != null)
                        messages.Add("PROCURAR: " + c + " sem pastas de sistema (ignorada).");
                }
                if (filtered.Count == 0 && messages != null)
                    messages.Add("PROCURAR: na unidade " + root + " nao achei TurboRoms\\roms com sistemas.");
                return filtered;
            }

            // TurboRoms (sem roms)
            if (string.Equals(folderName, ExtraRomsFolder, StringComparison.OrdinalIgnoreCase))
            {
                string nested = Path.Combine(full, RomsFolder);
                if (Directory.Exists(nested) && LooksLikeRomSystemsFolder(nested, validSystems))
                    add(nested);
                else if (LooksLikeRomSystemsFolder(full, validSystems))
                    add(full);
                else if (messages != null)
                    messages.Add("PROCURAR: " + full + " nao tem subpastas de sistema do catalogo.");
                return found;
            }

            // Pasta roms ou qualquer pasta com sistemas
            if (LooksLikeRomSystemsFolder(full, validSystems))
            {
                add(full);
                return found;
            }

            // Ultima tentativa: full\roms e full\TurboRoms\roms
            add(Path.Combine(full, RomsFolder));
            add(Path.Combine(full, ExtraRomsFolder, RomsFolder));
            List<string> ok = new List<string>();
            foreach (string c in found)
            {
                if (LooksLikeRomSystemsFolder(c, validSystems))
                    ok.Add(c);
            }
            if (ok.Count == 0 && messages != null)
                messages.Add("PROCURAR: nenhuma pasta de sistemas valida em " + full);
            return ok;
        }

        private static IEnumerable<string> GetCandidateDriveRoots()
        {
            SortedSet<string> roots = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            // Bitmask do Windows: inclui USB mesmo quando DriveInfo.IsReady falha de forma intermitente
            try
            {
                uint mask = GetLogicalDrives();
                for (int i = 0; i < 26; i++)
                {
                    if ((mask & (1u << i)) == 0) continue;
                    char letter = (char)('A' + i);
                    string root = letter + @":\";
                    try
                    {
                        uint type = GetDriveType(root);
                        // 2=Removable, 3=Fixed, 4=Remote, 5=CD, 6=RAM
                        if (type == 5 /* CD */) continue;
                        if (Directory.Exists(root))
                            roots.Add(root);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            foreach (DriveInfo drive in DriveInfo.GetDrives().OrderBy(d => d.Name))
            {
                try
                {
                    // Nao exigir IsReady primeiro (USB lento); tenta Exists
                    if (drive.DriveType == DriveType.CDRom || drive.DriveType == DriveType.NoRootDirectory)
                        continue;
                    string root = EnsureTrailingSlash(drive.RootDirectory.FullName);
                    if (Directory.Exists(root))
                        roots.Add(root);
                }
                catch
                {
                }
            }

            for (char letter = 'A'; letter <= 'Z'; letter++)
            {
                string root = letter + @":\";
                try
                {
                    if (Directory.Exists(root)) roots.Add(root);
                }
                catch
                {
                }
            }

            return OrderRootsWithExecutableFirst(roots.ToList());
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetLogicalDrives();

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern uint GetDriveType(string lpRootPathName);

        private static IEnumerable<string> GetMasterSearchDriveRoots()
        {
            // Mesmas unidades do scan TurboRoms (todas as prontas).
            return GetCandidateDriveRoots();
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
            // Qualquer unidade local com letra (C:\) e candidata a mestre.
            if (string.IsNullOrWhiteSpace(root)) return false;
            string normalized = EnsureTrailingSlash(root);
            return normalized.Length >= 3
                && char.IsLetter(normalized[0])
                && normalized[1] == ':'
                && (normalized[2] == '\\' || normalized[2] == '/');
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
