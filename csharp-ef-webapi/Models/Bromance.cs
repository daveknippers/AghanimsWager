namespace csharp_ef_webapi.Models;

using System.ComponentModel.DataAnnotations.Schema;

[Table("bromance_last_60")]
public class Bromance
{
    [Column("bro_1_name")]
    public string? bro1Name { get; set; }
    [Column("bro_2_name")]
    public string? bro2Name { get; set; }
    [Column("total_wins")]
    public int totalWins { get; set; }

    [Column("total_matches")]
    public int totalMatches { get; set; }

    [NotMapped]
    public float winRate {
        get {
            return totalMatches == 0 ? 0 : (float)totalWins / (float)totalMatches;
        }
    }

}