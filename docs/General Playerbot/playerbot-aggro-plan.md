# PlayerBot Aggro Fix — Implementation Plan

## Problem Statement

Hostile NPCs, monsters, and player-killer bots attacking a player never redirect
aggro onto the player's accompanying PlayerBots. Mobs laser-focus the real
PlayerMobile and ignore the bots entirely, which breaks the believability of bot
combat and makes the player the only damage-sponge regardless of group size.

---

## Root Cause Analysis

Three independent defects combine to produce the bug.

### Defect 1 — `IsEnemy()` treats PlayerBots as neutral creatures

**File:** [Scripts/Engines/AI/Creature/BaseCreature.cs:517](Scripts/Engines/AI/Creature/BaseCreature.cs)

```csharp
public virtual bool IsEnemy( Mobile m )
{
    if (!(m is BaseCreature))
        return true;               // PlayerMobile → always an enemy ✓
    BaseCreature c = (BaseCreature)m;
    return ( m_iTeam != c.m_iTeam ||
             (m_bSummoned || m_bControled) != (c.m_bSummoned || c.m_bControled) );
}
```

A wild NPC (team 0, uncontrolled) evaluating a PlayerBot (team 0, uncontrolled):
- `m_iTeam != c.m_iTeam` → `0 != 0` → **false**
- control status differs → **false != false** → **false**
- **`IsEnemy()` returns `false` — the bot looks like a neutral creature.**

Because `AquireFocusMob` gates all target selection on `IsEnemy()` (line 2066), no
hostile NPC ever puts a PlayerBot on its target list through normal scanning.

### Defect 2 — `bPlayerOnly = true` calls skip PlayerBots completely

**File:** [Scripts/Engines/AI/AI/BaseAI.cs:2056](Scripts/Engines/AI/AI/BaseAI.cs)

```csharp
if ( m.Player || !bPlayerOnly )   // PlayerBot.Player == false
```

When AIs use `bPlayerOnly = true` the PlayerBot is invisible. This affects:

| File | Line | Context |
|------|------|---------|
| `AnimalAI.cs` | 100, 120 | Flee/scatter from nearby players |
| `PredatorAI.cs` | 28, 84 | Predator detect + flee |
| `HealerAI.cs` | 77 | Healer finds a player to heal |

For flee/heal behaviors the impact is minor, but for any future `bPlayerOnly = true`
combat path it would matter.

### Defect 3 — `FightMode.Agressor` NPCs can only target their aggressor list

**File:** [Scripts/Engines/AI/AI/BaseAI.cs:2086–2087](Scripts/Engines/AI/AI/BaseAI.cs)

```csharp
if ( acqType == FightMode.Agressor )
    bCheckIt = PlayerMobile.CheckAggressors( m_Mobile, m );
```

Many dungeon monsters use `FightMode.Agressor`. They only switch to a new target if
that target appears in `m_Mobile.Aggressors` or `m_Mobile.Aggressed`. A PlayerBot
that has never attacked the NPC will never appear there, so even after fixing
Defects 1 and 2 these NPCs still ignore bots unless the bot strikes first.

---

## Target Behaviour After Fix

- A hostile NPC scanning for targets considers PlayerBots on equal footing with
  real players.
- A `FightMode.Agressor` NPC that is already fighting the player will also put
  nearby PlayerBots on its hit-list when the bot steps into the fight.
- Player-controlled pets (tamed animals, summoned creatures) do **not** start
  attacking the player's own bots.
- PlayerBot-vs-PlayerBot targeting is unchanged (governed by `ShouldAttack()`).

---

## Implementation

### Fix 1 — `IsEnemy()`: wild creatures treat PlayerBots as hostile

**File:** [Scripts/Engines/AI/Creature/BaseCreature.cs](Scripts/Engines/AI/Creature/BaseCreature.cs) ~line 517

Add one guarded block after the existing `BaseGuard` check:

