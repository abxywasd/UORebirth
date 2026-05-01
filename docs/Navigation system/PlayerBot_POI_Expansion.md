# PlayerBot POI Expansion — Implementation Spec

**Purpose:** Expand `PlayerBotNavigator.cs` from its current 20-entry flat list to a
rich, categorised POI table. Bots are currently too clamped — every profile draws from
a 6-7 entry pool with two broken coordinates and no wilderness, shrines, or crafting
destinations. This spec drives the implementation; do nothing until told to proceed.

---

## 1. Source files analysed

| File | What it provided |
|---|---|
| `Data/Common.map` | ~860 named locations, format `+/-type: x y mapIndex [name]` |
| `Data/Locations/felucca.xml` | Authoritative coords for towns, dungeon levels, shrines |
| `Scripts/Mobiles/PlayerBot/PlayerBotNavigator.cs` | Current 20 POIs + profile pools |

`Common.map` map-index key: **0 = Felucca** (incl. Lost Lands x > 5000), 1 = Trammel,
2 = Ilshenar, 3 = Malas, 4 = Tokuno. Bots operate on Felucca only; all new waypoints
use `Map.Felucca`.

---

## 2. Bug fixes (two wrong coordinates in current code)

| Key | Current (wrong) | Correct (from felucca.xml / Common.map) |
|---|---|---|
| `"Deceit"` | `1380, 1014, 0` | **`4111, 432, 5`** |
| `"Hythloth"` | `1473, 3502, 0` | **`4722, 3814, 0`** |

The current Deceit coord lands in open wilderness between Britain and Wrong dungeon.
The current Hythloth coord lands in ocean east of Trinsic.

---

## 3. Waypoint tag enum

Add a `WaypointTag` flags enum and a `Tags` field to `BotWaypoint`:

```csharp
[Flags]
public enum WaypointTag
{
    None      = 0,
    Town      = 1 << 0,   // player-service hub
    Dungeon   = 1 << 1,   // dungeon surface entrance
    Shrine    = 1 << 2,   // virtue shrine
    Notable   = 1 << 3,   // castle, abbey, landmark
    Mining    = 1 << 4,   // ore/resource gathering area
    Cemetery  = 1 << 5,   // graveyard
    Wilderness = 1 << 6,  // outpost, orc fort, bandit camp
    LostLands = 1 << 7,   // T2A coordinates (x > 5000)
    PKHub     = 1 << 8,   // historically a PK-activity centre
}
```

The existing `PickDestination` method continues working with named string pools;
tags enable an optional `PickByTag(WaypointTag mask)` helper added in the same pass.

---

## 4. Full waypoint table

All entries use `Map.Felucca`. Coordinates are from `felucca.xml` (preferred) or
`Common.map` where xml lacks the point.

### 4a. Towns (17 entries — 5 new vs. current 12)

| Key | X | Y | Z | Notes |
|---|---|---|---|---|
| `"Britain"` | 1475 | 1645 | 20 | felucca.xml Center |
| `"BuccaneersDen"` | 2720 | 2110 | 0 | Common.map; PK-hub |
| `"Cove"` | 2263 | 1237 | 0 | Common.map |
| `"Jhelom"` | 1388 | 3762 | 0 | current |
| `"Magincia"` | 3714 | 2235 | 20 | felucca.xml Bank |
| `"Minoc"` | 2475 | 417 | 15 | felucca.xml North |
| `"Moonglow"` | 4442 | 1122 | 5 | felucca.xml Center |
| `"NujelM"` | 3636 | 1198 | 0 | current |
| `"Ocllo"` | 3650 | 2516 | 0 | felucca.xml North |
| `"Delucia"` | 5228 | 3978 | 37 | felucca.xml Center — **NEW**, LostLands |
| `"Papua"` | 5730 | 3208 | -4 | felucca.xml Center — **NEW**, LostLands |
| `"SerpentsHold"` | 3025 | 3498 | 10 | felucca.xml North — **NEW** |
| `"SkaraBrae"` | 576 | 2200 | 0 | current (coord tidied) |
| `"Trinsic"` | 1927 | 2779 | 0 | felucca.xml Center |
| `"Vesper"` | 2882 | 788 | 0 | felucca.xml Center |
| `"Wind"` | 5252 | 104 | 15 | Common.map — **NEW** (enter via passage 1362, 896) |
| `"Yew"` | 535 | 992 | 0 | current |

### 4b. Dungeon entrances (11 entries — 4 new, 2 fixed vs. current 8)

