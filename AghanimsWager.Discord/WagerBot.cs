using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AghanimsWager.Data;
using AghanimsWager.Data.Models;

namespace AghanimsWager.Discord;

public class WagerBot
{
    readonly string _token;
    readonly string _steamApiKey;
    readonly DbContextOptions<WagerContext> _dbOptions;
    readonly DiscordSocketClient _client;
    readonly InteractionService _interactionService;
    readonly IServiceProvider _services;
    readonly HttpClient _http = new();
    readonly Dictionary<int, string> _heroNames = new();

    public long SuperuserId { get; }
    public bool AllowOpposingBets { get; }

    SocketTextChannel? _infoChannel;
    SocketTextChannel? _bettingChannel;

    // Lobby tracking state
    readonly ConcurrentDictionary<long, TrackedLobby> _trackedLobbies = new();

    // Matches waiting for GC to resolve (matchId -> first seen time)
    readonly ConcurrentDictionary<long, DateTimeOffset> _pendingResolution = new();

    // Tip cooldowns (discordId -> last tip time)
    readonly ConcurrentDictionary<long, DateTimeOffset> _tipCooldowns = new();

    public WagerBot(string token, string steamApiKey, long superuserId, bool allowOpposingBets, DbContextOptions<WagerContext> dbOptions)
    {
        _token = token;
        _steamApiKey = steamApiKey;
        SuperuserId = superuserId;
        AllowOpposingBets = allowOpposingBets;
        _dbOptions = dbOptions;

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
        };
        _client = new DiscordSocketClient(config);
        _interactionService = new InteractionService(_client);

        _services = new ServiceCollection()
            .AddSingleton(this)
            .BuildServiceProvider();

        _client.Ready += OnReady;
        _client.InteractionCreated += OnInteractionCreated;
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

