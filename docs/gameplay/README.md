# Gameplay — the in-session loop

> What the server does while the player plays: gather → reward → level → skill → quest, kept in sync by
> notifications. Part of the [docs set](../README.md) · conventions: [../CONVENTIONS.md](../CONVENTIONS.md).

- [progression-loop.md](progression-loop.md) — the **map** of the whole loop + the methodology and every dead
  end. The hub; read first, then the feature docs below.
- [notifications.md](notifications.md) — the in-session state-sync mechanism (the `Notifications` envelope;
  types 24/43/47/111/14/17). **Shared concept** — the other docs here link to it, never re-explain it.
- [wallet.md](wallet.md) — gold / life-force balances + storage capacity (the type-47 cap).
- [combat-rewards.md](combat-rewards.md) — StartAttack loot tables → EndAttack instance-ID reward scoring.
- [reward-item-shapes.md](reward-item-shapes.md) — exact wire shape per item type (consumable / gear / named-pet) + the load-bearing emit ORDER (items before ObjectiveCompleted).
- [hero-progression.md](hero-progression.md) — XP curve, level-up, and the skill-unlock (registry-level) gate.
- [objectives.md](objectives.md) — campaign objective tracking + completion (the type-14 mechanism).

> Authoritative JSON shapes for these are in [`../../code-analysis/rest-api/`](../../code-analysis/README.md).
</content>
