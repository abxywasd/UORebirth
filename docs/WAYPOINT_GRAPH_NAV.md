# Waypoint Graph Navigation — Implementation Plan

## 1. Root Cause Analysis

The current system has two layers that both fail for long-distance travel.

**Layer 1 — `MovementPath` (RunUO's A\*):** Hard-coded step limit (~200–400 tiles). Britain→Minoc is ~1,100 tiles. The pathfinder returns `Success=false`, falling back to direct heading. The bot runs into water or mountains.

**Layer 2 — `PlayerBotNavigator.Advance()`:** Calls `ai.MoveToPoint(dest, true, 5)` which creates a single `PathFollower` aimed at the *final destination*. There is no intermediate routing. Once `MovementPath` fails, stuck detection triggers after 40 ticks and the bot gives up — unless no player is watching, in which case it teleports (the only reason cross-map travel ever works today).

**The fix:** Turn the flat landmark dictionary into a sparse graph. Pre-compute an A\* path over graph nodes. Navigate one edge at a time — each hop ≤ 300 tiles, reliably within `MovementPath`'s range.

---

## 2. Architecture

```
PickDestination()
      │
      ▼
PlayerBotNavigator.ComputeRoute(startPos, destWaypoint)
      │   [A* over adjacency list loaded from Data/NavGraph.xml]
      │
      ▼
Queue<BotWaypoint>  ←  stored in ActivityState.WaypointHops
      │
      ▼  (each AI tick)
DoActivityTravel():
  peek → MoveToPoint(currentHop, 5 tiles)
  arrived? → dequeue, peek next hop
  queue empty? → OnArrived()
```

Each hop is a single edge in the graph. The hop target is always a nearby waypoint — short enough that `MovementPath` succeeds.

**Graph data lives in `Data/NavGraph.xml`**, not hardcoded C#. `BuildGraph()` reads it at startup. The in-game `[navbuild` tool writes to it and rebuilds the graph live — no script reload needed to add or adjust nodes.

**Graceful degradation:** `ComputeRoute()` returns `null` when no graph path exists (disconnected node, island, missing edges). The caller falls back to `SetTravelDirect()` — current behavior (direct heading + unobserved teleport). Bots never crash on missing edges; they just use the old behavior for that route. This means you can build the graph incrementally — add nodes without connections, connect region by region, each batch immediately improves routing for that corridor.

---

## 3. Files to Change

| File | Change |
|---|---|
| `Scripts/Mobiles/PlayerBot/PlayerBotNavigator.cs` | Remove hardcoded edges; add `BuildGraph()` reading XML, `ComputeRoute()` A\* |
| `Scripts/Mobiles/PlayerBot/PlayerBot.cs` | Update `TravelToRandom()` to call `ComputeRoute()`; add `ActivityState` hop queue |
| `Scripts/Mobiles/PlayerBot/PlayerBotAI.cs` | Rewrite `DoActivityTravel()` to drain `WaypointHops` hop by hop |
| `ActivityState` (inside `PlayerBot.cs`) | Add `Queue<BotWaypoint> WaypointHops`, `SetTravelRoute()`, `SetTravelDirect()` |
| New: `Data/NavGraph.xml` | The graph data — nodes and edges, authored via `[navbuild` |
| New: `Scripts/Commands/NavBuildCommand.cs` | In-game authoring tool `[navbuild` + debug commands `[navtest` `[navshow` `[navbot` |
| New: `Scripts/Items/Misc/NavNodeMarker.cs` | Temporary visual marker item placed at node positions (non-serialized, auto-deletes) |

---

## 4. Data Structure Changes

### 4a. `BotWaypoint` — add `RoutingOnly`

```csharp
public class BotWaypoint
{
    public Point3D     Location;
    public Map         Map;
    public string      Name;
    public WaypointTag Tags;
    public bool        RoutingOnly;   // graph node only, never a travel destination
}
```

`PickDestinationInternal()` must skip `RoutingOnly` nodes. They will never appear in the profile destination tables by construction, but an explicit guard prevents accidents if someone adds a routing node with a destination tag by mistake.

### 4b. `ActivityState` — add hop queue

Inside `ActivityState` in `PlayerBot.cs`:

```csharp
public Queue<BotWaypoint> WaypointHops     { get; private set; }
public BotWaypoint        FinalDestination { get; private set; }

// Start a routed journey (graph path found)
public void SetTravelRoute( BotWaypoint finalDest, List<BotWaypoint> hops )
{
    FinalDestination  = finalDest;
    WaypointHops      = new Queue<BotWaypoint>( hops );
    TravelDestination = hops.Count > 0 ? hops[0].Location : finalDest.Location;
    TravelMap         = finalDest.Map;
    SetActivity( BotActivity.Traveling );
}

// Start a direct journey (no graph path — island, disconnected node, or fallback)
public void SetTravelDirect( BotWaypoint dest )
{
    FinalDestination  = dest;
    WaypointHops      = null;
    TravelDestination = dest.Location;
    TravelMap         = dest.Map;
    SetActivity( BotActivity.Traveling );
}
```

`TravelDestination` now always tracks the **current hop target**, not the final destination. The existing stuck-detection code in `DoActivityTravel` watches `TravelDestination` and works unchanged.

### 4c. Graph storage — `Data/NavGraph.xml`

```xml
<NavGraph>
  <Nodes>
    <Node name="Britain"          x="1496" y="1628" z="10" map="Felucca" tags="Town" />
    <Node name="RoadNorthBrit1"   x="1480" y="1350" z="0"  map="Felucca" routing="true" />
    <!-- ... -->
  </Nodes>
  <Edges>
    <Edge a="Britain" b="RoadNorthBrit1" />
    <Edge a="RoadNorthBrit1" b="RoadNorthBrit2" />
    <!-- ... -->
  </Edges>
</NavGraph>
```

`BuildGraph()` deserializes this on startup. The `[navbuild` tool appends to it and calls `BuildGraph()` again to rebuild the in-memory graph without a script reload.

---

## 5. Coverage Philosophy

**Go dense.** The reliability of the system scales directly with node density. The goal is to cover all of Britannia's road network — every road junction, mountain pass, bridge crossing, dock edge, and town entry point.

**Target: 200–300 nodes for mainland Britannia.**

At this density, hops stay consistently under 150 tiles. `MovementPath` success rate approaches 100%. The stuck-skip logic becomes trivial — skipping one bad hop out of 15 is nothing; skipping one out of 3 is a disaster. Beyond ~300 nodes, diminishing returns kick in hard — terrain itself becomes the bottleneck, not hop length.

**Emergent behavior:** At high density the graph *is* the road network. Bots naturally follow actual roads instead of cutting straight lines through terrain. Britain→Minoc bots use the northern road. Trinsic bots use the south road. It looks like real player travel.

**What to prioritize:**
- Every road junction
- Both sides of every mountain pass / narrow corridor
- Both ends of every bridge
- Town entry points (gate or dock, not every building)
- Dungeon entrance areas
- Dock/ferry approach zones for coastal towns

**What to skip:**
- Interior of buildings — short-range `MovementPath` handles the last 50 tiles to a vendor
- Dungeon interiors — separate problem, out of scope (see section 12)
- Wilderness off-road — bots only need to navigate between landmarks, not explore randomly

**Practical workflow:** Use `[navbuild` (section 10) to place nodes in-game at your feet. You never transcribe coordinates manually. Stand at a junction, type one command, node exists. The tool is the canonical authoring workflow.

---

## 6. Seed Edge List

This is the initial set of edges to bootstrap the graph before dense coverage work begins. It covers the major corridors and proves the system works end-to-end. Expand aggressively from here using `[navbuild`.

### Britain Local

```
Britain          ↔ BlackthornCastle
Britain          ↔ BritishCastle
Britain          ↔ BritainCemetery
Britain          ↔ BritSmithGuild
Britain          ↔ SnakePass
Britain          ↔ MoongateBrit
Britain          ↔ RoadNorthBrit1
Britain          ↔ RoadBritTrinsic1
Britain          ↔ RoadSkaraBrit1
Britain          ↔ RoadYewBrit1
```

### Northern Corridor (Britain → Minoc)

```
RoadNorthBrit1       ↔ RoadNorthBrit2
RoadNorthBrit2       ↔ CompassionCrossroads
RoadNorthBrit2       ↔ ShrineCompassion
CompassionCrossroads ↔ MtKendallApproach
CompassionCrossroads ↔ Cove
MtKendallApproach    ↔ MtKendallPass
MtKendallPass        ↔ MinocSouthRoad
MinocSouthRoad       ↔ Minoc
MinocSouthRoad       ↔ Covetous
Minoc                ↔ MinocMiningCamp
Minoc                ↔ MinocNorth
Minoc                ↔ MinocGypsyCamp
Minoc                ↔ EastMines
Minoc                ↔ MoongateMinoc
```

### Cove – Vesper – Minoc East Road

```
Cove              ↔ RoadCoveVesper
RoadCoveVesper    ↔ Vesper
Vesper            ↔ VesperCemetery
Vesper            ↔ MoongateVesper
Vesper            ↔ RoadVesperMinoc1
RoadVesperMinoc1  ↔ MinocVesperBridge
MinocVesperBridge ↔ MinocSouthRoad
Vesper            ↔ Deceit
```

### Britain → Trinsic (South Road)

```
Britain              ↔ BritTrinsicXroad
BritTrinsicXroad     ↔ RoadBritTrinsic1
RoadBritTrinsic1     ↔ RoadBritTrinsic2
RoadBritTrinsic2     ↔ Trinsic
Trinsic              ↔ TrinsicCemetery
Trinsic              ↔ MoongateTrinsic
Trinsic              ↔ ShrineHonor
Trinsic              ↔ RoadTrinsicSerpent
RoadTrinsicSerpent   ↔ SerpentsHold
SerpentsHold         ↔ ShrineValor
```

### Britain → Yew → Skara Brae

```
RoadYewBrit1  ↔ EmpathAbbey
EmpathAbbey   ↔ Yew
Yew           ↔ MoongateYew
Yew           ↔ YewCemetery
Yew           ↔ YewFortDamned
Yew           ↔ YewPawPath
Yew           ↔ OrcFortYew
Yew           ↔ YewLumberRegion
Yew           ↔ ShrineJustice
Yew           ↔ StoneCircleYew
Yew           ↔ Shame
RoadSkaraBrit1 ↔ SkaraBrae
SkaraBrae     ↔ MoongateSkara
```

### Island Clusters (Disconnected from Mainland)

```
Moonglow  ↔ MoongateMoonglow
Moonglow  ↔ MoonglewCemetery
Moonglow  ↔ Lycaeum
Moonglow  ↔ ShrineHonesty

NujelM    ↔ NujelmCemetery

Magincia  ↔ MoongateMagincia
Magincia  ↔ RoadMaginciaOcllo
RoadMaginciaOcllo ↔ Ocllo
Ocllo     ↔ OclloMines
Ocllo     ↔ OldHavenRuins

Jhelom    ↔ MoongateJhelom
Jhelom    ↔ JhelomCemetery
```

Island clusters have no edges to the mainland. `ComputeRoute()` returns `null` for mainland→island trips, and the caller falls back to `SetTravelDirect()` (current behavior: teleport when unobserved).

### Dungeon Entrances (Wired to Nearest Mainland Node)

```
Despise  ↔ RoadNorthBrit1
Covetous ↔ MinocSouthRoad
Deceit   ↔ Vesper
Wrong    ↔ IceBrit
IceBrit  ↔ Minoc
Shame    ↔ Yew
Destard  ↔ RoadBritTrinsic2
OrcCave  ↔ RoadYewBrit1
FireBrit ↔ RoadTrinsicSerpent
```

### Lost Lands (Disconnected Cluster)

```
Delucia      ↔ Papua
Delucia      ↔ SavageCampT2A
Papua        ↔ TerathanKeep
```

Lost Lands have no mainland edges. Bots rolled for Lost Lands travel use `SetTravelDirect()` and teleport when unobserved (existing behavior unchanged).

### Miscellaneous Overworld Links

```
DesertCompassion   ↔ CompassionCrossroads
ShrineCompassion   ↔ CompassionCrossroads
GreatNorthernRoad  ↔ RoadNorthBrit2
GreatNorthernRoad  ↔ CompassionCrossroads
BrigandCamp        ↔ RoadYewBrit1
HedgeMaze          ↔ RoadBritTrinsic1
ShrineSpirituality ↔ BritTrinsicXroad
```

---

## 7. A\* Implementation

Add to `PlayerBotNavigator.cs`. The graph is small enough that an O(n²) open-list scan is sufficient — no priority queue needed.

```csharp
// Returns ordered hop list including finalDest as the last entry.
// Returns null if no graph path exists; caller uses SetTravelDirect().
public static List<BotWaypoint> ComputeRoute( Point3D start, Map map, BotWaypoint destination )
{
    BotWaypoint startNode = NearestNode( start, map, maxDistSq: 600 * 600 );
    if ( startNode == null )
        return null;
    if ( startNode.Name == destination.Name )
        return new List<BotWaypoint>{ destination };

    var open      = new List<string> { startNode.Name };
    var cameFrom  = new Dictionary<string,string>();
    var gScore    = new Dictionary<string,double> { { startNode.Name, 0.0 } };
    var fScore    = new Dictionary<string,double> { { startNode.Name, Heuristic(startNode, destination) } };

    while ( open.Count > 0 )
    {
        string current = LowestF( open, fScore );

        if ( current == destination.Name )
            return ReconstructPath( cameFrom, current );

        open.Remove( current );

        List<string> neighbors;
        if ( !s_Edges.TryGetValue( current, out neighbors ) )
            continue;

        BotWaypoint curWp = GetLandmark( current );
        if ( curWp == null ) continue;

        foreach ( string neighborName in neighbors )
        {
            BotWaypoint nb = GetLandmark( neighborName );
            if ( nb == null ) continue;

            double tentG = gScore[current] + Heuristic( curWp, nb );

            double existingG;
            if ( !gScore.TryGetValue( neighborName, out existingG ) || tentG < existingG )
            {
                cameFrom[neighborName] = current;
                gScore[neighborName]   = tentG;
                fScore[neighborName]   = tentG + Heuristic( nb, destination );
                if ( !open.Contains( neighborName ) )
                    open.Add( neighborName );
            }
        }
    }

    return null;
}

private static BotWaypoint NearestNode( Point3D pos, Map map, double maxDistSq )
{
    BotWaypoint best    = null;
    double      bestDsq = maxDistSq;

    foreach ( var wp in s_Landmarks.Values )
    {
        if ( wp.Map != map ) continue;
        double dx  = wp.Location.X - pos.X;
        double dy  = wp.Location.Y - pos.Y;
        double dsq = dx*dx + dy*dy;
        if ( dsq < bestDsq ) { bestDsq = dsq; best = wp; }
    }

    return best;
}

private static double Heuristic( BotWaypoint a, BotWaypoint b )
{
    double dx = a.Location.X - b.Location.X;
    double dy = a.Location.Y - b.Location.Y;
    return Math.Sqrt( dx*dx + dy*dy );
}

private static string LowestF( List<string> open, Dictionary<string,double> fScore )
{
    string best  = open[0];
    double bestF = fScore.ContainsKey(best) ? fScore[best] : double.MaxValue;
    foreach ( string n in open )
    {
        double f = fScore.ContainsKey(n) ? fScore[n] : double.MaxValue;
        if ( f < bestF ) { bestF = f; best = n; }
    }
    return best;
}

private static List<BotWaypoint> ReconstructPath( Dictionary<string,string> cameFrom, string current )
{
    var names = new List<string> { current };
    while ( cameFrom.ContainsKey(current) )
    {
        current = cameFrom[current];
        names.Insert( 0, current );
    }

    // Drop index 0 (startNode — bot is already there), convert rest to BotWaypoint
    var result = new List<BotWaypoint>();
    for ( int i = 1; i < names.Count; i++ )
    {
        BotWaypoint wp = GetLandmark( names[i] );
        if ( wp != null ) result.Add( wp );
    }
    return result;
}
```

---

## 8. Travel Initiator Changes

### `PlayerBot.TravelToRandom()`

```csharp
public void TravelToRandom()
{
    BotWaypoint dest = PlayerBotNavigator.PickDestination( PlayerBotProfile );
    if ( dest == null ) return;

    List<BotWaypoint> route = PlayerBotNavigator.ComputeRoute( Location, Map, dest );

    if ( route != null && route.Count > 0 )
        ActivityState.SetTravelRoute( dest, route );
    else
        ActivityState.SetTravelDirect( dest );
}
```

If any other code path sets `BotActivity.Traveling` (e.g. grouped travel, flee-to-town), it should also be updated to call `ComputeRoute()` and use `SetTravelRoute()` where a destination waypoint is known. If the destination is an arbitrary `Point3D` with no graph node, `SetTravelDirect()` is the correct fallback — current behavior is preserved.

---

## 9. `DoActivityTravel` Rewrite

The key behavioral changes:
- On arrival at a hop: dequeue and update `TravelDestination` to the next hop.
- On stuck after 40 ticks: **skip the hop** instead of abandoning the whole journey. One impassable segment should not abort a cross-map trip.
- On unobserved teleport: teleport to current hop target only (not the final destination), then advance the queue. This keeps the per-hop teleport distance reasonable and avoids Z-level mismatches at distant coordinates.

```csharp
private bool DoActivityTravel( PlayerBot bot )
{
    var     hops = bot.ActivityState.WaypointHops;
    Point3D dest = bot.ActivityState.TravelDestination;
    Map     dMap = bot.ActivityState.TravelMap;

    if ( dMap == null || dMap == Map.Internal )
    {
        bot.ActivityState.SetActivity( BotActivity.Wandering );
        return true;
    }

    EnsureRunSpeed();
    CheckTravelCombatInterrupt( bot );  // existing PK scan + aggressor check, extracted to helper

    // Unobserved: teleport to current hop, then advance queue
    if ( !AnyPlayersInRange( 18 ) )
    {
        int z = dMap.GetAverageZ( dest.X, dest.Y );
        m_Mobile.MoveToWorld( new Point3D( dest.X, dest.Y, z ), dMap );
        m_StuckTicks = 0;
        m_Path       = null;
        AdvanceHop( bot );
        return true;
    }

    // Stuck detection
    if ( m_Mobile.Location == m_LastTravelPos )
    {
        m_StuckTicks++;

        if ( m_StuckTicks >= 40 )
        {
            m_StuckTicks = 0;
            m_Path       = null;

            if ( hops != null && hops.Count > 0 )
                AdvanceHop( bot );      // skip stuck hop, keep traveling
            else
                bot.ActivityState.SetActivity( BotActivity.Wandering );

            return true;
        }

        if ( m_StuckTicks % 10 == 0 )
        {
            m_Path = null;
            DoMove( (Direction)Utility.Random(8) | Direction.Running );
        }
    }
    else
    {
        m_StuckTicks = 0;
    }

    m_LastTravelPos = m_Mobile.Location;

    if ( PlayerBotNavigator.Advance( this, bot, dest, dMap ) )
    {
        m_StuckTicks = 0;
        m_Path       = null;
        AdvanceHop( bot );
    }

    return true;
}

// Dequeue the current hop and update TravelDestination, or call OnArrived if done.
private void AdvanceHop( PlayerBot bot )
{
    var hops = bot.ActivityState.WaypointHops;

    if ( hops != null && hops.Count > 0 )
    {
        hops.Dequeue();
        if ( hops.Count > 0 )
            bot.ActivityState.TravelDestination = hops.Peek().Location;
        else
            bot.OnArrived();
    }
    else
    {
        bot.OnArrived();
    }
}
```

Extract the combat-interrupt block (PK scan + aggressor check) into a private `CheckTravelCombatInterrupt(PlayerBot bot)` helper so `DoActivityTravel` stays readable.

---

## 10. GM Tooling

New file `Scripts/Commands/NavBuildCommand.cs`. Two command groups: `[navbuild` for authoring the graph in-game, and read-only diagnostics (`[navtest`, `[navshow`, `[navbot`).

### `[navbuild` — In-Game Authoring Tool

This is the primary workflow for building the graph. You never transcribe coordinates manually. Stand at the location you want, type the command, done.

#### `[navbuild addnode <name> [routing]`

Adds a node at your current position. Appends the node to `Data/NavGraph.xml` and rebuilds the in-memory graph. Spawns a `NavNodeMarker` item at the position (visible on screen, auto-deletes after 60 seconds) as confirmation. The optional `routing` flag marks it `RoutingOnly=true` — graph hop only, never a bot destination.

```
[navbuild addnode RoadNorthMidpoint routing
→ Added routing node "RoadNorthMidpoint" at (1480, 1350, 12). Graph rebuilt (147 nodes).
```

#### `[navbuild connect [nameA nameB]`

Two modes:

- **Targeting mode** (no args): enters a two-step targeting sequence. Click one `NavNodeMarker`, then click another. Use when both nodes are visible on screen.
- **Name mode** (two args): connects by name with no targeting needed. Use when nodes are far apart — you don't need to see either one.

```
[navbuild connect RoadNorthMidpoint CompassionCrossroads
→ Connected "RoadNorthMidpoint" ↔ "CompassionCrossroads" (dist: 287 tiles). Graph rebuilt.
```

Both modes warn if the edge exceeds 300 tiles.

#### `[navbuild remove <name>`

Removes a node and all its edges from `NavGraph.xml`. Rebuilds the graph.

#### `[navbuild show [range]`

Spawns `NavNodeMarker` items at all nodes within `range` tiles (default 600). Fires a burst of particle effects along each edge for ~15 seconds. Use this to visually verify that edges don't cross water or mountains.

#### `[navbuild rebuild`

Reloads `NavGraph.xml` from disk and rebuilds the in-memory graph. Use after manually editing the XML file.

#### `[navbuild export`

Dumps the entire graph as C# `Add()` and `Connect()` calls to the GM's journal. Useful for archiving the graph in source control as code, or for diffing changes.

### `NavNodeMarker` item

Non-serialized temporary item placed at node positions by `[navbuild addnode` and `[navbuild show`. Custom item class (`Scripts/Items/Misc/NavNodeMarker.cs`) that:
- Overrides `Serialize` to do nothing (never saved to world)
- Displays the node name on single-click
- Auto-deletes via a 60-second `Timer` after spawning
- Is `Movable = false`

### `[navtest <from> <to>`

Computes the route between two named landmarks and prints each hop with distance to the GM's journal. No in-world effect — pure diagnostic.

```
[navtest Britain Minoc
→ Route (6 hops, ~1090 tiles):
    Britain → RoadNorthBrit1 (196 tiles)
    RoadNorthBrit1 → RoadNorthBrit2 (250 tiles)
    RoadNorthBrit2 → CompassionCrossroads (381 tiles)  ← WARN: > 300
    CompassionCrossroads → MtKendallApproach (290 tiles)
    MtKendallApproach → MtKendallPass (214 tiles)
    MtKendallPass → MinocSouthRoad (254 tiles)
    MinocSouthRoad → Minoc (265 tiles)
```

Any hop exceeding 300 tiles is flagged — insert an intermediate node at the midpoint and re-run.

### `[navshow`

Same as `[navbuild show` but read-only alias for quick visualization.

### `[navbot <botname>`

Reports which hop a named bot is currently on, its `WaypointHops` queue contents, and its `StuckTicks` count. Useful for watching a bot mid-travel without following it.

---

## 11. Implementation Sequence

Work through these in order. Each step is independently testable before moving to the next.

**Step 1 — Build the authoring tool first**
- Implement `NavNodeMarker.cs` (the temporary visual item).
- Implement `[navbuild addnode`, `[navbuild connect` (both modes), `[navbuild show`, `[navbuild rebuild`.
- Create a minimal `Data/NavGraph.xml` with just Britain and one routing node.
- Verify the tool works: add a node, connect it, `[navbuild show` shows it in-game.

**Step 2 — Extend data structures, no behavior change**
- Add `RoutingOnly` field to `BotWaypoint`.
- Add `s_Edges` and wire `BuildGraph()` to read `NavGraph.xml`.
- Implement `[navtest` and `[navshow`.
- Port the seed edge list (section 6) into `NavGraph.xml` using `[navbuild` or by editing the XML directly.
- Visually verify every seed edge with `[navshow`. Fix any edge that crosses water or a mountain.

**Step 3 — Add `ComputeRoute()`**
- Implement A\*, `NearestNode()`, `ReconstructPath()`.
- Test exhaustively with `[navtest` for every common city pair.
- Any hop flagged over 300 tiles: use `[navbuild addnode` at the midpoint and `[navbuild connect` to split it.
- No bot behavior changes yet.

**Step 4 — Wire `ActivityState` and `TravelToRandom()`**
- Add `WaypointHops`, `FinalDestination`, `SetTravelRoute()`, `SetTravelDirect()` to `ActivityState`.
- Update `TravelToRandom()` to call `ComputeRoute()` and branch on result.
- Bots now pick a route and store it. `DoActivityTravel` still navigates to `TravelDestination` (now the first hop). Partial improvement is immediately visible.

**Step 5 — Rewrite `DoActivityTravel`**
- Add `AdvanceHop()` helper.
- Replace stuck-abandon logic with stuck-skip.
- Adapt unobserved-teleport to advance one hop at a time.
- Test: force a bot to travel Britain→Minoc while watching. Watch the hop sequence via `[navbot`. Confirm no water-running.

**Step 6 — Dense coverage pass**
- Walk every major road in Britannia. Add nodes at every junction, pass, and bridge using `[navbuild addnode`. Connect as you go.
- Target 200–300 nodes total for mainland.
- Re-verify problem corridors with `[navtest` after adding new nodes.

**Step 7 — Field test all major routes**
- Britain → Trinsic
- Britain → Yew
- Britain → Vesper
- Yew → Skara Brae
- Vesper → Minoc
- Britain → Dungeon (Despise, Shame, Destard)
- Any island destination (confirm falls back to direct+teleport gracefully)

**Step 8 — Edge audit and cleanup**
- After watching bots travel for a session, note any consistent stuck points.
- Insert routing nodes or adjust coordinates to route around obstacles.
- Re-verify with `[navbuild show` after any graph changes.

---

## 12. What This Does Not Solve

**Island towns (Jhelom, Moonglow, Nujel'm, Magincia):** No mainland graph edges. `ComputeRoute()` returns `null`; `SetTravelDirect()` falls back to unobserved teleport. A future boat/moongate mechanic would allow proper island routing.

**Dungeon interiors:** The nav graph covers the overworld surface only. Dungeon entrance nodes get bots to the door. Interior distances are short enough that the existing `MovementPath` pathfinder handles individual corridor segments. Multi-floor navigation (stairs, ladders) is out of scope.

**Shops:** Not a problem. Shops are inside towns. The graph routes bots to the town node; the existing short-range `MovementPath` handles the final 50–150 tiles to the vendor. No special support needed.

**Dynamic obstacles:** A player house placed on a road hop causes stuck-detection to trigger and skip that hop. The bot resumes from the next waypoint. Acceptable behavior.

**Serialization:** `WaypointHops` is runtime-only — no serialization needed. If the server saves mid-journey, the bot re-calls `TravelToRandom()` on next AI tick and picks a new route.

**Lost Lands access:** Bots rolled for Delucia/Papua still teleport when unobserved (correct — there's no walkable overworld connection). A future improvement could add T2A passage teleporter nodes as graph bridges between mainland and Lost Lands clusters.
