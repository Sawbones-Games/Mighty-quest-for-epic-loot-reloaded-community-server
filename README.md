<div align="center">

# The Mighty Quest for Epic Loot — Reloaded

### A private-server reimplementation for a shut-down game.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Discord](https://img.shields.io/badge/Discord-join-5865F2?logo=discord&logoColor=white)](https://discord.gg/FcGF2E7U5)

*Bring the servers back for a game Ubisoft turned off in 2016.*

</div>

---

**The Mighty Quest for Epic Loot** (Ubisoft, 2013–2016) was an online-only action-RPG. Its servers were
shut down on **25 October 2016**, and with them the game — the client still installs from Steam, but with
nothing to talk to it just hangs at the login screen.

This project rebuilds that backend from scratch so the game can be brought back online as a **private
community server**: someone hosts the server, and players point their clients at it to play the game again
together — no dependence on Ubisoft's dead infrastructure. It's a **preservation / education project**,
reverse-engineered from the game's own client files — no leaked server code, and no Ubisoft executables or
media (see [Legal](#legal)).

> 💬 Join the Discord → **https://discord.gg/FcGF2E7U5**

## What works today

Once the [patcher](tools/mqel-patcher/) is applied — it drops a cert-bypass proxy DLL into the game folder
and repoints the launcher config (the game's own executables are left untouched) — the real client **boots
into the game against this server** and plays the full first-time experience end to end:

- ✅ **Full launcher boot chain** — config → maintenance → version → login → packages.
- ✅ **Account load & onboarding** — pick and name a castle, choose your first hero.
- ✅ **The core loop** — attack a dungeon, gather gold/loot, earn XP, level up, unlock a skill.
- ✅ **Campaign objectives** — tracking and completion, with the reward/notification flow.
- ✅ **Durable persistence** — per-account state in SQLite (Postgres-ready), with an admin dashboard
  and named save-states.

The current frontier is **multi-account hosting** — per-player sessions so a whole community shares one
server (the server routes to a single account today; per-user session/account routing is the next step) —
plus the **castle building / renovation** system and the wider metagame (shop, inventory, PvP). See
[`docs/`](docs/) for how each subsystem works.

## How it works

The client talks to its backend over plain **HTTP/JSON REST** (not binary netcode), which makes this far
friendlier than a typical MMO emulator. The server reimplements those endpoints.

```
Launcher (Qt + Awesomium)  ──maintenance / patch check──►   the launcher endpoints  (this server)
MightyQuest.exe  =  "Opal" engine (native C++, Zouna family)
   ├─ CEF UI running the "hyperquest" HTML/JS framework
   ├─ gameplay sim (castle attack / defence, loot)
   └─ Bloomberg (metagame) · Argo (HTTP queue) · libcurl
         ▲▼  HTTPS / JSON REST
   /mqel-live.gameserver   ◄──  the ".hqs" RPC backend this repo implements
```

The hard part is the **server-side game simulation** (combat resolution, loot generation, progression),
not the wire format.

## Quick start

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```sh
cd MQELServer

# 1. Generate a local dev TLS cert (the game requires an https server URL)
dotnet dev-certs https -ep src/MQEL.Gameserver/localhost.pfx -p mqel

# 2. Run the server
dotnet run --project src/MQEL.Gameserver      # http://0.0.0.0:8080  (+ https://0.0.0.0:8443)
```

The server creates a local SQLite database on first run and serves an **admin dashboard** at
<http://localhost:8080/>.

### Point the game client at it

1. Own the game on Steam (`steam://install/239220` — delisted from the store, but the depot still
   installs).
2. Run the **[MQEL Patcher](tools/mqel-patcher/)** (`MqelPatcher.exe`). It makes two changes *inside the
   game folder only* — drops a self-contained cert-bypass proxy (`dinput8.dll`) and repoints the
   launcher config at your server — so you can just press **Play** in Steam. Uninstall reverts everything.
3. Press **Play**.

The client's TLS trust store is empty (it rejects *every* certificate), so the patcher's proxy tells it
to skip TLS verification — which is why a self-signed dev cert on the server is fine. Full detail in the
[patcher README](tools/mqel-patcher/README.md) and [`MQELServer/README.md`](MQELServer/README.md).

> **Hosting for a community:** run the server where your players can reach it (optionally behind a domain +
> reverse proxy on 80/443), then share the address — each player runs the patcher pointed at that
> `--server-url` and joins your server.

## Game data

- **Bundled game-design config** — the small slice of the game's design data the server needs to resolve
  content (item templates, shop SKUs, objectives, castle-renovation costs, tutorial assignments) is
  included, repackaged into the server's own layout under
  [`MQELServer/src/MQEL.Gameserver/data/`](MQELServer/src/MQEL.Gameserver/data/). See its
  [README](MQELServer/src/MQEL.Gameserver/data/README.md) for provenance and rights.
- **The game client is *not* included** — obtain it from your own Steam account (`steam://install/239220`).

## Repository layout

```
MQELServer/        the server (.NET 8) — see MQELServer/README.md
  src/
    MQEL.Core/         domain models + contracts (repository, verification)
    MQEL.Data/         EF Core persistence (SQLite now, Postgres-ready)
    MQEL.Verification/ the anti-cheat seam (stubbed)
    MQEL.Gameserver/   the ASP.NET host: .hqs endpoints, static responses, admin UI, catalogs,
                       and the bundled game-design data/
  config/            the redirected launcher config
tools/mqel-patcher/  installs the cert-bypass proxy + repoints the client (launch from Steam)
docs/              how the server works — start at docs/README.md
```

## Documentation

Start at **[docs/README.md](docs/README.md)**. Highlights:

- [Boot flow](docs/boot/boot-flow.md) — the client launch → game sequence, gate by gate.
- [Progression loop](docs/gameplay/progression-loop.md) — the attack → reward → level-up loop.
- [Notifications](docs/gameplay/notifications.md) — the in-session state-sync mechanism.
- [Persistence](docs/ops/persistence.md) & [admin dashboard](docs/ops/admin-dashboard.md) — running it.

## Legal

This is an independent, non-commercial fan **preservation** project. The server code is
written from scratch.  *The Mighty Quest for Epic Loot* and all
related trademarks, game code, and assets are the property of
**Ubisoft Entertainment**. This project is **not affiliated with, endorsed by, or supported by Ubisoft**.
You must own the game and supply the client yourself.

Original code in this repository is released under the [MIT License](LICENSE); see [`LICENSE`](LICENSE)
for the accompanying notice on Ubisoft-owned material.
</content>
