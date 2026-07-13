using System.Text.Json.Nodes;

// Single source for the notification wire contracts appended to .hqs responses. The $type strings and the
// NotificationType integers live here EXACTLY ONCE — a typo in either is a SILENT client drop (the engine
// ignores unknown/mistyped notifications with no error and no log), so having them duplicated across EndAttack
// and MissionManager is precisely how a regression hides (fableReview §3.4; see find-notification-shape skill).
// Shapes verified in code-analysis/decompiled/account/in-session-state-sync.md + code-analysis/rest-api/objectives.md.
static class Notifications
{
    const string NS = ", HyperQuest.GameServer.Contracts";
    static string T(string name) => "HyperQuest.GameServer.Contracts." + name + NS;

    // type-24 WalletUpdated — the gained DELTA per currency the client adds to its pre-attack balance (IGC=2, LifeForce=4, PremiumCash=1).
    public static JsonObject WalletUpdated(params (int currencyType, int amount)[] amounts)
    {
        var arr = new JsonArray();
        foreach (var (c, a) in amounts) arr.Add(new JsonObject { ["CurrencyType"] = c, ["Amount"] = a });
        return new JsonObject { ["$type"] = T("WalletUpdatedNotification"), ["Amounts"] = arr, ["NotificationType"] = 24 };
    }

    // type-43 HeroXpChanged — LevelChanged:true drives the registry-hero level bump that ungates the skill tree.
    public static JsonObject HeroXpChanged(int heroClass, int added, int total, int level, bool levelChanged) => new()
    {
        ["$type"] = T("HeroXpChangedNotification"),
        ["HeroSpecContainerId"] = heroClass, ["XpAdded"] = added, ["TotalXp"] = total,
        ["Level"] = level, ["LevelChanged"] = levelChanged, ["NotificationType"] = 43
    };

    // type-111 InboxItemsAdded — one inbox entry. subtype = "InboxHeroEquipmentItem" (gear, itemType 3) or
    // "InboxConsumableItem" (materials, itemType 4). The HeroItem object carries no $type of its own.
    public static JsonObject InboxItemsAdded(string subtype, JsonObject heroItem, int itemType, string objectId) => new()
    {
        ["$type"] = T("InboxItemsAddedNotification"),
        ["InboxItems"] = new JsonArray(new JsonObject
        {
            ["$type"] = T(subtype), ["HeroItem"] = heroItem, ["ItemType"] = itemType, ["ObjectId"] = objectId
        }),
        ["NotificationType"] = 111
    };

    // type-14 ObjectiveCompleted.
    public static JsonObject ObjectiveCompleted(int objectiveId) => new()
    {
        ["$type"] = T("ObjectiveCompletedNotification"), ["ObjectiveId"] = objectiveId, ["NotificationType"] = 14
    };

    // type-17 ObjectiveUnlocked — chains the NEXT objective's unlock into the SAME response. Unlike type-14
    // (flat, the client already tracks that objective locally) this needs the full AccountObjective payload
    // (Status/LastStatusDate) since the client has never seen this objective before and must create its local
    // tracking entry from scratch. Exact shape from the reference capture (objectives.md §3.2):
    // {"$type":"…ObjectiveUnlockedNotification…","AccountObjective":{"ObjectiveId":303,"Status":1,"LastStatusDate":"2016-10-17T02:58:42Z"},"NotificationType":17}
    public static JsonObject ObjectiveUnlocked(int objectiveId, DateTime lastStatusUtc) => new()
    {
        ["$type"] = T("ObjectiveUnlockedNotification"),
        ["AccountObjective"] = new JsonObject
        {
            ["ObjectiveId"] = objectiveId, ["Status"] = 1,
            ["LastStatusDate"] = lastStatusUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        },
        ["NotificationType"] = 17,
    };
}
