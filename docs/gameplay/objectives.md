# Objectives & quests — campaign objective tracking + completion

> **Status:** implemented, data-driven (playtest-verified through objective 302) · **Server:** gameserver ·
> **Updated:** 2026-07-07

## Purpose

Beyond the FTUE coaching ([tutorial-steps.md](../content/tutorial-steps.md)), campaign progression is driven by
**objectives** — quests with a tracker, completion conditions, and a reward (e.g. *Q1 – Chickenvaders*: raid
Tybalt's Farm, kill 20 chickens, get crafting materials). This doc is how **our server** tracks and completes
them. The single load-bearing fact, learned the hard way: **objective completion is server-authoritative and
delivered as an EndAttack notification** — the client's in-attack condition ticks are just a HUD; the
*metagame* objective (which the assignment chain watches) only completes when the server says so. A second,
equally load-bearing fact learned later: **objective UNLOCKING can't be assumed to be assignment-VM-driven
either** — verify live, don't assume retail data "just works" in our reimplementation (see §"Chaining" below).

## Key code

| Type / file | Role |
|-------------|------|
| [`AccountState.Objectives`](../../MQELServer/src/MQEL.Gameserver/AccountState.cs) | per-objective player state (`Objective {ObjectiveId, Status, LastStatusUtc}`) |
| [`AccountState.CraftingMaterials`](../../MQELServer/src/MQEL.Gameserver/AccountState.cs) | material reward store (`MaterialId → qty`) |
| [`GameEndpoints.cs` SendCommands](../../MQELServer/src/MQEL.Gameserver/GameEndpoints.cs) | records `ObjectiveUnlockCommand` (Status=1) / acks `ObjectiveViewedCommand` |
| [`MissionCatalog`](../../MQELServer/src/MQEL.Gameserver/MissionCatalog.cs) | loads EVERY objective from `OBJECTIVESETTINGS.JSON` — conditions, rewards, `Requirements` — data-driven, nothing hardcoded per-objective |
| [`MissionManager.OnEndAttack`](../../MQELServer/src/MQEL.Gameserver/MissionManager.cs) | scores every active "Attack" mission generically, sends type-14 completion + reward notifications + type-17 chain-unlock |
| [`AttackScratch.CreatureSpecById`](../../MQELServer/src/MQEL.Gameserver/AccountState.cs) | instance-Id→spec map (built at StartAttack) for faithful kill-by-spec scoring |
| [`BuildAccountInformation`](../../MQELServer/src/MQEL.Gameserver/AccountState.cs) | seeds GAI `Objectives` from state for reconnect |

## How it works

1. **Unlock.** Either (a) the assignment VM runs `UnlockObjectiveAssignmentActionSpec {ObjectiveId}` client-side
   and the client sends an **`ObjectiveUnlockCommand`** in its next `SendCommands` batch (works reliably for
   ID-triggered assignments, e.g. 300 via `005005`), or (b) the server itself pushes a type-17
   `ObjectiveUnlockedNotification` when a mission's `Requirements` are satisfied (needed when the assumed
   assignment-VM path doesn't actually fire — see "Chaining" below). Either way we record `Status=1` (active)
   and persist it.
2. **Track (client-side HUD).** During the raid the engine's own condition-checkers tick the conditions
   visually. This is **not** authoritative — it never commits to the metagame objective on its own.
3. **Score + complete (server).** On `EndAttack`, `MissionManager.OnEndAttack` evaluates EVERY active "Attack"
   mission's real conditions from the attack report — generically, not hardcoded per-objective. Conditions:
   `CastleEntered` (reaching EndAttack on the castle), `CastleCompleted`, `Destroyed` (kill count by
   `SpecContainerId`, via the instance-Id→spec map built at StartAttack). When satisfied it pushes, in the
   EndAttack `Notifications`, an **`ObjectiveCompletedNotification {ObjectiveId, NotificationType:14}`** — see
   [notifications.md](notifications.md).
4. **Persist.** Sets `Objective.Status=2` so the GAI reflects it on reconnect.
5. **Reward.** Materials/currency/XP/gear from the objective's spec are credited and delivered in-session (type
   111 `InboxItemsAddedNotification` for materials/gear, type-24 for currency, type-43 for XP).
