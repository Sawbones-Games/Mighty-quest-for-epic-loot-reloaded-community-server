namespace MQEL.Core.Model;

/// <summary>
/// A looted item sitting in the inbox/inventory (not yet equipped). Keyed by its 24-hex ObjectId so the
/// client can reference it to equip. Tracked here so loot persists across reboots.
/// </summary>
public class InventoryItem
{
    public string ObjectId { get; set; } = "";
    public long AccountId { get; set; }
    public int ItemType { get; set; }                   // 3=HeroEquipment, 4=Consumable
    public int TemplateId { get; set; }
    public int StackCount { get; set; } = 1;            // consumables (ItemType 4) stack; gear (3) is always 1
    public int ArchetypeId { get; set; }
    public int ItemLevel { get; set; } = 1;
    public int DyeTemplateId { get; set; }
    public double Stat0 { get; set; }
    public double Stat1 { get; set; }
    public double Stat2 { get; set; }
    public bool IsSellable { get; set; } = true;
}

/// <summary>
/// A completed tutorial assignment. THE key to "jump into any tutorial step": serving the right set in the
/// GAI makes the client's assignment VM resume there. Populated from the SendCommands
/// StartAssignment/CompleteAssignment stream.
/// </summary>
public class CompletedAssignment
{
    public long AccountId { get; set; }
    public int AssignmentId { get; set; }
    public DateTime CompletedUtc { get; set; }
}

/// <summary>An account objective (300/301/302...) with its status.</summary>
public class Objective
{
    public long AccountId { get; set; }
    public int ObjectiveId { get; set; }
    public int Status { get; set; }
    public DateTime LastStatusUtc { get; set; }
}

/// <summary>A crafting material owned by an account (1002=Defenderidium, 1004=Smoldering Eye, …).</summary>
public class CraftingMaterial
{
    public long AccountId { get; set; }
    public int MaterialId { get; set; }
    public long Quantity { get; set; }
}
