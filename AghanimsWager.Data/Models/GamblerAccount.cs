namespace AghanimsWager.Data.Models;

public class GamblerAccount
{
    public long DiscordId { get; set; }
    public long Tokens { get; set; }
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public DiscordMapping? DiscordMapping { get; set; }
    public List<Wager> Wagers { get; set; } = [];
}
