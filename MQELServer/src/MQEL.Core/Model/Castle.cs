namespace MQEL.Core.Model;

/// <summary>The player's castle (1:1 with the account). Rooms hold buildings (CastleHeart/GoldStorage/...).</summary>
public class Castle
{
    public long CastleId { get; set; }
    public long AccountId { get; set; }
    public int LayoutId { get; set; } = 1;
    public int ThemeId { get; set; }
    public int CastleHeartRank { get; set; } = 1;
    public int MaxConstructionPoints { get; set; }
    public DateTime ModifiedUtc { get; set; }

    public List<CastleRoom> Rooms { get; set; } = new();
}

public class CastleRoom
{
    public long RoomId { get; set; }
    public long CastleId { get; set; }
    public int RoomIndex { get; set; }                  // BuildInfo Rooms[].Id
    public int SpecContainerId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    public List<CastleBuilding> Buildings { get; set; } = new();
}

public class CastleBuilding
{
    public long BuildingId { get; set; }
    public long RoomId { get; set; }
    public int BuildingIndex { get; set; }              // BuildInfo Buildings[].Id
    public int SpecContainerId { get; set; }            // 1=CastleHeart, 3=GoldStorage, 4=LifeForceStorage, ...
    public int Rank { get; set; }
    public int RoomZoneId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Orientation { get; set; }
}
