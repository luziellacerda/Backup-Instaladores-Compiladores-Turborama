using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TurboRama.Configuration;
using TurboRama.Core.Ipc;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Core.State;

namespace TurboRama.Launcher;

/// <summary>
/// Shell do kiosk: frontend loop, menu técnico opcional, hook de teclado opcional (default OFF).
/// Não altera Registro/BCD/serviços no boot.
/// </summary>
internal static class Program
{
    private const string SingleInstanceMutexName = "Global\\TurboRama.Launcher.SingleInstance";

    /// <summary>
    /// Processos que o bootstrap TurboRama.exe (RetroBat-like) deixa rodando após sair com code 0.
    /// Enquanto estes vivem, NÃO reiniciar o frontend.
    /// </summary>
    private static readonly string[] FrontendCompanionProcesses =
    {
        "emulationstation",
        "emulatorLauncher",
        "retroarch",
    };

    /// <summary>Processos do frontend a terminar antes da splash (evita ecrã preto / sobreposição).</summary>
    private static readonly string[] FrontendKillList =
    {
        "emulationstation",
        "emulatorLauncher",
        "retroarch",
        "TurboRama",
        "Frontend",
    };

    private static ITurboRamaLogger? _logger;
    private static bool _hookEnabled;
    private static IntPtr _hook = IntPtr.Zero;
    private static LowLevelKeyboardProc? _proc;
    private static Mutex? _singleInstance;
    /// <summary>Duração da sessão do frontend (para distinguir crash ao abrir vs Desligar sem script).</summary>
    private static Stopwatch? _frontendSessionSw;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try { ProductPaths.EnsureLayout(); } catch { /* ignore */ }

        _logger = new FileTurboRamaLogger(
            Directory.Exists(ProductPaths.LauncherLogs) ? ProductPaths.LauncherLogs : Path.GetTempPath(),
            "launcher");

        string user = Environment.UserName ?? "";
        string[] a = args ?? Array.Empty<string>();
        _logger.Info("Launcher", "Início. USER=" + user + " Interactive=" + Environment.UserInteractive +
                                   " Args=" + string.Join(' ', a));

        // Preview da loading SEM reiniciar o PC / sem ser shell Arcade
        // Uso: TurboRama.Launcher.exe --test-loading
        //      TurboRama.Launcher.exe --test-loading 8   (segundos)
        if (HasArg(a, "--test-loading") || HasArg(a, "--preview-loading") || HasArg(a, "/test-loading"))
        {
            RunLoadingPreview(a);
            return;
        }

        // Preview da tela de DESLIGAR (não desliga o PC de verdade)
        // Uso: TurboRama.Launcher.exe --test-shutdown
        if (HasArg(a, "--test-shutdown") || HasArg(a, "--preview-shutdown") || HasArg(a, "/test-shutdown"))
        {
            RunShutdownPreview(a);
            return;
        }

        // Preview menu segurança (substituto CAD)
        if (HasArg(a, "--test-security") || HasArg(a, "--test-operator") || HasArg(a, "--preview-security"))
        {
            RunSecurityMenuPreview();
            return;
        }

        // Agente: Ctrl+End = menu TurboRama (Ctrl+Alt+Del desativado no instalador)
        // Corre ANTES do single-instance do shell e também em Admin.
        if (HasArg(a, "--security-agent") || HasArg(a, "/security-agent"))
        {
            _logger.Info("Launcher", "Modo --security-agent (Ctrl+End → menu TurboRama).");
            Application.Run(new SecurityAgentHost(_logger));
            return;
        }

        // Uma única instância: evita Watchdog (SYSTEM) + shell Arcade em paralelo
        if (!TryAcquireSingleInstance())
        {
            _logger.Info("Launcher", "Outra instância já ativa — saindo sem diálogo.");
            return;
        }

