# NavGraph — GM Authoring Guide

## What this system is

PlayerBots navigate the world using a waypoint graph. The graph has two layers:

**Hardcoded landmarks** — defined in `PlayerBotNavigator.cs`. These are named destinations bots can choose to travel to: towns, dungeons, shrines, cemeteries, moongates, wilderness camps, mining areas. Bots pick among these based on their persona. You cannot edit these in-game; they require a code change and server restart.

**Routing nodes** — stored in `Data/NavGraph.xml`. These are intermediate waypoints that fill in the road network between landmarks. Bots never choose routing nodes as destinations; they are invisible infrastructure that A\* uses to find paths. You author these entirely in-game with GM commands.

**Edges** — also in `NavGraph.xml`. Every edge is bidirectional. A bot can walk either direction along any edge.

The live graph is the union of both layers. When you run `[navbuild rebuild` (or drop/connect/remove any node), the in-memory graph is rebuilt instantly from both sources. No restart needed.

---

## Node naming conventions

| Pattern | Meaning |
|---|---|
| `Britain`, `Minoc`, `Covetous` | Hardcoded landmark — a bot destination |
| `RoadNorthBrit1`, `RoadNorthBrit2` | Road segment nodes, numbered |
| `NorthRoad_01`, `NorthRoad_02` | Trail-mode auto-named nodes |
| `BridgeVesper_N`, `BridgeVesper_S` | Bridge flanking nodes (N/S sides) |
| `XroadMinocVesper` | Crossroads node |

Routing node names must be unique across the entire graph. Keep them descriptive — you will be reading them in hop queues and edge listings while debugging.

---

## Command reference

### Graph authoring

```
[navbuild addnode <name> [routing]
```
Adds a node at your feet. Without `routing`, it is a landmark (bots can choose it as a destination). With `routing`, it is infrastructure only.

```
[navbuild connect <nodeA> <nodeB>
[navbuild connect                     ← targeting mode: click two NavNodeMarkers
```
Creates a bidirectional edge between two nodes.

```
[navbuild removeedge <nodeA> <nodeB>
```
Removes the edge. Does not remove the nodes.

```
[navbuild remove <name>
```
Removes a node and all edges referencing it from XML.

```
[navbuild insert <newName> <nodeA> <nodeB>
```
Plants a routing node at your feet between two existing nodes. Removes the direct A↔B edge, adds A↔new and new↔B. Useful for splitting a gap that's too wide.

```
[navbuild move <name>
```
Relocates an XML node to your current position. Useful to fix a badly-placed node without removing and re-adding it.

```
[navbuild goto <name>
```
Teleports you to a named node. Starting point for manual inspection.

```
[navbuild edges <name>
```
Lists all neighbors of a node with distances. Flags edges over 300 tiles.

```
[navbuild rebuild
```
Forces a full graph rebuild from `NavGraph.xml`. Use after manually editing the XML file.

---

### Visibility

```
[navshow [range]
[navbuild show [range]
```
Spawns red gem markers (`NavNodeMarker`) at every node within range (default 600 tiles). Markers auto-delete after 60 seconds. Use this to see what is already placed around you before you start dropping new nodes.

```
[navnearest [count]
[navbuild nearest [count]
```
Prints a sorted list of the closest N nodes (default 10) with distance, coordinates, and edge connections. The fastest way to orient yourself — run this first whenever you arrive at an area you want to work on.

---

### Diagnostics

```
[navtest <fromNode> <toNode>
```
Runs A\* between two named nodes and prints every hop with distances. If it returns "no route found", there is a gap in the graph between those nodes. This is your main tool for verifying connectivity after authoring.

```
[navbuild isolated
[navbuild isolated all
```
Lists every hardcoded landmark (or, with `all`, every node) that has no edges. Output is grouped by tag category (Town, Dungeon, Shrine…) and sorted alphabetically within each group. This is your work order when starting a fresh graph — run it, pick a category, connect them one by one. Ends with `[navbuild goto <name>` reminder so you can jump straight to any listed node.

```
[navbuild export
```
Writes all routing nodes and edges to `Data/NavGraph_export.txt` for offline review.

---

### Trail mode — rapid authoring

Trail mode eliminates naming and wiring friction. You walk, you spam one command, the system handles the rest.

```
[navbuild trail start <prefix> [anchorNode] [minDist]
```
Begins a session. `prefix` is the base name (`NorthRoad` → `NorthRoad_01`, `NorthRoad_02`…). Optional `anchorNode` connects the first drop to an existing node. Optional `minDist` sets the minimum tiles between drops (default: 30). For city/bridge work use 5–10. Any numeric argument is treated as minDist; any non-numeric argument is treated as anchorNode.

```
[navbuild trail start VesperBridge 5            ← dense city mode, no anchor
[navbuild trail start VesperBridge SomeNode 5   ← dense city mode, anchored
```

