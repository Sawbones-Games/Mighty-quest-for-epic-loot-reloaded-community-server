# MQELServer — the private server (.NET 8)

The reimplemented backend for The Mighty Quest for Epic Loot. Modular monolith + a stubbed verification
service, headless, Docker-deployable (target: a TrueNAS host), with a Blazor admin UI to come.

See [`../FINDINGS.md`](../FINDINGS.md) for the reverse-engineering writeup and
[`../docs/STATUS.md`](../docs/STATUS.md) for the live status board.

## Layout

```
src/
  MQEL.Core/          domain models, repository interfaces, the verification contract (AuditBundle, IVerificationService)
  MQEL.Data/          EF Core DbContext + repo implementations — provider-swappable (SQLite now, Postgres later).
                      Schema DEFERRED until First Contact captures real traffic (no guessed schema).
  MQEL.Verification/  StubVerificationService — receives the full AuditBundle, returns {valid:true}. Real
                      replay re-simulation drops in later behind the same interface (no caller/schema change).
  MQEL.Gameserver/    ASP.NET host. Right now: the Step-1 capture rig.
  MQEL.AdminUI/       Blazor admin panel — added once there is state to manage.
config/               PublicLauncherSettings.private.json — the redirected launcher config
Dockerfile, docker-compose.yml
```

## Status: Step 1 — First Contact / capture

The host is a **catch-all request logger**. It writes every request the real client makes to
`captures/requests.jsonl` (method, path, query, headers, body) and returns `200`. The goal is to capture
the real **launch → login → lobby** traffic — the ground truth we build the actual endpoints against.
Several endpoints (e.g. the attack result/replay submission) are engine-native and not visible in the
client's JS, so capturing the wire is the only correct way to spec them.

The **verification seam is already in place** ([`IVerificationService`](src/MQEL.Core/Verification/IVerificationService.cs),
stubbed) so anti-cheat can be bolted on later with no protocol/schema change — but the audit *substrate*
(persisting seed/result/replay/castle snapshot) is built from day 1, never deferred.

## Run it

```sh
dotnet run --project src/MQEL.Gameserver        # listens on http://0.0.0.0:8080
# or containerised:
docker compose up --build
```

## Point the client at it

1. Copy [`config/PublicLauncherSettings.private.json`](config/PublicLauncherSettings.private.json) over the
   game's `Launcher/PublicLauncherSettings.json`. It defaults to `localhost:8080` (server on the same PC as
   the game); change `localhost` to the server's IP if it runs elsewhere (e.g. the TrueNAS box).
2. No hosts file or DNS — this is the game's own config-override mechanism (the `*.Template.json` proves the
   URLs are configurable). ⚠️ The Opal JSON parser is **strict**: keep the exact key set, **no comment/extra
   fields, ASCII only** — an unknown member or a non-ASCII char (e.g. `—`) makes the launcher fail with
   `Error 80000003: Cannot read the configuration file`.
3. Launch. Watch `captures/requests.jsonl` fill up. The launcher will also generate
   `GameData/Config/Synergy/Game.ini` (the game-side endpoint) — grab that file to learn the game-side
   repoint.

> `captures/` is gitignored — it can contain Steam auth tickets. Never commit it.
