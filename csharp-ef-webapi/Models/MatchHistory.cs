namespace csharp_ef_webapi.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

[Table("dota_match_history")]
public class MatchHistory
{
    [Key]
    [Column("match_id")]
    [JsonProperty("match_id")]
    public long matchId { get; set; }
    
    [Column("series_id")]
    [JsonProperty("series_id")]
    public int seriesId { get; set; }

    [Column("league_id")]
    public int leagueId { get; set; }

    [Column("series_type")]
    [JsonProperty("series_type")]
    public int seriesType { get; set; }

    [Column("match_seq_num")]
    [JsonProperty("match_seq_num")]
    public long matchSeqNum { get; set; }

    [Column("start_time")]
    [JsonProperty("start_time")]
    public long startTime { get; set; }

    [Column("lobby_type")]
    [JsonProperty("lobby_type")]
    public int lobbyType { get; set; }

    [Column("radiant_team_id")]
    [JsonProperty("radiant_team_id")]
    public long radiantTeamId { get; set; }

    [Column("dire_team_id")]
    [JsonProperty("dire_team_id")]
    public long direTeamId { get; set; }
    // players
}