using System.Text.Json.Nodes;

// Loads the packed game-design data (gamedata/items.json — see GameData; no hardcoded item ids). Two lookups:
//   SkuCode  -> { ItemId, Price }             ("Skus")
//   ItemId   -> { ArchetypeId, Type, Level }  ("Templates")
// Used to turn a BuyHeroItemCommand's SkuCode into a concrete equippable item the hero can carry/equip.
sealed class ItemCatalog
{
    public readonly record struct SkuInfo(int ItemId, int PriceCurrency, int PriceAmount);
    public readonly record struct TemplateInfo(int ArchetypeId, int HeroItemTypeId, int ItemLevel);

    readonly Dictionary<string, SkuInfo> _skus = new();
    readonly Dictionary<int, TemplateInfo> _templates = new();

    public int SkuCount => _skus.Count;
    public int TemplateCount => _templates.Count;

    static int I(JsonNode? n, int dflt = 0) => n is null ? dflt : (int?)n ?? dflt;

    public static ItemCatalog Load(string dataDir)
    {
        var c = new ItemCatalog();
        var root = GameData.Load(dataDir, "items.json");

        foreach (var kv in root["Skus"]?.AsObject() ?? new JsonObject())
        {
            if (kv.Value is not JsonObject s) continue;
            c._skus[kv.Key] = new SkuInfo(I(s["ItemId"]), I(s["PriceCurrency"]), I(s["PriceAmount"]));
        }

        foreach (var kv in root["Templates"]?.AsObject() ?? new JsonObject())
        {
            if (kv.Value is not JsonObject t || !int.TryParse(kv.Key, out var id)) continue;
            c._templates[id] = new TemplateInfo(I(t["ArchetypeId"]), I(t["HeroItemTypeId"]), I(t["ItemLevel"], 1));
        }
        return c;
    }

    public SkuInfo? ResolveSku(string code) => _skus.TryGetValue(code, out var s) ? s : null;

    // The equippable-item JsonObject (the HeroEquipmentItem contract the GAI + gear use) for a template id.
    public JsonObject? BuildItem(int templateId)
    {
        if (!_templates.TryGetValue(templateId, out var t)) return null;
        return new JsonObject
        {
            ["Type"] = "HeroEquipmentItem",
            ["ItemLevel"] = t.ItemLevel,
            ["ArchetypeId"] = t.ArchetypeId,
            ["PrimaryStatsModifiers"] = new JsonArray(0.4, 0.4, 0.4),
            ["TemplateId"] = templateId,
            ["DyeTemplateId"] = 0,
        };
    }
}