        // Conta admin / SYSTEM / sessão de serviço: NUNCA MessageBox (crash + WER "want to continue")
        // Exceto --test-loading / --security-agent (já tratados acima)
        if (IsAdministratorUser(user) || IsMachineOrServiceAccount(user))
        {
            _logger.Info("Launcher",
                "Conta administrativa/serviço — sem UI shell. Use --security-agent para menu Ctrl+End.");
            if (Environment.UserInteractive && !IsSessionZero())
            {
                TryShowInfoOnce(
                    "TurboRama.Launcher em conta administrativa.\n" +
                    "Kiosk = conta Arcade.\n\n" +
                    "Menu segurança: Ctrl+End\n" +
                    "  TurboRama.Launcher.exe --security-agent\n" +
                    "Preview:\n" +
                    "  TurboRama.Launcher.exe --test-security");
            }

            return;
        }

        ConfigurationStore.Load(out ProductConfiguration config);
        // Scripts ES → power-request.txt (Desligar/Reiniciar/Sair) para o Launcher
        EsPowerScriptsInstaller.EnsureInstalled(_logger);

        // Menu segurança TurboRama (Ctrl+End) — agente autónomo
        if (config.EnableSecurityMenu)
        {
            try { SecurityAgentHost.RegisterAndStart(_logger); }
            catch (Exception ex) { _logger.Warning("Launcher", "SecurityAgent: " + ex.Message); }
        }

        bool techMenu = config.EnableLauncherTechMenu;
        _hookEnabled = config.EnableLauncherKeyboardHook;

        if (_hookEnabled)
        {
            InstallKeyboardHook();
            _logger.Info("Launcher", "Keyboard hook ATIVO (config EnableLauncherKeyboardHook=true).");
        }
        else
        {
            _logger.Info("Launcher", "Keyboard hook OFF (default seguro).");
        }

        if (techMenu)
        {
            Application.AddMessageFilter(new TechMenuFilter(() => ShowTechMenu()));
        }

        string frontend = ResolveFrontend(config);
        _logger.Info("Launcher", "Frontend=" + frontend);

        // Loading + MP3 só no 1º ciclo da sessão (logon do usuário) — não nas bolinhas do Windows
        bool bootLoadingDone = false;

