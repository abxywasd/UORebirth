# PlayerBot Owner UI — Feature Specification

**Status:** Specification (not yet implemented)  
**Scope:** Player-facing gump interface and commands to manage owned PlayerBots.  
**Target client:** UO 1.0.1 (ASCII-only, no context menus)

---

## 1. Overview

When a player hires a PlayerBot (by saying "hire" within 4 tiles), the bot's `ControlMaster` is set to that player via `AddHire()`. Currently, there is no way for the player to inspect or interact with the bot beyond combat orders. This feature adds a complete management UI accessible via a new `[mybots` command or by saying "status" near one of your bots.

The UI is split into a **Bot List gump** (shows all controlled bots) and a per-bot **Detail gump** with four sections navigated by buttons: **Stats**, **Skills**, **Inventory**, and **Equipment**. A **Release** button is always visible on the Detail gump.

---

## 2. Access Methods

### 2.1 `[mybots` command

- Registered in `PlayerBotDirector.Initialize()` at `AccessLevel.Player`.
- Opens `PlayerBotListGump` for the calling player.
- Works from anywhere — does not require proximity to a bot.

### 2.2 Speech trigger: "status" or "manage"

- Handled in `PlayerBot.OnSpeech()` (already overridden).
- Only fires if `from == ControlMaster && from.InRange(this, 6)`.
- Opens `PlayerBotManageGump` directly for that specific bot.
- Does not trigger the list gump (skips straight to the bot that heard the speech).

---

## 3. Gump Architecture

Five gump classes, all in `Scripts/Mobiles/PlayerBot/PlayerBotOwnerGump.cs`:

| Class | Purpose |
|---|---|
| `PlayerBotListGump` | Shows all bots the player controls; entry point from `[mybots` |
| `PlayerBotManageGump` | Per-bot hub with navigation buttons; "Release" button lives here |
| `PlayerBotStatsGump` | Bot stats, persona, current activity |
| `PlayerBotSkillsGump` | All non-zero skills, read-only |
| `PlayerBotInventoryGump` | Backpack contents + bidirectional item transfer |
| `PlayerBotEquipGump` | Equipment slots + equip/unequip |

Navigation is always explicit: every sub-gump has a **Back** button returning to `PlayerBotManageGump`, and `PlayerBotManageGump` has a **Bot List** button returning to `PlayerBotListGump`.

---

## 4. Bot List Gump (`PlayerBotListGump`)

**Dimensions:** ~400 × variable (up to 10 bots before paging)  
**Background:** 9200 (standard dark panel)

### Layout

```
┌──────────────────────────────────────────────────────┐
│  Your Bots  (N controlled)                           │
├──────────────────────────────────────────────────────┤
│  [Manage]  Alaric the Bold   Adventurer · Proficient │
│            HP: ████████░░  Activity: Hunting         │
├──────────────────────────────────────────────────────┤
│  [Manage]  Seraphina         PlayerKiller · Newbie   │
│            HP: ████░░░░░░  Activity: Wandering       │
├──────────────────────────────────────────────────────┤
│  [Release All]              [< Prev]  Page 1/1  [Next >] │
└──────────────────────────────────────────────────────┘
```

### Per-row data

- Bot name (taken from `bot.Name`)
- Profile enum label (`PlayerKiller`, `Crafter`, `Adventurer`)
- Experience enum label (`Newbie`, `Average`, `Proficient`, `Grandmaster`)
- Hit point bar: filled using `AddImageTiled` with two tile IDs (filled hue 0x40 green, empty hue dark)
- Current activity: `bot.ActivityState.Current.ToString()`

### Buttons

| Button | Action |
|---|---|
| **Manage** (per row) | Opens `PlayerBotManageGump` for that bot |
| **Release All** | Calls `SetControlMaster(null)` on every controlled bot; sets activity to `Wandering`; closes gump with a message |
| **Prev / Next** | Page navigation (10 bots per page) |

### Guard conditions

