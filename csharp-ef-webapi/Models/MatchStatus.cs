namespace csharp_ef_webapi.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("match_status")]
public class MatchStatus
{
    [Key]
    [Column("match_id")]
    public long MatchId { get; set; }

    [Column("status")]
    public int Status { get; set; }

}