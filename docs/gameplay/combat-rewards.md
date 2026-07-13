# Combat rewards — StartAttack loot tables → EndAttack scoring

> **Status:** implemented (playtest-verified) · **Server:** gameserver · **Updated:** 2026-06-29

## Purpose

When a hero raids a castle, the reward (gold, life-force, XP, looted gear) is **summed by the server from
what the client reports it looted** — the backend does not dictate amounts, and the client never sends totals.
This doc covers the two-call mechanism: `StartAttack` hands out the **loot tables**; `EndAttack` reports the
**looted instance-IDs**; the server **scores** them and credits the account.

## Key code

| Type / file | Role |
|-------------|------|
| [`Program.cs` StartAttack](../../MQELServer/src/MQEL.Gameserver/Program.cs) | serves the castle, generates `CreatureLoot` per instance-Id, stamps class loot, **stores the loot tables** in the scratch |
| [`Program.cs` EndAttack](../../MQELServer/src/MQEL.Gameserver/Program.cs) | parses the looted-IDs, sums the stored tables, credits + persists, emits notifications |
| [`AttackScratch`](../../MQELServer/src/MQEL.Gameserver/Account.cs) | the transient per-attack tables (`CreatureLoot`/`TrapLoot`/`CreatureItems`/`CreatureSpecById`), survive the StartAttack→EndAttack pair |
| [`AccountState.ClassFirstLootTemplate`](../../MQELServer/src/MQEL.Gameserver/Account.cs) | per-class first-loot TemplateId (Knight 17 / Archer 81 / Mage 53 / Runaway 311) |

## How it works

1. **StartAttack** builds the attack payload (castle from [castles.md](../content/castles.md), the player's real hero,
   loot tables, settings). `CreatureLoot` is keyed by the **placed-creature instance Id** (`Rooms[].Creatures[].Id`,
   NOT `SpecContainerId`); a missing entry drops zero — so we cover **every** instance in the served castle
   (captains/elites get a bigger entry). The trap's first-loot drop is **class-stamped** so the in-mission item
   matches the reward. We then **store** these tables (+ an instance→spec map) in the per-account scratch.
2. **EndAttack** body is a **JSON object immediately followed by a binary replay blob** — parse **only the
   leading JSON** (brace-match to the balanced close; deserializing the whole body throws → zero reward, the
   exact "rewards went to 0" bug). The `endAttackParams` reports **which instance Ids were looted**
   (`LootedGoldCreatureIds`, `LootedLifeForceCreatureIds`, `KilledCreatureIds`, `LootedHeroItemTrapIds`,
   `TreasureRoom*`) — not amounts.
3. **Score:** sum the stored tables over the reported Ids → real gold / life-force / XP, plus the looted gear
   items. (Verified: sums equal the player's exact in-HUD gather, e.g. +34/+34.)
4. **Credit + persist** to the account ([wallet.md](wallet.md) clamps to capacity; [hero-progression.md](hero-progression.md)
   accrues XP), then emit notifications: **type-24** wallet delta, **type-43** hero XP, **type-111** one per
   looted item (fresh `ObjectId`, class-stamped). Delivery: [notifications.md](notifications.md).
5. **Objective scoring** also happens here (kill-by-spec) — see [objectives.md](objectives.md).

## REST / wire
`StartAttack` / `EndAttack` / `Resurrect` request+response JSON is owned by
[`../code-analysis/rest-api/attack-service.md`](../../code-analysis/rest-api/attack-service.md). This doc is the
implementation/scoring; the shapes live there.

## Data / persistence
The loot tables are **transient** (`AttackScratch`, per-account in-memory, re-derivable from the castle) —
never persisted. The credited gold/XP/items **are** persisted (see [persistence.md](../ops/persistence.md)).

## Design notes & gaps
- ⛔ Parsing the whole EndAttack body (not just the leading JSON) throws on the trailing binary → silent zero
  reward. Always brace-match.
- The binary **replay blob** is captured but unused — it's the substrate for the deferred anti-cheat re-sim
  ([verification.md](../ops/verification.md)).
- `UpdatedAccountStats` in the EndAttack Result is still the template's static block (cosmetic).
- `RateCastle` (+50 IGC) is not yet implemented.

## Related
- [castles.md](../content/castles.md) — the castle StartAttack serves · [hero-progression.md](hero-progression.md) — XP/level
- [wallet.md](wallet.md) — currency credit + caps · [notifications.md](notifications.md) — type-24/43/111
- [attack-service.md](../../code-analysis/rest-api/attack-service.md) — authoritative shapes · memory `project-endattack-scoring`
</content>
