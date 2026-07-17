using System.Diagnostics;
using TurboRama.Configuration;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;
using TurboRama.Core.State;

namespace TurboRama.Watchdog;

/// <summary>
/// Política de restart (estudo §13):
/// 1ª: 5s, 2ª: 15s, 3ª: 30s, 4ª em 10 min: modo recuperação (para de reiniciar).
/// Não reinicia launcher se houver Explorer (sessão Admin/manutenção).
/// Reinicia Launcher somente na sessão interativa (CreateProcessAsUser) — nunca Session 0.
/// Sai do modo recovery se recovery.flag e lock forem limpos manualmente.
/// </summary>
public sealed class WatchdogWorker
{
    private readonly ITurboRamaLogger _logger;
    private readonly int[] _delaysSeconds = { 5, 15, 30, 60 };
    private readonly List<DateTimeOffset> _restartTimes = new();
    private int _consecutiveFailures;
    private bool _recoveryMode;

    public WatchdogWorker(ITurboRamaLogger logger)
    {
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Watchdog", "WatchdogWorker iniciado.");
        ConfigurationStore.Load(out ProductConfiguration config);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_recoveryMode && CanExitRecovery())
                {
                    _recoveryMode = false;
                    _consecutiveFailures = 0;
                    _restartTimes.Clear();
                    ClearRecoveryFlag();
                    _logger.Info("Watchdog", "Saiu do modo recuperação (flags limpos).");
                }

                if (MaintenanceLock.IsActive())
                {
                    _logger.Info("Watchdog", "maintenance.lock ativo — sem reinícios.");
                    await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (_recoveryMode)
                {
                    _logger.Warning("Watchdog", "Modo recuperação — limpe recovery.flag e maintenance.lock (Sair manutenção / Fase 6).");
                    WriteRecoveryFlag("TR-008 loop de reinício");
                    await Task.Delay(10_000, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                bool hasExplorer = IsProcessRunning("explorer");
                bool hasLauncher = IsProcessRunning("TurboRama.Launcher");
                if (!hasLauncher && hasExplorer)
                {
                    if (_consecutiveFailures > 0)
                    {
                        _logger.Info("Watchdog", "Explorer ativo (manutenção Admin) — sem reiniciar Launcher; contador zerado.");
                    }

                    _consecutiveFailures = 0;
                    _restartTimes.Clear();
                    await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                string launcher = Path.Combine(ProductPaths.AppLauncher, "TurboRama.Launcher.exe");

                if (!hasLauncher)
                {
                    // Sem sessão interativa: não dispare Launcher em Session 0 (crash MessageBox / WER)
                    if (!InteractiveSessionLauncher.HasInteractiveConsoleSession())
                    {
                        _logger.Info("Watchdog", "Launcher ausente mas sem sessão console interativa — aguardando logon kiosk.");
                        await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    _consecutiveFailures++;
                    PruneRestarts();
                    _restartTimes.Add(DateTimeOffset.Now);

                    if (_restartTimes.Count >= 4)
                    {
                        _recoveryMode = true;
                        _logger.Error("Watchdog", "4 falhas em 10 min — entrando em recuperação.", errorCode: "TR-008");
                        WriteRecoveryFlag("TR-008");
                        MaintenanceLock.Enter("watchdog-recovery", "SYSTEM");
                        continue;
                    }

                    int delayIdx = Math.Min(_consecutiveFailures - 1, _delaysSeconds.Length - 1);
                    int delay = config.Watchdog.RestartDelaySeconds > 0 && _consecutiveFailures == 1
                        ? config.Watchdog.RestartDelaySeconds
                        : _delaysSeconds[delayIdx];

                    if (config.Watchdog.MaximumRestarts > 0 &&
                        _consecutiveFailures > config.Watchdog.MaximumRestarts)
                    {
                        _recoveryMode = true;
                        WriteRecoveryFlag("TR-008 max restarts");
                        MaintenanceLock.Enter("watchdog-max-restarts", "SYSTEM");
                        continue;
                    }

                    _logger.Warning("Watchdog", "Launcher morto. Reinício #" + _consecutiveFailures + " em " + delay + "s (sessão interativa)");
                    await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken).ConfigureAwait(false);

                    if (MaintenanceLock.IsActive() || IsProcessRunning("explorer") || IsProcessRunning("TurboRama.Launcher"))
                    {
                        continue;
                    }

                    if (!File.Exists(launcher))
                    {
                        _logger.Error("Watchdog", "Launcher ausente: " + launcher, errorCode: "WD_MISSING");
                        continue;
                    }

                    if (InteractiveSessionLauncher.TryStartInActiveSession(
                            launcher, ProductPaths.AppLauncher, out string detail))
                    {
                        _logger.Info("Watchdog", "Launcher reiniciado na sessão interativa: " + detail);
                    }
                    else
                    {
                        _logger.Error("Watchdog", "Falha ao iniciar Launcher na sessão: " + detail, errorCode: "WD_START");
                        // Não usar Process.Start (Session 0) — piora o problema
                    }
                }
                else
                {
                    if (_consecutiveFailures > 0)
                    {
                        _logger.Info("Watchdog", "Launcher vivo — contador zerado.");
                    }

                    _consecutiveFailures = 0;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("Watchdog", ex.Message, errorCode: "WD_LOOP");
            }

            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
        }

        _logger.Info("Watchdog", "WatchdogWorker encerrado.");
    }

    private static bool CanExitRecovery()
    {
        try
        {
            string flag = Path.Combine(ProductPaths.State, "recovery.flag");
            return !File.Exists(flag) && !MaintenanceLock.IsActive();
        }
        catch
        {
            return false;
        }
    }

    private void PruneRestarts()
    {
        DateTimeOffset cutoff = DateTimeOffset.Now.AddMinutes(-10);
        _restartTimes.RemoveAll(t => t < cutoff);
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

    private static void WriteRecoveryFlag(string code)
    {
        try
        {
            Directory.CreateDirectory(ProductPaths.State);
            File.WriteAllText(
                Path.Combine(ProductPaths.State, "recovery.flag"),
                code + Environment.NewLine + DateTimeOffset.Now.ToString("o"));
        }
        catch
        {
        }
    }

    private static void ClearRecoveryFlag()
    {
        try
        {
            string flag = Path.Combine(ProductPaths.State, "recovery.flag");
            if (File.Exists(flag))
            {
                File.Delete(flag);
            }
        }
        catch
        {
        }
    }
}