```
[navbuild trail drop
[navdrop                    ← identical, one word
```
Drops a routing node at your feet, names it, connects it to the previous drop. Won't drop if closer than `minDist` tiles to the last node. Warns if > 300 tiles (large gap between hops).

```
[navbuild trail end [anchorNode]
```
Closes the session. Optional `anchorNode` connects the last drop to an existing node. Prints a full route summary. Saves and rebuilds.

```
[navbuild trail cancel
```
Discards all nodes dropped this session, removes them from XML, rebuilds. Use when you walked a bad path.

```
[navbuild trail status
```
Shows the active session: prefix, drop count, last node name/coords, and how far you currently are from the last drop.

---

### Bot observation

```
[botfollow <botName>
```
Toggle. Teleports you 3 tiles behind the bot every 500ms. You go hidden. Every 2 seconds you receive a status line. Call again to stop. Auto-detaches if the bot dies or is stuck for 30 seconds (with a "consider adding a node here" message, since you're at the failure point).

```
[botinfo <botName>
```
Snapshot: location, activity, destination, full hop queue with distances, persona, current combat target, pause state.

```
[botpause <botName>
[botresume <botName>
```
Freeze/unfreeze a bot's AI tick. The bot stops in place. Use to examine where exactly in a route a bot stopped, without it running away mid-inspection.

---

## The two node types in practice

| | Hardcoded landmark | XML routing node |
|---|---|---|
| Defined in | `PlayerBotNavigator.cs` | `Data/NavGraph.xml` |
| Editable in-game | No | Yes |
| Bots travel to it | Yes (if tags match persona) | Never |
| Used for pathfinding | Yes | Yes |
| Survives server restart | Always | Yes (XML persists) |
| Created with | Code + restart | `[navbuild addnode` or trail drop |

---

## Workflow 1 — Connecting a new POI to the graph

You found an interesting location and want bots to visit it or pass through it.

### Step 1 — Decide what kind of node it is

**It's a destination** (bots should choose to travel here): add it as a hardcoded landmark in `PlayerBotNavigator.cs` with appropriate `WaypointTag` flags. This requires a code change and server restart. See the existing entries for examples.

**It's infrastructure only** (bots should route through it but never stop here): use a routing node via `[navbuild addnode <name> routing` or trail mode. No restart needed.

For this guide, we assume it's a routing node or a new landmark you want connected to the existing road network.

### Step 2 — Orient yourself

Teleport to the POI. Run:

```
[navnearest 10
```

This shows the 10 closest nodes. You are looking for:
- Which existing node is the **nearest reachable neighbor** to the west, south, east, north
- Whether there is already a node at or near this location (avoid duplicates)
- Which nodes are isolated (shown as `isolated` in the edge list) — these are candidates for connection

Also run:
```
[navshow 800
```
To see a visual overview of what is already placed around you.

### Step 3 — Add the POI node

Stand at the POI:

```
[navbuild addnode MyPOI routing
```

Or if bots should travel here (and you have set up the code):
```
[navbuild addnode MyPOI
```

### Step 4 — Find the nearest graph node to connect to

From your `[navnearest` output, pick the closest node that is already part of the network (has edges). Note its name. Check the distance — if it's under 300 tiles and there is a clear overland path, you can connect directly.

```
[navbuild connect MyPOI <nearestNode>
```

If the distance is over 300 tiles, or if there is a mountain or water body in the way, you need intermediate routing nodes. Use trail mode for this (see Workflow 2).

### Step 5 — Verify connectivity

```
[navtest <anyTown> MyPOI
```

If A\* finds a route, your node is connected. If it returns "no route found", there is still a gap somewhere between `MyPOI` and the rest of the network — usually because the nearest node you connected to is itself isolated.

Check:
```
[navbuild edges <nearestNode>
```

If it says "isolated", you need to connect `nearestNode` into the network first, then re-run `[navtest`.

---

## Workflow 2 — Laying a road segment with trail mode

You want to connect two existing nodes (or a POI to a node) across open terrain.

### The loop

```
[navbuild trail start <prefix> <startNode>
  → walk toward the destination, staying on the road or overland path
[navdrop
  → walk further
[navdrop
[navdrop
  → arrive near the destination node
[navbuild trail end <endNode>
```

**Spacing guidance:**
- Drop every 150–250 tiles on open roads (default 30-tile minimum is fine)
- Drop every 50–100 tiles near bridges, hills, and choke points
- For dense city streets, shop rows, and narrow bridges: start the trail with `minDist 5` and drop every 10–30 tiles
- You will see the distance printed after each drop — aim for even spacing
- If you see `WARN: large gap`, consider adding an extra drop in that stretch

### Naming prefixes

Use a prefix that describes the road or area:

```
NorthRoad_     Britain → Minoc northern road
SouthRoad_     Britain → Trinsic
WestRoad_      Britain → Yew / Skara
VesperMinoc_   Vesper → Minoc coastal road
MinocCovetous_ Minoc → Covetous dungeon approach
```

### Cancelling a bad path

If you realised you walked a wrong path mid-trail:
```
[navbuild trail cancel
```
All drops are removed. Start over with a corrected path.

---

## Workflow 3 — Verifying and fixing bot navigation

After authoring a segment, use a bot to validate it.

### Setup

1. Find a bot near the start of your new segment. Use `[botinfo <name>` to check its current state.
2. If it's idle or wandering, it will eventually route through your new segment naturally — or you can wait for one heading toward the destination.
3. Alternatively, pause it at a node near your segment start with `[botpause <name>`.

### Follow and observe

```
[botfollow <name>
```

Watch the 2-second status lines. You are looking for:

- `Travel -> Minoc | Next: NorthRoad_03 | 187 tiles` — healthy, making progress
- `STUCK — no movement for 8s | trying to reach: NorthRoad_03` — the bot cannot reach the next hop; there is a pathfinding failure or terrain obstacle

### When the bot gets stuck

The 30-second auto-detach fires when the bot is truly stuck. When it does, **you are standing at the failure point**. Common causes:

| Symptom | Likely cause | Fix |
|---|---|---|
| Stuck at a river/water | Missing bridge approach nodes | `[navdrop` a node on each bank, connect |
| Stuck at a cliff | Node placed on wrong side | `[navbuild move <name>` to a walkable spot |
| Stuck in a loop | Two nodes too close together | `[navbuild edges` both, remove the short edge |
| Stuck for no visible reason | Gap too wide for pathfinder | `[navbuild insert` a midpoint |

### Fix in place

If trail is active:
```
[navdrop
```
Drops a node exactly where you are (the failure point). This is the fastest fix.

If trail is not active:
```
[navbuild addnode FixPoint routing
[navbuild connect FixPoint <prevNode>
[navbuild connect FixPoint <nextNode>
```

### Re-verify

```
[botresume <name>
[botfollow <name>
```

Watch the bot re-attempt the fixed segment.

---

## Workflow 4 — Fixing an isolated node

`[navnearest` shows `isolated` next to a node that has no edges. These are orphans — they exist in the XML but no bot can ever route through them.

Causes:
- Server restarted mid-trail (session was lost, XML nodes saved but edges not yet wired)
- Manual `[navbuild addnode` without a subsequent `[navbuild connect`
- A `[navbuild remove` on a node that was the only bridge between two others

Fix:
```
[navbuild goto <isolatedNode>    ← teleport to it
[navnearest 10                   ← see what's nearby
[navbuild connect <isolatedNode> <nearestNetworkNode>
[navtest <isolatedNode> <anyTown>  ← confirm it's now reachable
```

---

## Gap size reference

| Distance | Guidance |
|---|---|
| < 30 tiles | Trail drop rejected (too close) |
| 30–150 tiles | Ideal hop size for open road |
| 150–300 tiles | Acceptable; consider terrain |
| 300–500 tiles | Add 1–2 intermediate nodes |
| > 500 tiles | Split into multiple drops; bots will visibly stutter |

Bots navigate by moving directly toward the next hop coordinate. If the hop is very long and there is any terrain variance in between, they may pathfind around obstacles and arrive correctly — but large gaps increase stuck risk, especially near water, bridges, and mountains.

---

## Common mistakes

**Placing nodes in water or on impassable terrain**
The graph has no terrain awareness. If you place a node inside a mountain or on a water tile, bots will try to walk there and get stuck. Always confirm you are standing on walkable ground before dropping.

**Forgetting to close a trail with `trail end` or `trail cancel`**
The session persists until you close it. If the server restarts mid-trail, the session is lost but any XML-saved nodes remain as orphans. Run `[navnearest` in the area to find them and either wire them in or remove them.

**Connecting a POI to an isolated node**
Your POI now has an edge, but the edge leads to another orphan. Always verify with `[navtest` after any new connection.

**Gap > 300 tiles across a bridge**
Bridges are choke points. Place nodes on both approaches, not just one side. The bot needs to align with the bridge entrance before crossing.

**Naming conflicts**
If a name already exists (hardcoded or XML), `[navbuild addnode` rejects it. Use `[navbuild edges <name>` to inspect the existing node before deciding whether to connect to it or rename your new one.


**Rough Workflow**
Hardcoded POIs are "final" landmarks that should be connected to the XML-based nodes and edges
ie: Britain, Minoc.

### The loop

```
[navbuild trail start Britain2Minoc Britain 5
  → walk toward the destination, staying on the road or overland path
[navdrop
  → walk further
[navdrop
[navdrop
  → arrive near the destination node
[navbuild trail end Minoc
```