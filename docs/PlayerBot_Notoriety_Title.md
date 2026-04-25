# PlayerBot Notoriety & Paperdoll Title

Goal: PlayerBots display karma-driven title prefixes ("The Honorable", "The Dread Lord") and skill
proficiency suffixes ("Master Mage", "Grandmaster Warrior") on single-click and the paperdoll,
consistent with how those strings appear on real players.

---

## Current state

### Karma
`InitPersona()` never sets `Mobile.Karma`.  All freshly spawned bots start at 0 (neutral), so
`GetNotoTitle()` always returns bare name regardless of profile.

### Single-click display (`OnSingleClick`, PlayerBot.cs:1112-1122)
Manually builds `"[Lord/Lady] Name [Title]"`.  Does not call `Titles.ComputeTitle()`, so:
- The karma prefix ("The Honorable …", "The Dread Lord …") is never shown.
- The skill-level suffix ("Master Mage", "Grandmaster Warrior") is never shown.

### Paperdoll title (`Titles.ComputeTitle`, Titles.cs:227-245)
`GetNotoTitle()` already has a `beheld is PlayerBot` branch (line 168) that formats the karma
prefix correctly — but only once karma is non-zero.  
`ComputePlayerTitle()` is called for PlayerBots at line 232, but only when
`|karma| >= Noto.NobleLordLady (100)`.  For lower-karma bots (e.g. Honorable at 50) the skill
suffix is suppressed.

### Notoriety hue
PK bots are already red because `AlwaysMurderer` returns `m_IsPlayerKiller`.  Non-PK bots with a
human body fall through to the `target.Body.IsHuman` branch in `MobileNotoriety()` (Notoriety.cs:178)
and return `Notoriety.Innocent` (blue) — correct behavior already in place.

---

## Proposed changes

### Change 1 — Karma initialisation (`PlayerBot.cs`, `InitPersona()`)

Add a karma assignment at the end of `InitPersona()`.  Map profile × experience to a range and
pick a random value inside it.

```
Profile       | Experience   | Karma range          | Display prefix
--------------|--------------|----------------------|--------------------------------
PlayerKiller  | Newbie       | [-80, -61]           | "The Dastardly …"
PlayerKiller  | Average      | [-100, -81]          | "The Dark/Evil …"
PlayerKiller  | Proficient   | [-120, -101]         | "The Evil …"
PlayerKiller  | Grandmaster  | [-127, -121]         | "The Dread Lord/Lady …"
Crafter       | Newbie       | [0,    39]           | bare name
Crafter       | Average      | [40,   59]           | "The Honorable …"
Crafter       | Proficient   | [60,   79]           | "The Noble …"
Crafter       | Grandmaster  | [80,   99]           | "The Lord/Lady …"
Adventurer    | Newbie       | [-20,  20]           | bare name (varies)
Adventurer    | Average      | [-20,  40]           | mostly neutral
Adventurer    | Proficient   | [0,    60]           | neutral → Honorable
Adventurer    | Grandmaster  | [30,   80]           | Honorable → Lord/Lady
```

Use `Utility.RandomMinMax(lo, hi)` (or `lo + Utility.Random(hi - lo + 1)`) to pick within range.
`Mobile.Karma` is serialised by the base class — no version bump required.

```csharp
// append at the end of InitPersona(), after m_IsPlayerKiller is set
int lo, hi;
switch (m_Persona.Profile)
{
    case PlayerBotPersona.PlayerBotProfile.PlayerKiller:
        switch (m_Persona.Experience)
        {
            case PlayerBotPersona.PlayerBotExperience.Newbie:
                lo = -80;  hi = -61;  break;
            case PlayerBotPersona.PlayerBotExperience.Average:
                lo = -100; hi = -81;  break;
            case PlayerBotPersona.PlayerBotExperience.Proficient:
                lo = -120; hi = -101; break;
            default: // Grandmaster
                lo = -127; hi = -121; break;
        }
        break;
    case PlayerBotPersona.PlayerBotProfile.Crafter:
        switch (m_Persona.Experience)
        {
            case PlayerBotPersona.PlayerBotExperience.Newbie:
                lo = 0;  hi = 39; break;
            case PlayerBotPersona.PlayerBotExperience.Average:
                lo = 40; hi = 59; break;
            case PlayerBotPersona.PlayerBotExperience.Proficient:
                lo = 60; hi = 79; break;
            default: // Grandmaster
                lo = 80; hi = 99; break;
        }
        break;
    default: // Adventurer — wide band, skewed positive as experience rises
        switch (m_Persona.Experience)
        {
            case PlayerBotPersona.PlayerBotExperience.Newbie:
                lo = -20; hi = 20; break;
            case PlayerBotPersona.PlayerBotExperience.Average:
                lo = -20; hi = 40; break;
            case PlayerBotPersona.PlayerBotExperience.Proficient:
                lo = 0;   hi = 60; break;
            default: // Grandmaster
                lo = 30;  hi = 80; break;
        }
        break;
}
Karma = lo + Utility.Random(hi - lo + 1);
```

