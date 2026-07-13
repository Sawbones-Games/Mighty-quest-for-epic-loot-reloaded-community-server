# Boot flow — what the server implements today

> **Status:** implemented — boots the real client fully (launcher → game → onboarding → FTUE tutorial) ·
> **Server:** launcher + gameserver · **Updated:** 2026-06-23

## Purpose

This is the spine of everything built so far: the exact request sequence the **real game client** makes
from Steam launch to the in-game session, and what [`MQELServer`](../../MQELServer/) returns at each step to
keep it advancing. The whole flow is one dispatcher in
[`Program.cs`](../../MQELServer/src/MQEL.Gameserver/Program.cs) — a capture middleware plus per-endpoint
handlers. It was built **empirically**: point the client at the server, read the client's *own* parse
errors (the [oracle](#how-we-know-the-sources)), implement the contract it names, repeat.

The wire/contract details (field names, why each shape) live in `code-analysis/` and are **linked, not
restated** here ([conventions](../CONVENTIONS.md)).

## How it connects (redirection)

No hosts file or DNS — the client's own config is repointed (same mechanism as the Dead Island project).
We ship [`config/PublicLauncherSettings.private.json`](../../MQELServer/config/PublicLauncherSettings.private.json)
over the game's `Launcher/PublicLauncherSettings.json` with the URLs set to our host. Details + the
strict-parser caveat: [FINDINGS §4](../../FINDINGS.md#4-the-backend-we-must-rebuild).

## The sequence (and our handler for each gate)

Two transports, both `<Service>.hqs/<Method>` RPC with .NET-contract (`$type`) JSON. Launcher contracts =
`Contracts.Common`/`DistributionService.Contracts`; game contracts = `HyperQuest.GameServer.Contracts`.

| # | Client request | Boot task | Our handler | Wire source |
|---|----------------|-----------|-------------|-------------|
| 1 | `GET …/launcher/` | TaskCheckMaintenance | `200` (catch-all, [Program.cs#L142](../../MQELServer/src/MQEL.Gameserver/Program.cs#L142)) | [launcher-boot](../../code-analysis/launcher-boot.md) |
| 2 | `GET …/static/empty.png` | TaskDownloadBackground | `200` | — |
| 3 | `GET PatcherService.hqs/GetRMLauncherVersion` | TaskCheckLauncherVersion | `{"VersionName":…}` → `RMLauncherPatch` ([L102](../../MQELServer/src/MQEL.Gameserver/Program.cs#L102)) | [launcher-boot](../../code-analysis/launcher-boot.md) |
| 4 | `GET …/launcher/load/?steamID&ticket` | login | login iframe page: cookies `t`/`hyperquest_launcher_session`/`email` + `window.userIsLoggedIn` ([L114](../../MQELServer/src/MQEL.Gameserver/Program.cs#L114)) | [launcher-boot §Login](../../code-analysis/launcher-boot.md#login-the-launcherload-page) |
| 5 | `POST PatcherService.hqs/GetRMLauncherAndPackagesVersion` | package check | nested `{RMLauncherPatch, RMServerPackagesVersion{…}}` ([L68](../../MQELServer/src/MQEL.Gameserver/Program.cs#L68)) | [launcher-boot §packages](../../code-analysis/launcher-boot.md#package-version-gate-checking-game-packages-version--reached) |
| — | *launcher launches `MightyQuest.exe`* | — | — | — |
| 6 | `POST ServerCommandService.hqs/SendCommands` | game command loop | `{"commands":[]}` ([L131](../../MQELServer/src/MQEL.Gameserver/Program.cs#L131)) | [gameserver-commands](../../code-analysis/rest-api/gameserver-commands.md) |
| 7 | `POST UtilityService.hqs/KeepAlive` | keepalive | `{}` ([L135](../../MQELServer/src/MQEL.Gameserver/Program.cs#L135)) | — |
| 8 | `POST TrackingService.hqs/Track` | telemetry | `{}` ([L139](../../MQELServer/src/MQEL.Gameserver/Program.cs#L139)) | — |

Everything else falls through to `200` so the client keeps revealing the flow. The game sends the
LoginToken from step 4 as an HTTP header **`t`** on its gameserver calls.

## Past boot — into the game (RESOLVED)

> ✅ The old **"Waiting for AccountServerController"** stall was **not** a missing server-command trigger —
> that was teardown noise. The real gates were: the client requires an **`https`** server URL
> (NetworkBootManager rejects non-`https`) and rejects our self-signed cert, and the session token must
> arrive on the game's **`-token` command-line arg** (not via launcher Argo IPC). With HTTPS served + the
> cert-trust WriteProcessMemory patch + `-token`, the game boots fully. See
> [progression-loop.md](../gameplay/progression-loop.md), [STATUS.md](../STATUS.md).

After step 6 the game establishes its session and issues `AccountService.hqs/GetAccountInformation`
(handler in [Program.cs](../../MQELServer/src/MQEL.Gameserver/Program.cs)); the GAI body is **generated from a
stateful `AccountState`**, not canned. From there onboarding (name → starter castle → hero) and the full
FTUE tutorial play through — gather → reward → level-up → skill unlock, all documented in
[progression-loop.md](../gameplay/progression-loop.md). Note `{"commands":[]}` (step 6) is the **correct** SendCommands
response, not a stopgap.

## Design notes & gaps

- The **launcher** handlers (steps 1–5) are minimal "advance the client" stubs; the package-version field
  names/values (step 5) are reconstructed from binary symbols and partly inferred — flagged in code. The
  **gameserver** handlers (GetAccountInformation, StartAttack/EndAttack, the SendCommands notifications) now
  implement real account/progression logic — see [progression-loop.md](../gameplay/progression-loop.md).
- **Deferred (correctly, with the seam built):** Steam ticket validation + persisting the LoginToken so the
  gameserver can authenticate the game's `t` header against it ([Program.cs#L112](../../MQELServer/src/MQEL.Gameserver/Program.cs#L112)).
- **Capture rig** ([Program.cs#L25](../../MQELServer/src/MQEL.Gameserver/Program.cs#L25)) logs every request to
  `captures/requests.jsonl` — keep it; it's how we learn each next step.

## How we know — the sources

Each step was settled by triangulating these (all reproducible):

| Source | Where | What it proves |
|--------|-------|----------------|
| Our capture | `captures/requests.jsonl` (gitignored — may hold Steam tickets) | what the client *sent* |
| Launcher log | `Launcher/ZounaLog.Txt` | the launcher's JSON parse errors → the contract+field it expects |
| Game log | `GameData/Bin/MQLog.txt`, `GameData/Bin/NetworkLog.Txt` | the game's parse errors + call sequence |
| Crash bundle | `CrashReport/*.breport` (text + minidump) | which task failed + bundled logs |
| Launcher binary | `Launcher/PublicLauncher.exe` strings | launcher contract names (it's native, not packed) |
| Game memory dump | `D:\mq.dmp` (procdump `-ma`) | the game contract catalog ([gameserver-catalog](../../code-analysis/gameserver-catalog.txt)) — the exe is UBX-packed so this is the only static view |

## Related
- [verification.md](../ops/verification.md) — the anti-cheat seam (built, stubbed)
- [STATUS.md](../STATUS.md) · [../FINDINGS.md](../../FINDINGS.md) — the RE writeup
