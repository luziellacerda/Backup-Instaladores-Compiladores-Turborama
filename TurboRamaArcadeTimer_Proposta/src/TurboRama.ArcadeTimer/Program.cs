using System.Diagnostics;
using System.Threading;

namespace TurboRama.ArcadeTimer;

internal static class Program
{
    private const string TimerMutexName = @"Global\TurboRama.ArcadeTimer";
    private const string GuardMutexName = @"Global\TurboRama.ArcadeTimer.Guard";

    [STAThread]
    private static void Main(string[] args)
    {
        bool guardMode = args.Any(a =>
            string.Equals(a, "--guard", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/guard", StringComparison.OrdinalIgnoreCase));

        if (guardMode)
        {
            RunGuard();
            return;
        }

        using var mutex = new Mutex(true, TimerMutexName, out bool createdNew);

        if (!createdNew)
        {
            // Kiosk: sair em silencio (sem dialogo modal na 2a instancia).
            return;
        }

        ApplicationConfiguration.Initialize();

        Application.ThreadException += (_, e) =>
            LogService.Write("Erro de interface", e.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                LogService.Write("Erro não tratado", ex);
        };

        try
        {
            Application.Run(new TimerForm());
        }
        catch (Exception ex)
        {
            LogService.Write("Falha fatal na aplicação", ex);
        }
    }

    /// <summary>
    /// Keep-alive do próprio Timer (não mexe no TurboRama).
    /// Garante uma única instância do Timer e relança se morrer.
    /// </summary>
    private static void RunGuard()
    {
        using var guardMutex = new Mutex(true, GuardMutexName, out bool createdNew);
        if (!createdNew)
            return;

        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;

        string workDir = AppContext.BaseDirectory;

        while (true)
        {
            try
            {
                if (!IsTimerMutexHeld())
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = workDir,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                try { LogService.Write("Guard: falha ao relançar Timer", ex); } catch { }
            }

            Thread.Sleep(4000);
        }
    }

    private static bool IsTimerMutexHeld()
    {
        try
        {
            using var existing = Mutex.OpenExisting(TimerMutexName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Existe mas sem permissão de abrir → assumir vivo.
            return true;
        }
        catch
        {
            return false;
        }
    }
}
