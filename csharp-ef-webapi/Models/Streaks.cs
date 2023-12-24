namespace csharp_ef_webapi.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("bets_streaks")]
public class BetStreak
{
    [Key]
    [Column("discord_name")]
    public string? discordName { get; set; }
    [Column("streak")]
    public long bet_streak { get; set; }
}

[Table("matches_streaks")]
public class MatchStreak
{
    [Key]
    [Column("discord_name")]
    public string? discordName { get; set; }
    [Column("streak")]
    public long match_streak { get; set; }
}