        int failures = 0;
        int missingNotices = 0;
        while (true)
        {
            if (!File.Exists(frontend))
            {
                _logger.Error("Launcher", "Frontend ausente: " + frontend, errorCode: "TR-001");
                // No máximo 1 diálogo por sessão kiosk; depois só log + espera
                if (missingNotices == 0 && Environment.UserInteractive)
                {
                    TryShowWarning(
                        "Frontend não encontrado:\n" + frontend +
                        "\n\nConfigure turborama.json ou coloque o EXE em C:\\TurboRama\\Frontend\\" +
                        (techMenu ? "\n\nCtrl+Shift+M = menu técnico." : ""));
                    missingNotices++;
                }

                Thread.Sleep(10_000);
                failures++;
                if (failures >= 5)
                {
                    break;
                }

                // Re-resolve (frontend pode ter sido instalado)
                frontend = ResolveFrontend(config);
                continue;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = frontend,
                    WorkingDirectory = Path.GetDirectoryName(frontend) ?? ProductPaths.Frontend,
                    UseShellExecute = true
                };

                // Limpa pedido de energia antigo (evita desligar por lixo de sessão anterior)
                PowerRequestStore.Clear();

                // 1ª subida da sessão: marca TURBORAMA VISÍVEL o tempo todo, SÓ DEPOIS abre o jogo
                if (!bootLoadingDone && config.ShowLoadingScreen)
                {
                    int brandMs = config.LoadingMinDisplayMs > 0 ? config.LoadingMinDisplayMs : 5000;
                    if (brandMs < 4000)
                    {
                        brandMs = 4000; // mínimo legível
                    }

                    _logger.Info("Launcher",
                        "Loading marca TURBORAMA " + brandMs + "ms ANTES do frontend (sem cobrir com o jogo).");

                    using var sound = new BootSoundPlayer();
                    using var loading = new LoadingScreenForm(config);

                    string soundPath = BootSoundPlayer.ResolveSoundPath(config.LoadingSoundFile);
                    _logger.Info("Launcher", "BootSound path resolvido: " + soundPath +
                                             " exists=" + File.Exists(soundPath));

                    bool soundStarted = false;
                    var soundKick = Stopwatch.StartNew();
                    try
                    {
                        loading.ShowBrandHold(brandMs, (p, st) =>
                        {
                            if (!soundStarted && soundKick.ElapsedMilliseconds >= 100)
                            {
                                soundStarted = sound.TryPlay(config.LoadingSoundFile, _logger);
                                _logger.Info("Launcher", "BootSound try ok=" + soundStarted);
                            }
                        });
                    }
                    catch (Exception exHold)
                    {
                        _logger.Error("Launcher", "ShowBrandHold: " + exHold.Message, errorCode: "TR-LOAD");
                    }

                    try
                    {
                        loading.HideLoading();
                    }
                    catch
                    {
                    }

                    // Liberta GDI da loading (reduz erro de memória ao trocar conta / relançar)
                    try
                    {
                        Application.DoEvents();
                        GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
                    }
                    catch
                    {
                    }

                    Application.DoEvents();
                    bootLoadingDone = true;
                    _logger.Info("Launcher", "Loading fechado — a iniciar frontend. soundOk=" + soundStarted);

                    Process? proc = null;
                    try
                    {
                        if (!TryStartFrontend(psi, frontend, out proc))
                        {
                            failures++;
                            // NUNCA desligar o PC se o jogo não chegou a arrancar
                            continue;
                        }

                        _frontendSessionSw = Stopwatch.StartNew();
                        int exitCode = WaitForFrontendLifecycle(proc, frontend);
                        _frontendSessionSw.Stop();
                        _logger.Info("Launcher", "Ciclo frontend encerrado (bootstrap exit=" + exitCode +
                                                 ", sessaoMs=" + _frontendSessionSw.ElapsedMilliseconds + ").");

                        if (HandleFrontendSessionEnd(out bool countFail))
                        {
                            break; // shutdown/reboot: sair do loop do Launcher
                        }

                        if (countFail)
                        {
                            failures++;
                        }
                        else
                        {
                            failures = 0;
                        }
                    }
                    finally
                    {
                        proc?.Dispose();
                        try { loading.HideLoading(); } catch { /* ignore */ }
                    }
                }
                else
                {
                    if (!TryStartFrontend(psi, frontend, out Process? proc))
                    {
                        failures++;
                        continue;
                    }

                    try
                    {
                        _frontendSessionSw = Stopwatch.StartNew();
                        int exitCode = WaitForFrontendLifecycle(proc, frontend);
                        _frontendSessionSw.Stop();
                        _logger.Info("Launcher", "Ciclo frontend encerrado (bootstrap exit=" + exitCode +
                                                 ", sessaoMs=" + _frontendSessionSw.ElapsedMilliseconds + ").");

                        if (HandleFrontendSessionEnd(out bool countFail))
                        {
                            break;
                        }

                        if (countFail)
                        {
                            failures++;
                        }
                        else
                        {
                            failures = 0;
                        }
                    }
                    finally
                    {
                        proc?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Launcher", ex.Message, errorCode: "TR-LAUNCH");
                failures++;
            }

            Thread.Sleep(3000);
            if (failures >= 8)
            {
                _logger.Error("Launcher", "Muitas falhas — saindo do loop.", errorCode: "TR-008");
                break;
            }
        }

        UninstallKeyboardHook();
        ReleaseSingleInstance();
    }

