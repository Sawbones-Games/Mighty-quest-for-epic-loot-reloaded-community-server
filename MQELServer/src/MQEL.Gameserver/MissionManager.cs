using System.Text.Json.Nodes;
using MQEL.Core.Model;

// Data-driven mission engine. On every EndAttack it scores the raid against every active "Attack" mission
// (from MissionCatalog) scoped to the attacked castle, accumulates per-condition progress (a mission may span
// multiple raids/castles), and when all of a mission's conditions are met it completes the mission + emits the
// reward notifications in that castle's EndAttack response. Build/Client missions are not scored here (the
// client completes them; the server only owes their reward).
//
// Progress accumulation is held in a per-account in-memory cache (the `progress` dict the caller supplies).
// It survives between EndAttacks within a session but does not persist across a reconnect — single-castle
// missions (all current campaign Attack objectives) complete in one raid, so that gap only affects a
// multi-castle mission interrupted by a reconnect.
static class MissionManager
{
    // The client gates objective completion on receiving all reward items: a missing reward stalls the
    // objective, a wrongly-serialized one crashes the client. Kept false so item rewards are always sent.
    const bool SkipItemRewards = false;

    public readonly record struct AttackContext(
        int CastleId, string CastleType, IReadOnlyDictionary<int, int> KilledSpecCounts, bool Completed);

    // Returns the notifications to APPEND to the EndAttack response (objective-completed + rewards). Mutates
    // gameState (objective status, wallet/material/xp credits, inbox) and `progress` (per-condition counters).
    public static JsonArray OnEndAttack(AccountState gs, MissionCatalog catalog, ItemCatalog items,
                                        AttackContext ctx, Dictionary<int, int[]> progress)
    {
        var nots = new JsonArray();
        foreach (var def in catalog.All)
        {
            if (def.Category != "Attack" || def.Conditions.Count == 0) continue;   // server scores only Attack missions
            if (!ScopeMatches(def, ctx.CastleId, ctx.CastleType)) continue;
            var obj = gs.Objectives.FirstOrDefault(o => o.ObjectiveId == def.Id);
            if (obj is null || obj.Status == 2) continue;                           // only an UNLOCKED, not-yet-done mission

            var ctr = progress.TryGetValue(def.Id, out var c) && c.Length == def.Conditions.Count
                ? c : new int[def.Conditions.Count];
            bool allMet = true;
            for (int i = 0; i < def.Conditions.Count; i++)
            {
                var cond = def.Conditions[i];
                int need = cond.Kind == "Destroyed" ? cond.Count : 1;
                switch (cond.Kind)
                {
                    case "CastleEntered":  ctr[i] = 1; break;                        // reaching EndAttack on the castle == entered it
                    case "CastleCompleted": if (ctx.Completed) ctr[i] = 1; break;
                    case "Destroyed":      ctr[i] += ctx.KilledSpecCounts.TryGetValue(cond.SpecContainerId, out var k) ? k : 0; break;
                    // build/unknown kinds never accumulate here → never auto-complete server-side
                }
                if (ctr[i] < need) allMet = false;
            }
            progress[def.Id] = ctr;
            if (!allMet) continue;

            obj.Status = 2; obj.LastStatusUtc = DateTime.UtcNow;                     // persisted → reconnect sees it done
            progress.Remove(def.Id);
            nots.Add(Notifications.ObjectiveCompleted(def.Id));
            foreach (var r in def.Rewards) EmitReward(r, gs, items, nots);

            // Chain-unlock: push a type-17 in the same response to unlock the next objective whenever its
            // Requirements are now fully satisfied. The backend owns this unlock (the FTUE assignment VM does
            // not drive it client-side), so the server emits it.
            foreach (var next in catalog.All)
            {
                if (next.Id == def.Id || next.RequiresObjectiveIds.Count == 0) continue;
                if (gs.Objectives.Any(o => o.ObjectiveId == next.Id)) continue;   // already unlocked/completed
                if (!next.RequiresObjectiveIds.All(r => gs.Objectives.Any(o => o.ObjectiveId == r && o.Status == 2))) continue;
                var now = DateTime.UtcNow;
                gs.Objectives.Add(new Objective { AccountId = gs.AccountId, ObjectiveId = next.Id, Status = 1, LastStatusUtc = now });
                nots.Add(Notifications.ObjectiveUnlocked(next.Id, now));
            }
        }
        return nots;
    }

