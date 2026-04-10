namespace AghanimsWager.Data.Models;

public enum MatchOutcome
{
    Unresolved = 1,
    Radiant = 2,
    Dire = 3,
    Error = 4,
}

public class Match
{
    public long MatchId { get; set; }
    public long LobbyId { get; set; }
    public MatchOutcome Outcome { get; set; } = MatchOutcome.Unresolved;
    public int Duration { get; set; }
    public int GameMode { get; set; }
    public int LobbyType { get; set; }
    public long StartTime { get; set; }
    public int RadiantScore { get; set; }
    public int DireScore { get; set; }
    public int FirstBloodTime { get; set; }
    public int Cluster { get; set; }
    public int ReplaySalt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public bool BettingClosed { get; set; } = true;

    // Discord message IDs for resuming after restart
    public ulong InfoMessageId { get; set; }
    public ulong AnnounceMessageId { get; set; }

    public List<Wager> Wagers { get; set; } = [];
    public List<MatchPlayer> Players { get; set; } = [];
}
