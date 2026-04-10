namespace AghanimsWager.Data.Models;

public class Wager
{
    public int WagerId { get; set; }
    public long MatchId { get; set; }
    public long GamblerId { get; set; }
    public MatchOutcome Side { get; set; }
    public long Amount { get; set; }
    public bool IsAutobet { get; set; }
    public bool Finalized { get; set; }
    public DateTimeOffset PlacedAt { get; set; }

    public Match Match { get; set; } = null!;
    public GamblerAccount Gambler { get; set; } = null!;
}
