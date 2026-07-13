# Persistence — the durable account store

> **Status:** implemented · **Server:** gameserver · **Updated:** 2026-07-07

## Purpose

Every gameplay value the player earns — hero, gear, gold/life-force, level, unlocked skill, objectives,
completed tutorial checkpoints — must survive a server restart and an `Alt-F4`/reconnect, or testing deeper
content would mean re-onboarding on every boot. So account state lives in a **relational database**, reached
only through a narrow **repository black box** ([`IAccountRepository`](../../MQELServer/src/MQEL.Core/Persistence/IAccountRepository.cs#L11)):
callers mutate a tracked object graph and ask it to save; they never see EF Core or SQL. Provider is **SQLite
today, PostgreSQL later** — swapping it touches one registration, not a single caller.

A second seam, [`IAccountResolver`](../../MQELServer/src/MQEL.Core/Persistence/IAccountResolver.cs#L11), decides
*which* account a request belongs to — deliberately **pinned to one fixed dev account** for now
([`DevAccountResolver`](../../MQELServer/src/MQEL.Data/Persistence/DevAccountResolver.cs#L11)); real
`SteamID → account` routing drops in here when multi-user lands.

## Key code

| Type / file | Role |
|-------------|------|
| [`IAccountRepository`](../../MQELServer/src/MQEL.Core/Persistence/IAccountRepository.cs#L11) | the storage black box — `GetAsync`/`GetBySteamIdAsync`/`SaveAsync`/`ExistsAsync` |
| [`EfAccountRepository`](../../MQELServer/src/MQEL.Data/Persistence/EfAccountRepository.cs#L8) | EF Core impl; [`WithGraph`](../../MQELServer/src/MQEL.Data/Persistence/EfAccountRepository.cs#L14) eager-loads the whole aggregate `AsSplitQuery`; `SaveAsync` decides insert vs. flush vs. skip |
| [`IAccountResolver`](../../MQELServer/src/MQEL.Core/Persistence/IAccountResolver.cs#L11) / [`DevAccountResolver`](../../MQELServer/src/MQEL.Data/Persistence/DevAccountResolver.cs#L11) | identity seam → account id (currently always the dev account) |
| [`GameDbContext`](../../MQELServer/src/MQEL.Data/Persistence/GameDbContext.cs#L10) | EF model + Fluent mapping |
| [`StorageOptions`](../../MQELServer/src/MQEL.Data/Persistence/StorageOptions.cs#L18) | `DefaultAccountId` (`3123971`), connection string, provider |
| [`AccountMapper`](../../MQELServer/src/MQEL.Gameserver/AccountMapper.cs#L14) | bridges the EF [`Account`](../../MQELServer/src/MQEL.Core/Model/Account.cs#L7) entity ↔ the in-session [`AccountState`](../../MQELServer/src/MQEL.Gameserver/AccountState.cs): `ToAccountState` (load) / `ApplyTo` (save, merge-by-key) |
| `GateFor` (`GameEndpoints.cs`) | per-account `SemaphoreSlim` — serialises concurrent same-account requests |

## How it works

A request that touches account state runs:

1. **Resolve** the account id (`IAccountResolver`; today always `DefaultAccountId`).
2. **Lock** that id ([`GateFor`](../../MQELServer/src/MQEL.Gameserver/Program.cs#L146)) so two concurrent requests
   can't interleave a read-modify-write.
3. **Load** the tracked graph with `GetAsync` → `WithGraph` pulls wallets, heroes (+gear/spells/consumables/
   inventory), account inventory, castle (rooms→buildings), completed assignments, **objectives** and
   **crafting materials** in one `AsSplitQuery`.
4. **Project** to [`AccountState`](../../MQELServer/src/MQEL.Gameserver/AccountState.cs) with `ToAccountState`, run the
   handler, write back with `ApplyTo`. `ApplyTo` **merges by key** per child collection (find-or-add,
   remove-missing) rather than clear-and-reinsert, so EF emits tight `UPDATE`s and never trips PK collisions
   on a detached graph.
5. **Save** via `SaveAsync`, which branches: **detached** (fresh first-login) → insert the graph; **tracked,
   no changes** (read-only) → skip the write; **tracked with changes** → stamp `UpdatedUtc` + flush.

> ⚠️ When you add a new child entity to the aggregate, add it to **both** `WithGraph` (repo) **and** `SnapGraph`
> ([save-states.md](save-states.md)) — `ApplyTo`'s merge-by-key can't delete rows it didn't load. (This bit us
> when `Objectives`/`CraftingMaterials` were added to `SnapGraph` late.)

## REST / wire
No game-facing wire of its own — it backs the account contracts (chiefly `GetAccountInformation`), whose JSON
is owned by [`../code-analysis/`](../../code-analysis/README.md) (e.g.
[account-load](../../code-analysis/decompiled/account/account-load.md)). It only decides *where state lives and how
it round-trips*. The admin/snapshot `/api/*` endpoints are server-internal — see [admin-dashboard.md](admin-dashboard.md).

## Data / persistence
- **Store:** SQLite file `mqel.db` (gameserver working dir = the project dir at `dotnet run`), EF Core 8,
  migration-managed (`MQEL.Data/Migrations`, applied on startup; design-time
  [`GameDbContextFactory`](../../MQELServer/src/MQEL.Data/Persistence/GameDbContextFactory.cs#L10) backs `dotnet ef`).
- **Shape:** relational — `Accounts` root + child tables (`Wallets`, `Heroes` +Gear/Spells/Consumables/Inventory,
  `Inventory`, `Castle`→`Rooms`→`Buildings`, `CompletedAssignments`, `Objectives`, `CraftingMaterials`).
  Account ids are **assigned, not DB-generated**. Save-state templates share the tables in **negative-id space**.

## How to …
### How to reset the dev account to a clean first-login
Use the dashboard's **Reset to starter ↺** button (or `POST /api/accounts/{id}/reset`) — see
[admin-dashboard.md](admin-dashboard.md#account-reset). Relaunch the game afterwards.

### How to swap SQLite → PostgreSQL
Change the provider + connection string in [`StorageOptions`](../../MQELServer/src/MQEL.Data/Persistence/StorageOptions.cs#L18)
and the EF provider registration; regenerate migrations. No caller changes — they only see `IAccountRepository`.

## Design notes & gaps
- 🟡 **Identity routing is stubbed.** `DevAccountResolver` always returns `DefaultAccountId`, so every request
  targets the one dev account. Real `SteamID → account` is the next infra piece (cross-account visibility +
  raiding); the repo already exposes `GetBySteamIdAsync` — only the resolver needs to read the request identity.
- The combat **audit substrate** (EndAttack replay blob) is a separate, still-stubbed seam ([verification.md](verification.md)).
- ⚠️ **Migration/schema drift can silently break login.** Hit 2026-07-07: two migrations (`CastleBuildingJson`,
  `PublishedCastleJson`) had been applied to the live dev DB, then later reverted from source **without a
  down-migration**, leaving `__EFMigrationsHistory` referencing migrations that no longer exist in code and the
  `Castles`/`CastleRooms`/`CastleBuildings` tables missing — every account load 500'd (`no such table: Castles`).
  Repaired by hand (recreated the 3 tables to match the `Initial` migration's schema, removed the 2 orphaned
  history rows) — **not** a proper EF migration, since the code no longer had one to apply. If a migration is
  ever reverted from source after being applied to a live DB, either write a real down-migration or delete
  `mqel.db` and let `Initial` recreate it from scratch — don't let history and schema diverge silently.

## Related
- [admin-dashboard.md](admin-dashboard.md) — the UI + `/api/*` (incl. account reset) on top of this store
- [save-states.md](save-states.md) — named snapshots (the same mapper round-trip)
- [boot-flow.md](../boot/boot-flow.md) — where account load/create sits in the launch sequence
- [code-analysis account-load](../../code-analysis/decompiled/account/account-load.md) — the `GetAccountInformation` wire shape this state feeds
</content>
