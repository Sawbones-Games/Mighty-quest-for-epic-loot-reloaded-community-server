using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQEL.Core.Model;
using MQEL.Core.Verification;

// The account-bearing game RPC services (the .hqs handlers): __state, GetAccountInformation, SendCommands,
// KeepAlive/Track, ChooseDisplayName/ChooseFirstHero, StartAttack, EndAttack. The per-account middleware loads
// the working AccountState into ctx.Items and saves it after this returns; shared services these handlers need
// are passed via GameDeps rather than captured. TryHandle returns null when no handler matched, so the caller
// falls through to the static file-backed dispatcher.
sealed record GameDeps(
    JsonSerializerOptions JsonOpts,
    Func<string, string> RespFile,
    Action<string> WireLog,
    ItemCatalog ItemCatalog,
    MissionCatalog MissionCatalog,
    ConcurrentDictionary<long, Dictionary<int, int[]>> MissionProgress,
    string AuditDir,
    ILogger Logger,
    AssignmentCatalog AssignmentCatalog,
    CastleRenovationCatalog CastleRenovation);

static class GameEndpoints
{
    // Campaign story castles (Ubisoft-owned, `responses/castles/<id>.json`). Anything else served by StartAttack
    // is the objective-301 PvP-tutorial bot pool (4/5/6/7), scored as CastleType "User" so the
    // CastleTypes:["User"]-scoped missions can complete.
    static readonly HashSet<int> CampaignCastleIds = new() { 2, 3, 100, 101, 102, 103, 104 };

    // Temporary toggle — fast-forward the attack-tutorial to base-building (MissionManager.FastForward…).
    // Set false / remove once a real defended castle is built and the chain is implemented.
    const bool FastForwardCheat = true;

