using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using AghanimsWager.Data;
using AghanimsWager.Discord;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables(prefix: "AW_")
    .Build();

var discordToken = config["DISCORD_TOKEN"] ?? throw new InvalidOperationException("Set AW_DISCORD_TOKEN");
var steamApiKey = config["STEAM_API_KEY"] ?? "";
var connString = config["CONNECTION_STRING"] ?? "Data Source=AghanimsWager.db";

var optionsBuilder = new DbContextOptionsBuilder<WagerContext>();
if (connString.StartsWith("Data Source=") || connString.EndsWith(".db"))
    optionsBuilder.UseSqlite(connString);
else
    optionsBuilder.UseNpgsql(connString);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Ensure database exists and enable WAL for concurrent access
using (var db = new WagerContext(optionsBuilder.Options))
{
    db.Database.EnsureCreated();
    if (connString.StartsWith("Data Source=") || connString.EndsWith(".db"))
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

var bot = new WagerBot(discordToken, steamApiKey, optionsBuilder.Options);
await bot.RunAsync(cts.Token);
