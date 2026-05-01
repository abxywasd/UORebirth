# Bugfix Plan: "Cleanup: Detected N inaccessible items" on Startup

## Summary

The `Cleanup: Detected 8247 inaccessible items, removing..` message is produced by
`Scripts/Misc/Cleanup.cs` ~2.5 s after every server start. The items flagged are real
`BaseWeapon`, `BaseArmor`, `BaseClothing`, boots/shoes, food, potions, etc. (`IsBuggable`
returns true) that exist in `World.Items` but meet all three orphan criteria:

```
item.Parent   == null
item.Map      == Map.Internal
item.Location == Point3D.Zero
```

There are **two distinct root causes**: one historical, one actively introduced by the
PlayerBot system.

---

## Root Cause 1 — Historical 2005 world save (dominant source)

The `Saves/` directory ships with the September 2005 snapshot of the Rebirth shard.
Years of live-shard operation left thousands of items in the orphaned state above.
Every item constructor sets:

```csharp
// Item.cs line 4643
m_Map = Map.Internal;   // default before placement
```

and `m_Location` defaults to `Point3D.Zero`. Items that were never properly placed
(or whose parent mobile was deleted out-of-band during the original shard) remained
in the save file with those default values and `m_Parent = null`.

**Why the count doesn't decrease across restarts:**
Cleanup deletes the items at runtime, but a subsequent world save (`[save`) must
occur before shutdown for the cleaned state to be persisted. If the server is
restarted frequently during development without a manual save, the same 2005
orphans reappear on every boot.

**Fix:** After the first clean startup, issue `[save` from in-game (or let the
auto-save timer fire) before shutting down. Orphan count will drop to near zero
on subsequent startups.

---

## Root Cause 2 — PlayerBot spawn-then-delete pattern (active new-orphan source)

### The mechanism

`Item()` (base constructor) runs `World.AddItem(this)` immediately, before the item
is attached to anything. When `new PlayerBot()` is called, `InitOutfit()` and
`InitBackpack()` create **15–20 items** (weapons, armor, clothing, backpack
contents). Each item is registered in `World.Items` with:

```
Map      = Map.Internal  (set by DefaultMobileInit → Map = Map.Internal)
Location = Point3D.Zero
Parent   = bot (or backpack)    ← non-null, so Cleanup skips them
```

`Mobile.AddItem()` propagates the bot's current map to the item:

```csharp
// Mobile.cs line 6071
item.Parent = this;
item.Map = m_Map;   // Map.Internal at construction time
```

In three spawn code paths, a fully-constructed bot is immediately deleted when the
spawn location is in a guarded region:

```csharp
// PlayerBotDirector.cs — BurstSpawnTick, POITick, TrySpawnEncounter
PlayerBot bot = new PlayerBot();        // 15-20 items created, Map.Internal
if (inGuardedRegion && isPlayerKiller)
{
    bot.Delete();                        // items cascade-deleted
    continue;
}
bot.MoveToWorld(loc, poi.Map);          // items get Map = Felucca (never reached)
```

**In the common path** `Mobile.Delete()` properly cascades via
`m_Items[i].OnParentDeleted(this)` → `item.Delete()`, so items ARE deleted and no
orphans accumulate at runtime.

### The world-save race (persistent orphan source)

`World.OnDelete()` defers any deletion that occurs while `m_Saving == true`:

```csharp
// World.cs line 75
public static bool OnDelete(IEntity entity) {
    if (m_Saving || m_Loading) {
        _deleteQueue.Enqueue(entity);
        return false;           // caller returns early
    }
    return true;
}
```

When `bot.Delete()` is called during an active world save:
1. `World.OnDelete(bot)` returns `false` → `Mobile.Delete()` returns at line 3581,
   **before** iterating `m_Items`.
2. Only the **bot** is enqueued; its items are not.
3. The world save captures the bot (Map.Internal, Location.Zero) **and** all its
   items (Map.Internal, Location.Zero, Parent=bot serial).
4. After the save, the delete queue retries `mob.Delete()` — this time succeeds and
   cascades to items.

**Critical edge:** if the process crashes or is killed between steps 3 and 4, the
save is on disk but the post-save cleanup never ran. On the next startup:

- The bot is deserialized → it is alive, on Map.Internal at (0,0,0).
- Its items are deserialized → parent (the bot) is found → `Parent` is non-null →
  **Cleanup skips them**.
- `DirectorTick()` skips bots on Map.Internal:
  ```csharp
  if (bot.Map == null || bot.Map == Map.Internal) continue;
  ```
  So the ghost bot is never despawned.

This produces **ghost bots** that accumulate silently on Map.Internal. Their items
are not caught by the inaccessible-items cleanup, but the bots themselves are
inaccessible and wasting memory/save space.

### Secondary risk — crafted items without fallback deletion

`PlayerBotCrafter.DoCraftTick()` creates an item before committing it to the
backpack:

```csharp
Item made = PickCraftOutput(bot);   // World.AddItem() called, Map.Internal
bot.AddToBackpack(made);             // no check on return value
```

`Mobile.AddToBackpack()` returns `false` if `CheckAdd()` fails or the backpack is
somehow null at that instant. If it ever returns false, `made` remains in
`World.Items` with `Map.Internal`, `Location.Zero`, `Parent = null` — a true orphan
caught by `Cleanup`.