- Collect controlled bots from `World.Mobiles` filtered by `bot.ControlMaster == from && !bot.Deleted && bot.Alive`.
- If the player has no controlled bots: show "You have no bots under your command." and no rows.

---

## 5. Bot Manage Gump (`PlayerBotManageGump`)

**Dimensions:** 420 × 260

The hub gump. Displays the bot's name prominently, a brief status line, and navigation buttons to each sub-section.

```
┌─────────────────────────────────────────────────────┐
│  Managing: Alaric the Bold                          │
│  Adventurer · Proficient · Melee fighter            │
│  HP: ████████░░  Mana: ████░░░░░░  Stam: ██████░░  │
├─────────────────────────────────────────────────────┤
│  [Stats]       [Skills]      [Inventory] [Equipment]│
├─────────────────────────────────────────────────────┤
│  [< Bot List]                          [Release Bot]│
└─────────────────────────────────────────────────────┘
```

### HP/Mana/Stam bars

Rendered with two `AddImageTiled` calls (filled portion, then empty portion) using fixed widths proportional to `Hits/HitsMax`, etc.

### Navigation buttons

| Button | Opens |
|---|---|
| **Stats** | `PlayerBotStatsGump` |
| **Skills** | `PlayerBotSkillsGump` |
| **Inventory** | `PlayerBotInventoryGump` — requires player within 3 tiles of bot |
| **Equipment** | `PlayerBotEquipGump` — requires player within 3 tiles of bot |
| **Bot List** | `PlayerBotListGump` |
| **Release Bot** | Confirmation dialog → releases bot on confirm |

### Proximity enforcement

Inventory and Equipment require `from.InRange(bot, 3)`. If the player is too far: send message "You must be closer to your bot to access their belongings." and do not open that sub-gump.

### Release confirmation

On "Release Bot" press, open a `WarningGump` (already exists in `Scripts/Gumps/WarningGump.cs`) with message:  
> "Release [BotName]? They will return to wandering the world."  

Confirm callback calls `ReleaseBot(from, bot)`:
```
bot.SetControlMaster(null);
bot.ActivityState.SetActivity(BotActivity.Wandering);
from.SendMessage("{0} has been released.", bot.Name);
```
After release, reopen `PlayerBotListGump`.

---

## 6. Stats Gump (`PlayerBotStatsGump`)

**Dimensions:** 420 × 340  
**Read-only.** No actions except navigation.

```
┌─────────────────────────────────────────────────────┐
│  Stats — Alaric the Bold                            │
├─────────────────────────────────────────────────────┤
│  Profile:     Adventurer                            │
│  Experience:  Proficient                            │
│  Combat:      Melee (Swords)                        │
│  Uses Magic:  Yes                                   │
├─────────────────────────────────────────────────────┤
│  Strength:    85   Dexterity:  79   Intelligence: 71│
│  Hits:        85 / 85                               │
│  Mana:        71 / 71                               │
│  Stamina:     79 / 79                               │
├─────────────────────────────────────────────────────┤
│  Activity:    Hunting  (for 0m 43s)                 │
│  Group:       None                                  │
├─────────────────────────────────────────────────────┤
│  [Back]                                             │
└─────────────────────────────────────────────────────┘
```

### Fields

| Field | Source |
|---|---|
| Profile | `bot.PlayerBotProfile.ToString()` |
| Experience | `bot.PlayerBotExperience.ToString()` |
| Combat style | If `bot.UsesMagic && !bot.PrefersMelee` → "Mage"; if `bot.UsesMagic && bot.PrefersMelee` → "Melee/Mage Hybrid"; else → `"Melee (" + bot.PreferedCombatSkill + ")"` |
| Uses Magic | `bot.UsesMagic` |
| Str / Dex / Int | `bot.Str`, `bot.Dex`, `bot.Int` |
| Hits / Mana / Stam | Current and max values |
| Activity | `bot.ActivityState.Current` + `bot.ActivityState.TimeInCurrentActivity` formatted as `Xm Ys` |
| Group | `bot.Group != null ? "Yes (size " + bot.Group.Members.Count + ")" : "None"` |

