using System.Collections.Concurrent;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using AghanimsWager.Data;
using AghanimsWager.Data.Models;

namespace AghanimsWager.Discord;

public class WagerBot
{
    readonly long _superuserId;

    readonly string _token;
    readonly string _steamApiKey;
    readonly DbContextOptions<WagerContext> _dbOptions;
    readonly DiscordSocketClient _client;
    readonly HttpClient _http = new();
    readonly Dictionary<int, string> _heroNames = new();

    SocketTextChannel? _infoChannel;
    SocketTextChannel? _bettingChannel;

    // Lobby tracking state
    readonly ConcurrentDictionary<long, TrackedLobby> _trackedLobbies = new();

    // Matches waiting for GC to resolve (matchId -> first seen time)
    readonly ConcurrentDictionary<long, DateTimeOffset> _pendingResolution = new();

    // Tip cooldowns (discordId -> last tip time)
    readonly ConcurrentDictionary<long, DateTimeOffset> _tipCooldowns = new();

    public WagerBot(string token, string steamApiKey, long superuserId, DbContextOptions<WagerContext> dbOptions)
    {
        _token = token;
        _steamApiKey = steamApiKey;
        _superuserId = superuserId;
        _dbOptions = dbOptions;

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
        };
        _client = new DiscordSocketClient(config);
        _client.Ready += OnReady;
        _client.MessageReceived += OnMessageReceived;

