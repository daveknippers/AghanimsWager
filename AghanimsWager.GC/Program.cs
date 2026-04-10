using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using AghanimsWager.Data;
using AghanimsWager.GC;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables(prefix: "AW_")
    .Build();

var steamUser = config["STEAM_USER"] ?? throw new InvalidOperationException("Set AW_STEAM_USER");
var steamPass = config["STEAM_PASS"] ?? throw new InvalidOperationException("Set AW_STEAM_PASS");
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

var bot = new DotaGCBot(steamUser, steamPass, optionsBuilder.Options);
await bot.RunAsync(cts.Token);
