# PlayerBot Command Obedience & "All" Prefix — Implementation Plan

## Problem Summary

PlayerBots fail to reliably obey orders from their master, particularly during or right after combat:

1. **`DoControlledThink` only honors `OrderType.Attack`** — Stop, Follow, Stay, Guard, Come are all silently ignored, falling through to the "assist master" block or the default follow-master behavior.
2. **"all kill" spawns N targeting cursors** — because each bot independently calls `BaseAI.BeginPickTarget`, the master gets one cursor per bot. This is a UX disaster and often results in only the first bot getting the order.
3. **Attack orders aren't sticky** — after the first AI tick processes an attack order, the bot is fighting the target. If the master also enters combat on the next tick, the "assist master" block reassigns the bot's combatant, overriding the explicit order.
4. **No squad commands** — there is no way to broadcast a single-cursor attack to all bots, nor to query all-bot status, nor to issue a healer dispatch command.

---

## Root Cause: `DoControlledThink` (PlayerBotAI.cs)

```csharp
// CURRENT CODE — only one order is respected
if (m_Mobile.ControlOrder == OrderType.Attack)
{
    // ... sets Combatant = ControlTarget, returns
}

// This block runs on EVERY tick for all other orders (Stop, Follow, Stay, Guard, Come)
// because none of them have a matching case above:
if (master.Combatant != null && master.Combatant.Alive)
{
    m_Mobile.Combatant = master.Combatant;   // overrides any previous order
    m_Mobile.Warmode   = true;
    return DoActivityCombat(bot);
}
```

Any `OrderType` other than `Attack` is transparent to `DoControlledThink` and is immediately steamrolled by the "assist master" or self-defense logic.

---

## Root Cause: Per-Bot Targeting Cursor for "all kill"

`BaseAI.OnSpeech` handles keyword `0x168` ("all kill") by calling `BeginPickTarget(master, OrderType.Attack)`. This call is made **inside each bot's `OnSpeech`**, so 3 bots → 3 cursors. The player must cancel 2 of them. Only the last remaining cursor actually issues the order, and only to the bot that opened it.

---

## Fix 1 — Honor All Order Types in `DoControlledThink`

**File:** `Scripts/Mobiles/PlayerBot/PlayerBotAI.cs`

Replace the current single `if (ControlOrder == Attack)` block with a full `switch`:

```csharp
switch (m_Mobile.ControlOrder)
{
    case OrderType.Attack:
    {
        Mobile ct = m_Mobile.ControlTarget;
        if (ct != null && !ct.Deleted && ct.Alive && ct.Map == m_Mobile.Map)
        {
            m_Mobile.Combatant = ct;
            m_Mobile.Warmode   = true;
            m_AttackOrderIssuedTime = DateTime.Now;   // stickiness anchor (see Fix 2)
            bot.ActivityState.SetActivity(BotActivity.Combat);
            return DoActivityCombat(bot);
        }
        if (ct != null)   // target gone — clear stale order
        {
            m_Mobile.ControlTarget = null;
            m_Mobile.ControlOrder  = OrderType.None;
        }
        break;
    }

    case OrderType.Stop:
    case OrderType.Stay:
        m_Mobile.Combatant = null;
        m_Mobile.Warmode   = false;
        bot.ActivityState.SetActivity(BotActivity.Wandering);
        return true;

    case OrderType.Come:
        m_Mobile.Combatant = null;
        m_Mobile.Warmode   = false;
        bot.ActivityState.SetActivity(BotActivity.Wandering);
        if (!m_Mobile.InRange(master, 2))
            FollowRunning(master, 2);
        return true;

    case OrderType.Follow:
    {
        Mobile followTarget = m_Mobile.ControlTarget ?? master;
        m_Mobile.Combatant  = null;
        m_Mobile.Warmode    = false;
        bot.ActivityState.SetActivity(BotActivity.Wandering);
        if (!m_Mobile.InRange(followTarget, 2))
            FollowRunning(followTarget, 2);
        return true;
    }

    case OrderType.Guard:
        // Guard = follow master + react to his aggressors.
        // The assist-master block below already handles the combat side;
        // we just need to ensure warmode is off when master is not fighting.
        break;
}
// Falls through to "assist master" / self-defense / follow-master default below
```

