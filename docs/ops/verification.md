# Verification (anti-cheat) — the seam, built and stubbed

> **Status:** planned (seam implemented, returns valid=true; no checking yet) · **Server:** gameserver ·
> **Updated:** 2026-06-20

## Purpose

Raid combat in MQEL is **simulated on the client, deterministically from a server-issued
`AttackRandomSeed`** — so the server does not re-run combat. It is *not* blind trust either: the server
owns the economy, and determinism + the seed + a stored replay make any attack re-verifiable. Why this is
the standard (and safe) async-raid design, with evidence:
[FINDINGS §7](../../FINDINGS.md#7-combat--sync-model--the-client-simulates-the-server-doesnt).

This subsystem is the **clean seam** for that verification, present from day 1 so anti-cheat can be added
later with **no protocol or schema change** — only the compute is deferred, never the audit data.

## Key code

| Type | Role |
|------|------|
| [`AuditBundle`](../../MQELServer/src/MQEL.Core/Verification/AuditBundle.cs) | the inputs to verify a completed attack: `AttackId`, accounts, `AttackRandomSeed`, and (opaque until captured) castle snapshot / attacker loadout / claimed result / replay |
| [`IVerificationService`](../../MQELServer/src/MQEL.Core/Verification/IVerificationService.cs) | `VerifyAsync(AuditBundle) → VerificationVerdict` |
| [`VerificationVerdict`](../../MQELServer/src/MQEL.Core/Verification/VerificationVerdict.cs) | `Valid` + reason |
| [`StubVerificationService`](../../MQELServer/src/MQEL.Verification/StubVerificationService.cs) | today: logs the bundle, returns `valid=true` |

Wired in [`Program.cs#L9`](../../MQELServer/src/MQEL.Gameserver/Program.cs#L9).

## How it works

The verifier is a **pure consumer of data the gameserver already has to store** (the seed it issued, the
result it records, the replay, the castle snapshot). So it is inherently post-match and adds no
protocol/schema later — that's what makes deferring it correct rather than a shortcut.

When real: a future implementation re-simulates the recorded replay from the seed (needs the engine's
exact combat math → the native game code) and compares to the claimed result. Tiers we can choose:
trust-client · cheap invariant checks (we own the castle state, so loot bounds are easy) · full replay
re-simulation (airtight).

## Design notes & gaps

- **The audit *substrate* is not deferred.** When the gameserver implements attack-result submission, it
  must persist seed + result + replay + castle snapshot even while `StubVerificationService` returns true.
  Omitting that capture would be the one real shortcut (you could never verify retroactively).
- The attack result/replay submission endpoint is **engine-native, not in the client JS** — reconstruct it
  from captured POSTs and/or the decompiled game code, not by guessing.

## Related
- [boot-flow.md](../boot/boot-flow.md) — the server overview · [FINDINGS §7](../../FINDINGS.md#7-combat--sync-model--the-client-simulates-the-server-doesnt)
