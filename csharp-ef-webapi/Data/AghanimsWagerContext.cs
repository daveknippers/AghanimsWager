#nullable disable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using csharp_ef_webapi.Models;

public class AghanimsWagerContext : DbContext
{
    IConfiguration _configuration;
    public AghanimsWagerContext(IConfiguration configuration, DbContextOptions<AghanimsWagerContext> options)
        : base(options)
    {
        _configuration = configuration;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        // connect to sqlite database
        var conn_string = _configuration.GetConnectionString("AghanimsWagerDatabase");
        conn_string = conn_string.Replace("{SQL_USER}", Environment.GetEnvironmentVariable("SQL_USER"));
        conn_string = conn_string.Replace("{SQL_PASSWORD}", Environment.GetEnvironmentVariable("SQL_PASSWORD"));
        options.UseNpgsql(conn_string);
    }
    public DbSet<BalanceLedger> BalanceLedger { get; set; }
    public DbSet<DiscordIds> DiscordIds { get; set; }
    public DbSet<PlayerMatchDetails> PlayerMatchDetails { get; set; }
    public DbSet<MatchStatus> MatchStatus { get; set; }
    public DbSet<Bromance> Bromance { get; set; }
    public DbSet<BetStreak> BetStreaks { get; set; }
    public DbSet<MatchStreak> MatchStreaks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("Kali");
        modelBuilder.Entity<BalanceLedger>().ToTable("balance_ledger", "Kali");

        modelBuilder.Entity<PlayerMatchDetails>()
            .HasKey(pmd => new { pmd.MatchId, pmd.PlayerSlot });

        modelBuilder.Entity<Bromance>()
            .HasKey(b => new { b.bro1Name, b.bro2Name });
    }

    public class StringArrayValueConverter : ValueConverter<string[], string>
    {
        public StringArrayValueConverter() : base(le => ArrayToString(le), (s => StringToArray(s)))
        {

        }
        private static string ArrayToString(string[] value)
        {
            if (value == null || value.Count() == 0)
            {
                return null;
            }

            return string.Join(',', value);
        }

        private static string[] StringToArray(string value)
        {
            if (value == null || value == string.Empty)
            {
                return null;
            }

            return value.Split(',');

        }
    }
}
