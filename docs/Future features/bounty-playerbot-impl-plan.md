# Bounty System Extension for PlayerBot PKs — Implementation Plan

## Overview

When a PlayerBot flagged as a Player Killer (`IsPlayerKiller == true`) kills either a human player or a "blue" (non-PK) PlayerBot, a bounty must be placed on that bot's head. The bot must then appear on bounty boards across the realm. A player who kills the bot can carve its head and deliver it to a guard to collect the reward. This extends the existing player-vs-player bounty system with minimal structural surgery.

---

## Current System — Key Facts

| Component | File | Relevant Behaviour |
|-----------|------|--------------------|
| `BountyBoard` | `Scripts/Items/Misc/BulletinBoards.cs` | Static `PlayerMobile[] m_List` (top 25 by bounty). `Update(PlayerMobile)` keeps the list sorted. `GetMessages()` renders bulletin posts. |
| `BountyMessage` | same file | Wraps one bounty post; stores kill count in the `Time` ticks field. |
| `Head` | `Scripts/Items/Body Parts/Head.cs` | Created by `Corpse.cs` for `AccessLevel.Player` corpses. Constructor casts `m_Owner` to `PlayerMobile` to read `.Bounty`. Stored `m_Bounty` survives serialization. `Carve()` produces skull + brain — **no bounty reward is triggered here**. |
| `ReportMurderer` | `Scripts/Gumps/ReportMurderer.cs` | Victim's bounty dialog. `BountyEntryResponse()` calls `killer.Kills++` and `kpm.Bounty += bounty` — both behind an `if (killer is PlayerMobile)` guard. |
| `BaseGuard` | `Scripts/Mobiles/Guards/BaseGuard.cs` | `OnDragDrop()` accepts a `Head` item and pays the reward if `head.Owner is PlayerMobile` and the head is fresh and has a positive bounty. Applies stat-loss penalty to the murderer. |
| `PlayerMobile` | `Scripts/Mobiles/PlayerMobile.cs` | `ProcDeathMenus()` collects aggressors from `this.Aggressors`, calls `ReportMurderer.SetKillers()`. Fires for human-player deaths only. |
| `PlayerBot` | `Scripts/Mobiles/PlayerBot/PlayerBot.cs` | Extends `BaseCreature`. `AlwaysMurderer` returns `m_IsPlayerKiller`. No `Bounty` property. No `OnDeath()` override. |

### What Currently Breaks

1. **`Head` constructor crashes** when a PlayerBot PK is the corpse owner — `((PlayerMobile)m_Owner).Bounty` throws `InvalidCastException`.
2. **`BountyBoard.Update()`** only accepts `PlayerMobile` — bot bounties can never appear on boards.
3. **`ReportMurderer.BountyEntryResponse()`** only acts when `killer is PlayerMobile` — gold donated against a bot killer is silently discarded.
4. **`BaseGuard.OnDragDrop()`** rejects heads whose owner is not a `PlayerMobile` — a bot head can never be redeemed.
5. **Bot-kills-blue-bot** has no victim player at all, so no dialog is ever shown and no bounty is ever placed.

---

## Target Behaviour

| Scenario | Trigger | Bounty Placement | Head Drop | Head Redemption |
|----------|---------|------------------|-----------|-----------------|
| PK bot kills human player | Player death dialog | Player donates gold (existing flow) | On bot's next death | Guard pays reward, bot stat-loss skip |
| PK bot kills blue PlayerBot | Bot death | Auto system bounty (`AutoBotBounty` gp) | On bot's next death | Guard pays reward |
| Human PK kills human player | Existing | Unchanged | Unchanged | Unchanged |

---

## Architecture Changes

### 1 — Add `Bounty` and `Kills` to `PlayerBot`

**File:** `Scripts/Mobiles/PlayerBot/PlayerBot.cs`

Add two serialized fields parallel to `PlayerMobile`:

```csharp
private int m_Bounty;
private int m_Kills;

[CommandProperty(AccessLevel.GameMaster)]
public int Bounty
{
    get { return m_Bounty; }
    set
    {
        m_Bounty = value;
        BountyBoard.Update(this);   // keep the board sorted
    }
}

[CommandProperty(AccessLevel.GameMaster)]
public int Kills
{
    get { return m_Kills; }
    set { m_Kills = value; }
}
```

Add serialization in `Serialize` / `Deserialize` under a new version bump (version 2 → 3, or whatever the current max is):