```csharp
public virtual bool IsEnemy( Mobile m )
{
    OppositionGroup g = this.OppositionGroup;
    if (g != null && g.IsEnemy(this, m))
        return true;

    if ( m is BaseGuard )
        return false;

    if ( !(m is BaseCreature) )
        return true;

    // --- NEW ---
    // Wild (uncontrolled, unsummoned) creatures treat PlayerBots as enemies,
    // mirroring how they treat real players.  Player-owned pets (m_bControled)
    // are explicitly excluded so tamed animals don't attack their owner's bots.
    if ( m is PlayerBot && !m_bControled && !m_bSummoned )
        return true;
    // -----------

    BaseCreature c = (BaseCreature)m;
    return ( m_iTeam != c.m_iTeam ||
             (m_bSummoned || m_bControled) != (c.m_bSummoned || c.m_bControled) );
}
```

**Why the guard `!m_bControled && !m_bSummoned`:**
A player's tamed horse or summoned energy vortex would otherwise treat the player's
own PlayerBots as enemies. Only creatures that belong to no player (the aggressor
NPCs we care about) get the new behaviour.

**Interaction with `OppositionGroup`:** OppositionGroup checks run first and still
override everything, so Ophidian/Savage/etc. faction mobs are unaffected.

---

### Fix 2 — `AquireFocusMob()`: PlayerBots visible in player-only passes

**File:** [Scripts/Engines/AI/AI/BaseAI.cs](Scripts/Engines/AI/AI/BaseAI.cs) ~line 2056

```csharp
// BEFORE
if ( m.Player || !bPlayerOnly )

// AFTER
if ( m.Player || m is PlayerBot || !bPlayerOnly )
```

This single character-class addition makes PlayerBots visible to AnimalAI flee
detection, PredatorAI wander scanning, and any future `bPlayerOnly = true` combat
path, without setting `Mobile.Player = true` on the bot (which risks packet-layer
side-effects because the bot has no `NetState`).

---

### Fix 3 — Aggressor-list seeding for `FightMode.Agressor` NPCs

This requires a new method in PlayerBotAI and a hook call from the bot's think loop.

#### 3a. New method `CheckDefendNearby()` in `PlayerBotAI.cs`

**File:** [Scripts/Mobiles/PlayerBot/PlayerBotAI.cs](Scripts/Mobiles/PlayerBot/PlayerBotAI.cs)

```csharp
/// <summary>
/// If a hostile mobile nearby is attacking a player or group-member, engage it
/// so we appear in its Aggressors list — allowing FightMode.Agressor NPCs to
/// subsequently target us.
/// </summary>
private void CheckDefendNearby()
{
    if ( m_Bot.Combatant != null )
        return;  // already in a fight

    IPooledEnumerable eable = m_Bot.Map.GetMobilesInRange( m_Bot.Location, m_Bot.RangePerception );

    Mobile bestTarget = null;

    foreach ( Mobile m in eable )
    {
        if ( m == m_Bot || m.Deleted || !m.Alive )
            continue;

        // Only consider hostile BaseCreatures that are actively in combat
        BaseCreature bc = m as BaseCreature;
        if ( bc == null || bc is PlayerBot )
            continue;

        Mobile combatant = bc.Combatant;
        if ( combatant == null )
            continue;

        // The NPC is fighting a PlayerMobile or another bot in our group
        bool fightingPlayer = combatant is PlayerMobile;
        bool fightingGroupMember = combatant is PlayerBot &&
                                   ((PlayerBot)combatant).Group != null &&
                                   ((PlayerBot)combatant).Group == m_Bot.Group;

        if ( !fightingPlayer && !fightingGroupMember )
            continue;

        if ( !m_Bot.CanBeHarmful( bc, false ) )
            continue;

        if ( m_Bot.ShouldAttack( bc ) )
        {
            bestTarget = bc;
            break;
        }
    }

    eable.Free();

    if ( bestTarget != null )
    {
        m_Bot.Combatant = bestTarget;
        // Landing a hit (or the mere Combatant assignment triggering AggressiveAction)
        // seeds the NPC's Aggressors list so it can retaliate against us.
    }
}
```

#### 3b. Call site in the bot's think loop

In `PlayerBotAI.cs`, inside the method that drives idle/wander behaviour (likely
`DoActivityWandering()` or the main `Think()` dispatch), add a call before the
standard activity handling:

