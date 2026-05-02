# PlayerBot Ghost Death — Design Proposal (v6)

## Overview

Dead hired PlayerBots become ghosts that behave exactly like dead players: garbled speech, ghost body, following their owner, occasionally muttering flavor lines only Spirit Speak users can understand. Resurrectable by Ankh / Healer NPC / Magery Resurrection spell / bandages. No skill penalty on resurrection.

No new NPCs. No new items. No timers the player has to chase.

---

## Ghost Speech Is Already Free

`Mobile.MutateSpeech()` (`RunUO-2.1/Server/Mobile.cs:4517`) garbles any dead mobile's speech into OooOOoO and tags it with `m_GhostMutateContext`. The speech loop then calls `CheckHearsMutatedSpeech()` per listener:

```csharp
if ( context == m_GhostMutateContext )
    return (m.Alive && !m.CanHearGhosts);
```

A listener hears garbled speech only if they are alive **and** don't have `CanHearGhosts`. Activating Spirit Speak (`SpiritSpeak.cs:37`) sets `m.CanHearGhosts = true` for a duration scaling with skill (10 seconds at 50, ~3 minutes at GM). This is per-listener and per-packet — two players standing next to the same ghost hear a different version depending on their Spirit Speak state.

A dead PlayerBot benefits from all of this with zero extra code, because setting `IsDeadPet = true` zeroes Hits, making `Alive == false`, which triggers `MutateSpeech()` in the base class.

---

## What Survives Death and Resurrection

The bot is never deleted — the same Mobile object transitions between living and ghost states. The only things lost are items on the corpse.

| Property | Survives? | Notes |
|---|---|---|
| Name | Yes | Mobile field |
| Skin hue | Yes | Mobile field, not touched by death |
| Hair / beard items | Yes | Layer items, exempt from corpse transfer |
| Hair / beard hue | Yes | Same |
| Speech hue | Yes | Mobile field |
| Skills | Yes | Mobile fields |
| Str / Dex / Int | Yes | Mobile fields |
| Persona (Profile, Experience, CombatStyle) | Yes | PlayerBot serialized fields |
| `ControlMaster` link | Yes | Cleared only on release |
| Equipped armor / weapons | **No** | Drop on corpse — bot returns naked |
| Backpack contents | **No** | Drop on corpse |
| Reagents | **No** | Drop on corpse |

The bot comes back as themselves, wearing just a new "resurrection" Robe, just as a freshly resurrected player would. The owner loots the corpse and re-equips them as desired.

### Why not use `IsBonded = true`

The obvious approach is to set `IsBonded = true` on hired bots, which triggers `BaseCreature.OnDeath()`'s bonded-pet path automatically. However `IsBonded` carries side effects: `OwnerAbandonTime` logic, bonding UI hooks, and interactions with `GiftOfLifeSpell`. These aren't broken, but they're unnecessary coupling.

The cleaner approach: **override `OnDeath()` in `PlayerBot.cs` and set `IsDeadPet = true` directly.** `ResurrectPet()` only checks `IsDeadPet`, not `IsBonded`. `CanBeDamaged()` in `BaseCreature` returns false when `IsDeadPet`. Everything we need hangs off that one flag.

---

## Ghost Follow Behavior

`BaseCreature.OnDeath()` for bonded pets sets `ControlOrder = OrderType.Follow` at line 4426. Our custom `OnDeath()` replicates this manually. The existing follow logic in `PlayerBotAI.cs` then handles movement.

Add one guard at the top of `PlayerBotAI.Think()`: if `m_Mobile.IsDeadPet`, skip all combat / activity logic and execute follow-master only. The ghost drifts silently behind the owner until resurrected.

---

## Ghost Speech — Flavor Lines

While `IsDeadPet == true`, a `GhostSpeechTimer` fires at random intervals (45–90 seconds) and has the bot say one of a set of lines. Listeners with Spirit Speak active hear the real text; everyone else hears OooOOoO. This makes Spirit Speak genuinely useful when travelling with a hired companion.

Lines are drawn from two pools — generic and persona-specific — with a coin flip to decide which pool is used each time.

**Generic (any bot):**
- "Could have used a heal back there."
- "Is anyone listening?"
- "I can see the shrine from here."
- "It's cold."
- "Don't leave me here."
- "I feel... lighter somehow."
- "Next time, maybe stay closer."

**PlayerKiller:**
- "I didn't see that mage coming. Well. I did."
- "Mark my words."
- "Find whoever did this."
- "I'll be back. Don't go anywhere."

**Adventurer:**
- "Tell me you at least looted the chest."
- "I dropped my sword back there somewhere."
- "We were so close."
- "I've died in worse places. Barely."

**Crafter:**
- "That was my best armor."
- "Do you know how long that gorget took to make?"
- "I could have been at the forge right now."
- "Someone is going to pay for this leather."

---

## Exact Changes Required

### 1. `Scripts/Mobiles/PlayerBot/PlayerBot.cs`

**Override `OnDeath(Container c)`** — enter ghost state manually, set follow order, start ghost speech timer, swap body.

```csharp
public override void OnDeath( Container c )
{
    if ( Controled && ControlMaster != null )
    {
        int sound = GetDeathSound();
        if ( sound >= 0 )
            Effects.PlaySound( this, Map, sound );

        Warmode   = false;
        Poison    = null;
        Combatant = null;
        Hits = 0; Stam = 0; Mana = 0;

        IsDeadPet     = true;
        ControlTarget = ControlMaster;
        ControlOrder  = OrderType.Follow;

        Body = Female ? 0x193 : 0x192;  // human ghost body

        ProcessDeltaQueue();
        SendIncomingPacket();
        SendIncomingPacket();

        StartGhostSpeechTimer();
        CheckStatTimers();
        return;  // skip base — no corpse creation, no deletion
    }

    base.OnDeath( c );  // unhired bot: normal deletion
}
```