---

## 7. Skills Gump (`PlayerBotSkillsGump`)

**Dimensions:** 420 × variable  
**Read-only.** Displays all skills with a value > 0.

```
┌─────────────────────────────────────────────────────┐
│  Skills — Alaric the Bold                           │
├────────────────────────────────┬────────────────────┤
│  Swords                        │  78.4              │
│  Tactics                       │  81.2              │
│  Magic Resist                  │  74.0              │
│  Parry                         │  69.5              │
│  Magery                        │  65.1              │
│  Eval Intelligence             │  58.3              │
│  Meditation                    │  52.8              │
│  Blacksmith                    │  0.0  (hidden)     │
├────────────────────────────────┴────────────────────┤
│  Total: 477.3 / 700.0                               │
│  [Back]                                             │
└─────────────────────────────────────────────────────┘
```

### Behaviour

- Enumerate all 52 skills via `bot.Skills` (the `Skills` property on `Mobile`).
- Show only skills with `skill.Base > 0.0`. Sort by value descending.
- Display skill name via `SkillInfo.Table[(int)skill.SkillID].Name`.
- Show total skill points: `bot.Skills.Total.ToString("F1")` and cap (700.0 for Pre-T2A).
- No edit functionality — players cannot change bot skills.

---

## 8. Inventory Gump (`PlayerBotInventoryGump`)

**Dimensions:** 460 × 380  
**Range-gated:** Player must be within 3 tiles of bot.  
**Paged:** 12 items per page.

```
┌──────────────────────────────────────────────────────┐
│  Inventory — Alaric the Bold      Page 1 / 2         │
├────────────────────────────┬────────┬────────────────┤
│  Item Name                 │  Qty   │  Action        │
├────────────────────────────┼────────┼────────────────┤
│  Gold                      │  350   │  [Take]        │
│  Bandage                   │  40    │  [Take]        │
│  Black Pearl               │  50    │  [Take]        │
│  Bread Loaf                │  1     │  [Take]        │
│  Iron Ingot                │  12    │  [Take]        │
│  ...                       │        │                │
├────────────────────────────┴────────┴────────────────┤
│  [Take All Gold]  [Give Item to Bot]                 │
│  [< Prev]  Page 1/2  [Next >]                        │
│  [Back]                                              │
└──────────────────────────────────────────────────────┘
```

### Item listing

