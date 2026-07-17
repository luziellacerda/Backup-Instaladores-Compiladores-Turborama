namespace TurboRama.ArcadeTimer.Models;

public enum TimerState
{
    Initializing,
    NoCredit,
    CreditAvailable,
    Playing,
    Warning,
    Ending,
    Error
}
