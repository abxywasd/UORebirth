# Island & Cross-Water Navigation — Transit Edge Proposal

## The Problem

The current NavGraph works entirely on foot. A* routes through waypoints using Euclidean distance, and bots walk between hops. This is sufficient for the Britannian mainland, but fails for:

- **Skara Brae** — island, accessible only by ferry or moongate
- **Moonglow** — island, accessible only by moongate
- **Magincia** — island, accessible only by moongate
- **Jhelom** — island group, accessible only by moongate
- **Nujel'm** — island, accessible only by moongate or ship
- **Delucia / Papua** — Lost Lands, accessible only via T2A passages (Serpent Pillars)

The root cause is not a pathfinding limitation — A* handles all of Felucca fine because every island town shares `Map.Felucca`. The problem is that there are no edges crossing water. If you added a direct edge from a mainland node to `SkaraBrae`, A* would route through it and the bot would walk straight into the sea.

The fix requires a new edge type: one that A* can route through, but that tells the bot to **teleport instead of walk**.

---

## Core Concept: Transit Edges

A **transit edge** is a normal graph edge with one behavioral difference: when a bot traverses it, it teleports from the source node to the destination node instead of walking.

Transit edges model:
- Moongates (8 bidirectional gates across Britannia)
- The Skara Brae ferry (mainland dock ↔ island dock)
- T2A passage gates (overworld entrance ↔ Lost Lands exit)
- Any future teleport mechanism (ship departure points, etc.)

From A*'s perspective, a transit edge is identical to a walking edge. The graph treats them the same: they create connections between nodes, they cost Euclidean distance, and they participate in pathfinding normally. The only difference is in the bot movement loop.

---

## Why Unobserved Bots Already Work

When no player is within 18 tiles, the bot's travel loop already teleports to each waypoint in sequence (see `DoActivityTravel` in `PlayerBotAI.cs`). This means that for unobserved bots, transit edges require **no AI change at all**. The bot teleports to the moongate entry node, then teleports to the moongate exit node, then teleports to the next road node — indistinguishable from any other hop sequence.

The only case that needs new code is the **observed** path: when a player is nearby, the bot walks toward each hop using RunUO's pathfinder. If the next hop is across water via a transit edge, the pathfinder will fail and the bot gets stuck.

**Summary:**

| Scenario | Current behavior | With transit edges |
|---|---|---|
| Unobserved bot, island destination | No route (water gap) | Works automatically |
| Observed bot, island destination | No route (water gap) | Teleports at gate entry node |

---

## The Moongate Network

All 8 Britannian moongates already exist as hardcoded landmarks in `PlayerBotNavigator.Initialize()`. Their coordinates are already correct. They just need transit edges connecting them.

| Entry Node | Exit Node | Connects |
|---|---|---|
| `MoongateBrit` | `MoongateSkara` | Britain ↔ Skara Brae |
| `MoongateBrit` | `MoongateMoonglow` | Britain ↔ Moonglow |
| `MoongateMinoc` | `MoongateVesper` | Minoc ↔ Vesper area (mainland shortcut) |
| `MoongateYew` | `MoongateTrinsic` | Yew ↔ Trinsic (mainland) |
| `MoongateMagincia` | `MoongateJhelom` | Magincia ↔ Jhelom |

These five pairs cover the island connections. The remaining moongate links are mainland-to-mainland shortcuts that may or may not be useful depending on whether land routes already exist.

> **Note:** In the real pre-T2A UO moongate system, each gate leads to a different destination depending on the lunar phase (Felucca and Trammel moons together determine the destination). For PlayerBots, we ignore moon phase entirely and treat each gate as a static point-to-point teleport. Bots have no reason to understand or respect the moon cycle; predictable routing matters more than authenticity here.

### Existing Moongate Landmark Coordinates

From `PlayerBotNavigator.cs`:

```
MoongateBrit     (1336, 1997,   5) Felucca
MoongateJhelom   (1499, 3771,   5) Felucca
MoongateMagincia (3563, 2139,  34) Felucca
MoongateMinoc    (2701,  692,   5) Felucca
MoongateMoonglow (4467, 1283,   5) Felucca
MoongateSkara    ( 643, 2067,   5) Felucca
MoongateTrinsic  (1828, 2948, -20) Felucca
MoongateVesper   (2701,  692,   5) Felucca   ← same coords as Minoc gate; verify in-game
MoongateYew      ( 771,  752,   5) Felucca
```