    async Task OnReady()
    {
        Log("Bot ready");

        await _interactionService.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);
        await _client.Rest.DeleteAllGlobalCommandsAsync();
        foreach (var guild in _client.Guilds)
            await _interactionService.RegisterCommandsToGuildAsync(guild.Id);
        Log("Slash commands registered");

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
    }

    async Task OnInteractionCreated(SocketInteraction interaction)
    {
        var ctx = new SocketInteractionContext(_client, interaction);
        var result = await _interactionService.ExecuteCommandAsync(ctx, _services);
        if (!result.IsSuccess)
        {
            Log($"Command error: {result.ErrorReason}");
            if (!interaction.HasResponded)
                await interaction.RespondAsync($"Something broke: {result.ErrorReason}");
        }
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
                var betHint = tracked.BettingOpen ? $"\n/bet {match.MatchId} radiant/dire amount" : "";
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
                name = user?.GlobalName ?? user?.Username ?? gamblerId.ToString();
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

        // Post full scoreboard to info channel
        if (_infoChannel != null)
        {
            var scoreboard = await BuildMatchScoreboard(match.MatchId);
            if (scoreboard != null)
                await SendLong(_infoChannel, scoreboard);
        }
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
        }
    }

    public async Task<long?> ResolveVanityUrl(string vanityName)
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

    public WagerContext CreateDbContext() => new WagerContext(_dbOptions);

    public TrackedLobby? GetTrackedLobbyByMatchId(long matchId) =>
        _trackedLobbies.Values.FirstOrDefault(t => t.MatchId == matchId);

    public bool IsOnTipCooldown(long discordId, out int remainingMinutes)
    {
        remainingMinutes = 0;
        if (_tipCooldowns.TryGetValue(discordId, out var lastTip) &&
            DateTimeOffset.UtcNow - lastTip < TimeSpan.FromHours(1))
        {
            remainingMinutes = (int)(TimeSpan.FromHours(1) - (DateTimeOffset.UtcNow - lastTip)).TotalMinutes;
            return true;
        }
        return false;
    }

    public void RecordTip(long discordId) =>
        _tipCooldowns[discordId] = DateTimeOffset.UtcNow;

    public async Task<GamblerAccount> EnsureAccount(WagerContext db, long discordId)
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

    public string HeroName(int heroId) =>
        _heroNames.GetValueOrDefault(heroId, $"Hero {heroId}");

    async Task<string> GetDiscordName(WagerContext db, long accountId)
    {
        var mapping = await db.DiscordMappings
            .FirstOrDefaultAsync(d => d.AccountId == accountId);
        return mapping?.DiscordName ?? "";
    }

    public async Task<string?> BuildMatchScoreboard(long matchId)
    {
        await using var db = new WagerContext(_dbOptions);

        var match = await db.Matches.FindAsync(matchId);
        if (match == null || match.Duration == 0) return null;

        var players = await db.MatchPlayers
            .Where(p => p.MatchId == matchId)
            .OrderBy(p => p.PlayerSlot)
            .ToListAsync();

        if (players.Count == 0) return null;

        var duration = TimeSpan.FromSeconds(match.Duration);
        var outcomeStr = match.Outcome switch
        {
            MatchOutcome.Radiant => "Radiant Victory",
            MatchOutcome.Dire => "Dire Victory",
            _ => match.Outcome.ToString(),
        };

        var lines = new List<string>
        {
            $"Match {matchId} — {outcomeStr} — {duration.Minutes}:{duration.Seconds:D2}",
            $"Score: Radiant {match.RadiantScore} - {match.DireScore} Dire",
            "",
        };

        // Map account IDs to discord names
        var accountIds = players.Select(p => p.AccountId).ToList();
        var dbMappings = await db.DiscordMappings
            .Where(d => accountIds.Contains(d.AccountId))
            .ToListAsync();
        var mappings = new Dictionary<long, string>();
        foreach (var m in dbMappings)
        {
            // Try to resolve live from guild for correct casing
            IUser? guildUser = null;
            foreach (var guild in _client.Guilds)
            {
                guildUser = guild.GetUser((ulong)m.DiscordId);
                if (guildUser != null) break;
            }
            var name = guildUser?.GlobalName ?? guildUser?.Username ?? m.DiscordName ?? "";
            mappings[m.AccountId] = name;
        }

        var heroW = players.Max(p => HeroName(p.HeroId).Length);
        heroW = Math.Max(heroW, 4);

        string FormatPlayer(MatchPlayer p)
        {
            var hero = HeroName(p.HeroId).PadRight(heroW);
            var discord = mappings.TryGetValue(p.AccountId, out var name) && !string.IsNullOrEmpty(name) ? $" | {name}" : "";
            var deadTime = TimeSpan.FromSeconds(p.SecondsDead);
            return $" {hero}  {p.Kills,3} {p.Deaths,3} {p.Assists,3} {p.LastHits,4} {p.Denies,4} {p.GoldPerMin,4} {p.XpPerMin,4} {p.NetWorth,6} {p.HeroDamage,6} {p.TowerDamage,5} {p.BountyRunes,3} {deadTime.Minutes}:{deadTime.Seconds:D2}{discord}";
        }

        var header = $" {"Hero".PadRight(heroW)}  {"K",3} {"D",3} {"A",3} {"LH",4} {"DN",4} {"GPM",4} {"XPM",4} {"NW",6} {"HD",6} {"TD",5} {"BR",3} {"Dead",5}";

        lines.Add("=== Radiant ===");
        lines.Add(header);
        foreach (var p in players.Where(p => p.TeamNumber == 0))
            lines.Add(FormatPlayer(p));

        lines.Add("");
        lines.Add("=== Dire ===");
        lines.Add(header);
        foreach (var p in players.Where(p => p.TeamNumber == 1))
            lines.Add(FormatPlayer(p));

        return string.Join('\n', lines);
    }

    public static long SteamIdToAccountId(long steamId64)
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

    public static void Log(string message)
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