### Tertiary issue — stashed weapon not recovered after restart

`PlayerBotAI.m_StashedEquipment` is a plain `List<Item>` field on the AI class
(not serialized). When a mage bot moves its weapon to the backpack before casting
and the server restarts before `RestoreStashedWeapons()` runs:

- The weapon persists in the bot's backpack (saved correctly).
- `m_StashedEquipment` is null on the fresh `PlayerBotAI`.
- The weapon is never re-equipped; the bot fights unarmed indefinitely.

This does not produce orphaned items but is a correctness bug.

---

## Fix Plan

### Fix 1 — Eliminate the spawn-then-delete pattern (HIGH priority)

Pre-screen guarded regions **before** constructing a full bot. Since the bot's
profile (PlayerKiller vs not) is determined randomly by `InitPersona()`, the
simplest approach is to skip spawning probabilistically when the zone is guarded,
matching the 1-in-3 PK probability:

```csharp
// In BurstSpawnTick(), POITick(), TrySpawnEncounter():
bool isGuarded = Region.Find(spawnLoc, spawnMap) is GuardedRegion;
if (isGuarded && Utility.Random(3) == 0)
    continue;   // would have been a PK — skip without constructing

PlayerBot bot = new PlayerBot();
// Now the post-creation guard check can be removed entirely,
// or kept as a safety net with a cheap path:
if (isGuarded && bot.PlayerBotProfile == PlayerBotPersona.PlayerBotProfile.PlayerKiller)
{
    bot.Delete();
    continue;
}
```

This reduces the frequency of create-then-delete from every guarded-zone PK roll
to a rare fallback (only when the pre-screen guess was wrong).

**Longer-term:** expose a `PlayerBotPersona.RollProfile()` static helper so the
profile can be determined before construction, eliminating the fallback entirely.

### Fix 2 — Delete crafted items on backpack failure (MEDIUM priority)

In `PlayerBotCrafter.DoCraftTick()`, add a safety delete:

```csharp
Item made = PickCraftOutput(bot);
if (!bot.AddToBackpack(made))
    made.Delete();   // prevent orphan if AddToBackpack ever fails
```

### Fix 3 — Detect and remove ghost bots on Map.Internal (MEDIUM priority)

Add a startup scan in `PlayerBotDirector.Deserialize()` (or in a new
`Initialize()`-time check) to cull any registered bot serial whose mobile is alive
but on `Map.Internal`:

```csharp
// In DirectorTick() or a one-shot post-load timer:
for (int i = m_BotSerials.Count - 1; i >= 0; i--)
{
    PlayerBot bot = World.FindMobile(m_BotSerials[i]) as PlayerBot;
    if (bot != null && !bot.Deleted && bot.Map == Map.Internal)
        bot.Delete();   // ghost bot — was never placed
}
```

### Fix 4 — Re-equip stashed weapon after restart (LOW priority)

On `PlayerBot.OnAfterDeserialize()` (or in `StartSkillTimer()` which already runs
after deserialization), scan the backpack for weapons that should be re-equipped:

```csharp
// In PlayerBot after Deserialize — move any unequipped weapons back to hand
// if the OneHanded/TwoHanded layer is empty
Item oneHand = FindItemOnLayer(Layer.OneHanded);
Item twoHand = FindItemOnLayer(Layer.TwoHanded);
if (oneHand == null && twoHand == null && Backpack != null)
{
    foreach (Item item in Backpack.Items)
    {
        if (item is BaseWeapon && !(item is Spellbook))
        {
            EquipItem(item);
            break;
        }
    }
}
```

### Fix 5 — Persist cleanup result (one-time operational step)

After the server starts and the Cleanup message appears, issue `[save` before
shutting down. This removes the 2005 historical orphans from the save file
permanently. Subsequent startups should report 0 (or very few) inaccessible items.

---

## File Locations

| File | Relevant lines |
|---|---|
| `Scripts/Misc/Cleanup.cs` | The cleanup scanner — lines 15–76 |
| `Scripts/Mobiles/PlayerBot/PlayerBotDirector.cs` | `BurstSpawnTick` L135, `POITick` L255, `TrySpawnEncounter` L304 |
| `Scripts/Mobiles/PlayerBot/PlayerBotCrafter.cs` | `DoCraftTick` L52–56 |
| `Scripts/Mobiles/PlayerBot/PlayerBotAI.cs` | `StashWeaponsForCasting` L798, `RestoreStashedWeapons` L822 |
| `RunUO-2.1/Server/Item.cs` | Item() constructor L4635, `OnParentDeleted` L3277 |
| `RunUO-2.1/Server/Mobile.cs` | `Delete()` L3577, `AddItem()` L6056, `DefaultMobileInit` L9764 |
| `RunUO-2.1/Server/World.cs` | `OnDelete()` L75, delete-queue drain L693 |


## Of note

1/ Booted server, 8200ish items to be cleaned, NOT FROM THE ORIGINAL SAVE - I made sure of that.
2/ Performed [save
3/ Rebooted server => no items to be cleaned
4/ I let the server run for 5 minutes
5/ new [save
6/ On reboot: "Cleanup: Detected 84 inaccessible items, removing.."