```csharp
// Serialize
writer.Write((int)m_Bounty);
writer.Write((int)m_Kills);

// Deserialize (version >= NEW_VERSION)
m_Bounty = reader.ReadInt();
m_Kills  = reader.ReadInt();
```

**Constant** (top of file or in a shared constants file):

```csharp
public static readonly int AutoBotBounty = 500; // gp placed automatically when bot kills a blue bot
```

---

### 2 — Refactor `BountyBoard` to Accept `Mobile`

**File:** `Scripts/Items/Misc/BulletinBoards.cs`

Change the static list type and every method that touches it:

```csharp
// Before
private static PlayerMobile[] m_List = new PlayerMobile[BountyCount];

// After
private static Mobile[] m_List = new Mobile[BountyCount];
```

Add a private helper to read the bounty from either mobile type uniformly:

```csharp
private static int GetBounty(Mobile m)
{
    if (m is PlayerMobile pm) return pm.Bounty;
    if (m is PlayerBot pb)   return pb.Bounty;
    return 0;
}

private static int GetKills(Mobile m)
{
    if (m is PlayerMobile pm) return pm.Kills;
    if (m is PlayerBot pb)   return pb.Kills;
    return 0;
}
```

Update `Update(PlayerMobile pm)` → `Update(Mobile m)`:

```csharp
public static void Update(Mobile m)
{
    // existing sorted-insertion logic, using GetBounty(m) instead of pm.Bounty
}
```

Update `LowestBounty` property:

```csharp
public static int LowestBounty
{
    get
    {
        for (int i = BountyCount - 1; i >= 0; i--)
            if (m_List[i] != null) return GetBounty(m_List[i]);
        return 0;
    }
}
```

Update `GetMessages()` to call `GetBounty()` and `GetKills()` on the mobile rather than casting directly:

```csharp
// Replace pm.Bounty → GetBounty(m)
// Replace pm.Kills  → GetKills(m)
// Replace pm.Name   → m.Name   (already on Mobile)
```

---

### 3 — Fix `Head.cs` — Support PlayerBot Owners

**File:** `Scripts/Items/Body Parts/Head.cs`

The constructor that runs when the head is first created:

```csharp
// Before
m_Bounty = ((PlayerMobile)m_Owner).Bounty;

// After
if (m_Owner is PlayerMobile pm)
    m_Bounty = pm.Bounty;
else if (m_Owner is PlayerBot pb)
    m_Bounty = pb.Bounty;
```

No other changes needed — `m_Bounty` is already serialized independently, and the guard system reads from the head, not from the live mobile.

---

### 4 — Ensure PlayerBot PK Corpse Drops a Head

**File:** `Scripts/Mobiles/PlayerBot/PlayerBot.cs` — add `OnDeath()` override

`Corpse.cs` creates a `Head` only for `AccessLevel.Player` corpses. PlayerBot inherits `BaseCreature`, which has `AccessLevel.Player` by default, **but** the inner cast in the `Head` constructor (fixed in Change 3) is the only blocker. Verify in `Corpse.cs` that no `is PlayerMobile` type-check is guarding head creation. If one exists, widen it to `|| m is PlayerBot pb2 && pb2.IsPlayerKiller`.

Regardless, add an explicit `OnDeath()` override in `PlayerBot` to handle the **bot-kills-blue-bot auto-bounty** and to clean up AI state:

```csharp
public override void OnDeath(Container c)
{
    base.OnDeath(c);

    if (!m_IsPlayerKiller)
        return;

    // Identify the top damage dealer among aggressors
    Mobile killer = GetTopDamageDealer();
    if (killer == null)
        return;

    // Bot-kills-bot: auto-place system bounty
    // (Bot-kills-player path is handled via ReportMurderer — see Change 5)
    // This branch fires when the *victim* was a bot (we are the PK bot dying here,
    // but we need to handle the case where THIS bot killed others before dying).
    // The auto-bounty for bot-kills-bot is applied in the victim bot's OnDeath,
    // so nothing extra is needed here for that scenario.
    // What we DO handle here is dropping a head if Corpse.cs doesn't.
}
```

**Separate override for victim blue bot** — add to `PlayerBot.OnDeath()`:

