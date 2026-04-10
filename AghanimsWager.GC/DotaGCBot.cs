using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SteamKit2;
using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using SteamKit2.Internal;
using AghanimsWager.Data;
using AghanimsWager.Data.Models;
using GCHello = SteamKit2.GC.Dota.Internal.CMsgClientHello;

namespace AghanimsWager.GC;

public class DotaGCBot
{
    const uint DotaAppId = 570;
    const int PollIntervalSeconds = 15;

    readonly string _username;
    readonly string _password;
    readonly DbContextOptions<WagerContext> _dbOptions;

    readonly SteamClient _steamClient;
    readonly CallbackManager _callbackManager;
    readonly SteamUser _steamUser;
    readonly SteamFriends _steamFriends;
    readonly SteamGameCoordinator _gc;

    bool _isRunning;
    bool _gcReady;
    bool _pollPending;
    DateTimeOffset _gcReadyTime;

    // Active lobbies we're tracking (from friend rich presence)
    readonly ConcurrentDictionary<ulong, ulong?> _friendGameIds = new();
    HashSet<ulong> _activeLobbies = [];

    // Track which matches have had players recorded
    readonly ConcurrentDictionary<long, bool> _matchPlayersRecorded = new();

    public DotaGCBot(string username, string password, DbContextOptions<WagerContext> dbOptions)
    {
        _username = username;
        _password = password;
        _dbOptions = dbOptions;

        _steamClient = new SteamClient();
        _callbackManager = new CallbackManager(_steamClient);

        _steamUser = _steamClient.GetHandler<SteamUser>()!;
        _steamFriends = _steamClient.GetHandler<SteamFriends>()!;
        _gc = _steamClient.GetHandler<SteamGameCoordinator>()!;

        // Register custom handler for raw rich presence parsing
        _steamClient.AddHandler(new RichPresenceHandler(this));

        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _callbackManager.Subscribe<SteamGameCoordinator.MessageCallback>(OnGCMessage);
        _callbackManager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _isRunning = true;
        _steamClient.Connect();

        Log("Connecting to Steam...");

        // Main loop: process callbacks and poll lobbies
        var lastPollTime = DateTimeOffset.MinValue;
        while (_isRunning && !ct.IsCancellationRequested)
        {
            _callbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));