**Override `OnAfterResurrect()`** — restore living body, stop speech timer.

```csharp
public override void OnAfterResurrect()
{
    base.OnAfterResurrect();
    Body = Female ? 0x191 : 0x190;  // living human body
    StopGhostSpeechTimer();
}
```

**Ghost speech timer and line tables:**

```csharp
private Timer m_GhostSpeechTimer;

private void StartGhostSpeechTimer()
{
    StopGhostSpeechTimer();
    m_GhostSpeechTimer = Timer.DelayCall(
        TimeSpan.FromSeconds( Utility.RandomMinMax( 45, 90 ) ),
        OnGhostSpeak );
}

private void StopGhostSpeechTimer()
{
    if ( m_GhostSpeechTimer != null )
    {
        m_GhostSpeechTimer.Stop();
        m_GhostSpeechTimer = null;
    }
}

private void OnGhostSpeak()
{
    if ( !IsDeadPet || Deleted ) return;
    Say( PickGhostLine() );
    StartGhostSpeechTimer();  // reschedule with a new random delay
}

private static readonly string[][] m_GhostLines = new string[][]
{
    // [0] generic
    new string[] {
        "Could have used a heal back there.",
        "Is anyone listening?",
        "I can see the shrine from here.",
        "It's cold.",
        "Don't leave me here.",
        "I feel... lighter somehow.",
        "Next time, maybe stay closer.",
    },
    // [1] PlayerKiller
    new string[] {
        "I didn't see that mage coming. Well. I did.",
        "Mark my words.",
        "Find whoever did this.",
        "I'll be back. Don't go anywhere.",
    },
    // [2] Adventurer
    new string[] {
        "Tell me you at least looted the chest.",
        "I dropped my sword back there somewhere.",
        "We were so close.",
        "I've died in worse places. Barely.",
    },
    // [3] Crafter
    new string[] {
        "That was my best armor.",
        "Do you know how long that gorget took to make?",
        "I could have been at the forge right now.",
        "Someone is going to pay for this leather.",
    },
};

private string PickGhostLine()
{
    // coin flip: generic pool vs persona pool
    string[] pool = Utility.RandomBool()
        ? m_GhostLines[0]
        : m_GhostLines[(int)m_Persona.Profile + 1];
    return pool[Utility.Random( pool.Length )];
}
```

---

### 2. `Scripts/Mobiles/PlayerBot/PlayerBotAI.cs`

At the top of `Think()`, add:

```csharp
if ( m_Mobile.IsDeadPet )
    return DoActionFollow();  // ghost follows master, nothing else
```

---

### 3. `Scripts/Spells/Eighth/Resurrection.cs` — line 51

Before:
```csharp
else if ( !m.Player )
{
    Caster.SendLocalizedMessage( 501043 ); // Target is not a being.
}
```

After:
```csharp
else if ( !m.Player && !(m is PlayerBot) )
{
    Caster.SendLocalizedMessage( 501043 ); // Target is not a being.
}
else if ( m is PlayerBot )
{
    SpellHelper.Turn( Caster, m );
    m.PlaySound( 0x214 );
    m.FixedEffect( 0x376A, 10, 16 );
    ((BaseCreature)m).ResurrectPet();
    FinishSequence();
}
```

---

### 4. `Scripts/Items/Construction/Ankhs.cs`

In `Ankhs.Resurrect( Mobile m )`, before the `ResurrectMenu` gump send:

```csharp
if ( m is PlayerBot )
{
    ((BaseCreature)m).ResurrectPet();
    return;
}
```

---

### 5. `Scripts/Mobiles/Humans/Vendors/Healer.cs`

In `OnMovement`, before `m.SendMenu( new ResurrectMenu(...) )`:

```csharp
if ( m is PlayerBot )
{
    m.PlaySound( 0x214 );
    m.FixedEffect( 0x376A, 10, 16 );
    ((BaseCreature)m).ResurrectPet();
    return;
}
```

---

## Resurrection Sources

| Source | Works? | Notes |
|---|---|---|
| Ankh / Virtue shrine | Yes | 4-line hook in `Ankhs.cs` |
| Healer NPC | Yes | 5-line hook in `Healer.cs` |
| Magery Resurrection spell | Yes | ~8-line fix in `Resurrection.cs` |
| Bandages (owner on ghost) | Already works | `Bandage.cs:249` calls `ResurrectPet()` for `IsDeadPet` mobs |

---

## Ghost Speech Behavior

| Listener | Hears |
|---|---|
| Alive player, Spirit Speak inactive | OooOOoOoO (garbled) |
| Alive player, Spirit Speak active | Clear — the actual flavor line |
| Dead player (also a ghost) | Clear |
| GM / Counselor | Clear (`CanHearGhosts` always true) |

---

## Files Changed

| File | Change |
|---|---|
| `Scripts/Mobiles/PlayerBot/PlayerBot.cs` | `OnDeath()`, `OnAfterResurrect()`, ghost speech timer + line tables |
| `Scripts/Mobiles/PlayerBot/PlayerBotAI.cs` | Dead-state guard in `Think()` |
| `Scripts/Spells/Eighth/Resurrection.cs` | ~8 lines |
| `Scripts/Items/Construction/Ankhs.cs` | ~4 lines |
| `Scripts/Mobiles/Humans/Vendors/Healer.cs` | ~5 lines |
