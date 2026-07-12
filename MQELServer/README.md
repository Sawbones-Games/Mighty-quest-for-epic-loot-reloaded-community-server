# MQELServer — the private server (.NET 8)

The reimplemented backend for *The Mighty Quest for Epic Loot*. A modular monolith with a stubbed
verification (anti-cheat) seam, headless, plus a small HTML admin UI.

## Layout

```
src/
  MQEL.Core/          domain models, repository interfaces, the verification contract
                      (AuditBundle, IVerificationService).
  MQEL.Data/          EF Core DbContext + repo implementations — provider-swappable
                      (SQLite today, Postgres-ready).
  MQEL.Verification/  StubVerificationService — receives the full AuditBundle, returns {valid:true}.
                      Real replay re-simulation drops in later behind the same interface.
  MQEL.Gameserver/    ASP.NET host: the .hqs RPC endpoints the client talks to, the file-backed
                      static responses (responses/), the admin dashboard (wwwroot/), and the
                      game-design catalogs loaded from the decrypted spec DB.
config/               PublicLauncherSettings.private.json — the redirected launcher config.
```

## Run it

```sh
dotnet run --project src/MQEL.Gameserver     # http://0.0.0.0:8080  (+ https://0.0.0.0:8443)
```

The server applies EF migrations on startup and creates a local SQLite database (`mqel.db`). The
game-design catalogs (`ItemCatalog`, `MissionCatalog`, `AssignmentCatalog`, `CastleRenovationCatalog`)
load from the bundled [`src/MQEL.Gameserver/data/`](src/MQEL.Gameserver/data/) folder; if it is absent
they fall back to empty and the server still boots — some content just won't resolve.

> **HTTPS cert.** The game client requires an `https` server URL, so Kestrel needs a dev certificate at
> `src/MQEL.Gameserver/localhost.pfx` (password `mqel`). Generate one with:
> `dotnet dev-certs https -ep src/MQEL.Gameserver/localhost.pfx -p mqel`. The client itself is told to skip
> TLS verification by the patcher, so a self-signed cert is fine.

## Point the client at it

Use the **[MQEL Patcher](../tools/mqel-patcher/)** — it drops the cert-bypass proxy into the game folder
and repoints the launcher config, so you just press **Play** in Steam. See its
[README](../tools/mqel-patcher/README.md).

See the top-level [README](../README.md) for the full quick-start, and [docs/](../docs/) for how each
subsystem works.
</content>
