using System.Text.Json.Nodes;

// Data-driven castle-renovation levels + costs, loaded from the decrypted spec DB
// (GeneralSettings/CASTLERENOVATIONSETTINGS.JSON). Backs the server's side of the castle-build system:
// build_getBuildModel/build_getCastleLevelUpInfo/buildingNavBar_* are engine-local native calls (not .hqs
// endpoints), so the only server-bound piece is recording the level advance + deducting its material cost
// when a SetCastleRenovationLevelAssignmentActionSpec is reported via ExecuteAssignmentActionCommand
// (see GameEndpoints.cs SendCommands).
sealed class CastleRenovationCatalog
{
    // 0-based on the wire (RenovationLevel1 = 0): CastleRenovationLevel=0 renders as "Level 1" client-side.
    static readonly string[] LevelNames = { "RenovationLevel1", "RenovationLevel2", "RenovationLevel3", "RenovationLevel4", "RenovationComplete" };

    readonly Dictionary<string, List<(int TemplateId, int Quantity)>> _costs = new();

    public static int Ordinal(string? levelName) => levelName is null ? -1 : Array.IndexOf(LevelNames, levelName);

    public IReadOnlyList<(int TemplateId, int Quantity)> CostFor(string levelName) =>
        _costs.TryGetValue(levelName, out var c) ? c : Array.Empty<(int, int)>();

    public static CastleRenovationCatalog Load(string dataRoot)
    {
        var c = new CastleRenovationCatalog();
        var doc = JsonNode.Parse(File.ReadAllText(
            Path.Combine(dataRoot, "castle", "renovation-settings.json")))!;
        foreach (var kv in doc["PerLevelRenovationInformation"]!.AsObject())
        {
            var cost = new List<(int, int)>();
            foreach (var item in kv.Value?["Cost"]?.AsArray() ?? new JsonArray())
                if (item is JsonObject io)
                    cost.Add(((int?)io["TemplateId"] ?? 0, (int?)io["StackCount"] ?? 1));
            c._costs[kv.Key] = cost;
        }
        return c;
    }

    public static string? FindDataRoot() => ItemCatalog.FindDataRoot();
}
