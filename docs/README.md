# docs — how the server works

Implementation docs for the MQEL private server (the [`MQELServer/`](../MQELServer/) codebase). They
describe **what the server code does and why** — the boot spine it answers, the gameplay/reward loop it
runs, the content it serves, and how to operate it.

> **Note.** These docs were written alongside a separate, private reverse-engineering workspace and
> describe the server itself; a few references to that raw analysis are not included here.

## Index (by feature)

**[Boot & spine](boot/README.md)**
- **[boot-flow.md](boot/boot-flow.md)** — the full client launch → game sequence and the handler the
  server returns at each gate. Start here to understand what the server does.

**[Gameplay loop](gameplay/README.md)** — overview + one doc per feature
- **[progression-loop.md](gameplay/progression-loop.md)** — the map of the reward/progression loop.
- **[notifications.md](gameplay/notifications.md)** — the in-session state-sync mechanism (the
  `Notifications` envelope; types 24/43/47/111/14/17).
- **[wallet.md](gameplay/wallet.md)** — gold / life-force balances + storage capacity (type-47).
- **[combat-rewards.md](gameplay/combat-rewards.md)** — StartAttack loot tables → EndAttack scoring.
- **[hero-progression.md](gameplay/hero-progression.md)** — XP curve, level-up, skill unlock.
- **[objectives.md](gameplay/objectives.md)** — campaign objective tracking + completion (type-14).

**[Content & tutorial](content/README.md)**
- **[tutorial-steps.md](content/tutorial-steps.md)** — how the FTUE steps are traced & implemented.
- **[castles.md](content/castles.md)** — serving a castle to attack: required fields, auto-loot.
- **[castle-building.md](content/castle-building.md)** — the renovation/build system (the current frontier).

**[Persistence & ops](ops/README.md)**
- **[persistence.md](ops/persistence.md)** — the durable account store (`IAccountRepository`, EF/SQLite).
- **[admin-dashboard.md](ops/admin-dashboard.md)** — the server control UI + `/api/*`.
- **[save-states.md](ops/save-states.md)** — named account snapshots (capture/restore/delete).
- **[verification.md](ops/verification.md)** — the anti-cheat seam (built, stubbed).

## Related

- [../README.md](../README.md) — project overview and quick start.
</content>
</invoke>
