# responses — the JSON bodies the server sends to the client

This folder holds the response payloads the gameserver returns to the game client. There are two kinds:

- **Served verbatim** — `<Service>.hqs/<Method>.json` files are returned as-is by the generic dispatcher
  in [`Program.cs`](../Program.cs) (`DispatchStatic`). Drop a new `responses/<Service>.hqs/<Method>.json`
  file to answer a new service call — no code change, hot-loaded per request. Examples:
  `AttackSelectionService.hqs/GetAttackSelectionList.json`, `CastleForSaleService.hqs/GetCastlesForSale.json`,
  `SeasonalCompetitionService.hqs/*`, `GuildService.hqs/GetLeaderboardPage.json`.
- **Templates merged with live state** — files like `account-information-firstrun.json`,
  `attack-tutorial-start.json`, `attack-tutorial-end.json`, `castle-bought-notifications.json`, and the
  `castles/<id>.json` specs are loaded and then combined with the account's real state (from SQLite)
  before being sent.

Genuinely per-account, mutable data — wallet, hero, inventory, objectives, progression — is **not** here;
it already lives in the database and is generated per request.

## ⚠️ Temporary — dev-phase scaffolding

These flat JSON files are a **temporary mechanism for the initial development phase**. They let us stand
up each endpoint quickly against captured/known-good payloads while the protocol is still being mapped.

**Once we're past the initial dev phase, this content moves into a real database** (served/generated
dynamically like the account data already is), rather than being read from files on disk. Treat anything
in this folder as a stand-in, not the final storage model.
</content>