    /// <summary>
    /// TurboRama.exe (RetroBat-like) inicia EmulationStation e sai com code 0 em poucos segundos.
    /// Polling rápido: se o menu Desligar/Reiniciar gravar power-request, mata o frontend
    /// e devolve logo — evita ecrã preto entre o ES fechar e a splash TurboRama.
    /// </summary>
    private static int WaitForFrontendLifecycle(Process? bootstrap, string frontendPath)
    {
        int exitCode = -1;
        try
        {
            // Espera bootstrap OU pedido de energia (Desligar a meio)
            while (bootstrap != null && !bootstrap.HasExited)
            {
                if (IsPowerOffRequestPending())
                {
                    _logger?.Info("Launcher", "power-request detetado durante bootstrap — a libertar ecrã.");
                    ForceCloseFrontendUi(frontendPath);
                    try { exitCode = bootstrap.HasExited ? bootstrap.ExitCode : 0; } catch { exitCode = 0; }
                    return exitCode;
                }

                Thread.Sleep(200);
                Application.DoEvents();
            }

            if (bootstrap != null)
            {
                try { exitCode = bootstrap.ExitCode; } catch { exitCode = -1; }
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning("Launcher", "WaitForExit bootstrap: " + ex.Message);
        }

        _logger?.Info("Launcher", "Bootstrap saiu code=" + exitCode);

        // Companheiros (ES) vivos — poll 200ms (antes era 2s → ecrã preto longo)
        string name = Path.GetFileNameWithoutExtension(frontendPath) ?? "";
        while (AnyCompanionRunning() || (!string.IsNullOrEmpty(name) && IsProcessRunning(name)))
        {
            if (IsPowerOffRequestPending())
            {
                _logger?.Info("Launcher",
                    "power-request (Desligar/Reiniciar) — a fechar frontend e mostrar splash.");
                ForceCloseFrontendUi(frontendPath);
                Thread.Sleep(250);
                Application.DoEvents();
                return exitCode;
            }

            Thread.Sleep(200);
            Application.DoEvents();
        }

        _logger?.Info("Launcher", "Companheiros do frontend encerrados.");
        // Pequena pausa só se NÃO for desligar (quit/crash)
        if (!IsPowerOffRequestPending())
        {
            Thread.Sleep(400);
        }

        return exitCode;
    }

    private static bool IsPowerOffRequestPending()
    {
        // Quit também conta: no menu TurboRama "Desligar" dispara script quit
        PowerRequestKind k = PowerRequestStore.Peek();
        return k is PowerRequestKind.Shutdown or PowerRequestKind.Reboot or PowerRequestKind.Quit;
    }

    /// <summary>
    /// Termina ES/TurboRama/emuladores para a splash não ficar por baixo (ecrã preto).
    /// </summary>
    private static void ForceCloseFrontendUi(string? frontendPath = null)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string p in FrontendKillList)
        {
            names.Add(p);
        }

        if (!string.IsNullOrWhiteSpace(frontendPath))
        {
            names.Add(Path.GetFileNameWithoutExtension(frontendPath) ?? "");
        }

