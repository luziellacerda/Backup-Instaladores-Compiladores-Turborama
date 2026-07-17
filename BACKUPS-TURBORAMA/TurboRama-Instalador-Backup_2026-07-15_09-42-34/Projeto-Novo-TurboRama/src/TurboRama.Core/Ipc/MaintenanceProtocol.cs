namespace TurboRama.Core.Ipc;

/// <summary>
/// Protocolo de comandos predefinidos (sem shell genérico) — estudo §14.
/// </summary>
public static class MaintenanceProtocol
{
    public const string PipeName = "TurboRamaMaintenance";
    public const string PipePath = @"\\.\pipe\" + PipeName;

    public static class Commands
    {
        public const string Ping = "PING";
        public const string Status = "STATUS";
        public const string EnterMaintenance = "ENTER_MAINTENANCE";
        public const string ExitMaintenance = "EXIT_MAINTENANCE";
        public const string RestartLauncher = "RESTART_LAUNCHER";
        public const string Reboot = "REBOOT";
        public const string Shutdown = "SHUTDOWN";
        public const string StopWatchdogRestarts = "STOP_WATCHDOG_RESTARTS";
        public const string AllowWatchdogRestarts = "ALLOW_WATCHDOG_RESTARTS";
    }

    public static bool IsAllowed(string command)
    {
        string c = (command ?? string.Empty).Trim().ToUpperInvariant();
        return c is Commands.Ping
            or Commands.Status
            or Commands.EnterMaintenance
            or Commands.ExitMaintenance
            or Commands.RestartLauncher
            or Commands.Reboot
            or Commands.Shutdown
            or Commands.StopWatchdogRestarts
            or Commands.AllowWatchdogRestarts;
    }
}