---

### Change 2 — `ComputeTitle` threshold (`Titles.cs`, line 232)

Show the skill-level suffix for **all** PlayerBots, not only those with |karma| >= 100.
Replace the current condition:

```csharp
// BEFORE
if (beheld is PlayerBot && (beholder == beheld || Math.Abs(beheld.Karma) >= (int)Noto.NobleLordLady || ...))
```

```csharp
// AFTER — always show skill level on PlayerBots
if (beheld is PlayerBot)
{
    ComputePlayerTitle(beholder, beheld, title);
}
```

The existing `else if (customTitle …)` and `else if (beheld.Player …)` branches remain unchanged.

---

### Change 3 — `OnSingleClick` title display (`PlayerBot.cs`, lines 1112-1122)

Replace the manual string-builder block with a call to `Titles.ComputeTitle()` so the same karma +
skill formatting logic is shared with the paperdoll path.

```csharp
// BEFORE
System.Text.StringBuilder sb = new System.Text.StringBuilder();

if ( Karma >= (int)Noto.LordLady || Karma <= (int)Noto.Dark )
    sb.Append( Female ? "Lady " : "Lord " );

sb.Append( Name );

if ( ClickTitle && Title != null && Title.Length > 0 )
{
    sb.Append( ' ' );
    sb.Append( Title );
}
```

```csharp
// AFTER
System.Text.StringBuilder sb = new System.Text.StringBuilder( Titles.ComputeTitle( from, this ) );
```

The frozen/blessed suffixes that follow are appended to `sb` unchanged.

> Note: `Titles.ComputeTitle` already calls `GetNotoTitle()` internally (which handles the
> "The Dread Lord …" prefix) and `ComputePlayerTitle()` (skill suffix) — so after Change 2, the
> full title is built in one place.

---

## Files to edit

| File | Section | What changes |
|------|---------|--------------|
| `Scripts/Mobiles/PlayerBot/PlayerBot.cs` | `InitPersona()` | Add karma range assignment at end |
| `Scripts/Mobiles/PlayerBot/PlayerBot.cs` | `OnSingleClick()` (lines 1112-1122) | Replace manual sb block with `Titles.ComputeTitle()` |
| `Scripts/Misc/Titles.cs` | `ComputeTitle()` (line 232) | Remove |karma| >= 100 gate for PlayerBots |

No serialisation version bump required (`Mobile.Karma` is already serialised by the base class).
No changes to `Notoriety.cs` — PK bots are already red via `AlwaysMurderer`; non-PK human-body
bots already return `Notoriety.Innocent` (blue) through the existing `Body.IsHuman` branch.

---

## Expected results after implementation

| Profile + Experience | Hue | Single-click / paperdoll title |
|----------------------|-----|-------------------------------|
| PK Grandmaster       | Red | The Dread Lord Thorgrim, Grandmaster Warrior |
| PK Proficient        | Red | The Evil Lord Thorgrim, Master Swordsman |
| Adventurer Grandmaster | Blue | The Lord Thorgrim, Grandmaster Mage |
| Crafter Grandmaster  | Blue | The Lord Elara, Grandmaster Blacksmith |
| Crafter Average      | Blue | The Honorable Elara, Expert Blacksmith |
| Crafter Newbie       | Blue | Elara, Apprentice Blacksmith |
| Adventurer Newbie    | Blue | Thorgrim (neutral range, bare name) |

---

## Out of scope / not changed

- `AlterNotoriety` rate-limiting (15-min cooldown) applies only to `PlayerMobile`; bots bypass it.
- Karma does not decay over time for bots — static at spawn, consistent with a historical snapshot
  server.
- No new `[CommandProperty]` needed: `Mobile.Karma` is already inspectable via `[props]` on the
  GM command interface.
