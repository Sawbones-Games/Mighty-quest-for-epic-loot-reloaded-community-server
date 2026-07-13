using MQEL.Core.Model;
using System.Text.Json.Nodes;

// Stateful account model. The GetAccountInformation body is GENERATED from this — never a canned snapshot.
// State accrues exactly as the player progresses (project-ftue-progression): boot (no hero/castle) →
// pick+name castle (empty) → ChooseFirstHero (no gear/spells) → forest dungeon (gold + 1st loot) →
// equip loot → store (buy+equip weapon) → witch dungeon → skill earned at level 2. Mirrors
// MQELOffline_cpp Source/Backend (Wallet/Hero). NOTHING is fabricated or pre-seeded.
class AccountState
{
    public long AccountId { get; set; } = 3123971;          // our test account id
    public string DisplayName { get; set; } = "Champion";   // set at ChooseDisplayName

    public HeroState? Hero { get; set; }                     // null until ChooseFirstHero
    public int HeroClass => Hero?.Class ?? 0;
    public int HeroLevel => Hero?.Level ?? 1;

    // Wallet (Backend::Wallet — amount + capacity per currency). IGC starts 1000 (DEFAULTACCOUNT);
    // LifeForce starts 0. Capacities are the rank-1 GoldStorage/LifeForceStorage spec values — TO BE
    // VERIFIED live via CDP (a fresh empty castle may deliver them differently; see WalletCapacityUpdated).
    public int InGameCoin { get; set; } = 1000;
    public int LifeForce { get; set; } = 0;
    public int InGameCoinStorageCapacity { get; set; } = 2000;
    public int LifeForceStorageCapacity { get; set; } = 2000;
    public int PremiumCash { get; set; } = 0;

    public bool CastleClaimed { get; set; }                 // StarterCastleSelection (step 2): named, empty, can't build yet

    // 0-based (RenovationLevel1=0 .. RenovationComplete=4 per CastleRenovationCatalog). Advanced + persisted
    // when the client reports a SetCastleRenovationLevelAssignmentActionSpec via ExecuteAssignmentActionCommand
    // (see GameEndpoints.cs SendCommands) — the one server-bound piece of the castle-build system.
    public int CastleRenovationLevel { get; set; }
    //  FTUE/tutorial assignment ids the player has finished (from CompleteAssignmentCommand). The hero-created GAI
    // emits these as CompletedAssignments so the client SKIPS done steps on reconnect instead of restarting. List
    // (not set) to keep completion order, which mirrors the reference body. Deduped on insert.
    public List<int> CompletedAssignments { get; set; } = new();

    // Objectives (campaign quests) with their status. Status: 0=not seen, 1=active/in-progress, 2=completed.
    // Persisted so objective state survives reconnect.
   public List<Objective> Objectives { get; set; } = new();

    // Crafting materials owned. Persisted like wallets; granted by objective completions.
    public Dictionary<int, int> CraftingMaterials { get; set; } = new();

    // Looted items waiting in the inbox (keyed by their 24-hex ObjectId), produced by EndAttack and pulled into
    // the hero inventory by InboxCollectToHeroInventoryCommand. Persisted so loot survives a reconnect pre-collect.
    public Dictionary<string, JsonObject> Inbox { get; set; } = new();

    // --- Combat reward scoring (EndAttack) ---
    // The EndAttack request reports WHICH placed instances the client looted (by Id) — NOT amounts. The server
    // sums the loot tables it sent in the matching StartAttack to score the real gold/lifeforce/xp and hand back
    // the looted items. Verified: summing a real capture's LootedGold/LifeForceCreatureIds == the player's +34/+34.
    // This combat scratch is TRANSIENT — held in a per-account in-memory cache (so it survives the
    // StartAttack->EndAttack request pair) and never persisted; it is re-derivable from the attacked castle.
    // AccountState delegates to it, so the handlers (gameState.AttackCreatureLoot, .LastAttackCastle, …) are unchanged.
    public AttackScratch Attack { get; set; } = new();
    public int LastAttackCastle { get => Attack.LastAttackCastle; set => Attack.LastAttackCastle = value; }
    public Dictionary<int, (int Gold, int Xp, int LifeForce)> AttackCreatureLoot => Attack.CreatureLoot;
    public Dictionary<int, List<JsonObject>> AttackCreatureItems => Attack.CreatureItems;  // creature Id -> dropped gear
    public Dictionary<int, List<JsonObject>> AttackTrapLoot => Attack.TrapLoot;             // trap ItemId -> dropped gear
    public Dictionary<int, int> AttackCreatureSpec => Attack.CreatureSpecById;              // creature instance Id -> SpecContainerId
    public Dictionary<int, int> AttackDecorationSpec => Attack.DecorationSpecById;          // decoration instance Id -> SpecContainerId
    public Dictionary<int, int> AttackBuildingSpec => Attack.BuildingSpecById;              // building instance Id -> SpecContainerId
    public int InventorySeq { get; set; } = 0x10;