        LoadHeroData();
    }

    void LoadHeroData()
    {
        var heroFile = Path.Combine(Directory.GetCurrentDirectory(), "heroes.json");
        if (!File.Exists(heroFile)) return;

        var json = File.ReadAllText(heroFile);
        using var doc = JsonDocument.Parse(json);
        foreach (var hero in doc.RootElement.GetProperty("heroes").EnumerateArray())
        {
            var id = hero.GetProperty("id").GetInt32();
            var name = hero.GetProperty("localized_name").GetString() ?? $"Hero {id}";
            _heroNames[id] = name;
        }
        _heroNames[0] = "...";
        Log($"Loaded {_heroNames.Count} heroes");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();

        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }

        await _client.StopAsync();
    }

    Task OnReady()
    {
        Log("Bot ready");

        foreach (var guild in _client.Guilds)
        {
            _infoChannel ??= guild.TextChannels.FirstOrDefault(c => c.Name == "aghanims-wager-info");
            _bettingChannel ??= guild.TextChannels.FirstOrDefault(c => c.Name == "aghanims-wager");
        }

        if (_bettingChannel == null)
            Log("WARNING: Could not find 'aghanims-wager' channel");

        _ = Task.Run(async () =>
        {
            await ResumeUnfinishedMatches();
            await LobbyPollLoop();
        });
        return Task.CompletedTask;
    }

    async Task ResumeUnfinishedMatches()
    {
        await using var db = new WagerContext(_dbOptions);

        var unfinished = await db.Matches
            .Where(m => m.Outcome != MatchOutcome.Unresolved)
            .Include(m => m.Wagers)
            .Where(m => m.Wagers.Any(w => !w.Finalized))
            .ToListAsync();

        foreach (var match in unfinished)
        {
            Log($"Resuming resolved match {match.MatchId} ({match.Outcome}) — processing payouts");
            await ProcessPayouts(db, match);
        }

        var unresolved = await db.Matches
            .Where(m => m.Outcome == MatchOutcome.Unresolved)
            .Include(m => m.Wagers)
            .Where(m => m.Wagers.Any())
            .ToListAsync();

        foreach (var match in unresolved)
        {
            // Check if there's a live match — if not, it's pending resolution
            var isLive = await db.LiveMatches.AnyAsync(l => l.MatchId == match.MatchId);
            if (!isLive)
            {
                Log($"Resuming unresolved match {match.MatchId} — adding to pending resolution");
                _pendingResolution[match.MatchId] = DateTimeOffset.UtcNow;
            }
        }
    }

    async Task LobbyPollLoop()
    {
        while (_client.ConnectionState == ConnectionState.Connected)
        {
            try
            {
                await CheckLobbies();
            }
            catch (Exception ex)
            {
                Log($"Poll error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    async Task CheckLobbies()
    {
        await using var db = new WagerContext(_dbOptions);

        var liveMatches = await db.LiveMatches
            .Include(m => m.Players)
            .ToListAsync();

        var currentLobbyIds = liveMatches.Select(m => m.LobbyId).ToHashSet();

        foreach (var match in liveMatches)
        {
            if (!_trackedLobbies.TryGetValue(match.LobbyId, out var tracked))
            {
                var matchRecord = await db.Matches.FindAsync(match.MatchId);
                var isResume = matchRecord != null;
                var bettingClosed = matchRecord?.BettingClosed ?? false;
                tracked = new TrackedLobby(match.LobbyId, match.MatchId, bettingClosed);
                _trackedLobbies[match.LobbyId] = tracked;

                if (isResume)
                {
                    tracked.InfoMessageId = matchRecord!.InfoMessageId;
                    tracked.AnnounceMessageId = matchRecord.AnnounceMessageId;
                    var wagerCount = await db.Wagers.CountAsync(w => w.MatchId == match.MatchId);
                    Log($"Resuming match {match.MatchId} (lobby {match.LobbyId}), {wagerCount} existing wager(s){(bettingClosed ? ", betting closed" : "")}");
                }
                else
                {
                    await InitializeMatch(db, match);
                }
            }

            await UpdateMatchDisplay(db, match, tracked);
        }

        var finishedLobbyIds = _trackedLobbies.Keys
            .Where(id => !currentLobbyIds.Contains(id))
            .ToList();

        foreach (var lobbyId in finishedLobbyIds)
        {
            var tracked = _trackedLobbies[lobbyId];
            Log($"Match {tracked.MatchId} no longer live — resolving");
            await ResolveMatch(db, tracked);
            _trackedLobbies.TryRemove(lobbyId, out _);
        }

        StatusLine($"[{DateTime.Now:HH:mm:ss}] {_trackedLobbies.Count} match(es), {_pendingResolution.Count} pending resolution(s)");

        // Retry pending match resolutions
        await CheckPendingResolutions(db);
    }

    async Task InitializeMatch(WagerContext db, LiveMatch match)
    {
        db.Matches.Add(new Match
        {
            MatchId = match.MatchId,
            LobbyId = match.LobbyId,
            Outcome = MatchOutcome.Unresolved,
            BettingClosed = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var playerAccountIds = match.Players.Select(p => p.AccountId).ToList();
        var mappings = await db.DiscordMappings
            .Where(d => playerAccountIds.Contains(d.AccountId))
            .ToListAsync();

        int newAutobets = 0;
        foreach (var mapping in mappings)
        {
            var player = match.Players.First(p => p.AccountId == mapping.AccountId);
            var side = player.PlayerNum < 5 ? MatchOutcome.Radiant : MatchOutcome.Dire;

            var account = await db.GamblerAccounts.FindAsync(mapping.DiscordId);
            if (account == null)
            {
                account = new GamblerAccount
                {
                    DiscordId = mapping.DiscordId,
                    Tokens = Constants.NewPlayerStipend,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.GamblerAccounts.Add(account);
            }

            db.Wagers.Add(new Wager
            {
                MatchId = match.MatchId,
                GamblerId = mapping.DiscordId,
                Side = side,
                Amount = Constants.AutobetAmount,
                IsAutobet = true,
                Finalized = false,
                PlacedAt = DateTimeOffset.UtcNow,
            });
            newAutobets++;
        }

        await db.SaveChangesAsync();
        Log($"New match {match.MatchId} (lobby {match.LobbyId}), {newAutobets} auto-bet(s)");
    }

    async Task UpdateMatchDisplay(WagerContext db, LiveMatch match, TrackedLobby tracked)
    {
        if (_infoChannel == null) return;

        var sortedPlayers = match.Players.OrderBy(p => p.PlayerNum).ToList();
        bool pickPhase = sortedPlayers.Any(p => p.HeroId == 0);

        var longestName = _heroNames.Values.DefaultIfEmpty("").Max(n => n.Length);
        var lines = new List<string> { $"Match ID: {match.MatchId}\n" };

        lines.Add("========= Radiant =========");
        lines.Add("---------------------------");
        for (int i = 0; i < 5 && i < sortedPlayers.Count; i++)
        {
            var p = sortedPlayers[i];
            var hero = HeroName(p.HeroId);
            var discord = await GetDiscordName(db, p.AccountId);
            lines.Add($"--  {hero.PadRight(longestName)} | {discord}");
        }
        lines.Add("---------------------------");

        lines.Add("========= Dire ============");
        lines.Add("---------------------------");
        for (int i = 5; i < 10 && i < sortedPlayers.Count; i++)
        {
            var p = sortedPlayers[i];
            var hero = HeroName(p.HeroId);
            var discord = await GetDiscordName(db, p.AccountId);
            lines.Add($"--  {hero.PadRight(longestName)} | {discord}");
        }
        lines.Add("---------------------------");

        lines.Add("");
        lines.Add($"Game Time: {match.GameTime}");
        if (match.AverageMmr > 0)
            lines.Add($"Average MMR: {match.AverageMmr}");
        lines.Add($"Radiant Lead: {match.RadiantLead}");
        lines.Add($"Radiant Score: {match.RadiantScore}");
        lines.Add($"Dire Score: {match.DireScore}");
        lines.Add($"Building State: {match.BuildingState}");

        var justClosed = tracked.UpdateGamblingWindow(pickPhase, match.GameTime, Log);
        lines.Add("");
        lines.Add(tracked.GamblingStatus);

        var content = $"```\n{string.Join('\n', lines)}\n```";

        var matchRecord = await db.Matches.FindAsync(match.MatchId);

        if (justClosed && matchRecord != null)
        {
            matchRecord.BettingClosed = true;
            await db.SaveChangesAsync();
        }

        if (tracked.InfoMessageId == 0)
        {
            var msg = await _infoChannel.SendMessageAsync(content);
            tracked.InfoMessageId = msg.Id;
            if (matchRecord != null)
            {
                matchRecord.InfoMessageId = msg.Id;
                await db.SaveChangesAsync();
            }
        }
        else
        {
            try
            {
                var msg = await _infoChannel.GetMessageAsync(tracked.InfoMessageId) as IUserMessage;
                if (msg != null && msg.Content != content)
                    await msg.ModifyAsync(m => m.Content = content);
            }
            catch (Exception ex)
            {
                Log($"Failed to update info message: {ex.Message}");
            }
        }

        // Announce message in betting channel (updates with gambling status)
        if (_bettingChannel != null)
        {
            var playerLines = new List<string>();
            var playersWithNames = match.Players
                .OrderBy(p => p.PlayerNum)
                .ToList();

            foreach (var p in playersWithNames)
            {
                var discord = await GetDiscordName(db, p.AccountId);
                if (string.IsNullOrEmpty(discord)) continue;
                var sideStr = p.PlayerNum < 5 ? "Radiant" : "Dire";
                if (pickPhase)
                    playerLines.Add($"{sideStr,-7} | {discord}");
                else
                    playerLines.Add($"{sideStr,-7} | {HeroName(p.HeroId).PadRight(longestName)} | {discord}");
            }

            if (playerLines.Count > 0)
            {
                var betHint = tracked.BettingOpen ? $"\n!bet {match.MatchId} radiant/dire amount" : "";
                var announceContent = $"```Match {match.MatchId}\n{string.Join('\n', playerLines)}\n\n{tracked.GamblingStatus}{betHint}```";

                if (tracked.AnnounceMessageId == 0)
                {
                    var announceMsg = await _bettingChannel.SendMessageAsync(announceContent);
                    tracked.AnnounceMessageId = announceMsg.Id;
                    if (matchRecord != null)
                    {
                        matchRecord.AnnounceMessageId = announceMsg.Id;
                        await db.SaveChangesAsync();
                    }
                }
                else
                {
                    try
                    {
                        var msg = await _bettingChannel.GetMessageAsync(tracked.AnnounceMessageId) as IUserMessage;
                        if (msg != null && msg.Content != announceContent)
                            await msg.ModifyAsync(m => m.Content = announceContent);
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to update announce message: {ex.Message}");
                    }
                }
            }
        }
    }

    async Task ResolveMatch(WagerContext db, TrackedLobby tracked)
    {
        var match = await db.Matches.FindAsync(tracked.MatchId);
        if (match == null || match.Outcome == MatchOutcome.Unresolved)
        {
            Log($"Match {tracked.MatchId} ended — awaiting GC result");
            _pendingResolution[tracked.MatchId] = DateTimeOffset.UtcNow;
            if (_bettingChannel != null)
                await _bettingChannel.SendMessageAsync($"```Match {tracked.MatchId} has ended. Awaiting results...```");
            return;
        }

        await ProcessPayouts(db, match);
    }

    async Task CheckPendingResolutions(WagerContext db)
    {
        if (_pendingResolution.Count == 0) return;

        var resolved = new List<long>();

        foreach (var (matchId, firstSeen) in _pendingResolution)
        {
            // Give up after 10 minutes
            if (DateTimeOffset.UtcNow - firstSeen > TimeSpan.FromMinutes(10))
            {
                Log($"Match {matchId} resolution timed out");
                resolved.Add(matchId);
                if (_bettingChannel != null)
                    await _bettingChannel.SendMessageAsync($"```Match {matchId} resolution timed out. Bets will be refunded if result never arrives.```");
                continue;
            }

            var match = await db.Matches.FindAsync(matchId);
            if (match != null && match.Outcome != MatchOutcome.Unresolved)
            {
                Log($"Match {matchId} resolved via retry: {match.Outcome}");
                await ProcessPayouts(db, match);
                resolved.Add(matchId);
            }
        }

        foreach (var id in resolved)
            _pendingResolution.TryRemove(id, out _);
    }

    async Task ProcessPayouts(WagerContext db, Match match)
    {
        if (_bettingChannel == null) return;

        var wagers = await db.Wagers
            .Where(w => w.MatchId == match.MatchId && !w.Finalized)
            .ToListAsync();

        if (wagers.Count == 0) return;

        var outcome = match.Outcome;
        var winners = new List<string>();
        var losers = new List<string>();
        var bonuses = new List<string>();

        // Cache Discord user lookups to avoid redundant API calls
        var userNameCache = new Dictionary<long, string>();
        async Task<string> GetName(long gamblerId)
        {
            if (!userNameCache.TryGetValue(gamblerId, out var name))
            {
                var user = await _client.GetUserAsync((ulong)gamblerId);
                name = user?.Username ?? gamblerId.ToString();
                userNameCache[gamblerId] = name;
            }
            return name;
        }

        if (outcome == MatchOutcome.Error)
        {
            // Refund non-autobet wagers
            foreach (var w in wagers.Where(w => !w.IsAutobet))
            {
                var account = await db.GamblerAccounts.FindAsync(w.GamblerId);
                if (account != null) account.Tokens += w.Amount;
                w.Finalized = true;

                var name = await GetName(w.GamblerId);
                winners.Add($"REFUND: {w.Amount,12} | {name}");
            }
            // Finalize autobets without refund
            foreach (var w in wagers.Where(w => w.IsAutobet))
                w.Finalized = true;

            await db.SaveChangesAsync();

            var cancelLines = new List<string> { $"Cancelled match id: {match.MatchId}" };
            cancelLines.AddRange(winners);
            await SendLong(_bettingChannel, string.Join('\n', cancelLines));
            return;
        }

        var loserPlayerCount = wagers
            .Where(w => w.IsAutobet && w.Side != outcome)
            .Count();

        var totalLosses = 0L;

        foreach (var w in wagers)
        {
            w.Finalized = true;
            var name = await GetName(w.GamblerId);

            if (w.Side == outcome)
            {
                var account = await db.GamblerAccounts.FindAsync(w.GamblerId);
                if (account == null) continue;

                if (w.IsAutobet)
                {
                    // Autobet winners get their stake back + share of losses
                    account.Tokens += w.Amount;
                    winners.Add($"WINNER: {w.Amount,12} | {name}");
                }
                else
                {
                    // Real bet winners get 2x
                    var winnings = w.Amount * 2;
                    account.Tokens += winnings;
                    winners.Add($"WINNER: {winnings,12} | {name}");

                    // Bonus from losing players
                    var bonus = (long)(0.1 * loserPlayerCount * w.Amount);
                    if (bonus > 0)
                    {
                        account.Tokens += bonus;
                        bonuses.Add($" BONUS: {bonus,12} | {name}");
                    }
                }

                // Update streak (manual bets only)
                if (!w.IsAutobet)
                {
                    account.CurrentStreak++;
                    if (account.CurrentStreak > account.BestStreak)
                        account.BestStreak = account.CurrentStreak;
                }
            }
            else
            {
                if (!w.IsAutobet)
                    totalLosses += w.Amount;

                losers.Add($" LOSER: {w.Amount,12} | {name}");

                // Reset streak (manual bets only)
                if (!w.IsAutobet)
                {
                    var account = await db.GamblerAccounts.FindAsync(w.GamblerId);
                    if (account != null)
                        account.CurrentStreak = 0;
                }
            }
        }

        // Autobet winners also get a cut of total losses
        if (totalLosses > 0)
        {
            foreach (var w in wagers.Where(w => w.IsAutobet && w.Side == outcome))
            {
                var account = await db.GamblerAccounts.FindAsync(w.GamblerId);
                if (account == null) continue;
                var charityBonus = (long)(totalLosses * 0.2);
                if (charityBonus > 0)
                {
                    account.Tokens += charityBonus;
                    var name = await GetName(w.GamblerId);
                    bonuses.Add($" BONUS: {charityBonus,12} | {name}");
                }
            }
        }

        await db.SaveChangesAsync();

        var winnerStr = outcome == MatchOutcome.Radiant ? "Radiant" : "Dire";
        var resultLines = new List<string>
        {
            "********************************************",
            $"Match {match.MatchId} complete. Winner: {winnerStr}",
        };
        resultLines.AddRange(winners);
        resultLines.Add("");
        resultLines.AddRange(losers);
        resultLines.Add("");
        resultLines.AddRange(bonuses);

        Log($"Payouts for match {match.MatchId}: {winners.Count} winners, {losers.Count} losers");
        await SendLong(_bettingChannel, string.Join('\n', resultLines));
    }

    async Task OnMessageReceived(SocketMessage message)
    {
        if (message.Author.IsBot) return;

        // DM shaming
        if (message.Channel is IDMChannel)
        {
            Log($"DM from {message.Author.Username} — shaming");
            await message.Channel.SendMessageAsync(
                $"{message.Author.Mention}, ALL COMMUNICATION MUST NOW BE PUBLIC");
            return;
        }

        if (message.Channel is not SocketTextChannel textChannel) return;
        if (textChannel.Name != "aghanims-wager") return;

        var content = message.Content.Trim();
        if (!content.StartsWith('!')) return;

        var parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();

        Log($"{message.Author.Username}: {content}");

        await using var db = new WagerContext(_dbOptions);

        try
        {
        switch (command)
        {
            case "!bet":
                await HandleBet(db, message, parts);
                break;
            case "!balance":
                await HandleBalance(db, message);
                break;
            case "!leaderboard":
            case "!leaderboards":
                await HandleLeaderboard(db, message);
                break;
            case "!feederboard":
                await HandleFeederboard(db, message);
                break;
            case "!add_steam_id":
                await HandleAddSteamId(db, message, parts);
                break;
            case "!tip":
                await HandleTip(db, message, parts);
                break;
            case "!active_bets":
                await HandleActiveBets(db, message, parts);
                break;
            case "!hi":
                await message.Channel.SendMessageAsync($"{message.Author.Mention} ?");
                break;
            case "!redistribute_wealth":
                await HandleRedistributeWealth(db, message);
                break;
            case "!help":
                await HandleHelp(message);
                break;
        }
        }
        catch (Exception ex)
        {
            Log($"Command error: {ex}");
            await message.Channel.SendMessageAsync($"Something broke: {ex.Message}");
        }
    }

    async Task HandleHelp(SocketMessage message)
    {
        var help = string.Join('\n', new[]
        {
            "Aghanim's Wager — Commands",
            "",
            "!bet <match_id> <radiant/dire> <amount>",
            "    Place a bet on an active match",
            "!balance",
            "    Check your golden salt balance",
            "!active_bets [match_id]",
            "    Show active bets (optionally filter by match)",
            "!leaderboard",
            "    Top 20 richest gamblers",
            "!feederboard",
            "    The cooler leaderboard",
            "!add_steam_id <steam_id | profile_url>",
            "    Link your Steam account for auto-bets",
            "!add_steam_id --update <steam_id | profile_url | vanity_name>",
            "    Change your linked Steam account",
            "!tip @user",
            "    Send 1 golden salt to a friend",
            "!redistribute_wealth",
            "    Communism (superuser only)",
            "!help",
            "    This message",
        });
        await SendLong((ISocketMessageChannel)message.Channel, help);
    }

    async Task HandleBet(WagerContext db, SocketMessage message, string[] parts)
    {
        var mention = message.Author.Mention;

        if (parts.Length != 4)
        {
            await message.Channel.SendMessageAsync($"{mention}, try !bet match_id side amount");
            return;
        }

        if (!long.TryParse(parts[1], out var matchId))
        {
            await message.Channel.SendMessageAsync($"{mention}, match_id must be an integer");
            return;
        }

        var sideStr = parts[2].ToLowerInvariant();
        MatchOutcome side;
        if (sideStr == "radiant") side = MatchOutcome.Radiant;
        else if (sideStr == "dire") side = MatchOutcome.Dire;
        else
        {
            await message.Channel.SendMessageAsync($"{mention}, must enter side as either radiant or dire");
            return;
        }

        if (!long.TryParse(parts[3], out var amount) || amount <= 0)
        {
            await message.Channel.SendMessageAsync($"{mention}, amount must be a non-negative integer");
            return;
        }

        var discordId = (long)message.Author.Id;

        var tracked = _trackedLobbies.Values.FirstOrDefault(t => t.MatchId == matchId);
        if (tracked == null || !tracked.BettingOpen)
        {
            await message.Channel.SendMessageAsync($"{mention}, betting for {matchId} is closed");
            return;
        }

        var account = await EnsureAccount(db, discordId);

        if (account.Tokens < amount)
        {
            await message.Channel.SendMessageAsync($"{mention}, insufficient balance.");
            return;
        }

        var existingBets = await db.Wagers
            .Where(w => w.MatchId == matchId && w.GamblerId == discordId && !w.Finalized)
            .ToListAsync();

        if (existingBets.Any(b => b.Side != side))
        {
            await message.Channel.SendMessageAsync($"{mention}, cannot bet on both sides.");
            return;
        }

        account.Tokens -= amount;
        db.Wagers.Add(new Wager
        {
            MatchId = matchId,
            GamblerId = discordId,
            Side = side,
            Amount = amount,
            IsAutobet = false,
            Finalized = false,
            PlacedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var sideLog = side == MatchOutcome.Radiant ? "Radiant" : "Dire";
        Log($"Bet placed: {message.Author.Username} bet {amount} on {sideLog} for match {matchId}");

        var sideDisplay = side == MatchOutcome.Radiant ? "Radiant" : "Dire";
        await message.Channel.SendMessageAsync(
            $"{mention}, bet {amount} {Constants.Currency} on {sideDisplay} win for match id {matchId}.");
    }

    async Task HandleBalance(WagerContext db, SocketMessage message)
    {
        var discordId = (long)message.Author.Id;
        var account = await EnsureAccount(db, discordId);

        var suffix = account.Tokens == 1 ? Constants.Currency : Constants.Currency + "s";
        await message.Channel.SendMessageAsync(
            $"{message.Author.Mention}, you have {account.Tokens} {suffix}");
    }

    async Task HandleLeaderboard(WagerContext db, SocketMessage message)
    {
        var accounts = await db.GamblerAccounts
            .OrderByDescending(a => a.Tokens)
            .Take(20)
            .ToListAsync();

        if (accounts.Count == 0)
        {
            await message.Channel.SendMessageAsync("```No players yet```");
            return;
        }

        var lines = new List<string> { $"{"#",5} {"Salt",12} {"Streak",8} {"Best",6} | Name" };
        lines.Add(new string('-', 51));
        for (int i = 0; i < accounts.Count; i++)
        {
            var a = accounts[i];
            var user = await _client.GetUserAsync((ulong)a.DiscordId);
            var name = user?.Username ?? a.DiscordId.ToString();
            var streak = a.CurrentStreak > 0 ? a.CurrentStreak.ToString() : "";
            var best = a.BestStreak > 0 ? a.BestStreak.ToString() : "";
            lines.Add($"{i + 1,5} {a.Tokens,12} {streak,8} {best,6} | {name}");
        }

        await SendLong(_bettingChannel ?? (ISocketMessageChannel)message.Channel, string.Join('\n', lines));
    }

    async Task HandleFeederboard(WagerContext db, SocketMessage message)
    {
        // This would need match detail data to work properly.
        // Placeholder that acknowledges the command exists.
        await message.Channel.SendMessageAsync(
            "```The cooler leaderboard is under construction.```");
    }

    async Task HandleAddSteamId(WagerContext db, SocketMessage message, string[] parts)
    {
        var mention = message.Author.Mention;

        // Accept: !add_steam_id <input> or !add_steam_id --update <input>
        bool isUpdate = parts.Any(p => p == "--update");
        var nonFlagParts = parts.Where(p => p != "--update").ToArray();

        if (nonFlagParts.Length != 2)
        {
            await message.Channel.SendMessageAsync(
                $"{mention}, try: !add_steam_id <steam_id | profile_url>");
            return;
        }

        var input = nonFlagParts[1].Trim('<', '>').TrimEnd('/');
        long? steamId = null;

        // Raw 17-digit Steam ID
        if (input.Length == 17 && input.All(char.IsDigit))
        {
            steamId = long.Parse(input);
        }
        // https://steamcommunity.com/profiles/76561198...
        else if (input.Contains("steamcommunity.com/profiles/"))
        {
            var segment = input.Split("/profiles/", 2)[1].Split('/')[0];
            if (segment.Length == 17 && segment.All(char.IsDigit))
                steamId = long.Parse(segment);
        }
        // https://steamcommunity.com/id/vanityname
        else if (input.Contains("steamcommunity.com/id/"))
        {
            var vanity = input.Split("/id/", 2)[1].Split('/')[0];
            steamId = await ResolveVanityUrl(vanity);
        }
        // Bare vanity names not supported — use the full profile URL

        if (steamId == null)
        {
            await message.Channel.SendMessageAsync($"{mention}, couldn't resolve that to a Steam ID.");
            return;
        }

        var accountId = SteamIdToAccountId(steamId.Value);
        var discordId = (long)message.Author.Id;

        await EnsureAccount(db, discordId);

        var existing = await db.DiscordMappings.FindAsync(discordId);
        if (existing != null)
        {
            if (existing.SteamId == steamId.Value)
            {
                await message.Channel.SendMessageAsync($"{mention}, you're already linked to Steam {steamId.Value}.");
                return;
            }

            // Check for --update flag
            if (!isUpdate)
            {
                await message.Channel.SendMessageAsync(
                    $"{mention}, you're already linked to Steam {existing.SteamId}. " +
                    $"This would change it to {steamId.Value}. " +
                    $"Use `!add_steam_id --update <steam_id>` to confirm the change.");
                return;
            }

            existing.SteamId = steamId.Value;
            existing.AccountId = accountId;
            existing.DiscordName = message.Author.Username;
        }
        else
        {
            db.DiscordMappings.Add(new DiscordMapping
            {
                DiscordId = discordId,
                SteamId = steamId.Value,
                AccountId = accountId,
                DiscordName = message.Author.Username,
            });
        }

        var isFriend = await db.Friends.AnyAsync(f => f.SteamId == steamId.Value);

        if (!isFriend)
        {
            // Queue a friend request so the GC bot can add them
            if (!await db.PendingFriendRequests.AnyAsync(r => r.SteamId == steamId.Value))
            {
                db.PendingFriendRequests.Add(new PendingFriendRequest
                {
                    SteamId = steamId.Value,
                    RequestedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync();

        Log($"Linked {message.Author.Username} to Steam {steamId.Value} (account {accountId}), friend: {isFriend}");
        var response = $"Linked {mention} to Steam {steamId.Value}";
        if (!isFriend)
            response += "\nYou are not on the bot's friends list — a friend request should arrive within a minute. Accept it so the bot can track your games.";
        await message.Channel.SendMessageAsync(response);
    }

    async Task<long?> ResolveVanityUrl(string vanityName)
    {
        if (string.IsNullOrEmpty(_steamApiKey))
            return null;

        try
        {
            var url = $"http://api.steampowered.com/ISteamUser/ResolveVanityURL/v1/?key={Uri.EscapeDataString(_steamApiKey)}&vanityurl={Uri.EscapeDataString(vanityName)}";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var resp = doc.RootElement.GetProperty("response");
            if (resp.GetProperty("success").GetInt32() == 1)
                return long.Parse(resp.GetProperty("steamid").GetString()!);
        }
        catch (Exception ex)
        {
            Log($"Vanity URL resolve failed: {ex.Message}");
        }
        return null;
    }

    async Task HandleTip(WagerContext db, SocketMessage message, string[] parts)
    {
        var mention = message.Author.Mention;

        if (message.MentionedUsers.Count != 1)
        {
            await message.Channel.SendMessageAsync($"{mention}, usage: !tip @user");
            return;
        }

        var tipee = message.MentionedUsers.First();
        if (tipee.Id == message.Author.Id) return;

        var tipperId = (long)message.Author.Id;
        if (_tipCooldowns.TryGetValue(tipperId, out var lastTip) &&
            DateTimeOffset.UtcNow - lastTip < TimeSpan.FromHours(1))
        {
            var remaining = (int)(TimeSpan.FromHours(1) - (DateTimeOffset.UtcNow - lastTip)).TotalMinutes;
            await message.Channel.SendMessageAsync($"{mention}, you can tip again in {remaining} minute{(remaining == 1 ? "" : "s")}.");
            return;
        }

        var tipeeId = (long)tipee.Id;

        var tipper = await db.GamblerAccounts.FindAsync(tipperId);
        if (tipper == null || tipper.Tokens < Constants.SaltMine + 1)
        {
            await message.Channel.SendMessageAsync($"{mention}, you're too poor.");
            return;
        }

        var tipeeAccount = await db.GamblerAccounts.FindAsync(tipeeId);
        if (tipeeAccount == null)
        {
            await message.Channel.SendMessageAsync(
                $"User {tipee.Mention} isn't participating in Aghanim's Wager.");
            return;
        }

        tipper.Tokens -= 1;
        tipeeAccount.Tokens += 1;
        await db.SaveChangesAsync();

        _tipCooldowns[tipperId] = DateTimeOffset.UtcNow;
        Log($"Tip: {message.Author.Username} -> {tipee.Username}");
        await message.Channel.SendMessageAsync(
            $"{mention} sent {tipee.Mention} a salty tip.");
    }

    async Task HandleActiveBets(WagerContext db, SocketMessage message, string[] parts)
    {
        var mention = message.Author.Mention;
        IQueryable<Wager> query = db.Wagers.Where(w => !w.Finalized);

        if (parts.Length == 2 && long.TryParse(parts[1], out var filterMatchId))
            query = query.Where(w => w.MatchId == filterMatchId);

        var bets = await query.ToListAsync();
        if (bets.Count == 0)
        {
            await message.Channel.SendMessageAsync($"{mention}, there are no active bets");
            return;
        }

        var grouped = bets
            .GroupBy(b => new { b.MatchId, b.GamblerId, b.Side })
            .Select(g => new { g.Key.MatchId, g.Key.GamblerId, g.Key.Side, Amount = g.Sum(w => w.Amount) })
            .OrderBy(g => g.MatchId);

        var lines = new List<string>();
        foreach (var g in grouped)
        {
            var user = await _client.GetUserAsync((ulong)g.GamblerId);
            var name = user?.Username ?? g.GamblerId.ToString();
            var sideStr = g.Side == MatchOutcome.Radiant ? "Radiant" : "Dire";
            lines.Add($"{g.MatchId} | {sideStr,7} | {g.Amount,12} | {name}");
        }

        await SendLong((ISocketMessageChannel)message.Channel, string.Join('\n', lines));
    }

    async Task HandleRedistributeWealth(WagerContext db, SocketMessage message)
    {
        if ((long)message.Author.Id != _superuserId)
        {
            await message.Channel.SendMessageAsync($"{message.Author.Mention}, No.");
            return;
        }

        var accounts = await db.GamblerAccounts
            .Where(a => a.DiscordId > 0)
            .OrderByDescending(a => a.Tokens)
            .ToListAsync();

        if (accounts.Count == 0)
        {
            await message.Channel.SendMessageAsync("There's no users in the database");
            return;
        }

        const double taxRate = 0.05;
        var totalTokens = accounts.Sum(a => a.Tokens);
        if (totalTokens == 0)
        {
            await message.Channel.SendMessageAsync("Everyone is broke. The revolution has already won.");
            return;
        }

        var totalTax = accounts.Sum(a => (long)Math.Floor(a.Tokens * taxRate));
        var perPerson = totalTax / accounts.Count;

        // Check if redistribution would actually change anything
        var wouldChange = accounts.Any(a => (long)Math.Floor(a.Tokens * taxRate) != perPerson);
        if (!wouldChange)
        {
            await message.Channel.SendMessageAsync("The proletariat are already equal, comrade. There is nothing to redistribute.");
            return;
        }

        var remainder = totalTax - (perPerson * accounts.Count);

        foreach (var a in accounts)
        {
            var tax = (long)Math.Floor(a.Tokens * taxRate);
            a.Tokens += perPerson - tax;
        }

        // Spread leftover salt one at a time from poorest up
        var byPoorest = accounts.OrderBy(a => a.Tokens).ToList();
        for (int i = 0; i < remainder; i++)
            byPoorest[i % byPoorest.Count].Tokens++;

        await db.SaveChangesAsync();
        Log($"Wealth redistributed: {totalTax} {Constants.Currency} across {accounts.Count} accounts");
        await message.Channel.SendMessageAsync(
            $"Rejoice, My Comrades! {totalTax} {Constants.Currency} has been redistributed.");
    }

    async Task<GamblerAccount> EnsureAccount(WagerContext db, long discordId)
    {
        var account = await db.GamblerAccounts.FindAsync(discordId);
        if (account != null)
        {
            // Salt mine floor
            if (account.Tokens < Constants.SaltMine)
            {
                var hasActiveBets = await db.Wagers
                    .AnyAsync(w => w.GamblerId == discordId && !w.Finalized);
                if (!hasActiveBets)
                    account.Tokens = Constants.SaltMine;
            }
            return account;
        }

        account = new GamblerAccount
        {
            DiscordId = discordId,
            Tokens = Constants.NewPlayerStipend,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.GamblerAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    string HeroName(int heroId) =>
        _heroNames.GetValueOrDefault(heroId, $"Hero {heroId}");

    async Task<string> GetDiscordName(WagerContext db, long accountId)
    {
        var mapping = await db.DiscordMappings
            .FirstOrDefaultAsync(d => d.AccountId == accountId);
        return mapping?.DiscordName ?? "";
    }

    static long SteamIdToAccountId(long steamId64)
    {
        const long id64Base = 76561197960265728;
        return steamId64 - id64Base;
    }

    const int MaxMsgLen = 2000 - 6;

    static async Task SendLong(ISocketMessageChannel channel, string msg)
    {
        while (msg.Length > MaxMsgLen)
        {
            var chunk = msg[..MaxMsgLen];
            var lastNewline = chunk.LastIndexOf('\n');
            if (lastNewline > 0)
            {
                await channel.SendMessageAsync($"```{chunk[..lastNewline]}```");
                msg = chunk[lastNewline..] + msg[MaxMsgLen..];
            }
            else
            {
                await channel.SendMessageAsync($"```{chunk}```");
                msg = msg[MaxMsgLen..];
            }
        }
        if (msg.Length > 0)
            await channel.SendMessageAsync($"```{msg}```");
    }

    static void Log(string message)
    {
        Console.Write($"\r{new string(' ', Console.WindowWidth - 1)}\r");
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Discord] {message}");
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
