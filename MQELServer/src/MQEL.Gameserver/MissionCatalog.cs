using System.Text.Json.Nodes;

// Loads ALL campaign missions (objectives) from the decrypted spec DB — the data-driven source for the
// MissionManager. One file: GeneralSettings/OBJECTIVESETTINGS.JSON ("Objectives":[...]). Every distinct
// condition/reward $type in that file is finite and mapped here (see code-analysis/rest-api/objectives.md);
// nothing is hardcoded per-mission. New custom missions later = more MissionDefs feeding the same engine.
sealed class MissionCatalog
{
    // ---- condition (one per Conditions[] entry) ---------------------------------------------------------
    // Kind ∈ CastleEntered | CastleCompleted | Destroyed | CPBuilt | BuildingRank | Built | CastleValid.
    // Server-EVALUABLE from an EndAttack: CastleEntered, CastleCompleted, Destroyed (the "Attack" category).
    // The build kinds load but are client-completed (ClientSideCompletion) — the engine won't score them.
    public sealed record Cond(string Kind, string? ItemType, int SpecContainerId, int Count, int Rank);

    // ---- reward (one per Reward.RewardItems[] entry) ----------------------------------------------------
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

    // Strip the ".NET contract" envelope ("Ns.Ns.Name, Assembly") down to the bare type name.
    static string ShortType(JsonNode? n)
    {
        var s = (string?)n ?? "";
        int comma = s.IndexOf(','); if (comma >= 0) s = s[..comma];
        int dot = s.LastIndexOf('.'); return dot >= 0 ? s[(dot + 1)..] : s;
    }
    static int I(JsonNode? n, int dflt = 0) => n is null ? dflt : (int?)n ?? dflt;
    static string? S(JsonNode? n) => (string?)n;

    public static MissionCatalog Load(string specRoot)
    {
        var c = new MissionCatalog();
        var doc = JsonNode.Parse(File.ReadAllText(
            Path.Combine(specRoot, "GameplaySettings", "GeneralSettings", "OBJECTIVESETTINGS.JSON")))!;
        foreach (var o in doc["Objectives"]?.AsArray() ?? new JsonArray())
        {
            if (o is not JsonObject obj || obj["Id"] is null) continue;
            int id = I(obj["Id"]);

            var conds = new List<Cond>();
            foreach (var cn in obj["Conditions"]?.AsArray() ?? new JsonArray())
            {
                if (cn is not JsonObject co) continue;
                conds.Add(ShortType(co["$type"]) switch
                {
                    "CastleEnteredCondition"             => new Cond("CastleEntered", null, 0, 0, 0),
                    "CastleCompletedCondition"           => new Cond("CastleCompleted", null, 0, 0, 0),
                    "DefenseIngredientDestroyedCondition"=> new Cond("Destroyed", S(co["ItemType"]), I(co["SpecContainerId"]), I(co["Count"], 1), 0),
                    "ConstructionPointsBuiltCondition"   => new Cond("CPBuilt", null, 0, I(co["Count"], 1), 0),
                    "BuildingRankCondition"              => new Cond("BuildingRank", null, I(co["SpecContainerId"]), I(co["Count"], 1), I(co["Rank"], 1)),
                    "DefenseIngredientBuiltCondition"    => new Cond("Built", S(co["ItemType"]), 0, I(co["Count"], 1), 0),
                    "CastleValidityCondition"            => new Cond("CastleValid", null, 0, 0, 0),
                    var other                            => new Cond(other, null, 0, 0, 0),   // unknown → loaded, never auto-met
                });
            }

            var rewards = new List<Reward>();
            void AddRewards(JsonNode? rewardObj)
            {
                foreach (var rn in rewardObj?["RewardItems"]?.AsArray() ?? new JsonArray())
                {
                    if (rn is not JsonObject ro) continue;
                    switch (ShortType(ro["$type"]))
                    {
                        case "CurrencyAmountRewardItem":
                            rewards.Add(new Reward("Currency", S(ro["CurrencyAmount"]?["CurrencyType"]), I(ro["CurrencyAmount"]?["Amount"]), 0, Array.Empty<(int, int)>(), 0, 0, 0));
                            break;
                        case "CraftingMaterialsRewardItem":
                            var mats = new List<(int, int)>();
                            foreach (var m in ro["CraftingMaterials"]?.AsArray() ?? new JsonArray())
                                if (m is JsonObject mo) mats.Add((I(mo["Id"]), I(mo["Quantity"], 1)));
                            rewards.Add(new Reward("Materials", null, 0, 0, mats, 0, 0, 0));
                            break;
                        case "XpRewardItem":
                            rewards.Add(new Reward("Xp", null, 0, I(ro["Xp"]), Array.Empty<(int, int)>(), 0, 0, 0));
                            break;
                        case "InventoryItemRewardItem":
                            var item = ro["Item"];
                            rewards.Add(new Reward("Item", null, 0, 0, Array.Empty<(int, int)>(),
                                I(item?["TemplateId"]), I(item?["ItemLevel"], 1), I(item?["ArchetypeId"])));
                            break;
                    }
                }
            }
            AddRewards(obj["Reward"]);
            foreach (var ph in obj["PerHeroReward"]?.AsObject() ?? new JsonObject()) AddRewards(ph.Value);

            var requires = new List<int>();
            foreach (var rq in obj["Requirements"]?.AsArray() ?? new JsonArray())
                if (rq is JsonObject rqo && ShortType(rqo["$type"]) == "ObjectiveCompletedObjectiveRequirement")
                    requires.Add(I(rqo["ObjectiveId"]));

            var castleTypes = new List<string>();
            foreach (var ct in obj["CastleTypes"]?.AsArray() ?? new JsonArray())
                if ((string?)ct is { } cts) castleTypes.Add(cts);

            c._byId[id] = new Def(
                id,
                S(obj["Category"]) ?? "",
                ShortType(obj["$type"]),
                S(obj["CastleId"]?["CastleType"]),
                obj["CastleId"]?["Id"] is { } cid ? (int?)cid : null,
                castleTypes,
                conds, rewards, requires,
                (bool?)obj["ManualPopupTriggerOnCompletion"] ?? false,
                (bool?)obj["ClientSideCompletion"] ?? false);
        }
        return c;
    }

    // game-data/settings-extracted lookup (shared convention with ItemCatalog).
    public static string? FindSpecRoot() => ItemCatalog.FindSpecRoot();
}
