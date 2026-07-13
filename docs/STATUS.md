# Status

> Big-picture state. Update when something crosses a line (works / stubbed / next). Dated facts.
> Last updated: 2026-07-07.

## Done

- ✅ **Client recovered & verified complete** — Steam depot `239220`, 8,255/8,255 manifest files, all 48
  bigfile packages.
- ✅ **Architecture mapped** — Opal/Zouna C++ engine, CEF+hyperquest UI, REST `.hqs` services.
- ✅ **Game data unpacked + spec DB decrypted** — 48 `.BFPC` extracted with `bff`; and
  `bff extract-mqfel-settings-bin` → **8726 JSON** spec files at `game-data/settings-extracted/`
  (Castles/Assignments/Creatures/Heroes/Items/AccountTemplates…) = the authoritative game-design DB.
- ✅ **Repo + .NET server scaffolded** — `MQELServer/` (.NET 8) + Docker + capture rig + the `AuditBundle`
  verification substrate ([verification.md](ops/verification.md)).
- ✅ **Whole launcher boot reimplemented** — config → maintenance → version → login (Steam ticket → cookie)
  → packages. The launcher accepts our server and starts `MightyQuest.exe`. ([boot-flow.md](boot/boot-flow.md))
- ✅ **BOOT SOLVED** — the game boots fully into its UI. The fix was an **HTTPS-scheme requirement**
  (NetworkBootManager rejects non-`https` server URL) + **cert-trust bypass** (WriteProcessMemory patch so
  the client accepts our cert); the session token reaches the game via the **`-token` command-line arg**
  (no launcher Argo/`ProxyLoggedIn` IPC). The old "Waiting for AccountServerController" was teardown noise,
  not a missing server command. ([boot-flow.md](boot/boot-flow.md))
- ✅ **Onboarding** — account creation, display name, **starter-castle claim**, **hero pick** — all work; the
  GAI body is GENERATED from a stateful `AccountState`, never canned.
- ✅ **Full FTUE tutorial plays through** — forest dungeon → equip looted gear → store (buy + equip weapon) →
  witch dungeon → **skill unlock at level 2**, with coaching.