Verify the Vesper moongate — it currently shares coordinates with the Minoc gate, which is likely a data error. Use `[navbuild goto MoongateVesper` to jump there and confirm.

---

## The Skara Brae Ferry

Skara Brae is also reachable by ferry from the mainland. The ferry runs from a dock on the western coast to the Skara Brae docks. The ferryman is an NPC on the boat.

For PlayerBots, modeling the ferry as a transit edge is straightforward: add two routing nodes (one on each dock approach) and a transit edge between them. The bot walks to the mainland dock node, teleports to the Skara island dock node, and walks on.

This is less authentic than actually boarding the ferry, but:
- The ferry has a schedule and requires NPC interaction (double-click)
- Implementing ferry boarding would require the bot to detect the ferry item, wait for it, and interact
- The moongate on Skara already provides authentic cross-water travel for most purposes

**Recommendation:** Implement the ferry as a transit edge pair for now. If the authenticity of bots "taking the ferry" becomes important later, the dock nodes are already in place and the AI hook exists.

**Approximate dock coordinates (verify in-game):**

| Node name | Side | Approximate location |
|---|---|---|
| `FerryDockMainland` | West coast of Britannia | ~(370, 2100, 0) |
| `FerryDockSkara` | Skara Brae west dock | ~(220, 2000, 0) |

Walk to each dock in-game and use `[where` to get the exact coordinates, then add them with `[navbuild addnode FerryDockMainland routing` etc.

---

## Lost Lands Access (T2A)

The Lost Lands are located in the far east and southeast of Map.Felucca (X > 5000). They are separated from Britannia not by impassable water but by the fact that there is no overland path — the continent simply ends. Access is via special passage gates.

Three passage landmarks already exist in the navigator:

```
MarblePassage    (1957, 2072,  0)  Overworld side of the Lost Lands passage
DeluciaPassage   (1629, 3321,  0)  Overworld side of the Delucia passage
FireIslandEntrance (2923, 3406, 8)  Fire Island / Fire dungeon
```

These are already wired as hardcoded landmarks with `WaypointTag.LostLands`. What is missing is the exit-side node in the Lost Lands and the transit edge linking the two sides.

**Example for Delucia:**

| Entry node | Exit node |
|---|---|
| `DeluciaPassage` (overworld) | `DeluciaEntry` (Lost Lands side, near Delucia) |

The Lost Lands side entry coordinate is approximately `(5765, 2965, -2)` (verify in-game). Add it as a routing node, then add a transit edge from `DeluciaPassage` to `DeluciaEntry`. Bots routed to `Delucia` will then be able to reach it via A* through the passage.

Similarly for Papua via `MarblePassage` and an exit node near Papua.

---

## Required Implementation Changes

### 1. `PlayerBotNavigator.cs` — Transit edge registry

Add a static set to track which edges are transit:

```csharp
private static readonly HashSet<string> s_TransitEdges
    = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

// Canonical key: alphabetically sorted pair to handle both directions
private static string TransitKey( string a, string b )
{
    return string.Compare( a, b, StringComparison.OrdinalIgnoreCase ) < 0
        ? a + "|" + b
        : b + "|" + a;
}

private static void AddTransitEdge( string a, string b )
{
    AddEdge( a, b );
    s_TransitEdges.Add( TransitKey( a, b ) );
}

public static bool IsTransitEdge( string a, string b )
{
    return s_TransitEdges.Contains( TransitKey( a, b ) );
}
```

Clear `s_TransitEdges` at the start of `BuildGraph()` alongside `s_Edges.Clear()`.

Load the `transit` attribute in the XML edge loop:

```csharp
bool transit = string.Equals(
    el.GetAttribute( "transit" ), "true", StringComparison.OrdinalIgnoreCase );

if ( transit )
    AddTransitEdge( a, b );
else
    AddEdge( a, b );
```

Wire the 8 moongate pairs directly in `Initialize()` using `AddTransitEdge` (they are hardcoded landmarks, not XML nodes):

