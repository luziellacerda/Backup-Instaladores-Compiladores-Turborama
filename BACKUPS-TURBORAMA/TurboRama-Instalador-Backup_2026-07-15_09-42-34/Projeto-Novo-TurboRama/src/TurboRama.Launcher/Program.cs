using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TurboRama.Configuration;
using TurboRama.Core.Ipc;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;

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

    private static ITurboRamaLogger? _logger;
    private static bool _hookEnabled;
    private static IntPtr _hook = IntPtr.Zero;
    private static LowLevelKeyboardProc? _proc;
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try { ProductPaths.EnsureLayout(); } catch { /* ignore */ }

        _logger = new FileTurboRamaLogger(
            Directory.Exists(ProductPaths.LauncherLogs) ? ProductPaths.LauncherLogs : Path.GetTempPath(),
            "launcher");

        string user = Environment.UserName ?? "";
        _logger.Info("Launcher", "Início. USER=" + user + " Interactive=" + Environment.UserInteractive +
                                   " Args=" + string.Join(' ', args ?? Array.Empty<string>()));

        // Uma única instância: evita Watchdog (SYSTEM) + shell Arcade em paralelo
        if (!TryAcquireSingleInstance())
        {
            _logger.Info("Launcher", "Outra instância já ativa — saindo sem diálogo.");
            return;
        }

        // Conta admin / SYSTEM / sessão de serviço: NUNCA MessageBox (crash + WER "want to continue")
        if (IsAdministratorUser(user) || IsMachineOrServiceAccount(user))
        {
            _logger.Info("Launcher",
                "Conta administrativa/serviço — sem UI. O shell do kiosk (Arcade) deve iniciar o Launcher.");
            // Em desktop Admin interativo, dica única e silenciosa se falhar
            if (Environment.UserInteractive && !IsSessionZero())
            {
                TryShowInfoOnce(
                    "TurboRama.Launcher em conta administrativa.\n" +
                    "No kiosk use a conta Arcade (shell).\n" +
                    "Este processo não altera o Windows.");
            }

            return;
        }

        ConfigurationStore.Load(out ProductConfiguration config);
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

                // 1ª subida da sessão (logon Arcade): loading + boot.wav/logo (pack estável)
                if (!bootLoadingDone && config.ShowLoadingScreen)
                {
                    _logger.Info("Launcher",
                        "Exibindo loading de logon (Assets: boot.wav / boot-up.mp3 + logo.png).");

                    using var sound = new BootSoundPlayer();
                    using var loading = new LoadingScreenForm(config);
                    loading.ShowLoading();
                    loading.SetStatus("Entrando no TurboRama...");
                    loading.SetProgress(8);
                    Application.DoEvents();

                    // Som primeiro (WAV estável ou MP3 se o técnico colocou)
                    bool soundOk = sound.TryPlay(config.LoadingSoundFile, _logger);
                    _logger.Info("Launcher", "BootSound ok=" + soundOk + " path=" + BootSoundPlayer.ResolveSoundPath(config.LoadingSoundFile));

                    // Pequena pausa para o utilizador ver a tela + ouvir o chime
                    int minMs = config.LoadingMinDisplayMs > 0 ? config.LoadingMinDisplayMs : 4500;
                    var pre = Stopwatch.StartNew();
                    while (pre.ElapsedMilliseconds < Math.Min(1200, minMs / 3))
                    {
                        loading.SetProgress(8 + (int)(pre.ElapsedMilliseconds / 40));
                        loading.SetStatus("Preparando console arcade...");
                        Application.DoEvents();
                        Thread.Sleep(30);
                    }

                    loading.SetStatus("Iniciando arcade...");
                    loading.SetProgress(35);
                    Application.DoEvents();

                    using Process? proc = Process.Start(psi);
                    int pid = proc?.Id ?? 0;
                    _logger.Info("Launcher", "Frontend iniciado PID=" + (pid > 0 ? pid.ToString() : "?"));

                    var sw = Stopwatch.StartNew();
                    int maxWait = Math.Max(minMs + 8000, 14000);
                    while (sw.ElapsedMilliseconds < maxWait)
                    {
                        Application.DoEvents();
                        Thread.Sleep(40);
                        bool minOk = sw.ElapsedMilliseconds >= minMs;
                        bool esReady = AnyCompanionRunning();
                        int p = 35 + (int)(55.0 * Math.Min(1.0, sw.ElapsedMilliseconds / (double)Math.Max(minMs, 1)));
                        loading.SetProgress(Math.Min(96, p));

                        if (esReady && minOk)
                        {
                            loading.SetStatus("Pronto.");
                            loading.SetProgress(100);
                            Thread.Sleep(400);
                            break;
                        }

                        if (minOk && proc != null && proc.HasExited && !esReady)
                        {
                            loading.SetStatus("A iniciar...");
                            // Mantém loading até minMs já cumprido; sai do loop
                            break;
                        }

                        if (sw.ElapsedMilliseconds > minMs / 2)
                        {
                            loading.SetStatus(esReady ? "Quase lá..." : "Carregando menu...");
                        }
                    }

                    Thread.Sleep(200);
                    loading.HideLoading();
                    // Não para o som de imediato se ainda estiver a tocar o fim do chime
                    Thread.Sleep(100);
                    sound.Stop();
                    bootLoadingDone = true;
                    _logger.Info("Launcher", "Loading de logon encerrado.");

                    int exitCode = WaitForFrontendLifecycle(proc, frontend);
                    _logger.Info("Launcher", "Ciclo frontend encerrado (bootstrap exit=" + exitCode + ").");
                    failures = 0;
                }
                else
                {
                    using Process? proc = Process.Start(psi);
                    int pid = proc?.Id ?? 0;
                    _logger.Info("Launcher", "Frontend iniciado PID=" + (pid > 0 ? pid.ToString() : "?"));

                    // Espera o bootstrap (TurboRama.exe) e, se for o caso, o EmulationStation
                    int exitCode = WaitForFrontendLifecycle(proc, frontend);
                    _logger.Info("Launcher", "Ciclo frontend encerrado (bootstrap exit=" + exitCode + ").");
                    failures = 0;
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
    /// Reiniciar o EXE enquanto ES está vivo causa "already running" e loop de restart = falha de kiosk.
    /// </summary>
    private static int WaitForFrontendLifecycle(Process? bootstrap, string frontendPath)
    {
        int exitCode = -1;
        try
        {
            bootstrap?.WaitForExit();
            exitCode = bootstrap?.ExitCode ?? -1;
        }
        catch (Exception ex)
        {
            _logger?.Warning("Launcher", "WaitForExit bootstrap: " + ex.Message);
        }

        _logger?.Info("Launcher", "Bootstrap saiu code=" + exitCode);

        // Se companheiros (ES) estão vivos, ficar no loop até eles saírem — sem relançar TurboRama.exe
        if (AnyCompanionRunning())
        {
            _logger?.Info("Launcher",
                "Frontend bootstrap encerrou com companheiro ativo (EmulationStation) — aguardando saída do jogo/UI.");
            while (AnyCompanionRunning())
            {
                Thread.Sleep(2000);
            }

            _logger?.Info("Launcher", "Companheiros do frontend encerrados.");
            Thread.Sleep(1000);
            return exitCode;
        }

        // Bootstrap saiu rápido sem deixar ES: pode ser falha ou single-instance
        string name = Path.GetFileNameWithoutExtension(frontendPath);
        if (!string.IsNullOrEmpty(name) && IsProcessRunning(name) &&
            (bootstrap == null || bootstrap.HasExited))
        {
            _logger?.Info("Launcher", "Outra instância de " + name + " ainda ativa — aguardando.");
            while (IsProcessRunning(name))
            {
                Thread.Sleep(2000);
            }
        }

        return exitCode;
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
