namespace AghanimsWager.Data.Models;

public class DiscordMapping
{
    public long DiscordId { get; set; }
    public long SteamId { get; set; }
    public long AccountId { get; set; }
    public required string DiscordName { get; set; }
}
