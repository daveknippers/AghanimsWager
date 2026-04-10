namespace AghanimsWager.Data.Models;

public class LivePlayer
{
    public long MatchId { get; set; }
    public int PlayerNum { get; set; }
    public long AccountId { get; set; }
    public int HeroId { get; set; }

    public LiveMatch Match { get; set; } = null!;
}