- Source: `bot.Backpack.Items` (the top-level container contents).
- Display `item.Amount` for stackable items; always 1 for non-stackable.
- Item name: `item.Name ?? ItemData.Table[item.ItemID].Name`.
- Do not recurse into nested containers (only the bot's direct backpack contents).

### "Take" button (per item)

1. Re-validate: `from.InRange(bot, 3)`, bot not deleted, item still in bot's backpack.
2. Move: `from.AddToBackpack(item)`.
3. If player's pack is full or item cannot be picked up: send "Your pack is full." and do nothing.
4. Reopen the gump at the same page.

### "Take All Gold"

1. Collect all `Gold` items from `bot.Backpack.Items`.
2. Sum quantities. Create a single new `Gold(total)` in player's pack (or add to existing gold stack via `from.AddToBackpack`).
3. Delete individual gold items from bot's backpack.
4. Send message: "You take {total} gold from {bot.Name}."
5. Reopen gump.

### "Give Item to Bot"

1. Open a custom target: `from.Target = new PlayerBotGiveTarget(from, bot, page)`.
2. `PlayerBotGiveTarget` (inner class implementing `Target`):
   - `AllowGround = false`, `Range = 2` (item must be in player's immediate reach).
   - `OnTarget`: check targeted item is in `from.Backpack`; check bot's backpack is not full; call `bot.Backpack.AddItem(item)`.
   - On success: send "You give {item.Name} to {bot.Name}." and reopen the gump.
   - On failure (wrong container, too far, etc.): send appropriate message, reopen gump.

### Page navigation

- Store current page index as a constructor parameter; pass it through button responses.
- "Prev" / "Next" buttons simply reopen `PlayerBotInventoryGump` with `page - 1` or `page + 1`.

---

## 9. Equipment Gump (`PlayerBotEquipGump`)

**Dimensions:** 460 × 400  
**Range-gated:** Player must be within 3 tiles of bot.

```
┌──────────────────────────────────────────────────────┐
│  Equipment — Alaric the Bold                         │
├─────────────────────┬────────────────────────────────┤
│  Head               │  Plate Helm              [Take]│
│  Chest              │  Plate Chest             [Take]│
│  Arms               │  Plate Arms              [Take]│
│  Gloves             │  Plate Gloves            [Take]│
│  Gorget             │  Leather Gorget          [Take]│
│  Legs               │  Chain Legs              [Take]│
│  Right Hand         │  Longsword               [Take]│
│  Left Hand          │  (empty)                       │
│  Cloak              │  Blue Cloak              [Take]│
│  Shirt              │  Red Shirt               [Take]│
│  Pants              │  Long Pants              [Take]│
│  Shoes              │  Boots                   [Take]│
│  Neck               │  (empty)                       │
│  Ring               │  (empty)                       │
├─────────────────────┴────────────────────────────────┤
│  [Equip Item]   (target item from your pack)         │
│  [Back]                                              │
└──────────────────────────────────────────────────────┘
```

### Slot definitions

Map each `Layer` enum value to a display name. Show these layers in display order:

```
Layer.Helm        → "Head"
Layer.InnerTorso  → "Chest"
Layer.Arms        → "Arms"
Layer.Gloves      → "Gloves"
Layer.Gorget      → "Gorget / Neck Armor"
Layer.Pants       → "Legs"
Layer.OneHanded   → "Right Hand"
Layer.TwoHanded   → "Two-Handed / Left Hand"
Layer.Cloak       → "Cloak"
Layer.InnerLegs   → "Inner Legs"
Layer.OuterLegs   → "Outer Legs"
Layer.OuterTorso  → "Outer Torso"
Layer.MiddleTorso → "Middle Torso"
Layer.Waist       → "Waist"
Layer.Shirt       → "Shirt"
Layer.Shoes       → "Shoes"
Layer.Ring        → "Ring"
Layer.Bracelet    → "Bracelet"
Layer.Earrings    → "Earrings"
Layer.Hair        → (hidden — cosmetic only, no Take)
Layer.FacialHair  → (hidden — cosmetic only, no Take)
```

For each layer: call `bot.FindItemOnLayer(layer)`. If null, display "(empty)" with no Take button. If found, display item name and a Take button.

### "Take" button (per equipped item)

1. Re-validate proximity, item still equipped on bot.
2. Call `bot.RemoveItem(item)`.
3. Call `from.AddToBackpack(item)`.
4. If player's pack is full: re-equip the item on the bot (`bot.EquipItem(item)`) and send "Your pack is full."
5. Reopen gump.

### "Equip Item" button

1. Open `PlayerBotEquipTarget(from, bot)` (custom `Target`).
2. `PlayerBotEquipTarget`:
   - `AllowGround = false`, `Range = 2`.
   - `OnTarget`: check item is in `from.Backpack`; check item is `IWearable` (implements equipment interface) or is a `BaseArmor`, `BaseWeapon`, or `BaseClothing`; check bot can equip this item layer (slot not already occupied, or overwrite with confirmation).
   - Equip: `from.Backpack.RemoveItem(item)`, then `bot.EquipItem(item)`.
   - If the slot is occupied: automatically move the existing item to bot's backpack first, then equip the new item.
   - Send message: "You equip {item.Name} on {bot.Name}."
3. Reopen gump.

---

## 10. New Files

| File | Contents |
|---|---|
| `Scripts/Mobiles/PlayerBot/PlayerBotOwnerGump.cs` | All five gump classes + `PlayerBotGiveTarget` + `PlayerBotEquipTarget` |

No additional files required. The `[mybots` command and the speech trigger are registered/handled within existing files (`PlayerBotDirector.Initialize` and `PlayerBot.OnSpeech`).

---

## 11. Existing File Changes

### `Scripts/Mobiles/PlayerBot/PlayerBotDirector.cs`

In `Initialize()`, add:

```csharp
CommandSystem.Register("MyBots", AccessLevel.Player, new CommandEventHandler(MyBots_OnCommand));
```

And the handler:

```csharp
[Usage("MyBots")]
[Description("Opens the PlayerBot management interface for your controlled bots.")]
private static void MyBots_OnCommand(CommandEventArgs e)
{
    e.Mobile.SendGump(new PlayerBotListGump(e.Mobile));
}
```

### `Scripts/Mobiles/PlayerBot/PlayerBot.cs`

In `OnSpeech()`, before the existing hire-keyword check, add a speech branch that triggers on keywords "status" or "manage":

```csharp
if (HasKeyword(e, "status") || HasKeyword(e, "manage"))
{
    e.Handled = true;
    if (this.Controled && this.ControlMaster == e.Mobile)
        e.Mobile.SendGump(new PlayerBotManageGump(e.Mobile, this));
    else
        Say("I don't think we've been introduced...");
    return;
}
```

Use `e.Speech.ToLower().Contains(...)` rather than `e.HasKeyword()` since these are not pre-registered UO keyword IDs. The check runs before the existing hire branch.

---

## 12. Guard Conditions (complete list)

Every gump response handler must verify these before taking any action:

1. `from == null || from.Deleted || !from.Alive` → close silently.
2. `bot == null || bot.Deleted` → send "Your bot no longer exists." close gump.
3. `!bot.Alive` → send "Your bot is dead." Close gump (do not allow inventory access on a dead bot's corpse — loot rules apply separately).
4. `bot.ControlMaster != from` → send "You no longer control that bot." Close gump (ownership may have changed).
5. For Inventory/Equipment actions: `!from.InRange(bot, 3)` → send "You are too far away." Reopen the gump (so they can move closer and retry without re-navigating).

---

## 13. Edge Cases

| Scenario | Handling |
|---|---|
| Bot dies while gump is open | Next action triggers guard condition #3 above |
| Bot is released (control cleared) while gump is open | Guard condition #4 catches it |
| Player is dead | Guard condition #1 catches it |
| Player logs off mid-action | Gump is closed server-side; no stale state |
| Bot has a nested container in its backpack | Inventory gump shows the container as a single item; player can Take it as a unit |
| Multiple bots controlled | List gump handles all; manage gump is per-bot |
| Player tries to equip something that is not wearable (e.g., a gem) | `PlayerBotEquipTarget.OnTarget` rejects it: "That cannot be equipped." |
| Bot's backpack is full when giving an item | `bot.Backpack.TryDropItem` fails; send "Your bot's pack is full." |
| Equipping a 2H weapon while bot has a 1H + shield | Existing RunUO `EquipItem` logic handles slot conflicts; the gump moves displaced items to bot's pack |

---

## 14. UI Constants (reference for implementation)

```
Background gump ID : 9200
Separator tile ID  : 9304
Standard button IDs: 4005 (normal), 4007 (pressed) — confirm/action
Destructive button : 4017 (normal), 4019 (pressed) — release/dangerous
Standard text hue  : 0 (white)
Green label hue    : 0x40
Red/warning hue    : 0x26
Header hue         : 0x384

HP bar filled tile : 9755  (dark red/health bar tile)
HP bar empty tile  : 9756
Bar total width    : 100px — fill = (current/max) * 100 pixels
```

---

## 15. Out of Scope (explicitly excluded)

- Renaming bots (bot names are set at spawn and `CanBeRenamedBy` returns false).
- Changing bot persona/experience via the player gump (GM-only via `[props`).
- Bot skill editing (players cannot alter bot skills).
- Persistent bot ownership across server restarts without being in control range (ownership is already serialized via `ControlMaster`).
- Bank access from the bot (no bank box interaction).
- Giving items directly from one bot to another (only player ↔ bot transfers).