```csharp
// Called on the VICTIM bot's death
// Check if the killer is a PK bot and if so, auto-place bounty
public override void OnDeath(Container c)
{
    base.OnDeath(c);

    if (m_IsPlayerKiller)
        return; // PK bot dying is handled differently — see BaseGuard / head redemption

    // We are a blue bot being killed
    Mobile topKiller = GetTopDamageDealer();
    if (topKiller is PlayerBot pkBot && pkBot.IsPlayerKiller)
    {
        pkBot.Kills++;
        pkBot.Bounty += AutoBotBounty; // triggers BountyBoard.Update via the Bounty setter
        pkBot.SendAsciiMessage(
            "The realm has placed a {0}gp bounty on your head for slaying {1}.",
            AutoBotBounty, this.Name);
    }
}
```

Add the private helper `GetTopDamageDealer()`:

```csharp
private Mobile GetTopDamageDealer()
{
    DamageEntry top = null;
    foreach (DamageEntry de in DamageEntries)
    {
        if (top == null || de.DamageGiven > top.DamageGiven)
            top = de;
    }
    return top?.Damager;
}
```

---

### 5 — Extend `ReportMurderer` for PlayerBot Killers

**File:** `Scripts/Gumps/ReportMurderer.cs`

**`BountyEntryResponse()` — current code (simplified):**

```csharp
killer.Kills++;
if (killer is PlayerMobile kpm && bounty > 0)
{
    kpm.Bounty += bounty;
    ...
}
```

**Replace with:**

```csharp
// Increment kill counter on whichever type holds it
if (killer is PlayerMobile kpm2)
    kpm2.Kills++;
else if (killer is PlayerBot pkBot2)
    pkBot2.Kills++;

// Apply bounty to whichever type holds it
if (bounty > 0)
{
    if (killer is PlayerMobile kpm && bounty > 0)
    {
        kpm.Bounty += bounty;
        killer.SendAsciiMessage("You have been reported! A {0}gp bounty is now on your head.", bounty);
    }
    else if (killer is PlayerBot pkBot && pkBot.IsPlayerKiller)
    {
        pkBot.Bounty += bounty;
        // No SendAsciiMessage to a bot — optionally notify nearby players
        // "The realm places a {bounty}gp bounty on {pkBot.Name}!"
        BroadcastBountyNotice(pkBot, bounty, from.Location, from.Map);
    }
}

// Wealth confiscation — only meaningful for human PKs (bots have no bank)
// Skip confiscation block entirely if killer is PlayerBot
```

Add the optional broadcast helper:

```csharp
private static void BroadcastBountyNotice(PlayerBot bot, int bounty, Point3D loc, Map map)
{
    IPooledEnumerable eable = map.GetMobilesInRange(loc, 18);
    foreach (Mobile m in eable)
        m.SendAsciiMessage("{0} has slain an innocent! A {1}gp bounty is now on their head.", bot.Name, bounty);
    eable.Free();
}
```

No change needed to `SetKillers()` or `SendNext()` — `PlayerMobile.ProcDeathMenus()` already iterates `this.Aggressors` which can contain any `Mobile`, including PlayerBots.

---

### 6 — Extend `BaseGuard.OnDragDrop()` for PlayerBot Heads

**File:** `Scripts/Mobiles/Guards/BaseGuard.cs`

The guard currently rejects heads whose owner is not a `PlayerMobile`. Widen the owner check and skip the stat-loss block for bots (bots have no stats to penalise in the same way):

```csharp
public override bool OnDragDrop(Mobile from, Item dropped)
{
    if (!(dropped is Head head))
        return base.OnDragDrop(from, dropped);

    Mobile owner = head.Owner;
    bool ownerIsPlayerBot = owner is PlayerBot pb && pb.IsPlayerKiller;
    bool ownerIsPlayerMobile = owner is PlayerMobile;

    if (!ownerIsPlayerMobile && !ownerIsPlayerBot)
        return base.OnDragDrop(from, dropped);

    // Existing age / bounty / self / account checks — unchanged

    int currentBounty = ownerIsPlayerBot
        ? ((PlayerBot)owner).Bounty
        : ((PlayerMobile)owner).Bounty;

    if (currentBounty <= 0 || head.Bounty <= 0)
    {
        from.SendAsciiMessage("There is no bounty on that head.");
        return false;
    }

    int reward = Math.Min(head.Bounty, currentBounty);

    // Pay reward
    from.BankBox.DropItem(new Gold(reward));
    from.SendAsciiMessage("You have received {0}gp for delivering this head to justice.", reward);

    // Deduct from perpetrator's bounty
    if (ownerIsPlayerBot)
        ((PlayerBot)owner).Bounty -= reward;  // setter calls BountyBoard.Update
    else
        ((PlayerMobile)owner).Bounty -= reward;

    // Stat-loss — only apply to human PKs (meaningful for PlayerMobile)
    if (ownerIsPlayerMobile)
        ApplyStatLoss((PlayerMobile)owner, reward);  // existing logic, extracted to helper if not already

    // Messaging
    if (reward >= 15000)
        from.SendAsciiMessage("You have rid the realm of an infamous criminal!");
    else if (reward > 100)
        from.SendAsciiMessage("You have rid the realm of a minor criminal.");
    else
        from.SendAsciiMessage("The guard mocks you for such a pathetic bounty.");

    head.Delete();
    return true;
}
```