            if (_gcReady)
            {
                await ProcessPendingFriendRequests();

                await CleanStaleLiveMatches();

                var elapsed = (DateTimeOffset.UtcNow - lastPollTime).TotalSeconds;
                if (!_pollPending && elapsed >= PollIntervalSeconds)
                {
                    await PollLobbiesAsync();
                    lastPollTime = DateTimeOffset.UtcNow;
                }
            }
        }
    }

    void OnConnected(SteamClient.ConnectedCallback cb)
    {
        Log("Connected. Logging in...");
        _steamUser.LogOn(new SteamUser.LogOnDetails
        {
            Username = _username,
            Password = _password,
        });
    }

    void OnDisconnected(SteamClient.DisconnectedCallback cb)
    {
        Log("Disconnected. Reconnecting in 5 seconds...");
        _gcReady = false;


        Task.Run(async () =>
        {
            await Task.Delay(5000);
            if (_isRunning)
                _steamClient.Connect();
        });
    }

    void OnLoggedOn(SteamUser.LoggedOnCallback cb)
    {
        if (cb.Result != EResult.OK)
        {
            Log($"Login failed: {cb.Result}");
            _isRunning = false;
            return;
        }

        Log("Logged in. Launching Dota 2...");


        _steamFriends.SetPersonaState(EPersonaState.Online);

        // Tell Steam we're playing Dota 2
        var playGame = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
        playGame.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = DotaAppId });
        _steamClient.Send(playGame);

        // Send GC hello after a short delay
        Task.Run(async () =>
        {
            await Task.Delay(2000);
            SendGCHello();
        });
    }

    void SendGCHello()
    {
        var hello = new ClientGCMsgProtobuf<GCHello>((uint)EGCBaseClientMsg.k_EMsgGCClientHello);
        hello.Body.engine = ESourceEngine.k_ESE_Source2;
        _gc.Send(hello, DotaAppId);
        Log("Sent GC Hello");
    }

    void OnGCMessage(SteamGameCoordinator.MessageCallback cb)
    {
        var msgType = cb.EMsg;

        // Welcome = GC is ready
        if (msgType == (uint)EGCBaseClientMsg.k_EMsgGCClientWelcome)
        {
            Log("GC connection established");
            _gcReady = true;
            _gcReadyTime = DateTimeOffset.UtcNow;
            SyncFriends();
            ResolveUnresolvedMatches();
            return;
        }

        // SourceTV response with live match data
        if (msgType == (uint)EDOTAGCMsg.k_EMsgGCToClientFindTopSourceTVGamesResponse)
        {
            _pollPending = false;
            var response = new ClientGCMsgProtobuf<CMsgGCToClientFindTopSourceTVGamesResponse>(cb.Message);
            ProcessSourceTVResponse(response.Body);
            return;
        }

        // Match details response (for post-game data)
        if (msgType == (uint)EDOTAGCMsg.k_EMsgGCMatchDetailsResponse)
        {
            var response = new ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>(cb.Message);
            ProcessMatchDetailsResponse(response.Body);
            return;
        }
    }

    void OnPersonaState(SteamFriends.PersonaStateCallback cb)
    {
        var steamId = cb.FriendID.ConvertToUInt64();

        var friendName = _steamFriends.GetFriendPersonaName(cb.FriendID);

        // Not playing Dota 2 — clear tracking
        // Ignore transient "left" events during startup (persona state flaps on connect)
        if (cb.GameID.AppID != DotaAppId)
        {
            var sinceReady = (DateTimeOffset.UtcNow - _gcReadyTime).TotalSeconds;
            if (sinceReady < 30 && _friendGameIds.TryGetValue(steamId, out var cur) && cur.HasValue)
            {
                Log($"{friendName} transient leave ignored (startup)");
                return;
            }

            if (_friendGameIds.TryGetValue(steamId, out var prev) && prev.HasValue)
                Log($"{friendName} left Dota 2");
            _friendGameIds[steamId] = null;
            _ = UpdateActiveLobbies();
            return;
        }

        // Log when a friend launches Dota (only if not already tracked)
        if (!_friendGameIds.ContainsKey(steamId))
            Log($"{friendName} launched Dota 2");

        // Ensure they're in the dictionary even if not in a match
        _friendGameIds.TryAdd(steamId, null);
    }

    /// <summary>
    /// Custom handler that intercepts raw ClientPersonaState messages to extract rich presence
    /// data (WatchableGameID, status, param0) which isn't exposed by SteamKit2's high-level API.
    /// </summary>
    sealed class RichPresenceHandler : ClientMsgHandler
    {
        readonly DotaGCBot _bot;

        public RichPresenceHandler(DotaGCBot bot) => _bot = bot;

        public override void HandleMsg(IPacketMsg packetMsg)
        {
            if (packetMsg.MsgType != EMsg.ClientPersonaState)
                return;

            var msg = new ClientMsgProtobuf<CMsgClientPersonaState>(packetMsg);

            foreach (var friend in msg.Body.friends)
            {
                var steamId = friend.friendid;

                if (friend.gameid != DotaAppId)
                {
                    _bot._friendGameIds[steamId] = null;
                    continue;
                }

                if (friend.rich_presence.Count == 0)
                {
                    _bot._friendGameIds[steamId] = null;
                    continue;
                }

                string? status = null;
                string? param0 = null;
                ulong? watchableGameId = null;

                foreach (var rp in friend.rich_presence)
                {
                    switch (rp.key)
                    {
                        case "status":
                            status = rp.value;
                            break;
                        case "param0":
                            param0 = rp.value;
                            break;
                        case "WatchableGameID":
                            if (ulong.TryParse(rp.value, out var gid))
                                watchableGameId = gid;
                            break;
                    }
                }

                if (status == null || param0 == null || watchableGameId is null or 0)
                {
                    _bot._friendGameIds[steamId] = null;
                    continue;
                }

                // Filter out game modes we don't care about
                if (IgnoredModes.Contains(param0))
                {
                    _bot._friendGameIds[steamId] = null;
                    continue;
                }

                if (TrackStatuses.Contains(status))
                {
                    var wasTracked = _bot._friendGameIds.TryGetValue(steamId, out var prev) && prev.HasValue;
                    _bot._friendGameIds[steamId] = watchableGameId;
                    if (!wasTracked)
                    {
                        var name = _bot._steamFriends.GetFriendPersonaName(new SteamID(steamId));
                        Log($"{name} is in a match (lobby {watchableGameId}, {status})");
                    }
                }
                else
                {
                    _bot._friendGameIds[steamId] = null;
                }
            }

            _ = _bot.UpdateActiveLobbies();
        }

        static readonly HashSet<string> IgnoredModes =
        [
            "#DOTA_lobby_type_name_lobby",
            "#game_mode_18",
            "#game_mode_lobby_name_20",
            "#game_mode_lobby_name_7",
            "#game_mode_lobby_name_8",
            "#game_mode_lobby_name_9",
            "#game_mode_lobby_name_11",
        ];

        static readonly HashSet<string> TrackStatuses =
        [
            "#DOTA_RP_PLAYING_AS",
            "#DOTA_RP_HERO_SELECTION",
            "#DOTA_RP_STRATEGY_TIME",
        ];
    }

    async Task UpdateActiveLobbies()
    {
        var newLobbies = _friendGameIds.Values
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToHashSet();

        var added = newLobbies.Except(_activeLobbies).ToList();
        var removed = _activeLobbies.Except(newLobbies).ToList();

        foreach (var lobby in added)
            Log($"Tracking lobby {lobby}");

        if (removed.Count > 0)
        {
            try
            {
                await using var db = new WagerContext(_dbOptions);
                foreach (var lobby in removed)
                {
                    Log($"Stopped tracking lobby {lobby}");
                    var liveMatch = await db.LiveMatches
                        .FirstOrDefaultAsync(m => m.LobbyId == (long)lobby);
                    if (liveMatch != null)
                    {
                        var match = await db.Matches.FindAsync(liveMatch.MatchId);
                        if (match == null || match.Outcome == MatchOutcome.Unresolved)
                        {
                            Log($"Lobby {lobby} gone — requesting details for match {liveMatch.MatchId}");
                            RequestMatchDetails(liveMatch.MatchId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error resolving removed lobbies: {ex.Message}");
            }
        }

        _activeLobbies = newLobbies;
    }

    async Task ProcessPendingFriendRequests()
    {
        await using var db = new WagerContext(_dbOptions);
        var pending = await db.PendingFriendRequests.ToListAsync();
        if (pending.Count == 0) return;

        foreach (var req in pending)
        {
            _steamFriends.AddFriend(new SteamID((ulong)req.SteamId));
            Log($"Sent friend request to {req.SteamId}");
        }

        db.PendingFriendRequests.RemoveRange(pending);
        await db.SaveChangesAsync();
    }

    async Task CleanStaleLiveMatches()
    {
        await using var db = new WagerContext(_dbOptions);
        var allLiveMatches = await db.LiveMatches.ToListAsync();
        if (allLiveMatches.Count == 0) return;

        var staleThreshold = DateTimeOffset.UtcNow.AddMinutes(-2);
        var staleMatches = allLiveMatches
            .Where(m => m.LastSeen < staleThreshold)
            .ToList();

        if (staleMatches.Count == 0) return;

        foreach (var stale in staleMatches)
        {
            var stalePlayers = await db.LivePlayers
                .Where(p => p.MatchId == stale.MatchId)
                .ToListAsync();
            db.LivePlayers.RemoveRange(stalePlayers);

            var match = await db.Matches.FindAsync(stale.MatchId);
            if (match == null || match.Outcome == MatchOutcome.Unresolved)
            {
                RequestMatchDetails(stale.MatchId);
                Log($"Match {stale.MatchId} stale for 2+ min — requesting details");
            }
            else
            {
                Log($"Cleaned up stale live data for resolved match {stale.MatchId}");
            }
        }
        db.LiveMatches.RemoveRange(staleMatches);
        await db.SaveChangesAsync();
    }

    Task PollLobbiesAsync()
    {
        if (_activeLobbies.Count == 0)
        {
            StatusLine($"[{DateTime.Now:HH:mm:ss}] Idle, no active lobbies");
            return Task.CompletedTask;
        }

        StatusLine($"[{DateTime.Now:HH:mm:ss}] Polling {_activeLobbies.Count} lobby(s)");

        // Request live match data from GC
        var request = new ClientGCMsgProtobuf<CMsgClientToGCFindTopSourceTVGames>(
            (uint)EDOTAGCMsg.k_EMsgClientToGCFindTopSourceTVGames);

        foreach (var lobbyId in _activeLobbies)
            request.Body.lobby_ids.Add(lobbyId);

        _pollPending = true;
        _gc.Send(request, DotaAppId);
        return Task.CompletedTask;
    }

    void ProcessSourceTVResponse(CMsgGCToClientFindTopSourceTVGamesResponse response)
    {
        if (!response.specific_games)
            return;

        StatusLine($"[{DateTime.Now:HH:mm:ss}] Polling {_activeLobbies.Count} lobby(s), {response.game_list.Count} live match(es)");

        Task.Run(async () =>
        {
            try
            {
            await using var db = new WagerContext(_dbOptions);
            var now = DateTimeOffset.UtcNow;

            foreach (var game in response.game_list)
            {
                var existing = await db.LiveMatches
                    .FirstOrDefaultAsync(m => m.LobbyId == (long)game.lobby_id);

                if (existing != null)
                {
                    existing.GameTime = game.game_time;
                    existing.RadiantScore = (int)game.radiant_score;
                    existing.DireScore = (int)game.dire_score;
                    existing.RadiantLead = game.radiant_lead;
                    existing.BuildingState = (long)game.building_state;
                    existing.Spectators = (int)game.spectators;
                    existing.LastUpdateTime = game.last_update_time;
                    existing.LastSeen = now;
                }
                else
                {
                    var match = new LiveMatch
                    {
                        LobbyId = (long)game.lobby_id,
                        MatchId = (long)game.match_id,
                        ServerSteamId = (long)game.server_steam_id,
                        GameMode = (int)game.game_mode,
                        GameTime = game.game_time,
                        AverageMmr = (int)game.average_mmr,
                        RadiantScore = (int)game.radiant_score,
                        DireScore = (int)game.dire_score,
                        RadiantLead = game.radiant_lead,
                        BuildingState = (long)game.building_state,
                        Spectators = (int)game.spectators,
                        LastUpdateTime = game.last_update_time,
                        LastSeen = now,
                    };
                    db.LiveMatches.Add(match);
                }

                // Record players (once per match, update when heroes are picked)
                var matchId = (long)game.match_id;
                var allHeroesPicked = game.players.All(p => p.hero_id != 0);
                var alreadyRecorded = _matchPlayersRecorded.TryGetValue(matchId, out var recorded) && recorded;

                if (!alreadyRecorded || allHeroesPicked)
                {
                    // Remove old player records for this match
                    var oldPlayers = await db.LivePlayers
                        .Where(p => p.MatchId == matchId)
                        .ToListAsync();
                    db.LivePlayers.RemoveRange(oldPlayers);

                    for (int i = 0; i < game.players.Count; i++)
                    {
                        db.LivePlayers.Add(new LivePlayer
                        {
                            MatchId = matchId,
                            PlayerNum = i,
                            AccountId = (long)game.players[i].account_id,
                            HeroId = (int)game.players[i].hero_id,
                        });
                    }

                    // Mark as fully recorded once all heroes are picked
                    if (allHeroesPicked)
                        _matchPlayersRecorded[matchId] = true;
                }
            }

            await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Log($"SourceTV processing error: {ex}");
            }
        });
    }

    void ProcessMatchDetailsResponse(CMsgGCMatchDetailsResponse response)
    {
        if (response.result != (uint)EResult.OK || response.match == null)
        {
            Log($"Match details request failed: {response.result}");
            return;
        }

        var match = response.match;
        Log($"Got match details for {match.match_id}, outcome: {match.match_outcome}");

        // Store match outcome and player stats
        Task.Run(async () =>
        {
            try
            {
                await using var db = new WagerContext(_dbOptions);
                var existing = await db.Matches.FindAsync((long)match.match_id);
                if (existing == null) return;

                if (existing.Outcome == MatchOutcome.Unresolved)
                {
                    existing.Outcome = match.match_outcome switch
                    {
                        EMatchOutcome.k_EMatchOutcome_RadVictory => MatchOutcome.Radiant,
                        EMatchOutcome.k_EMatchOutcome_DireVictory => MatchOutcome.Dire,
                        _ => MatchOutcome.Error,
                    };
                    existing.ResolvedAt = DateTimeOffset.UtcNow;
                }

                // Always update match-level details
                existing.Duration = (int)match.duration;
                existing.GameMode = (int)match.game_mode;
                existing.LobbyType = (int)match.lobby_type;
                existing.StartTime = match.starttime;
                existing.RadiantScore = (int)match.radiant_team_score;
                existing.DireScore = (int)match.dire_team_score;
                existing.FirstBloodTime = (int)match.first_blood_time;
                existing.Cluster = (int)match.cluster;
                existing.ReplaySalt = (int)match.replay_salt;

                // Save player data if not already present
                var hasPlayers = await db.MatchPlayers.AnyAsync(p => p.MatchId == (long)match.match_id);
                if (!hasPlayers)
                {
                    foreach (var p in match.players)
                    {
                        db.MatchPlayers.Add(new MatchPlayer
                        {
                            MatchId = (long)match.match_id,
                            PlayerSlot = (int)p.player_slot,
                            AccountId = (long)p.account_id,
                            HeroId = p.hero_id,
                            TeamNumber = (int)p.team_number,
                            Kills = (int)p.kills,
                            Deaths = (int)p.deaths,
                            Assists = (int)p.assists,
                            HeroDamage = (int)p.hero_damage,
                            TowerDamage = (int)p.tower_damage,
                            HeroHealing = (int)p.hero_healing,
                            LastHits = (int)p.last_hits,
                            Denies = (int)p.denies,
                            GoldPerMin = (int)p.gold_per_min,
                            XpPerMin = (int)p.xp_per_min,
                            Gold = (int)p.gold,
                            GoldSpent = (int)p.gold_spent,
                            GoldLostToDeath = (int)p.gold_lost_to_death,
                            NetWorth = (int)p.net_worth,
                            SupportGold = (int)p.support_gold,
                            ClaimedFarmGold = (int)p.claimed_farm_gold,
                            Level = (int)p.level,
                            Item0 = p.item_0,
                            Item1 = p.item_1,
                            Item2 = p.item_2,
                            Item3 = p.item_3,
                            Item4 = p.item_4,
                            Item5 = p.item_5,
                            Item6 = p.item_6,
                            Item7 = p.item_7,
                            Item8 = p.item_8,
                            Item9 = p.item_9,
                            Item10 = p.item_10,
                            HeroPickOrder = (int)p.hero_pick_order,
                            SelectedFacet = (int)p.selected_facet,
                            HeroPlayCount = (int)p.hero_play_count,
                            HeroWasRandomed = p.hero_was_randomed,
                            ScaledKills = p.scaled_kills,
                            ScaledDeaths = p.scaled_deaths,
                            ScaledAssists = p.scaled_assists,
                            ScaledHeroDamage = (int)p.scaled_hero_damage,
                            ScaledTowerDamage = (int)p.scaled_tower_damage,
                            ScaledHeroHealing = (int)p.scaled_hero_healing,
                            ScaledMetric = p.scaled_metric,
                            ExpectedTeamContribution = p.expected_team_contribution,
                            SupportAbilityValue = (int)p.support_ability_value,
                            SecondsDead = (int)p.seconds_dead,
                            BountyRunes = (int)p.bounty_runes,
                            OutpostsCaptured = (int)p.outposts_captured,
                            LeaverStatus = (int)p.leaver_status,
                            PartyId = (long)p.party_id,
                            FeedingDetected = p.feeding_detected,
                            SearchRank = (int)p.search_rank,
                            SearchRankUncertainty = (int)p.search_rank_uncertainty,
                            PreviousRank = (int)p.previous_rank,
                            RankChange = p.rank_change,
                            MmrType = (int)p.mmr_type,
                        });
                    }

                }

                await db.SaveChangesAsync();
                Log($"Match {match.match_id}: outcome={existing.Outcome}, {match.players.Count} players");
            }
            catch (Exception ex)
            {
                Log($"Match details save error: {ex.Message}");
            }
        });
    }

    void SyncFriends()
    {
        Log("Syncing friends list...");
        Task.Run(async () =>
        {
            try
            {
                await using var db = new WagerContext(_dbOptions);

                var friendIds = new List<long>();
                for (int i = 0; i < _steamFriends.GetFriendCount(); i++)
                {
                    var friend = _steamFriends.GetFriendByIndex(i);
                    friendIds.Add((long)friend.ConvertToUInt64());
                    var name = _steamFriends.GetFriendPersonaName(friend);
                    Log($"  Friend: {name} ({friend.ConvertToUInt64()})");
                }

                // Replace all friends
                await db.Database.ExecuteSqlRawAsync("DELETE FROM Friends");
                foreach (var id in friendIds)
                    db.Friends.Add(new Friend { SteamId = id });

                await db.SaveChangesAsync();
                Log($"Synced {friendIds.Count} friends");
            }
            catch (Exception ex)
            {
                Log($"Friends sync error: {ex.Message}");
            }
        });
    }

    void ResolveUnresolvedMatches()
    {
        Task.Run(async () =>
        {
            await using var db = new WagerContext(_dbOptions);
            var unresolved = await db.Matches
                .Where(m => m.Outcome == MatchOutcome.Unresolved)
                .ToListAsync();

            foreach (var match in unresolved)
            {
                RequestMatchDetails(match.MatchId);
            }

            if (unresolved.Count > 0)
                Log($"Requesting details for {unresolved.Count} unresolved match(es)");
        });
    }

    public void RequestMatchDetails(long matchId)
    {
        var request = new ClientGCMsgProtobuf<CMsgGCMatchDetailsRequest>(
            (uint)EDOTAGCMsg.k_EMsgGCMatchDetailsRequest);
        request.Body.match_id = (ulong)matchId;
        _gc.Send(request, DotaAppId);
        Log($"Requested match details for {matchId}");
    }

    static void Log(string message)
    {
        // Clear the status line before printing a real log line
        Console.Write($"\r{new string(' ', Console.WindowWidth - 1)}\r");
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [GC] {message}");
        // Reprint status line if we have one
        if (_lastStatus != null)
            Console.Write($"\r  {_lastStatus}");
    }

    static string? _lastStatus;

    static void StatusLine(string message)
    {
        _lastStatus = message;
        Console.Write($"\r  {message}{new string(' ', Math.Max(0, Console.WindowWidth - message.Length - 4))}");
    }
}
