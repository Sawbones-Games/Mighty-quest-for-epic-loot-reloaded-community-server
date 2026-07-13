# Game Server — v1 Sourcing Checklist

> Companion to the `game-server-build.md` build spec (kept externally). This is the **sourcing/tracking
> sheet** for v1: what to buy, what's owned, what's deferred, and the pre-build validation gates.
> Tick **Ordered** when purchased, **Received** when in hand.

**Config locked:** platform **Radxa Rock 5A (RK3588S)**; orchestrator **Orange Pi Zero 3 2 GB** (external
to the module). Start populated with **2× Rock 5A 4 GB + 1× Zero 3**, **SD boot**, **NVMe deferred**.
Box infrastructure sized for **6 boards** (bought once; added boards ≈ ¥17.7k each).

---

## BOM

### Compute
| Item | Qty | ~¥ | Ordered | Received |
|---|---|---|---|---|
| Rock 5A 4 GB | 2 | — | ☐ | ☐ |
| Orange Pi Zero 3 2 GB (orchestrator) | 1 | — | ☐ | ☐ |
| Full-board flush-to-IO heatsink (Rock 5A) | 2 | — | ☐ | ☐ |
| Small stick-on heatsink (Zero 3) | 1 | — | ☐ | ☐ |

### Cooling
| Item | Qty | ~¥ | Ordered | Received |
|---|---|---|---|---|
| Noctua NF-A14 PWM **12 V, 4-pin** (NOT 5 V / chromax / iPPC) | 1 | 3,000 | ☐ | ☐ |
| Dust filter, 140 mm magnetic mesh | 1 | 1,200 | ☐ | ☐ |

### Power
| Item | Qty | ~¥ | Ordered | Received |
|---|---|---|---|---|
| Mean Well **LRS-100-12** | 1 | 2,600 | ☐ | ☐ |
| Isolated **copper bus bar** (V+ / V−) | 2 | — | ☐ | ☐ |
| USB-C **passive passthrough** power plug, screw-terminal (5/9/12 V) | 2 now (→6) | — | ☐ | ☐ |
| **Switched fused IEC C14 inlet** (10 A, swappable 5×20 mm fuse drawer) | 1 | 800 | ☐ | ☐ |
| Fuse **5×20 mm, T3.15 A, 250 V, ceramic slow-blow** (+ spares) | 1 pk | ~300 | ☐ | ☐ |
| Bootlace ferrules (optional, for stranded ends in screw terminals) | 1 pk | ~200 | ☐ | ☐ |

### Networking
| Item | Qty | ~¥ | Ordered | Received |
|---|---|---|---|---|
| **8-port industrial gigabit switch** (9–57 V, unmanaged, 16 Gbps backplane, store-and-forward, -40/+85 °C) | 1 | — | ☐ | ☐ |
| Ethernet patch cable, **0.3 m** (board→switch) | 6 | — | ☐ | ☐ |
| Ethernet patch cable, 0.5 m (spare for far boards) | 2 | — | ☐ | ☐ |
| Ethernet uplink cable (box→router, length to suit) | 1 | — | ☐ | ☐ |

### Storage
| Item | Qty | ~¥ | Ordered | Received |
|---|---|---|---|---|
| **SanDisk Extreme 128 GB** microSD (A2) — Rock 5A boards | 2 | — | ☐ | ☐ |
| microSD (genuine, 32–128 GB) — **Orange Pi Zero 3 orchestrator boot** | 1 | — | ☐ | ☐ |

---

## Owned / no-buy (do NOT re-order)
- **Buck converter 5 V 2 A** — feeds ESP32 only (switch is on the 12 V rail). Over-provisioned, fine.
- **18 AWG wire** — used for trunk *and* board feeds (over-spec on feeds = extra margin).
- **ESP32** — day-1 hardware layer (fan PWM, tach, power actuation, later LCD). Rail-powered, USB = data.
- **IEC C13 power cord** (standard PC/kettle lead).
- **Wago connectors** (inline 1-to-1 joins only).
- **3D-print filament + threaded inserts.**
- **2.4" mono LCD** — deferred; wires to the ESP32, not the orchestrator.

## Deferred (with upgrade triggers)
- **NVMe** (M.2 E→M adapter + SSD) → when prices normalize **AND** the 16 GB upgrade happens. *(Deferred now: adapter ~4k + doubled SSD price.)*
- **16 GB RAM boards** → trigger: DB outgrows 4 GB (read-latency-up-while-CPU-idle) **OR** starting the vision/VLM project.
- **T-slot frame** → multi-module scaling (single module is freestanding).
- **+4 Rock 5A match boards** → as match load grows; ~¥17.7k each (board + heatsink + SD + USB-C plug).
- **Orchestrator power** = its own USB-C charger, **never** the box rail.

