# Open issues

> Known gaps that are understood but deliberately deferred — not bugs we're unaware of. Each entry has enough
> context to pick back up cold. Remove an entry once it's solved (and note the resolution in the relevant doc).

## World-map castle-ring needs more real seed castles

**Found:** 2026-07-07, while fixing the PvP-tutorial "camera pans to empty space" bug (see
[[project-pvp-tutorial-bot-castles]] in project memory / the session that diagnosed this).

**Mechanism (confirmed live):** the attack-selection world map spawns "other player" castles in a ring
**360° around the player's own castle, evenly distributed by count** — not at fixed slot indices, not tied to
any specific castle ID. With only 2-5 real castles at a given level, they cluster near the cardinal directions
and leave large empty gaps; panning/looking anywhere else shows nothing. This is why the FTUE's scripted camera
pan (`PanAttackSelectionCameraAssignmentActionSpec` in assignment `005006`) landed on empty space even after the
underlying quest objective (301, "Friendly Pillage") was fully working — the objective only needs ANY castle
attacked, but the *visual* ring needs enough castles to have one facing wherever the camera happens to look.

**What we tried and reverted:** duplicated the existing 14 non-campaign bot castles (spec-DB `PVE_01/02/03_*`
retired dungeons — see `tools/make_tutorial_castle.py`) 3x each under cosmetic name suffixes ("Shamblington II",
etc.) at fresh IDs, purely to confirm the "ring fills in with enough count" theory. **Confirmed correct** — with
~15-28 castles per level the ring visibly filled in — but this is fake padding (four copies of the same 14
dungeons is not a legitimate solution) and was reverted. Do not re-add it as a "fix"; it was a diagnostic test.

**Real fix, not yet done:** populate the ring with enough *distinct* real castles per level. Two paths, either or
both:
1. **Find more well-structured, distinct castle spec entries already in the game-data spec DB** beyond the 14
   already used (`4,5,71` @ Level 1, `6,7,72,73` @ Level 2, `8,9,10,74,75,76,80` @ Level 3 — see
   `tools/make_tutorial_castle.py`'s `CASTLES` dict). Before adding more, re-run the same collision check used
   for the existing pool (`OBJECTIVESETTINGS.JSON` CastleId + every `Assignments/*` `UbisoftCastleId`) so nothing
   picked collides with future campaign content.
2. **Seed with real player-built castles once the castle-building tutorial is implemented** (currently blocked
   on the renovation-level system reaching the point where build-mode unlocks — see `docs/STATUS.md` "castle
   building / renovation"). Once a player (or the dev account) has actually built a few castles through real
   play, capture/save a handful of those as additional ring entries — this is the more authentic long-term
   answer, since it's what actually happens in production (real other players' castles fill the ring).

**How many is "enough"?** Unconfirmed — the diagnostic test used ~15-28 and it visibly worked, but the real
per-level count in retail is unknown (we don't have a live capture of the real `GetAttackSelectionList` response
size). Don't just re-inflate the count arbitrarily when revisiting this; if possible, get a real number from
a live capture or from whichever mechanism (2) above ends up producing before over/under-provisioning.

**Files touched by the diagnostic test (already reverted):** `tools/make_tutorial_castle.py`,
`MQELServer/src/MQEL.Gameserver/responses/AttackSelectionService.hqs/GetAttackSelectionList.json`,
`MQELServer/src/MQEL.Gameserver/responses/castles/*.json`.
