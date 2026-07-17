#!/usr/bin/env python3
"""Pack the decrypted spec DB down to the server's OWN runtime format.

The gameserver must never read game-data/settings-extracted at runtime: that tree is a local, gitignored
decrypt of Ubisoft's settings.bin, absent on any machine that did not run the decrypt. A runtime dependency on
it means the server silently resolves nothing (empty catalogs) everywhere it is not present.

This is a dev tool. Its output under MQELServer/src/MQEL.Gameserver/gamedata/ is committed and is the only
game-design data the server reads. Regenerate it after supplying your own game-data/settings-extracted.

Every record here is reduced to the fields the server actually reads — the catalogs already threw the rest
away at load time, so this only moves that reduction from runtime to build time. 3.5MB of source settings
becomes ~330KB of pack. If the server ever needs a new field, add it here and re-run; don't reach back into
the extraction from C#.

Usage:  python tools/pack_gamedata.py            (paths default to this repo's layout)
        python tools/pack_gamedata.py --spec-root ... --out-dir ...
"""

import argparse
import json
import os
import sys

FORMAT_VERSION = 1

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_SPEC = os.path.join(REPO, "game-data", "settings-extracted")
DEFAULT_OUT = os.path.join(REPO, "MQELServer", "src", "MQEL.Gameserver", "gamedata")


def short_type(s):
    """'Ns.Ns.Name, Assembly' -> 'Name'. Mirrors MissionCatalog.ShortType."""
    s = s or ""
    comma = s.find(",")
    if comma >= 0:
        s = s[:comma]
    dot = s.rfind(".")
    return s[dot + 1:] if dot >= 0 else s


def read_json(*parts):
    with open(os.path.join(*parts), "r", encoding="utf-8-sig") as f:
        return json.load(f)


def write_pack(out_dir, name, payload):
    payload = {"FormatVersion": FORMAT_VERSION, **payload}
    path = os.path.join(out_dir, name)
    # sort_keys + trailing newline: the pack is committed, so byte-stable output keeps diffs meaningful.
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(payload, f, indent=1, sort_keys=True, ensure_ascii=False)
        f.write("\n")
    return os.path.getsize(path)


# ---- missions (GeneralSettings/OBJECTIVESETTINGS.JSON) ----------------------------------------------------
# Condition/reward $types are a finite set (code-analysis/rest-api/objectives.md). An unrecognised condition is
# packed under its raw short type so it still LOADS and is simply never auto-met — same as the old runtime
# behaviour, rather than being silently dropped into "no conditions => instantly complete".

COND_MAP = {
    "CastleEnteredCondition":              lambda c: {"Kind": "CastleEntered"},
    "CastleCompletedCondition":            lambda c: {"Kind": "CastleCompleted"},
    "DefenseIngredientDestroyedCondition": lambda c: {"Kind": "Destroyed", "ItemType": c.get("ItemType"),
                                                      "SpecContainerId": c.get("SpecContainerId", 0),
                                                      "Count": c.get("Count", 1)},
    "ConstructionPointsBuiltCondition":    lambda c: {"Kind": "CPBuilt", "Count": c.get("Count", 1)},
    "BuildingRankCondition":               lambda c: {"Kind": "BuildingRank",
                                                      "SpecContainerId": c.get("SpecContainerId", 0),
                                                      "Count": c.get("Count", 1), "Rank": c.get("Rank", 1)},
    "DefenseIngredientBuiltCondition":     lambda c: {"Kind": "Built", "ItemType": c.get("ItemType"),
                                                      "Count": c.get("Count", 1)},
    "CastleValidityCondition":             lambda c: {"Kind": "CastleValid"},
}


