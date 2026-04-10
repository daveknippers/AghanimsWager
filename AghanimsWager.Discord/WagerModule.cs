using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System.Data;
using AghanimsWager.Data;
using AghanimsWager.Data.Models;

namespace AghanimsWager.Discord;

public class WagerModule : InteractionModuleBase<SocketInteractionContext>
{
    readonly WagerBot _bot;

    public WagerModule(WagerBot bot)
    {
        _bot = bot;
    }

    [SlashCommand("bet", "Place a bet on an active match")]
    public async Task Bet(
        [Summary(description: "ID of the match to bet on")] long match_id,
        [Summary(description: "radiant or dire")] string side,
        [Summary(description: "Amount of golden salt to wager")] long amount)
    {
        await using var db = _bot.CreateDbContext();
        var mention = Context.User.Mention;

        var sideStr = side.ToLowerInvariant();
        MatchOutcome betSide;
        if (sideStr == "radiant") betSide = MatchOutcome.Radiant;
        else if (sideStr == "dire") betSide = MatchOutcome.Dire;
        else
        {
            await RespondAsync($"{mention}, must enter side as either radiant or dire");
            return;
        }

        if (amount <= 0)
        {
            await RespondAsync($"{mention}, invalid bet amount");
            return;
        }

        var discordId = (long)Context.User.Id;

        var tracked = _bot.GetTrackedLobbyByMatchId(match_id);
        if (tracked == null || !tracked.BettingOpen)
        {
            await RespondAsync($"{mention}, betting for {match_id} is closed");
            return;
        }

        using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var account = await _bot.EnsureAccount(db, discordId);

            if (account.Tokens < amount)
            {
                await RespondAsync($"{mention}, insufficient balance.");
                return;
            }

            var existingBets = await db.Wagers
                .Where(w => w.MatchId == match_id && w.GamblerId == discordId && !w.Finalized)
                .ToListAsync();

            if (existingBets.Any(b => b.Side != betSide))
            {
                if (!_bot.AllowOpposingBets && existingBets.Any(b => b.IsAutobet))
                {
                    await RespondAsync($"{mention}, you can't bet against your own team.");
                    return;
                }
                await RespondAsync($"{mention}, cannot bet on both sides.");
                return;
            }

            account.Tokens -= amount;
            db.Wagers.Add(new Wager
            {
                MatchId = match_id,
                GamblerId = discordId,
                Side = betSide,
                Amount = amount,
                IsAutobet = false,
                Finalized = false,
                PlacedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            WagerBot.Log($"Bet transaction failed: {ex.Message}");
            await RespondAsync($"{mention}, bet failed — try again.");
            return;
        }

        var sideDisplay = betSide == MatchOutcome.Radiant ? "Radiant" : "Dire";
        WagerBot.Log($"Bet placed: {Context.User.Username} bet {amount} on {sideDisplay} for match {match_id}");
        await RespondAsync(
            $"{mention}, bet {amount} {Constants.Currency} on {sideDisplay} win for match id {match_id}.");
    }

    [SlashCommand("balance", "Check your golden salt balance")]
    public async Task Balance()
    {
        await using var db = _bot.CreateDbContext();
        var discordId = (long)Context.User.Id;
        var account = await _bot.EnsureAccount(db, discordId);

        var suffix = account.Tokens == 1 ? Constants.Currency : Constants.Currency + "s";
        await RespondAsync(
            $"{Context.User.Mention}, you have {account.Tokens} {suffix}");
    }

    [SlashCommand("leaderboard", "Top 20 richest gamblers")]
    public async Task Leaderboard()
    {
        await using var db = _bot.CreateDbContext();

        var accounts = await db.GamblerAccounts
            .OrderByDescending(a => a.Tokens)
            .Take(20)
            .ToListAsync();

        if (accounts.Count == 0)
        {
            await RespondAsync("No players yet.");
            return;
        }

        var lines = new List<string> { $"{"#",5} {"Salt",12} {"Streak",8} {"Best",6} | Name" };
        lines.Add(new string('-', 51));
        for (int i = 0; i < accounts.Count; i++)
        {
            var a = accounts[i];
            var user = await Context.Client.GetUserAsync((ulong)a.DiscordId);
            var name = user?.GlobalName ?? user?.Username ?? a.DiscordId.ToString();
            var streak = a.CurrentStreak > 0 ? a.CurrentStreak.ToString() : "";
            var best = a.BestStreak > 0 ? a.BestStreak.ToString() : "";
            lines.Add($"{i + 1,5} {a.Tokens,12} {streak,8} {best,6} | {name}");
        }

        await RespondAsync($"```{string.Join('\n', lines)}```");
    }

    [SlashCommand("feederboard", "The cooler leaderboard")]
    public async Task Feederboard(
        [Summary(description: "Number of recent matches (default: 20)")] int last_n_matches = 20)
    {
        var maxGames = Math.Clamp(last_n_matches, 1, 100);
        await using var db = _bot.CreateDbContext();

        // Get all linked players
        var mappings = await db.DiscordMappings.ToListAsync();
        if (mappings.Count == 0)
        {
            await RespondAsync("```No players yet```");
            return;
        }

        var entries = new List<(string Name, double AvgDeaths, int Games)>();

        foreach (var mapping in mappings)
        {
            var recentDeaths = await db.MatchPlayers
                .Where(p => p.AccountId == mapping.AccountId)
                .Join(db.Matches.Where(m => m.Outcome != MatchOutcome.Unresolved && m.Outcome != MatchOutcome.Error),
                    p => p.MatchId, m => m.MatchId, (p, m) => new { p.Deaths, m.MatchId })
                .OrderByDescending(x => x.MatchId)
                .Take(maxGames)
                .ToListAsync();

            if (recentDeaths.Count == 0) continue;

            var avg = recentDeaths.Average(x => x.Deaths);
            var user = await Context.Client.GetUserAsync((ulong)mapping.DiscordId);
            var name = user?.GlobalName ?? user?.Username ?? mapping.DiscordName ?? mapping.DiscordId.ToString();
            entries.Add((name, avg, recentDeaths.Count));
        }

        if (entries.Count == 0)
        {
            await RespondAsync("```No match data yet```");
            return;
        }

        entries.Sort((a, b) => b.AvgDeaths.CompareTo(a.AvgDeaths));

        var lines = new List<string> { $"Feederboard (last {maxGames})", "" , $"{"#",5} {"Avg Deaths",12} {"Games",7} | Name" };
        lines.Add(new string('-', 45));
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            lines.Add($"{i + 1,5} {e.AvgDeaths,12:F1} {e.Games,7} | {e.Name}");
        }

        await RespondAsync($"```{string.Join('\n', lines)}```");
    }

    [SlashCommand("add_steam_id", "Link your Steam account for auto-bets")]
    public async Task AddSteamId(
        [Summary(description: "Steam ID, profile URL, or vanity URL")] string steam_id,
        [Summary(description: "Set to true to change an existing link")] bool update = false)
    {
        await using var db = _bot.CreateDbContext();
        var mention = Context.User.Mention;

        var input = steam_id.Trim('<', '>').TrimEnd('/');
        long? steamId = null;

        if (input.Length == 17 && input.All(char.IsDigit))
        {
            steamId = long.Parse(input);
        }
        else if (input.Contains("steamcommunity.com/profiles/"))
        {
            var segment = input.Split("/profiles/", 2)[1].Split('/')[0];
            if (segment.Length == 17 && segment.All(char.IsDigit))
                steamId = long.Parse(segment);
        }
        else if (input.Contains("steamcommunity.com/id/"))
        {
            var vanity = input.Split("/id/", 2)[1].Split('/')[0];
            steamId = await _bot.ResolveVanityUrl(vanity);
        }

        if (steamId == null)
        {
            await RespondAsync($"{mention}, couldn't resolve that to a Steam ID.");
            return;
        }

        var accountId = WagerBot.SteamIdToAccountId(steamId.Value);
        var discordId = (long)Context.User.Id;

        await _bot.EnsureAccount(db, discordId);

        var existing = await db.DiscordMappings.FindAsync(discordId);
        if (existing != null)
        {
            if (existing.SteamId == steamId.Value)
            {
                await RespondAsync($"{mention}, you're already linked to Steam {steamId.Value}.");
                return;
            }

            if (!update)
            {
                await RespondAsync(
                    $"{mention}, you're already linked to Steam {existing.SteamId}. " +
                    $"This would change it to {steamId.Value}. " +
                    $"Use `/add_steam_id {steam_id} update:True` to confirm the change.");
                return;
            }

            existing.SteamId = steamId.Value;
            existing.AccountId = accountId;
            existing.DiscordName = Context.User.GlobalName ?? Context.User.Username;
        }
        else
        {
            db.DiscordMappings.Add(new DiscordMapping
            {
                DiscordId = discordId,
                SteamId = steamId.Value,
                AccountId = accountId,
                DiscordName = Context.User.GlobalName ?? Context.User.Username,
            });
        }

        var isFriend = await db.Friends.AnyAsync(f => f.SteamId == steamId.Value);

        if (!isFriend)
        {
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

        WagerBot.Log($"Linked {Context.User.Username} to Steam {steamId.Value} (account {accountId}), friend: {isFriend}");
        var response = $"Linked {mention} to Steam {steamId.Value}";
        if (!isFriend)
            response += "\nYou are not on the bot's friends list — a friend request should arrive within a minute. Accept it so the bot can track your games.";
        await RespondAsync(response);
    }

    [SlashCommand("tip", "Send 1 golden salt to a friend")]
    public async Task Tip(
        [Summary(description: "Player to send a golden salt to")] IUser user)
    {
        await using var db = _bot.CreateDbContext();
        var mention = Context.User.Mention;

        if (user.Id == Context.User.Id) return;

        var tipperId = (long)Context.User.Id;
        if (_bot.IsOnTipCooldown(tipperId, out var remaining))
        {
            await RespondAsync($"{mention}, you can tip again in {remaining} minute{(remaining == 1 ? "" : "s")}.");
            return;
        }

        var tipeeId = (long)user.Id;

        using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var tipper = await db.GamblerAccounts.FindAsync(tipperId);
            if (tipper == null || tipper.Tokens < Constants.SaltMine + 1)
            {
                await RespondAsync($"{mention}, you're too poor.");
                return;
            }

            var tipeeAccount = await db.GamblerAccounts.FindAsync(tipeeId);
            if (tipeeAccount == null)
            {
                await RespondAsync(
                    $"User {user.Mention} isn't participating in Aghanim's Wager.");
                return;
            }

            tipper.Tokens -= 1;
            tipeeAccount.Tokens += 1;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            WagerBot.Log($"Tip transaction failed: {ex.Message}");
            await RespondAsync($"{mention}, tip failed — try again.");
            return;
        }

        _bot.RecordTip(tipperId);
        WagerBot.Log($"Tip: {Context.User.Username} -> {user.Username}");
        await RespondAsync(
            $"{mention} sent {user.Mention} a salty tip.");
    }

    [SlashCommand("active_bets", "Show active bets")]
    public async Task ActiveBets(
        [Summary(description: "Filter by match ID (default: all)")] long? match_id = null)
    {
        await using var db = _bot.CreateDbContext();
        var mention = Context.User.Mention;
        IQueryable<Wager> query = db.Wagers.Where(w => !w.Finalized);

        if (match_id.HasValue)
            query = query.Where(w => w.MatchId == match_id.Value);

        var bets = await query.ToListAsync();
        if (bets.Count == 0)
        {
            await RespondAsync($"{mention}, there are no active bets");
            return;
        }

        var grouped = bets
            .GroupBy(b => new { b.MatchId, b.GamblerId, b.Side })
            .Select(g => new { g.Key.MatchId, g.Key.GamblerId, g.Key.Side, Amount = g.Sum(w => w.Amount) })
            .OrderBy(g => g.MatchId);

        var lines = new List<string>();
        foreach (var g in grouped)
        {
            var user = await Context.Client.GetUserAsync((ulong)g.GamblerId);
            var name = user?.GlobalName ?? user?.Username ?? g.GamblerId.ToString();
            var sideStr = g.Side == MatchOutcome.Radiant ? "Radiant" : "Dire";
            lines.Add($"{g.MatchId} | {sideStr,7} | {g.Amount,12} | {name}");
        }

        await RespondAsync($"```{string.Join('\n', lines)}```");
    }

    [SlashCommand("hi", "Say hi")]
    public async Task Hi()
    {
        await RespondAsync($"{Context.User.Mention} ?");
    }

    [SlashCommand("redistribute_wealth", "Communism (superuser only)")]
    public async Task RedistributeWealth()
    {
        await using var db = _bot.CreateDbContext();

        if ((long)Context.User.Id != _bot.SuperuserId)
        {
            await RespondAsync($"{Context.User.Mention}, No.");
            return;
        }

        var accounts = await db.GamblerAccounts
            .Where(a => a.DiscordId > 0)
            .OrderByDescending(a => a.Tokens)
            .ToListAsync();

        if (accounts.Count == 0)
        {
            await RespondAsync("There's no users in the database");
            return;
        }

        const double taxRate = 0.05;
        var totalTokens = accounts.Sum(a => a.Tokens);
        if (totalTokens == 0)
        {
            await RespondAsync("Everyone is broke. The revolution has already won.");
            return;
        }

        var totalTax = accounts.Sum(a => (long)Math.Floor(a.Tokens * taxRate));
        var perPerson = totalTax / accounts.Count;

        var wouldChange = accounts.Any(a => (long)Math.Floor(a.Tokens * taxRate) != perPerson);
        if (!wouldChange)
        {
            await RespondAsync("The proletariat are already equal, comrade. There is nothing to redistribute.");
            return;
        }

        var remainder = totalTax - (perPerson * accounts.Count);

        foreach (var a in accounts)
        {
            var tax = (long)Math.Floor(a.Tokens * taxRate);
            a.Tokens += perPerson - tax;
        }

        var byPoorest = accounts.OrderBy(a => a.Tokens).ToList();
        for (int i = 0; i < remainder; i++)
            byPoorest[i % byPoorest.Count].Tokens++;

        await db.SaveChangesAsync();
        WagerBot.Log($"Wealth redistributed: {totalTax} {Constants.Currency} across {accounts.Count} accounts");
        await RespondAsync(
            $"Rejoice, My Comrades! {totalTax} {Constants.Currency} has been redistributed.");
    }

    [SlashCommand("stats", "Player stats over recent matches")]
    public async Task Stats(
        [Summary(description: "Player to look up (default: yourself)")] IUser? user = null,
        [Summary(description: "Number of recent matches (default: 20)")] int last_n_matches = 20)
    {
        var maxGames = Math.Clamp(last_n_matches, 1, 100);
        var target = user ?? Context.User;
        var discordId = (long)target.Id;
        await using var db = _bot.CreateDbContext();

        var mapping = await db.DiscordMappings.FindAsync(discordId);
        if (mapping == null)
        {
            await RespondAsync($"{target.Mention} hasn't linked a Steam account.");
            return;
        }

        var recent = await db.MatchPlayers
            .Where(p => p.AccountId == mapping.AccountId)
            .Join(db.Matches.Where(m => m.Outcome != MatchOutcome.Unresolved && m.Outcome != MatchOutcome.Error),
                p => p.MatchId, m => m.MatchId, (p, m) => new { Player = p, Match = m })
            .OrderByDescending(x => x.Match.MatchId)
            .Take(maxGames)
            .ToListAsync();

        if (recent.Count == 0)
        {
            await RespondAsync($"No match data for {target.Mention}.");
            return;
        }

        var wins = recent.Count(x =>
            (x.Player.TeamNumber == 0 && x.Match.Outcome == MatchOutcome.Radiant) ||
            (x.Player.TeamNumber == 1 && x.Match.Outcome == MatchOutcome.Dire));
        var gameCount = recent.Count;
        var winRate = (double)wins / gameCount * 100;

        var avgKills = recent.Average(x => x.Player.Kills);
        var avgDeaths = recent.Average(x => x.Player.Deaths);
        var avgAssists = recent.Average(x => x.Player.Assists);
        var avgGpm = recent.Average(x => x.Player.GoldPerMin);
        var avgXpm = recent.Average(x => x.Player.XpPerMin);
        var avgHeroDmg = recent.Average(x => x.Player.HeroDamage);
        var avgTowerDmg = recent.Average(x => x.Player.TowerDamage);
        var avgLastHits = recent.Average(x => x.Player.LastHits);
        var avgNetWorth = recent.Average(x => x.Player.NetWorth);

        // Most played heroes
        var topHeroes = recent
            .GroupBy(x => x.Player.HeroId)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => $"{_bot.HeroName(g.Key)} ({g.Count()})")
            .ToList();

        var name = target.GlobalName ?? target.Username;
        var lines = new List<string>
        {
            $"{name} — last {gameCount} game{(gameCount == 1 ? "" : "s")}",
            "",
            $"{"Win Rate",-18}{$"{winRate:F0}%  ({wins}W {gameCount - wins}L)",24}",
            $"{"Avg KDA",-18}{avgKills,6:F1} / {avgDeaths,5:F1} / {avgAssists,5:F1}",
            $"{"Avg GPM",-18}{avgGpm,24:F0}",
            $"{"Avg XPM",-18}{avgXpm,24:F0}",
            $"{"Avg Last Hits",-18}{avgLastHits,24:F0}",
            $"{"Avg Net Worth",-18}{avgNetWorth,24:F0}",
            $"{"Avg Hero Dmg",-18}{avgHeroDmg,24:F0}",
            $"{"Avg Tower Dmg",-18}{avgTowerDmg,24:F0}",
            $"{"Avg Dead Time",-18}{FormatTime((int)recent.Average(x => x.Player.SecondsDead)),24}",
            $"{"Avg Bounties",-18}{recent.Average(x => x.Player.BountyRunes),24:F1}",
            "",
            $"{"Top Heroes",-15} {string.Join(", ", topHeroes)}",
        };

        await RespondAsync($"```{string.Join('\n', lines)}```");
    }

    [SlashCommand("compare", "Compare two players' stats")]
    public async Task Compare(
        [Summary(description: "First player")] IUser user1,
        [Summary(description: "Second player")] IUser user2,
        [Summary(description: "Number of recent matches (default: 20)")] int last_n_matches = 20)
    {
        var maxGames = Math.Clamp(last_n_matches, 1, 100);
        await using var db = _bot.CreateDbContext();

        async Task<List<(MatchPlayer Player, Match Match)>?> GetRecent(IUser user)
        {
            var mapping = await db.DiscordMappings.FindAsync((long)user.Id);
            if (mapping == null) return null;

            var results = await db.MatchPlayers
                .Where(p => p.AccountId == mapping.AccountId)
                .Join(db.Matches.Where(m => m.Outcome != MatchOutcome.Unresolved && m.Outcome != MatchOutcome.Error),
                    p => p.MatchId, m => m.MatchId, (p, m) => new { Player = p, Match = m })
                .OrderByDescending(x => x.Match.MatchId)
                .Take(maxGames)
                .ToListAsync();

            return results.Select(x => (x.Player, x.Match)).ToList();
        }

        var data1 = await GetRecent(user1);
        var data2 = await GetRecent(user2);

        if (data1 == null || data1.Count == 0)
        {
            await RespondAsync($"No match data for {user1.Mention}.");
            return;
        }
        if (data2 == null || data2.Count == 0)
        {
            await RespondAsync($"No match data for {user2.Mention}.");
            return;
        }

        double WinRate(List<(MatchPlayer Player, Match Match)> data)
        {
            var wins = data.Count(x =>
                (x.Player.TeamNumber == 0 && x.Match.Outcome == MatchOutcome.Radiant) ||
                (x.Player.TeamNumber == 1 && x.Match.Outcome == MatchOutcome.Dire));
            return (double)wins / data.Count * 100;
        }

        var n1 = user1.GlobalName ?? user1.Username;
        var n2 = user2.GlobalName ?? user2.Username;
        var w = Math.Max(n1.Length, n2.Length);
        w = Math.Max(w, 8);

        var lines = new List<string>
        {
            $"Comparison (last {maxGames})",
            "",
            $"{"",16} {n1.PadLeft(w)} {n2.PadLeft(w)}",
            $"{"Games",16} {data1.Count.ToString().PadLeft(w)} {data2.Count.ToString().PadLeft(w)}",
            $"{"Win Rate",16} {WinRate(data1).ToString("F0").PadLeft(w - 1)}% {WinRate(data2).ToString("F0").PadLeft(w - 1)}%",
            $"{"Avg Kills",16} {data1.Average(x => x.Player.Kills).ToString("F1").PadLeft(w)} {data2.Average(x => x.Player.Kills).ToString("F1").PadLeft(w)}",
            $"{"Avg Deaths",16} {data1.Average(x => x.Player.Deaths).ToString("F1").PadLeft(w)} {data2.Average(x => x.Player.Deaths).ToString("F1").PadLeft(w)}",
            $"{"Avg Assists",16} {data1.Average(x => x.Player.Assists).ToString("F1").PadLeft(w)} {data2.Average(x => x.Player.Assists).ToString("F1").PadLeft(w)}",
            $"{"Avg GPM",16} {data1.Average(x => x.Player.GoldPerMin).ToString("F0").PadLeft(w)} {data2.Average(x => x.Player.GoldPerMin).ToString("F0").PadLeft(w)}",
            $"{"Avg XPM",16} {data1.Average(x => x.Player.XpPerMin).ToString("F0").PadLeft(w)} {data2.Average(x => x.Player.XpPerMin).ToString("F0").PadLeft(w)}",
            $"{"Avg Last Hits",16} {data1.Average(x => x.Player.LastHits).ToString("F0").PadLeft(w)} {data2.Average(x => x.Player.LastHits).ToString("F0").PadLeft(w)}",
            $"{"Avg Net Worth",16} {data1.Average(x => x.Player.NetWorth).ToString("F0").PadLeft(w)} {data2.Average(x => x.Player.NetWorth).ToString("F0").PadLeft(w)}",
            $"{"Avg Hero Dmg",16} {data1.Average(x => x.Player.HeroDamage).ToString("F0").PadLeft(w)} {data2.Average(x => x.Player.HeroDamage).ToString("F0").PadLeft(w)}",
            $"{"Avg Tower Dmg",16} {data1.Average(x => x.Player.TowerDamage).ToString("F0").PadLeft(w)} {data2.Average(x => x.Player.TowerDamage).ToString("F0").PadLeft(w)}",
            $"{"Avg Dead Time",16} {FormatTime((int)data1.Average(x => x.Player.SecondsDead)).PadLeft(w)} {FormatTime((int)data2.Average(x => x.Player.SecondsDead)).PadLeft(w)}",
            $"{"Avg Bounties",16} {data1.Average(x => x.Player.BountyRunes).ToString("F1").PadLeft(w)} {data2.Average(x => x.Player.BountyRunes).ToString("F1").PadLeft(w)}",
        };

        await RespondAsync($"```{string.Join('\n', lines)}```");
    }

    [SlashCommand("match", "Show full scoreboard for a match")]
    public async Task MatchDetails(
        [Summary(description: "ID of the match to display")] long match_id)
    {
        await using var db = _bot.CreateDbContext();

        var match = await db.Matches.FindAsync(match_id);
        if (match == null)
        {
            await RespondAsync($"Match {match_id} not found.");
            return;
        }

        var players = await db.MatchPlayers
            .Where(p => p.MatchId == match_id)
            .OrderBy(p => p.PlayerSlot)
            .ToListAsync();

        if (players.Count == 0)
        {
            await RespondAsync($"No player data for match {match_id}.");
            return;
        }

        var duration = TimeSpan.FromSeconds(match.Duration);
        var outcomeStr = match.Outcome switch
        {
            MatchOutcome.Radiant => "Radiant Victory",
            MatchOutcome.Dire => "Dire Victory",
            _ => match.Outcome.ToString(),
        };

        var lines = new List<string>
        {
            $"Match {match_id} — {outcomeStr} — {duration.Minutes}:{duration.Seconds:D2}",
            $"Score: Radiant {match.RadiantScore} - {match.DireScore} Dire",
            "",
        };

        // Map account IDs to discord names (resolve live from guild for correct casing)
        var accountIds = players.Select(p => p.AccountId).ToList();
        var dbMappings = await db.DiscordMappings
            .Where(d => accountIds.Contains(d.AccountId))
            .ToListAsync();
        var mappings = new Dictionary<long, string>();
        foreach (var m in dbMappings)
        {
            var guildUser = Context.Guild?.GetUser((ulong)m.DiscordId);
            var name = guildUser?.GlobalName ?? guildUser?.Username ?? m.DiscordName ?? "";
            mappings[m.AccountId] = name;
        }

        var heroW = players.Max(p => _bot.HeroName(p.HeroId).Length);
        heroW = Math.Max(heroW, 4);

        string FormatPlayer(MatchPlayer p)
        {
            var hero = _bot.HeroName(p.HeroId).PadRight(heroW);
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

        await RespondAsync($"```{string.Join('\n', lines)}```");
    }

    static string FormatTime(int totalSeconds)
    {
        var t = TimeSpan.FromSeconds(totalSeconds);
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes:D2}m {t.Seconds:D2}s";
        if (t.TotalMinutes >= 1)
            return $"{t.Minutes}m {t.Seconds:D2}s";
        return $"{t.Seconds}s";
    }

    [SlashCommand("help", "List available commands")]
    public async Task Help()
    {
        var help = string.Join('\n', new[]
        {
            "Aghanim's Wager — Commands",
            "",
            "/bet <match_id> <radiant/dire> <amount>",
            "    Place a bet on an active match",
            "/balance",
            "    Check your golden salt balance",
            "/active_bets [match_id]",
            "    Show active bets (optionally filter by match)",
            "/leaderboard",
            "    Top 20 richest gamblers",
            "/feederboard",
            "    The cooler leaderboard",
            "/stats [@user]",
            "    Player stats over recent matches",
            "/compare @user1 @user2",
            "    Side-by-side stat comparison",
            "/match <match_id>",
            "    Full scoreboard for a match",
            "/add_steam_id <steam_id | profile_url>",
            "    Link your Steam account for auto-bets",
            "/add_steam_id <steam_id | profile_url> update:True",
            "    Change your linked Steam account",
            "/tip @user",
            "    Send 1 golden salt to a friend",
            "/redistribute_wealth",
            "    Communism (superuser only)",
            "/help",
            "    This message",
        });
        await RespondAsync($"```{help}```");
    }
}
