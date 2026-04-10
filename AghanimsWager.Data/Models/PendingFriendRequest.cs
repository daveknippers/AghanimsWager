namespace AghanimsWager.Data.Models;

public class PendingFriendRequest
{
    public long SteamId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
}
