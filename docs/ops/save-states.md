# Save-states — named account snapshots

> **Status:** implemented · **Server:** gameserver (`/api/snapshots`) · **Updated:** 2026-06-29

## Purpose

Capture an account's full graph at any tutorial step under a name, then restore it later to jump straight back
in — no replaying the FTUE to reach a checkpoint. Built on the [admin dashboard](admin-dashboard.md) and the
same [persistence](persistence.md) mapper the game uses, so a snapshot captures exactly what a real login
restores (no separate serialization format to drift).

## Key code

| Type / file | Role |
|-------------|------|
| [`Program.cs` snapshot endpoints](../../MQELServer/src/MQEL.Gameserver/Program.cs) | `/api/snapshots` GET/POST + `/{name}/restore` + DELETE |
| [`Program.cs` `SnapGraph`](../../MQELServer/src/MQEL.Gameserver/Program.cs) | the eager-load graph (Wallets, Heroes+children, Inventory, CompletedAssignments, **Objectives**, **CraftingMaterials**) |
| [`AccountMapper`](../../MQELServer/src/MQEL.Gameserver/AccountMapper.cs) | `ToAccountState` / `ApplyTo` — the capture/restore round-trip |

## How it works

- A **snapshot is an `Account` row** with `IsTemplate = true`, a `SnapshotName`, and a synthetic **negative
  AccountId** (so it can never collide with a live, positive account).
- **Capture** clones the live dev account's graph into a template via `ToAccountState` → `ApplyTo`.
- **Restore** clones the template back onto the live account via the same path; relaunch the game to pick it up.
- Because capture and restore both go through `AccountMapper`, they cover whatever the mapper covers — and the
  [account reset](admin-dashboard.md#account-reset) reuses the restore path with a `NewFirstRun` source.

> ⚠️ `SnapGraph` MUST `.Include` every child collection (it was missing `Objectives`/`CraftingMaterials` when
> those were added — `ApplyTo`'s merge-by-key can't delete rows it can't see, so snapshots silently dropped
> them). When you add a new child entity, add it to `SnapGraph` **and** the repo's `WithGraph`.

## REST / wire
None (server-internal admin). The DTOs are in [`AdminApi.cs`](../../MQELServer/src/MQEL.Gameserver/AdminApi.cs).
- `POST /api/snapshots {"name":"after-witch"}` · `POST /api/snapshots/after-witch/restore` · `DELETE /api/snapshots/after-witch`.

## Data / persistence
Templates share the live tables; they live in **negative-AccountId space** with `IsTemplate=true`. See
[persistence.md](persistence.md).

## Design notes & gaps
- 🟡 Today the tool snapshots only the **single dev account** (`DefaultAccountId`). 💡 Once multi-user routing
  lands, generalize to "snapshot every account into one timestamped set" for pre-patch wholesale rollback
  (the capture/restore plumbing is already account-agnostic). See [STATUS.md](../STATUS.md) → Ideas.

## Related
- [admin-dashboard.md](admin-dashboard.md) — the UI that drives these (+ the reset button)
- [persistence.md](persistence.md) — the store + the `AccountMapper` round-trip
</content>
