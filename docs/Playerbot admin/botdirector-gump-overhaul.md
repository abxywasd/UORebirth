# BotDirector GM Menu — Full Implementation Plan

## Current State

`PlayerBotDirectorGump.cs` is a single flat panel (420×320) with seven actions:
enable/disable toggle, ±10 target count, live count display, spawn-at-location,
hire 10 nearby, dismiss controlled, and delete-all.

`PlayerBotDirector.cs` only exposes `Enabled`, `TargetBotCount`, `GetLiveBots()`,
`SpawnOneBot()`, `RegisterBot()`, `UnregisterBot()`.

---

## Feature List (Complete)

### Tab 1 — Dashboard (enhanced overview)
- Enabled state with toggle
- Target bot count with ±1 / ±10 controls (not just ±10)
- Live count split: **Regular** | **Encounter** | **Controlled**
- Persona breakdown: PK N | Crafter N | Adventurer N
- Activity breakdown: Wandering N | Traveling N | Combat N | Crafting N | Other N
- Active group count
- Quick actions: Spawn 1 here, Hire 10 nearby, Dismiss mine
- Destructive quick actions: Delete encounter bots, Delete regular bots, Delete ALL

### Tab 2 — Bot List (paginated)
- 10 rows per page, each row showing: Name | Persona | XP Level | Activity | HP% | Map | Coords
- Per-row buttons: **[Goto]** **[Kill]** **[Props]** **[Hire/Release]**
- Pagination controls: Prev / Next / "Page X of Y"
- Filter buttons: All | PKs | Crafters | Adventurers | Encounter
- Sort options: Name | Persona | Activity | HP

### Tab 3 — Spawn Controls
- Spawn count selector (1–50, ±1 / ±10)
- Persona selector: Random | PlayerKiller | Crafter | Adventurer
- Experience selector: Random | Newbie | Average | Proficient | Grandmaster
- **[Spawn N at My Location]** — respects persona/XP selections
- **[Spawn N at Random POI]** — picks any under-cap POI
- **[Clear Encounter Bots]** — deletes only IsEncounterBot == true
- **[Clear Regular Bots]** — deletes non-encounter bots
- **[Delete ALL]** — full purge with red label

### Tab 4 — POI Management
- Table of all POIs: Name | Type | Current/Max | Map | Coords
- Per-POI buttons: **[Goto]** **[Force Spawn 1]** **[Clear Bots]**
- Header: total POI slots filled vs. capacity

### Tab 5 — Groups
- List of all active `PlayerBotGroup` instances
- Per group: Leader name | Member count | Activity
- Per-group buttons: **[Goto Leader]** **[Disband]**
- **[Form Group Near Me]** — calls `PlayerBotGroup.TryForm` near GM

### Tab 6 — Settings
All settings are serialized on `PlayerBotDirector` and persist across restarts.
- **POI bot despawn timeout** (default 15 min): ±1 min
- **Encounter bot despawn timeout** (default 8 min): ±1 min
- **Encounter chance per tick** (default 40%): ±5%
- **Encounter tick interval** (default 15 sec): ±5 sec
- **POI tick interval** (default 2 min): ±1 min
- **Director tick interval** (default 1 min): ±1 min
- **Max bots per burst tick** (default 5): ±1
- **Observation radius** (default 18 tiles): ±2 tiles
- **[Reset to Defaults]** — restores all above to listed defaults

---

## Architecture

### Gump Structure

Replace the single class with a tabbed design. Pass `m_Tab` and pagination state
as constructor parameters so each action button re-opens the gump on the same tab.

```
PlayerBotDirectorGump(Mobile from, PlayerBotDirector dir,
                      int tab = 0, int botPage = 0,
                      int botFilter = 0, int botSort = 0,
                      int spawnCount = 1, int spawnPersona = -1, int spawnXP = -1)
```

Gump size: **600 × 500**. Tab bar across the top (y=10–50). Content area below (y=60–490).

#### Button ID Ranges

