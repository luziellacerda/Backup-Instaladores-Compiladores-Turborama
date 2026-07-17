using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using TurboRama.Core.Ipc;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;
using TurboRama.Core.State;

namespace TurboRama.Maintenance;

/// <summary>
/// Servidor named pipe: apenas comandos predefinidos (estudo §14).
/// Várias instâncias + timeout de leitura — evita travar o Status na UI.
/// </summary>
public sealed class MaintenancePipeServer
{
    private readonly ITurboRamaLogger _logger;

    public MaintenancePipeServer(ITurboRamaLogger logger)
    {
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Maintenance", "Pipe server em " + MaintenanceProtocol.PipePath);

        // Aceita várias conexões em paralelo (UI pode abandonar timeout sem travar o loop).
        var workers = new List<Task>();
        for (int i = 0; i < 4; i++)
        {
            int workerId = i;
            workers.Add(Task.Run(() => AcceptLoopAsync(workerId, cancellationToken), cancellationToken));
        }

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutdown normal
        }
    }

    private async Task AcceptLoopAsync(int workerId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                string response = await HandleConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
                if (response.Length > 0 && pipe.IsConnected)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(response + "\n");
                    await pipe.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                    await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("Maintenance", "w" + workerId + ": " + ex.Message, errorCode: "PIPE_LOOP");
                try
                {
                    await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                if (pipe is not null)
                {
                    try
                    {
                        if (pipe.IsConnected)
                        {
                            pipe.Disconnect();
                        }
                    }
                    catch
                    {
                        /* ignore */
                    }

                    try { pipe.Dispose(); } catch { /* ignore */ }
                }
            }
        }
    }

    private async Task<string> HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        // Timeout de leitura: se a UI abandonar, liberamos o worker.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(4));

        byte[] buffer = new byte[2048];
        int n;
        try
        {
            n = await pipe.ReadAsync(buffer.AsMemory(0, buffer.Length), linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Info("Maintenance", "Read timeout — conexão abandonada");
            return string.Empty;
        }

        if (n <= 0)
        {
            return "ERR EMPTY";
        }

        string line = Encoding.UTF8.GetString(buffer, 0, n)
            .Trim()
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        string cmd = line.Trim().ToUpperInvariant();
        _logger.Info("Maintenance", "CMD=" + cmd);
        return Handle(cmd);
    }

    private static NamedPipeServerStream CreatePipe()
    {
        // ACL: Administrators + SYSTEM (UI deve rodar elevada)
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            MaintenanceProtocol.PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 4,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private string Handle(string cmd)
    {
        if (!MaintenanceProtocol.IsAllowed(cmd))
        {
            return "ERR DENIED";
        }

        try
        {
            return cmd switch
            {
                MaintenanceProtocol.Commands.Ping => "OK PONG",
                MaintenanceProtocol.Commands.Status => BuildStatus(),
                MaintenanceProtocol.Commands.EnterMaintenance => EnterMaintenance(),
                MaintenanceProtocol.Commands.ExitMaintenance => ExitMaintenance(),
                MaintenanceProtocol.Commands.RestartLauncher => RestartLauncher(),
                MaintenanceProtocol.Commands.StopWatchdogRestarts => EnterMaintenance("stop-watchdog"),
                MaintenanceProtocol.Commands.AllowWatchdogRestarts => ExitMaintenance(),
                MaintenanceProtocol.Commands.Reboot => ScheduleShutdown("/r"),
                MaintenanceProtocol.Commands.Shutdown => ScheduleShutdown("/s"),
                _ => "ERR UNKNOWN"
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Maintenance", ex.Message, errorCode: "CMD_EX");
            return "ERR " + ex.Message.Replace('\n', ' ');
        }
    }

    private string BuildStatus()
    {
        bool lockActive = MaintenanceLock.IsActive();
        bool launcher = Process.GetProcessesByName("TurboRama.Launcher").Length > 0;
        bool recovery = File.Exists(Path.Combine(ProductPaths.State, "recovery.flag"));
        return "OK lock=" + (lockActive ? "1" : "0") +
               " launcher=" + (launcher ? "1" : "0") +
               " recovery=" + (recovery ? "1" : "0");
    }

    private string EnterMaintenance(string? reason = null)
    {
        var r = MaintenanceLock.Enter(reason ?? "manual", Environment.UserName);
        foreach (Process p in Process.GetProcessesByName("TurboRama.Launcher"))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
        }

        try
        {
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                Process.Start(explorer);
            }
        }
        catch
        {
        }

        return r.Success ? "OK ENTER_MAINTENANCE" : "ERR " + r.Message;
    }

    private string ExitMaintenance()
    {
        var r = MaintenanceLock.Exit();
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

        return r.Success ? "OK EXIT_MAINTENANCE" : "ERR " + r.Message;
    }

    private string RestartLauncher()
    {
        if (MaintenanceLock.IsActive())
        {
            return "ERR maintenance.lock ativo";
        }

        string launcher = Path.Combine(ProductPaths.AppLauncher, "TurboRama.Launcher.exe");
        if (!File.Exists(launcher))
        {
            return "ERR launcher ausente";
        }

        foreach (Process p in Process.GetProcessesByName("TurboRama.Launcher"))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
        }

        Thread.Sleep(500);
        Process.Start(new ProcessStartInfo
        {
            FileName = launcher,
            WorkingDirectory = ProductPaths.AppLauncher,
            UseShellExecute = true
        });
        return "OK RESTART_LAUNCHER";
    }

    private string ScheduleShutdown(string flag)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = flag + " /t 10 /c \"TurboRama Maintenance\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        return "OK " + (flag.Contains('r') ? "REBOOT" : "SHUTDOWN") + " em 10s";
    }
}
