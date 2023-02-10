namespace csharp_ef_webapi.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("balance_ledger")]
public class BalanceLedger
{
    [Key]
    [Column("discord_id")]
    public long DiscordId { get; set; }

    [Column("tokens")]
    public long Tokens { get; set; }
}