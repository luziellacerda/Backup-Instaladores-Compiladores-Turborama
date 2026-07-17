namespace TurboRama.ArcadeTimer.Models;

public sealed class CreditData
{
    public long RemainingSeconds { get; set; }
    public long TotalCoinsAccepted { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
