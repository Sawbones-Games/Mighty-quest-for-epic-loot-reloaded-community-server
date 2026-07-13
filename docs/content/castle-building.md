# Castle building & renovation

> **Status:** renovation LEVEL-UP implemented + confirmed via real play (2026-07-07); room/trap BUILD-MODE
> geometry still not implemented (blocked behind 3 more renovation levels) · **Server:** gameserver ·
> **Updated:** 2026-07-07

## Purpose

After the Tybalt's Farm objective completes ([objectives.md](../gameplay/objectives.md)) and the PvP-tutorial
objective (301) is completed, assignment `005006`/`005007` send the player to **build their first castle
floor** — the **castle renovation** flow. This doc covers what's actually implemented (the level-up mechanism)
and what's still ahead (real build-mode: placing rooms/traps/buildings, which only unlocks after finishing all 4
renovation levels).

## What's implemented (2026-07-07)

**The ONE server-bound piece of this system**, confirmed correct by both a direct simulation AND real live play:
the client reports a renovation level-up via `SendCommands → ExecuteAssignmentActionCommand{AssignmentId,
ActionIndex}` — this command carries ONLY those two integers, never the action's payload
([command-queue.md](../../code-analysis/rest-api/command-queue.md) §5.7). The server resolves it back to the
real `SetCastleRenovationLevelAssignmentActionSpec{CastleRenovationLevel}` via two new catalogs:

- **[`AssignmentCatalog`](../../MQELServer/src/MQEL.Gameserver/AssignmentCatalog.cs)** — indexes every
  `GameplaySettings/Assignments/*` folder by its numeric ID prefix, lazily parses+caches a given assignment's
  `Actions[]` on first lookup. Given `{AssignmentId, ActionIndex}`, returns the actual action spec.
- **[`CastleRenovationCatalog`](../../MQELServer/src/MQEL.Gameserver/CastleRenovationCatalog.cs)** — loads the
  exact per-level material costs from `GeneralSettings/CASTLERENOVATIONSETTINGS.JSON`. Level1→2 = Defenderidium
  (1002)×3 + SmolderingEye(1004)×2 — confirmed to match objective 300's reward exactly (hand-tuned, zero
  surplus). Costs continue through 2→3/3→4/4→Complete (different materials — see the settings file).

On the command, `GameEndpoints.cs` deducts the cost from `AccountState.CraftingMaterials` and persists
`CastleRenovationLevel` (0-based — `RenovationLevel1=0`, `RenovationComplete=4`) via `AccountMapper`/
`Account.CastleRenovationLevel` (a DB column that existed since the `Initial` migration but was unused until
now); GAI reports the real level instead of a hardcoded `0`.

**Confirmed working through real play** (not just a direct command simulation): the wire trace shows
`CastleRenovationLevel -> RenovationLevel2 (1)` firing naturally after the player used the Castle Crafting UI —
the client handles the ENTIRE build-panel UI/flow locally (spending materials it already has, no server round
trip needed for `build_get*`), then reports the result via the one command above.

## What's confirmed NOT server-bound (engine-local only)

`build_getBuildModel`, `build_getCastleLevelInfo`, `build_getCastleLevelUpInfo`, `build_getBuildToolbarModel`,
`build_getCastleValidityState`, `building_getBuildingUpgradeViewModel`, `buildingNavBar_getBuildingNavBarModel`
are **engine-LOCAL native IPC calls** (`CastleRenovationController`), never `.hqs` endpoints — confirmed by zero
wire traffic for any of them across the whole project's capture history, and the renovation flow working
end-to-end without ever needing to serve them.

## What's still NOT implemented

1. **Real build-mode room/trap/building placement geometry.** The player's own castle (`BuildInfo.Draft` in the
   GAI) still has zero rooms/buildings — just a flat `CastleRenovationLevel` int. This is **fine and expected
   right now**: defense/build-mode (where this would matter) only unlocks after finishing **all 4** renovation
   levels, and we've only reached level 1→2. Do NOT try to populate `BuildInfo.Draft` prematurely — the game
   genuinely doesn't need it yet, and the correct wire shape for a player-owned buildable room (as opposed to a
   campaign dungeon room) has never been captured live.
2. **Renovation levels 2→3, 3→4, 4→Complete.** Mechanically identical to 1→2 (same command pattern, costs
   already known from `CASTLERENOVATIONSETTINGS.JSON`) — should "just work" once the player reaches them, but
   not yet playtested.
3. The build-mode **commands** (`AddCastleRoom`/`Trap`/`Building`/`UpgradeBuilding`… — see the
   [`ServerCommandType` enum](../../code-analysis/rest-api/response-contracts.md)) for the eventual build
   tutorial (`130/140/145/170` assignment chain).
4. `attackType:4` **castle validation** (the validate-your-castle step).
5. Real castle 101 ("Fendrick's Farm") — objective 302 needs it, not yet built. See
   [objectives.md §"Next system"](../../code-analysis/rest-api/objectives.md).

## REST / wire
Nothing new to trace — the ONE server-bound mutation (`ExecuteAssignmentActionCommand`) is implemented and
verified; the `build_get*` calls are confirmed engine-local (no wire shape to capture). If real build-mode
placement is tackled later, THAT will need a fresh `trace-rest-endpoint`/`inspect-live-client` pass.

## Related
- [objectives.md](../gameplay/objectives.md) — the reward/unlock chain that hands off into this
- [tutorial-steps.md](tutorial-steps.md) — the assignment chain (`005006`/`005007`, `130/140/145/170`)
- [castles.md](castles.md) — serving a campaign castle layout (the *attack* side; build is the inverse, and
  structurally different — campaign rooms are fight-level layouts, not buildable player rooms)
- [STATUS.md](../STATUS.md) — current frontier
- [`docs/OPEN_ISSUES.md`](../OPEN_ISSUES.md) — tracked deferred gaps (world-map castle ring)
</content>
