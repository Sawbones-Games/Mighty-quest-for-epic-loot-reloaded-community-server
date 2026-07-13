# Tutorial steps — how to trace & implement the next FTUE step

> **Status:** FTUE + campaign chain playtest-verified through objective 302 (Fendrick's Farm intro) ·
> **Server:** gameserver · **Updated:** 2026-07-07

## Purpose

The first-time-user experience (FTUE) is a chain of **assignments** run by a **client-side data-driven state
machine** (the *Assignment VM*). The coaching popups, arrows, screen navigation, castle locks and objective
unlocks are **not** server pushes — the client runs them from local specs in `GameplaySettings`. This doc is
the durable method for **figuring out what the next tutorial step needs from the server and implementing it**,
so each new step is a repeatable data-wiring job, not a re-derivation. It complements
[castles.md](castles.md) (the castle a step sends you into) and
[progression-loop.md](../gameplay/progression-loop.md) (the reward loop a step rides on).

> **The mental model that took two days to earn:** the server's job in the tutorial is small and specific —
> serve the **right castle** (with any trigger volumes the active assignment listens for) and the **right
> rewards/notifications**, and **ack/record the commands** the client sends. The coaching itself is the
> client's. Most "the tutorial is stuck" bugs are a missing castle trigger, a missing notification, or a
> missing reward — not a missing coaching push.

## Key code

| Type / file | Role |
|-------------|------|
| [`GameEndpoints.cs` SendCommands](../../MQELServer/src/MQEL.Gameserver/GameEndpoints.cs) | acks the client command batch; records `CompleteAssignmentCommand`, resolves `ExecuteAssignmentActionCommand` via `AssignmentCatalog` (and is where new command cases go) |
| [`AssignmentCatalog`](../../MQELServer/src/MQEL.Gameserver/AssignmentCatalog.cs) | resolves `ExecuteAssignmentActionCommand{AssignmentId,ActionIndex}` back to the real action spec — the command never carries the payload itself |
| [`AccountState.CompletedAssignments`](../../MQELServer/src/MQEL.Gameserver/AccountState.cs) | finished assignment ids; re-emitted in the GAI so reconnect skips done steps |
| [`BuildAccountInformation`](../../MQELServer/src/MQEL.Gameserver/AccountState.cs) | emits `CompletedAssignments` + `Objectives` so the client resumes mid-tutorial |
| [`GameEndpoints.cs` StartAttack](../../MQELServer/src/MQEL.Gameserver/GameEndpoints.cs) | serves the castle (+ its triggers) an in-attack assignment waits on |
| [`MissionManager`](../../MQELServer/src/MQEL.Gameserver/MissionManager.cs) | pushes type-17 `ObjectiveUnlockedNotification` server-side when an assignment-VM unlock can't be trusted to fire — see the "bare trigger" gotcha below |

## How it works

### 1. The Assignment VM (client-side)
- An **`AssignmentSpec`** = `Assignments/<id>/GAMEPLAY.JSON` → `{GroupId, Enable, Trigger, Actions[]}`
  (+ a sibling `UI.JSON`). `Actions` run sequentially; each may have a `Condition` (gate) and `EndTriggers`
  (what advances it).
- Assignments **chain by trigger**, not numeric order:
  `Trigger: AssignmentCompletedAssignmentTriggerSpec {AssignmentId:N}` fires this assignment when assignment
  N completes. To find the **next** step, search for the assignment whose trigger points at the current one.
- The client loads all assignment specs locally; the server usually does **not** need to drive the VM
  (it *can* via `StartAssignment`/`ExecuteAssignmentAction`/`CompleteAssignment` commands). **Exception
  learned 2026-07-07:** a *bare* trigger (`AssignmentCompletedAssignmentTriggerSpec` with `AssignmentId`
  omitted, defaulting to 0) cannot be assumed to fire reliably even in real retail data — objective 301's
  intended unlock via assignment `000180` never fired in our reimplementation despite matching the reference
  spec exactly. When a VM-driven unlock doesn't fire, the proven fallback is a server-pushed type-17
  `ObjectiveUnlockedNotification` (see [objectives.md](../gameplay/objectives.md) "Chaining") — verify live via
  CDP before trusting a bare-trigger path, don't assume it "just works."

### 2. The trigger / action vocabulary (the building blocks)
Recovered from the decrypted assignments. **Triggers** (what advances an action/assignment):
`AssignmentCompleted` (chain), `ObjectiveCompleted`, `AttackStarted {CastleId}`, `ServerAttackEnded`,
`SimulationLoaded`, `LoadingScreenHidden`, `TriggerEvent {TriggerId, TriggerState}` (hero enters/leaves a
castle trigger volume), `HeroInsideTrigger {TriggerId}`, `EntityDied` (creature death),
`AnyItemEquipped`, `ItemPurchased`, `ShopShown`, `PanelShown {panel}`, `AbilityLaunched`, `EmoteLaunched`,
`PlayerMiniCastleSelected`. **Actions** (what a step does):
`GoToScreen`, `LaunchAttack {UbisoftCastleId}`, `SetAttackSelectionLockOn {AccountId, LockOnDisabledPickingType}`
(lock the world map to one castle; `AccountId:-1` = unlock), `UnlockObjective {ObjectiveId}`,
`DisplayObjectiveCompleted`, `Popup {Type: FloatingNarrativeBox|Arrow|SimpleBox, TextOasisId, GameButtonId,
ArrowDirection, Position}`, `LockWidgets`/`UnLockWidgets`, `Wait {Conditions, EndTriggers, Duration}`,
`AddItemsToInventory`, `DisplayPlayerCastleInAttackSelection`, `SetLastVisitedShopCategory`,
`EnableHeroLevelUpNotification`, `SetAfterAttackNavigation`.

### 3. Castle triggers — the one server hook the coaching needs
An in-attack assignment that waits on `TriggerEvent {TriggerId:N}` only advances when the hero enters
**CastleTrigger volume Id N** — a placed volume in `Rooms[].Triggers[]` (`SpecContainerId 52`,
`{Id, X, Y, SizeX, SizeY, RoomZoneId, Orientation}`). **The served castle MUST contain the trigger Ids the
active assignment references**, or coaching can never advance — there is no "player moved"/timer fallback.
This is the server's main coaching obligation (real specs carry these; substitute castles don't).

