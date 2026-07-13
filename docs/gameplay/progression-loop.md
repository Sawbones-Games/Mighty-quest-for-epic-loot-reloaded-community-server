# Reward & progression loop — the overview

> **Status:** implemented (playtest-verified end-to-end) · **Server:** gameserver · **Updated:** 2026-06-29

## Purpose

This is the **map** of the in-session progression loop: a hero raids a dungeon, gathers gold/soul + XP, the
rewards **persist** on dungeon-end, the hero **levels up**, the first **skill unlocks**, and (campaign)
**objectives** complete — all from real play, **no force-grants, no canned data** ([[feedback-no-shortcuts-correctness]]).
The per-feature mechanism each lives in its own doc; this page ties them together and is the durable record of
**the methodology and every dead end** so we never re-derive them.

> The single hardest lesson: **the client's view-models are frozen at boot only for the parts the server never
> updates. Everything else syncs in-session via [notifications](notifications.md).** Most "value is stuck" bugs
> were a missing/mistyped notification, not immutable state.

## The loop (and where each piece is documented)

| Step | Player sees | Mechanism | Doc |
|---|---|---|---|
| 1. Claim starter castle | castle + storage cap | `CastleBought` + two type-47 capacity notifs | [wallet.md](wallet.md) |
| 2. Gather in dungeon | gold/soul rises to a cap | client tallies the `StartAttack` loot table; cap from step 1 | [combat-rewards.md](combat-rewards.md), [wallet.md](wallet.md) |
| 3. Finish dungeon | rewards "transfer" to lobby | `EndAttack` reports looted instance-IDs → server **sums** the loot tables | [combat-rewards.md](combat-rewards.md) |
| 4. Balance + XP persist | HUD updates | type-24 `WalletUpdated`, type-43 `HeroXpChanged`, type-111 `InboxItemsAdded` | [notifications.md](notifications.md) |
| 5. Cross XP threshold | "LEVEL 2!" + skill ungates | type-43 `LevelChanged:true` → registry hero level | [hero-progression.md](hero-progression.md) |
| 6. Complete a quest | objective ✓ + chain advances | `EndAttack` type-14 `ObjectiveCompleted` | [objectives.md](objectives.md) |

Supporting features: [castles.md](../content/castles.md) (serving the dungeon), [tutorial-steps.md](../content/tutorial-steps.md)
(the coaching/assignment chain), [persistence.md](../ops/persistence.md) (durability).

## What worked — the methodology

The method that cracked every step (now the [`inspect-live-client`](../../.claude/skills/inspect-live-client/SKILL.md) skill):
1. **Read the live client before theorising.** CDP (`tools/cdp.py`) reads the real view-models; the wire
   capture (`d:\mqel-trace.log`, `captures/`) + the game's own `MQLog.txt` (`Unhandled member` warnings) are
   the oracle. Every wrong value came from reasoning on canned/reference data instead.
2. **Capture the real request/response, never guess a shape.** The looted-IDs reward model, the binary replay
   blob, and the type-14 objective notification were all invisible until we read the actual wire/captures.
3. **Build every value from an authoritative source** — the decrypted spec DB, the decompiled contracts, the
   `MQELOffline_cpp` real captures. No force-grants, no canned data.

## The dead ends (so we never repeat them)

| ⛔ Dead end | What we learned |
|---|---|
| GAI wallet `InGameCoinStorageCapacity` field / the GoldStorage building drive the cap | **No** — only the type-47 notification sets `MaxGold`/`MaxLifeForce`. [wallet.md](wallet.md) |
| Force-granting the skill (`UnlockedSpells` / `SpellUnlockedNotification`) | forbidden **and** ineffective — the gate is the *level*. [hero-progression.md](hero-progression.md) |
| Parsing the whole EndAttack body | throws on the trailing binary replay → silent zero reward. Brace-match the leading JSON. [combat-rewards.md](combat-rewards.md) |
| Forcing a `SendCommands` failure to trigger a GAI re-fetch | **crashes** the client (unrecoverable network error). Never. |
| `ObjectiveCompletedNotification` with `NotificationType:112` | a guess — the client silently ignores unknown types → tutorial stalled a day. Real type is **14**. [objectives.md](objectives.md) |
| Inferring "no objective contract exists" from `gameserver-catalog.txt` | the dumped catalog is **incomplete for notifications**; trust the `MQELOffline_cpp` captures / live wire. [notifications.md](notifications.md) |
| Trusting `MQELOffline_cpp` for *behavior* | it hardcodes EndAttack + skips the tutorial — authoritative for **contract shapes**, misleading for behavior/values. |
| The canned `Ness199X` account body / "account is frozen, nothing syncs" model | fabrication / wrong model — state syncs via notifications; the account is GENERATED from real state. |

## Related
- Feature docs: [wallet.md](wallet.md) · [combat-rewards.md](combat-rewards.md) · [hero-progression.md](hero-progression.md) · [objectives.md](objectives.md) · [notifications.md](notifications.md)
- [attack-service.md](../../code-analysis/rest-api/attack-service.md) — authoritative attack/notification shapes
- [in-session-state-sync.md](../../code-analysis/decompiled/account/in-session-state-sync.md) — the native getSpells gate
- memory: [[project-wallet-capacity-type47]], [[project-endattack-scoring]], [[project-skilltree-grey-clientside]], [[project-tybalts-farm-next-step]]
</content>
