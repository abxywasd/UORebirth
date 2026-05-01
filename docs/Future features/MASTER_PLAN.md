# UORebirth Master Plan
## Solo Player Experience on a "Populated" 1997 Shard

*Last updated: 2026-04-30*

---

## Vision

A single player logs in to UORebirth and experiences what it felt like to play Ultima Online in late 1997: a living world full of other "players" — traders haggling at the docks, adventurers forming parties for Shame, PKs lurking outside Britain, crafters pounding iron at the forge, thieves lifting pouches in the market. All of it driven by PlayerBots, with the solo player as the only human thread in the tapestry.

This document is the complete backlog of everything that needs fixing, finishing, and inventing to make that vision real.

---

## Table of Contents

1. [Critical Bugs](#1-critical-bugs)
2. [PlayerBot System — Gaps & Improvements](#2-playerbot-system--gaps--improvements)
3. [New Bot Archetypes](#3-new-bot-archetypes)
4. [Economy System](#4-economy-system)
5. [World Population & Immersion](#5-world-population--immersion)
6. [Dynamic Events](#6-dynamic-events)
7. [Core Gameplay Fixes](#7-core-gameplay-fixes)
8. [New Gameplay Systems](#8-new-gameplay-systems)
9. [GM/Admin Tooling](#9-gmadmin-tooling)
10. [Priority Matrix](#10-priority-matrix)

---

## 1. Critical Bugs

These are broken features that actively harm gameplay. Fix before anything else.

---

### 1.1 PK vs. PK Combat Freeze

**Location:** `PlayerBot.cs` — `ShouldAttack()`, `PlayerBotAI.cs` — `DoActivityCombat()`

**Problem:** PKs cannot attack each other. The `CanBeHarmful()` check fails for two `AlwaysMurderer` mobiles because the base RunUO code calls `Mobile_AllowHarmful()` with both parties flagged as red, and the internal check short-circuits. The result: two PK bots enter combat posture and stand frozen, swinging at air.

**Impact:** PKs in the world never fight each other. The PK ecosystem is static. Two PKs meeting on the road — one of 1997's defining moments — is broken.

**Fix:** Override `CanBeHarmful()` on `PlayerBot` to always return `true` when both source and target are `PlayerBot` instances. PK-vs-PK combat should be unconditionally allowed. Additionally, ensure `ShouldAttack()` properly returns `true` when a PK bot encounters another PK bot that is not in the same group.

---

### 1.2 CombatStyle Enum Is Dead Code

**Location:** `PlayerBotPersona.cs`

**Problem:** `CombatStyle { Melee, Archery, Magic, MeleeAndMagic }` is defined and never assigned or read anywhere. Combat style is instead controlled by `m_PrefersMelee`, `m_UsesMagic`, and `m_PreferedCombatSkill` on `PlayerBot`. The enum is misleading and creates ambiguity about the intended design.

**Fix:** Either delete the enum and fully commit to the existing 3-field system, or wire the enum up properly so that `CombatStyle` drives all combat style decisions from a single source of truth. The latter is cleaner long-term.

---

### 1.3 Encounter Bot System Is a Skeleton

**Location:** `PlayerBotDirector.cs` — `EncounterTick()`

**Problem:** `m_IsEncounterBot` is serialized and tracked, and `GetRegularBotCount()` correctly excludes encounter bots from the cap. However, `EncounterTick()` never actually spawns an encounter bot in a meaningfully different way from `BurstSpawnTick`. There is no encounter composition logic: no "bandit ambush," no "PK patrol," no "merchant convoy with guards." The `m_EncounterChancePct` (40%) fires and does nothing special.

**Fix:** See [Section 6.1 — Encounter System](#61-encounter-system) for the full design. At minimum, implement 3 encounter archetypes before release.

---

### 1.4 Group State Lost on Server Restart

**Location:** `PlayerBotGroup.cs` — no serialization

**Problem:** Groups are runtime-only objects. After a world save/load, all groups dissolve. Bots that were traveling together or sharing a `SharedTarget` lose cohesion instantly on restart. This creates phantom behavior where bots assigned to `BotActivity.Grouped` have no group reference and fall back to wandering.

**Fix:** Serialize group membership as a `List<Serial>` on each `PlayerBot`. On world load, reconstruct groups from the serialized membership lists using a two-pass approach (deserialize all bots first, then reform groups in a post-load hook). The group leader is the first serialized member whose serial resolves to a live bot.

---

### 1.5 Magic Item Loot Table Is Empty

**Location:** `Scripts/Misc/Loot.cs` — `m_MagicWeaponTypes` / `LootPack.cs`

**Problem:** The `MagicItems` array in `LootPack` is empty. Monster loot never drops magic weapons or armor. This is a critical gap in the 1997 UO experience — magic items were the primary progression system. Liches dropping Black Staffs, Poison Elementals dropping Silver Swords, Ancient Wyrms dropping Vanquishing weapons. Without this, dungeon runs have no meaningful reward.

**Fix:** Populate `LootPack` with era-accurate magic item drops per monster tier. See [Section 7.2 — Magic Item Drops](#72-magic-item-drops) for the full drop table design.

---

### 1.6 Bot Speech Fires Regardless of Player Proximity

**Location:** `PlayerBotAI.cs` — `MaybeSpeak()`, `MaybeSocialize()`

**Problem:** The 4% speech chance fires on every AI tick regardless of whether any player can hear it. The server sends speech packets to zero clients, wasting CPU on `PublicOverheadMessage` calls. More importantly, the `InConversation` flag on distant bots blocks conversation starts even when no player is anywhere near.

**Fix:** Gate `MaybeSpeak()` and `MaybeSocialize()` behind `AnyPlayersInRange()`. Use the existing cached observation check — if no players are within observation radius, skip speech entirely. This also makes bot conversations feel intentional rather than happening in empty wilderness.

---

### 1.7 Bots Do Not Loot Corpses After Combat

**Location:** `PlayerBotAI.cs` — `DoActivityCombat()` post-kill

**Problem:** When a bot kills a creature or player, it moves on immediately. In 1997 UO, players always looted corpses. The absence of looting makes bot PKs feel mechanical and removes a core social behavior (PKs dancing on your corpse while taking your regs is iconic).

**Fix:** After a combat kill, add a `DoActivityLoot` state: the bot moves to the corpse, sends an animation, and after a short delay (2–4 seconds), transfers random items from the corpse backpack to its own. For PK bots, prioritize: gold > reagents > magic items > weapons/armor. Non-PK bots loot creature corpses for gold and gems only. Add speech from `PlayerBotSpeaker` on successful loot ("Ha! Worth the effort.").

---

### 1.8 Bots Do Not Use Healing Potions

**Location:** `PlayerBot.cs` — `InitBackpack()`, `PlayerBotAI.cs`

**Problem:** Bots carry `Bandages` but the AI never uses healing potions even though potions are standard UO kit. Potion use was a core combat skill in 1997. Bandages require a skill check and take time; potions are instant.

**Fix:** Add `HealPotion` (3–8) and `CurePotion` (2–4) to backpacks (Proficient+). In `PlayerBotAI`, after the self-heal spell check, add a potion check: if not mage or out of mana and Hits < 60% max, drink a heal potion (1.5s delay, RunUO `Item.Delete` after use with a drink animation). Poison cure check similarly.

---

## 2. PlayerBot System — Gaps & Improvements

Implemented features that are incomplete or could be substantially better.

---

### 2.1 Bot Progression Over Time

**Current state:** Skill gain fires via `PlayerBotSkillTimer` and `TryGainCombatSkills()`, but `PlayerBotExperience` never changes. A Newbie bot gains skill to 100 and still has Newbie stats and Newbie armor.

**Design:** Every time a bot's total skill gains cross a threshold, upgrade its `Experience` tier. On tier upgrade:
- Re-roll stats (higher bracket) with a `StatMod` rather than permanently setting raw stats, preserving compatibility with the base system.
- Upgrade armor: call `InitArmor()` again and delete old armor pieces.
- Update gold/backpack contents.
- Send an emote: "Gained experience in battle." or profile-specific speech.
- Log the upgrade in `PlayerBotDirector` for GM visibility.

Thresholds (total skill points gained since last upgrade):
- Newbie → Average: 150 total skill points
- Average → Proficient: 400 total skill points  
- Proficient → Grandmaster: 800 total skill points

This gives the world a dynamic population where bots that survive longer become visibly more dangerous.

---

### 2.2 Persistent Named Bot Roster

**Current state:** Bots are spawned with random names from a pool. There is no concept of "this is Gareth the blacksmith who lives in Britain." Bots are anonymous and interchangeable.

**Design:** The `PlayerBotDirector` maintains a `NamedRoster` — a fixed list of named bot configurations (name, profile, home POI, experience tier). When a named bot dies and despawns, the roster slot is marked "vacant." The director will eventually respawn that specific named bot at their home POI. Named bots carry their name in their title and have POI-specific speech topics. This creates recurring NPCs the player comes to recognize.

Implementation: Add `m_RosterSlot` (int, -1 = anonymous) to `PlayerBot`. `PlayerBotDirector` serializes `NamedRosterVacancies` (List<int>). On director tick, if a slot is vacant and enough time has passed (configurable, default 30 minutes), respawn.

A default roster of 20–30 named bots covers major towns and dungeon adventurers.

---

### 2.3 Bots Respawn After Death

**Current state:** Bots are permanent until deleted by the director's despawn logic. If a bot dies in combat, the corpse persists and the bot serial is never cleaned up properly, potentially leaking from the director's `m_BotSerials` list.

**Design:** When a `PlayerBot` dies (override `OnDeath()`):
1. Set a `m_RespawnTime` field to `DateTime.Now + TimeSpan.FromMinutes(10 + Utility.Random(20))`.
2. The bot's corpse should drop loot normally (handled by base).
3. The director's prune tick checks: if bot is dead and respawn time has passed, delete the corpse, remove from `m_BotSerials`, and respawn a fresh bot at the bot's home POI.
4. Named bots respawn at their designated home POI. Anonymous bots respawn at any underpopulated POI.
5. Add speech from bystanders: if another bot witnessed the death and is nearby when the player is present, they may mention it ("I saw what happened to Gareth. Shame.").

---

### 2.4 Bot-to-Bot Trade & Item Passing

**Current state:** Bots carry items in backpacks but never exchange them with each other or with the player (except hire/dismiss). The economy is static.

**Design:** In `DoActivityTownVisit`, bots occasionally "trade" with each other. Implementation: when two bots are within 2 tiles during a town visit, there is a 5% chance per tick that one bot transfers a random non-essential item (food, extra weapon, reagent stack) to the other. No gump needed — just `item.MoveToWorld` + speech ("Here, take this. I found it in the dungeon.").

For player interaction: add a `[vendorBuy` keyword that opens a simple gump listing items from the bot's pack at 50–150% of their base value. PKs never sell. Crafters have the best stock. This makes the player's relationship with bots economic as well as combat-oriented.

---

### 2.5 Bot Banking

**Current state:** Bots never visit banks. Banks exist but are entirely player-facing.

**Design:** Add `BotActivity.Banking` as a new activity state. After a successful dungeon run (Hunting activity followed by returning to town), bots with more than 500 gold occasionally travel to the nearest bank POI and "deposit" (simply delete) their excess gold above a comfort threshold. This is purely behavioral — there is no actual bank account — but bots saying "Off to the bank" and walking to Britain Bank creates world texture. Crafters with surplus crafted goods may "sell" to the local vendor NPC (walk to vendor tile, send speech, delete item, add gold).

---

### 2.6 More Bot Conversation Topics

**Current state:** `PlayerBotConversation.cs` has 10 hardcoded topics. After a few hours of play the player will have seen every conversation.

**Design:** Expand to 40–60 topics across categories:

**Locations:** Specific dungeon floors, notable monster spawns, safe travel routes, hidden spots  
**PvP/PKs:** Famous PK guilds, safe vs. dangerous roads, warnings about specific areas  
**Economy:** Ore prices, reagent shortages, where to sell loot, GM item crafting costs  
**World history:** UO lore references, in-game rumors, legendary player deeds  
**Skills:** Tips for skill training, arguing over which combat skill is best  
**Current events:** References to things the player recently did (see [Section 5.3 — World Memory](#53-world-memory))  

Topics should be profile-weighted: crafters talk economy, adventurers talk dungeons, PKs talk victims.

---

### 2.7 Bots React to Player Actions

**Current state:** Bots are mostly oblivious to what the player does. They have greeting speech but no reaction to player events.

**Design:** Add a lightweight event hook system:

| Player Event | Bot Reaction |
|---|---|
| Player kills a creature nearby | Nearby bots applaud or cheer |
| Player dies nearby | Nearby bots react with concern or mockery (based on profile) |
| Player attacks a bot | All nearby bots of same profile run or fight |
| Player casts a spell | Bots remark on the magic display |
| Player enters a dungeon | Dungeon bots may greet or warn |
| Player levels up a skill | A watching bot may compliment |
| Player equips a notable item | "Nice halberd. Where'd you find that?" |

Implement as `OnPlayerEvent(PlayerEventType type, Mobile player)` static method on `PlayerBotSpeaker`, called from appropriate hooks in `PlayerMobile` and `PlayerBotAI`.

---

### 2.8 Mounted Bots

**Current state:** Bots are never mounted. In 1997 UO, experienced players almost always rode horses. The absence of mounted bots is immediately noticeable.

**Design:** During `InitOutfit()` for Proficient+ non-Crafter bots, 40% chance of spawning with an `EtherealHorse` (or a real `Horse` as a pet). Mounted bots use `CurrentSpeed = 0.1` (faster than walking). The horse despawns with the bot on `OnDeath()`. Pure mages never mount (they cast from standstill). Grandmaster PKs are always mounted.

For real horses: spawn a `Horse` pet, set `ControlMaster = bot`, `ControlOrder = OrderType.Follow`. The horse AI handles following. On `BotActivity.Combat`, the bot dismounts (call `Mobile.Mount = null`) — in 1997 UO you couldn't cast while mounted and melee while mounted had a delay.

---

### 2.9 Stealth & Hiding Bot Behavior

**Current state:** No bots use Hiding or Stealth. In 1997 UO, thieves and some PKs were constantly hiding and sneaking.

**Design:** PK bots with the `DetectHidden`, `Hiding`, and `Stealth` skills (newly added to their skill set during init) should occasionally hide when a player approaches. Implementation in `PlayerBotAI.Think()`: if PK bot has Hiding ≥ 50 and a player enters range who is not their target, 25% chance to use the Hiding skill (call `Skills[SkillName.Hiding].Value`-based success check, set `Hidden = true`). While hidden, the bot switches to `BotActivity.Stalking` (new sub-state of Wander), slowly following the player. After 30–60 seconds, the bot either reveals itself and attacks (ambush) or abandons if the player moves too far away.

Thieves (a new persona — see Section 3) use Stealth for stealing, not combat.

---

### 2.10 Bandage Use by Melee Bots

**Current state:** `Bandages` are in bot backpacks but never used. The base `BaseCreature` does not automatically use bandages like players do.

**Design:** Add a `TryUseBandage()` method to `PlayerBotAI`. If the bot has bandages and Hits < 70% max and is not currently in melee range of an enemy, spend 8 seconds (timer) applying a bandage: restore `HealAmount = (int)(Healing * 0.18 + Anatomy * 0.18)` HP (approximating 1997 formula). Consumes one bandage on completion. PKs doing this mid-combat while running away is iconic behavior.

---

## 3. New Bot Archetypes

The current three profiles (PlayerKiller, Crafter, Adventurer) cover only part of the 1997 player archetype space. These additions dramatically increase world variety.

---

### 3.1 Thief Bot

**Profile addition:** `PlayerBotProfile.Thief`

**Behavior:**
- Skills: Stealing (70–100), Snooping (60–100), Hiding (70–100), Stealth (50–80), Lockpicking (40–70), Arms Lore (30–60)
- Spawns in towns and near banks. Wears dark clothing, leather gloves, no obvious weapons (concealed dagger).
- Activity loop: `TownVisit` → `Steal` (attempt to steal from player or bot backpack) → `Hide` → `TownVisit`
- Stealing attempt: walk within 1 tile of target, use Snooping to check pack (speech: "What's in here..."), then attempt Stealing skill check. On success, transfer a random non-quest item. On failure, become criminal, flee.
- If caught (player uses `DetectHidden` or Snooping back), the thief runs.
- Never initiates combat unless cornered. Carries a dagger for escape only.

**Immersion value:** High. Players chasing a thief through Britain market is peak 1997 UO.

---

### 3.2 Bard Bot

**Profile addition:** `PlayerBotProfile.Bard`

**Behavior:**
- Skills: Musicianship (80–100), Peacemaking (60–90), Provocation (50–80), Discordance (40–70), Magery (30–60)
- Always carries an instrument (Lute, Harp, or Drums). Wears colorful clothing.
- Activity loop: `TownVisit` (plays music, 10% chance per minute of Peacemaking affecting nearby aggressed mobs), `Adventuring` (joins dungeon groups), `Provocation` (provokes monsters against each other in dungeons)
- In group combat: uses Provocation to send a weaker monster at a stronger one, reducing the number the group faces.
- Passive: while in town, plays music (`PlayInstrumentAnimation()`) every 30–90 seconds. Nearby bots may "dance" (random direction changes + emote).
- Peacemaking in town: if two NPCs are fighting near a guard zone (e.g., bard bot near a combatant bot), attempt to pacify the aggressor.

**Immersion value:** High. A lute playing outside the Skara Brae pub while adventurers drink inside is exactly right.

---

### 3.3 Fisher Bot

**Profile addition:** Sub-activity for `Adventurer`, or standalone profile `PlayerBotProfile.Fisher`

**Behavior:**
- Skills: Fishing (50–100), Cooking (30–70)
- Spawns near water bodies: docks at Britain, Vesper, Ocllo, Moonglow, Minoc, Buccaneer's Den.
- Activity loop: `TownVisit` (dock area) → `Fishing` (stand at water edge, play fishing animation every 10s, gain Fishing skill, occasionally add raw fish to backpack)
- After fishing for 10–20 minutes, walks to nearby town and sells raw fish to a cook vendor.
- Speech: fishing tips, sea stories, weather observations.
- Occasional "big catch" event: emotes excitement and holds up a large fish.

**Immersion value:** Medium-High. The docks feel lifeless without fishers.

---

### 3.4 Miner/Lumberjack Bot

**Profile addition:** Sub-activity for `Crafter`, or standalone

**Behavior:**
- Skills: Mining (50–100) or Lumberjacking (50–100), Blacksmithy or Carpentry (for crafters)
- Miners: travel to known mining areas (caves, mountain passes). Use Mining animation every 8s. Accumulate ore in backpack (add `OreType.Iron` items). Travel back to town to smelt/sell. Occasionally find gems.
- Lumberjacks: travel to wooded areas. Use Lumberjacking animation. Accumulate boards. Travel back to carpenter vendor to sell.
- Both carry appropriate tools (Pickaxe or Hatchet) prominently equipped.
- Danger: Miners in deeper areas (Minoc mines, Shame level 1) may be attacked by creatures. They fight or flee based on experience tier.

**Immersion value:** High. A world where you see miners walking to the hills with pickaxes and returning loaded with ore makes resource gathering feel real.

---

### 3.5 Murderer-Hunted Bot (Escort Variant)

Not a full profile but a situational spawn: a bot that is explicitly fleeing a pursuing PK bot. This creates visible drama in the world.

**Trigger:** When a PK bot successfully kills another bot and the director observes it, 30% chance to spawn an "alarmed survivor" bot at that location. The survivor runs toward the nearest guarded town, occasionally crying for help. Once in town, they recover and resume normal activity. The PK may continue pursuing into the guard zone (suicidal if PK goes inside).

---

## 4. Economy System

The 1997 UO economy was vibrant, chaotic, and driven entirely by players. This section builds a simulated version of that.

---

### 4.1 Bot Vendor Purchasing

**Current state:** Player vendors exist but bots never interact with them.

**Design:** During `DoActivityTownVisit`, a bot near a `PlayerVendor` has a 2% chance per tick of "purchasing" from it. Implementation: pick a random item from the vendor's inventory under 200 gold. Remove the item from the vendor, add gold to the vendor's balance, add the item to the bot's backpack. The bot says "I'll take this." The player vendor's gold increments as if a real sale occurred.

This gives player vendors passive income and makes the player economy feel real even when the player is the only human.

---

### 4.2 Crafter Bot Production Pipeline

**Current state:** `PlayerBotCrafter` simulates crafting without producing real items. The bot wastes ingots and gains skill, but nothing enters the world economy.

**Design:** Rework `PlayerBotCrafter` to produce real items (using existing `CraftSystem` internals where possible). The crafted item is placed in the bot's backpack. After accumulating 3–5 crafted items, the bot travels to the appropriate vendor NPC and "sells" them (delete items, add gold to bot's pack). Grandmaster crafters have a 5% chance to produce a GM-marked item (add a `Crafter` property string — `PlayerBot`'s name). These GM items are loot-able from the bot's corpse and enter the player economy.

The pipeline: Miner bot mines ore → sells to Crafter bot → Crafter smelts and crafts → sells to Vendor NPC → Adventurer bot buys from Vendor → Adventurer bot goes to dungeon. This is a closed loop but creates the texture of a real economy.

---

### 4.3 Dynamic Gold Economy

**Current state:** Gold spawns from loot and disappears when spent. No tracking.

**Design:** Add a lightweight `EconomyTracker` singleton (Item on Map.Internal) that records:
- Gold in circulation (estimated from bot inventories, sampled on director tick)
- Total gold generated per hour (monster loot)
- Total gold destroyed per hour (vendor purchases by bots)

If gold in circulation exceeds a configurable threshold, reduce loot gold amounts by 10% until equilibrium. If too low, increase. This prevents hyperinflation in long-running servers and keeps bot gold amounts meaningful.

---

### 4.4 Reagent Supply & Demand

**Current state:** Reagents drop from certain monsters but price is fixed at vendor.

**Design:** Mage bots consume reagents during combat (already implemented). When a bot's reagent supply falls below threshold, it travels to a Mage vendor to restock. The vendor NPC should stock a limited supply (200 per type, restocking every 60 minutes in-game). If many mage bots are restocking simultaneously, the vendor runs low. A bot that finds the vendor sold out complains in speech and travels to an alternate vendor in another town.

This creates cross-town travel patterns that feel realistic.

---

## 5. World Population & Immersion

Systems that make the world feel lived-in beyond combat and crafting.

---

### 5.1 Merchant Convoy System

**Design:** Every 45–90 minutes, the director spawns a merchant convoy: 1 Crafter bot (the merchant) + 2–4 Adventurer bots (guards) traveling together from one town to another along a road route. The merchant carries valuable crafted goods and substantial gold (500–2000 gold for Grandmaster convoys). The convoy uses the `BotActivity.Grouped` system.

PKs within range scan for convoys and may intercept them. The guards fight; the merchant flees toward the destination town. If the merchant is killed, the gold and goods are lootable.

The convoy announces its departure from the origin town (public speech: "Heading to Vesper with trade goods. Watch the road.") and its arrival. Players can escort convoys for an implied reward, or join the PKs attacking them.

Convoy routes: Britain→Vesper, Britain→Yew, Minoc→Britain, Trinsic→Britain, Skara Brae (ferry dock)→Britain.

---

### 5.2 Town Crier System

**Design:** A `TownCrier` bot (new NPC type, separate from `PlayerBot`) spawns in each major town. Every 20–30 minutes it announces recent world events in `PublicOverheadMessage` style:

- Recent player kills: "Hear ye! `PlayerName` was slain by the murderer `BotName` on the road to Vesper!"
- Dungeon activity: "Adventurers have been seen entering the dungeon Shame in great numbers!"
- Merchant news: "A shipment of iron ore arrived in Britain this morning!"
- PK bounties: "A bounty of 500 gold has been placed on the head of `PKBotName`!"
- Guild news: "The guild `GuildName` has declared war upon `GuildName2`!"

Implementation: `TownCrierBot` is a simple NPC that reads from a `WorldEventLog` (List<string> on `PlayerBotDirector`) and broadcasts entries on a timer. `PlayerMobile` and `PlayerBotAI` write to `WorldEventLog` on significant events.

---

### 5.3 World Memory System

**Design:** `PlayerBotDirector` maintains a `WorldEventLog` (capped at 50 entries, FIFO). Events are written by:
- Player kills a creature
- Player kills or is killed by a bot
- Player enters a dungeon
- A PK bot kills another bot
- A named bot completes a significant task

Bots with relevant topics reference this log in speech. A bot near Britain bank might say: "I heard you took out a lich in Deceit yesterday. Impressive." This makes the world feel aware of the player's actions without breaking the NPC illusion.

Log entries include: type, actor name, target name, location, timestamp. Bots check the log for entries within the last 2 hours. Speech templates parameterize log fields.

---

### 5.4 Bot Guilds

**Current state:** The guild system exists (GuildStones, deeds) but bots never form guilds.

**Design:** Create 3–5 persistent bot guilds with names pulled from 1997 UO PvP culture:

**PK guilds** (red): e.g., "The Brotherhood of Chaos," "Dark Alliance"  
**Defender guilds** (blue): e.g., "The Order of Silver," "Guardians of Sosaria"  
**Crafter guilds** (neutral): e.g., "The Artisans Guild," "Ironworkers Union"  

Guilds are hardcoded configurations in `PlayerBotDirector`. On server start, if the guilds don't exist in the save (check by guild name), create them via `Guild` constructor and assign member bots as they spawn. PKs of the same guild don't attack each other (uses the existing guild ally check in `Notoriety.cs`). Defender guilds are at war with PK guilds (persistent `AddEnemy` guild war declaration).

Guild membership is shown in bot title. Bots of the same guild assist each other in combat automatically (the existing `CheckDefendNearby` already does this — just wire it to guild membership).

---

### 5.5 Tavern & Social Hub System

**Design:** Designate several static locations as "social hubs" (taverns, bank steps, dungeon entrances) where bots cluster more densely, sit (use Chair items via `Mobile.Direction` + emote), drink (eat/drink animation), and have conversations at higher frequency.

Implementation: Add `BotActivityType.Socializing` — a sub-state of TownVisit where the bot finds the nearest chair/table and "sits" (stops moving, faces the table, does periodic drink animations). `MaybeSocialize()` fires more frequently in these zones (10% per tick vs. 1%).

Tag POIs in `PlayerBotPOI` with `IsHub = true`. Bots assigned to Hub POIs have 70% weight toward social activities.

---

### 5.6 Day/Night Behavior

**Current state:** Bots behave identically at all hours.

**Design:** Add time-of-day awareness using `Clock.GetTime()`:

| Time | Behavior Changes |
|---|---|
| Day (6am–8pm) | Normal activity distribution |
| Dusk (8pm–10pm) | Bots return toward towns; speech: "Getting dark, best head in." |
| Night (10pm–6am) | 30% of non-PK bots switch to `Idle` (simulate sleeping). PKs are MORE active. Dungeon bots continue normally. |
| Midnight (12am–2am) | Very few bots in the open world. Graveyard/Deceit bots more active (Spirit Speak users). |

PKs prefer night activity (additional hunting weight at night). This creates a natural ebb and flow that matches how a real shard behaved.

---

### 5.7 PK Bounty System

**Design:** When a PK bot kills the player or another blue bot in front of the player, a bounty is placed on that PK. Implementation:

- `BountyBoard` (a static item placeable in town) displays current bounties.
- Bounties are stored in `PlayerBotDirector.m_Bounties` (Dictionary<Serial, int>).
- Bounties accumulate: each kill adds `ExperienceMultiplier * 100` gold to the bounty.
- When any bot kills a PK with an active bounty, the bounty gold is distributed to nearby blue bots as a "reward" (they gain it in speech: "That's for the bounty on `PKName`'s head.").
- The player killing a bounty-tagged PK receives a system message about collecting the bounty (gold added to their pack from a spawned bag).
- Bounties decay at 100 gold per in-game hour.

This gives the player a reason to hunt specific PKs and creates named villains in the world.

---

## 6. Dynamic Events

Scripted and procedural events that break routine and make the world feel alive.

---

### 6.1 Encounter System (Full Design)

Replace the skeleton `EncounterTick()` with real encounter archetypes. The director rolls for an encounter every `m_EncounterTickSeconds` (15s default) within range of any player. On success (40% base chance), pick a weighted archetype:

**Archetype 1: Bandit Ambush (30% weight)**
- 2–4 PK bots spawn in cover near the player's current road path.
- They wait 10 seconds (simulate lying in wait) then charge.
- Speech: "Stand and deliver!" / "Your gold or your life!"
- Encounter bots despawn 8 minutes after player leaves range.

**Archetype 2: Wandering Adventurer Party (25% weight)**
- 2–3 Adventurer bots spawn heading toward a nearby dungeon entrance.
- They greet the player, may ask if they want to join (player can follow and be "in the group").
- If the player tags along, the group enters the dungeon together.

**Archetype 3: Merchant in Distress (20% weight)**
- A Crafter bot spawns on the road, visibly fleeing (running animation, panicked speech).
- 1–2 PK bots spawn 20 tiles behind, chasing.
- If the player helps the merchant escape (kills or deters PKs), the merchant thanks them and gives a reward item.
- If the player ignores it, the merchant is killed and the PKs loot the body.

**Archetype 4: PK Patrol (15% weight)**
- 2–3 PK bots in a loose formation traveling a road together.
- They spot the player and evaluate: attack immediately (if player is gray/red) or tail menacingly for 2 minutes before either attacking or losing interest.

**Archetype 5: Dungeon Survivor (10% weight)**
- A single heavily-damaged Adventurer bot runs out of a dungeon entrance.
- Very low HP (10–30%), no armor, carrying a strange magic item.
- Says: "Something terrible... inside... get away!" and collapses (plays death animation, not actually dead).
- After 60 seconds recovers and resumes normal activity.

Encounter bots are flagged `IsEncounterBot = true` and despawn via existing director logic after 8 minutes unobserved.

---

### 6.2 Monster Invasion Events

**Design:** Every 3–7 days of real-time server uptime, a major invasion event triggers. The director selects a town (or major road) and spawns a surge of high-tier creatures attacking it. Town guards and any blue bots in the area assist in defense. PKs may use the chaos to attack blue players and bots. The player is the de facto hero who can turn the tide.

Implementation:
- `InvasionEvent` class (Item on Map.Internal) with serialized `InvasionState` (enum: None, Active, Repelled, Failed).
- Active invasion: spawn waves of creatures (3 waves, 15-minute gap). Creature tier scales with `InvasionLevel` (1–3).
- Blue bots in the target town gain `IsDefending = true`, attack creatures on sight, and use combat speech.
- Town Crier announces the invasion at the start and the outcome.
- On player-kills + bot-kills exceeding a threshold, invasion is "repelled." Director broadcasts a victory message.

Invasion locations: Britain (rare, dramatic), Vesper (medium), Yew (common), Dungeon entrances (always ongoing, subtle).

---

### 6.3 Dungeon Ecosystem Events

**Design:** Dungeons have periodic internal events visible to any player inside:

**Creature Turf War:** Two creature factions in the dungeon fight each other (e.g., Orcs vs. Trolls on level 1 of Wrong). Bot adventurers inside may comment on the chaos.

**Boss Spawn:** A named champion creature spawns on a dungeon level (e.g., "Ancient Lich Lord" in Deceit level 4). Bot adventurers inside react ("Something big is coming...") and may flee or attack. The boss drops a guaranteed magic item on death.

**Dungeon Collapse:** A section of the dungeon becomes temporarily "unstable" — creatures in that section become aggressive without line-of-sight (their `RangeAgro` doubles). Bot bards try to calm things down.

---

### 6.4 Seasonal Events

**Christmas (already implemented):** `Scripts/Special/Christmas.cs` exists. Verify it fires and bots receive/acknowledge presents.

**New Year's (January 1):** Firework display in Britain (spawn firework items at Britain center). Bots gather and cheer. Town Crier announces the new year.

**Halloween (October 31):** Creature spawns shift to undead-heavy. Bots wear costumes (random hat/robe combo). Increased lich/wraith spawns near graveyards.

**Easter equivalent (Spring):** Easter egg hunt style — colored egg items scattered around town for a week. Bots collect them and trade them.

**Server Anniversary:** On September 20 (the date of the world save), Town Crier announces the date and bots reflect on the "old days." Nostalgic speech topics activate.

---

### 6.5 Player-Triggered Events

**Murder Investigation:** When the player is killed by a named PK bot, they can report the murder at the local Constable NPC (new static NPC). The Constable sends 2 Warrior-class defender bots to patrol the road where the murder occurred. These bots have enhanced tracking behavior and will attack the specific PK bot on sight for 30 minutes. The PK bot is aware (has a `Hunted` flag) and may go into hiding.

**Dungeon Clear Challenge:** When the player enters a dungeon, the director detects this and optionally generates a "difficulty rating" for the player's current dungeon level. After the player has been in a dungeon for 30+ minutes and killed 20+ creatures, a Town Crier message announces: "`PlayerName` has been clearing `DungeonName` for some time. Word is getting around." Adventurer bots start traveling toward that dungeon entrance to join.

---

## 7. Core Gameplay Fixes

Systems from the base RunUO code that are broken or incomplete relative to 1997 UO.

---

### 7.1 Guard Response in Guard Zones

**Location:** `Scripts/Regions/GuardedRegion.cs`

**Problem:** The `AllowReds` flag always returns `true`, meaning red players can sit in guard zones freely. In 1997 UO, reds were instantly killed by guards upon entering a guarded town. Guards should auto-aggro any red player who steps in range.

**Fix:** Implement `OnEnter(Mobile m)` override in `GuardedRegion`. When a red mobile (Karma ≤ -3125 for players, `AlwaysMurderer` for bots) enters the region, immediately call the guard summoning sequence. This is the most iconic pre-T2A mechanic and must be correct.

Also fix the guard response feedback: the commented-out town guard arrival messages should be restored. "Halt! Thou art a murderer!" is essential.

---

### 7.2 Magic Item Drops

**Location:** `Scripts/Misc/Loot.cs`, `Scripts/Mobiles/LootPack.cs`

**Design:** Populate `LootPack` with era-accurate magic item drops. Pre-T2A magic system used 6 weapon modifier tiers and 6 armor modifier tiers.

**Weapon modifiers (weakest to strongest):** Ruin, Might, Force, Power, Vanquishing  
**Armor modifiers:** Defense, Guarding, Hardening, Fortification, Invulnerability  
**Special:** Spell channeling (for mages), Supremely Accurate, Surpassingly Accurate

Drop rate by monster difficulty (approximate):
| Monster Tier | Magic Item Chance | Typical Modifier |
|---|---|---|
| Easy (Orcs, Skeletons) | 2% | Ruin/Defense |
| Medium (Ogres, Harpies) | 5% | Might/Guarding |
| Hard (Liches, Elementals) | 12% | Force-Power |
| Boss (Ancient Wyrm, Balron) | 35% | Power-Vanquishing |

Implementation: Create `MagicItemLoot` class with `Roll(int tier)` method returning a magic-modified weapon or armor piece. In `LootPack`, add `MagicItemLoot.Roll(tier)` entries to appropriate monster loot packs.

Bot bots that find magic items equip them if better than current gear (simple comparison via `WeaponDamageLevel` or `ArmorLevel`). This drives bots to dungeon content for progression.

---

### 7.3 Karma System Pacing

**Location:** `Scripts/Misc/Titles.cs`

**Problem:** Karma gain is limited to +1 per 15 minutes. For a solo player who is the only one generating karma events, this makes the notoriety system extremely slow to move. A player who has killed 10 murderers is still treated as a commoner.

**Fix:** Review the 15-minute cap for appropriateness in a solo context. Consider: the cap was designed to prevent macro-grinding karma. In a solo context where bot kills generate karma, the cap is still valid but the events driving karma (PK kills) should be more frequent. Ensure the encounter system generates enough PK activity that the karma system feels responsive. Alternatively, increase the cap to +3 per 15 minutes for solo servers and document this as an intentional configuration.

---

### 7.4 Skill Gain Rates for Solo Play

**Location:** `Scripts/Misc/SkillCheck.cs`

**Problem:** Skill gain rates were designed for a populated server where players trained on each other, had access to training NPCs with skills to match, and had consistent grinding sessions. Solo play with a single player character may encounter artificial bottlenecks.

**Analysis needed:** Profile the player's skill gain curve from 0→70 (easy), 70→90 (moderate), 90→100 (hard) for each combat skill against the current `SkillCheck` implementation. If the gain curve is too flat in the 90–100 range without other players to practice against, add a small gain bonus when the player successfully defeats a `PlayerBot` (the bot serves as a sparring partner).

**Fix:** After the player successfully hits a `PlayerBot` in combat, call a `SkillCheck.Gain` with a small bonus multiplier (1.1–1.2x) specifically for combat skills. Document this as a solo-mode adjustment.

---

### 7.5 Poison Damage Balance

**Location:** `Scripts/Misc/Poison.cs` — line 44

**Problem:** Damage is scaled by `0.75` with a note "are the t2a values too harsh?" This is undocumented and possibly wrong. Poison in 1997 UO was brutal — Greater Poison was genuinely life-threatening without a Cure spell or potion.

**Fix:** Research the original 1997 poison damage tables. If the original values are available from packet captures or archive sources, restore them. At minimum, document the scalar choice in a comment with reasoning. If the 0.75x reduction was intentional (making solo play less punishing), that's a valid solo-mode adjustment but should be explicit.

---

### 7.6 Horse Lag

**Location:** `Scripts/Mobiles/PlayerMobile.cs`

**Status:** Described in CLAUDE.md as intentionally implemented. Verify it functions correctly — mounting and dismounting should have a movement delay that makes horse ownership a tactical decision, not just a speed bonus.

---

## 8. New Gameplay Systems

Entirely new systems that didn't exist in the original codebase but enhance the 1997 experience or the solo experience specifically.

---

### 8.1 Treasure Map System (Complete)

**Status check:** RunUO 2.1 includes a basic treasure map system. Verify it is active and functional.

**Enhancements for solo play:**
- Bot adventurers carry treasure maps (5% chance on Proficient+). These are lootable from their corpses.
- Crafter bots (Cartography skill) occasionally produce treasure maps and "sell" them to other bots or leave them on vendor NPCs.
- The treasure chest spawns monsters appropriate to the map level (already in base RunUO).
- When a chest is dug up, the World Memory System logs it ("Reports of a treasure chest being unearthed near Trinsic"). Other bots (especially PKs) may head toward that area, creating danger.

---

### 8.2 Escort Quest System

**Design:** Periodically, a bot NPC generates an escort request. The player can accept by speaking "I will escort you" or similar keyword.

Implementation:
- `EscortQuestBot` (sub-state of `Adventurer` profile): spawns at a town, says "Could someone escort me to `DestinationTown`? I'll pay well."
- Player accepts → bot becomes `ControlMaster = player.Serial` equivalent (but without the RunUO control system — just uses `Follow` logic targeting the player).
- Bot announces danger if enemies approach ("Watch out!").
- On arrival at destination, bot gives the player a reward: gold (100–500g scaled to distance) + 10% chance of a magic item.
- If the bot dies during escort, the player gets a message of regret and no reward.
- If the player abandons (moves more than 50 tiles away for 2+ minutes), the bot gives up and returns to origin.

Escort destinations: short (adjacent towns, 3–5 minute walks), medium (cross-map, 10 minutes), long (near dungeon, dangerous, high reward).

---

### 8.3 Player Housing Integration with Bots

**Design:** Bots can "visit" player houses (if the house is public/open access). During `DoActivityTownVisit`, if a player's house is in the area, 10% chance the bot walks up to it and stands outside. If the door is unlocked, the bot enters, looks around, comments, and exits within 2–3 minutes.

More importantly: allow the player to designate one bot as a "house guard" (using the hire system). A hired house guard stays within 10 tiles of the house sign, attacks any criminal or red player who approaches, and sends the player a message when the house is "threatened" (if such notification systems exist).

---

### 8.4 Shard History & Lore System

**Design:** A `ShardHistoryBook` item that the player can obtain from the Town Crier or scribe. When read, it displays the top 10 world events from the `WorldEventLog`, formatted as a narrative ("On the 15th day of April, the murderer 'Shadow' did slay the merchant 'Thomas' upon the road to Vesper...").

Named bots accumulate kill counts that persist in `PlayerBotDirector`. At world save, the top 3 most dangerous PKs and top 3 most prolific adventurers are recorded in the shard history. The Town Crier may reference these: "The most feared murderer on our roads is currently Shadow, with 7 kills this week."

---

### 8.5 PvP Tournament System

**Design:** A monthly (real-time) auto-tournament. The director selects a tournament day, announces it via Town Crier a week in advance. On tournament day:
- A designated arena location is used (or generate a temporary arena with walls via multi-spawn).
- 8–16 Adventurer and PK bots are entered automatically.
- Bracket is determined randomly.
- Fights are 1v1, within the arena, to defeat (not death — loser drops to 1 HP and yield).
- Town Crier announces results each round.
- The player can enter the tournament by speaking "I wish to enter" to the tournament master bot.
- Winner receives a special prize: a GM-crafted weapon or named magic item.

This gives the player a structured PvP goal and creates world event buzz.

---

### 8.6 Spirit Speak & Undead Lore System

**Design:** Bots with `SpiritSpeak` skill (a new sub-profile of Adventurer) visit graveyards and Deceit dungeon. When near a fresh corpse (within 1 hour of death), they attempt Spirit Speak and "channel" a message from the dead: a random speech line from the dead bot's persona, presented as ghostly communication. This creates a haunting, lore-appropriate behavior for the undead zones.

---

## 9. GM/Admin Tooling

Improvements to the tools for managing the PlayerBot system.

---

### 9.1 PlayerBotDirector Gump Enhancements

**Current state:** The gump shows basic stats and allows bot spawning. 

**Add:**
- Event log viewer (last 20 WorldEventLog entries)
- Economy snapshot (gold in circulation estimate, reagent stock levels)
- Bot leaderboard (top 5 PKs by kills, top 5 adventurers by dungeon time)
- Encounter history (last 5 encounter spawns with outcome)
- Named roster status (which named bots are alive/dead/vacant)
- One-click "Run Encounter Now" for testing
- One-click "Start Invasion" with town selector

---

### 9.2 Bot Inspector Command

**Design:** `[BotInspect` — target a PlayerBot to open a read-only gump showing:
- Full stat block (Str/Dex/Int/Hits/Stam/Mana)
- All skills with current values
- Current activity + time in state
- Group membership
- Last 10 speech lines spoken
- Kill count (new field: `m_KillCount` on PlayerBot, incremented on `OnGaveMelee`)
- Loot inventory snapshot
- Experience tier + progress toward next tier

---

### 9.3 World Event Trigger Commands

**Design:** `[TriggerInvasion <town>`, `[TriggerEncounter <type>`, `[TriggerConvoy <origin> <destination>` — admin commands to manually trigger events for testing and special occasions.

---

### 9.4 Population Heatmap

**Design:** `[BotHeatmap` — generates a text representation (or a map overlay if client supports) showing bot population density across the map. Helps identify underpopulated areas.

---

## 10. Priority Matrix

Ordered by impact on solo play experience vs. implementation complexity.

### Tier 1 — Do Now (Critical / High Impact / Tractable)

| # | Item | Section | Effort |
|---|---|---|---|
| 1 | PK vs. PK combat freeze | 1.1 | Small |
| 2 | Magic item loot drops | 1.5 / 7.2 | Medium |
| 3 | Bot corpse looting | 1.7 | Small |
| 4 | Bot potion use | 1.8 | Small |
| 5 | Group serialization | 1.4 | Medium |
| 6 | Bot respawn after death | 2.3 | Medium |
| 7 | Encounter system (3 archetypes) | 6.1 | Medium |
| 8 | Guards vs. reds in guard zones | 7.1 | Small |
| 9 | Day/night behavior | 5.6 | Small |
| 10 | Bot bandage use | 2.10 | Small |

### Tier 2 — High Value (Significant Gameplay Impact)

| # | Item | Section | Effort |
|---|---|---|---|
| 11 | Bot progression / experience upgrade | 2.1 | Medium |
| 12 | Named bot roster | 2.2 | Medium |
| 13 | Town Crier system | 5.2 | Medium |
| 14 | Mounted bots | 2.8 | Medium |
| 15 | Thief bot archetype | 3.1 | Large |
| 16 | Bard bot archetype | 3.2 | Medium |
| 17 | Merchant convoy system | 5.1 | Large |
| 18 | Bot guild formation | 5.4 | Medium |
| 19 | PK bounty system | 5.7 | Medium |
| 20 | World memory + conversation expansion | 2.6 / 5.3 | Large |

### Tier 3 — Polish & Depth (Nice to Have)

| # | Item | Section | Effort |
|---|---|---|---|
| 21 | Stealth/hiding bots | 2.9 | Medium |
| 22 | Crafter production pipeline | 4.2 | Large |
| 23 | Bot vendor purchasing | 4.1 | Medium |
| 24 | Tavern/social hub system | 5.5 | Medium |
| 25 | Bot banking behavior | 2.5 | Small |
| 26 | Monster invasion events | 6.2 | Large |
| 27 | Seasonal events (Halloween, etc.) | 6.4 | Small |
| 28 | Escort quest system | 8.2 | Large |
| 29 | Fisher bot | 3.3 | Small |
| 30 | Miner/Lumberjack bot | 3.4 | Medium |
| 31 | Bot-to-bot trading | 2.4 | Small |
| 32 | PvP tournament system | 8.5 | Large |
| 33 | Dungeon ecosystem events | 6.3 | Large |
| 34 | GM tooling enhancements | 9.x | Medium |
| 35 | Shard history book | 8.4 | Small |

### Tier 4 — Long-term Vision

| # | Item | Section | Effort |
|---|---|---|---|
| 36 | Spirit Speak bots at graveyards | 8.6 | Small |
| 37 | Player housing + bot integration | 8.3 | Medium |
| 38 | Full economy simulator | 4.3 / 4.4 | Large |
| 39 | Murder investigation system | 6.5 | Large |
| 40 | Bot progression ranking system | 2.1 extended | Large |

---

## Appendix A — CombatStyle Enum Resolution

**Recommendation:** Wire up `CombatStyle` as the single source of truth for combat decisions. Mapping:

| CombatStyle | PrefersMelee | UsesMagic | Preferred Skill |
|---|---|---|---|
| Melee | true | false | Swords/Macing/Fencing (random) |
| Archery | true (false for range) | false | Archery |
| Magic | false | true | Magery |
| MeleeAndMagic | true | true | Swords or Macing |

Replace the three separate fields with `CombatStyle` in both PlayerBot and PlayerBotPersona. Update serialization to version 3.

---

## Appendix B — Missing 1997 UO Features (Intentionally Excluded)

The following are 1997 UO features that should NOT be implemented, as they are either post-T2A or incompatible with the 1.0.1 client target:

- Trammel facet (added in Renaissance 2000)
- Bulk Order Deeds (added in T2A)
- Paladin/Necromancer classes (Second Age)
- Skill locks (deliberate omission per CLAUDE.md)
- Item insurance (AOS)
- AOS item properties (resists, attributes)
- Context menus (post-T2A client feature)
- House lockdowns/secures system (pre-T2A behavior is items-just-persist)
- Virtue system points UI (pre-T2A karma is the virtue system)
- Champion spawns (introduced in T2A)
- Powerscrolls

---

## Appendix C — Reference Sources for 1997 UO Accuracy

When implementing any mechanic, verify against:

1. **Stratics History Archive** — skill formulas, poison tables, creature stats from 1997–1998
2. **UO Forever / Era UO historical documentation** — pre-T2A server implementations
3. **Original UO patch notes** (Lord Blackthorn's era, August–December 1997)
4. **UOGamers: Rebirth forum archives** (2004–2006) — the specific shard being recreated
5. **The in-world save data** (`Saves/`) — creature and item values from September 20, 2005 are the canonical reference for this shard

---

*End of MASTER_PLAN.md*
