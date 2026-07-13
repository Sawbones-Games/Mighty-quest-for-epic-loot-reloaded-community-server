using System.Text.Json.Nodes;
using MQEL.Core.Model;

// Boundary translator: DB entity (MQEL.Core.Model.Account) <-> the working AccountState the FTUE handlers
// mutate. Keeps the handlers free of EF: they only ever see AccountState. ToAccountState runs on load,
// ApplyTo flushes back onto the TRACKED entity graph (merge by key, so EF emits real add/update/delete deltas).
static class AccountMapper
{
    const int Premium = 1, Gold = 2, LifeForce = 4;   // CurrencyType

    // ---- DB -> working model (load) -------------------------------------------------------------------
    // `scratch` is the per-account transient combat cache; AccountState delegates its attack tables to it,
    // so the loot survives the StartAttack->EndAttack request pair without being persisted.
    public static AccountState ToAccountState(Account e, AttackScratch scratch)
    {
        var s = new AccountState
        {
            AccountId = e.AccountId,
            DisplayName = e.DisplayName,
            CastleClaimed = e.CastleClaimed,
            CastleRenovationLevel = e.CastleRenovationLevel,
            InventorySeq = e.InventorySeq,
            PremiumCash = (int)(e.Wallets.FirstOrDefault(w => w.CurrencyType == Premium)?.Amount ?? 0),
            Attack = scratch,
          CompletedAssignments = e.CompletedAssignments.OrderBy(c => c.CompletedUtc).Select(c => c.AssignmentId).ToList(),
            Objectives = e.Objectives.OrderBy(o => o.ObjectiveId).Select(o => new Objective
            {
                AccountId = o.AccountId,
                ObjectiveId = o.ObjectiveId,
                Status = o.Status,
                LastStatusUtc = o.LastStatusUtc
            }).ToList(),
        };
        if (e.Wallets.FirstOrDefault(w => w.CurrencyType == Gold) is { } g)
        { s.InGameCoin = (int)g.Amount; s.InGameCoinStorageCapacity = (int)g.Capacity; }
        if (e.Wallets.FirstOrDefault(w => w.CurrencyType == LifeForce) is { } lf)
        { s.LifeForce = (int)lf.Amount; s.LifeForceStorageCapacity = (int)lf.Capacity; }

        if (e.Heroes.FirstOrDefault() is { } hero)
        {
            var h = new HeroState { Class = hero.HeroClass, Level = hero.Level, Xp = hero.Xp };
            h.Consumables = hero.Consumables.Select(c => c.TemplateId).ToList();
            foreach (var gi in hero.Gear)
                h.Gear[gi.Slot] = GearJson(gi.TemplateId, gi.ArchetypeId, gi.ItemLevel, gi.DyeTemplateId, gi.Stat0, gi.Stat1, gi.Stat2);
            foreach (var iv in hero.Inventory)
                h.Inventory[iv.Slot] = GearJson(iv.TemplateId, iv.ArchetypeId, iv.ItemLevel, iv.DyeTemplateId, iv.Stat0, iv.Stat1, iv.Stat2);
            h.Spells = hero.Spells.OrderBy(sp => sp.SlotIndex).Select(sp => new JsonObject
            {
                ["SpellSpecContainerId"] = sp.SpellId,
                ["Level"] = sp.Level,
                ["SlotIndex"] = sp.SlotIndex,
            }).ToList();
            s.Hero = h;
        }
         foreach (var it in e.Inventory)   // the inbox (looted/rewarded, pre-collect)
            s.Inbox[it.ObjectId] = it.ItemType == 4      // §1.6 — consumables round-trip as {StackCount,TemplateId}, NOT gear
                ? new JsonObject { ["StackCount"] = it.StackCount, ["TemplateId"] = it.TemplateId }
                : GearJson(it.TemplateId, it.ArchetypeId, it.ItemLevel, it.DyeTemplateId, it.Stat0, it.Stat1, it.Stat2);
        foreach (var cm in e.CraftingMaterials.Where(cm => cm.Quantity > 0))
            s.CraftingMaterials[cm.MaterialId] = (int)cm.Quantity;
        return s;
    }

