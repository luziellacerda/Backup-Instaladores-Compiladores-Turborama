using TurboRama.Configuration;
using TurboRama.Core.Baseline;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Diagnostics;
using TurboRama.Installation;
using TurboRama.Installation.Steps;
using TurboRama.Rollback;
using TurboRama.Windows.Baseline;
using TurboRama.Windows.Optional;
using TurboRama.Windows.Recovery;

namespace TurboRama.UI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        string mode = args.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "--ui";
        // EXE renomeado TurboRama.Setup.exe sem argumentos = instalação completa de fábrica
        if ((args.Length == 0 || mode is "--ui") &&
            string.Equals(
                Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? AppContext.BaseDirectory),
                "TurboRama.Setup",
                StringComparison.OrdinalIgnoreCase))
        {
            mode = "--install-full";
        }

        bool quiet = args.Any(a => string.Equals(a, "--quiet", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(a, "-q", StringComparison.OrdinalIgnoreCase));
        string resultFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "TurboRama",
            "last-phase-result.txt");
        try
        {
            string? rf = args.SkipWhile(a => !string.Equals(a, "--result", StringComparison.OrdinalIgnoreCase))
                .Skip(1).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(rf))
            {
                resultFile = rf;
            }
        }
        catch
        {
            /* keep default */
        }

        try
        {
            ProductPaths.EnsureLayout();
        }
        catch (Exception ex)
        {
            WriteCliResult(resultFile, false, "Não foi possível preparar C:\\TurboRama: " + ex.Message);
            if (!quiet)
            {
                MessageBox.Show(
                    "Não foi possível preparar C:\\TurboRama:\n" + ex.Message +
                    "\n\nExecute como Administrador.",
                    "TurboRama",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            Environment.ExitCode = 1;
            return;
        }

        var logger = new FileTurboRamaLogger(ProductPaths.InstallerLogs, "installer");
        logger.Info("UI", "TurboRama.UI iniciado. mode=" + mode + " quiet=" + quiet);

        ProductConfiguration config;
        OperationResult loadCfg = ConfigurationStore.Load(out config);
        logger.Info("UI", loadCfg.Message);
        if (config.InstallationId == Guid.Empty)
        {
            config.InstallationId = Guid.NewGuid();
        }

        if (mode is "--preflight")
        {
            PreflightReport pf = RunPreflight(config, logger, showUi: !quiet);
            WriteCliResult(resultFile, pf.Success, pf.ToOperationResult().Message);
            Environment.ExitCode = pf.Success ? 0 : 1;
            return;
        }

        // Teste seguro de backup/rollback (não mexe em kiosk/shell/serviços)
        if (mode is "--test-backup" or "--test-rollback")
        {
            OperationResult r = RunBackupErrorCaseTestAsync(config, logger).GetAwaiter().GetResult();
            WriteCliResult(resultFile, r.Success, r.Message);
            logger.Info("UI", "TestBackup: " + r.Message);
            if (!quiet)
            {
                MessageBox.Show(
                    r.Message,
                    r.Success ? "Backup/Rollback OK" : "Backup/Rollback FALHOU",
                    MessageBoxButtons.OK,
                    r.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            }

            Environment.ExitCode = r.Success ? 0 : 1;
            return;
        }

        if (mode is "--validate" or "--phase6" or "--accept-factory")
        {
            bool clearLocks = args.Any(a =>
                string.Equals(a, "--clear-locks", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--clear-lock", StringComparison.OrdinalIgnoreCase));
            var validator = new PostInstallValidationService();
            OperationResult r = validator.RunToResult(config, clearLocks);
            WriteCliResult(resultFile, r.Success, r.Message);
            logger.Info("UI", "Phase6: " + r.Message);
            if (!quiet)
            {
                MessageBox.Show(
                    r.Message,
                    r.Success ? "Fase 6 — Aceite OK" : "Fase 6 — Aceite FALHOU",
                    MessageBoxButtons.OK,
                    r.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }

            Environment.ExitCode = r.Success ? 0 : 1;
            return;
        }

        // Instalador completo de fábrica (outro PC): seed + Fase2 + Fase3 + aceite
        if (mode is "--install-full" or "--factory-install" or "--setup")
        {
            OperationResult r = RunFactoryFullInstallAsync(config, logger).GetAwaiter().GetResult();
            WriteCliResult(resultFile, r.Success, r.Message);
            logger.Info("UI", "FactoryFull: " + r.Message);
            if (!quiet)
            {
                MessageBox.Show(
                    r.Message + (r.Success
                        ? "\n\nReinicie o PC para autologon Arcade + Launcher.\nSenha kiosk (se pedir): veja FactoryDefaults / documentação."
                        : ""),
                    r.Success ? "Instalação de fábrica concluída" : "Instalação de fábrica FALHOU",
                    MessageBoxButtons.OK,
                    r.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            }

            Environment.ExitCode = r.Success ? 0 : 1;
            return;
        }

        if (mode is "--phase2" or "--install-phase2")
        {
            OperationResult r = RunPhase2Async(config, logger, force: true).GetAwaiter().GetResult();
            WriteCliResult(resultFile, r.Success, r.Message);
            if (!quiet)
            {
                MessageBox.Show(r.Message, r.Success ? "Fase 2 OK" : "Fase 2 falhou",
                    MessageBoxButtons.OK, r.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            }

            Environment.ExitCode = r.Success ? 0 : 1;
            return;
        }

        if (mode is "--phase3" or "--install-phase3")
        {
            OperationResult r = RunPhase3Async(config, logger, force: true).GetAwaiter().GetResult();
            WriteCliResult(resultFile, r.Success, r.Message);
            if (!quiet)
            {
                MessageBox.Show(r.Message, r.Success ? "Fase 3 OK" : "Fase 3 falhou",
                    MessageBoxButtons.OK, r.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            }

            Environment.ExitCode = r.Success ? 0 : 1;
            return;
        }

        if (mode is "--rollback-phase2")
        {
            OperationResult r = RunPhase2RollbackAsync(config, logger).GetAwaiter().GetResult();
            WriteCliResult(resultFile, r.Success, r.Message);
            if (!quiet)
            {
                MessageBox.Show(r.Message, r.Success ? "Rollback OK" : "Rollback falhou",
                    MessageBoxButtons.OK, r.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            }

            Environment.ExitCode = r.Success ? 0 : 1;
            return;
        }

        if (mode is "--rollback-phase3")
        {
            OperationResult r = RunPhase3RollbackAsync(config, logger).GetAwaiter().GetResult();
            WriteCliResult(resultFile, r.Success, r.Message);
            if (!quiet)
            {
                MessageBox.Show(r.Message, r.Success ? "Rollback OK" : "Rollback falhou",
                    MessageBoxButtons.OK, r.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            }

            Environment.ExitCode = r.Success ? 0 : 1;
            return;
        }

        Application.Run(new MainForm(config, logger));
    }

    private static void WriteCliResult(string path, bool success, string message)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(
                path,
                (success ? "OK" : "FAIL") + Environment.NewLine +
                DateTimeOffset.Now.ToString("O") + Environment.NewLine +
                message + Environment.NewLine);
        }
        catch
        {
            /* ignore */
        }
    }

    internal static PreflightReport RunPreflight(ProductConfiguration config, ITurboRamaLogger logger, bool showUi)
    {
        var service = new PreflightService();
        PreflightReport report = service.Run(config);
        foreach (PreflightItem item in report.Items)
        {
            if (item.Severity == "ERRO")
                logger.Error("Preflight", item.Message, errorCode: item.Code);
            else if (item.Severity == "AVISO")
                logger.Warning("Preflight", item.Message);
            else
                logger.Info("Preflight", item.Message);
        }

        if (showUi)
        {
            string text = string.Join(Environment.NewLine, report.Items.Select(i => i.Severity + ": " + i.Message));
            MessageBox.Show(text, report.Success ? "Preflight OK" : "Preflight com erros",
                MessageBoxButtons.OK, report.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        return report;
    }

    internal static InstallationContext CreateContext(ProductConfiguration config) =>
        new()
        {
            InstallationId = config.InstallationId,
            ProductVersion = config.ProductVersion,
            InstallDirectory = config.InstallDirectory,
            StateDirectory = ProductPaths.State,
            BackupDirectory = ProductPaths.Backup,
            LogsDirectory = ProductPaths.InstallerLogs,
            KioskUserName = string.IsNullOrWhiteSpace(config.KioskUser)
                ? FactoryDefaults.KioskUserName
                : config.KioskUser,
            // Senha de fábrica embutida no programa (Lz2026@$) ou override em memória
            KioskPassword = FactoryDefaults.ResolveKioskPassword(config),
            FrontendExecutable = config.FrontendExecutable,
            Profile = Enum.TryParse(config.Profile, true, out InstallationProfile profile)
                ? profile
                : InstallationProfile.KioskBasic
        };

    internal static List<IInstallationStep> BuildPhase1Steps(bool includeProbe) =>
        BuildSteps(includeBaseline: true, includeProbe: includeProbe, includeKiosk: false);

    internal static List<IInstallationStep> BuildPhase2Steps() =>
        BuildSteps(includeBaseline: true, includeProbe: false, includeKiosk: true, includeServices: false);

    internal static List<IInstallationStep> BuildPhase3Steps() =>
        BuildSteps(includeBaseline: true, includeProbe: false, includeKiosk: false, includeServices: true);

    internal static List<IInstallationStep> BuildPhase4Steps() =>
        BuildSteps(
            includeBaseline: true,
            includeProbe: false,
            includeKiosk: false,
            includeServices: false,
            includeOptionalModules: true);

    internal static List<IInstallationStep> BuildSteps(
        bool includeBaseline,
        bool includeProbe,
        bool includeKiosk,
        bool includeServices = false,
        bool includeOptionalModules = false)
    {
        var steps = new List<IInstallationStep> { new EnsureDirectoryLayoutStep() };
        if (includeBaseline)
        {
            steps.Add(new CaptureWindowsBaselineStep());
        }

        if (includeProbe)
        {
            steps.Add(new Phase1ProbeStep());
        }

        if (includeKiosk)
        {
            steps.Add(new DeployLauncherStep());
            steps.Add(new CreateKioskAccountStep());
            steps.Add(new ConfigureUserShellStep());
            steps.Add(new ConfigureAutologonStep());
            steps.Add(new ApplyKioskPoliciesStep());
        }

        if (includeServices)
        {
            steps.Add(new DeployServicesBinariesStep());
            steps.Add(new InstallWindowsServicesStep());
        }

        if (includeOptionalModules)
        {
            steps.Add(new OptionalAdvancedModulesStep());
        }

        return steps.OrderBy(s => s.Order).ToList();
    }

    internal static async Task<OperationResult> RunLayoutInstallAsync(ProductConfiguration config, ITurboRamaLogger logger) =>
        await RunPipelineAsync(config, logger, BuildSteps(false, false, false), force: false).ConfigureAwait(false);

    internal static async Task<OperationResult> RunBaselineOnlyAsync(ProductConfiguration config, ITurboRamaLogger logger) =>
        await RunPipelineAsync(config, logger, BuildSteps(true, false, false), force: false).ConfigureAwait(false);

    internal static async Task<OperationResult> RunPhase1Async(ProductConfiguration config, ITurboRamaLogger logger, bool includeProbe = true) =>
        await RunPipelineAsync(config, logger, BuildPhase1Steps(includeProbe), force: false).ConfigureAwait(false);

    /// <summary>
    /// Instalação prática em PC novo: copia pack → preflight → baseline/kiosk → serviços → aceite.
    /// </summary>
    internal static async Task<OperationResult> RunFactoryFullInstallAsync(
        ProductConfiguration config,
        ITurboRamaLogger logger)
    {
        var stepsLog = new List<string>();
        void L(string m)
        {
            stepsLog.Add(m);
            logger.Info("FactoryFull", m);
        }

        // Evita duas instalações simultâneas (corrupção de state/serviços)
        using var installMutex = new Mutex(true, @"Global\TurboRamaFactoryFullInstall", out bool createdNew);
        if (!createdNew)
        {
            return OperationResult.Fail(
                "Outra instalação TurboRama já está em andamento. Aguarde ou reinicie e tente de novo.",
                "FULL_MUTEX",
                "FactoryFull");
        }

        try
        {
            string? pack = FactoryFullInstall.FindPackRoot();
            if (pack is null)
            {
                return OperationResult.Fail(
                    "Pacote de fábrica não encontrado ao lado do instalador. " +
                    "Use a pasta TurboRama-Factory-Pack completa (App\\ + Installer\\).",
                    "FULL_NO_PACK",
                    "FactoryFull");
            }

            L("Pack root: " + pack);

            // .NET 8 Desktop Runtime — falha dura (sem runtime o kiosk não sobe)
            if (!HasDotNetDesktopRuntime8())
            {
                return OperationResult.Fail(
                    "Microsoft .NET 8 Desktop Runtime (x64) não encontrado. " +
                    "Instale: https://dotnet.microsoft.com/download/dotnet/8.0 e rode de novo.",
                    "FULL_NO_DOTNET8",
                    "FactoryFull");
            }

            L("Runtime .NET 8 Desktop: OK");

            OperationResult seed = FactoryFullInstall.SeedPackToMachine(pack, logger);
            L("Seed: " + (seed.Success ? "OK" : "FAIL") + " — " + seed.Message);
            if (!seed.Success)
            {
                return seed;
            }

            // Garante InstallationId estável
            if (config.InstallationId == Guid.Empty)
            {
                if (BaselineStore.TryGetLatestInstallationId(out Guid latest))
                {
                    config.InstallationId = latest;
                }
                else
                {
                    config.InstallationId = Guid.NewGuid();
                }
            }

            ConfigurationStore.Save(config);

            PreflightReport pf = RunPreflight(config, logger, showUi: false);
            L("Preflight: " + (pf.Success ? "OK" : "FAIL") + " (" + pf.Items.Count + " checks)");
            if (!pf.Success)
            {
                return OperationResult.Fail(
                    "Preflight falhou — corrija e rode de novo como Admin.\n" +
                    string.Join("\n", pf.Errors.Select(e => "• " + e.Message)),
                    "FULL_PF",
                    "FactoryFull");
            }

            // Restore point best-effort
            try
            {
                OperationResult rp = SystemRestoreHelper.TryCreateRestorePoint("TurboRama Factory Full Install");
                L("RestorePoint: " + rp.Message);
            }
            catch
            {
                /* ignore */
            }

            L("Fase 2 Kiosk (conta/shell/autologon/políticas)...");
            OperationResult p2 = await RunPhase2Async(config, logger, force: true).ConfigureAwait(false);
            L("Fase2: " + (p2.Success ? "OK" : "FAIL") + " — " + p2.Message);
            if (!p2.Success)
            {
                return OperationResult.Fail(
                    "Fase 2 falhou: " + p2.Message + "\n" + string.Join(" | ", stepsLog),
                    "FULL_P2",
                    "FactoryFull");
            }

            L("Fase 3 Serviços (Watchdog + Maintenance)...");
            OperationResult p3 = await RunPhase3Async(config, logger, force: true).ConfigureAwait(false);
            L("Fase3: " + (p3.Success ? "OK" : "FAIL") + " — " + p3.Message);
            if (!p3.Success)
            {
                return OperationResult.Fail(
                    "Fase 3 falhou: " + p3.Message + "\n" + string.Join(" | ", stepsLog),
                    "FULL_P3",
                    "FactoryFull");
            }

            // Fase 4 + lockdown de produção (igual a este Windows de referência).
            // Keyboard Filter / DeviceLockdown / SecurityAgent / políticas CAD.
            // TurboRama (jogos) NÃO entra aqui — copiar D:\Turborama depois.
            L("Segurança Windows produção (Keyboard Filter + Agent + políticas)...");
            config.EnableKeyboardFilter = true;
            config.EnableUwf = false;
            config.EnableBootBranding = false;
            // Frontend padrão = pasta de jogos copiada depois (não bloqueia kiosk)
            if (string.IsNullOrWhiteSpace(config.FrontendExecutable) ||
                config.FrontendExecutable.IndexOf("Frontend.exe", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                config.FrontendExecutable = @"D:\Turborama\TurboRama.exe";
            }

            ConfigurationStore.Save(config);

            OperationResult p4 = await RunPhase4Async(
                config,
                logger,
                enableUwf: false,
                enableKeyboardFilter: true,
                enableBootBranding: false,
                force: true).ConfigureAwait(false);
            L("Fase4 KbFilter: " + (p4.Success ? "OK" : "WARN") + " — " + p4.Message);

            OperationResult sec = ProductionKioskSecurityService.Apply();
            L("SecurityProd: " + (sec.Success ? "OK" : "WARN") + " — " + sec.Message);
            // Segurança é best-effort se a edição Windows não tiver IoT;
            // kiosk básico (F2+F3) já está instalado.

            L("Fase 6 Aceite...");
            OperationResult p6 = new PostInstallValidationService().RunToResult(config, clearLocks: true);
            L("Fase6: " + (p6.Success ? "OK" : "FAIL") + " — " + p6.Message);
            if (!p6.Success)
            {
                return OperationResult.Fail(
                    "Instalação feita, mas aceite falhou: " + p6.Message +
                    "\nRevise Status e logs. Passos: " + string.Join(" | ", stepsLog),
                    "FULL_P6",
                    "FactoryFull");
            }

            string summary =
                "INSTALAÇÃO DE FÁBRICA CONCLUÍDA (Windows = kiosk + segurança de produção).\n\n" +
                "• Pack: " + pack + "\n" +
                "• Kiosk: " + FactoryDefaults.KioskUserName + "\n" +
                "• Senha kiosk (se login manual): " + FactoryDefaults.KioskPassword + "\n" +
                "• Serviços: Watchdog + Maintenance\n" +
                "• Segurança: Keyboard Filter + SecurityAgent + políticas (como PC referência)\n" +
                "• Frontend esperado (copiar depois): " + config.FrontendExecutable + "\n" +
                "• Aceite: OK\n\n" +
                "1) REINICIE o PC (autologon Arcade + filtro de teclado).\n" +
                "2) Quando o Windows estiver no kiosk, COPIE a pasta D:\\Turborama (jogos/ES).\n" +
                "   Admin continua para manutenção (Explorer).\n\n" +
                "Log: " + string.Join(" → ", stepsLog);

            return OperationResult.Ok(summary, "FactoryFull");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "Exceção instalação completa: " + ex.Message + " | " + string.Join(" | ", stepsLog),
                "FULL_EX",
                "FactoryFull",
                exception: ex);
        }
    }

    /// <summary>
    /// .NET 8 Desktop Runtime (WindowsDesktop.App 8.x) — obrigatório para Launcher/serviços.
    /// </summary>
    private static bool HasDotNetDesktopRuntime8()
    {
        try
        {
            string[] hosts =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"),
                @"C:\Program Files\dotnet\dotnet.exe",
                @"D:\tr-dotnet\dotnet.exe",
            };
            string? dotnet = hosts.FirstOrDefault(File.Exists);
            if (dotnet is null)
            {
                // Frameworks instalados sem host no PATH
                string fx = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "dotnet", "shared", "Microsoft.WindowsDesktop.App");
                if (Directory.Exists(fx) &&
                    Directory.GetDirectories(fx).Any(d => Path.GetFileName(d).StartsWith("8.", StringComparison.Ordinal)))
                {
                    return true;
                }

                return false;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = dotnet,
                Arguments = "--list-runtimes",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null)
            {
                return false;
            }

            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15_000);
            return output.Contains("Microsoft.WindowsDesktop.App 8.", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Teste seguro para caso de erro: Capture→Apply→Validate→Rollback do Phase1Probe.
    /// Não altera kiosk, autologon, shell nem serviços. Só HKLM\SOFTWARE\TurboRama\Secure\Phase1Probe.
    /// </summary>
    internal static async Task<OperationResult> RunBackupErrorCaseTestAsync(
        ProductConfiguration config,
        ITurboRamaLogger logger)
    {
        var lines = new List<string>();
        void Log(string m)
        {
            lines.Add(m);
            logger.Info("TestBackup", m);
        }

        try
        {
            // 1) Verificar backup em disco
            if (config.InstallationId == Guid.Empty &&
                BaselineStore.TryGetLatestInstallationId(out Guid latest))
            {
                config.InstallationId = latest;
            }

            string backupRoot = Path.Combine(ProductPaths.Backup, config.InstallationId.ToString("D"));
            string baselinePath = Path.Combine(backupRoot, "baseline", "baseline.json");
            if (!File.Exists(baselinePath))
            {
                return OperationResult.Fail(
                    "Baseline ausente em " + baselinePath + " — rode captura baseline antes.",
                    "TB_NO_BASE",
                    "TestBackup");
            }

            Log("OK baseline existe (" + new FileInfo(baselinePath).Length + " bytes)");

            string[] expected =
            {
                "layout-capture.json",
                "change-manifest.json",
                Path.Combine("baseline", "baseline.json"),
                Path.Combine("baseline", "bcd-backup"),
            };
            foreach (string rel in expected)
            {
                string p = Path.Combine(backupRoot, rel);
                if (File.Exists(p) || Directory.Exists(p))
                {
                    Log("OK artifact " + rel);
                }
                else
                {
                    Log("AVISO artifact ausente: " + rel);
                }
            }

            // 2) Ciclo probe (simula apply + erro → rollback)
            InstallationContext context = CreateContext(config);
            var probe = new Phase1ProbeStep();
            CancellationToken ct = CancellationToken.None;

            OperationResult cap = await probe.CaptureAsync(context, ct).ConfigureAwait(false);
            Log("Capture: " + (cap.Success ? "OK" : "FAIL") + " — " + cap.Message);
            if (!cap.Success)
            {
                return OperationResult.Fail("Capture falhou: " + cap.Message, "TB_CAP", "TestBackup");
            }

            string snapPath = context.Properties["ProbeSnapshot"];
            if (!File.Exists(snapPath))
            {
                return OperationResult.Fail("Snapshot de backup não gravado: " + snapPath, "TB_SNAP", "TestBackup");
            }

            Log("OK snapshot gravado: " + snapPath);

            OperationResult app = await probe.ApplyAsync(context, ct).ConfigureAwait(false);
            Log("Apply (simula mudança): " + (app.Success ? "OK" : "FAIL") + " — " + app.Message);
            if (!app.Success)
            {
                return OperationResult.Fail("Apply falhou: " + app.Message, "TB_APP", "TestBackup");
            }

            OperationResult val = await probe.ValidateAsync(context, ct).ConfigureAwait(false);
            Log("Validate (mudança presente): " + (val.Success ? "OK" : "FAIL") + " — " + val.Message);
            if (!val.Success)
            {
                await probe.RollbackAsync(context, ct).ConfigureAwait(false);
                return OperationResult.Fail("Validate falhou: " + val.Message, "TB_VAL", "TestBackup");
            }

            // 3) Simula erro → rollback do backup
            Log("Simulando erro: executando Rollback a partir do backup capturado...");
            OperationResult rb = await probe.RollbackAsync(context, ct).ConfigureAwait(false);
            Log("Rollback: " + (rb.Success ? "OK" : "FAIL") + " — " + rb.Message);
            if (!rb.Success)
            {
                return OperationResult.Fail("Rollback falhou: " + rb.Message, "TB_RB", "TestBackup");
            }

            // 4) Confirma que o valor voltou (não deve ser o probe value se não existia)
            var after = Windows.Registry.RegistryValueHelper.Capture(
                Microsoft.Win32.RegistryHive.LocalMachine,
                Phase1ProbeStep.SubKey,
                Phase1ProbeStep.ValueName,
                Microsoft.Win32.RegistryView.Registry64);

            bool okAfter =
                (!after.Existed) ||
                !string.Equals(after.Value, Phase1ProbeStep.ProbeValue, StringComparison.Ordinal);

            if (!okAfter)
            {
                Log("ERRO: após rollback o probe ainda está ativo: " + after.Value);
                return OperationResult.Fail(
                    "Após rollback o valor de teste ainda existe — backup não restaurou.",
                    "TB_RB_VAL",
                    "TestBackup");
            }

            Log("OK pós-rollback: estado original restaurado (existia=" + after.Existed + ")");

            // 5) Atomic previous folder (se existir)
            string prev = Path.Combine(ProductPaths.App, "previous");
            if (Directory.Exists(prev))
            {
                Log("OK pasta App\\previous presente (deploy atômico)");
            }
            else
            {
                Log("INFO App\\previous ainda não criada (só após re-deploy de App)");
            }

            string summary = "TESTE BACKUP/ERRO OK. " + string.Join(" | ", lines);
            return OperationResult.Ok(summary, "TestBackup");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "Exceção no teste de backup: " + ex.Message + " | " + string.Join(" | ", lines),
                "TB_EX",
                "TestBackup",
                exception: ex);
        }
    }

    internal static async Task<OperationResult> RunPhase2Async(ProductConfiguration config, ITurboRamaLogger logger, bool force = true)
    {
        // Garante baseline se faltar
        if (!File.Exists(BaselineStore.GetDocumentPath(config.InstallationId)))
        {
            if (BaselineStore.TryGetLatestInstallationId(out Guid latest) &&
                File.Exists(BaselineStore.GetDocumentPath(latest)))
            {
                config.InstallationId = latest;
            }
            else
            {
                OperationResult baseResult = await RunBaselineOnlyAsync(config, logger).ConfigureAwait(false);
                if (!baseResult.Success)
                {
                    return baseResult;
                }
            }
        }

        return await RunPipelineAsync(config, logger, BuildPhase2Steps(), force).ConfigureAwait(false);
    }

    internal static async Task<OperationResult> RunPhase3Async(ProductConfiguration config, ITurboRamaLogger logger, bool force = true)
    {
        return await RunPipelineAsync(config, logger, BuildPhase3Steps(), force).ConfigureAwait(false);
    }

    internal static async Task<OperationResult> RunPhase4Async(
        ProductConfiguration config,
        ITurboRamaLogger logger,
        bool enableUwf,
        bool enableKeyboardFilter,
        bool enableBootBranding,
        bool force = true)
    {
        var extras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EnableUwf"] = enableUwf ? "1" : "0",
            ["EnableKeyboardFilter"] = enableKeyboardFilter ? "1" : "0",
            ["EnableBootBranding"] = enableBootBranding ? "1" : "0",
        };

        return await RunPipelineAsync(config, logger, BuildPhase4Steps(), force, extras).ConfigureAwait(false);
    }

    internal static async Task<OperationResult> RunPhase4RollbackAsync(ProductConfiguration config, ITurboRamaLogger logger)
    {
        InstallationContext context = CreateContext(config);
        InstallationStateStore.Load(out InstallationState state);
        if (state.InstallationId == Guid.Empty)
        {
            state.InstallationId = config.InstallationId;
        }

        var optOnly = BuildPhase4Steps().Where(s => s.Order >= 100).ToList();
        var rollback = new RollbackService(optOnly, logger);
        return await rollback.RollbackAllAsync(context, state).ConfigureAwait(false);
    }

    internal static async Task<OperationResult> RunPhase1RollbackAsync(ProductConfiguration config, ITurboRamaLogger logger)
    {
        InstallationContext context = CreateContext(config);
        InstallationStateStore.Load(out InstallationState state);
        if (state.InstallationId == Guid.Empty)
        {
            state.InstallationId = config.InstallationId;
        }

        var rollback = new RollbackService(BuildPhase1Steps(includeProbe: true), logger);
        return await rollback.RollbackAllAsync(context, state).ConfigureAwait(false);
    }

    internal static async Task<OperationResult> RunPhase2RollbackAsync(ProductConfiguration config, ITurboRamaLogger logger)
    {
        InstallationContext context = CreateContext(config);
        InstallationStateStore.Load(out InstallationState state);
        if (state.InstallationId == Guid.Empty)
        {
            state.InstallationId = config.InstallationId;
        }

        var kioskOnly = BuildPhase2Steps()
            .Where(s => s.Order >= 35)
            .ToList();
        var rollback = new RollbackService(kioskOnly, logger);
        return await rollback.RollbackAllAsync(context, state).ConfigureAwait(false);
    }

    internal static async Task<OperationResult> RunPhase3RollbackAsync(ProductConfiguration config, ITurboRamaLogger logger)
    {
        InstallationContext context = CreateContext(config);
        InstallationStateStore.Load(out InstallationState state);
        if (state.InstallationId == Guid.Empty)
        {
            state.InstallationId = config.InstallationId;
        }

        var svcOnly = BuildPhase3Steps()
            .Where(s => s.Order >= 85)
            .ToList();
        var rollback = new RollbackService(svcOnly, logger);
        return await rollback.RollbackAllAsync(context, state).ConfigureAwait(false);
    }

    internal static OperationResult ValidateBaseline(ProductConfiguration config, ITurboRamaLogger logger)
    {
        Guid id = config.InstallationId;
        if (BaselineStore.TryGetLatestInstallationId(out Guid latest))
        {
            id = latest;
        }

        OperationResult r = WindowsBaselineService.ValidateIntegrity(id);
        logger.Info("Baseline", r.ToString());
        return r;
    }

    private static async Task<OperationResult> RunPipelineAsync(
        ProductConfiguration config,
        ITurboRamaLogger logger,
        List<IInstallationStep> steps,
        bool force,
        IDictionary<string, string>? extraProperties = null)
    {
        PreflightReport preflight = new PreflightService().Run(config);
        if (!preflight.Success)
        {
            OperationResult fail = preflight.ToOperationResult();
            logger.Error("Installer", fail.Message, errorCode: fail.ErrorCode);
            return fail;
        }

        // Proposta §5: ponto de restauração best-effort (não bloqueia)
        try
        {
            OperationResult rp = SystemRestoreHelper.TryCreateRestorePoint(
                "TurboRama Secure " + config.ProductVersion);
            logger.Info("Installer", rp.Message);
        }
        catch
        {
            /* ignore */
        }

        ConfigurationStore.Save(config);

        InstallationState state;
        if (InstallationStateStore.Load(out InstallationState existing).Success &&
            existing.InstallationId == config.InstallationId)
        {
            state = existing;
            // Resume após queda de energia: refaz step que ficou IN_PROGRESS
            state.NormalizeAfterCrash();
            if (force)
            {
                string[] redoSteps =
                {
                    "DeployLauncher", "CreateKioskAccount", "ConfigureUserShell",
                    "ConfigureAutologon", "ApplyKioskPolicies", "Phase1Probe",
                    "DeployServicesBinaries", "InstallWindowsServices",
                    "OptionalAdvancedModules"
                };
                state.CompletedStages.RemoveAll(s =>
                    redoSteps.Contains(s, StringComparer.OrdinalIgnoreCase));
                state.FailedStage = null;
                state.LastError = null;
                state.InProgressStage = null;
            }
        }
        else
        {
            state = InstallationStateStore.CreateNew(config.InstallationId, config.Profile, config.ProductVersion);
        }

        InstallationContext context = CreateContext(config);
        if (extraProperties is not null)
        {
            foreach (var kv in extraProperties)
            {
                context.Properties[kv.Key] = kv.Value;
            }
        }

        Directory.CreateDirectory(context.InstallationBackupRoot);
        state.CurrentStage = InstallationStage.PreflightValidated;
        InstallationStateStore.Save(state);

        var engine = new InstallationEngine(steps, logger);
        OperationResult result = await engine.RunAsync(context, state).ConfigureAwait(false);
        logger.Info("Installer", result.ToString());
        return result;
    }
}