    // TEMPORARY: fast-forward the currently-active User/PvP attack objective. User-castle raids can't validate
    // without a real defended castle (unlocks only at castle-renovation rank 4), so when such an objective is
    // unlocked (Status 1) the next castle finish grants its rewards + completes it + emits the type-17 chain-
    // unlock. Remove once a real defended castle exists (along with the call in GameEndpoints.EndAttack).
    //
    // Only ever completes the currently-active (Status 1) attack objective, never a still-locked one — so the
    // forest and first-time witch tutorials (no attack objective active) grant nothing; this only kicks in once
    // the client unlocks Tybalt (obj 300), then 301, 302, … each on the next castle finish.
    public static JsonArray FastForwardNextObjective(AccountState gs, MissionCatalog catalog, ItemCatalog items)
    {
        var nots = new JsonArray();
        // Pick the active PvP mission: unlocked (Status 1) + Attack + CastleTypes["User"]. Only User/PvP
        // objectives use this workaround; PvE objectives (302-306, fixed to real dungeons) complete via
        // OnEndAttack natural condition tracking.
        var pick = gs.Objectives.Where(o => o.Status == 1)
            .Select(o => (obj: o, def: catalog.All.FirstOrDefault(d => d.Id == o.ObjectiveId)))
            .Where(x => x.def is { Category: "Attack" }
                     && x.def.CastleTypes.Any(t => string.Equals(t, "User", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.obj.ObjectiveId)
            .FirstOrDefault();
        if (pick.def is null) return nots;                                           // no active PvP mission -> grant nothing
        var def = pick.def; var obj = pick.obj;

        var now = DateTime.UtcNow;
        obj.Status = 2; obj.LastStatusUtc = now;                                      // complete the active mission
        nots.Add(Notifications.ObjectiveCompleted(def.Id));
        foreach (var r in def.Rewards) EmitReward(r, gs, items, nots);              // this mission's rewards

        foreach (var next in catalog.All)                                          // type-17 unlock any now-eligible next
        {
            if (next.Id == def.Id || next.RequiresObjectiveIds.Count == 0) continue;
            if (gs.Objectives.Any(o => o.ObjectiveId == next.Id)) continue;
            if (!next.RequiresObjectiveIds.All(r => gs.Objectives.Any(o => o.ObjectiveId == r && o.Status == 2))) continue;
            gs.Objectives.Add(new Objective { AccountId = gs.AccountId, ObjectiveId = next.Id, Status = 1, LastStatusUtc = now });
            nots.Add(Notifications.ObjectiveUnlocked(next.Id, now));
        }
        return nots;
    }

    static bool ScopeMatches(MissionCatalog.Def d, int castleId, string castleType)
    {
        if (d.CastleId is { } cid) return cid == castleId;                           // specific campaign castle (e.g. 100)
        if (d.CastleTypes.Count > 0)                                                 // a class of castle (e.g. "User" = PvP)
            return d.CastleTypes.Any(t => string.Equals(t, castleType, StringComparison.OrdinalIgnoreCase));
        return false;
    }

    static void EmitReward(MissionCatalog.Reward r, AccountState gs, ItemCatalog items, JsonArray nots)
    {
        switch (r.Kind)
        {
            case "Materials":   // → one type-111 InboxConsumableItem per unit
                foreach (var (mid, qty) in r.Materials)
                {
                    gs.CraftingMaterials[mid] = gs.CraftingMaterials.GetValueOrDefault(mid) + qty;
                    for (int n = 0; n < qty; n++)
                    {
                        var oid = gs.NextObjectId();
                        var consumable = new JsonObject { ["StackCount"] = 1, ["TemplateId"] = mid };
                        gs.Inbox[oid] = (JsonObject)consumable.DeepClone();
                        nots.Add(Notifications.InboxItemsAdded("InboxConsumableItem", consumable, 4, oid));
                    }
                }
                break;

            case "Currency":    // → type-24 WalletUpdated (IGC=2, LifeForce=4, PremiumCash=1)
            {
                int credited = CreditCurrency(gs, r.CurrencyType, r.Amount);
                if (credited > 0) nots.Add(Notifications.WalletUpdated((CurrencyInt(r.CurrencyType), credited)));
                break;
            }

            case "Xp":          // → type-43 HeroXpChanged (LevelChanged set if the grant crosses a threshold)
                if (gs.Hero is { } hero && r.Xp > 0)
                {
                    int pre = hero.Level, total = hero.AddXp(r.Xp);
                    nots.Add(Notifications.HeroXpChanged(gs.HeroClass, r.Xp, total, hero.Level, hero.Level > pre));
                }
                break;

            case "Item":        // → type-111 InboxHeroEquipmentItem.
                // Mirror the item shape from the objective spec's reward `Item`. A pet (HeroNamedItem) carries
                // only {ItemLevel, TemplateId} — no per-instance stats/archetype (all its data is on the
                // template); adding gear fields makes the client crash building the item model. Only real gear
                // (spec supplied an archetype) gets the full stat shape below.
                if (SkipItemRewards) break;
                {
                    var heroItem = new JsonObject { ["ItemLevel"] = r.ItemLevel, ["TemplateId"] = r.ItemTemplateId };
                    if (r.ItemArchetypeId > 0)   // real gear (spec supplied an archetype) → full stat shape
                    {
                        heroItem["ArchetypeId"] = r.ItemArchetypeId;
                        heroItem["PrimaryStatsModifiers"] = new JsonArray(0.4, 0.4, 0.4);
                        heroItem["IsSellable"] = true;
                    }
                    var oid = gs.NextObjectId();
                    gs.Inbox[oid] = (JsonObject)heroItem.DeepClone();
                    nots.Add(Notifications.InboxItemsAdded("InboxHeroEquipmentItem", heroItem, 3, oid));
                }
                break;
        }
    }

    static int CurrencyInt(string? t) => t switch { "PremiumCash" => 1, "IGC" => 2, "LifeForce" => 4, _ => 0 };
    static int CreditCurrency(AccountState gs, string? t, int amount) => t switch
    {
        "IGC" => gs.CreditGold(amount),
        "LifeForce" => gs.CreditLifeForce(amount),
        "PremiumCash" => Inc(() => gs.PremiumCash += amount, amount),
        _ => 0,
    };
    static int Inc(Action a, int amount) { a(); return amount; }
}
