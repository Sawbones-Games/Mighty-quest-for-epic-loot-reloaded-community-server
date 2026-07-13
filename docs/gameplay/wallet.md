# Wallet & currency capacity

> **Status:** implemented (playtest-verified) · **Server:** gameserver · **Updated:** 2026-06-29

## Purpose

The player holds two soft currencies — **gold / in-game-coin (IGC, CurrencyType 2)** and **life-force
(soul shards, CurrencyType 4)** — each with a **storage capacity** (`MaxGold` / `MaxLifeForce`) that caps how
much can be held. Gathering in a dungeon is clamped to the cap, so the cap must be delivered or gathering
rounds to nothing. This doc covers how our server models balances + capacities and keeps them synced.

## Key code

| Type / file | Role |
|-------------|------|
| [`AccountState.InGameCoin` / `.LifeForce`](../../MQELServer/src/MQEL.Gameserver/Account.cs) | balances |
| [`AccountState.InGameCoinStorageCapacity` / `.LifeForceStorageCapacity`](../../MQELServer/src/MQEL.Gameserver/Account.cs) | caps |
| [`AccountState.CreditGold` / `.CreditLifeForce`](../../MQELServer/src/MQEL.Gameserver/Account.cs) | credit, **clamped to capacity**, returns the actual delta |
| [`responses/castle-bought-notifications.json`](../../MQELServer/src/MQEL.Gameserver/responses/castle-bought-notifications.json) | the two type-47 capacity notifications |

## How it works

- **Balances** live on `AccountState` and persist (the `Wallets` table). `CreditGold`/`CreditLifeForce` add
  an amount **clamped to the capacity** and return the *actual* gain — that returned delta is what drives the
  type-24 notification (so the HUD never shows more than was really stored).
- **Capacity is delivered ONLY by the type-47 `WalletCapacityUpdatedNotification`** (`{CurrencyType, Amount=cap}`),
  sent as two notifications (IGC + life-force, `Amount 2000` = storage rank-1 from the spec) riding the
  **`CastleBought`** response when the starter castle is claimed. Without it the cap is 0 and in-dungeon
  gathering clamps to ~0.
- **Balance changes** ride the type-24 `WalletUpdatedNotification` (the gained **delta**) — see
  [combat-rewards.md](combat-rewards.md) for the EndAttack path.

All notification delivery + the "deltas not totals" rule are in [notifications.md](notifications.md); the JSON
shapes are in [attack-service.md §3.2](../../code-analysis/rest-api/attack-service.md).

## Design notes & gaps

- ⛔ **Dead ends (do not retry):** the GAI wallet field `InGameCoinStorageCapacity` is **inert** — the client
  never reads it for the cap; and the `GoldStorage` *building* does **not** drive the cap. The cap is the
  type-47 notification, full stop. (Both proven live: cap stayed 0 until the type-47 was sent.)
- Premium cash (CurrencyType 1) is modeled but unused in the tutorial.

## Related
- [notifications.md](notifications.md) — type-24 / type-47 delivery
- [combat-rewards.md](combat-rewards.md) — where in-dungeon gold/life-force is credited
- [progression-loop.md](progression-loop.md) — the loop overview · memory `project-wallet-capacity-type47`
</content>
