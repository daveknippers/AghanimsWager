using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
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
    public async Task Bet(long match_id, string side, long amount)
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
            await RespondAsync("```No players yet```");
            return;
        }

        var lines = new List<string> { $"{"#",5} {"Salt",12} {"Streak",8} {"Best",6} | Name" };
        lines.Add(new string('-', 51));
        for (int i = 0; i < accounts.Count; i++)
        {
            var a = accounts[i];
            var user = await Context.Client.GetUserAsync((ulong)a.DiscordId);
            var name = user?.Username ?? a.DiscordId.ToString();
            var streak = a.CurrentStreak > 0 ? a.CurrentStreak.ToString() : "";
            var best = a.BestStreak > 0 ? a.BestStreak.ToString() : "";
            lines.Add($"{i + 1,5} {a.Tokens,12} {streak,8} {best,6} | {name}");
        }

        await RespondAsync($"```{string.Join('\n', lines)}```");
    }

    [SlashCommand("feederboard", "The cooler leaderboard")]
    public async Task Feederboard()
    {
        await RespondAsync(
            "```The cooler leaderboard is under construction.```");
    }

    [SlashCommand("add_steam_id", "Link your Steam account for auto-bets")]
    public async Task AddSteamId(string steam_id, bool update = false)
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
            existing.DiscordName = Context.User.Username;
        }
        else
        {
            db.DiscordMappings.Add(new DiscordMapping
            {
                DiscordId = discordId,
                SteamId = steamId.Value,
                AccountId = accountId,
                DiscordName = Context.User.Username,
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
    public async Task Tip(IUser user)
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

        _bot.RecordTip(tipperId);
        WagerBot.Log($"Tip: {Context.User.Username} -> {user.Username}");
        await RespondAsync(
            $"{mention} sent {user.Mention} a salty tip.");
    }

    [SlashCommand("active_bets", "Show active bets")]
    public async Task ActiveBets(long? match_id = null)
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
            var name = user?.Username ?? g.GamblerId.ToString();
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
