# Notifications — the in-session state-sync mechanism

> **Status:** implemented · **Server:** gameserver · **Updated:** 2026-07-07

## Purpose

The client does **not** re-fetch the account ([`GetAccountInformation`](../../code-analysis/decompiled/account/account-load.md))
after every change. Instead, the server pushes **notifications** in the response to the request that caused
the change, and the client **applies each one to its live in-memory view-models**. This is the single most
important mechanism in the metagame server: almost every "value is stuck / didn't update" bug was a
**missing notification**, not frozen client state. This doc is the one place that explains the mechanism and
catalogs the notification types we send; **per-feature docs link here instead of re-explaining it.**

> The hard-won model: the client's view-models are frozen at boot **only for the parts the server never
> updates**. Everything else syncs in-session via notifications. ([progression-loop.md](progression-loop.md))

## Key code

| Type / file | Role |
|-------------|------|
| [`Notifications`](../../MQELServer/src/MQEL.Gameserver/Notifications.cs) | single source of every `$type`/`NotificationType` pair — build new notifications here, exactly once |
| [`GameEndpoints.cs` EndAttack](../../MQELServer/src/MQEL.Gameserver/GameEndpoints.cs) | builds the `Notifications` array for the combat-reward sync (type-24/43/111) |
| [`MissionManager.OnEndAttack`](../../MQELServer/src/MQEL.Gameserver/MissionManager.cs) | objective completion (type-14) + reward (type-111/24/43) + chain-unlock (type-17) — data-driven, scores every active mission |
| [`responses/castle-bought-notifications.json`](../../MQELServer/src/MQEL.Gameserver/responses/castle-bought-notifications.json) | the type-47 capacity notifications on castle-claim |
| [`GameEndpoints.cs` SendCommands](../../MQELServer/src/MQEL.Gameserver/GameEndpoints.cs) | the command channel (the other half — client→server commands the server acks/records) |

## How it works

1. A request that mutates account state (e.g. `EndAttack`, the `CastleBought` `SendCommands`) returns an
   envelope: **`{"Result": {…}, "Notifications": [ {…}, … ]}`** (either key may be present alone).
2. Each notification carries a polymorphic **`$type`** (the `…Contracts.<Name>Notification` discriminator)
   **and** an integer **`NotificationType`**. The client routes on these and applies the change to the
   relevant view-model (wallet, hero, inbox, objective, …).
3. **Amounts are DELTAS, not totals** for the additive notifications (wallet/xp): the client ADDs them to its
   current value (proven: 1000 + 33 = 1033). Send the real gained delta, never the new total.
4. An **unknown `NotificationType` is silently ignored** — this is a sharp edge: a wrong integer looks like
   "nothing happened." (Cost us a day: `ObjectiveCompletedNotification` sent as `112` instead of `14` was
   dropped — see [objectives.md](objectives.md).)

### Notification types we send

| Type | `$type` | Carries | Used by | Shape |
|---|---|---|---|---|
| **24** | `WalletUpdatedNotification` | `Amounts[{CurrencyType,Amount(delta)}]` | [wallet.md](wallet.md), [combat-rewards.md](combat-rewards.md) | [attack-service §3.2](../../code-analysis/rest-api/attack-service.md) |
| **43** | `HeroXpChangedNotification` | `XpAdded, TotalXp, Level, LevelChanged` | [hero-progression.md](hero-progression.md) | [attack-service §3.2](../../code-analysis/rest-api/attack-service.md) |
| **47** | `WalletCapacityUpdatedNotification` | `CurrencyType, Amount(=cap)` | [wallet.md](wallet.md) | [attack-service §3.2](../../code-analysis/rest-api/attack-service.md) |
| **111** | `InboxItemsAddedNotification` | `InboxItems[{$type,HeroItem,ItemType,ObjectId}]` | [combat-rewards.md](combat-rewards.md), [objectives.md](objectives.md) | [attack-service §3.2](../../code-analysis/rest-api/attack-service.md) |
| **14** | `ObjectiveCompletedNotification` | `ObjectiveId` | [objectives.md](objectives.md) | [objectives wire §3.3](../../code-analysis/rest-api/objectives.md) |
| **17** | `ObjectiveUnlockedNotification` | `AccountObjective{ObjectiveId,Status,LastStatusDate}` | [objectives.md](objectives.md) | [objectives wire §3.3](../../code-analysis/rest-api/objectives.md) |

> The authoritative JSON for each shape lives in `code-analysis/` (linked above). This table is the **index**
> of which notification drives which feature — it never restates the JSON.

## Design notes & gaps

- **The envelope is NOT `{"commands":[]}`.** That's the `SendCommands` *command* channel; the notification
  envelope uses `Notifications`. Returning the wrong wrapper crashes the client
  ([gameserver-commands](../../code-analysis/rest-api/gameserver-commands.md)).
- Notification-type integers are **not** catalogued in one place in the binary (the dumped
  `gameserver-catalog.txt` is incomplete for notifications). Ground truth for the rarer ones is the
  `MQELOffline_cpp` real captures — confirm a type empirically (the client ignores wrong ones silently).
- `NotificationType:14`/`17` (objectives) were recovered from the reference capture, not the catalog. Type-14
  has been in active use since objective 300; type-17 was documented from the capture early on but only put into
  active use 2026-07-07, when the assumed assignment-VM-driven unlock for objective 301 turned out not to fire
  in our reimplementation — see [objectives.md](objectives.md) "Chaining".

## Related
- [progression-loop.md](progression-loop.md) — the loop these notifications drive, + the dead-ends
- [code-analysis/rest-api/attack-service.md](../../code-analysis/rest-api/attack-service.md) — authoritative notification JSON
- [code-analysis/rest-api/response-contracts.md](../../code-analysis/rest-api/response-contracts.md) — the `ServerCommandType` enum (the command channel)
</content>