6. **Chaining.** Immediately after completing a mission, `MissionManager` reverse-scans the catalog for any OTHER
   mission whose `Requirements` are now ALL satisfied and not yet unlocked, and pushes type-17
   `ObjectiveUnlockedNotification` for it in the SAME response (301 after 300, 302 after 301 — confirmed live).
   **Why this exists as an explicit server push rather than relying on the assignment VM:** objective 301's
   "intended" unlock path was assignment `000180`'s `UnlockObjectiveAssignmentActionSpec`, gated by a BARE
   `AssignmentCompletedAssignmentTriggerSpec` (no explicit `AssignmentId`). Despite this being real retail data,
   extensive live verification (CDP `objective_getAllObjectives` showing `UnlockedObjectives:[]` indefinitely)
   plus a deep native reverse-engineering pass proved this trigger never actually fires in our reimplementation.
   Rather than keep chasing why, we used the OTHER proven mechanism (type-17, already confirmed via a real
   production wire capture for a different objective pair) — which works. **Lesson: verify an assignment-VM path
   live before depending on it; don't assume retail data "just works" once ported.**

## REST / wire
Endpoint shapes (the completion/unlock notification types, the `AccountObjective` shape, the objective spec
conditions/reward) are owned by **[`../code-analysis/rest-api/objectives.md`](../../code-analysis/rest-api/objectives.md)**.
Notification mechanism: [notifications.md](notifications.md). The objective's authoritative definition is the
decrypted spec `GeneralSettings/OBJECTIVESETTINGS.JSON`.

## Data / persistence
`Objective` + `CraftingMaterial` are EF tables on the account aggregate, round-tripped by
[`AccountMapper`](../../MQELServer/src/MQEL.Gameserver/AccountMapper.cs) and loaded by the repo's
`AccountGraph.Includes`. See [persistence.md](../ops/persistence.md).

## How to … add the next objective
Nothing to implement — `MissionCatalog` already loads every objective's conditions/rewards/`Requirements` from
`OBJECTIVESETTINGS.JSON`, and `MissionManager` scores + chain-unlocks generically. Adding a new objective is a
data problem, not a code problem, UNLESS its conditions use a `Kind` `MissionCatalog` doesn't yet map (check the
`Cond`/`Reward` switch statements) — extend those switches if a new condition/reward `$type` shows up.

## Design notes & gaps
- **Dead end (do not repeat):** `NotificationType:112` was a guess — the client silently ignores unknown
  types, so the tutorial stalled for a day. Real type is **14** (reference capture). The dumped contract
  catalog is incomplete for notifications; trust the `MQELOffline_cpp` captures / live wire.
- **The client never sends an `ObjectiveCompleteCommand`** — completion is server-push only.
- **Don't assume an assignment-VM trigger fires just because it's retail data** — see "Chaining" above. Verify
  live (CDP) before depending on it; the proven server-push (type-17) is the safer default when in doubt.
- Cosmetic gap (does not block progression): the world-map castle ring needs more real seed castles — see
  [`docs/OPEN_ISSUES.md`](../../docs/OPEN_ISSUES.md).

## Related
- [code-analysis/rest-api/objectives.md](../../code-analysis/rest-api/objectives.md) — the authoritative wire
- [notifications.md](notifications.md) — the type-14/17 delivery mechanism
- [tutorial-steps.md](../content/tutorial-steps.md) — how an objective is unlocked by the assignment chain
- [castles.md](../content/castles.md) — serving the quest's castle · [castle-building.md](../content/castle-building.md) — the renovation system
</content>