- ✅ **Reward & progression loop** — wallet capacity (type-47), in-dungeon gather, EndAttack reward scoring
  (client's looted-instance-IDs → real gold/soul/XP), level-up off the `XpPerLevel` curve, and skill unlock
  via the `HeroXpChanged.LevelChanged` flag. **All earned, no force-grants.** Full record + every dead end:
  **[progression-loop.md](gameplay/progression-loop.md)**.
- ✅ **Durable per-account persistence** — `AccountState` round-trips through a **relational EF Core** store
  (SQLite today, Postgres-ready via a provider swap) behind an `IAccountRepository` black box. Detached graphs
  merge by key (no delete+insert churn); a per-account lock serialises concurrent requests. The full FTUE
  survives an `Alt-F4`/reconnect — hero, gear, gold, level, and the unlocked skill all resume.
  **Playtest-verified**, committed `d3df2d3`.
- ✅ **Server admin dashboard** — a dependency-free static UI (game-themed "Sky Keep" skin, fonts/colours
  ripped from the client) at `https://localhost:8080/`: live service + account status, an account editor
  (gold, life force, hero level/XP, named tutorial checkpoints), a tailing server log, and **save-states** —
  named snapshots of an account's full graph (capture / restore / delete) for replay-free checkpoint testing.
- ✅ **Tybalt's Farm + Chickenvaders quest (Stage A)** — castle 100 spec generated from authoritative spec DB
  (`make_tutorial_castle.py`) and committed to `responses/castles/100.json`. StartAttack auto-loots chicken
  (1081), goatman-captain (1079), and chicken-elite (1155) with class-appropriate rewards.
- ✅ **Tybalt's Farm + Chickenvaders quest — SOLVED + playtest-verified (2026-06-29)** — the campaign
  **objective system** works end-to-end. Objective completion is **server-authoritative via an EndAttack
  notification** `ObjectiveCompletedNotification {ObjectiveId:300, NotificationType:14}` (the real type — an
  earlier **112** guess was silently ignored, which caused a day-long stall). EndAttack scores the objective's
  conditions faithfully (counts **spec-1081 kills ≥ 20** via an instance-Id→spec map from StartAttack), sends
  type-14, persists `Objective.Status=2`. GAI seeds `Objectives` in the real `{ObjectiveId,Status,LastStatusDate}`
  shape for reconnect. **Live-verified (CDP):** objective 300 → `CompletedObjectives, Status:2` → assignment
  `005006` fires → opens `CastleRenovationPanel`. Ground truth was the `MQELOffline_cpp` `AttackService.cpp`
  real capture (type-14 + the type-17 `ObjectiveUnlockedNotification`). Full record: [objectives.md](gameplay/objectives.md).
- ✅ **Objective-reward crafting-material delivery — SOLVED + playtest-verified (2026-06-30)** — completing the
  chicken dungeon (objective 300) now delivers its reward materials (Defenderidium 1002 ×3 + Smoldering Eye 1004
  ×2) to the client **in-session**, and the renovation/castle-crafting panel correctly shows them. Mechanism:
  the EndAttack response carries **5× `InboxItemsAddedNotification` (type 111) `InboxConsumableItem`**
  (`ItemType:4`, `HeroItem:{StackCount:1,TemplateId}`, one per unit) — the same client-driven path as gear loot
  (NOT a GAI re-seed, NOT a wallet currency). **Dead ends (do not repeat):** a GAI `ClientCraftingMaterials`
  re-seed is the WRONG model (account model is frozen at boot → only updates on reconnect), and **reconnecting
  into a half-finished tutorial step HANGS the client** (infinite loader on the first screen). Full record + all
  gotchas: [objectives.md §3.3–3.4](../code-analysis/rest-api/objectives.md).
- ✅ **Objectives generalized into a data-driven MISSION MANAGER** — `MissionCatalog` loads every objective
  from `OBJECTIVESETTINGS.JSON` (conditions/rewards/`Requirements`); `MissionManager.OnEndAttack` scores every
  active "Attack" mission generically (replaces the old per-objective hardcoding). See
  [objectives.md](gameplay/objectives.md).
- ✅ **PvP-tutorial bot-castle pool + objective 301 → 302 chain — SOLVED + playtest-verified (2026-07-07).**
  The FTUE routes into a PvP beat (objective **301** "Q2 - Friendly Pillage", scoped `CastleTypes:["User"]`:
  just `CastleEntered`+`CastleCompleted`, no kill count) right after objective 300. Three real bugs found and
  fixed through iterative playtest:
  1. `AttackSelectionService.hqs/GetAttackSelectionList.json` was **leaked 2016 production data** colliding
     with our own witch-dungeon castle (`AccountId 3` reused) — replaced with a real, spec-derived bot-castle
     pool (`tools/make_tutorial_castle.py`, AccountIds 2/3/4/5/6/7/71/72/73/8/9/10/74/75/76/80 — real retired
     `PVE_01/02/03_*` dungeons, collision-checked against every objective `CastleId` and assignment
     `UbisoftCastleId`). Two follow-on regressions from the same rewrite (accidentally dropping the forest
     castle and Tybalt's Farm from the list) were also caught and fixed.
  2. `EndAttack`'s `CastleType` was **hardcoded `"Ubisoft"` always**, so `MissionManager` could never score a
     `CastleTypes:["User"]` mission — fixed (`CampaignCastleIds` set decides Ubisoft vs User).
  3. **The bigger one**: objective 301's "intended" unlock (assignment `000180`'s bare-trigger
     `UnlockObjectiveAssignmentActionSpec`) never actually fires in our reimplementation, despite being real
     retail data — confirmed via live CDP (`objective_getAllObjectives` showed `UnlockedObjectives:[]`
     indefinitely) after an extensive native reverse-engineering pass (memory dump + Ghidra decompile at the
     exact stuck state) came up short on WHY. Fixed the right way: `MissionManager` now pushes the
     already-proven type-17 `ObjectiveUnlockedNotification` itself whenever a mission's `Requirements` are
     satisfied, instead of depending on the unreliable assignment-VM path. **Confirmed live**: attacking any
     bot castle completes 301 and correctly unlocks 302 ("Revenge of the Birds", castle 101 — not yet built).
  Also fixed `GetAttackSelectionList`/`GetCastleInfo` — the latter is now a real dynamic handler in
  `GameEndpoints.cs` (was a static file that silently retargeted every attack to whichever castle it
  described; deleted). See [objectives.md](gameplay/objectives.md) and
  [code-analysis/rest-api/objectives.md](../code-analysis/rest-api/objectives.md).
- ✅ **Castle renovation level-up + persistence — implemented AND confirmed via real play (2026-07-07).** See
  [castle-building.md](content/castle-building.md) for the full mechanism (`AssignmentCatalog` +
  `CastleRenovationCatalog`, `CastleRenovationLevel` 0→1 fired naturally through real Castle Crafting UI use,
  not just simulation).
- 🟡 **World-map castle ring needs more real seed castles (cosmetic, tracked)** — the attack-selection screen
  spawns opponent castles in a 360° ring around the player's own castle, evenly distributed by count. Our pool
  (5-7 castles per level) is too small to fill every direction, so some camera angles show empty space — this
  does NOT block progression (any castle satisfies the objective), just a visual gap. See
  [`docs/OPEN_ISSUES.md`](../OPEN_ISSUES.md).
- 🟡 **DB schema drift — found + repaired (2026-07-07), not yet a proper migration.** The live dev `mqel.db` had
  drifted from an earlier, since-reverted experiment (migrations `CastleBuildingJson`/`PublishedCastleJson`
  dropped the `Castles`/`CastleRooms`/`CastleBuildings` tables; the code was reverted but the DB never rolled
  back) — every account load 500'd (`no such table: Castles`). Repaired by hand: recreated the three tables per
  the `Initial` migration's schema (they're empty/dead either way — see the fableReview §5.4 decision below) and
  removed the two orphaned `__EFMigrationsHistory` rows. **This was a manual DB fix, not a new EF migration** —
  fine for now since the tables now match what's actually in source, but flag if this class of drift recurs.

