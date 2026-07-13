# Admin dashboard — the server control UI

> **Status:** implemented · **Server:** gameserver (`/` + `/api/*`) · **Updated:** 2026-06-29

## Purpose

A dependency-free, game-themed web UI at **`https://localhost:8080/`** for inspecting and steering the dev
server during development: live service/account status, an account editor, a tailing log, **one-click account
reset**, and [save-states](save-states.md). It exists so testing deeper content doesn't require hand-editing
the DB or re-onboarding on every change. It is **server-internal**, not part of the game protocol.

## Key code

| Type / file | Role |
|-------------|------|
| [`wwwroot/index.html`](../../MQELServer/src/MQEL.Gameserver/wwwroot/index.html) | the dashboard markup (Services / Accounts / Save-States / Log) |
| [`wwwroot/ui/app.js`](../../MQELServer/src/MQEL.Gameserver/wwwroot/ui/app.js) | the UI logic (poll, account editor, reset, snapshots) |
| [`Program.cs` UI middleware](../../MQELServer/src/MQEL.Gameserver/Program.cs) | serves `wwwroot` via explicit `SendFileAsync` (ahead of the game catch-all) |
| [`Program.cs` `/api/*`](../../MQELServer/src/MQEL.Gameserver/Program.cs) | the admin API the dashboard calls |
| [`AdminApi.cs`](../../MQELServer/src/MQEL.Gameserver/AdminApi.cs) | request DTOs (`AccountEdit`, `SnapshotReq`) |

## How it works

The dashboard polls `/api/*` every 3 s. Auth = an optional **`X-Admin-Token`** header vs config `Admin:Token`
(empty ⇒ open, for local dev).

| Endpoint | Method | Does |
|---|---|---|
| `/api/status` | GET | server/db summary (accounts, snapshots, SKU/template counts) |
| `/api/accounts` | GET | account list (id, name, hero) |
| `/api/accounts/{id}` | GET | full account detail (gold, life-force, hero level/xp, gear, spells, completed assignments) |
| `/api/accounts/{id}` | POST | edit (display name, gold, life-force, hero level/xp, completed assignments) |
| **`/api/accounts/{id}/reset`** | POST | **reset to a fresh first-run starter** (see below) |
| `/api/logs` | GET | tail of `d:\mqel-trace.log` |
| `/api/snapshots …` | — | save-states — see [save-states.md](save-states.md) |

### Account reset
A **Reset to starter ↺** button (in the account editor and as a row action) calls `POST /api/accounts/{id}/reset`.
It rebuilds the account to the exact state a brand-new account boots with — no hero/gear/spells, no
objectives/materials, no completed assignments, empty inbox, 1000 gold / 0 life-force, castle unclaimed — so the
FTUE can be retested from scratch without a manual DB wipe. It **reuses the [save-state](save-states.md) restore
path** (`AccountMapper.ApplyTo` over the graph), only the source is a freshly-built `NewFirstRun(id)` instead of
a template, and it drops the transient combat scratch. **Relaunch the game after resetting.**

## REST / wire
None — this is server-internal admin, deliberately **not** in [`code-analysis/`](../../code-analysis/README.md)
(which owns the game ↔ server protocol). The account state these endpoints read/write lives in
[persistence.md](persistence.md).

## Design notes & gaps
- The reset/snapshot endpoints go through `/api/*`, which (like the game requests) is fine while the game is
  idle at the lobby; relaunch the game afterwards to pick up the new state.
- The account editor doesn't yet expose objectives/crafting-materials (only the common fields).

## Related
- [save-states.md](save-states.md) — named snapshots (capture/restore/delete)
- [persistence.md](persistence.md) — the account store these endpoints operate on
</content>
