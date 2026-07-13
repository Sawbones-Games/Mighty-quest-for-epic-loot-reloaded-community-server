# Reward & inbox item wire shapes

> **Status:** implemented (playtest-verified) · **Server:** gameserver · **Updated:** 2026-07-12

## Purpose

The exact JSON shape for **every item type** the server delivers to the client (raid loot, objective
rewards) and the **order** those notifications must be sent in. Getting a shape or the order wrong causes a
**silent client crash** (a UI binding derefs a null model) — not a JSON parse error, so it's invisible on
the wire. This doc is the authoritative reference; mirror it exactly.

Items reach the client as **`InboxItemsAddedNotification` (NotificationType 111)** — one notification per
item (never batched). Builder: [`Notifications.InboxItemsAdded`](../../MQELServer/src/MQEL.Gameserver/Notifications.cs).
Emitters: raid loot in [`GameEndpoints.cs` EndAttack](../../MQELServer/src/MQEL.Gameserver/GameEndpoints.cs);
objective rewards in [`MissionManager.EmitReward`](../../MQELServer/src/MQEL.Gameserver/MissionManager.cs).

## The enums (client, `game-data/loose/UI/Js/generated/models/`)

| Enum | Values |
|------|--------|
| **`ItemType`** (the InboxItem's `ItemType` field) | `None:0, Hero:1, Spell:2, HeroEquipmentItem:3, Consumable:4, Creature:5, Trap:6, …, CraftingMaterialsPack:18, …` |
| **`HeroEquipmentItemType`** (the item's *sub*-type, from its template) | `Weapons:1, Armor:2, Jewels:3, Costume:4, Pet:5` |
| **`RewardItemType`** (objective-spec reward kinds) | `None:0, CurrencyAmount:1, InventoryItem:2, …, CraftingMaterials:10, …, Xp:13` |

## Inbox item shapes

Each `InboxItemsAddedNotification` wraps ONE inbox item. The inbox item's `$type` names the concrete type;
its `HeroItem` object carries **no `$type` of its own** (typed by the wrapper).

### Consumable / crafting material — `InboxConsumableItem`, `ItemType` 4

```json
{ "$type": "…InboxConsumableItem…", "HeroItem": { "StackCount": 1, "TemplateId": 1003 }, "ItemType": 4, "ObjectId": "<24-hex>" }
```
- `HeroItem` = **`{ StackCount, TemplateId }`** only. One notification **per unit** (StackCount 1 each), so
  a reward of 3× DinoScales = 3 separate type-111 notifications.
- Used for: crafting-material objective rewards (`CraftingMaterialsRewardItem`).

### Equipment (gear) — `InboxHeroEquipmentItem`, `ItemType` 3

```json
{ "$type": "…InboxHeroEquipmentItem…",
  "HeroItem": { "ItemLevel": 1, "ArchetypeId": 2, "PrimaryStatsModifiers": [1,1,1], "IsSellable": true, "TemplateId": 81 },
  "ItemType": 3, "ObjectId": "<24-hex>" }
```
- `HeroItem` = **`{ ItemLevel, ArchetypeId, PrimaryStatsModifiers, IsSellable, TemplateId }`**. No `Type`
  or `DyeTemplateId` (those belong to the *equipped*-slot shape in the GAI, and the client warns
  `Unhandled member [Type] in class [InventoryItem]` if you send them on an inbox item).
- Used for: raid gear loot, and equipment objective rewards **whose template has a real `ArchetypeId`**.

### Named item / pet — `InboxHeroEquipmentItem`, `ItemType` 3, MINIMAL body

```json
{ "$type": "…InboxHeroEquipmentItem…", "HeroItem": { "ItemLevel": 1, "TemplateId": 84349 }, "ItemType": 3, "ObjectId": "<24-hex>" }
```
- A **`HeroNamedItem`** template (pet, `Rarity:"Named"`, `HeroEquipmentItemType:Pet`, e.g. 84349 "Gangster
  Nigel") has **no per-instance archetype/stats** — all its data lives on the template. Deliver it as the
  spec's reward `Item` defines it: **`{ ItemLevel, TemplateId }` only** — NO `ArchetypeId`,
  `PrimaryStatsModifiers`, or `IsSellable`. This matches the account's equipped-Pet shape
  (`FULLACCOUNT.JSON` `Inventory.Heroes[].Equipment.Pet`).
- **Detection:** the spec-DB template has no `ArchetypeId` field. `ItemCatalog` / `MissionManager.EmitReward`
  key off that: archetype present → full gear shape; absent → minimal named shape.

## Objective-spec reward → delivery mapping

The objective's `Reward.RewardItems[]` (`OBJECTIVESETTINGS.JSON`) maps to the above deliveries:

| Spec `$type` (`RewardItemType`) | Delivered as |
|---|---|
| `CraftingMaterialsRewardItem` (10) | type-111 `InboxConsumableItem` per unit (`ItemType` 4) |
| `InventoryItemRewardItem` (2), `Item.$type = HeroEquipmentItem` | type-111 `InboxHeroEquipmentItem` (`ItemType` 3) — gear or minimal-named per template |
| `CurrencyAmountRewardItem` (1) | type-24 `WalletUpdatedNotification` (IGC=2, LifeForce=4, PremiumCash=1) |
| `XpRewardItem` (13) | type-43 `HeroXpChangedNotification` |

## ⚠️ Emit ORDER is load-bearing

**Reward ITEMS (type-111) MUST be emitted BEFORE `ObjectiveCompleted` (type-14).**

The client's objective-complete popup renders the reward items *from the inbox*. If `ObjectiveCompleted`
fires before the items are in the inbox, the popup binds a **null** model → hard crash (native
`FUN_0044e130` `push [esi+10]`, `esi=0`; call stack in `InboxController.cpp`; the null is a
`GameStateManager` "current entity"). This is invisible on the wire — the response is contract-clean.

Correct order for an objective-completing EndAttack response `Notifications[]`:

```
WalletUpdated (24) · [loot items 111] · [reward items 111] · ObjectiveCompleted (14) · ObjectiveUnlocked (17) · HeroXpChanged (43)
```

Root-caused 2026-07-12 by runtime-bisecting the reward notifications one type at a time; see the commit
"fix: objective-completion crash — emit reward items BEFORE ObjectiveCompleted". Both
`MissionManager.OnEndAttack` and `.FastForwardNextObjective` emit rewards before `ObjectiveCompleted`.

## Gotchas / dead ends (do not repeat)

- The pet crash was **NOT** the pet's serialization (4 shape guesses all failed) nor the level-up nor the
  castle list — it was purely the type-14-before-items order. A pet delivered as raw loot on any castle did
  **not** crash; only the objective-reward path (which sent completion first) did.
- The client **gates objective completion on receiving all reward items** — omitting an item STALLS the
  objective (never completes client-side); a wrongly-serialized item CRASHES. So items must be sent AND
  correct AND before type-14.

## See also
- [notifications.md](notifications.md) — NotificationType catalog + shapes
- [combat-rewards.md](combat-rewards.md) — StartAttack loot tables → EndAttack scoring
- [objectives.md](objectives.md) — objective conditions, completion, reward delivery