| Range | Owner |
|-------|-------|
| 1–6 | Tab navigation (1=Dashboard … 6=Settings) |
| 100–119 | Dashboard actions |
| 200–209 | Bot list: Goto row 0-9 |
| 210–219 | Bot list: Kill row 0-9 |
| 220–229 | Bot list: Props row 0-9 |
| 230–239 | Bot list: Hire/Release row 0-9 |
| 250–251 | Bot list: Prev / Next page |
| 260–264 | Bot list: Filter (All/PK/Crafter/Adventurer/Encounter) |
| 270–273 | Bot list: Sort (Name/Persona/Activity/HP) |
| 300–329 | Spawn tab controls |
| 400–459 | POI tab (Goto/Spawn/Clear per slot, max 15 POIs) |
| 500–549 | Groups tab (Goto/Disband per slot, max 25 groups) |
| 550 | Groups tab: Form Group Near Me |
| 601–620 | Settings: decrement each of 8 settings |
| 621–640 | Settings: increment each of 8 settings |
| 650 | Settings: Reset to Defaults |

---

## Required Changes to `PlayerBotDirector.cs`

### New Serialized Fields

```csharp
private int m_PoiTimeoutMinutes;      // default 15
private int m_EncounterTimeoutMinutes; // default 8
private int m_EncounterChancePct;     // default 40
private int m_EncounterTickSeconds;   // default 15
private int m_PoiTickMinutes;         // default 2
private int m_DirectorTickMinutes;    // default 1
private int m_MaxBurstPerTick;        // default 5
private int m_ObservationRadius;      // default 18
```

All exposed with `[CommandProperty(AccessLevel.GameMaster)]`. Setters for tick-interval
fields must stop/restart the relevant timer:
- `EncounterTickSeconds` setter → restarts `m_EncounterTimer`
- `PoiTickMinutes` setter → restarts `m_POITimer`
- `DirectorTickMinutes` setter → restarts `m_DirectorTimer`

Bump serialization to **version 1**, write all 8 fields after the existing v0 fields.
`Deserialize` reads them with their defaults on v0 load.

Replace hardcoded literals in `DirectorTick`, `EncounterTick`, `BurstSpawnTick`,
`StartDirectorTimer` with the new fields.

### New Query Methods

```csharp
public List<PlayerBot> GetEncounterBots()
public List<PlayerBot> GetRegularBots()
public List<PlayerBot> GetBotsByPersona(BotPersona persona)
public List<PlayerBot> GetBotsByActivity(BotActivity activity)
public List<PlayerBot> GetControlledBots()
public List<PlayerBotGroup> GetActiveGroups()
public int DeleteEncounterBots()   // returns deleted count
public int DeleteRegularBots()     // returns deleted count
```

### New Spawn Overload

```csharp
public PlayerBot SpawnOneBot(Point3D? location, BotPersona persona, BotExperience xp)
```

Existing `SpawnOneBot(Point3D?)` calls the new overload with random persona/xp (current behavior).

Add:

```csharp
public int SpawnNBots(int count, Point3D? location, BotPersona persona, BotExperience xp)
```

---

## Implementation Plan (Phases)

### Phase 1 — Gump Skeleton Refactor

**File:** `PlayerBotDirectorGump.cs`

1. Add constructor parameters: `tab`, `botPage`, `botFilter`, `botSort`,
   `spawnCount`, `spawnPersona`, `spawnXP`.
2. Store as private fields.
3. Expand gump to 600×500.
4. Draw tab bar at top. Active tab uses a highlighted background (button image 2445
   vs 2444 pressed/unpressed). Tab labels: Dashboard | Bot List | Spawn | POIs | Groups | Settings.
5. Wire tab buttons 1–6 to re-open the gump on the target tab (botPage resets to 0
   when switching away from Bot List).
6. Move existing Dashboard content under `if (m_Tab == 0)`.
7. Verify compile — stub the other 5 tabs as `AddLabel(20, 80, 0, "(coming soon)")`.

### Phase 2 — Dashboard Tab Enhancements

**File:** `PlayerBotDirectorGump.cs`, `PlayerBotDirector.cs`

