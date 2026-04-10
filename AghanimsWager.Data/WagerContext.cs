using Microsoft.EntityFrameworkCore;
using AghanimsWager.Data.Models;

namespace AghanimsWager.Data;

public class WagerContext : DbContext
{
    public DbSet<LiveMatch> LiveMatches => Set<LiveMatch>();
    public DbSet<LivePlayer> LivePlayers => Set<LivePlayer>();
    public DbSet<Friend> Friends => Set<Friend>();
    public DbSet<DiscordMapping> DiscordMappings => Set<DiscordMapping>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Wager> Wagers => Set<Wager>();
    public DbSet<GamblerAccount> GamblerAccounts => Set<GamblerAccount>();
    public DbSet<MatchPlayer> MatchPlayers => Set<MatchPlayer>();
    public DbSet<PendingFriendRequest> PendingFriendRequests => Set<PendingFriendRequest>();

    public WagerContext(DbContextOptions<WagerContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LiveMatch>(e =>
        {
            e.HasKey(m => m.LobbyId);
            e.HasIndex(m => m.MatchId);
            e.HasMany(m => m.Players)
                .WithOne(p => p.Match)
                .HasForeignKey(p => p.MatchId)
                .HasPrincipalKey(m => m.MatchId);
        });

        modelBuilder.Entity<LivePlayer>(e =>
        {
            e.HasKey(p => new { p.MatchId, p.PlayerNum });
        });

        modelBuilder.Entity<Friend>(e =>
        {
            e.HasKey(f => f.SteamId);
        });

        modelBuilder.Entity<DiscordMapping>(e =>
        {
            e.HasKey(d => d.DiscordId);
            e.HasIndex(d => d.AccountId);
        });

        modelBuilder.Entity<Match>(e =>
        {
            e.HasKey(m => m.MatchId);
            e.Property(m => m.MatchId).ValueGeneratedNever();
            e.HasMany(m => m.Wagers)
                .WithOne(w => w.Match)
                .HasForeignKey(w => w.MatchId);
            e.HasMany(m => m.Players)
                .WithOne(p => p.Match)
                .HasForeignKey(p => p.MatchId);
        });

        modelBuilder.Entity<MatchPlayer>(e =>
        {
            e.HasKey(p => new { p.MatchId, p.PlayerSlot });
        });

        modelBuilder.Entity<Wager>(e =>
        {
            e.HasKey(w => w.WagerId);
            e.Property(w => w.WagerId).ValueGeneratedOnAdd();
            e.HasIndex(w => new { w.MatchId, w.Finalized });
        });

        modelBuilder.Entity<PendingFriendRequest>(e =>
        {
            e.HasKey(r => r.SteamId);
        });

        modelBuilder.Entity<GamblerAccount>(e =>
        {
            e.HasKey(g => g.DiscordId);
            e.Property(g => g.DiscordId).ValueGeneratedNever();
            e.HasOne(g => g.DiscordMapping)
                .WithOne()
                .HasForeignKey<DiscordMapping>(d => d.DiscordId);
            e.HasMany(g => g.Wagers)
                .WithOne(w => w.Gambler)
                .HasForeignKey(w => w.GamblerId);
        });
    }
}