### 4. Objectives / quests (the campaign layer)
Beyond pure coaching, campaign steps unlock **objectives** (quests with a tracker, completion conditions and a
reward). Conditions like `DefenseIngredientDestroyed {SpecContainerId, Count}` are counted **client-side**
during the raid. The **server owns the objective's persistence (GAI `Objectives`) and the completion reward**.
This is a newer surface — its wire contract is being traced in
[objectives.md](../../code-analysis/rest-api/objectives.md).

### 5. The known FTUE chain (for orientation)
```
10 intro → 20 starter-castle select → (21–26 tour) → 30 name → 40 castle present → 90 hero select
→ 120 first tutorial castle (StartAttack 2; movement/attack/door/stairs coaching)
→ 125 equip a skill (witch dungeon, skill unlock at L2)
   ├─ build-tutorial branch: 130 add room → 140 trap → 145 trap generator → 170 validate (attackType:4)
   │  [not yet reached — gated behind finishing all 4 renovation levels]
   └─ castle-crafting branch: 005003 (off 120) → 005005 Tybalt's Farm/Chickenvaders (off 125)
      → objective 300 completes → 005006 opens CastleRenovationPanel → 005007
      → objective 301 unlocks (server-pushed type-17, see §2 above) → PvP-tutorial castle attacks
      → CastleRenovationLevel 0→1 (RenovationLevel2) via ExecuteAssignmentActionCommand
      → objective 302 unlocks (Fendrick's Farm intro)                 ← last verified-working step (2026-07-07)
```