1. Add `GetControlledBots()` and the per-persona/per-activity breakdown methods to
   the Director.
2. Layout (x=20, y=70 baseline, row height=20):
   ```
   Enabled: YES/NO          [Toggle]
   Target:  N               [-1] [-10] [+10] [+1]
   Regular: N  Encounter: N  Controlled: N
   
   ── Personas ──
   PlayerKillers: N   Crafters: N   Adventurers: N
   
   ── Activities ──
   Wandering: N  Traveling: N  Combat: N  Crafting: N  Other: N
   
   ── Groups ──
   Active groups: N
   
   ── Quick Actions ──────────────────────
   [Spawn 1 Here]  [Hire 10 Nearby]  [Dismiss Mine]
   [Del Encounters]  [Del Regular]  [DEL ALL *]
   ```
3. Button IDs 100–119 for dashboard actions (see table above).

### Phase 3 — Bot List Tab

**File:** `PlayerBotDirectorGump.cs`

1. At tab entry, collect bots from Director:
   - Apply filter (m_BotFilter: 0=All, 1=PK, 2=Crafter, 3=Adventurer, 4=Encounter).
   - Apply sort (m_BotSort: 0=Name, 1=Persona, 2=Activity, 3=HP).
   - Page into windows of 10.
2. Filter button row at y=70: **[All] [PKs] [Crafters] [Adventurers] [Encounter]**
   Selected filter gets a different label hue (0x40).
3. Sort button row at y=95: **[Name] [Persona] [Activity] [HP]**
4. Column headers at y=120: Name | Persona | XP | Activity | HP | Map | Coords
5. Rows y=140 + (i×30), 10 rows:
   - Name (truncate at 14 chars)
   - Persona abbrev (PK / Craft / Adv)
   - XP abbrev (New / Avg / Prof / GM)
   - Activity (Wander / Travel / Combat / …)
   - HP% as colored label (green >50%, yellow >25%, red ≤25%)
   - Map (Fel / Tram / etc.)
   - X,Y coords
   - [Goto] [Kill] [Props] [Hire] or [Rel] (hire if uncontrolled, release if controlled)
6. Pagination bar at y=455: **[< Prev]  Page X of Y  [Next >]**

**OnResponse** cases:
- **Goto (200+i)**: Resolve bot from filtered+paged list at slot i. Call
  `m_From.MoveToWorld(bot.Location, bot.Map)`. Re-open gump.
- **Kill (210+i)**: `bot.Delete()`. Re-open gump (page may decrease if last on page).
- **Props (220+i)**: `m_From.SendGump(new PropertiesGump(m_From, bot))`. Re-open gump.
- **Hire/Release (230+i)**: If uncontrolled call `bot.AddHire(m_From)`;
  if controlled call `bot.SetControlMaster(null)` + set Wandering. Re-open.
- **Prev (250)**: `m_BotPage = Math.Max(0, m_BotPage - 1)`. Re-open.
- **Next (251)**: `m_BotPage++` (constructor clamps to valid range). Re-open.
- **Filter (260–264)**: Set `m_BotFilter`, reset `m_BotPage = 0`. Re-open.
- **Sort (270–273)**: Set `m_BotSort`. Re-open.

> **Note:** The filtered/sorted list must be rebuilt identically in OnResponse to
> map button slot → bot correctly. Store nothing on the instance; rebuild from
> Director every time.

### Phase 4 — Spawn Tab

**File:** `PlayerBotDirectorGump.cs`, `PlayerBotDirector.cs`

1. Add `SpawnNBots` and the persona/XP overload to Director.
2. Layout:
   ```
   Spawn count:  N          [-1] [-10] [+10] [+1]
   
   Persona:  [Random] [PlayerKiller] [Crafter] [Adventurer]
   
   XP Level: [Random] [Newbie] [Average] [Proficient] [Grandmaster]
   
   [Spawn N at My Location]
   [Spawn N at Random POI]
   
   ── Bulk Removal ───────────────────────────
   [Clear Encounter Bots]   [Clear Regular Bots]
   [DELETE ALL  (irreversible)]
   ```