```csharp
// In Think() or the wander/idle branch, after safety checks:
CheckDefendNearby();
```

Call it only when the bot is **not** already in combat (`m_Bot.Combatant == null`)
to avoid disrupting an active fight target.

---

### Fix 4 — Guard `ShouldAttack()` against defensive retaliation loops

When Fix 3 causes the bot to engage an NPC, that NPC may have `FightMode.Agressor`
and will now counter-target the bot.  Verify that `PlayerBot.ShouldAttack()` (lines
1064–1097 in `PlayerBot.cs`) returns `true` for any hostile BaseCreature attacking
a non-PK bot, so the bot doesn't immediately drop its combatant.

The current logic:
```csharp
// Non-PK bots attack hostile PlayerBots only — silent about wild creatures
```

If wild creatures fall outside the current `ShouldAttack()` true-path, extend it:

```csharp
// In ShouldAttack(), after the group-member check:
if ( !(other is PlayerBot) )
{
    // Attacking a normal NPC/monster — allow if it is a threat
    BaseCreature bc = other as BaseCreature;
    if ( bc != null && bc.Combatant != null )
        return true;   // It's actively in a fight; fair game
    return false;
}
```

*(Adjust to match actual existing flow rather than overwriting current PK/non-PK
branching.)*

---

## Risk Assessment

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| Player-owned pets attack bots | Low | `!m_bControled && !m_bSummoned` guard in Fix 1 |
| PK bots trigger `CheckDefendNearby` and jump into fights they should avoid | Medium | `ShouldAttack()` gate inside `CheckDefendNearby` prevents engaging targets the bot's persona wouldn't fight |
| `bPlayerOnly = true` flee AI now flees from bots (animals scatter near bots) | Low / Intentional | This is arguably correct — animals should fear "player-like" beings |
| Summoned creatures (Energy Vortex, Blade Spirit) attack bots | Medium | These are `m_bSummoned = true`, so Fix 1's guard excludes them. Verify with field test. |
| `FightMode.Agressor` NPCs perpetually chain from bot to bot | Low | `CheckDefendNearby` only fires when bot is idle; once the NPC engages, it stays on its current target until the fight resolves normally |

---

## File Change Summary

| File | Change |
|------|--------|
| [Scripts/Engines/AI/Creature/BaseCreature.cs:517](Scripts/Engines/AI/Creature/BaseCreature.cs) | `IsEnemy()` — add PlayerBot + wild-NPC guard |
| [Scripts/Engines/AI/AI/BaseAI.cs:2056](Scripts/Engines/AI/AI/BaseAI.cs) | `AquireFocusMob()` — add `m is PlayerBot` to bPlayerOnly bypass |
| [Scripts/Mobiles/PlayerBot/PlayerBotAI.cs](Scripts/Mobiles/PlayerBot/PlayerBotAI.cs) | Add `CheckDefendNearby()`, call from Think/wander loop |
| [Scripts/Mobiles/PlayerBot/PlayerBot.cs:1064](Scripts/Mobiles/PlayerBot/PlayerBot.cs) | `ShouldAttack()` — ensure wild attacking creatures return true |

---

## Testing Checklist

- [ ] Spawn a PlayerBot (non-PK) and walk near an aggressive monster (Orc, Troll,
      etc.) — monster should eventually aggro the bot as well as the player.
- [ ] Confirm player-tamed horse does not attack its owner's bot.
- [ ] Spawn a PK PlayerBot and confirm it still attacks the real player (existing
      behaviour must not regress).
- [ ] Summon an Energy Vortex and confirm it does not attack nearby bots.
- [ ] Walk a bot into a dungeon with `FightMode.Agressor` creatures; confirm the
      bot can draw aggro off the player once it enters the fight.
- [ ] Confirm animals/predators still flee from players; also verify they now react
      (flee or fight) to nearby PlayerBots.
- [ ] Two bots of different personas (PK vs Merchant) — verify PK bot still attacks
      Merchant bot per `ShouldAttack()` and the new code does not break that.