| Key | X | Y | Z | Notes |
|---|---|---|---|---|
| `"Covetous"` | 2499 | 919 | 0 | correct, unchanged |
| `"Deceit"` | 4111 | 432 | 5 | **FIXED** (was 1380, 1014) |
| `"Despise"` | 1298 | 1080 | 0 | correct, unchanged |
| `"Destard"` | 1176 | 2637 | 0 | correct, unchanged |
| `"Hythloth"` | 4722 | 3814 | 0 | **FIXED** (was 1473, 3502) |
| `"Shame"` | 514 | 1561 | 0 | correct, unchanged |
| `"Wrong"` | 2043 | 238 | 10 | felucca.xml (was 236, 13) |
| `"OrcCave"` | 1019 | 1431 | 0 | felucca.xml — **NEW** |
| `"FireBrit"` | 2923 | 3407 | 8 | felucca.xml Brit Entrance — **NEW** |
| `"IceBrit"` | 1999 | 81 | 4 | felucca.xml Brit Entrance — **NEW** |
| `"TerathanKeep"` | 5451 | 3143 | -60 | felucca.xml — **NEW**, LostLands |

> **Note on OrcFort:** Current key `"OrcFort"` uses 2429, 1380. felucca.xml lists the
> Cove Orc Fort at 2171, 1372. Rename the key to `"OrcFortCove"` and use
> felucca.xml coords. Add `"OrcFortYew"` at 633, 1499 separately.

### 4c. Virtue Shrines (9 entries — all new)

| Key | X | Y | Z | Source |
|---|---|---|---|---|
| `"ShrineChaos"` | 1458 | 844 | 0 | felucca.xml Shrines |
| `"ShrineCompassion"` | 1858 | 874 | -1 | felucca.xml |
| `"ShrineHonesty"` | 4217 | 564 | 36 | felucca.xml |
| `"ShrineHonor"` | 1730 | 3528 | 3 | felucca.xml |
| `"ShrineHumility"` | 4276 | 3699 | 0 | felucca.xml |
| `"ShrineJustice"` | 1301 | 639 | 16 | felucca.xml |
| `"ShrineSacrifice"` | 3355 | 299 | 9 | felucca.xml |
| `"ShrineSpirituality"` | 1595 | 2490 | 5 | felucca.xml |
| `"ShrineValor"` | 2496 | 3933 | 0 | felucca.xml |

### 4d. Notable overworld landmarks (9 entries — all new)

| Key | X | Y | Z | Notes |
|---|---|---|---|---|
| `"EmpathAbbey"` | 635 | 860 | 0 | Common.map / felucca.xml |
| `"BlackthornCastle"` | 1523 | 1456 | 15 | felucca.xml Blackthorn Entrance |
| `"BritishCastle"` | 1401 | 1625 | 28 | felucca.xml British Entrance |
| `"Lycaeum"` | 4312 | 1000 | 0 | Common.map (Moonglow area) |
| `"SerpentPillarN"` | 2986 | 2887 | 0 | Common.map doracron pillar |
| `"BrigandCamp"` | 885 | 1682 | 0 | felucca.xml Yew-Britain Brigand Camp |
| `"YewFortDamned"` | 972 | 768 | 0 | felucca.xml Yew Fort of the Damned |
| `"OrcFortYew"` | 633 | 1499 | 0 | felucca.xml Yew Orc Fort |
| `"OrcFortCove"` | 2171 | 1372 | 0 | felucca.xml Cove Orc Fort (replaces old OrcFort) |

### 4e. Mining / crafting areas (5 entries — all new)

| Key | X | Y | Z | Notes |
|---|---|---|---|---|
| `"MinocMiningCamp"` | 2583 | 528 | 15 | felucca.xml Mining Camp |
| `"MinocNorth"` | 2475 | 417 | 15 | felucca.xml — guild, forge area |
| `"MinocGypsyCamp"` | 2540 | 651 | 0 | felucca.xml Gypsy Camp |
| `"EastMines"` | 2587 | 492 | 0 | Common.map east mines |
| `"BritSmithGuild"` | 1348 | 1778 | 0 | Common.map Britain's Blacksmith Guild |

### 4f. Cemeteries / dark locations (5 entries — all new)

| Key | X | Y | Z | Notes |
|---|---|---|---|---|
| `"BritainCemetery"` | 1384 | 1497 | 10 | felucca.xml Cemetery |
| `"VesperCemetery"` | 2786 | 867 | 0 | felucca.xml Cemetery |
| `"YewCemetery"` | 724 | 1138 | 0 | felucca.xml Cemetery |
| `"JhelomCemetery"` | 1296 | 3719 | 0 | felucca.xml Cemetery |
| `"MoonglewCemetery"` | 4546 | 1338 | 8 | felucca.xml Cemetery |

---

## 5. Tag assignments

Each waypoint gets one or more tags from §3. The implementation assigns these at
registration time in `Initialize()`:

