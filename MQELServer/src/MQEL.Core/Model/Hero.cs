namespace MQEL.Core.Model;

/// <summary>A hero in the account's registry (the value the skill-tree gate reads is <see cref="Level"/>).</summary>
public class Hero
{
    public long HeroId { get; set; }
    public long AccountId { get; set; }
    public int HeroClass { get; set; }                  // eHerotype / SpecContainerId: 2 Knight/3 Archer/4 Mage/5 Runaway
    public int Level { get; set; } = 1;
    public int Xp { get; set; }

    public List<HeroGearItem> Gear { get; set; } = new();
    public List<HeroSpell> Spells { get; set; } = new();
    public List<HeroConsumable> Consumables { get; set; } = new();
    public List<HeroInventoryItem> Inventory { get; set; } = new();
}

/// <summary>An equipped item, one per slot (MainHand/Head/Body/Hands/Shoulders/...).</summary>
public class HeroGearItem
{
    public long HeroId { get; set; }
    public string Slot { get; set; } = "";
    public int TemplateId { get; set; }
    public int ArchetypeId { get; set; }
    public int ItemLevel { get; set; } = 1;
    public int DyeTemplateId { get; set; }
    public double Stat0 { get; set; }                   // PrimaryStatsModifiers[0..2]
    public double Stat1 { get; set; }
    public double Stat2 { get; set; }
}

/// <summary>An equipped spell (e.g. Piercing Shot 158) at a tree level + action-bar slot.</summary>
public class HeroSpell
{
    public long HeroId { get; set; }
    public int SpellId { get; set; }                    // SpellSpecContainerId
    public int Level { get; set; } = 1;
    public int SlotIndex { get; set; }
}

/// <summary>An equipped consumable stack (template 1 = the starter potion).</summary>
public class HeroConsumable
{
    public long HeroId { get; set; }
    public int TemplateId { get; set; }
    public int StackCount { get; set; } = 1;
}

/// <summary>A non-equipped item in the hero's inventory at a numeric slot (looted-then-collected, or bought,
/// before it's equipped). Persisted because the collect->equip handshake spans two SendCommands batches.</summary>
public class HeroInventoryItem
{
    public long HeroId { get; set; }
    public int Slot { get; set; }
    public int TemplateId { get; set; }
    public int ArchetypeId { get; set; }
    public int ItemLevel { get; set; } = 1;
    public int DyeTemplateId { get; set; }
    public double Stat0 { get; set; }
    public double Stat1 { get; set; }
    public double Stat2 { get; set; }
}
