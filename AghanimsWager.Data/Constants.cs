namespace AghanimsWager.Data;

public static class Constants
{
    public const long NewPlayerStipend = 1000;
    public const long AutobetAmount = 250;
    public const long SaltMine = 50; // minimum balance floor
    public const int GamblingCloseWaitSeconds = 90;
    public const int MaxBetGameTime = 250; // failsafe: close betting at this game time regardless
    public const string Currency = "golden salt";
}