## How to … implement the next tutorial step

1. **Find the next assignment.** From the last completed assignment id, grep `Assignments/*/GAMEPLAY.JSON`
   for `AssignmentCompletedAssignmentTriggerSpec {AssignmentId:<that id>}`. Read its `Actions[]`.
2. **List what the server owes.** For each action decide: does it need a **castle** (LaunchAttack → serve
   `responses/castles/<id>.json` per [castles.md](castles.md), with the trigger Ids it references)? an
   **objective**? a **reward/notification**? a **command ack/record**? Coaching popups/arrows/locks need
   **nothing** from the server.
3. **Decrypt the supporting specs.** Castle layout, objective (`OBJECTIVESETTINGS.JSON`), creatures, materials,
   and localized text (`Oasis/OASIS_EN.JSON`). Build every value from these — no canned data
   ([[feedback-no-shortcuts-correctness]]).
4. **Implement server-side first** ([[feedback-client-patch-last-resort]]): serve the castle, populate state,
   handle the new commands, grant the reward.
5. **Verify on the live client (the oracle).** Boot the patched client, restore a save-state to the step,
   capture the **real** `SendCommands`/`EndAttack` bodies + read CDP view-models, and implement exactly what
   the client reveals it expects — restart, repeat ([emulate-boot-step](../../.claude/skills/emulate-boot-step/SKILL.md),
   [inspect-live-client](../../.claude/skills/inspect-live-client/SKILL.md)). Re-run unpack-and-decompile at the
   milestone ([[feedback-redump-every-milestone]]).
6. **Record completion.** Ensure the step's `CompleteAssignmentCommand` is acked + recorded so reconnect skips
   it; snapshot a new save-state for the next step.

## Design notes & gaps — anti-patterns & red herrings
- **"The tutorial is stuck" is almost never a missing coaching push.** Check, in order: a missing **castle
  trigger** the assignment waits on; a missing **notification** (wallet/xp/loot/level sync — type-24/43/47/111,
  [progression-loop.md](../gameplay/progression-loop.md)); a missing **reward**; a malformed response. The VM advances on
  these, not on a server "advance" message.
- **Don't force a `SendCommands` failure to trigger a re-fetch** — the client treats a failed `SendCommandsTask`
  as an unrecoverable network error and **crashes** ([progression-loop.md](../gameplay/progression-loop.md) dead-ends).
- **Don't force-grant** the step's reward/skill/objective. The gate is usually a *level* or a real completion
  signal a grant doesn't move; force-grants are forbidden **and** often ineffective.
- **`MQELOffline_cpp` skips the tutorial** and hardcodes attacks — trust it for envelope/contract **shapes**,
  never for tutorial behaviour or values ([[reference-mqeloffline-cpp]]).
- **Assignments chain by trigger, not number** — `005005` (Tybalt's) triggers off `125`, not `5004`. Don't
  infer order from ids. **A bare trigger (no `AssignmentId`) is not reliable** — don't assume it fires just
  because it's real retail data; verify live via CDP before depending on it (see §1 above).
- **`bin/` response files are stray** — edit the project `responses/` tree, not `bin/` (see [castles.md](castles.md)).
- **In-session state is process-global per account + resets on server restart** — use admin save-states to
  re-enter a step quickly ([persistence.md](../ops/persistence.md)).

## Related
- [castles.md](castles.md) — serving the castle a step attacks
- [progression-loop.md](../gameplay/progression-loop.md) — the reward/notification loop steps ride on
- [objectives.md](../../code-analysis/rest-api/objectives.md) — the quest/objective wire (new surface)
- [attack-service.md §4](../../code-analysis/rest-api/attack-service.md) — the Assignment-VM mechanism in detail
- [`.opencode/plans/tybalts-farm-castle.md`](../../.opencode/plans/tybalts-farm-castle.md) — worked example
</content>