---

## Fix 2 — Attack Order Stickiness

**File:** `Scripts/Mobiles/PlayerBot/PlayerBotAI.cs`

A manually issued attack order should not be overridden by the "assist master" block for several seconds, giving the player's intent priority.

Add to `PlayerBotAI` field declarations:
```csharp
private DateTime m_AttackOrderIssuedTime;
private const double AttackOrderGracePeriod = 8.0; // seconds
```

In the "assist master" block, add a guard:
```csharp
bool recentManualOrder = (DateTime.Now - m_AttackOrderIssuedTime).TotalSeconds < AttackOrderGracePeriod;

if (!recentManualOrder
    && master.Alive && master.Combatant != null
    && !master.Combatant.Deleted && master.Combatant.Alive)
{
    m_Mobile.Combatant = master.Combatant;
    m_Mobile.Warmode   = true;
    bot.ActivityState.SetActivity(BotActivity.Combat);
    return DoActivityCombat(bot);
}
```

The `m_AttackOrderIssuedTime` is set inside the `OrderType.Attack` case above when a valid ControlTarget is committed. This gives an 8-second window during which the bot ignores the "assist master" override and continues on its ordered target.

---

## Fix 3 — "all kill" Coordination (Single Cursor)

### New File: `Scripts/Mobiles/PlayerBot/PlayerBotAllCommandHandler.cs`

```csharp
using System;
using System.Collections.Generic;
using Server;
using Server.Targeting;

namespace Server.Mobiles
{
    // Coordinates broadcast commands issued via "all <cmd>" speech so that
    // a single targeting cursor is opened on the master (not one per bot).
    public static class PlayerBotAllCommandHandler
    {
        // Tracks masters that currently have an open "all attack" cursor.
        private static readonly HashSet<Mobile> s_PendingAttackCursor = new HashSet<Mobile>();

        // Called by PlayerBot.OnSpeech on "all kill" / "all attack" keywords.
        // Returns true if this bot should suppress the BaseAI handler.
        public static bool TryBeginAllAttack(Mobile master)
        {
            if (s_PendingAttackCursor.Contains(master))
                return true; // cursor already open — suppress this bot's duplicate

            s_PendingAttackCursor.Add(master);
            master.Target = new PlayerBotAllAttackTarget(master);
            master.SendMessage("Select a target for all your bots.");
            return true;
        }

        // Called by the targeting cursor when a target is selected.
        public static void BroadcastAttackOrder(Mobile master, Mobile target)
        {
            s_PendingAttackCursor.Remove(master);
            foreach (PlayerBot bot in GetControlledBots(master))
            {
                bot.ControlTarget = target;
                bot.ControlOrder  = OrderType.Attack;
            }
        }

        // Called when the cursor is cancelled.
        public static void ClearPendingCursor(Mobile master)
        {
            s_PendingAttackCursor.Remove(master);
        }

        // Broadcasts a non-targeted order to all controlled bots.
        public static void BroadcastOrder(Mobile master, OrderType order, Mobile orderTarget = null)
        {
            foreach (PlayerBot bot in GetControlledBots(master))
            {
                bot.ControlTarget = orderTarget;
                bot.ControlOrder  = order;
            }
        }

        // Broadcasts a status report — each bot says HP/activity overhead.
        public static void BroadcastStatusReport(Mobile master)
        {
            foreach (PlayerBot bot in GetControlledBots(master))
            {
                string msg = String.Format(
                    "[{0}% HP] [{1}]",
                    (int)((double)bot.Hits / bot.HitsMax * 100),
                    bot.ActivityState.Current
                );
                bot.Say(msg);
            }
        }

        // Broadcasts a heal order — all mage bots cast Heal on master immediately.
        public static void BroadcastHealMaster(Mobile master)
        {
            foreach (PlayerBot bot in GetControlledBots(master))
            {
                if (!bot.UsesMagic) continue;

                PlayerBotAI ai = bot.AIObject as PlayerBotAI;
                if (ai == null) continue;

                // Force the bot to immediately check master heal on the next AI tick
                // by clearing the next-cast throttle is not cleanly accessible from here,
                // so instead: set a transient flag that PlayerBotAI polls.
                bot.ForceMasterHeal = true;
            }
        }

        private static IEnumerable<PlayerBot> GetControlledBots(Mobile master)
        {
            var bots = new List<PlayerBot>();
            foreach (Mobile m in World.Mobiles.Values)
            {
                PlayerBot pb = m as PlayerBot;
                if (pb != null && !pb.Deleted && pb.Alive && pb.ControlMaster == master)
                    bots.Add(pb);
            }
            return bots;
        }
    }

    // Targeting cursor that broadcasts the attack order to all bots on resolution.
    public class PlayerBotAllAttackTarget : Target
    {
        private readonly Mobile m_Master;

        public PlayerBotAllAttackTarget(Mobile master)
            : base(-1, false, TargetFlags.Harmful)
        {
            m_Master = master;
        }

        protected override void OnTarget(Mobile from, object targeted)
        {
            Mobile target = targeted as Mobile;
            if (target == null || !target.Alive || target.Deleted)
            {
                PlayerBotAllCommandHandler.ClearPendingCursor(m_Master);
                from.SendMessage("That is not a valid target.");
                return;
            }

            PlayerBotAllCommandHandler.BroadcastAttackOrder(m_Master, target);
        }

        protected override void OnTargetCancel(Mobile from, TargetCancelType cancelType)
        {
            PlayerBotAllCommandHandler.ClearPendingCursor(m_Master);
        }
    }
}
```

