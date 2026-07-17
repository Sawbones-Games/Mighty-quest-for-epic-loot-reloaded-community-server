# gamedata — bundled game-design configuration

This folder holds the small slice of the game's design data the server needs to resolve content at runtime.
It is loaded by the catalog classes in the gameserver (`ItemCatalog`, `MissionCatalog`, `AssignmentCatalog`,
`CastleRenovationCatalog`) via `GameData.FindDir()`.

It is a **reduced pack in this project's own format** — not the game's original folder structure, and only the
handful of fields the server actually reads (not the full settings dump). It is produced from a decrypted
`settings.bin` by [`tools/pack_gamedata.py`](../../../../tools/pack_gamedata.py) and committed, so a clone runs
with no setup.

```
gamedata/
  items.json             shop SKUs (code -> item id + price) + item templates (archetype/type/level)
  missions.json          campaign objectives: conditions, rewards, requirements
  assignments.json       tutorial/campaign assignment action lists, keyed by id
  castle-renovation.json castle-renovation levels + material costs
```

Each file carries a `FormatVersion` that the server checks at startup; a mismatch is fatal. Need a spec field
the server doesn't read yet? Add it to `pack_gamedata.py`, re-run, and commit the regenerated pack — never read
the extraction from the C# runtime.

## Provenance & rights

These values are **game-design configuration extracted from the publicly distributed game client** and are the
property of **Ubisoft Entertainment**. They are included here only so the reimplemented server can resolve
items, objectives, and progression the way the original did (interoperability). This is a non-commercial
preservation/education project and is not affiliated with or endorsed by Ubisoft. See the repository root
[`LICENSE`](../../../../LICENSE) and README for the full notice.

If the folder is missing, the server fails to start with a message naming the missing file and how to
regenerate it — the catalogs never silently load empty.
