using AghanimsWager.Data;

namespace AghanimsWager.Discord;

public class TrackedLobby
{
    public long LobbyId { get; }
    public long MatchId { get; }
    public ulong InfoMessageId { get; set; }
    public ulong AnnounceMessageId { get; set; }

    public bool BettingOpen { get; private set; } = true;
    public string GamblingStatus { get; private set; } = "Bets are open";

    DateTimeOffset? _bettingCloseTime;

    public TrackedLobby(long lobbyId, long matchId, bool bettingClosed = false)
    {
        LobbyId = lobbyId;
        MatchId = matchId;
        if (bettingClosed)
        {
            BettingOpen = false;
            GamblingStatus = "Bets are closed";
        }
    }

    /// <summary>Returns true if betting just closed this update.</summary>
    public bool UpdateGamblingWindow(bool pickPhase, int gameTime, Action<string>? log = null)
    {
        var wasBettingOpen = BettingOpen;

        if (pickPhase)
        {
            BettingOpen = true;
            GamblingStatus = "Bets are open";
        }
        else if (gameTime > Constants.MaxBetGameTime)
        {
            BettingOpen = false;
            GamblingStatus = "Bets are closed";
        }
        else
        {
            // First time leaving pick phase — start the countdown
            if (_bettingCloseTime == null)
            {
                _bettingCloseTime = DateTimeOffset.UtcNow.AddSeconds(Constants.GamblingCloseWaitSeconds);
                log?.Invoke($"Match {MatchId}: picks ended, betting closes in {Constants.GamblingCloseWaitSeconds}s");
            }

            if (DateTimeOffset.UtcNow >= _bettingCloseTime)
            {
                BettingOpen = false;
                GamblingStatus = "Bets are closed";
            }
            else
            {
                var remaining = (int)(_bettingCloseTime.Value - DateTimeOffset.UtcNow).TotalSeconds;
                BettingOpen = true;
                GamblingStatus = $"Bets closing in ~{remaining}s";
            }
        }

        var justClosed = wasBettingOpen && !BettingOpen;
        if (justClosed)
            log?.Invoke($"Match {MatchId}: betting closed");
        return justClosed;
    }
}
