# Ops — server operations

> How account state is stored and how we drive/inspect the dev server. Part of the [docs set](../README.md) ·
> conventions: [../CONVENTIONS.md](../CONVENTIONS.md).

- [persistence.md](persistence.md) — the durable account store: the `IAccountRepository` black box
  (EF Core/SQLite, Postgres-ready), the identity seam, and the `AccountState` round-trip.
- [admin-dashboard.md](admin-dashboard.md) — the server control UI + `/api/*` (status, account editor,
  tailing log, **one-click account reset**).
- [save-states.md](save-states.md) — named account snapshots (capture / restore / delete) for replay-free
  checkpoint testing.
- [verification.md](verification.md) — the anti-cheat / combat-audit seam (built, stubbed).
</content>
