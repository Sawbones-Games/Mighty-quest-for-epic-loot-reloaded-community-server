# Gameplay — the in-session loop

> What the server does while the player plays: gather → reward → level → skill → quest, kept in sync by
> notifications.

- [progression-loop.md](progression-loop.md) — the **map** of the whole loop + the methodology and every dead
  end. The hub; read first, then the feature docs below.
- [notifications.md](notifications.md) — the in-session state-sync mechanism (the `Notifications` envelope;
  types 24/43/47/111/14/17). **Shared concept** — the other docs here link to it, never re-explain it.
- [wallet.md](wallet.md) — gold / life-force balances + storage capacity (the type-47 cap).
- [combat-rewards.md](combat-rewards.md) — StartAttack loot tables → EndAttack instance-ID reward scoring.
- [hero-progression.md](hero-progression.md) — XP curve, level-up, and the skill-unlock (registry-level) gate.
- [objectives.md](objectives.md) — campaign objective tracking + completion (the type-14 mechanism).
</content>