    // Per-class first-loot TemplateId the forest trap drops — class-appropriate (the tutorial gives gear your hero
    // can equip). Archer 81 = padded cloth tunic (verified equippable). Knight 17 / Mage 53 / Runaway 311.
    public static int ClassFirstLootTemplate(int cls) => cls switch { 2 => 17, 3 => 81, 4 => 53, 5 => 311, _ => 81 };

    // Credit currency, clamped to storage capacity (can't hold past MaxGold/MaxLifeForce). Returns the ACTUAL gain
    // (the type-24 WalletUpdated delta the client adds to its pre-attack balance).
    public int CreditGold(int amount) { int b = InGameCoin; InGameCoin = Math.Min(InGameCoin + Math.Max(0, amount), InGameCoinStorageCapacity); return InGameCoin - b; }
    public int CreditLifeForce(int amount) { int b = LifeForce; LifeForce = Math.Min(LifeForce + Math.Max(0, amount), LifeForceStorageCapacity); return LifeForce - b; }

    // A fresh 24-hex inbox ObjectId for a looted item (the client tracks/equips items by it).
    public string NextObjectId() => (++InventorySeq).ToString("x").PadLeft(24, '0');

    // Reference Backend::Hero::Createhero + HeroService::ChooseFirstHero — a new hero: Level 1, XP 0, one
    // consumable (template 1), AttackRegion 1 unlocked, NO spells, plus the class's BASIC STARTER GEAR
    // (default weapon + armor). A bare hero with no weapon CRASHES the client. The forest dungeon then drops
    // the FIRST looted item on top of this set. (project-ftue-progression; gear IDs from the reference.)
    public HeroState CreateHero(int cls)
    {
        var h = new HeroState { Class = cls };
        void Eq(string slot, int arch, int tpl) => h.Gear[slot] = HeroState.GearItem(arch, tpl);
        switch (cls)
        {
            case 2: // Knight  (Board-with-a-nail + armor)
                Eq("MainHand", 2, 108); Eq("Head", 8, 109); Eq("Body", 8, 110); Eq("Hands", 8, 111); Eq("Shoulders", 8, 132); break;
            case 3: // Archer  (Pea shooter crossbow + armor)
                Eq("MainHand", 2, 124); Eq("Head", 8, 125); Eq("Body", 8, 126); Eq("Hands", 8, 127); Eq("Shoulders", 8, 133); break;
            case 4: // Mage    (Twig staff [weapon archetype 9] + armor)
                Eq("MainHand", 9, 128); Eq("Head", 8, 129); Eq("Body", 8, 130); Eq("Hands", 8, 131); Eq("Shoulders", 8, 131); break;
            case 5: // Runaway — real starter items unresearched (reference asserts/crashes on them). The Knight
                    // set lets the hero be created (reference note) but is a known-incomplete stand-in. TODO: find real items.
                Eq("MainHand", 2, 108); Eq("Head", 8, 109); Eq("Body", 8, 110); Eq("Hands", 8, 111); Eq("Shoulders", 8, 132); break;
        }
        Hero = h;
        return Hero;
    }

