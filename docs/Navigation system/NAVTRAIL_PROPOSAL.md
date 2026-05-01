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

---

## Bot Follow Mode

### Problem

After laying nodes, you need to verify bots actually traverse them correctly. Watching a bot navigate from a distance is hard — they move at run speed, change direction, and may silently fall back to direct travel when the graph fails. Without sticking to a specific bot, it's impossible to observe subtle pathing failures (bridge stumbles, gap hang-ups, stuck loops).

### Command

```
[botfollow <botName>
```

**Toggle.** First call starts following the named bot; second call (any arguments or none) stops it.

- Finds the nearest `PlayerBot` whose `Name` matches `<botName>` (case-insensitive).
- GM becomes hidden if not already (no footstep sounds, no overhead name — observer mode).
- A server-side timer fires every **500 ms**, teleporting the GM to a fixed offset 3 tiles behind the bot at the same Z. The GM's client sees smooth movement because the offset is small and frequent.
- Every **2 seconds**, a private system message is sent to the GM showing the bot's current nav state (see Display below).
- Auto-detaches when the bot reaches its destination, dies, or is deleted.

### State Display (while following)

Every 2 seconds, the GM receives a private system message in this format:

```
[Kira] Travel → Minoc | Hop 4/7: NorthRoad_02 → NorthRoad_03 | 187 tiles
[Kira] Combat — target: Orc #A3B1 | idle nav
[Kira] Idle at Britain | no travel queued
[Kira] STUCK — no movement for 8s | last hop: RoadNorthBrit1 → RoadNorthBritlMid
```

Fields:
- Bot name
- Current activity (`Travel`, `Combat`, `Idle`, `STUCK`)
- For travel: `destination | Hop N/Total: currentNode → nextNode | distance to next node`
- STUCK flag triggers after 8 seconds without coordinate change during travel

### State Machine

```csharp
private static Dictionary<Mobile, BotFollowSession> s_ActiveFollows
    = new Dictionary<Mobile, BotFollowSession>();

private class BotFollowSession {
    public PlayerBot  Target;
    public Timer      FollowTimer;
    public Point3D    LastPosition;   // for stuck detection
    public DateTime   LastMoveTime;
    public int        StatusTickCount; // fires status message every 4 ticks (2s at 500ms)
}
```

Multiple GMs can follow different bots simultaneously. One GM follows one bot at a time — starting a new follow auto-stops the previous one.

### Auto-detach Conditions

| Condition | Behavior |
|---|---|
| Bot arrives at destination | Detach, notify GM: `[Kira] reached Minoc. Follow ended.` |
| Bot deleted or killed | Detach, notify GM |
| GM disconnects | Session cleaned up on next tick |
| GM types `[botfollow` again (any args) | Detach immediately |
| Bot stuck > 30 seconds | Detach with warning: `[Kira] stuck >30s — follow ended. Consider adding a node here.` |

The stuck-30s detach is intentional: it flags a graph problem right at the failure point while the GM is physically present to drop a fix with `[navdrop`.

---

## Bot Inspection Commands

Companion commands for diagnosing individual bots without needing to follow in real time.

### `[botinfo <botName>`

Prints a full snapshot of the bot's current state to the GM:

```
[botinfo Kira
  Name:     Kira
  Location: (1352, 1228, 0) Felucca
  Activity: Travel
  Destination: Minoc
  Waypoint queue: RoadNorthBrit1 → NorthRoad_01 → NorthRoad_02 → GreatNorthernRoad → ... (7 hops)
  Next hop:  NorthRoad_01 at (1352, 1228) — 123 tiles
  Persona:   PlayerKiller (Veteran)
  Target:    none
```

Useful for a quick check without committing to a follow session.

### `[botpause <botName>`

Suspends the bot's AI tick — it freezes in place. The bot does not wander, attack, or move. Useful to examine where exactly in a route the bot stopped, or to inspect its state without it running away mid-sentence.

```
[botpause Kira
  Kira paused. Use [botresume Kira to continue.
```

### `[botresume <botName>`

Resumes a paused bot.

```
[botresume Kira
  Kira resumed.
```

---

## Combined Workflow: Authoring + Verification Loop

The trail and follow commands are designed to be used together in a tight loop:

1. **Author a segment** using trail mode while walking the route yourself.
2. **Find a bot** near the start of the segment and issue its travel command (or wait for it to naturally head that way).
3. **Follow it** with `[botfollow <name>` — you teleport with it through every hop.
4. **Spot a failure** — the bot hesitates at a gap, stumbles at a bridge, gets stuck.
5. **Drop a fix** — while physically present at the failure point, type `[navdrop` (trail must be active) or `[navbuild connect` to wire in a missing edge.
6. **Resume and re-verify** — `[botresume`, watch the bot re-attempt the fixed segment.

---

## Implementation Scope (Updated)

| File | Changes |
|---|---|
| `Scripts/Commands/NavBuildCommand.cs` | Trail mode: `TrailSession` class, `DoTrailStart/Drop/End/Cancel/Status`. Register `[navdrop` alias. ~150 lines. |
| `Scripts/Commands/BotFollowCommand.cs` | New file. `BotFollowSession` class, follow timer, state display, stuck detection. `[botfollow`, `[botinfo`, `[botpause`, `[botresume`. ~200 lines. |
| `Scripts/Mobiles/PlayerBot/PlayerBotAI.cs` | Expose current activity, destination name, and hop queue as readable properties for `[botinfo` display. ~20 lines. |
| `Data/NavGraph.xml` | No changes. |
| `Scripts/Mobiles/PlayerBot/PlayerBotNavigator.cs` | No changes. |

**Total estimated additions: ~370 lines of C#.**
