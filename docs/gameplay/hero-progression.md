# Hero progression — XP, leveling & skill unlock

> **Status:** implemented (playtest-verified) · **Server:** gameserver · **Updated:** 2026-06-29

## Purpose

A hero accrues XP from combat, **levels up** off the real curve, and at level 2 unlocks its first **skill**.
All of it is *earned* from real play — no force-grants. This doc covers the XP curve, the level-up, and the
subtle skill-unlock gate that took the longest to crack.

## Key code

| Type / file | Role |
|-------------|------|
| [`HeroState.AddXp` / `.LevelForXp` / `XpPerLevel`](../../MQELServer/src/MQEL.Gameserver/Account.cs) | accrue XP, re-derive level from the curve |
| [`Program.cs` EndAttack](../../MQELServer/src/MQEL.Gameserver/Program.cs) | accrues XP, emits the type-43 `HeroXpChanged` with `LevelChanged` |
| [`Program.cs` SendCommands `HeroEquipSpellCommand`](../../MQELServer/src/MQEL.Gameserver/Program.cs) | equips the unlocked skill to an action-bar slot |

## How it works

- **XP curve:** `XpPerLevel` = `GeneralSettings/HEROSETTINGS.XpPerLevel` (global, all classes;
  `[0, 75, 400, 1000, …]` — L2 at 75 total XP). `AddXp` accrues and re-derives the level via `LevelForXp`,
  so the hero levels the instant total XP crosses a threshold — the same point the client levels up mid-dungeon
  (verified: forest 62 XP → still L1; the witch crossed 75 → L2).
- **Level sync:** the EndAttack **type-43 `HeroXpChangedNotification`** carries `XpAdded`, `TotalXp`, `Level`,
  and **`LevelChanged`** ([notifications.md](notifications.md)).
- **Skill unlock — the registry-hero-level gate (the hard one):** the client's `getSpells` gates each
  skill-tree node on the **registry hero's level** (`hero+0x15c`), which the *mid-dungeon* level-up does NOT
  move (that bumps a different, *combat* hero — proven live: combat=2, registry=1). The fix: the type-43 must
  carry **`LevelChanged:true`** when a threshold is crossed; that flag drives a dynamic subscriber to write the
  **registry** hero level → the skill ungates. Earned, not granted. Full trace:
  [in-session-state-sync.md](../../code-analysis/decompiled/account/in-session-state-sync.md).
- **Equipping the skill:** the client sends `HeroEquipSpellCommand {SpellId, SlotIndex}`; we move it into the
  hero's equipped spells.

## Data / persistence
Hero level, XP, gear and equipped spells persist on the `Heroes` aggregate ([persistence.md](../ops/persistence.md)).

## Design notes & gaps
- ⛔ **Dead ends:** force-granting the skill (injecting `UnlockedSpells` / pushing a `SpellUnlockedNotification`)
  is forbidden **and** ineffective — the gate is the *level*, which a spell-unlock doesn't move. Removed.
- Level-up only re-derives the number; it does not yet emit per-level building/feature unlocks.

## Related
- [combat-rewards.md](combat-rewards.md) — where XP is awarded · [notifications.md](notifications.md) — type-43
- [in-session-state-sync.md](../../code-analysis/decompiled/account/in-session-state-sync.md) — the registry-level gate (native)
- [progression-loop.md](progression-loop.md) — the loop overview · memory `project-skilltree-grey-clientside`
</content>