    // Generate the AccountInformation Result from the clean first-run base + real state.
    // firstRunResult = the "Result" object of account-information-firstrun.json (static boilerplate +
    // minimal EMPTY castle + IGC wallet) — itself faithful (reference first-run, no Ness199X).
    public JsonObject BuildAccountInformation(JsonObject firstRunResult)
    {
        var res = (JsonObject)firstRunResult.DeepClone()!;
        res["AccountId"] = AccountId;
        res["Wallet"] = BuildWallet();

        if (Hero != null)   // hero-created: add the hero-created-only fields from real state
        {
            res["DisplayName"] = DisplayName;
            res["SelectedHeroId"] = Hero.Class;
            res["Privileges"] = 401;
            res["GamerScore"] = 0;
            res["CastleRenovationLevel"] = CastleRenovationLevel;
            res["Heroes"] = new JsonArray(Hero.Serialize());
            res["CompletedAchievements"] = new JsonArray();
  res["Stats"] = new JsonObject();
            // Objectives (campaign quests). LIVE FINDING (CDP getAllObjectives): the engine only TRACKS +
            // completes an objective whose state is seeded in this GAI account model — a runtime
            // UnlockObjectiveAssignmentActionSpec alone puts it in the UI list but its conditions never
            // commit (all stay Status 0), so ObjectiveCompletedAssignmentTriggerSpec never fires → 005006
            // never starts. So we MUST seed the unlocked objectives here from real state (gameState.Objectives,
            // populated by ObjectiveUnlockCommand + persisted). SHAPE PROBE: the exact AccountObjective field
            // names are unconfirmed — emit several candidates so the engine's "Unhandled member [X] in class
            // [Y]" JSON-warning (GameData/Bin/MQLog.txt) names the real ones in one reboot. Trim to the
            // confirmed shape once known. (objectives.md §3.1)
            // AccountObjective shape (GROUND TRUTH from the reference ObjectiveUnlockedNotification capture):
            // { ObjectiveId, Status, LastStatusDate }. Emit the player's unlocked/completed objectives so the
            // engine's metagame model is correct on reconnect (Status 1 active / 2 completed). Completion itself
            // is driven by the EndAttack ObjectiveCompletedNotification (type 14), not by this seed.
            var objArr = new JsonArray();
            foreach (var o in Objectives)
                objArr.Add(new JsonObject
                {
                    ["ObjectiveId"] = o.ObjectiveId,
                    ["Status"] = o.Status,             // 0 not-seen / 1 active / 2 completed
                    ["LastStatusDate"] = o.LastStatusUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                });
            res["Objectives"] = objArr;
            // NOTE: a GAI `ClientCraftingMaterials` seed was tried (2026-06-30) to surface owned crafting materials
            // for the castle-renovation panel, but REVERTED — the account model is frozen at boot, so a GAI field
            // only updates on reconnect, which is the WRONG model. Crafting-material delivery must be IN-SESSION,
            // client-driven (the same shape as the ability/level fix: the engine already has the reward locally from
            // OBJECTIVESETTINGS; the server's job is the in-session signal, not a re-seed). See objectives.md §3.3.
            // Reference MQELOffline_cpp serves CompletedAssignments as a flat int[] in the hero-created body; the
            // client reads it on boot to skip finished FTUE steps. Empty here = the client restarts the tutorial.
            res["CompletedAssignments"] = new JsonArray(CompletedAssignments.Select(a => (JsonNode)a).ToArray());
            res["LeagueId"] = 1;
            res["SubLeagueId"] = 1;
            // Keep the minimal EMPTY castle from the first-run base (no rooms; built later); stamp ownership.
            if (res["BuildInfo"]?["Draft"] is JsonObject draft)
            {
                draft["AccountId"] = AccountId;
                draft["AccountDisplayName"] = DisplayName;
            }
        }
        return res;
    }

    JsonObject BuildWallet()
    {
        // First-run wallet is just InGameCoin (reference). Hero-created adds LifeForce + capacities.
        var w = new JsonObject { ["InGameCoin"] = InGameCoin };
        if (Hero != null)
        {
            w["LifeForce"] = LifeForce;
            w["InGameCoinStorageCapacity"] = InGameCoinStorageCapacity;
            w["LifeForceStorageCapacity"] = LifeForceStorageCapacity;
            if (PremiumCash > 0) w["PremiumCash"] = PremiumCash;
        }
        return w;
    }

    // (Removed CheckAndCompleteAutoObjectives — it fabricated an "ObjectiveCompletedNotification" (type-112)
    //  that has NO contract in the dumped catalog, and persisted objective Status=2 which poisons replays.
    //  Objective completion is ENGINE-side: the assignment VM unlocks the objective and the engine completes
    //  it from its conditions, firing ObjectiveCompletedAssignmentTriggerSpec → the next assignment (005006).
    //  The server owes nothing at completion. See code-analysis/rest-api/objectives.md.)
}

// A single hero (the chosen class). Gear / Spells / Xp / Level accrue as the player plays — never pre-seeded.
class HeroState
{
    public int Class { get; set; }                                   // eHerotype: 2 Knight / 3 Archer / 4 Mage / 5 Runaway
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public List<int> Consumables { get; set; } = new() { 1 };        // EquippedConsumables (template 1)
    public Dictionary<string, JsonObject> Gear { get; set; } = new(); // slot name -> item (empty until looted/bought)
    public Dictionary<int, JsonObject> Inventory { get; set; } = new(); // inventory slot index -> item (collected/bought, pre-equip)
    public List<JsonObject> Spells { get; set; } = new();            // EquippedSpells (empty until earned/equipped)

    // Hero level XP curve — GeneralSettings/HEROSETTINGS.XpPerLevel (global, all classes). Index L-1 = total XP
    // required to BE level L: L1=0, L2=75, L3=400, L4=1000, … Matches the playtest (forest 62 XP stayed L1; the
    // witch crossed 75 mid-dungeon -> L2).
    static readonly int[] XpPerLevel = { 0, 75, 400, 1000, 3500, 5300, 8540, 12600, 18800, 26225, 36725, 48935,
        67655, 88975, 119075, 152825, 197945, 247925, 316775, 392300, 474800, 582530, 699350, 825620, 961700,
        1156700, 1365740, 1589300, 1887500, 2268560 };
    public static int LevelForXp(int xp) { int l = 1; for (int i = 1; i < XpPerLevel.Length && xp >= XpPerLevel[i]; i++) l = i + 1; return l; }