---

## Pre-build validation gates (do BEFORE mass assembly)
- [ ] **USB-C plug → boot ONE board** first. Pure VBUS+GND passthrough occasionally needs a CC signal for the board to accept fixed 12 V. Prove it powers before wiring all 6.
- [ ] **Multimeter each USB-C plug**: continuity V→VBUS, G→GND, **open** V-to-G. Wrong (PD-trigger) plugs fight 12 V injection.
- [ ] **h2testw / f3 both SD cards** — SanDisk is the most-counterfeited brand; confirm real capacity before trusting the DB card.
- [ ] **Bus-bar amp rating** ≥ ~9 A on the listing (copper bars are typically 60 A+ — sanity check only). Keep V+ and V− bars **isolated** (no bridge = they're a dead short across the PSU).
- [ ] **Ethernet reach**: measure longest routed board→switch path; if ≤ ~25 cm, all-0.3 m works, else use a 0.5 m there. Don't over-length (excess blocks the airflow duct).
- [ ] **AC safety**: fuse the input (T3.15 A in the inlet drawer), earth the inlet → PSU ground terminal, **cover the PSU AC terminals** (3D-print), strain-relief the mains entry.
- [ ] **DB durability**: high-endurance card **not** required at this write volume, but **snapshot the DB to the NAS from day 1** (tiny DB → near-free frequent backups make SD corruption fully recoverable).

## Open confirms (verify you have / included)
- [ ] Board **mounting screws / standoffs** (M2.5 into heat-set inserts) — owned?
- [ ] **Thermal paste/pads** for the heatsinks — usually bundled; confirm yours include it.
- [ ] Fuse holder takes **5×20 mm** (not 6×30 mm) — match the fuses to the drawer.

---

## Physical dimensions (for case CAD)

Box target: **150 (W) × 200 (L) × 180 (H) mm**. Component footprints below (mm):

| Item | Footprint (L × W) | Height / depth | Source | Notes |
|---|---|---|---|---|
| **Noctua NF-A14** | 140 × 140 | 25 (thick) | official | The keystone — sets box width (140 + walls). |
| **LRS-100-12 PSU** | 129 × 97 | 30 (H) | official | Lower-rear zone. Chassis-mount. |
| **Rock 5A** | 85 × 56 | ~16 (with flush-to-IO heatsink) | official | Pi form factor. ×2 now (→6 in array). |
| **8-port switch** (stripped) | 80 × 70 | **⚠ measure** (~13–16 typ. w/ RJ45) | user | Lower-front zone. Height not yet captured. |
| **Copper bus bar** | 124 × 14 | 36 (H) | user | ×2 (V+ / V−). Mount **isolated**, not touching. |
| **Fused IEC inlet** | 31 (W) panel face | 75 (H) face × **29.7 deep into box** | user | Panel-mount; needs 29.7 mm clearance behind the wall. |
| **Orange Pi Zero 3** | 55 × 50 | ~13 + stick-on HS | official | **External to module** — size for its own mount, not the box array. |
| microSD | 15 × 11 | 1 | std | Negligible. |
| Fuse | 5 × 20 (Ø×L) | — | std | Lives in the inlet drawer. |

**CAD-critical constraints:**
- **Width** is set by the 140 mm Noctua (+ wall thickness) → the ~150 mm box width.
- **Lower zone** must fit the PSU (129 × 97 × 30) + switch (80 × 70 × ~15) inline with an airflow gap between them.
- **Bus bars** (124 × 14 × 36 each) → two isolated rails; budget ~28–30 mm width for the pair with an isolation gap.
- **Inlet** protrudes **29.7 mm** into the box behind its panel — keep that pocket clear.
- **⚠ Still need: switch height** — measure the stripped board's tallest point (RJ45 jacks) before finalizing the lower-zone height.

## Running total (v1)

| | ~¥ |
|---|---|
| Boards + heatsinks | 36,000 |
| Box hardware (complete) | 23,500 |
| **Total v1** | **~59,500** |

vs. original v1 estimate ~¥86,000 → **~¥26.5k under**, entirely from deferred upgrades (4 GB-not-16 GB
+ NVMe deferred), **not** skipped essentials. Box guts are sized for 6, so marginal board cost is ~¥17.7k.

## Current draw reference (for wiring sanity)
- **AC mains (inlet/fuse):** ~2 A at full 6-board load (~80 W DC → non-PFC on 100 V). Fuse T3.15 A.
- **12 V rail (bus bars / trunk / USB-C plugs):** ~7 A at 6 boards (~1 A/board + fan + switch). Doc's ~9 A includes headroom.
- **5 V buck (ESP32 + LCD only):** ~0.5 A. 2 A buck = wildly comfortable.