```csharp
// Moongate transit network — call after all Add() entries, before BuildGraph()
AddTransitEdge( "MoongateBrit",     "MoongateSkara"    );
AddTransitEdge( "MoongateBrit",     "MoongateMoonglow" );
AddTransitEdge( "MoongateMinoc",    "MoongateVesper"   );
AddTransitEdge( "MoongateYew",      "MoongateTrinsic"  );
AddTransitEdge( "MoongateMagincia", "MoongateJhelom"   );
```

The remaining moongate-to-moongate links (where not needed because land routes exist) can be skipped. Add them if pathfinding testing shows bots benefit from the shortcut.

Also expose the transit set for the GM command layer:

```csharp
public static HashSet<string> TransitEdges { get { return s_TransitEdges; } }
```

---

### 2. `PlayerBotAI.cs` — Teleport on transit hop

Modify `AdvanceHop` to check for transit when dequeuing a node and the next hop exists:

```csharp
private void AdvanceHop( PlayerBot bot )
{
    var hops = bot.ActivityState.WaypointHops;
    if ( hops == null || hops.Count == 0 ) { bot.OnArrived(); return; }

    BotWaypoint arrived = hops.Dequeue();

    if ( hops.Count == 0 ) { bot.OnArrived(); return; }

    BotWaypoint next = hops.Peek();

    // Transit edge: teleport to next hop instead of walking
    if ( PlayerBotNavigator.IsTransitEdge( arrived.Name, next.Name ) )
    {
        m_Mobile.MoveToWorld( next.Location, next.Map );
        m_StuckTicks = 0;
        m_Path       = null;

        hops.Dequeue(); // consume the transit destination

        if ( hops.Count > 0 )
        {
            bot.ActivityState.TravelDestination = hops.Peek().Location;
            m_LastTravelPos = Point3D.Zero;
        }
        else
        {
            bot.OnArrived();
        }
        return;
    }

    bot.ActivityState.TravelDestination = next.Location;
    m_LastTravelPos = Point3D.Zero;
}
```

This change fires for both observed and unobserved bots, but for unobserved bots the travel loop already teleports to each hop individually, so the transit hop will be consumed there before AdvanceHop is even reached with a non-transit next-hop. No double-teleport risk.

---

### 3. `NavBuildCommand.cs` — New `transit` subcommand

```
[navbuild transit <nodeA> <nodeB>
```

Writes a `transit="true"` edge to `NavGraph.xml`, rebuilds the graph, and prints confirmation.

```
[navbuild transits
```

Opens a `NavNodeListGump` listing every transit edge pair. Each row shows the two node names; Goto teleports to the first node of the pair.

These are small additions to the existing subcommand dispatch. Implementation is ~40 lines.

---

### 4. `NavGraph.xml` — Transit edge format

No schema change needed. Transit edges use the existing `<Edge>` element with an added attribute:

```xml
<Edges>
  <!-- Standard walking edge -->
  <Edge a="NorthRoad_01" b="NorthRoad_02"/>

  <!-- Transit edge — bot teleports instead of walks -->
  <Edge a="FerryDockMainland" b="FerryDockSkara" transit="true"/>
  <Edge a="DeluciaPassage"    b="DeluciaEntry"   transit="true"/>
</Edges>
```

Hardcoded moongate transits do not appear in NavGraph.xml — they are set in `Initialize()`. Only XML-authored transit edges (ferry, T2A passages) go in the file.

---

## GM Authoring Workflow

### Connecting a moongate to the road network

The moongate nodes exist but are isolated — no edges connect them to the road grid. Each gate needs an approach trail from the nearest road.

Example: Britain moongate at `(1336, 1997, 5)`.

```
[navbuild goto MoongateBrit         ← teleport to the gate
[navnearest 10                      ← find nearest connected road nodes
[navbuild trail start BritGate 10   ← dense mode for approach road
  walk toward nearest road node
[navdrop
[navdrop
[navbuild trail end <nearestRoadNode>
[navtest Britain MoongateSkara      ← verify full cross-water route
```

Repeat for each gate. Once the gate node is connected to the road grid, A* can route through it and the transit edge carries bots to the island.

### Creating a new transit edge (ferry or passage)