---

## Fix 4 — Intercept "all kill/attack" in `PlayerBot.OnSpeech`

**File:** `Scripts/Mobiles/PlayerBot/PlayerBot.cs`

The existing `PlayerBot.OnSpeech` calls `base.OnSpeech(e)` which flows to `BaseCreature` → `BaseAI.OnSpeech`. `BaseAI.OnSpeech` handles "all kill" (keyword 0x168) by calling `BeginPickTarget` — one cursor per bot. We must intercept before that call.

Add a new block at the top of `OnSpeech`, before the "status/manage" check:

```csharp
public override void OnSpeech(SpeechEventArgs e)
{
    // ── Intercept "all" commands from master (must run before base.OnSpeech
    //    which would open N targeting cursors for attack commands) ───────────
    if (!e.Handled
        && e.Mobile.Alive
        && this.Controled
        && this.ControlMaster == e.Mobile
        && e.Mobile.InRange(this, 14))
    {
        int[] keywords = e.Keywords;

        for (int i = 0; i < keywords.Length; i++)
        {
            switch (keywords[i])
            {
                case 0x168: // all kill
                case 0x169: // all attack
                    // Coordinate: only the first bot opens a cursor
                    PlayerBotAllCommandHandler.TryBeginAllAttack(e.Mobile);
                    // Suppress BaseAI handling for this bot to avoid N cursors
                    return;

                case 0x164: // all come
                case 0x16C: // all follow me
                    // BaseAI handles per-bot Come/Follow correctly (no cursor),
                    // BUT DoControlledThink now honors it. Let BaseAI continue.
                    break;

                case 0x167: // all stop
                case 0x170: // all stay
                    // Let BaseAI set ControlOrder=Stop on each bot.
                    break;

                case 0x166: // all guard
                case 0x16B: // all guard me
                    // Let BaseAI set ControlOrder=Guard on each bot.
                    break;
            }
        }

        // ── Custom non-keyword "all" commands ───────────────────────────────
        string speech = e.Speech.ToLower().Trim();

        if (speech == "all status" || speech == "all report")
        {
            PlayerBotAllCommandHandler.BroadcastStatusReport(e.Mobile);
            e.Handled = true;
            return;
        }

        if (speech == "all heal me" || speech == "all heal")
        {
            PlayerBotAllCommandHandler.BroadcastHealMaster(e.Mobile);
            e.Handled = true;
            return;
        }

        if (speech == "all release" || speech == "release all")
        {
            // Safety: require confirmation via gump rather than instant release
            e.Mobile.SendMessage("Use the bot management gump to release bots.");
            e.Handled = true;
            return;
        }
    }

    // ── Existing speech handling (status/manage gump + hire) ──────────────
    if (!e.Handled && e.Mobile.InRange(this, 6))
    {
        string speech = e.Speech.ToLower();
        if (speech.Contains("status") || speech.Contains("manage"))
        {
            // ... existing gump opener code
        }
    }
    // ... rest of existing OnSpeech
    base.OnSpeech(e);
}
```

