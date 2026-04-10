# Aghanim's Wager

A Dota 2 betting bot for Discord. Tracks your friends' live matches via the Dota 2 Game Coordinator and lets your Discord server wager fake currency on the outcomes.

## How it works

Two processes run side by side:

- **AghanimsWager.GC** -- A headless Steam client that monitors your friends list for live Dota 2 matches. It connects to the Dota 2 Game Coordinator via SteamKit2, polls SourceTV for match data, and writes results to the shared database.

- **AghanimsWager.Discord** -- A Discord bot that presents the match data, manages bets, tracks balances, and announces results.

When a friend starts a Dota 2 match, the GC bot detects it through Steam rich presence and begins polling for live game data. The Discord bot picks this up, posts match info, opens a betting window, and resolves payouts when the game ends.

## Features

- Automatic match detection via Steam friend rich presence
- Live match display with hero picks, scores, and game time
- Auto-betting for linked Steam accounts (players bet on themselves)
- Manual betting with `!bet` during the betting window
- Betting window countdown with configurable close timer
- Leaderboard with balances and win streaks
- Salt mine (minimum balance floor so nobody goes completely broke)
- Survives restarts mid-match -- persists match state, message IDs, and betting status

## Commands

| Command | Description |
|---|---|
| `!bet <match_id> <radiant/dire> <amount>` | Place a bet on an active match |
| `!balance` | Check your golden salt balance |
| `!leaderboard` | Top players by balance, with streaks |
| `!tip <@user> <amount>` | Tip another player |
| `!add_steam_id <steam_profile_url>` | Link your Discord account to your Steam profile |
| `!help` | List available commands |

## Architecture

```
Steam GC  <-->  AghanimsWager.GC  <-->  SQLite  <-->  AghanimsWager.Discord  <-->  Discord
```

- **AghanimsWager.Data** -- Shared data layer. EF Core with SQLite. Models for matches, wagers, player accounts, and live match state.
- **AghanimsWager.GC** -- SteamKit2 + Dota 2 GC protobuf communication. Tracks lobbies, polls SourceTV, resolves match outcomes.
- **AghanimsWager.Discord** -- Discord.NET bot. Betting logic, payouts, display, user commands.

## Setup

Requires .NET 9.

```sh
dotnet restore
dotnet build
```

Copy the template config files and fill in your credentials:

```sh
cp AghanimsWager.GC/appsettings.template.json AghanimsWager.GC/appsettings.json
cp AghanimsWager.Discord/appsettings.template.json AghanimsWager.Discord/appsettings.json
```

The GC bot needs Steam credentials and an API key. The Discord bot needs a Discord bot token and a Steam API key. Both need a path to the shared SQLite database.

Run both processes:

```sh
cd AghanimsWager.GC && dotnet run
cd AghanimsWager.Discord && dotnet run
```

## Currency

The unit of currency is **golden salt**. New players receive a stipend of 1,000. Players in a match auto-bet 250 on their own team. The salt mine ensures nobody drops below 50.

Winners of manual bets receive 2x their wager. Auto-bet winners get their stake back plus a share of the losers' salt. Bonus payouts scale with the number of losing players.