```
[stand at mainland dock]
[navbuild addnode FerryDockMainland routing

[stand at island dock]
[navbuild addnode FerryDockSkara routing

[navbuild transit FerryDockMainland FerryDockSkara   ← creates transit edge
[navtest SkaraBrae Britain                            ← verify end-to-end
```

Then connect each dock node to its local road network with trail mode.

### Verifying island connectivity

```
[navtest Britain SkaraBrae
[navtest Britain Moonglow
[navtest Britain Magincia
[navtest Minoc Delucia
[navtest Papua Delucia
```

Each of these should return a route once the approach trails and transit edges are in place. If `[navtest` returns "no route found", the gap is almost always the approach trail — the gate node itself is isolated or the transit edge is missing.

---

## Naming Conventions for Transit Infrastructure

| Pattern | Meaning |
|---|---|
| `MoongateBrit`, `MoongateSkara` | Existing gate nodes (hardcoded) |
| `GateApproach_<gateName>_01` | Approach trail node near a gate |
| `FerryDock<Location>` | Ferry dock node |
| `T2AEntry_<name>` | Lost Lands exit-side node |
| `T2AApproach_<name>_01` | Approach trail to a T2A passage |

---

## Routing Approach for Island Towns

Once transit edges are in place, each island town's hardcoded landmark needs to be reachable via at least one transit edge. The A* graph connects them like this:

```
Britain → [road nodes] → MoongateBrit ==transit==> MoongateSkara → [road nodes] → SkaraBrae
```

For Skara Brae specifically: the `SkaraBrae` hardcoded landmark is at `(576, 2200, 0)`. The `MoongateSkara` gate is at `(643, 2067, 5)`. These are ~155 tiles apart — close enough for a short approach trail or even a direct edge connection if the terrain is clear.

For Moonglow: the `Moonglow` hardcoded landmark is at `(4442, 1122, 5)`. The `MoongateMoonglow` gate is at `(4467, 1283, 5)`. About 161 tiles. Similar approach.

---

## Design Trade-offs

**Moongate authenticity vs determinism**

Real pre-T2A moongates cycle through destinations based on the lunar phase. A bot that "uses" the Britain gate might end up at Moonglow, Minoc, Trinsic, or Skara Brae depending on when it arrives. This is interesting world behavior but makes routing unpredictable and could cause bots to end up stranded on the wrong island.

The transit edge model ignores moon phase entirely: each gate is a static point-to-point link. Bots always arrive where A* says they should. This is the right trade-off for a navigation system.

**Ferry authenticity**

The Skara Brae ferry runs on a schedule with an NPC ferryman. Bots using the transit edge model teleport between dock nodes instantly, skipping the crossing entirely. A future enhancement could have bots walk onto the ferry item and trigger a timed crossing, but this requires item detection logic and schedule awareness. Transit edges are the correct baseline.

**Multiple moongate links vs single link**

A bot traveling from Britain to Skara Brae currently has one option: the transit edge. In real UO, a bot could also sail there. For now, the single transit edge is sufficient — the boat option is a much larger feature (boat ownership, navigation, docking) and not planned.

**A* cost for transit edges**

A* currently uses Euclidean distance as both the heuristic and the edge cost. A transit edge between `MoongateBrit` and `MoongateSkara` has an Euclidean distance of ~700 tiles, which A* treats as a high-cost edge. In practice this doesn't matter — there is no competing land route to Skara Brae, so A* will always choose the transit path regardless of its apparent cost. If in the future we want A* to correctly model the near-zero cost of instant teleportation, the edge cost could be overridden to a small constant (e.g., 50 tiles). This is a minor optimization, not a correctness issue.

---

## Implementation Scope

| File | Change | Estimated size |
|---|---|---|
| `PlayerBotNavigator.cs` | Add `s_TransitEdges`, `AddTransitEdge`, `IsTransitEdge`, load XML `transit` attribute, wire 5 moongate pairs in `Initialize()` | ~50 lines |
| `PlayerBotAI.cs` | Modify `AdvanceHop` to detect and execute transit teleport | ~20 lines |
| `NavBuildCommand.cs` | Add `transit` and `transits` subcommands | ~40 lines |
| `NavGraph.xml` | No format change; ferry + T2A transit edges added as data during in-game authoring | 0 lines now |

Total estimated additions: **~110 lines of C#**, plus in-game authoring time to lay approach trails to each gate.
