using System.Text.Json.Nodes;

// Loads the decrypted game-design spec (authoritative source — no hardcoded item ids). Two lookups:
//   SkuCode  -> { ItemId, Price }            from ShopSettings/SHOPSKUBASESETTINGS.JSON  ("Skus":[...])
//   ItemId   -> { ArchetypeId, Type, Level } from HeroItems/HEROITEMTEMPLATES.JSON       ("TemplateList":[...])
// Used to turn a BuyHeroItemCommand's SkuCode into a concrete equippable item the hero can carry/equip.
sealed class ItemCatalog
{
    public readonly record struct SkuInfo(int ItemId, int PriceCurrency, int PriceAmount);
    public readonly record struct TemplateInfo(int ArchetypeId, int HeroItemTypeId, int ItemLevel);

    readonly Dictionary<string, SkuInfo> _skus = new();
    readonly Dictionary<int, TemplateInfo> _templates = new();

    public int SkuCount => _skus.Count;
    public int TemplateCount => _templates.Count;

    public static ItemCatalog Load(string specRoot)
    {
        var c = new ItemCatalog();

        var skuDoc = JsonNode.Parse(File.ReadAllText(Path.Combine(specRoot, "GameplaySettings", "ShopSettings", "SHOPSKUBASESETTINGS.JSON")))!;
        foreach (var s in skuDoc["Skus"]!.AsArray())
        {
            if (s is null || (string?)s["Code"] is not { } code) continue;
            c._skus[code] = new SkuInfo((int?)s["ItemId"] ?? 0, (int?)s["Price"]?["CurrencyType"] ?? 0, (int?)s["Price"]?["Amount"] ?? 0);
        }

        var tplDoc = JsonNode.Parse(File.ReadAllText(Path.Combine(specRoot, "GameplaySettings", "HeroItems", "HEROITEMTEMPLATES.JSON")))!;
        foreach (var t in tplDoc["TemplateList"]!.AsArray())
        {
            if (t is null || (int?)t["Id"] is not { } id) continue;
            c._templates[id] = new TemplateInfo((int?)t["ArchetypeId"] ?? 0, (int?)t["HeroItemTypeId"] ?? 0, (int?)t["LevelMin"] ?? 1);
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

    // Walk up from the working dir to locate game-data/settings-extracted (robust to where the exe is launched).
    public static string? FindSpecRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game-data", "settings-extracted");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
