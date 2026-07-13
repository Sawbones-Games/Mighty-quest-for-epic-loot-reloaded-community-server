using System.Text.Json.Nodes;
using MQEL.Core.Model;

// Data-driven mission engine. Replaces the hardcoded per-objective scoring that used to live in EndAttack.
// On every EndAttack it scores the raid against EVERY active "Attack" mission (from MissionCatalog) scoped to
// the attacked castle, accumulates per-condition progress (so a mission may span multiple raids/castles), and
// when ALL of a mission's conditions are met it completes the mission + emits the reward notifications in THAT
// castle's EndAttack response. Build/Client missions are NOT scored here (the client completes them; the server
// only owes their reward — wire that when we learn how the client signals build completion).
//
// Progress accumulation is held in a per-account IN-MEMORY cache (the `progress` dict the caller supplies,
// mirroring AttackScratch). It survives between EndAttacks within a session; it does NOT yet persist across a
// reconnect — single-castle missions (all current campaign Attack objectives) complete in one raid, so that gap
// only affects a hypothetical multi-castle mission interrupted by a reconnect. Persisting it is the documented
// next increment. See code-analysis/rest-api/objectives.md.
static class MissionManager
{
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
            // ORDER-CRITICAL (crash-bisected via /api/rewardstage 2026-07-12): emit the reward ITEMS (type-111)
            // BEFORE ObjectiveCompleted (type-14). The client's objective-complete popup renders the inbox reward
            // items; if type-14 fires before the items are in the inbox, it binds a null model → crash (the
            // InboxController / null GameStateManager "current entity" the x32dbg trace pinned). Items-first =
            // clean (stages 1-5 all passed once the items preceded the completion).
            foreach (var r in def.Rewards) EmitReward(r, gs, items, nots);
            nots.Add(Notifications.ObjectiveCompleted(def.Id));

            // Chain-unlock: reference capture confirms the real backend pushes type-17 in the SAME response to
            // unlock the next objective (objectives.md §3.2 — "{...AccountObjective:{ObjectiveId:303,Status:1,
            // ...},NotificationType:17}"). We'd assumed the FTUE's own assignment VM (e.g. 000180) would drive
            // this instead via its own UnlockObjectiveAssignmentActionSpec — confirmed WRONG for at least one
            // real case (301 after 300: the assignment's bare AssignmentCompletedAssignmentTriggerSpec never
            // fires client-side, live-verified via CDP objective_getAllObjectives showing UnlockedObjectives:[]
            // indefinitely). Push it ourselves whenever a mission's Requirements are now fully satisfied — the
            // authoritative, already-proven mechanism, not a new one.
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

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────
    // ⚠️ TEMPORARY CHEAT (2026-07-10) — REMOVE after bootstrapping to base-building.
    // Reason: the attack-tutorial castles 101-104/14 (objectives 302-306) aren't built, and User-castle raids
    // can't validate (need a real defended castle, which unlocks only at castle-renovation rank 4 = base
    // building). To break that chicken-and-egg, when the game has ASKED the player for a castle mission (an
    // "Attack" objective the client has UNLOCKED = Status 1), the NEXT castle finish (ANY castle) grants that
    // mission's exact rewards + completes it + the type-17 chain-unlock — one mission per castle finish.
    //
    // CRITICAL — do NOT advance prematurely: only ever complete the currently-ACTIVE (Status 1) attack
    // objective, NEVER one that isn't unlocked yet. That way the forest + first-time witch/Bewarewich tutorials
    // (no attack objective active yet) grant NOTHING; the fast-forward only kicks in once the client asks for
    // Tybalt (obj 300 → ObjectiveUnlockCommand → Status 1), then 301, 302, … each on the next castle finish.
    // Once we have a real defended castle, DELETE this + the call in GameEndpoints.EndAttack and implement the
    // chain correctly. Tracking: project-pvp-tutorial-bot-castles memory (defender-entity finding).
    public static JsonArray FastForwardNextObjective(AccountState gs, MissionCatalog catalog, ItemCatalog items)
    {
        var nots = new JsonArray();
        // The one PvP mission the game is asking for RIGHT NOW: unlocked (Status 1) + Attack + CastleTypes["User"].
        // ONLY User/PvP objectives (e.g. 301 "Friendly Pillage") get the workaround — they can't validate without a
        // real defended castle. PvE objectives (302-306, fixed CastleId to real dungeons) are done PROPERLY: the
        // player attacks the real castle and MissionManager.OnEndAttack completes them via natural condition tracking.
        var pick = gs.Objectives.Where(o => o.Status == 1)
            .Select(o => (obj: o, def: catalog.All.FirstOrDefault(d => d.Id == o.ObjectiveId)))
            .Where(x => x.def is { Category: "Attack" }
                     && x.def.CastleTypes.Any(t => string.Equals(t, "User", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.obj.ObjectiveId)
            .FirstOrDefault();
        if (pick.def is null) return nots;                                           // no active PvP mission -> grant NOTHING
        var def = pick.def; var obj = pick.obj;

        var now = DateTime.UtcNow;
        obj.Status = 2; obj.LastStatusUtc = now;                                      // complete the ACTIVE mission
        foreach (var r in def.Rewards) EmitReward(r, gs, items, nots);              // rewards FIRST (items before type-14 — see OnEndAttack note)
        nots.Add(Notifications.ObjectiveCompleted(def.Id));

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
            case "Materials":   // → one type-111 InboxConsumableItem per unit (verified delivery path, objectives.md §3.3)
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
                // Deliver the item as the objective spec defines its reward `Item`: for the obj-303 "pet Nigel"
                // (84349, a HeroNamedItem) that's minimal {ItemLevel, TemplateId} — a named item carries no
                // per-instance archetype/stats (all its data lives on the template). Regular equipment rewards
                // with a real archetype get the full stat shape below. (The obj-303 crash was NOT this shape —
                // it was reward ITEMS being emitted after ObjectiveCompleted; see the emit-order note above.)
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