---

## Data Flow — Sequence Diagrams

### Scenario A: PK Bot Kills Human Player

```
PlayerBot (PK) attacks PlayerMobile (victim)
  → victim.OnDeath()
  → victim.ProcDeathMenus()
      → scans this.Aggressors (includes the PlayerBot)
      → calls ReportMurderer.SetKillers(victim, [PlayerBot])
      → Timer.DelayCall → ReportMurderer.SendNext(victim)
          → BountyEntryPacket gump shown to victim
          → victim enters bounty amount
          → BountyEntryResponse():
              pkBot.Kills++
              pkBot.Bounty += gold    ← NEW branch
              BountyBoard.Update(pkBot)
              Board now shows the bot in the most-wanted list

Later: any player kills the PK bot
  → PK bot corpse drops Head (owner = PlayerBot, m_Bounty = pkBot.Bounty)
  → Player picks up head, drags to guard
  → BaseGuard.OnDragDrop():
      reward = min(head.Bounty, pkBot.Bounty)
      player.BankBox += Gold(reward)
      pkBot.Bounty -= reward
      head.Delete()
```

### Scenario B: PK Bot Kills Blue PlayerBot

```
PlayerBot (PK) attacks PlayerBot (blue victim)
  → blueBot.OnDeath()
      → GetTopDamageDealer() → pkBot
      → pkBot is PlayerBot && pkBot.IsPlayerKiller → true
      → pkBot.Kills++
      → pkBot.Bounty += AutoBotBounty (500gp)   ← NEW
      → BountyBoard.Update(pkBot)
      → BroadcastBountyNotice() to nearby mobiles (optional)

Later: player kills the PK bot
  → same head/guard flow as Scenario A
```

---

## File Change Summary

| File | Change Type | Summary |
|------|-------------|---------|
| `Scripts/Mobiles/PlayerBot/PlayerBot.cs` | Modify | Add `m_Bounty`, `m_Kills`, `Bounty` property, `Kills` property, `OnDeath()` override, `GetTopDamageDealer()` helper, `AutoBotBounty` constant. Serialize/Deserialize version bump. |
| `Scripts/Items/Misc/BulletinBoards.cs` | Modify | Change `m_List` to `Mobile[]`. Add `GetBounty(Mobile)` + `GetKills(Mobile)` helpers. Update `Update()`, `LowestBounty`, and `GetMessages()` to use helpers. |
| `Scripts/Items/Body Parts/Head.cs` | Modify | Guard the `((PlayerMobile)m_Owner).Bounty` cast with a type check for `PlayerBot`. |
| `Scripts/Gumps/ReportMurderer.cs` | Modify | Extend `BountyEntryResponse()` to handle `killer is PlayerBot`. Add `BroadcastBountyNotice()`. Skip wealth-confiscation block for bot killers. |
| `Scripts/Mobiles/Guards/BaseGuard.cs` | Modify | Widen owner check to accept `PlayerBot`. Add `PlayerBot` bounty deduction. Skip stat-loss for bot owners. |
| `Scripts/Mobiles/PlayerBot/PlayerBotAI.cs` | No change | Death handling fully managed via `PlayerBot.OnDeath()` override. |
| `Scripts/Misc/Notoriety.cs` | No change | `AlwaysMurderer` already returns red for PK bots. |

---

## Edge Cases & Invariants