3. Selected persona/XP buttons use hue 0x40; unselected use 0 (white).
4. Button IDs 300–329. Spawn count clamped 1–50 in Director.

### Phase 5 — POI Tab

**Files:** `PlayerBotDirectorGump.cs`, `PlayerBotPOI.cs` (read-only changes)

1. Call `PlayerBotPOI.AllPOIs` (static list — may need to be made `public static`
   if it isn't already).
2. For each POI, count live bots within `poi.SpawnRadius` tiles of `poi.Location`
   by scanning the bot list. This is O(bots × POIs) but tolerable for GM use.
3. Layout (scrollable via pagination if > 12 POIs):
   ```
   # | Name             | Type     | Curr/Max | Map | Coords    | Actions
   1 | Britain          | Town     |  4/6     | Fel | 1445,1599 | [Goto][Spawn][Clear]
   2 | Trinsic          | Town     |  2/6     | Fel | 1827,2750 | [Goto][Spawn][Clear]
   …
   ```
4. **Goto**: GM teleports to poi.Location.
5. **Force Spawn**: Calls `Director.SpawnOneBot(poi.RandomSpawnPoint(), random, random)`
   and registers result; spawns up to `poi.MaxBots - current` bots.
6. **Clear**: Finds all registered bots within the POI radius and deletes them.

> If `AllPOIs` is not yet public, add a `public static IReadOnlyList<BotPOI> AllPOIs`
> property to `PlayerBotPOI`.

### Phase 6 — Groups Tab

**Files:** `PlayerBotDirectorGump.cs`, `PlayerBotGroup.cs`

1. Add `public static List<PlayerBotGroup> ActiveGroups` (static list maintained
   by `PlayerBotGroup` on `TryForm`/`Disband`). Or add `GetActiveGroups()` to Director
   that scans live bots for non-null `bot.Group` and de-dupes.
2. Layout (10 per page):
   ```
   # | Leader           | Members | Activity
   1 | Gorash the Bold  |   3     | Combat     [Goto] [Disband]
   2 | Yuria            |   2     | Wandering  [Goto] [Disband]
   …
   [Form Group Near Me]
   ```
3. **Goto**: teleport GM to group leader's location.
4. **Disband**: call `group.Disband()` (add this method to `PlayerBotGroup` if missing).
5. **Form Group Near Me**: `PlayerBotGroup.TryForm(nearbyBot, 15)` — pick any ungrouped
   uncontrolled bot within 15 tiles of GM.

### Phase 7 — Settings Tab + Director Persistence

**Files:** `PlayerBotDirectorGump.cs`, `PlayerBotDirector.cs`

1. Add the 8 new fields to `PlayerBotDirector` (see New Serialized Fields section).
2. Replace all hardcoded values in Director methods with these fields.
3. Settings layout:
   ```
   POI bot despawn timeout (min):      15    [-1] [+1]
   Encounter bot despawn timeout (min): 8    [-1] [+1]
   Encounter chance per tick (%):       40   [-5] [+5]
   Encounter tick interval (sec):       15   [-5] [+5]
   POI tick interval (min):             2    [-1] [+1]
   Director tick interval (min):        1    [-1] [+1]
   Max bots per burst tick:             5    [-1] [+1]
   Observation radius (tiles):          18   [-2] [+2]
   
                           [Reset to Defaults]
   ```
4. Each decrement/increment button re-opens the gump. Min/max clamps:
   - All timeouts: 1–60 min
   - Encounter chance: 5–100%
   - Tick intervals: 5 sec–300 sec for encounter; 1–30 min for others
   - Burst per tick: 1–20
   - Observation radius: 5–50 tiles
5. Bump `Serialize` version to 1. `Deserialize` v0 reads only existing fields then
   applies defaults for the 8 new ones.

---

## Implementation Notes

### PropertiesGump import
`PlayerBotDirectorGump.cs` will need `using Server.Gumps;` (already present) but
also ensure `PropertiesGump` is accessible — it lives in `RunUO-2.1/Server/Gumps/PropertiesGump.cs`.
In RunUO the standard idiom is:
```csharp
m_From.SendGump(new PropertiesGump(m_From, bot));
```

### Filtering/sorting in OnResponse
The filtered-and-sorted bot list must be deterministically rebuilt in `OnResponse`
using the same parameters stored in `m_BotFilter` and `m_BotSort`. Never cache it on
the gump instance — gumps are reconstructed each response. Pattern:

```csharp
private List<PlayerBot> GetFilteredBots()
{
    List<PlayerBot> all = m_Director.GetLiveBots();
    // filter
    List<PlayerBot> filtered = new List<PlayerBot>();
    foreach (PlayerBot bot in all)
    {
        if (m_BotFilter == 0) { filtered.Add(bot); continue; }
        if (m_BotFilter == 1 && bot.PlayerBotProfile == BotPersona.PlayerKiller) filtered.Add(bot);
        if (m_BotFilter == 2 && bot.PlayerBotProfile == BotPersona.Crafter)      filtered.Add(bot);
        if (m_BotFilter == 3 && bot.PlayerBotProfile == BotPersona.Adventurer)   filtered.Add(bot);
        if (m_BotFilter == 4 && bot.IsEncounterBot)                               filtered.Add(bot);
    }
    // sort
    filtered.Sort((a, b) => {
        switch (m_BotSort) {
            case 1: return a.PlayerBotProfile.CompareTo(b.PlayerBotProfile);
            case 2: return a.ActivityState.CurrentActivity.CompareTo(b.ActivityState.CurrentActivity);
            case 3: return a.Hits.CompareTo(b.Hits);
            default: return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }
    });
    return filtered;
}
```

Use the same method in the constructor (for display) and in `OnResponse` (for resolving button slot → bot).

### POI bot-count query
```csharp
private int CountBotsNearPOI(BotPOI poi)
{
    int count = 0;
    foreach (PlayerBot bot in m_Director.GetLiveBots())
    {
        if (bot.Map == poi.Map &&
            bot.GetDistanceToSqrt(poi.Location) <= poi.SpawnRadius)
            count++;
    }
    return count;
}
```

### Group de-duplication
```csharp
private List<PlayerBotGroup> GetDistinctGroups()
{
    HashSet<PlayerBotGroup> seen = new HashSet<PlayerBotGroup>();
    foreach (PlayerBot bot in m_Director.GetLiveBots())
        if (bot.Group != null) seen.Add(bot.Group);
    return new List<PlayerBotGroup>(seen);
}
```

### Destructive action confirmation
"Delete ALL" and "Clear Regular Bots" open a `GenericConfirmGump` (RunUO built-in)
or a simple two-button inline gump before executing. Implement as a small inner class
`BotDeleteConfirmGump` that takes a callback enum:
```csharp
private enum ConfirmAction { DeleteAll, DeleteRegular, DeleteEncounter, ClearPOI }
```

---

## File Change Summary

| File | Change Type |
|------|-------------|
| `PlayerBotDirectorGump.cs` | Full rewrite (same class, tabbed) |
| `PlayerBotDirector.cs` | New fields + new query/spawn methods + serialization v1 |
| `PlayerBotPOI.cs` | Make `AllPOIs` public static (if not already) |
| `PlayerBotGroup.cs` | Add `Disband()` method + static `ActiveGroups` list (if needed) |

No new files required.

---

## Open Questions (resolve before implementing)

1. Does `BotPOI` have a `Name` string field, or must we infer it from coordinates?
   Check `PlayerBotPOI.cs` — if no name, add one.
2. Does `PlayerBotGroup` already have a `Disband()` method? The explore notes only
   mention "disband when empty" — a public `Disband()` callable by the GM may need
   adding.
3. Does `PlayerBot.AddHire(Mobile)` work correctly when called by a GM on a bot that
   already has a different ControlMaster? Verify before wiring the Hire button.
4. `GetDistanceToSqrt` returns a float — compare against `(float)poi.SpawnRadius`
   for the POI bot-count query, not against the integer.
