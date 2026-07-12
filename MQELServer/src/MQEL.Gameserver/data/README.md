# data — bundled game-design configuration

This folder holds the small slice of the game's design data the server needs to resolve content at
runtime. It is loaded by the catalog classes in the gameserver (`ItemCatalog`, `MissionCatalog`,
`AssignmentCatalog`, `CastleRenovationCatalog`) via `ItemCatalog.FindDataRoot()`.

It has been **repackaged into this project's own layout** — it is not the game's original folder
structure, and it is only the handful of files the server actually reads (not the full settings dump).

```
data/
  items/
    shop-skus.json            shop SKU → item id + price       (SkuCode lookups)
    hero-item-templates.json  item template → archetype/type/level
  objectives/
    objective-settings.json   campaign objectives: conditions, rewards, requirements
  castle/
    renovation-settings.json  castle-renovation levels + material costs
  assignments/
    <id>.json                 one per tutorial/campaign assignment (its action list)
```

## Provenance & rights

These values are **game-design configuration extracted from the publicly distributed game client** and
are the property of **Ubisoft Entertainment**. They are included here only so the reimplemented server
can resolve items, objectives, and progression the way the original did (interoperability). This is a
non-commercial preservation/education project and is not affiliated with or endorsed by Ubisoft. See the
repository root [`LICENSE`](../../../../LICENSE) and README for the full notice.

If the folder is missing, the catalogs fall back to empty and the server still boots — store purchases,
objective rewards, and renovation costs simply won't resolve.
</content>