| Case | Handling |
|------|----------|
| PK bot is deleted before head is turned in | `head.Owner` reference will be `null` or deleted. Guard checks `owner == null || owner.Deleted` before paying — **existing check, no change needed**. Bounty is lost (same behaviour as current system for deleted players). |
| Multiple players deal damage to PK bot | Only the top damage dealer triggers the auto-bounty in Scenario B. For Scenario A, `ProcDeathMenus()` already collects all aggressors from `Aggressors` list, so each contributing killer is listed and can receive a bounty dialog. |
| PK bot is resurrected (if resurrection is ever added) | Bounty persists on the bot (stored in `m_Bounty`). Head from the previous death was already redeemed or decayed. No stale state issue. |
| Blue bot killed by human PK (not a PlayerBot PK) | No bounty auto-placed on the human PK — the victim bot has no `ProcDeathMenus()`. Out of scope: the human PK is already covered by the existing player-kills-player system if a player was also attacked. |
| Bot kills bot repeatedly (griefing) | Bounty accumulates linearly with each kill. No cap is implemented here; consider adding `pkBot.Bounty = Math.Min(pkBot.Bounty + AutoBotBounty, MaxBotBounty)` if griefing becomes an issue. |
| Head age check | `BaseGuard` already rejects heads older than 1 day. No change needed. |
| Same account / self check | Existing account and self checks in `BaseGuard` still apply — a player cannot redeem a head they created. |
| BountyBoard top-25 cap | Bots and players compete for the same 25 slots, sorted by bounty. A bot with a 10,000gp bounty outranks a player with 500gp — this is intentional and thematically correct. |
| Bounty decay on bots | Currently `PlayerMobile` decays its bounty on login. PlayerBots are always online; add a periodic decay using `PlayerBot`'s existing `OnThink()` or a dedicated timer. Suggested: decay 100gp per real-world day, same as players. |

---

## Bounty Decay for PlayerBot

Since bots never "log in," the login-triggered decay in `PlayerMobile` does not apply. Add to `PlayerBot.OnThink()` (or the AI timer):

```csharp
private static readonly TimeSpan BountyDecayInterval = TimeSpan.FromDays(1.0);
private DateTime m_NextBountyDecay = DateTime.Now + TimeSpan.FromDays(1.0);

// Inside OnThink() or a dedicated timer callback:
if (m_Bounty > 0 && DateTime.Now >= m_NextBountyDecay)
{
    m_Bounty = Math.Max(0, m_Bounty - 100);
    m_NextBountyDecay = DateTime.Now + BountyDecayInterval;
    BountyBoard.Update(this);
}
```

Serialize `m_NextBountyDecay` alongside `m_Bounty`.

---

## Implementation Order

Complete these changes in order to avoid runtime crashes at each intermediate step:

1. **`BulletinBoards.cs`** — Widen `m_List` to `Mobile[]` and add `GetBounty`/`GetKills` helpers. The new overload `Update(Mobile)` must exist before other files call it.
2. **`Head.cs`** — Fix the `PlayerMobile` cast. Eliminates the crash if a bot head already exists in saves.
3. **`PlayerBot.cs`** — Add `Bounty`/`Kills` properties and `OnDeath()` override. Now bots have a bounty that the board can track.
4. **`ReportMurderer.cs`** — Extend `BountyEntryResponse()`. Now player-donated gold correctly targets bot killers.
5. **`BaseGuard.cs`** — Widen head acceptance. Now bot heads can be redeemed.
6. Manual test cycle (see below).

---

## Manual Test Checklist

- [ ] PK bot attacks and kills a human player → bounty dialog appears → player donates 500gp → PK bot appears on bounty board with 500gp bounty.
- [ ] Another player kills the PK bot → corpse drops a head with owner = bot → player drags head to guard → guard pays 500gp → bot's bounty clears → bot no longer on board (after `BountyBoard.Update`).
- [ ] PK bot attacks and kills a blue bot → no dialog → PK bot automatically gains 500gp bounty → PK bot appears on board.
- [ ] PK bot accumulates bounty from multiple kills → board reflects highest total.
- [ ] Head older than 1 day is rejected by guard.
- [ ] Player cannot redeem their own head or a head on the same account.
- [ ] Non-PK bot death does not place a bounty on the killer.
- [ ] Human PK kills human player — existing flow completely unchanged.
- [ ] Bounty board displays bot names alongside human names correctly.
- [ ] Server restart → bot bounty survives deserialization.
- [ ] Bounty decay: set `m_NextBountyDecay` to `DateTime.Now` via `[set]`, confirm 100gp decays on next think cycle.
