using System.Text.Json.Nodes;

// Data-driven castle-renovation levels + costs, loaded from the server's packed game data
// (gamedata/castle-renovation.json — see GameData). Backs the server's side of the castle-build system:
// build_getBuildModel/build_getCastleLevelUpInfo/buildingNavBar_* are engine-LOCAL native calls (never .hqs
// endpoints), so the ONLY server-bound piece is recording the level
// advance + deducting its material cost when a SetCastleRenovationLevelAssignmentActionSpec is reported via
// ExecuteAssignmentActionCommand (see GameEndpoints.cs SendCommands).
sealed class CastleRenovationCatalog
{
    // 0-based on the wire (RenovationLevel1 = 0) — confirmed by playtest: sending CastleRenovationLevel=0 at
    // boot renders as "Level 1" client-side.
    static readonly string[] LevelNames = { "RenovationLevel1", "RenovationLevel2", "RenovationLevel3", "RenovationLevel4", "RenovationComplete" };

    readonly Dictionary<string, List<(int TemplateId, int Quantity)>> _costs = new();

    public int Count => _costs.Count;

    public static int Ordinal(string? levelName) => levelName is null ? -1 : Array.IndexOf(LevelNames, levelName);

    public IReadOnlyList<(int TemplateId, int Quantity)> CostFor(string levelName) =>
        _costs.TryGetValue(levelName, out var c) ? c : Array.Empty<(int, int)>();

    public static CastleRenovationCatalog Load(string dataDir)
    {
        var c = new CastleRenovationCatalog();
        var root = GameData.Load(dataDir, "castle-renovation.json");

        foreach (var kv in root["Costs"]?.AsObject() ?? new JsonObject())
        {
            var cost = new List<(int, int)>();
            foreach (var item in kv.Value?.AsArray() ?? new JsonArray())
                if (item is JsonObject io)
                    cost.Add(((int?)io["TemplateId"] ?? 0, (int?)io["Quantity"] ?? 1));
            c._costs[kv.Key] = cost;
        }
        return c;
    }
}