        foreach (string n in names)
        {
            if (string.IsNullOrWhiteSpace(n))
            {
                continue;
            }

            try
            {
                foreach (Process p in Process.GetProcessesByName(n.Replace(".exe", "", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.CloseMainWindow();
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    try
                    {
                        if (!p.HasExited && !p.WaitForExit(400))
                        {
                            p.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                        try { p.Kill(); } catch { /* ignore */ }
                    }
                    finally
                    {
                        try { p.Dispose(); } catch { /* ignore */ }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        // Liberta VRAM/GDI do frontend (reduz "erro de memória" ao trocar conta)
        try
        {
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        }
        catch
        {
            // ignore
        }
    }

    private static bool AnyCompanionRunning()
    {
        foreach (string p in FrontendCompanionProcesses)
        {
            if (IsProcessRunning(p))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProcessRunning(string processName)
    {
        try
        {
            string name = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
            return Process.GetProcessesByName(name).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveFrontend(ProductConfiguration config)
    {
        string frontend = config.FrontendExecutable;
        if (!string.IsNullOrWhiteSpace(frontend) && File.Exists(frontend))
        {
            return frontend;
        }

        string[] candidates =
        {
            Path.Combine(ProductPaths.Frontend, "Frontend.exe"),
            Path.Combine(ProductPaths.Frontend, "TurboRama.exe"),
            @"D:\Turborama\TurboRama.exe",
            @"D:\TURBOPCINSTALL\build\TurboRama.exe",
            @"D:\Turborama\emulationstation\emulationstation.exe",
        };
        return candidates.FirstOrDefault(File.Exists) ?? frontend;
    }

    /// <summary>
    /// Inicia o frontend. Se falhar e não houver ES/companheiros, NÃO desliga o Windows.
    /// </summary>
    private static bool TryStartFrontend(ProcessStartInfo psi, string frontendPath, out Process? proc)
    {
        proc = null;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger?.Error("Launcher", "Falha ao iniciar frontend: " + ex.Message, errorCode: "TR-START");
            return false;
        }

        int pid = proc?.Id ?? 0;
        _logger?.Info("Launcher", "Frontend iniciado PID=" + (pid > 0 ? pid.ToString() : "?") + " path=" + frontendPath);

        if (proc != null)
        {
            return true;
        }

        // Process.Start devolveu null — espera um pouco por companheiros (ES)
        Thread.Sleep(2500);
        string name = Path.GetFileNameWithoutExtension(frontendPath) ?? "";
        if (AnyCompanionRunning() || (!string.IsNullOrEmpty(name) && IsProcessRunning(name)))
        {
            _logger?.Info("Launcher", "Frontend sem Process handle mas processo/companheiro ativo — OK.");
            return true;
        }

        _logger?.Error("Launcher", "Frontend não arrancou (Process.Start=null, sem companheiros).", errorCode: "TR-START");
        return false;
    }

    /// <summary>
    /// Após o frontend fechar: interpreta power-request.txt (scripts ES).
    /// Returns true = Launcher deve sair do loop (shutdown/reboot em curso).
    /// Returns false = relançar frontend. countAsFailure=true se crash (sem flag).
    /// </summary>
    private static bool HandleFrontendSessionEnd(out bool countAsFailure)
    {
        countAsFailure = false;
        PowerRequestKind req = PowerRequestStore.Consume();
        _logger?.Info("Launcher", "Pedido de energia após frontend: " + req);

        switch (req)
        {
            case PowerRequestKind.Shutdown:
            case PowerRequestKind.Quit:
                // No TurboRama/ES o menu "Desligar" costuma disparar o script QUIT (não SHUTDOWN).
                // Antes: Quit → relançava TurboRama (ecrã preto / reabre o jogo). Agora: splash + power off.
                _logger?.Info("Launcher",
                    "Menu Desligar/Sair (" + req + ") — splash + desligar Windows (kiosk).");
                EnterPowerLock("shutdown");
                RunPowerOffSequence(reboot: false);
                return true;

            case PowerRequestKind.Reboot:
                EnterPowerLock("reboot");
                RunPowerOffSequence(reboot: true);
                return true;

            default:
                // Sem flag: se o frontend saiu de forma limpa após sessão, no kiosk = Desligar
                // (scripts ES por vezes não correm). Só relança se a sessão foi muito curta (crash ao abrir).
                if (_frontendSessionSw != null && _frontendSessionSw.ElapsedMilliseconds >= 8000)
                {
                    _logger?.Info("Launcher",
                        "Frontend saiu sem flag após " + _frontendSessionSw.ElapsedMilliseconds +
                        "ms — kiosk: tratar como Desligar (splash + power off).");
                    EnterPowerLock("shutdown");
                    RunPowerOffSequence(reboot: false);
                    return true;
                }

                _logger?.Warning("Launcher",
                    "Frontend saiu cedo/sem flag (" +
                    (_frontendSessionSw?.ElapsedMilliseconds ?? 0) +
                    "ms) — crash/recuperação: relançar TurboRama (sem power-off).");
                countAsFailure = true;
                return false;
        }
    }

    private static void EnterPowerLock(string reason)
    {
        try
        {
            // Impede o Watchdog de reiniciar o Launcher a meio do desligar/reiniciar
            var r = MaintenanceLock.Enter("power-" + reason, Environment.UserName);
            _logger?.Info("Launcher", "maintenance.lock (Watchdog pause): " + r.Message);
        }
        catch (Exception ex)
        {
            _logger?.Warning("Launcher", "maintenance.lock falhou: " + ex.Message);
        }
    }

    private static void ExitPowerLockSafe()
    {
        try
        {
            MaintenanceLock.Exit();
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Splash TurboRama + desligar ou reiniciar Windows.
    /// Fecha o frontend primeiro (sem ecrã preto) e só depois pede o power-off.
    /// </summary>
    private static void RunPowerOffSequence(bool reboot)
    {
        string action = reboot ? "reiniciar" : "desligar";
        _logger?.Info("Launcher", "A mostrar tela de desligar e a " + action + " o Windows.");

        // 1) Matar UI do jogo ANTES da splash (principal causa do ecrã preto)
        try
        {
            ForceCloseFrontendUi();
            Thread.Sleep(200);
            Application.DoEvents();
        }
        catch (Exception ex)
        {
            _logger?.Warning("Launcher", "ForceCloseFrontendUi: " + ex.Message);
        }

        // 2) Cancela shutdown que o ES possa ter agendado (ganhamos tempo para a splash)
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/a",
                CreateNoWindow = true,
                UseShellExecute = false
            })?.Dispose();
        }
        catch
        {
            // ignore
        }

        bool ok = false;
        string msg = string.Empty;
        try
        {
            // Splash mais curta no real: 3.5s + 8s cobrir Windows (evita “preto” à espera)
            ShutdownScreenForm.ShowAndHold(
                holdMsBefore: 3500,
                holdMsAfter: 8000,
                shutdownAction: () =>
                {
                    ok = reboot
                        ? PowerShutdownHelper.RebootNow(out msg, _logger)
                        : PowerShutdownHelper.ShutdownNow(out msg, _logger);
                    _logger?.Info("Launcher", action + " ok=" + ok + " " + msg);
                });
        }
        catch (Exception ex)
        {
            _logger?.Error("Launcher", "Tela desligar: " + ex.Message, errorCode: "TR-SHUT-UI");
            try
            {
                ok = reboot
                    ? PowerShutdownHelper.RebootNow(out msg, _logger)
                    : PowerShutdownHelper.ShutdownNow(out msg, _logger);
            }
            catch (Exception ex2)
            {
                msg = ex2.Message;
            }
        }

        if (!ok)
        {
            _logger?.Error("Launcher", "Falha ao " + action + " PC: " + msg, errorCode: reboot ? "TR-REBOOT" : "TR-SHUT");
            ExitPowerLockSafe();
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch
            {
                // ignore
            }

            if (Environment.UserInteractive)
            {
                try
                {
                    MessageBox.Show(
                        "Não foi possível " + action + " o PC.\n" + msg,
                        "TurboRama",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private static void RunSecurityMenuPreview()
    {
        try
        {
            ConfigurationStore.Load(out ProductConfiguration config);
            string pin = SystemSecurityForm.ResolvePin(config);
            _logger?.Info("Launcher", "PREVIEW menu segurança (Ctrl+End) — sem executar power-off real");
            using var form = new SystemSecurityForm(pin);
            form.ShowDialog();
            _logger?.Info("Launcher", "PREVIEW segurança ação=" + form.ResultAction);
        }
        catch (Exception ex)
        {
            _logger?.Error("Launcher", "PREVIEW security: " + ex.Message, errorCode: "TR-PREVIEW-SEC");
            try
            {
                MessageBox.Show("Falha no preview do menu:\n" + ex.Message, "TurboRama",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Preview da tela de desligar SEM desligar o PC.
    /// </summary>
    private static void RunShutdownPreview(string[] args)
    {
        try
        {
            // Preview: mesma “respiração” da loading (~5s) + 2s final
            int before = 5000;
            int after = 2000;
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i].Equals("--test-shutdown", StringComparison.OrdinalIgnoreCase) ||
                     args[i].Equals("--preview-shutdown", StringComparison.OrdinalIgnoreCase) ||
                     args[i].Equals("/test-shutdown", StringComparison.OrdinalIgnoreCase)) &&
                    i + 1 < args.Length &&
                    int.TryParse(args[i + 1], out int s) &&
                    s is >= 2 and <= 20)
                {
                    before = s * 1000;
                    after = Math.Max(1500, s * 400);
                }
            }

            _logger?.Info("Launcher", "PREVIEW shutdown " + before + "+" + after + "ms (NÃO desliga o PC)");
            ShutdownScreenForm.ShowAndHold(before, after, shutdownAction: null);
            _logger?.Info("Launcher", "PREVIEW shutdown encerrado.");
        }
        catch (Exception ex)
        {
            _logger?.Error("Launcher", "PREVIEW shutdown: " + ex.Message, errorCode: "TR-PREVIEW-SHUT");
            try
            {
                MessageBox.Show("Falha no preview da tela de desligar:\n" + ex.Message, "TurboRama",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <summary>
    /// Abre só a tela de loading (+ som) e sai. Funciona como Admin ou Arcade, sem reboot.
    /// </summary>
    private static void RunLoadingPreview(string[] args)
    {
        try
        {
            ConfigurationStore.Load(out ProductConfiguration config);
            int seconds = 5;
            // --test-loading 8
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i].Equals("--test-loading", StringComparison.OrdinalIgnoreCase) ||
                     args[i].Equals("--preview-loading", StringComparison.OrdinalIgnoreCase) ||
                     args[i].Equals("/test-loading", StringComparison.OrdinalIgnoreCase)) &&
                    i + 1 < args.Length &&
                    int.TryParse(args[i + 1], out int s) &&
                    s is >= 2 and <= 30)
                {
                    seconds = s;
                }
            }

            int ms = config.LoadingMinDisplayMs > 0 ? config.LoadingMinDisplayMs : seconds * 1000;
            if (HasArg(args, "--test-loading") || HasArg(args, "--preview-loading") || HasArg(args, "/test-loading"))
            {
                // se passou número, usa esse; senão config ou 5s
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (int.TryParse(args[i + 1], out int s) && s is >= 2 and <= 30 &&
                        (args[i].Contains("test-loading", StringComparison.OrdinalIgnoreCase) ||
                         args[i].Contains("preview-loading", StringComparison.OrdinalIgnoreCase)))
                    {
                        ms = s * 1000;
                    }
                }
            }

            if (ms < 3000)
            {
                ms = 3000;
            }

            _logger?.Info("Launcher", "PREVIEW loading " + ms + "ms (sem kiosk/reboot)");

            using var sound = new BootSoundPlayer();
            using var loading = new LoadingScreenForm(config);
            bool soundOk = false;
            var kick = Stopwatch.StartNew();
            loading.ShowBrandHold(ms, (_, _) =>
            {
                if (!soundOk && kick.ElapsedMilliseconds >= 100)
                {
                    soundOk = sound.TryPlay(config.LoadingSoundFile, _logger);
                }
            });
            if (!soundOk)
            {
                sound.TryPlay(config.LoadingSoundFile, _logger);
            }

            loading.HideLoading();
            _logger?.Info("Launcher", "PREVIEW loading encerrado. soundOk=" + soundOk);
        }
        catch (Exception ex)
        {
            _logger?.Error("Launcher", "PREVIEW loading: " + ex.Message, errorCode: "TR-PREVIEW");
            try
            {
                MessageBox.Show("Falha no preview da loading:\n" + ex.Message, "TurboRama",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
            }
        }
    }

    private static bool HasArg(string[] args, string name)
    {
        foreach (string a in args)
        {
            if (a.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ShowTechMenu()
    {
        try
        {
            var choice = MessageBox.Show(
                "Menu técnico TurboRama (Launcher)\n\n" +
                "Sim = Status do serviço Maintenance (pipe)\n" +
                "Não = Sair manutenção (clear lock via pipe)\n" +
                "Cancelar = fechar menu\n\n" +
                "Requer serviço Maintenance RUNNING e permissão no pipe.",
                "TurboRama — Técnico",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (choice == DialogResult.Cancel)
            {
                return;
            }

            string cmd = choice == DialogResult.Yes
                ? MaintenanceProtocol.Commands.Status
                : MaintenanceProtocol.Commands.ExitMaintenance;

            OperationResult r = MaintenanceClient.Send(cmd, timeoutMs: 2500);
            TryShowInfoOnce(
                r.Success ? r.Message : ("Falha: " + r.Message),
                "TurboRama — Pipe");
            _logger?.Info("Launcher", "TechMenu " + cmd + " => " + r.Message);
        }
        catch (Exception ex)
        {
            _logger?.Error("Launcher", "TechMenu: " + ex.Message, errorCode: "TR-TECH");
        }
    }

    private static void TryShowInfoOnce(string text, string caption = "TurboRama Launcher")
    {
        try
        {
            if (!Environment.UserInteractive || IsSessionZero())
            {
                return;
            }

            MessageBox.Show(text, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger?.Warning("Launcher", "MessageBox ignorado: " + ex.Message);
        }
    }

    private static void TryShowWarning(string text)
    {
        try
        {
            if (!Environment.UserInteractive || IsSessionZero())
            {
                return;
            }

            MessageBox.Show(text, "TurboRama", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _logger?.Warning("Launcher", "MessageBox ignorado: " + ex.Message);
        }
    }

    private static bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstance = new Mutex(true, SingleInstanceMutexName, out bool created);
            if (!created)
            {
                _singleInstance.Dispose();
                _singleInstance = null;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Se mutex falhar (ACL), não bloquear kiosk
            _logger?.Warning("Launcher", "Mutex single-instance: " + ex.Message);
            return true;
        }
    }

    private static void ReleaseSingleInstance()
    {
        try
        {
            _singleInstance?.ReleaseMutex();
            _singleInstance?.Dispose();
            _singleInstance = null;
        }
        catch
        {
        }
    }

    private static bool IsAdministratorUser(string user)
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var p = new System.Security.Principal.WindowsPrincipal(id);
            return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return user.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                   user.Equals("Administrator", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool IsMachineOrServiceAccount(string user)
    {
        if (string.IsNullOrEmpty(user))
        {
            return true;
        }

        // SYSTEM, LOCAL SERVICE, NETWORK SERVICE, ou COMPUTERNAME$
        if (user.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) ||
            user.Equals("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase) ||
            user.Equals("NETWORK SERVICE", StringComparison.OrdinalIgnoreCase) ||
            user.EndsWith("$", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            string? name = id.Name;
            if (!string.IsNullOrEmpty(name) &&
                (name.EndsWith("\\SYSTEM", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("NT AUTHORITY", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>Session 0 = serviços; MessageBox não é válido.</summary>
    private static bool IsSessionZero()
    {
        try
        {
            return Process.GetCurrentProcess().SessionId == 0;
        }
        catch
        {
            return !Environment.UserInteractive;
        }
    }

    // ---- Keyboard hook (optional, default OFF) ----
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static void InstallKeyboardHook()
    {
        _proc = HookCallback;
        using Process cur = Process.GetCurrentProcess();
        using ProcessModule mod = cur.MainModule!;
        _hook = SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, _proc, GetModuleHandle(mod.ModuleName), 0);
    }

    private static void UninstallKeyboardHook()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _hookEnabled)
        {
            int vk = Marshal.ReadInt32(lParam);
            const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_TAB = 0x09, VK_ESCAPE = 0x1B;
            bool alt = (Control.ModifierKeys & Keys.Alt) == Keys.Alt;
            bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;
            if (vk is VK_LWIN or VK_RWIN)
            {
                return (IntPtr)1;
            }

            if (alt && vk == VK_TAB)
            {
                return (IntPtr)1;
            }

            if (ctrl && vk == VK_ESCAPE)
            {
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private sealed class TechMenuFilter : IMessageFilter
    {
        private readonly Action _onMenu;
        public TechMenuFilter(Action onMenu) => _onMenu = onMenu;

        public bool PreFilterMessage(ref Message m)
        {
            const int WM_KEYDOWN = 0x100;
            if (m.Msg == WM_KEYDOWN)
            {
                Keys key = (Keys)(int)m.WParam;
                if (key == Keys.M &&
                    Control.ModifierKeys == (Keys.Control | Keys.Shift))
                {
                    _onMenu();
                    return true;
                }
            }

            return false;
        }
    }
}