```
Town:      all §4a entries
Dungeon:   all §4b entries
Shrine:    all §4c entries
Notable:   all §4d entries
Mining:    all §4e entries (+ tag Town for Minoc entries)
Cemetery:  all §4f entries
PKHub:     BuccaneersDen, YewFortDamned, BrigandCamp, Deceit, Despise, Wrong
LostLands: Delucia, Papua, Wind, TerathanKeep, OrcFortLostLands (§4b)
Wilderness: OrcFortYew, OrcFortCove, BrigandCamp, YewFortDamned, OrcCave
```

---

## 6. Updated profile pools

These replace the current hard-coded string arrays in `PickDestination`.

### PlayerKiller

```
Primary destinations (70% of picks):
  Deceit, Despise, Wrong, Covetous, Shame, Destard, OrcFortYew, OrcFortCove,
  BrigandCamp, YewFortDamned, OrcCave, FireBrit, IceBrit

Social/stalk destinations (30% of picks):
  BuccaneersDen
```

Weight ratio: 7:3 between primary and social. Implementation: roll `Utility.Random(10)`,
0-6 → primary pool, 7-9 → social pool.

### Crafter

```
Workshop/supply destinations (60% of picks):
  MinocMiningCamp, MinocNorth, MinocGypsyCamp, EastMines, BritSmithGuild,
  Britain, Minoc, Vesper, Yew, Trinsic

Town browse destinations (40% of picks):
  Britain, Vesper, Magincia, Moonglow, SkaraBrae, Trinsic, Cove, NujelM
```

### Adventurer

```
Dungeon runs (40% of picks):
  Deceit, Despise, Destard, Covetous, Shame, Wrong, Hythloth,
  OrcCave, FireBrit, IceBrit, TerathanKeep

Overland exploration (35% of picks):
  EmpathAbbey, BlackthornCastle, BrigandCamp, SerpentsHold, Lycaeum,
  OrcFortYew, OrcFortCove, SerpentPillarN, YewFortDamned

Shrine pilgrimage (15% of picks):
  ShrineCompassion, ShrineHonesty, ShrineJustice, ShrineSacrifice,
  ShrineSpirituality, ShrineValor, ShrineHumility, ShrineHonor, ShrineChaos

Town visit (10% of picks):
  Britain, Moonglow, Vesper, Jhelom, SerpentsHold, SkaraBrae
```

---

## 7. Lost Lands gating

Bots that travel to Lost Lands destinations (x > 5000 on Map.Felucca) currently
teleport directly when unobserved. No map-switch logic is needed — the Lost Lands
are part of `Map.Felucca`. However, the surface-world entrances (the cave passages
at 2404, 218 and the Delucia passage) exist as landmarks in Common.map.

For version 1 of this expansion, bots are allowed to teleport directly to Lost Lands
coords when unobserved (current behaviour). No passage routing is required.

**Profile restriction:** Only `Adventurer` (20% chance) and `PlayerKiller` (10% chance)
should pick Lost Lands destinations. `Crafter` never picks them.
Add a `LostLands` filter inside `PickDestination`: before returning a `LostLands`-tagged
waypoint, roll against the profile threshold; re-pick if the roll fails.

---

## 8. Proposed code structure changes

### BotWaypoint (minimal extension)

```csharp
public class BotWaypoint
{
    public Point3D   Location;
    public Map       Map;
    public string    Name;
    public WaypointTag Tags;   // NEW
}
```

### PlayerBotNavigator additions

```csharp
// Existing Add() overload — unchanged
private static void Add(string name, Point3D loc, Map map) { ... }

// New overload with tags
private static void Add(string name, Point3D loc, Map map, WaypointTag tags)
{
    if (!s_Landmarks.ContainsKey(name))
        s_Landmarks[name] = new BotWaypoint { Location=loc, Map=map, Name=name, Tags=tags };
}

// New: pick by tag mask (future use)
public static BotWaypoint PickByTag(WaypointTag mask)
{
    // collect matching, return random
}
```

`PickDestination` continues using string pools (no refactor needed — just bigger arrays
and the weight-split logic described in §6).

---

## 9. Summary of changes vs. current state

| Metric | Before | After |
|---|---|---|
| Total waypoints | 20 | 65 |
| Broken coordinates | 2 | 0 |
| Towns | 12 | 17 |
| Dungeon entrances | 8 | 11 |
| Shrines | 0 | 9 |
| Notable landmarks | 0 | 9 |
| Mining / crafting | 0 | 5 |
| Cemeteries | 0 | 5 |
| PK primary pool size | 6 | 13 |
| Crafter primary pool size | 6 | 10 |
| Adventurer pool size | 7 | 23+ (across sub-pools) |
| Lost Lands destinations | 0 | 5 (gated by profile roll) |