    // Accrue XP from combat and re-derive the level from the curve (the hero levels up the moment total XP crosses
    // a threshold — the same point the client levels up mid-dungeon). Returns the new total XP.
    public int AddXp(int xp) { Xp += Math.Max(0, xp); Level = LevelForXp(Xp); return Xp; }

    // A HeroEquipmentItem entry (contract: Type / ItemLevel / ArchetypeId / PrimaryStatsModifiers / TemplateId / DyeTemplateId).
    public static JsonObject GearItem(int archetypeId, int templateId) => new()
    {
        ["Type"] = "HeroEquipmentItem",
        ["ItemLevel"] = 1,
        ["ArchetypeId"] = archetypeId,
        ["PrimaryStatsModifiers"] = new JsonArray(0.4, 0.4, 0.4),
        ["TemplateId"] = templateId,
        ["DyeTemplateId"] = 0
    };

    // Numeric HeroEquipmentEquipCommand.DestinationSlot -> named equipment slot (the keys Gear uses). Confirmed
    // from the live wire: 3=Body, 8=MainHand. The full enum isn't decompiled yet; an unknown slot returns null
    // (the equip is skipped + logged) rather than guess. Extend as more slots are observed on the wire.
    public static string? SlotName(int destinationSlot) => destinationSlot switch
    {
        3 => "Body",
        8 => "MainHand",
        _ => null,
    };

    public JsonObject Serialize()
    {
        var o = new JsonObject { ["HeroSpecContainerId"] = Class, ["Level"] = Level };
        if (Xp > 0) o["XP"] = Xp;
        if (Gear.Count > 0)
        {
            var eq = new JsonObject();
            foreach (var kv in Gear) eq[kv.Key] = kv.Value.DeepClone();
            o["Equipment"] = eq;
        }
        if (Spells.Count > 0)
            o["EquippedSpells"] = new JsonArray(Spells.Select(s => (JsonNode)s.DeepClone()!).ToArray());
        if (Consumables.Count > 0)
            o["EquippedConsumables"] = new JsonArray(Consumables.Select(c => (JsonNode)new JsonObject { ["TemplateId"] = c }).ToArray());
        o["Stats"] = new JsonObject();
        o["AttackRegions"] = new JsonArray(new JsonObject { ["AttackRegionId"] = 1, ["Status"] = 2 });
        return o;
    }
}

// Transient per-attack combat scratch: the loot tables the server sends at StartAttack and scores against at
// EndAttack, plus the current target castle. Held in a per-account in-memory cache (survives the StartAttack->
// EndAttack request pair), never persisted — re-derivable from the attacked castle.
class AttackScratch
{
    public int LastAttackCastle { get; set; }
    public Dictionary<int, (int Gold, int Xp, int LifeForce)> CreatureLoot { get; } = new();
    public Dictionary<int, List<JsonObject>> CreatureItems { get; } = new();
    public Dictionary<int, List<JsonObject>> TrapLoot { get; } = new();
    // Placed-creature INSTANCE Id -> SpecContainerId for the attacked castle (built at StartAttack). Lets
    // EndAttack count kills BY SPEC (e.g. 20 chickens = spec 1081) to score objective conditions faithfully.
    public Dictionary<int, int> CreatureSpecById { get; } = new();
    // Same instance-Id -> SpecContainerId maps for DECORATIONS and BUILDINGS, so DefenseIngredientDestroyed
    // objective conditions of ItemType Decoration/Building can be scored (e.g. obj 303 "Break Nigel out of his
    // box" = destroy decoration spec 226; obj 304 = destroy 6 buildings spec 5). Instance Ids are NOT unique
    // across types, so each type needs its own map.
    public Dictionary<int, int> DecorationSpecById { get; } = new();
    public Dictionary<int, int> BuildingSpecById { get; } = new();
    // Captured from the EndAttack request so that subsequent requests can check what creatures were killed.
    public int[] LastAttackKilledCreatureIds { get; set; } = Array.Empty<int>();
    public int[] LastAttackDestroyedDecorationIds { get; set; } = Array.Empty<int>();
    // Building instance Ids of mines pillaged this raid (from EndAttack PillagedMines[].MineBuildingId) — lets
    // "destroy N buildings" objectives (e.g. obj 304 "destroy 6 gold mines" spec 5) score via BuildingSpecById.
    public int[] LastAttackPillagedMineBuildingIds { get; set; } = Array.Empty<int>();
}