- ✅ **Server code-health pass (fableReview, 2026-07-03)** — acted on the [fableReview.md](../fableReview.md)
  findings (plan + verification in [fableReviewFixes.md](../fableReviewFixes.md)). Fixed: the **P0 crafting-material
  data-loss** (`WithGraph` was missing `.Include(CraftingMaterials)` → materials read empty after reconnect + a
  UNIQUE-constraint crash on re-earn); **consumable inbox items persisting as gear** (added `InventoryItem.StackCount`
  + `ItemType` branch, migration `InventoryStackCount`); **admin writes now hold the per-account gate** (were racing
  the live game save); `AccountEdit` fields made **nullable/apply-if-present** (a partial POST no longer zeroes the
  wallet / de-levels the hero) + long→int clamp; **raw EndAttack bytes now persisted verbatim + the `IVerificationService`
  seam is actually called** (was a lossy UTF-8 read, then discarded); hand-built JSON/cookie bodies rebuilt with
  `JsonObject` + steamID sanitised; dead closure state (`seenAccountFetch`/`gaiCount`) removed; the hardcoded
  `d:\mqel-trace.log` replaced with a **config-pathed, lock-serialised** wire log; the three drifting include-graphs
  unified into one `AccountGraph.Includes`; the notification `$type` table centralised into one `Notifications`
  class; swallowed exceptions now logged, the SendCommands catch narrowed per-command, and a `STUB 200 {path}` line
  added; SQLite `busy_timeout` + WAL enabled. **First tests in the repo**: `MQEL.Tests` — an `AccountMapper` EF
  round-trip suite (3 green) that pins the two persistence fixes. **All wire-body changes are byte-shape-preserving
  but still need a live playtest pass to confirm** (the client fails silently). NOT done (deferred, by design): the
  Program.cs god-file split and the full golden-file wire harness — both need the live client to verify safely.

## In progress / known

- 🟡 **Multi-user identity routing** — the identity seam (`IAccountResolver`) is currently **pinned to one
  fixed dev account**; every request routes to it. Real `SteamID → account` routing is the next infra piece,
  needed for **cross-account castle visibility and raiding** tests. "Multi-user = swap the resolver"
  **understates it** — preconditions before flipping it on (fableReview §5.2/§3.8):
  1. **Session store** — `/launcher/load` mints a token into a cookie and forgets it; add a `Sessions` table
     (token → SteamId → AccountId) so the resolver has something to look the token up in (this also gives the
     auth check for free).
  2. **No host-lifetime request state** — the Program.cs closure counters are gone (2026-07-03); keep it that way,
     or account A's session bleeds into B.
  3. **Bound the per-account dictionaries** — `accountLocks`/`attackScratch`/`missionProgress` need a last-seen
     eviction sweep at N accounts (fine forever at 1).
  4. **Generalise the admin/save-state endpoints** — snapshot capture/restore are pinned to `devAccountId`.
  5. **AccountId allocation** — a scheme for unknown tokens (SteamId-derived or sequence); today every unknown
     token maps to the one dev account (safe, but not multi-user).
  - **Security gates (do before any 2nd untrusted machine can reach this):** set/require the admin token (or bind
    admin+dashboard to loopback), persist+**check** the session token, validate the Steam ticket.