---

## Fix 5 — `ForceMasterHeal` Flag for "all heal me"

**File:** `Scripts/Mobiles/PlayerBot/PlayerBot.cs`

Add a transient field to `PlayerBot`:
```csharp
[NonSerialized]
public bool ForceMasterHeal;
```

**File:** `Scripts/Mobiles/PlayerBot/PlayerBotAI.cs`

In `CheckMasterHeal`, skip the HP-deficit guard when the flag is set:
```csharp
private bool CheckMasterHeal(PlayerBot bot)
{
    if (!bot.UsesMagic) return false;
    if (m_Mobile.Spell != null && m_Mobile.Spell.IsCasting) return false;
    if (DateTime.Now < m_NextCastTime) return false;

    Mobile master = m_Mobile.ControlMaster;
    if (master == null || master.Deleted || !master.Alive) return false;
    if (!m_Mobile.InRange(master, 12)) return false;

    bool forced = bot.ForceMasterHeal;
    bot.ForceMasterHeal = false;

    // Normal threshold: skip if master is healthy enough. Forced: always cast.
    if (!forced && !master.Poisoned && master.Hits >= master.HitsMax - 10) return false;

    if (!PlayerBotCombatHelper.HasHealSpellReadyFor(bot, master)) return false;

    StashWeaponsForCasting();
    bool result = PlayerBotCombatHelper.TryCastHealTarget(bot, master, ref m_NextCastTime);
    if (result)
        m_HealTarget = master;
    else
        RestoreStashedWeapons();
    return result;
}
```

---

## Full Command Reference

| Spoken Command | Effect | Source |
|---|---|---|
| `all come` | All bots run to master immediately | BaseAI → DoControlledThink (Come) |
| `all follow me` | All bots follow master, breaking off combat | BaseAI → DoControlledThink (Follow) |
| `all stop` | All bots stop combat, stand still | BaseAI → DoControlledThink (Stop) |
| `all stay` | Alias for all stop | BaseAI → DoControlledThink (Stay) |
| `all kill` / `all attack` | All bots attack one target (**single cursor**) | New: `PlayerBotAllCommandHandler` |
| `all guard` / `all guard me` | All bots protect master (attack his aggressors) | BaseAI → DoControlledThink (Guard) |
| `all status` / `all report` | All bots say their HP% and activity overhead | New: custom speech → `BroadcastStatusReport` |
| `all heal me` / `all heal` | All mage bots cast Heal on master immediately | New: custom speech → `BroadcastHealMaster` |
| `[name] come` | Named bot runs to master | BaseAI (per-bot, WasNamed check) |
| `[name] follow me` | Named bot follows master | BaseAI (per-bot) |
| `[name] stop` / `[name] stay` | Named bot stops | BaseAI (per-bot) |
| `[name] kill` / `[name] attack` | Named bot attacks a target (cursor) | BaseAI (per-bot) |
| `[name] guard` | Named bot guards master | BaseAI (per-bot) |
| `status` / `manage` (near bot) | Opens the management gump for this bot | Existing |
| `all move` | Same behavior as `%name% move`, makes all bots move in order to make way for their owner | 