    public static async Task<IResult?> TryHandle(HttpContext ctx, string path, AccountState gameState, GameDeps deps)
    {
    // Diagnostic snapshot of the live account.
    if (path.Contains("__state", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { gameState.HeroClass, gameState.HeroLevel, gameState.InGameCoin, gameState.LifeForce, gameState.InGameCoinStorageCapacity, gameState.LifeForceStorageCapacity, gameState.CastleClaimed, gameState.LastAttackCastle });

    // ACCOUNT LOAD — AccountInformationService.hqs/GetAccountInformation.
    // On boot the engine blocks until AccountServerController → ShopController → LobbyController are all ready.
    // AccountServerController dispatches a GetAccountInformationTask whose success callback sets the ready flag
    // only if the response deserializes as an AccountInformation, so a parseable response advances boot past the
    // Account gate — the field content matters for the lobby UI, not the ready flag itself.
    //
    // $type MUST be the concrete subclass "...AccountInformation..." (not ...Base) — the polymorphic Argo
    // deserializer selects the reader by $type. We send the scalar fields + empty typed-lists (empty array →
    // empty list). The complex typed objects (Wallet/BuildInfo/Stats/Inventory/BuyBack/HeroFreeTrialInfoPeriod)
    // are omitted, so the client leaves them at their ctor defaults rather than risk a malformed nested $type
    // crashing a sub-deserializer.
    if (path.Contains("GetAccountInformation", StringComparison.OrdinalIgnoreCase))
    {
        // gameState is process-global: a game relaunch without a server restart keeps prior state, so restart
        // the server for a fresh game session until per-account/session state exists.
        deps.WireLog($"GAI HeroClass={gameState.HeroClass} HeroLevel={gameState.HeroLevel} -> {(gameState.HeroClass != 0 ? "herocreated.L" + gameState.HeroLevel : "firstrun")}");
        // Generate the AccountInformation from the stateful account model, wrapped in the {"Result":...} envelope
        // the .hqs services use. Base = the clean first-run (no-hero) body (boilerplate + minimal empty castle +
        // IGC wallet); populated sub-objects prevent AccountServerController crashing on null Wallet/BuildInfo/
        // Inventory/Stats. Once a hero exists, BuildAccountInformation adds the hero-created fields (DisplayName/
        // SelectedHeroId/Heroes) from real state; otherwise the first-run body proceeds to hero selection.
        var firstRun = JsonNode.Parse(await File.ReadAllTextAsync(deps.RespFile("account-information-firstrun.json")))!;
        var result = gameState.BuildAccountInformation((JsonObject)firstRun["Result"]!);
        return Results.Content(new JsonObject { ["Result"] = result }.ToJsonString(deps.JsonOpts), "application/json");
    }

    // GAME command channel. The client POSTs a {"commands":[...]} batch; the server processes each and replies
    // with an empty {} (success, no notifications) or a {"Notifications":[...]} envelope — never {"commands":[]},
    // which the client rejects as undeserializable (SendCommandsTask failure 0x80044003 → "refreshing account"
    // infinite loop). The starter-flow BuyCommand ("BuyRandomCastle") must reply with a CastleBoughtNotification
    // (+ SkusModifiersUpdatedNotification) or the starter-castle purchase never completes; all other commands
    // (TrackingCommand, StartAssignmentCommand, ClientIdleCommand, …) just ack with {}.
    if (path.EndsWith("/SendCommands", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Request.Body.Position = 0;
        using var sr = new StreamReader(ctx.Request.Body, leaveOpen: true);
        var cmdBody = await sr.ReadToEndAsync();
        // Apply the client-authoritative command batch to the PERSISTED account, in order:
        //   CompleteAssignmentCommand        -> track completion (drives reconnect tutorial resume)
        //   InboxCollectToHeroInventoryCommand -> looted inbox item -> hero inventory slot
        //   BuyHeroItemCommand               -> resolve SKU from spec, -price, item -> hero inventory slot
        //   HeroEquipmentEquipCommand        -> hero inventory slot -> equipment slot (named)
        // Everything else just acks (the client batches many commands we don't need to model yet).
        try
        {
            using var cdoc = JsonDocument.Parse(cmdBody);
            if (cdoc.RootElement.TryGetProperty("commands", out var cmds) && cmds.ValueKind == JsonValueKind.Array)
                foreach (var c in cmds.EnumerateArray())
                {
                    var type = c.TryGetProperty("$type", out var ct) ? (ct.GetString() ?? "") : "";
                    try   // per-command isolation: one bad command must not drop the rest of the batch
                    {
                    if (type.Contains("CompleteAssignmentCommand"))
                    {
                        if (c.TryGetProperty("AssignmentId", out var aid) && aid.TryGetInt32(out var aidv) && !gameState.CompletedAssignments.Contains(aidv))
                            gameState.CompletedAssignments.Add(aidv);
                    }
                    else if (type.Contains("InboxCollectToHeroInventoryCommand") && gameState.Hero is { } ih)
                    {
                        var oid = c.TryGetProperty("ObjectId", out var o) ? o.GetString() : null;
                        int slot = 0;   // dest inventory slot = the first key of SlotIndexes
                        if (c.TryGetProperty("SlotIndexes", out var si) && si.ValueKind == JsonValueKind.Object)
                            foreach (var p in si.EnumerateObject()) { int.TryParse(p.Name, out slot); break; }
                        if (oid != null && gameState.Inbox.Remove(oid, out var litem))
                            ih.Inventory[slot] = litem;
                    }
                    else if (type.Contains("BuyHeroItemCommand") && gameState.Hero is { } bh)
                    {
                        var code = c.TryGetProperty("SkuCode", out var sc) ? sc.GetString() : null;
                        int slot = c.TryGetProperty("SlotIndex", out var sl) && sl.TryGetInt32(out var slv) ? slv : 0;
                        if (code != null && deps.ItemCatalog.ResolveSku(code) is { } sku && deps.ItemCatalog.BuildItem(sku.ItemId) is { } bought)
                        {
                            int price = c.TryGetProperty("ClientPrice", out var cp) && cp.TryGetProperty("Amount", out var pa) && pa.TryGetInt32(out var pav) ? pav : sku.PriceAmount;
                            if (sku.PriceCurrency == 4) gameState.LifeForce = Math.Max(0, gameState.LifeForce - price);
                            else gameState.InGameCoin = Math.Max(0, gameState.InGameCoin - price);
                            // ⚠️ TEMPORARY CHEAT — the store-tutorial weapon hits like a truck so we can blast the PvE
                            // attack tutorial (incl. the 113-creature boss). Max item level (MaxItemLevel=29) + wildly
                            // super-rolled stat modifiers. Remove alongside the fast-forward cheat once real progression works.
                            bought["ItemLevel"] = 29;
                            bought["PrimaryStatsModifiers"] = new JsonArray(50.0, 50.0, 50.0);
                            bh.Inventory[slot] = bought;
                        }
                    }
                    else if (type.Contains("HeroEquipmentEquipCommand") && gameState.Hero is { } eh)
                    {
                        int src = c.TryGetProperty("SourceSlotId", out var ss) && ss.TryGetInt32(out var ssv) ? ssv : 0;
                        int dst = c.TryGetProperty("DestinationSlot", out var ds) && ds.TryGetInt32(out var dsv) ? dsv : -1;
                        if (HeroState.SlotName(dst) is { } slotName && eh.Inventory.Remove(src, out var eqitem))
                            eh.Gear[slotName] = eqitem;   // displaced starter gear is dropped
                        else if (dst >= 0)
                            deps.WireLog($"EQUIP DestinationSlot {dst} unmapped (src {src}) — item left in inventory; extend HeroState.SlotName");
                    }
                    else if (type.Contains("ObjectiveUnlockCommand"))
                    {
                        if (c.TryGetProperty("ObjectiveId", out var oid) && oid.TryGetInt32(out var oidv))
                        {
  gameState.Objectives.RemoveAll(o => o.ObjectiveId == oidv);
                            gameState.Objectives.Add(new Objective
                            {
                                AccountId = gameState.AccountId,
                                ObjectiveId = oidv,
                                Status = 1,
                                LastStatusUtc = DateTime.UtcNow
                            });
                        }
                    }
                    else if (type.Contains("ObjectiveViewedCommand"))
                    {
                        // no state change — the client acks that it showed the objective tracker
                    }
                    // No ObjectiveCompleteCommand case: the client never sends one. Objective completion is
                    // engine-side; the server only ever sees ObjectiveUnlock/Viewed.
                    else if (type.Contains("ExecuteAssignmentActionCommand"))
                    {
                        // The command carries only {AssignmentId, ActionIndex} — never the action's payload — so
                        // look the action up in the spec DB via AssignmentCatalog. The only one we react to is
                        // SetCastleRenovationLevelAssignmentActionSpec, the castle-build system's one server-bound
                        // mutation (build_get*/buildingNavBar_* are engine-local, never .hqs endpoints).
                        if (c.TryGetProperty("AssignmentId", out var eAid) && eAid.TryGetInt32(out var eAidv) &&
                            c.TryGetProperty("ActionIndex", out var eIdx) && eIdx.TryGetInt32(out var eIdxv) &&
                            deps.AssignmentCatalog.GetAction(eAidv, eIdxv) is { } action &&
                            (string?)action["$type"] is { } atype && atype.Contains("SetCastleRenovationLevelAssignmentActionSpec") &&
                            (string?)action["CastleRenovationLevel"] is { } levelName &&
                            CastleRenovationCatalog.Ordinal(levelName) is int newLevel and >= 0)
                        {
                            foreach (var (materialId, qty) in deps.CastleRenovation.CostFor(levelName))
                                gameState.CraftingMaterials[materialId] = Math.Max(0, gameState.CraftingMaterials.GetValueOrDefault(materialId) - qty);
                            gameState.CastleRenovationLevel = newLevel;
                            deps.WireLog($"CastleRenovationLevel -> {levelName} ({newLevel}) via assignment {eAidv} action {eIdxv}");
                        }
                    }
                    else if (type.Contains("HeroEquipSpellCommand") && gameState.Hero is { } sh)
                    {
                        // equip an unlocked spell to an action-bar slot (the skill-tree reward)
                        int spellId = c.TryGetProperty("SpellId", out var sp) && sp.TryGetInt32(out var spv) ? spv : 0;
                        int slotIdx = c.TryGetProperty("SlotIndex", out var si2) && si2.TryGetInt32(out var siv) ? siv : 0;
                        if (spellId != 0)
                        {
                            sh.Spells.RemoveAll(x => (int?)x["SpellSpecContainerId"] == spellId);
                            sh.Spells.Add(new JsonObject { ["SpellSpecContainerId"] = spellId, ["Level"] = 1, ["SlotIndex"] = slotIdx });
                        }
                    }
                    }
                    catch (Exception cmdEx)   // log + skip this one command; the rest of the batch still applies
                    { deps.Logger.LogWarning(cmdEx, "SendCommands: command {Type} threw; skipped", type); deps.WireLog($"SendCommands cmd {type} FAILED: {cmdEx.Message}"); }
                }
        }
        catch (Exception ex) { deps.Logger.LogWarning(ex, "SendCommands: unparseable batch, acking {Path}", path); deps.WireLog($"SendCommands unparseable: {ex.Message}"); }
        // Only the one-time starter-castle purchase (the first BuyCommand, from StarterCastleSelection/
        // BuyRandomCastle) gets a CastleBoughtNotification. Later BuyCommands are shop items / traps / rooms
        // bought in build mode — those must not fire CastleBought (it would spuriously re-grant a castle); they
        // just ack {}.
        if (cmdBody.Contains("Contracts.BuyCommand,", StringComparison.OrdinalIgnoreCase) && !gameState.CastleClaimed)
        {
            gameState.CastleClaimed = true;
            var nf = Path.Combine(Directory.GetCurrentDirectory(), "responses", "castle-bought-notifications.json");
            if (!File.Exists(nf)) nf = Path.Combine(AppContext.BaseDirectory, "responses", "castle-bought-notifications.json");
            return Results.Content(await File.ReadAllTextAsync(nf), "application/json");
        }
        // SpellViewedCommand just acks like any other command; skill ownership flows through the real
        // account/progression path, not a pushed SpellUnlockedNotification.
        return Results.Content("{}", "application/json");
    }

    // Gameserver keepalive — request body is {}, respond with an object.
    if (path.EndsWith("/KeepAlive", StringComparison.OrdinalIgnoreCase))
        return Results.Content("{}", "application/json");

    // TrackingService.hqs/Track - fire-and-forget telemetry. Return an empty object in case a result is read.
    if (path.EndsWith("/Track", StringComparison.OrdinalIgnoreCase))
        return Results.Content("{}", "application/json");

    // AccountService.hqs/ChooseDisplayName — the player names their account; capture it so the hero-created
    // AccountInformation reflects it. Reply {}.
    if (path.EndsWith("/ChooseDisplayName", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Request.Body.Position = 0;
        using var sr = new StreamReader(ctx.Request.Body, leaveOpen: true);
        var b = await sr.ReadToEndAsync();
        try { using var d = JsonDocument.Parse(b); if (d.RootElement.TryGetProperty("displayName", out var n)) gameState.DisplayName = n.GetString() ?? gameState.DisplayName; }
        catch { /* keep default */ }
        return Results.Content("{}", "application/json");
    }

    // HeroService.hqs/ChooseFirstHero — player picks a starter class (heroSpecContainerId = eHerotype 2/3/4/5).
    // Create and serialize that hero, remember it so GetAccountInformation flips to hero-created, and return
    // {"Result":<hero>}.
    if (path.EndsWith("/ChooseFirstHero", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Request.Body.Position = 0;
        using var sr = new StreamReader(ctx.Request.Body, leaveOpen: true);
        var b = await sr.ReadToEndAsync();
        int cls = 2;
        try { using var d = JsonDocument.Parse(b); if (d.RootElement.TryGetProperty("heroSpecContainerId", out var h)) cls = h.GetInt32(); }
        catch { /* default Knight */ }
        if (cls < 2 || cls > 5) cls = 2;
        // Create the fresh hero in account state (Level 1, XP 0, one consumable, region 1 unlocked, no gear,
        // no spells). GetAccountInformation then generates the hero-created body from this hero.
        var hero = gameState.CreateHero(cls);
        return Results.Content(new JsonObject { ["Result"] = hero.Serialize() }.ToJsonString(deps.JsonOpts), "application/json");
    }

    // AttackSelectionService.hqs/GetCastleInfo — the preview popup shown before the player commits to an attack.
    // Must be dynamic, keyed by the request's "castleId" query param: the client's subsequent StartAttack uses
    // THIS response's Id as castleAccountId, so a static single-castle file here would retarget every attack to
    // whichever castle that file described. Derives the entry from GetAttackSelectionList.json (the single source
    // of castle metadata) so the two can never drift apart.
    if (path.EndsWith("/GetCastleInfo", StringComparison.OrdinalIgnoreCase))
    {
        long castleId = ctx.Request.Query.TryGetValue("castleId", out var cidStr) && long.TryParse(cidStr, out var cidVal) ? cidVal : 0;
        var list = JsonNode.Parse(await File.ReadAllTextAsync(deps.RespFile(Path.Combine("AttackSelectionService.hqs", "GetAttackSelectionList.json"))))!["Result"]!;
        JsonObject? entry = null;
        if (list["BossCastle"] is JsonObject boss && boss["DefenderAccountSummary"]?["Id"]?.GetValue<long>() == castleId) entry = boss;
        if (entry is null && list["CastlesByLevel"] is JsonArray levels)
            foreach (var lvl in levels)
                if (lvl?["Castles"] is JsonArray cs)
                    foreach (var c in cs)
                        if (c is JsonObject co && co["DefenderAccountSummary"]?["Id"]?.GetValue<long>() == castleId) { entry = co; break; }
        entry ??= (JsonObject)list["CastlesByLevel"]![0]!["Castles"]![0]!;   // unknown id -> first known castle

        int rooms = 0, traps = 0;
        var castleFile = deps.RespFile(Path.Combine("castles", castleId + ".json"));
        if (File.Exists(castleFile) && JsonNode.Parse(await File.ReadAllTextAsync(castleFile)) is { } cd && cd["Rooms"] is JsonArray rs)
        {
            rooms = rs.Count;
            foreach (var r in rs) traps += (r?["Traps"] as JsonArray)?.Count ?? 0;
        }
        int level = entry["Level"]?.GetValue<int>() ?? 1;
        var summary = entry["DefenderAccountSummary"]!;
        int cp = Math.Max(rooms * 8, 1);
        // Built via JsonNode.Parse of a text template (not `new JsonObject { ["x"] = 0.5 }`): a raw CLR
        // double/bool assigned through JsonNode's implicit operators needs deps.JsonOpts to carry a
        // TypeInfoResolver (it doesn't) and throws at write time. Parsed-JsonElement-backed nodes don't hit that path.
        string json =
            "{\"Result\":{" +
            "\"DefenderAccountSummary\":{\"Id\":" + castleId + ",\"DisplayName\":" + (summary["DisplayName"]?.ToJsonString() ?? "\"\"") + ",\"OasisNameId\":" + (summary["OasisNameId"]?.ToJsonString() ?? "null") + "}," +
            "\"CastleType\":" + (entry["CastleType"]?.ToJsonString() ?? "1") + "," +
            "\"RoomCount\":" + rooms + "," +
            "\"Difficulty\":" + level + "," +
            "\"PotentialLoot\":{\"Xp\":" + (level * 60) + ",\"TreasureRoomStealableIGC\":" + (level * 10) + ",\"TreasureRoomStealableLifeForce\":" + (level * 10) + ",\"IGC\":" + (level * 30) + ",\"LifeForce\":" + (level * 30) + "}," +
            "\"IsNew\":true," +
            "\"IsCastleAttackable\":" + (entry["IsCastleAttackable"]?.ToJsonString() ?? "true") + "," +
            "\"AttackabilityStatus\":1," +
            "\"AttackType\":5," +
            "\"Level\":" + level + "," +
            "\"Stats\":{\"TotalConstructionPoints\":" + cp + ",\"MaxConstructionPoints\":" + cp + ",\"TrapCount\":" + traps + ",\"WinRatio\":0.5,\"WinRatioDifficulty\":" + (entry["WinRatioDifficulty"]?.ToJsonString() ?? level.ToString()) + "}," +
            "\"VictoryConditionRewardRatios\":[1,0.75,0.5]" +
            "}}";
        return Results.Content(JsonNode.Parse(json)!.ToJsonString(deps.JsonOpts), "application/json");
    }

    // AttackService.hqs/StartAttack — the player attacks a castle. The client needs a full attack payload or the
    // level never loads (hero spawns on the start rock, can't move): the castle layout to render+fight, the
    // player's chosen Hero, loot tables + settings. For castles without a real spec we reuse the CastleForSale
    // "Draft" layout (+ its Result-level archetypes/indices) as the bot castle, stamped AccountId=castleAccountId
    // / IsTutorialCastle. Tutorial = attackType None(0)/Progression(1).
    if (path.EndsWith("/StartAttack", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Request.Body.Position = 0;
        using var sr = new StreamReader(ctx.Request.Body, leaveOpen: true);
        var b = await sr.ReadToEndAsync();
        long castleAccountId = 2; int castleType = 1, attackType = 0;
        try
        {
            using var d = JsonDocument.Parse(b); var r = d.RootElement;
            if (r.TryGetProperty("castleAccountId", out var c)) castleAccountId = c.GetInt64();
            if (r.TryGetProperty("castleType", out var t)) castleType = t.GetInt32();
            if (r.TryGetProperty("attackType", out var a)) attackType = a.GetInt32();
        }
        catch { /* defaults = tutorial bot castle */ }
        bool tutorial = attackType == 0 || attackType == 1;
        gameState.LastAttackCastle = (int)castleAccountId;

        // The castle to attack. Prefer the real decrypted castle spec (responses/castles/<id>.json). For the
        // tutorial (castleAccountId 2 = PVE_00_TUTORIAL_01) this carries the CastleTrigger volumes the client's
        // Assignment VM coaching waits on, without which the FTUE coaching can't advance. Fall back to the
        // CastleForSale Draft for any castle without a real spec.
        JsonNode castle;
        var realCastle = deps.RespFile(Path.Combine("castles", castleAccountId + ".json"));
        if (File.Exists(realCastle))
        {
            castle = JsonNode.Parse(await File.ReadAllTextAsync(realCastle))!;
        }
        else
        {
            var cfs = JsonNode.Parse(await File.ReadAllTextAsync(deps.RespFile(Path.Combine("CastleForSaleService.hqs", "GetCastleForSaleBuildInfo.json"))))!;
            var cfsRes = cfs["Result"]!;
            var c = cfsRes["Draft"]!.DeepClone().AsObject();
            c.Remove("IsForSaleCastle");
            c["AccountId"] = castleAccountId;
            c["AccountDisplayName"] = "Tutorial Castle";
            if (tutorial) c["IsTutorialCastle"] = true;
            foreach (var k in new[] { "CreatureArchetypes", "TrapArchetypes", "InventoryThemes", "RoomNextIndex", "CreatureNextIndex", "TrapNextIndex", "DecorationNextIndex", "TriggerNextIndex", "BuildingNextIndex" })
                if (cfsRes[k] is JsonNode v) c[k] = v.DeepClone();
            castle = c;
        }

        var tmpl = JsonNode.Parse(await File.ReadAllTextAsync(deps.RespFile("attack-tutorial-start.json")))!;
        var res = tmpl["Result"]!;
        res["Castle"] = castle;
        res["CastleType"] = castleType;
        res["AttackerDisplayName"] = gameState.DisplayName;
        // CreatureLoot is keyed by the placed-creature INSTANCE Id (each Rooms[].Creatures[].Id), NOT the
        // SpecContainerId. When a creature dies the client looks up CreatureLoot[entry.Id]; a missing entry
        // drops zero gold/XP/lifeforce. Cover every creature instance in the attacked castle: keep the hand-tuned
        // template entries and add a tier-appropriate entry for any instance the template misses, so loot scales
        // to any castle, not just a fixed id range.
        if (res["CreatureLoot"] is JsonArray loot)
        {
            var have = new HashSet<int>();
            foreach (var e in loot) if (e?["Id"] is JsonNode idn) have.Add(idn.GetValue<int>());
            var captains = new HashSet<int> { 1003, 1006, 1023, 1029, 1079, 1155 };   // _Captain/Elite SpecContainerIds -> bigger reward
            if (castle["Rooms"] is JsonArray rooms)
                foreach (var room in rooms)
                    if (room?["Creatures"] is JsonArray creatures)
                        foreach (var cr in creatures)
                        {
                            if (cr?["Id"] is not JsonNode cidn) continue;
                            int cid = cidn.GetValue<int>();
                            if (!have.Add(cid)) continue;   // already covered by the hand-tuned template
                            int spec = cr["SpecContainerId"] is JsonNode sn ? sn.GetValue<int>() : 0;
                            loot.Add(captains.Contains(spec)
                                ? new JsonObject { ["Id"] = cid, ["Gold"] = 2, ["Xp"] = 8, ["LifeForce"] = 3, ["HealthOrbFragments"] = 16 }
                                : new JsonObject { ["Id"] = cid, ["Gold"] = 1, ["Xp"] = 2, ["LifeForce"] = 1, ["HealthOrbFragments"] = 1 });
                        }
        }
        if (gameState.Hero != null)
        {
            // The combat hero is the player's current hero — gear/level/spells exactly as they are.
            res["Hero"] = gameState.Hero.Serialize();
        }
        // Stamp the trap's drop to the hero's class first-loot item so the in-mission drop matches what EndAttack
        // returns (otherwise the player sees one item in-mission but receives a different class's item).
        if (gameState.Hero != null && res["TrapLoot"] is JsonArray trapStamp && trapStamp.Count > 0
            && trapStamp[0]?["InventoryItems"] is JsonArray ti0 && ti0.Count > 0 && ti0[0] is JsonObject titem)
            titem["TemplateId"] = AccountState.ClassFirstLootTemplate(gameState.HeroClass);
        // Remember this attack's loot tables (creature gold/xp/lifeforce by instance Id, trap items by ItemId) so
        // EndAttack can score the client's reported looted-ids by summing these.
        gameState.AttackCreatureLoot.Clear(); gameState.AttackCreatureItems.Clear(); gameState.AttackTrapLoot.Clear();
        // Map each placed-entity instance Id -> SpecContainerId so EndAttack can count destructions by spec, for
        // all DefenseIngredientDestroyed condition kinds: Creature, Decoration, and Building.
        gameState.AttackCreatureSpec.Clear(); gameState.AttackDecorationSpec.Clear(); gameState.AttackBuildingSpec.Clear();
        if (castle["Rooms"] is JsonArray rsp)
            foreach (var room in rsp)
            {
                if (room?["Creatures"] is JsonArray crs)
                    foreach (var cr in crs)
                        if (cr?["Id"] is JsonNode cidn2 && cr["SpecContainerId"] is JsonNode spn2)
                            gameState.AttackCreatureSpec[cidn2.GetValue<int>()] = spn2.GetValue<int>();
                if (room?["Decorations"] is JsonArray dcs)
                    foreach (var dc in dcs)
                        if (dc?["Id"] is JsonNode didn && dc["SpecContainerId"] is JsonNode dspn)
                            gameState.AttackDecorationSpec[didn.GetValue<int>()] = dspn.GetValue<int>();
                if (room?["Buildings"] is JsonArray bls)
                    foreach (var bl in bls)
                        if (bl?["Id"] is JsonNode bidn && bl["SpecContainerId"] is JsonNode bspn)
                            gameState.AttackBuildingSpec[bidn.GetValue<int>()] = bspn.GetValue<int>();
            }
        if (res["CreatureLoot"] is JsonArray clStore)
            foreach (var e in clStore)
            {
                if (e?["Id"] is not JsonNode eidn) continue;
                int eid = eidn.GetValue<int>();
                gameState.AttackCreatureLoot[eid] = (
                    e["Gold"]?.GetValue<int>() ?? 0, e["Xp"]?.GetValue<int>() ?? 0, e["LifeForce"]?.GetValue<int>() ?? 0);
                if (e["InventoryItems"] is JsonArray eitems)
                    gameState.AttackCreatureItems[eid] = eitems.Select(x => (JsonObject)x!.DeepClone()).ToList();
            }
        if (res["TrapLoot"] is JsonArray tlStore)
            foreach (var t in tlStore)
            {
                if (t?["Id"] is not JsonNode tidn) continue;
                gameState.AttackTrapLoot[tidn.GetValue<int>()] = t["InventoryItems"] is JsonArray titems
                    ? titems.Select(x => (JsonObject)x!.DeepClone()).ToList() : new();
            }
        return Results.Content(tmpl.ToJsonString(deps.JsonOpts), "application/json");
    }

    // AttackService.hqs/EndAttack — combat finished. The client reports WHICH placed instances it looted (by Id),
    // not amounts; we score that against the loot tables sent in the matching StartAttack (summing gold/lifeforce/
    // xp), credit + persist the gain to the account, and hand back the items the player actually looted.
    if (path.EndsWith("/EndAttack", StringComparison.OrdinalIgnoreCase))
    {
        // Read the body as BYTES (not a UTF-8 StreamReader): the trailing replay blob is arbitrary bytes and
        // UTF-8-decoding it is lossy (→ U+FFFD), which would corrupt the replay. Persist the raw bytes verbatim
        // before scoring so the audit record exists regardless of what follows.
        ctx.Request.Body.Position = 0;
        byte[] raw;
        using (var ms = new MemoryStream()) { await ctx.Request.Body.CopyToAsync(ms); raw = ms.ToArray(); }
        long attackId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try { await File.WriteAllBytesAsync(Path.Combine(deps.AuditDir, $"{attackId}_a{gameState.AccountId}_c{gameState.LastAttackCastle}.bin"), raw); } catch { }
        // The EndAttack POST is a JSON object immediately followed by a binary replay blob — JsonNode.Parse
        // throws on the trailing data, so parse only the leading JSON (brace-match from the first '{' to its
        // balanced close). Decoding the whole buffer to a string is safe for scoring: the JSON prefix is valid
        // UTF-8 and FirstJsonObject stops at its balanced close, never touching the trailing blob.
        var body = System.Text.Encoding.UTF8.GetString(raw);
        static string FirstJsonObject(string s)
        {
            int start = s.IndexOf('{'); if (start < 0) return s;
            int depth = 0; bool inStr = false, esc = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (esc) esc = false; else if (c == '\\') esc = true; else if (c == '"') inStr = false; }
                else if (c == '"') inStr = true;
                else if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return s.Substring(start, i - start + 1); }
            }
            return s.Substring(start);
        }
        int gold = 0, lifeforce = 0, xp = 0;
        var lootItems = new List<JsonObject>();
        try
        {
            var p = JsonNode.Parse(FirstJsonObject(body))?["endAttackParams"];
            if (p != null)
            {
                int[] Ids(string k) => (p[k] as JsonArray)?.Where(n => n != null).Select(n => n!.GetValue<int>()).ToArray() ?? Array.Empty<int>();
                foreach (var id in Ids("LootedGoldCreatureIds")) if (gameState.AttackCreatureLoot.TryGetValue(id, out var cl)) gold += cl.Gold;
                foreach (var id in Ids("LootedLifeForceCreatureIds")) if (gameState.AttackCreatureLoot.TryGetValue(id, out var cl)) lifeforce += cl.LifeForce;
                foreach (var id in Ids("KilledCreatureIds")) if (gameState.AttackCreatureLoot.TryGetValue(id, out var cl)) xp += cl.Xp;
                gold += p["TreasureRoomLootedGold"]?.GetValue<int>() ?? 0;
                lifeforce += p["TreasureRoomLootedLifeForce"]?.GetValue<int>() ?? 0;
                foreach (var id in Ids("LootedHeroItemCreatureIds")) if (gameState.AttackCreatureItems.TryGetValue(id, out var its)) lootItems.AddRange(its.Select(x => (JsonObject)x.DeepClone()));
                if (p["LootedHeroItemTrapIds"] is JsonArray tl)
                    foreach (var t in tl)
                    {
                        int itemId = t?["ItemId"]?.GetValue<int>() ?? -1;
                        int idx = t?["ItemIndex"]?.GetValue<int>() ?? 0;
                        if (gameState.AttackTrapLoot.TryGetValue(itemId, out var its) && idx >= 0 && idx < its.Count)
                            lootItems.Add((JsonObject)its[idx].DeepClone());
                    }
                // Capture killed creature IDs for objective completion detection on subsequent requests.
                gameState.Attack.LastAttackKilledCreatureIds = Ids("KilledCreatureIds");
                gameState.Attack.LastAttackDestroyedDecorationIds = Ids("DestroyedDecorationIds");
                // PillagedMines[] = [{ "CastleBuildingId":9, "IsDestroyed":true, ... }]. Count only
                // actually-destroyed mines for "destroy N mines" objectives.
                gameState.Attack.LastAttackPillagedMineBuildingIds = (p["PillagedMines"] as JsonArray)?
                    .Where(m => m?["IsDestroyed"]?.GetValue<bool>() ?? true)
                    .Select(m => m?["CastleBuildingId"]?.GetValue<int>() ?? -1).Where(id => id >= 0).ToArray() ?? Array.Empty<int>();
            }
        }
        catch (Exception ex) { deps.Logger.LogWarning(ex, "EndAttack: params parse failed -> zero reward"); deps.WireLog($"EndAttack params parse FAILED: {ex.Message}"); }

        // Exercise the verification seam. The verdict is ignored today (StubVerificationService returns
        // valid=true), but the AuditBundle is constructed and the call graph exists, so swapping in real re-sim
        // later changes no caller. AttackRandomSeed is 0 for now: StartAttack does not yet issue/record a
        // per-attack seed — thread it through when PvP/anti-cheat lands.
        try
        {
            var verifier = ctx.RequestServices.GetRequiredService<IVerificationService>();
            JsonElement claimed = default;
            try { using var cd = JsonDocument.Parse(FirstJsonObject(body)); claimed = cd.RootElement.Clone(); } catch { }
            _ = await verifier.VerifyAsync(new AuditBundle
            {
                AttackId = attackId,
                AttackerAccountId = gameState.AccountId,
                AttackRandomSeed = 0,
                ClaimedResult = claimed,
                SubmittedAtUtc = DateTimeOffset.UtcNow,
            });
        }
        catch { /* audit is best-effort; never fail the attack flow on it */ }

        // Credit + PERSIST to the account (clamped to storage capacity); the deltas drive the notifications.
        int initialGold = gameState.InGameCoin;
        int goldGained = gameState.CreditGold(gold);
        int lifeGained = gameState.CreditLifeForce(lifeforce);
        int xpGained = xp, totalXp = xp, preLevel = gameState.HeroLevel, heroLevel = gameState.HeroLevel;
        if (gameState.Hero != null) { totalXp = gameState.Hero.AddXp(xp); heroLevel = gameState.Hero.Level; }
        bool levelChanged = heroLevel > preLevel;   // HeroXpChanged.LevelChanged — set when a level threshold was crossed

        // Response = the template structure with the SCORED Result fields overridden.
        var end = JsonNode.Parse(await File.ReadAllTextAsync(deps.RespFile("attack-tutorial-end.json")))!;
        var er = (JsonObject)end["Result"]!;
        er["DefenderCastleId"] = gameState.LastAttackCastle;
        er["InitialGold"] = initialGold;
        er["HeroLevel"] = heroLevel;
        er["TotalGold"] = goldGained; er["KillsGold"] = goldGained;
        er["TotalLifeForce"] = lifeGained; er["KillsLifeForce"] = lifeGained;
        er["TotalXp"] = xpGained; er["KillsXp"] = xpGained;

        // Notifications: type-24 WalletUpdated (the gained DELTA the client adds to its pre-attack balance), type-43
        // HeroXpChanged, and one type-111 InboxItemsAdded per looted item (fresh ObjectId so the client can equip it).
        var nots = new JsonArray
        {
            Notifications.WalletUpdated((2, goldGained), (4, lifeGained)),
            Notifications.HeroXpChanged(gameState.HeroClass, xpGained, totalXp, heroLevel, levelChanged),
        };
        foreach (var item in lootItems)
        {
            item.Remove("$type");   // HeroItem carries no $type; it's typed by the wrapping InboxHeroEquipmentItem
            var objectId = gameState.NextObjectId();
            gameState.Inbox[objectId] = (JsonObject)item.DeepClone();   // PERSIST it so InboxCollect can resolve this ObjectId later
            nots.Add(Notifications.InboxItemsAdded("InboxHeroEquipmentItem", item, 3, objectId));
        }
        // Mission/objective scoring, data-driven over all OBJECTIVESETTINGS missions. MissionManager scores this
        // raid against every active "Attack" mission scoped to the attacked castle, accumulates per-condition
        // progress (a mission may span multiple raids/castles), and when all of a mission's conditions are met
        // completes it + emits the reward notifications in this EndAttack response: type-14 ObjectiveCompleted +
        // materials (type-111 InboxConsumableItem) / currency (type-24) / gear (type-111) / xp (type-43).
        // Objective completion is server-authoritative via these notifications — the engine's in-attack condition
        // ticks are just the HUD; the metagame objective only completes on the type-14.
        // killedSpecCounts feeds MissionManager's DefenseIngredientDestroyed conditions. Merge creature kills and
        // decoration destructions by spec — spec ranges don't collide across types (creatures ~1000+, decorations
        // ~200-300), so one dict is safe.
        var killedSpecCounts = new Dictionary<int, int>();
        foreach (var kid in gameState.Attack.LastAttackKilledCreatureIds)
            if (gameState.AttackCreatureSpec.TryGetValue(kid, out var spec))
                killedSpecCounts[spec] = killedSpecCounts.GetValueOrDefault(spec) + 1;
        foreach (var did in gameState.Attack.LastAttackDestroyedDecorationIds)
            if (gameState.AttackDecorationSpec.TryGetValue(did, out var dspec))
                killedSpecCounts[dspec] = killedSpecCounts.GetValueOrDefault(dspec) + 1;
        foreach (var bid in gameState.Attack.LastAttackPillagedMineBuildingIds)   // destroyed mines -> building spec
            if (gameState.AttackBuildingSpec.TryGetValue(bid, out var bspec))
                killedSpecCounts[bspec] = killedSpecCounts.GetValueOrDefault(bspec) + 1;
        // CastleType: campaign story castles are "Ubisoft"; otherwise the raid target is a PvP-tutorial bot
        // castle (objective 301 is scoped CastleTypes:["User"] and scores CastleEntered + CastleCompleted, not
        // kills, against a non-campaign AccountId).
        string castleType = CampaignCastleIds.Contains(gameState.LastAttackCastle) ? "Ubisoft" : "User";
        var missionCtx = new MissionManager.AttackContext(gameState.LastAttackCastle, castleType, killedSpecCounts, Completed: true);
        var missionNots = MissionManager.OnEndAttack(gameState, deps.MissionCatalog, deps.ItemCatalog, missionCtx,
            deps.MissionProgress.GetOrAdd(gameState.AccountId, _ => new()));
        foreach (var mn in missionNots) nots.Add(mn!.DeepClone());
        // Temporary — fast-forward the FTUE toward base-building. Fallback only: if the natural scoring above
        // did not complete a mission this raid (the active attack objective can't finish on this castle — a
        // User/PvP one, or a not-yet-built target castle), force-complete the currently-active attack objective +
        // grant its rewards. Kept to one mission per castle finish and never fires during the forest/witch
        // tutorials (no active attack objective yet). Skipped when a type-14 ObjectiveCompleted was already
        // emitted, so a real raid doesn't also fast-forward the next objective in the same raid.
        bool completedNaturally = missionNots.Any(n => (n as JsonObject)? ["NotificationType"]?.GetValue<int>() == 14);
        if (FastForwardCheat && !completedNaturally)
        {
            var ff = MissionManager.FastForwardNextObjective(gameState, deps.MissionCatalog, deps.ItemCatalog);
            foreach (var fn in ff) nots.Add(fn!.DeepClone());
            if (ff.Count > 0) deps.WireLog($"CHEAT fast-forward: force-completed active attack objective (+{ff.Count} notifs)");
        }
        // ORDER-CRITICAL: a HeroXpChanged with LevelChanged:true rebuilds the current hero client-side,
        // transiently nulling the "current entity" view-model. If the InboxController processes a reward
        // equipment item (type-111 InboxHeroEquipmentItem) during that window, its binding derefs the null model
        // and crashes. This only happens when a level-up and an equipment reward land in one response. Emit the
        // level-up last, so every inbox/reward item is processed against a stable hero before the level-up applies.
        var levelUps = nots.Where(n => (n as JsonObject)?["NotificationType"]?.GetValue<int>() == 43
                                     && (n as JsonObject)?["LevelChanged"]?.GetValue<bool>() == true).ToList();
        foreach (var lu in levelUps) { nots.Remove(lu); nots.Add(lu); }
        end["Notifications"] = nots;

        deps.WireLog($"EndAttack castle={gameState.LastAttackCastle} SCORED gold +{goldGained} life +{lifeGained} xp +{xpGained} (total {totalXp}) items {lootItems.Count}; mission notifs {missionNots.Count}; levelUp-moved-last {levelUps.Count}");
        return Results.Content(end.ToJsonString(deps.JsonOpts), "application/json");
    }

        return null;   // no game handler matched -> caller falls through to the static dispatcher
    }
}