def pack_reward_items(reward, out):
    for r in (reward or {}).get("RewardItems", []) or []:
        t = short_type(r.get("$type"))
        if t == "CurrencyAmountRewardItem":
            ca = r.get("CurrencyAmount") or {}
            out.append({"Kind": "Currency", "CurrencyType": ca.get("CurrencyType"), "Amount": ca.get("Amount", 0)})
        elif t == "CraftingMaterialsRewardItem":
            mats = [{"Id": m.get("Id", 0), "Quantity": m.get("Quantity", 1)}
                    for m in (r.get("CraftingMaterials") or [])]
            out.append({"Kind": "Materials", "Materials": mats})
        elif t == "XpRewardItem":
            out.append({"Kind": "Xp", "Xp": r.get("Xp", 0)})
        elif t == "InventoryItemRewardItem":
            it = r.get("Item") or {}
            out.append({"Kind": "Item", "ItemTemplateId": it.get("TemplateId", 0),
                        "ItemLevel": it.get("ItemLevel", 1), "ItemArchetypeId": it.get("ArchetypeId", 0)})


def pack_missions(spec_root, out_dir):
    doc = read_json(spec_root, "GameplaySettings", "GeneralSettings", "OBJECTIVESETTINGS.JSON")
    missions = []
    for o in doc.get("Objectives", []):
        if not isinstance(o, dict) or o.get("Id") is None:
            continue

        conds = []
        for c in o.get("Conditions", []) or []:
            if not isinstance(c, dict):
                continue
            t = short_type(c.get("$type"))
            conds.append(COND_MAP[t](c) if t in COND_MAP else {"Kind": t})

        rewards = []
        pack_reward_items(o.get("Reward"), rewards)
        for _cls, per_hero in sorted((o.get("PerHeroReward") or {}).items()):
            pack_reward_items(per_hero, rewards)

        requires = [r.get("ObjectiveId", 0) for r in (o.get("Requirements") or [])
                    if isinstance(r, dict) and short_type(r.get("$type")) == "ObjectiveCompletedObjectiveRequirement"]

        castle_id = o.get("CastleId") or {}
        missions.append({
            "Id": o["Id"],
            "Category": o.get("Category") or "",
            "Type": short_type(o.get("$type")),
            "CastleType": castle_id.get("CastleType"),
            "CastleId": castle_id.get("Id"),
            "CastleTypes": list(o.get("CastleTypes") or []),
            "Conditions": conds,
            "Rewards": rewards,
            "RequiresObjectiveIds": requires,
            "ManualPopupOnCompletion": bool(o.get("ManualPopupTriggerOnCompletion", False)),
            "ClientSideCompletion": bool(o.get("ClientSideCompletion", False)),
        })
    missions.sort(key=lambda m: m["Id"])
    size = write_pack(out_dir, "missions.json", {"Missions": missions})
    return f"missions.json      {len(missions):5d} missions   {size/1024:7.1f} KB"


# ---- items (ShopSettings/SHOPSKUBASESETTINGS.JSON + HeroItems/HEROITEMTEMPLATES.JSON) ---------------------

def pack_items(spec_root, out_dir):
    sku_doc = read_json(spec_root, "GameplaySettings", "ShopSettings", "SHOPSKUBASESETTINGS.JSON")
    skus = {}
    for s in sku_doc.get("Skus", []):
        code = (s or {}).get("Code")
        if not code:
            continue
        price = s.get("Price") or {}
        skus[code] = {"ItemId": s.get("ItemId", 0),
                      "PriceCurrency": price.get("CurrencyType", 0),
                      "PriceAmount": price.get("Amount", 0)}

    tpl_doc = read_json(spec_root, "GameplaySettings", "HeroItems", "HEROITEMTEMPLATES.JSON")
    templates = {}
    for t in tpl_doc.get("TemplateList", []):
        tid = (t or {}).get("Id")
        if tid is None:
            continue
        # ItemLevel comes from LevelMin — the field the old ItemCatalog read.
        templates[str(tid)] = {"ArchetypeId": t.get("ArchetypeId", 0),
                               "HeroItemTypeId": t.get("HeroItemTypeId", 0),
                               "ItemLevel": t.get("LevelMin", 1)}

    size = write_pack(out_dir, "items.json", {"Skus": skus, "Templates": templates})
    return f"items.json         {len(skus):5d} skus / {len(templates)} templates   {size/1024:7.1f} KB"