> **Note on "all follow me" vs "all follow":**
> - `all follow me` (keyword 0x16C) → each bot sets `ControlTarget = master, ControlOrder = Follow` via BaseAI. Works correctly.
> - `all follow` (keyword 0x165) → each bot opens a targeting cursor for a custom follow target. This is the correct UO behavior and can stay as-is; the player clicks who to follow.

---

## Files to Create / Modify

### New file
- `Scripts/Mobiles/PlayerBot/PlayerBotAllCommandHandler.cs`
  - `PlayerBotAllCommandHandler` (static class)
  - `PlayerBotAllAttackTarget` (Target subclass)

### Modified files

**`Scripts/Mobiles/PlayerBot/PlayerBotAI.cs`**
- Add `m_AttackOrderIssuedTime` field + `AttackOrderGracePeriod` constant
- Replace `if (ControlOrder == Attack)` with full `switch` across all OrderTypes
- Guard the "assist master" block with `recentManualOrder` check
- Update `CheckMasterHeal` to respect `bot.ForceMasterHeal`

**`Scripts/Mobiles/PlayerBot/PlayerBot.cs`**
- Add `[NonSerialized] public bool ForceMasterHeal` field
- Expand `OnSpeech`: intercept "all kill/attack" keywords before `base.OnSpeech`
- Add custom string matching for `all status`, `all heal me`, `all release`

---

## Behavior After Implementation

### Scenario: Master orders "all kill" during active combat

1. Player says `all kill` (UO keyword 0x168).
2. First bot to hear it calls `TryBeginAllAttack` → opens ONE cursor on master. All subsequent bots suppress.
3. Player clicks target mob.
4. `PlayerBotAllAttackTarget.OnTarget` calls `BroadcastAttackOrder` → sets `ControlTarget = mob, ControlOrder = Attack` on every bot.
5. On next AI tick each bot's `DoControlledThink` sees `ControlOrder == Attack`, forces `Combatant = ControlTarget`, records `m_AttackOrderIssuedTime`, enters combat.
6. For 8 seconds, "assist master" logic cannot override this assignment. Bot stays on its ordered target even if master engages someone else.

### Scenario: Master says "all stop" mid-fight

1. Player says `all stop` (keyword 0x167).
2. `BaseAI.OnSpeech` sets `ControlOrder = Stop` on each bot (standard behavior, no cursor needed).
3. On next tick, `DoControlledThink` sees `Stop` → clears `Combatant`, clears `Warmode`, transitions to `Wandering`. Bot stands still.

### Scenario: Named single-bot order

1. Player says `Elara stop` (UO keyword 0x161, WasNamed check matches "Elara").
2. `BaseAI.OnSpeech` for bot named "Elara" sets `ControlOrder = Stop`.
3. Same flow as above — bot stops.
4. Other bots in range do not respond because `WasNamed` fails for them.

### Scenario: "all heal me" in a tough fight

1. Player says `all heal me`.
2. `PlayerBot.OnSpeech` matches the custom string → calls `BroadcastHealMaster`.
3. All mage bots that have their master in range get `ForceMasterHeal = true`.
4. On the next AI tick, `CheckMasterHeal` sees the flag → casts Heal on master regardless of HP threshold.

---

## Edge Cases & Guards

- **"all kill" cursor timeout**: If the master never resolves the cursor (walks away, logs out), the `s_PendingAttackCursor` entry stays. Mitigation: `OnTargetCancel` fires on logout via `Mobile.OnNetStateChanged` → target auto-cancels → `ClearPendingCursor` is called.
- **Dead master**: All order types guard `master.Alive` at the top of `DoControlledThink` — no changes needed.
- **Bot out of hearing range**: "all" commands are heard at range 14 (standard UO). Bots further away simply never receive the speech event and keep their existing order.
- **Multiple masters**: `s_PendingAttackCursor` is keyed per-master `Mobile`, so concurrent multi-master scenarios are isolated.
- **Attack order on a dead target**: Existing code in the `OrderType.Attack` case already handles this (clears stale `ControlOrder` when `ControlTarget` is gone).
