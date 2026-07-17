using System.Text.Json.Nodes;

// Loads ALL campaign missions (objectives) from the server's packed game data (gamedata/missions.json — see
// GameData) — the data-driven source for the MissionManager. The condition/reward kinds are a finite set,
// normalised at pack time from the spec DB's $type envelopes; nothing is hardcoded per-mission. New custom
// missions later = more MissionDefs feeding the same engine.
sealed class MissionCatalog
{
    // ---- condition (one per Conditions[] entry) ---------------------------------------------------------
    // Kind ∈ CastleEntered | CastleCompleted | Destroyed | CPBuilt | BuildingRank | Built | CastleValid.
    // Server-EVALUABLE from an EndAttack: CastleEntered, CastleCompleted, Destroyed (the "Attack" category).
    // The build kinds load but are client-completed (ClientSideCompletion) — the engine won't score them.
    // A kind the packer didn't recognise is carried through as its raw spec type: it loads, and is never met.
    public sealed record Cond(string Kind, string? ItemType, int SpecContainerId, int Count, int Rank);

    // ---- reward (one per Rewards[] entry) ---------------------------------------------------------------
    // Kind ∈ Materials | Currency | Xp | Item.
    public sealed record Reward(string Kind, string? CurrencyType, int Amount, int Xp,
                                IReadOnlyList<(int Id, int Quantity)> Materials,
                                int ItemTemplateId, int ItemLevel, int ItemArchetypeId);

    // ---- mission (one objective) ------------------------------------------------------------------------
    public sealed record Def(
        int Id, string Category, string Type,
        string? CastleType, int? CastleId, IReadOnlyList<string> CastleTypes,
        IReadOnlyList<Cond> Conditions, IReadOnlyList<Reward> Rewards,
        IReadOnlyList<int> RequiresObjectiveIds, bool ManualPopupOnCompletion, bool ClientSideCompletion);

    readonly Dictionary<int, Def> _byId = new();
    public IReadOnlyCollection<Def> All => _byId.Values;
    public int Count => _byId.Count;
    public Def? Get(int id) => _byId.TryGetValue(id, out var d) ? d : null;

    static int I(JsonNode? n, int dflt = 0) => n is null ? dflt : (int?)n ?? dflt;
    static string? S(JsonNode? n) => (string?)n;

    public static MissionCatalog Load(string dataDir)
    {
        var c = new MissionCatalog();
        var root = GameData.Load(dataDir, "missions.json");

        foreach (var m in root["Missions"]?.AsArray() ?? new JsonArray())
        {
            if (m is not JsonObject obj || obj["Id"] is null) continue;

            var conds = new List<Cond>();
            foreach (var cn in obj["Conditions"]?.AsArray() ?? new JsonArray())
                if (cn is JsonObject co)
                    conds.Add(new Cond(S(co["Kind"]) ?? "", S(co["ItemType"]),
                                       I(co["SpecContainerId"]), I(co["Count"]), I(co["Rank"])));

            var rewards = new List<Reward>();
            foreach (var rn in obj["Rewards"]?.AsArray() ?? new JsonArray())
            {
                if (rn is not JsonObject ro) continue;
                var mats = new List<(int, int)>();
                foreach (var mn in ro["Materials"]?.AsArray() ?? new JsonArray())
                    if (mn is JsonObject mo) mats.Add((I(mo["Id"]), I(mo["Quantity"], 1)));
                rewards.Add(new Reward(S(ro["Kind"]) ?? "", S(ro["CurrencyType"]), I(ro["Amount"]), I(ro["Xp"]),
                                       mats, I(ro["ItemTemplateId"]), I(ro["ItemLevel"], 1), I(ro["ItemArchetypeId"])));
            }

            var requires = new List<int>();
            foreach (var rq in obj["RequiresObjectiveIds"]?.AsArray() ?? new JsonArray()) requires.Add(I(rq));

            var castleTypes = new List<string>();
            foreach (var ct in obj["CastleTypes"]?.AsArray() ?? new JsonArray())
                if ((string?)ct is { } cts) castleTypes.Add(cts);

            int id = I(obj["Id"]);
            c._byId[id] = new Def(
                id,
                S(obj["Category"]) ?? "",
                S(obj["Type"]) ?? "",
                S(obj["CastleType"]),
                obj["CastleId"] is { } cid ? (int?)cid : null,
                castleTypes,
                conds, rewards, requires,
                (bool?)obj["ManualPopupOnCompletion"] ?? false,
                (bool?)obj["ClientSideCompletion"] ?? false);
        }
        return c;
    }
}