# ---- castle renovation (GeneralSettings/CASTLERENOVATIONSETTINGS.JSON) ------------------------------------

def pack_castle_renovation(spec_root, out_dir):
    doc = read_json(spec_root, "GameplaySettings", "GeneralSettings", "CASTLERENOVATIONSETTINGS.JSON")
    costs = {}
    for level_name, info in (doc.get("PerLevelRenovationInformation") or {}).items():
        costs[level_name] = [{"TemplateId": i.get("TemplateId", 0), "Quantity": i.get("StackCount", 1)}
                             for i in ((info or {}).get("Cost") or [])]
    size = write_pack(out_dir, "castle-renovation.json", {"Costs": costs})
    return f"castle-renovation.json {len(costs):1d} levels           {size/1024:7.1f} KB"


# ---- assignments (Assignments/<id> - <name>/GAMEPLAY.JSON) -----------------------------------------------
# ExecuteAssignmentActionCommand carries only {AssignmentId, ActionIndex}, so the server needs to resolve that
# pair back to an action type. Actions stay POSITIONAL (ActionIndex indexes this array) — an action we don't
# model is packed as {"Type": ...} with no payload rather than omitted, so later indices don't shift.
# Only SetCastleRenovationLevelAssignmentActionSpec's payload is read today (GameEndpoints.cs).

def pack_assignments(spec_root, out_dir):
    root = os.path.join(spec_root, "GameplaySettings", "Assignments")
    assignments = {}
    for folder in sorted(os.listdir(root)):
        full = os.path.join(root, folder)
        if not os.path.isdir(full):
            continue
        id_part = folder.split(" - ", 1)[0]
        if not id_part.isdigit():
            continue
        gameplay = os.path.join(full, "GAMEPLAY.JSON")
        if not os.path.isfile(gameplay):
            continue
        doc = read_json(gameplay)
        raw_actions = (doc[0].get("Actions") if isinstance(doc, list) and doc and isinstance(doc[0], dict) else None) or []
        actions = []
        for a in raw_actions:
            if not isinstance(a, dict):
                actions.append({"Type": ""})
                continue
            t = short_type(a.get("$type"))
            entry = {"Type": t}
            if t == "SetCastleRenovationLevelAssignmentActionSpec":
                entry["CastleRenovationLevel"] = a.get("CastleRenovationLevel")
            actions.append(entry)
        assignments[str(int(id_part))] = {"Name": folder, "Actions": actions}
    size = write_pack(out_dir, "assignments.json", {"Assignments": assignments})
    return f"assignments.json   {len(assignments):5d} assignments {size/1024:7.1f} KB"


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--spec-root", default=DEFAULT_SPEC)
    ap.add_argument("--out-dir", default=DEFAULT_OUT)
    args = ap.parse_args()

    if not os.path.isdir(args.spec_root):
        sys.exit(f"spec root not found: {args.spec_root}\n"
                 f"Decrypt it first:  tools/bff/bff-cli.exe extract-mqfel-settings-bin <settings.bin>")
    os.makedirs(args.out_dir, exist_ok=True)

    print(f"spec: {args.spec_root}\nout:  {args.out_dir}\n")
    for line in (pack_missions(args.spec_root, args.out_dir),
                 pack_items(args.spec_root, args.out_dir),
                 pack_castle_renovation(args.spec_root, args.out_dir),
                 pack_assignments(args.spec_root, args.out_dir)):
        print("  " + line)
    print(f"\nformat v{FORMAT_VERSION} — commit the pack; the server reads only this.")


if __name__ == "__main__":
    main()
