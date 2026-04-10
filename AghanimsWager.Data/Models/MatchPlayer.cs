namespace AghanimsWager.Data.Models;

public class MatchPlayer
{
    public long MatchId { get; set; }
    public int PlayerSlot { get; set; }
    public long AccountId { get; set; }
    public int HeroId { get; set; }
    public int TeamNumber { get; set; }

    // Combat
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int HeroDamage { get; set; }
    public int TowerDamage { get; set; }
    public int HeroHealing { get; set; }

    // Economy
    public int LastHits { get; set; }
    public int Denies { get; set; }
    public int GoldPerMin { get; set; }
    public int XpPerMin { get; set; }
    public int Gold { get; set; }
    public int GoldSpent { get; set; }
    public int GoldLostToDeath { get; set; }
    public int NetWorth { get; set; }
    public int SupportGold { get; set; }
    public int ClaimedFarmGold { get; set; }
    public int Level { get; set; }

    // Items (slots 0-5 inventory, 6-8 backpack, 9-10 extra)
    public int Item0 { get; set; }
    public int Item1 { get; set; }
    public int Item2 { get; set; }
    public int Item3 { get; set; }
    public int Item4 { get; set; }
    public int Item5 { get; set; }
    public int Item6 { get; set; }
    public int Item7 { get; set; }
    public int Item8 { get; set; }
    public int Item9 { get; set; }
    public int Item10 { get; set; }

    // Hero
    public int HeroPickOrder { get; set; }
    public int SelectedFacet { get; set; }
    public int HeroPlayCount { get; set; }
    public bool HeroWasRandomed { get; set; }

    // Performance / scaled
    public float ScaledKills { get; set; }
    public float ScaledDeaths { get; set; }
    public float ScaledAssists { get; set; }
    public int ScaledHeroDamage { get; set; }
    public int ScaledTowerDamage { get; set; }
    public int ScaledHeroHealing { get; set; }
    public float ScaledMetric { get; set; }
    public float ExpectedTeamContribution { get; set; }
    public int SupportAbilityValue { get; set; }

    // Misc
    public int SecondsDead { get; set; }
    public int BountyRunes { get; set; }
    public int OutpostsCaptured { get; set; }
    public int LeaverStatus { get; set; }
    public long PartyId { get; set; }
    public bool FeedingDetected { get; set; }

    // Rank
    public int SearchRank { get; set; }
    public int SearchRankUncertainty { get; set; }
    public int PreviousRank { get; set; }
    public int RankChange { get; set; }
    public int MmrType { get; set; }

    public Match Match { get; set; } = null!;
}