    // ---- working model -> DB (save) -------------------------------------------------------------------
    public static void ApplyTo(AccountState s, Account e)
    {
        e.DisplayName = s.DisplayName;
        e.SelectedHeroClass = s.HeroClass;
        e.CastleClaimed = s.CastleClaimed;
        e.CastleRenovationLevel = s.CastleRenovationLevel;
        e.InventorySeq = s.InventorySeq;
        // completed FTUE assignments — merge by AssignmentId (keep existing CompletedUtc, stamp new ones)
        e.CompletedAssignments.RemoveAll(c => !s.CompletedAssignments.Contains(c.AssignmentId));
 foreach (var aid in s.CompletedAssignments)
                if (e.CompletedAssignments.All(c => c.AssignmentId != aid))
                    e.CompletedAssignments.Add(new CompletedAssignment { AccountId = e.AccountId, AssignmentId = aid, CompletedUtc = DateTime.UtcNow });
        // objectives — merge by ObjectiveId
        e.Objectives.RemoveAll(o => !s.Objectives.Any(x => x.ObjectiveId == o.ObjectiveId));
        foreach (var obj in s.Objectives)
        {
            var existing = e.Objectives.FirstOrDefault(x => x.ObjectiveId == obj.ObjectiveId);
           if (existing is null) { existing = new Objective { AccountId = e.AccountId, ObjectiveId = obj.ObjectiveId }; e.Objectives.Add(existing); }
            existing.Status = obj.Status;
            existing.LastStatusUtc = obj.LastStatusUtc;
        }
        // crafting materials — merge by MaterialId
        e.CraftingMaterials.RemoveAll(cm => !s.CraftingMaterials.ContainsKey(cm.MaterialId));
        foreach (var (id, qty) in s.CraftingMaterials)
        {
            var existing = e.CraftingMaterials.FirstOrDefault(x => x.MaterialId == id);
          if (existing is null) { existing = new CraftingMaterial { AccountId = e.AccountId, MaterialId = id }; e.CraftingMaterials.Add(existing); }
            existing.Quantity = qty;
        }
        UpsertWallet(e, Gold, s.InGameCoin, s.InGameCoinStorageCapacity);
        UpsertWallet(e, LifeForce, s.LifeForce, s.LifeForceStorageCapacity);
        if (s.PremiumCash > 0) UpsertWallet(e, Premium, s.PremiumCash, 0);

        // inbox (looted/rewarded items, pre-collect) — merge by ObjectId
        e.Inventory.RemoveAll(it => !s.Inbox.ContainsKey(it.ObjectId));
        foreach (var (oid, j) in s.Inbox)
        {
            var it = e.Inventory.FirstOrDefault(x => x.ObjectId == oid);
            if (it is null) { it = new InventoryItem { ObjectId = oid, AccountId = e.AccountId }; e.Inventory.Add(it); }
            it.TemplateId = Int(j["TemplateId"]);
            // §1.6 — a consumable/material reward is inboxed as {StackCount,TemplateId} (no gear stats). Persist it as
            // ItemType 4 with its StackCount, NOT as ItemType-3 gear (which drops StackCount and fabricates stats on reload).
            if (j["StackCount"] is not null)
            {
                it.ItemType = 4; it.StackCount = Int(j["StackCount"], 1);
                it.ArchetypeId = 0; it.ItemLevel = 1; it.DyeTemplateId = 0; it.Stat0 = it.Stat1 = it.Stat2 = 0;
            }
            else
            {
                var psm = j["PrimaryStatsModifiers"] as JsonArray;
                it.ItemType = 3; it.StackCount = 1;
                it.ArchetypeId = Int(j["ArchetypeId"]); it.ItemLevel = Int(j["ItemLevel"], 1);
                it.DyeTemplateId = Int(j["DyeTemplateId"]); it.Stat0 = Stat(psm, 0); it.Stat1 = Stat(psm, 1); it.Stat2 = Stat(psm, 2);
            }
        }

        if (s.Hero is null) { e.Heroes.Clear(); return; }
        e.Heroes.RemoveAll(h => h.HeroClass != s.Hero.Class);              // single-hero FTUE
        var hero = e.Heroes.FirstOrDefault();
        if (hero is null) { hero = new Hero { AccountId = e.AccountId, HeroClass = s.Hero.Class }; e.Heroes.Add(hero); }
        hero.Level = s.Hero.Level;
        hero.Xp = s.Hero.Xp;

        // consumables — merge by TemplateId
        hero.Consumables.RemoveAll(c => !s.Hero.Consumables.Contains(c.TemplateId));
        foreach (var t in s.Hero.Consumables)
            if (hero.Consumables.All(c => c.TemplateId != t))
                hero.Consumables.Add(new HeroConsumable { TemplateId = t, StackCount = 1 });

        // gear — merge by Slot
        hero.Gear.RemoveAll(g => !s.Hero.Gear.ContainsKey(g.Slot));
        foreach (var (slot, j) in s.Hero.Gear)
        {
            var g = hero.Gear.FirstOrDefault(x => x.Slot == slot);
            if (g is null) { g = new HeroGearItem { Slot = slot }; hero.Gear.Add(g); }
            var psm = j["PrimaryStatsModifiers"] as JsonArray;
            g.TemplateId = Int(j["TemplateId"]);
            g.ArchetypeId = Int(j["ArchetypeId"]);
            g.ItemLevel = Int(j["ItemLevel"], 1);
            g.DyeTemplateId = Int(j["DyeTemplateId"]);
            g.Stat0 = Stat(psm, 0); g.Stat1 = Stat(psm, 1); g.Stat2 = Stat(psm, 2);
        }

        // spells — merge by SpellId
        var spellIds = s.Hero.Spells.Select(sp => Int(sp["SpellSpecContainerId"])).ToHashSet();
        hero.Spells.RemoveAll(sp => !spellIds.Contains(sp.SpellId));
        for (int i = 0; i < s.Hero.Spells.Count; i++)
        {
            var src = s.Hero.Spells[i];
            int id = Int(src["SpellSpecContainerId"]);
            var sp = hero.Spells.FirstOrDefault(x => x.SpellId == id);
            if (sp is null) { sp = new HeroSpell { SpellId = id }; hero.Spells.Add(sp); }
            sp.Level = Int(src["Level"], 1);
            sp.SlotIndex = Int(src["SlotIndex"], i);
        }

        // hero inventory (collected/bought, pre-equip) — merge by Slot
        hero.Inventory.RemoveAll(iv => !s.Hero.Inventory.ContainsKey(iv.Slot));
        foreach (var (slot, j) in s.Hero.Inventory)
        {
            var iv = hero.Inventory.FirstOrDefault(x => x.Slot == slot);
            if (iv is null) { iv = new HeroInventoryItem { Slot = slot }; hero.Inventory.Add(iv); }
            var psm = j["PrimaryStatsModifiers"] as JsonArray;
            iv.TemplateId = Int(j["TemplateId"]); iv.ArchetypeId = Int(j["ArchetypeId"]); iv.ItemLevel = Int(j["ItemLevel"], 1);
            iv.DyeTemplateId = Int(j["DyeTemplateId"]); iv.Stat0 = Stat(psm, 0); iv.Stat1 = Stat(psm, 1); iv.Stat2 = Stat(psm, 2);
        }
    }

    static void UpsertWallet(Account e, int type, long amount, long capacity)
    {
        var w = e.Wallets.FirstOrDefault(x => x.CurrencyType == type);
        if (w is null) { w = new Wallet { AccountId = e.AccountId, CurrencyType = type }; e.Wallets.Add(w); }
        w.Amount = amount; w.Capacity = capacity;
    }

    static int Int(JsonNode? n, int fallback = 0) => (int?)n ?? fallback;
    static double Stat(JsonArray? a, int i) => (a is not null && i < a.Count) ? (double?)a[i] ?? 0 : 0;

    // The HeroEquipmentItem JsonObject (the contract the GAI + gear/inventory share).
    static JsonObject GearJson(int templateId, int archetypeId, int itemLevel, int dye, double s0, double s1, double s2) => new()
    {
        ["Type"] = "HeroEquipmentItem",
        ["ItemLevel"] = itemLevel,
        ["ArchetypeId"] = archetypeId,
        ["PrimaryStatsModifiers"] = new JsonArray(s0, s1, s2),
        ["TemplateId"] = templateId,
        ["DyeTemplateId"] = dye,
    };
}

