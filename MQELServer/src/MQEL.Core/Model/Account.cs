namespace MQEL.Core.Model;

/// <summary>
/// The account aggregate root. Persisted as a graph (wallets, heroes, inventory, castle, progress).
/// A row with <see cref="IsTemplate"/> = a save-state snapshot rather than a live player.
/// </summary>
public class Account
{
    public long AccountId { get; set; }                 // we assign these (e.g. 3123971); not DB-generated
    public string? SteamId { get; set; }                // external identity (multi-user); null for dev/template
    public string DisplayName { get; set; } = "";
    public int SelectedHeroClass { get; set; }
    public int Privileges { get; set; } = 9;
    public int CastleRenovationLevel { get; set; }
    public int LeagueId { get; set; } = 1;
    public int SubLeagueId { get; set; } = 1;
    public bool IsTemplate { get; set; }                // true = a named save-state snapshot
    public string? SnapshotName { get; set; }           // templates only: the snapshot's name (null for live accounts)
    public bool CastleClaimed { get; set; }             // StarterCastleSelection done (the empty starter castle)
    public int InventorySeq { get; set; } = 0x10;       // monotonic source for looted-item ObjectIds (persists across reboots)
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public List<Wallet> Wallets { get; set; } = new();
    public List<Hero> Heroes { get; set; } = new();
    public List<InventoryItem> Inventory { get; set; } = new();
    public Castle? Castle { get; set; }
    public List<CompletedAssignment> CompletedAssignments { get; set; } = new();
    public List<Objective> Objectives { get; set; } = new();
    public List<CraftingMaterial> CraftingMaterials { get; set; } = new();
}

/// <summary>Per-currency balance + capacity. CurrencyType: 1=Premium, 2=Gold/IGC, 4=LifeForce.</summary>
public class Wallet
{
    public long AccountId { get; set; }
    public int CurrencyType { get; set; }
    public long Amount { get; set; }
    public long Capacity { get; set; }
}
