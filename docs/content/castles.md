# Castles — how the server serves a castle to attack

> **Status:** forest `2`, witch `3`, Tybalt's Farm `100` + a 17-castle PvP-tutorial bot pool (levels 1-3)
> served + playtest-verified through objective 302 · **Server:** gameserver · **Updated:** 2026-07-07

## Purpose

Every "send your hero into a castle" session — FTUE dungeons, PvE campaign castles, later PvP raids — needs
the server to hand the client a **full castle layout** to render and fight. This doc is the durable how-to for
**turning a decrypted castle spec into a served castle**, what the client requires in it, and the gotchas that
have bitten us. It is the companion to [tutorial-steps.md](tutorial-steps.md) (the coaching that wraps a
castle) and the wire shape in [attack-service.md](../../code-analysis/rest-api/attack-service.md) (the source of
truth for the JSON).

## Key code

| Type / file | Role |
|-------------|------|
| [`GameEndpoints.cs` StartAttack](../../MQELServer/src/MQEL.Gameserver/GameEndpoints.cs) | loads `responses/castles/<id>.json`, injects the hero, **auto-generates `CreatureLoot`**, stores loot tables for scoring |
| [`GameEndpoints.cs` EndAttack](../../MQELServer/src/MQEL.Gameserver/GameEndpoints.cs) | scores looted instance-Ids vs the stored tables; credits + notifies; derives `CastleType` from `CampaignCastleIds` |
| [`GameEndpoints.cs` GetCastleInfo](../../MQELServer/src/MQEL.Gameserver/GameEndpoints.cs) | dynamic per-castle world-map info (win ratio, room/trap counts) — reads `?castleId=` and cross-references `GetAttackSelectionList.json` + the real castle file; NOT a static response file |
| [`responses/castles/*.json`](../../MQELServer/src/MQEL.Gameserver/responses/castles/) | the served castle layouts (the source of truth — see the bin gotcha) |
| [`tools/make_tutorial_castle.py`](../../tools/make_tutorial_castle.py) | transforms a decrypted spec → a served castle file (17 castles baked as of 2026-07-07) |
| [`responses/AttackSelectionService.hqs/GetAttackSelectionList.json`](../../MQELServer/src/MQEL.Gameserver/responses/AttackSelectionService.hqs/GetAttackSelectionList.json) | the world-map list of attackable castles |

## How it works

