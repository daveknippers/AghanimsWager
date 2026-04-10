namespace AghanimsWager.Data.Models;

public class LiveMatch
{
    public long LobbyId { get; set; }
    public long MatchId { get; set; }
    public long ServerSteamId { get; set; }
    public int GameMode { get; set; }
    public int GameTime { get; set; }
    public int AverageMmr { get; set; }
    public int RadiantScore { get; set; }
    public int DireScore { get; set; }
    public int RadiantLead { get; set; }
    public long BuildingState { get; set; }
    public int Spectators { get; set; }
    public double LastUpdateTime { get; set; }
    public DateTimeOffset LastSeen { get; set; }

    public List<LivePlayer> Players { get; set; } = [];
}
