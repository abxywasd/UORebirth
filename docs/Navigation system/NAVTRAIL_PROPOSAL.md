# NavBuild Trail Mode — Proposal

## Problem

Laying out a route requires naming every node and manually wiring connections. Current workflow per node:

1. Walk to position
2. Invent a unique name
3. `[navbuild addnode <name> routing`
4. `[navbuild connect <prevNode> <name>`
5. Repeat

Steps 2 and 4 are the friction. Across 5–10 nodes on one road segment, this is slow and error-prone.

---

## Proposed Solution: Trail Mode

A **trail** is a temporary recording session. You walk your route and spam one short command. The system handles naming and connections automatically.

### Commands

```
[navbuild trail start <prefix> [fromNode]
```
Begin a trail. `<prefix>` is the base name for auto-generated nodes (`NorthRoad` → `NorthRoad_01`, `NorthRoad_02` …). Optional `fromNode` anchors the first drop to an existing landmark.

```
[navbuild trail drop
```
The **spam command**. Drops a `routing` node at your feet, auto-names it, connects it to the previous drop. Prints distance from last node, warns if > 300 tiles.

```
[navbuild trail end [toNode]
```
Finalize the trail. Optional `toNode` connects the last drop to an existing landmark. Saves XML, rebuilds graph, prints a full summary.

```
[navbuild trail cancel
```
Discard — removes all XML elements added during this session and rebuilds.

```
[navbuild trail status
```
Shows current prefix, drop count, last node name, last node coords, and distance to your current position.

---

## Example Workflow

Walking the Britain → Minoc northern road:

```
[navbuild trail start NorthRoad RoadNorthBrit1
  Trail started. Prefix: NorthRoad. Anchored from: RoadNorthBrit1 (1310, 1350)

[walk north to first waypoint]

[navbuild trail drop
  Dropped NorthRoad_01 at (1352, 1228). RoadNorthBrit1 <-> NorthRoad_01 (123 tiles)

[walk further north]

[navbuild trail drop
  Dropped NorthRoad_02 at (1424, 1109). NorthRoad_01 <-> NorthRoad_02 (137 tiles)

[walk to GreatNorthernRoad area]

[navbuild trail end GreatNorthernRoad
  Connected: NorthRoad_02 <-> GreatNorthernRoad (76 tiles). Trail saved. 2 nodes dropped.
  Route: RoadNorthBrit1 → NorthRoad_01 → NorthRoad_02 → GreatNorthernRoad
```

---

## State Machine

Trail state lives as a static field on `NavBuildCommand` — one trail active server-wide:

```csharp
private static TrailSession s_ActiveTrail = null;

private class TrailSession {
    public string Prefix;
    public int    Counter;
    public string LastNodeName;      // null = anchored from existing node only
    public List<string> DroppedNodes; // for cancel rollback
}
```

`trail start` while a trail is already active prints the current status and asks for `trail cancel` first.

---

## Safety Guards

| Situation | Behavior |
|---|---|
| Drop < 30 tiles from last | Skip, warn "too close — move further before dropping" |
| Drop > 300 tiles from last | Allow, print `WARN: large gap` |
| Auto-name already exists in XML | Skip ahead to next counter slot |
| `trail end` with 0 drops | Abort, tell user to `trail cancel` |
| Server restart mid-trail | Session lost; already-saved XML nodes persist as orphans — run `[navbuild edges <lastSavedNode>` to verify |

---

## Optional: `[navdrop` Alias

Register a standalone `[navdrop` command that calls `DoTrailDrop()` directly. Then the spam loop is a single word with no subcommand:

```
[navdrop
[navdrop
[navdrop
```

This is the fastest possible input — just `[navdrop` every time you reach a waypoint position.

---

## Implementation Scope

Changes required:

- **`NavBuildCommand.cs`** — Add `TrailSession` inner class, static `s_ActiveTrail`, and handlers: `DoTrailStart`, `DoTrailDrop`, `DoTrailEnd`, `DoTrailCancel`, `DoTrailStatus`. Extend the `trail` subcommand to dispatch to these. Optionally register `[navdrop` as a second command entry point.
- **`NavGraph.xml`** — No changes needed; `DoTrailDrop` appends nodes and edges exactly like `DoInsert` does today.
- **`PlayerBotNavigator.cs`** — No changes needed.

Estimated additions: ~150 lines of C#.
