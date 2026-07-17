using System.IO.Pipes;
using System.Text;
using TurboRama.Core.Results;

namespace TurboRama.Core.Ipc;

/// <summary>
/// Cliente named pipe para o serviço Maintenance.
/// Timeout rígido: nunca bloqueia a UI por mais do que timeoutMs + margem.
/// </summary>
public static class MaintenanceClient
{
    public static OperationResult Send(string command, int timeoutMs = 3000)
    {
        if (!MaintenanceProtocol.IsAllowed(command))
        {
            return OperationResult.Fail("Comando não permitido: " + command, "IPC_CMD", "MaintenanceClient");
        }

        // Outer hard timeout: se Connect/Read/Dispose travar, abandonamos a thread e devolvemos falha.
        int hardMs = Math.Max(500, timeoutMs) + 1200;
        try
        {
            var work = Task.Run(() => SendCore(command, timeoutMs));
            if (!work.Wait(hardMs))
            {
                return OperationResult.Fail(
                    "Timeout global no pipe Maintenance (" + hardMs + "ms). Serviço pode estar ocupado — tente de novo.",
                    "IPC_HARD_TIMEOUT",
                    "MaintenanceClient");
            }

            return work.Result;
        }
        catch (AggregateException aex)
        {
            Exception ex = aex.InnerException ?? aex;
            return OperationResult.Fail("IPC: " + ex.Message, "IPC_EX", "MaintenanceClient", exception: ex);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("IPC: " + ex.Message, "IPC_EX", "MaintenanceClient", exception: ex);
        }
    }

    private static OperationResult SendCore(string command, int timeoutMs)
    {
        NamedPipeClientStream? pipe = null;
        try
        {
            pipe = new NamedPipeClientStream(
                ".",
                MaintenanceProtocol.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            // ConnectAsync com Wait — se estourar, abandona o pipe (Dispose com connect pendente trava).
            Task connectTask = pipe.ConnectAsync(timeoutMs);
            if (!connectTask.Wait(timeoutMs + 300))
            {
                pipe = null;
                return OperationResult.Fail(
                    "Timeout ao conectar no serviço Maintenance (" + timeoutMs + "ms). O serviço está RUNNING?",
                    "IPC_CONNECT",
                    "MaintenanceClient");
            }

            // Propaga falha de connect (ex.: pipe inexistente)
            connectTask.GetAwaiter().GetResult();

            if (!pipe.IsConnected)
            {
                return OperationResult.Fail("Não conectou ao pipe Maintenance.", "IPC_CONN", "MaintenanceClient");
            }

            // Write linha (UTF-8 + \n)
            byte[] payload = Encoding.UTF8.GetBytes(command.Trim().ToUpperInvariant() + "\n");
            pipe.Write(payload, 0, payload.Length);
            pipe.Flush();

            // Read com timeout via buffer assíncrono
            byte[] buffer = new byte[4096];
            Task<int> readTask = pipe.ReadAsync(buffer, 0, buffer.Length);
            if (!readTask.Wait(timeoutMs))
            {
                // Abandona pipe — Dispose com read pendente pode travar para sempre.
                pipe = null;
                return OperationResult.Fail(
                    "Timeout aguardando resposta do Maintenance (" + timeoutMs + "ms).",
                    "IPC_READ",
                    "MaintenanceClient");
            }

            int n = readTask.Result;
            if (n <= 0)
            {
                return OperationResult.Fail("Sem resposta do serviço Maintenance.", "IPC_EMPTY", "MaintenanceClient");
            }

            string response = Encoding.UTF8.GetString(buffer, 0, n)
                .Trim()
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(response))
            {
                return OperationResult.Fail("Sem resposta do serviço Maintenance.", "IPC_EMPTY", "MaintenanceClient");
            }

            if (response.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Ok(response, "MaintenanceClient.Send", currentState: response);
            }

            return OperationResult.Fail(response, "IPC_ERR", "MaintenanceClient", currentState: response);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("IPC: " + ex.Message, "IPC_EX", "MaintenanceClient", exception: ex);
        }
        finally
        {
            if (pipe is not null)
            {
                try { pipe.Dispose(); } catch { /* ignore dispose races */ }
            }
        }
    }
}
