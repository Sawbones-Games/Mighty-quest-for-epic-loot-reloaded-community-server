# docs — how our server is built

Implementation docs for the MQEL private server (the [`MQELServer/`](../MQELServer/) codebase). These
describe **what our code does and why**. They follow [Diátaxis](https://diataxis.fr/) (four doc types) and
[llms.txt](https://llmstxt.org/)-style indexing so they're readable by **humans and AI agents** — see
[CONVENTIONS.md](CONVENTIONS.md).

> **The one rule:** never duplicate. Each fact lives in one place; everything else links to it. Wire
> formats / REST shapes are owned by [`../code-analysis/`](../code-analysis/README.md) — a server doc says
> *which* endpoint it implements and links there for the shape; it never restates the JSON.

**Layout** — feature docs are split into folders by functional area, each with its own index:
[boot/](boot/README.md) · [gameplay/](gameplay/README.md) · [content/](content/README.md) · [ops/](ops/README.md).

## Start here

- **[STATUS.md](STATUS.md)** — what works, what's stubbed, what's next (the live board).
- **[CONVENTIONS.md](CONVENTIONS.md)** — how to write a doc here.
- **[_TEMPLATE.md](_TEMPLATE.md)** — copy this to start a subsystem doc.
- **[POLISH.md](POLISH.md)** — correct-but-not-final values deferred for later tuning.

## Index (by feature)

**[Boot & spine](boot/README.md)**
- **[boot-flow.md](boot/boot-flow.md)** — the full client launch→game sequence and the handler we return at each
  gate. Start here to understand what the server does today.

**[Gameplay loop](gameplay/README.md)** — overview + one doc per feature (mechanism lives in the feature doc; the overview links)
- **[progression-loop.md](gameplay/progression-loop.md)** — the map of the reward/progression loop + the methodology
  and **every dead end**. The hub for the docs below.
- **[notifications.md](gameplay/notifications.md)** — the in-session state-sync mechanism (the `Notifications` envelope;
  types 24/43/47/111/14/17). *Shared concept — other docs link here, never re-explain it.*
- **[wallet.md](gameplay/wallet.md)** — gold/life-force balances + storage capacity (type-47).
- **[combat-rewards.md](gameplay/combat-rewards.md)** — StartAttack loot tables → EndAttack instance-ID scoring.
- **[hero-progression.md](gameplay/hero-progression.md)** — XP curve, level-up, skill unlock (registry-hero-level gate).
- **[objectives.md](gameplay/objectives.md)** — campaign objective tracking + completion (the type-14 mechanism).

**[Content & tutorial](content/README.md)**
- **[tutorial-steps.md](content/tutorial-steps.md)** — how to trace & implement the next FTUE step (the Assignment VM,
  trigger/action vocabulary, anti-patterns). The method.
- **[castles.md](content/castles.md)** — serving a castle to attack: decrypted-spec → served file, required fields,
  auto-loot, the `bin/`-vs-source gotcha.
- **[castle-building.md](content/castle-building.md)** — 🚧 *planned*: the renovation/build system (the next frontier).

**[Persistence & ops](ops/README.md)**
- **[persistence.md](ops/persistence.md)** — the durable account store (`IAccountRepository`, EF/SQLite, the
  `AccountState` round-trip, the identity seam).
- **[admin-dashboard.md](ops/admin-dashboard.md)** — the server control UI + `/api/*` (status, account editor,
  log, **account reset**).
- **[save-states.md](ops/save-states.md)** — named account snapshots (capture/restore/delete).
- **[verification.md](ops/verification.md)** — the anti-cheat seam (built, stubbed).

_The server plays the full FTUE loop end-to-end — boot, onboarding, the attack/reward/notification loop, the
campaign objective system — all built; account state is **durably persisted** with admin + save-state tooling.
The current frontier is **castle building/renovation** (see [castle-building.md](content/castle-building.md)) and the
wider metagame (shop, inventory, PvP). Per-feature docs are added as each system is built._

## Related

- [../README.md](../README.md) — project overview · [../FINDINGS.md](../FINDINGS.md) — the RE writeup
- [../code-analysis/](../code-analysis/README.md) — the protocol (source of truth for the wire)