- 🟡 **Metagame breadth** — beyond the tutorial: castle building, shop, inventory, hero management, PvP.
- 🟡 **Anti-cheat / audit substrate** — as of 2026-07-03 the EndAttack **raw body (JSON + binary replay blob) is
  now persisted verbatim** (append-only, one file per attack under the audit dir) and the `IVerificationService`
  seam **is called** with an `AuditBundle`; the verdict is still stubbed (`valid=true`) and the re-sim compute is
  deferred. Remaining gap: **no per-attack `AttackRandomSeed` is issued/recorded at StartAttack** (bundle seed = 0)
  — record it there before the re-sim verifier is built. ([verification.md](ops/verification.md))
- 🟡 **Real build-mode geometry — DECIDE BEFORE IT STARTS (fableReview §5.4, still open).** The renovation
  LEVEL-UP is done (see "Done" above), but the player's own castle still has zero rooms/buildings — expected for
  now, since defense/build-mode only unlocks after all 4 renovation levels (we're at 1). The relational
  `Castle`/`CastleRoom`/`CastleBuilding` schema is currently **dead** (mapped in the include graph, but no
  handler reads/writes it; campaign castles are `responses/castles/*.json`, the GAI `BuildInfo` is canned).
  Recommended: **store the player castle as a JSON document** (the `Draft`/`Published` `BuildInfo` verbatim, as
  the client round-trips it) + a few promoted columns, and **drop the three relational castle tables in the same
  migration** — the client speaks whole-graph JSON and fidelity is the requirement. Keeping half-built relational
  tables is the worst option: build-mode would try to shred whole-castle JSON (traps/decorations/triggers/next-
  index counters — none of which the tables model) into a shape that doesn't fit. See
  [castle-building.md](content/castle-building.md).

## Current frontier

- FTUE, the in-session reward loop, **durable persistence**, and the objective/PvP-tutorial chain through
  objective 302 all work end-to-end (live-playtest-verified). There is **no single blocker**; the frontier is
  **breadth** (the full metagame, remaining renovation levels, real build-mode) and **multi-user** (real
  per-Steam-ID accounts → cross-account raiding).

## Next (in leverage order)

1. ⬜ **Fendrick's Farm (objective 302, castle 101)** — not yet built. Same pattern as Tybalt's Farm: add its
   spec (`000101 - PVE_R01Q030_BLACKMAGICFARM.JSON`) to `tools/make_tutorial_castle.py`, generate the response,
   wire it into `GetAttackSelectionList.json` as a campaign entry (add 101 to `CampaignCastleIds` in
   `GameEndpoints.cs` too).
2. ⬜ **Remaining renovation levels (2→3→4→Complete)** — mechanically identical to 1→2 (same
   `ExecuteAssignmentActionCommand` pattern, costs already known), should "just work" but not yet playtested
   that far.
3. ⬜ **Multi-user routing** — resolve `SteamID → account` instead of the fixed dev account, so cross-account
   castle visibility and raiding can be tested.
4. ⬜ **Broaden the metagame** — shop, inventory, hero management, the account-model surfaces.
5. ⬜ **Wire the audit substrate** — persist seed+result+replay (the EndAttack binary blob) for the deferred
   anti-cheat re-sim.
6. ⬜ **World-map castle-ring seed castles** (cosmetic, doesn't block anything) — see
   [`docs/OPEN_ISSUES.md`](../OPEN_ISSUES.md).
7. ⬜ **Real build-mode room/trap placement** — blocked behind finishing all 4 renovation levels; needs the
   fableReview §5.4 data-model decision first (see above).

## Ideas / later

- 💡 **Pre-patch "snapshot all accounts" backup** _(extends save-states)_ — the admin save-state tool today
  clones the **single dev account** (`storageOptions.DefaultAccountId`) into a named template. Once multi-user
  routing lands, generalise it to **capture every account into one timestamped snapshot set before applying a
  big patch or schema migration**, so a bad rollout can be rolled back wholesale rather than per-account. The
  capture/restore plumbing (`AccountMapper.ToAccountState`/`ApplyTo`, template rows in negative-AccountId
  space) is already account-agnostic — only the hardcoded `devAccountId` needs to become "iterate all live
  accounts," plus a snapshot-set grouping/label in the UI.