### 1. Where castle specs come from
The decrypted spec DB has **96 PvE castle layouts** at
`game-data/settings-extracted/GameplaySettings/Castles/` (decrypted with `bff` — see
[attack-service.md §2](../../code-analysis/rest-api/attack-service.md)). Each is named
`NNNNNN - <CODENAME>.JSON` where the leading number is the castle's `AccountId` (e.g. `000002 -
PVE_00_TUTORIAL_01`, `000100 - PVE_R01Q010_CHICKENFARM` = Tybalt's Farm). This is the authoritative source —
**never hand-author a castle layout.**

### 2. The transform spec → served file
`make_tutorial_castle.py` does the minimal, faithful transform:
1. add `"$type": "HyperQuest.GameServer.Contracts.UbisoftCastle, …"` (the polymorphic discriminator the
   client's deserializer selects the reader by — without it the castle won't parse),
2. **drop `CustomAttackerReward`** (server-internal class-aware loot; we handle rewards in EndAttack instead —
   see [attack-service.md §6](../../code-analysis/rest-api/attack-service.md)),
3. stamp `AccountId` = the map id.

Add a castle by extending the `CASTLES` dict and re-running it. Output → `responses/castles/<id>.json`.

### 3. What the client requires in a castle (or it softlocks)
- **`$type` UbisoftCastle** — or the body doesn't deserialize.
- **At least one room with the hero start + reachable layout** — a bare/triggerless substitute leaves the
  hero spawned on the start rock unable to advance (this bit us — see the FTUE coaching note below).
- **`Rooms[].Creatures[].Id`** (placed-instance Id, distinct from `SpecContainerId`) — the loot table is keyed
  by instance Id; a creature with no matching `CreatureLoot[Id]` entry drops **zero** gold/xp/lifeforce.
- **`CreatureTiers` / `TrapTiers`** — the spec's tier lists; keep them.
- **`Triggers[]` (SpecContainerId 52)** only matter for **coaching** — see [tutorial-steps.md](tutorial-steps.md).
  A pure campaign castle (like Tybalt's Farm) needs few/none.

### 4. Auto-generated loot (so any castle just works)
StartAttack walks every `Rooms[].Creatures[]` and ensures a `CreatureLoot` entry exists for each instance Id:
captains/elites get a bigger entry, everything else a basic one. This is why **dropping a new castle file is
usually all it takes** for it to be raidable with working rewards. The captain/elite set is a hardcoded list of
`SpecContainerId`s — **extend it when a new castle introduces a new captain/elite** (e.g. Tybalt's Farm adds
`1079` Goatman_Captain, `1155` Chicken Elite). Full scoring detail: [progression-loop.md](../gameplay/progression-loop.md).

### 5. Appearing on the world map
A castle is only selectable if it's in `GetAttackSelectionList.json` (`{Id, DisplayName, Level, CastleThemeId,
CastleHeartRank, IsCastleAttackable}`). Current pool (2026-07-07): campaign castles (2 tutorial forest, 3
witch/Hedgehog, 100 Tybalt's Farm — `CampaignCastleIds` in `GameEndpoints.cs`, scored `CastleType:"Ubisoft"`)
plus a **17-castle PvP-tutorial bot pool** spread across 3 levels (L1: 4,5,71 · L2: 6,7,72,73,100 · L3:
8,9,10,74,75,76,80 — scored `CastleType:"User"`), all real spec-derived layouts, no leaked/fake data. The list
is **static** today; it becomes dynamic with multi-user/castle-building. **Known gap:** opponent castles render
in a 360° ring evenly distributed by count — too few castles in a level leaves visible empty gaps in the ring
(cosmetic, not a scoring bug) — tracked in [`docs/OPEN_ISSUES.md`](../OPEN_ISSUES.md).

## How to …

### How to add a new campaign/PvE castle
1. Find its spec: `Castles/NNNNNN - <CODENAME>.JSON` (the number = `AccountId`).
2. Add it to `make_tutorial_castle.py`'s `CASTLES` dict; run it; **`git add responses/castles/<id>.json`**.
3. If it has a new captain/elite `SpecContainerId`, add it to the StartAttack captain set.
4. Ensure it's in `GetAttackSelectionList.json` (add an entry if missing).
5. Smoke-test: select it → `StartAttack` renders + fights → `EndAttack` credits. Use a save-state to skip the
   FTUE on each iteration ([persistence.md](../ops/persistence.md)).

## Data / persistence
Castle layouts are static files (`responses/castles/`). The *player's own* castle (build mode) is account
state, not covered here — see [castle-building.md](castle-building.md) for the renovation-level system that's
implemented so far. The transient per-attack loot tables live in `AttackScratch`
([AccountState.cs](../../MQELServer/src/MQEL.Gameserver/AccountState.cs)), re-derived each StartAttack.

## Design notes & gaps
- ⚠️ **The `bin/` gotcha (the big one).** There is **no `responses/` copy rule** in
  [the csproj](../../MQELServer/src/MQEL.Gameserver/MQEL.Gameserver.csproj). The server reads `responses/`
  relative to the **working directory at `dotnet run`** (the project dir), with a fallback to
  `AppContext.BaseDirectory` (= `bin/`). Files that appear **only** under `bin/Debug|Release/responses/`
  (e.g. `castles/100.json`, `castles/11.json` as of 2026-06-28) are **stray, untracked, and not regenerated by
  the build** — editing them changes nothing durable and they vanish on a clean. **Always edit/commit the
  project `responses/` tree.** Verify with `git ls-files responses/castles/`.
- The CastleForSale "Pink Castle" Draft is the **fallback** when no `responses/castles/<id>.json` exists —
  renderable but generic; prefer a real spec.
- `CastleType` (`"Ubisoft"` = campaign/scripted, `"User"` = bot/PvP) is derived server-side from
  `CampaignCastleIds` on EndAttack — NOT hardcoded, NOT passed through from the client (a prior bug hardcoded
  it to `"Ubisoft"` always, silently blocking PvP objective scoring). `attackType` (0/1 = tutorial/progression …
  4 = validation, 5 = visit) IS passed through from the StartAttack request — see
  [attack-service.md §3.1](../../code-analysis/rest-api/attack-service.md).

## Related
- [tutorial-steps.md](tutorial-steps.md) — the coaching/assignment layer that wraps a castle
- [progression-loop.md](../gameplay/progression-loop.md) — the StartAttack→EndAttack reward scoring in full
- [attack-service.md](../../code-analysis/rest-api/attack-service.md) — the authoritative castle/attack JSON shape
- [`.opencode/plans/tybalts-farm-castle.md`](../../.opencode/plans/tybalts-farm-castle.md) — the current castle-implementation plan
</content>
