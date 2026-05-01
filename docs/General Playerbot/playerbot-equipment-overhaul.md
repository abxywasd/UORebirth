# PlayerBot Equipment & Loot Overhaul — Implementation Plan

## Goals

1. Armor and weapon selection reflects real '97 UO player diversity: mages in full plate, halberds on anyone, no class-locked loadouts.
2. Every bot spawns with a backpack containing contextually plausible loot (gold, reagents, bandages, hunting spoils, etc.).
3. A fashion layer (robes, capes, sashes, kilts) sits on top of armor and is dyed with vivid player-typical hues.
4. Base clothing layer is also more varied in color and silhouette.

---

## 1. Armor Overhaul (`InitArmor`)

### Problem with current code
- Newbies never wear armor (too clean for '97 UO where everyone scavenged something).
- Mages are hard-gated to leather regardless of experience.
- Grandmaster PKs cap at chain/ringmail — no plate.
- Sets are always complete and symmetric; real players mixed mismatched pieces constantly.

### New approach: material tier + slot lottery

**Step 1 — Roll a material tier**, weighted by experience:

| Experience  | Naked | Leather | Chain/Ring | Plate |
|-------------|-------|---------|------------|-------|
| Newbie      | 50%   | 40%     | 10%        | 0%    |
| Average     | 10%   | 50%     | 30%        | 10%   |
| Proficient  | 0%    | 30%     | 40%        | 30%   |
| Grandmaster | 0%    | 15%     | 30%        | 55%   |

Mages are **not** penalized — in pre-T2A UO there was no armor/casting penalty. A GM mage in full plate is historically correct.

**Step 2 — Pick slots**, 50% chance randomly, not as full sets **OR** 50% chance full set. If randomly, for each slot (chest, legs, arms, gloves, gorget, helm/cap), roll independently. Higher tiers have higher fill probability per slot:

| Tier    | Chest | Legs | Arms | Gloves | Gorget | Head |
|---------|-------|------|------|--------|--------|------|
| Leather | 80%   | 70%  | 60%  | 50%    | 40%    | 35%  |
| Studded | 80%   | 70%  | 60%  | 50%    | 40%    | 35%  |
| Ringmail| 80%   | 70%  | 60%  | 50%    | 40%    | 0%   |
| Chain   | 85%   | 75%  | 65%  | 55%    | 0%     | 45%  |
| Bone    | 85%   | 75%  | 65%  | 55%    | 0%     | 45%  |
| Plate   | 90%   | 80%  | 70%  | 60%    | 55%    | 40%  |

Chain/ringmail/Bone has no gorget slot. Use `PlateGorget` or `LeatherGorget` or `StuddedGorget` (even chances for the three so 33/33/33 approx), if Strength stat permits.
For Leather / Studded / Chain / Ringmail, Head/Helmet can sometimes be `PlateHelm` or `CloseHelm` or `ChainCoif` or `NorseHelm` or `OrcHelm` if Strength stat permits.
For plate, use a mix of `PlateChest`, `PlateLegs`, `PlateArms`, `PlateGloves`, `PlateGorget`, and for head: `PlateHelm` or `CloseHelm` or `ChainCoif` or `NorseHelm` or `OrcHelm`.

**Step 3 — Cross-tier mixing** if full set has been selected (15% chance per slot if tier > leather): roll the slot one tier down. This produces "chain chest + leather legs" naturally.

### Affected code
`InitArmor()` in `PlayerBot.cs` — rewrite entirely. Crafter exception stays (no armor).

---

## 2. Weapon Pool Expansion (`GenerateWeapon` + `InitWeapon`)

### Problem
- No halberds, bardiches, or executioner's axes in the pool.
- Mages who `PrefersMelee` already equip weapons — but the Swords pool misses the big two-handers that were everywhere in '97.
- Mage-only path skips the weapon entirely, leaving them with just a spellbook — fine, but pure mages could still carry a backup dagger visibly equipped.

### Changes

**Add to Swords pool:**
- `Halberd` (Str >= 45)
- `Bardiche` (Str >= 40)
- `ExecutionersAxe` (Str >= 40)
- `TwoHandedAxe` (Str >= 35)

These are Swords skill weapons in RunUO (verify class names: `Halberd`, `Bardiche`, `ExecutionersAxe`, `WarAxe`, `TwoHandedAxe`).

**Add to Fencing pool:**
- `Kryss` (Str >= 10) — extremely common in '97 PvP
- `WarFork` already present — good

**Add to Macing pool:**
- Already reasonably populated; add `QuarterStaff` (Str >= 15) if available.
- `WarAxe` (Str >= 35) (classically uses Macefighting skill).

**Mage-only weapon slot:** Instead of no weapon, 40% chance to equip a `Dagger` or `Kryss` in the weapon layer (even pure mages carried sidearms).

### Affected code
`GenerateWeapon()` and the mage-only branch in `InitWeapon()`.

---

## 3. Fashion Layer — New Method `InitFashionLayer`

Call this after `InitArmor()` in `InitOutfit()`.

### Philosophy
Fashion items layer over armor using separate equipment slots (Layer.OuterTorso, Layer.Cloak, Layer.Waist, etc.). They should be dyed with a curated palette of vivid hues that real players used — not just `RandomNondyedHue()`.

### Player hue palette

Define a static `int[] PlayerHues` array containing ~30–40 hues typical of '97 UO dyeing:
- Reds: 0x21, 0x26, 0x2B, 0x2F
- Blues: 0x05, 0x0B, 0x10, 0x15
- Greens: 0x40, 0x44, 0x48, 0x52
- Purples: 0x72, 0x76, 0x7A
- Yellows/oranges: 0x35, 0x38, 0x8A, 0x8C
- Browns/tans: 0x96, 0x97, 0x99
- Blacks/dark: 0x01, 0x66
- Whites/light: 0x03F5, 0x047E

Helper: `private static int RandomPlayerHue() => PlayerHues[m_Rnd.Next(PlayerHues.Length)]`

### Fashion item pool

Each is an independent roll. Items must not conflict with existing equipped slots (check `FindItemOnLayer` before adding).

| Item | Layer | Roll chance | Notes |
|------|-------|-------------|-------|
| `Robe` | OuterTorso | 25% | All genders; skipped if already has Robe from InitClothing |
| `Cloak` | Cloak | 35% | All genders |
| `BodySash` | MiddleTorso | 30% | All genders |
| `Kilt` | InnerLeg | 20% males | Skip if already has Kilt/LongPants conflict |
| `Skirt` | InnerLeg | 15% females | |
| `HalfApron` | Waist | 20% | All genders; craftsy look |
| `FullApron` | Waist | 10% | More craftsman; any profile |

All fashion items are dyed with `RandomPlayerHue()`.

### `InitClothing` adjustments

- Remove the hard link between `m_UsesMagic/Crafter` and adding a Robe — instead `InitFashionLayer` handles robes for everyone randomly.
- Base layer shirt/pants/shoes/dress: expand color range to use `RandomPlayerHue()` instead of `RandomNondyedHue()` for more saturation.
- Shoes: 33% boots (`Boots`), 33% shoes (`Shoes`), 33% thigh boots (`ThighBoots`) — real players wore all.
- Males: 30% chance of `ShortPants` instead of `LongPants` (more variety).

---

## 4. Backpack Loot — New Method `InitBackpack`

Call at the end of the constructor, after `InitOutfit` and `InitReagents`.

All PlayerBots **always** spawn with a `Backpack` (they already get one via the `BaseCreature` base class — `PackItem` adds to it). This method populates it.

### Gold

Scaled to experience with randomness:

| Experience  | Gold range |
|-------------|------------|
| Newbie      | 5 – 60     |
| Average     | 50 – 250   |
| Proficient  | 150 – 600  |
| Grandmaster | 300 – 1500 |

Add as `new Gold(amount)`.

### Bandages (non-Crafter only)

Real '97 players always carried bandages. Amount by experience:
- Newbie: 5–20
- Average: 15–50
- Proficient: 30–80
- Grandmaster: 50–150

### Food (everyone)

2–5 random items drawn from a small pool: `Bread`, `CheeseWheel`, `Grapes`, `Watermelon` (or just `Food` base items). These are small and universal.

### Hunting spoils (Adventurer + PlayerKiller only)

Simulate that they've been out hunting. Roll 1–3 drops from:
- `RawCutLeather` or `Leather` (qty 5–30) — from animals
- `IronIngot` (qty 2–15) — looted from humanoids
- Random gem (`Citrine`, `Tourmaline`, `Amethyst`, `Sapphire`, `Ruby`, `Emerald`, `Diamond`) qty 1–3
- `Bone` (qty 1–5) — undead hunting
- `Arrow` (qty 10–40) if the bot carries a bow (already handled in InitWeapon)

Roll each item with ~40% probability; pick 0–3 of these categories.

### Crafter supplies

For Crafter-profile bots, instead of hunting spoils:
- `IronIngot` or `ShadowIronIngot` (qty 10–50)
- `Board` (qty 5–25)
- A random tool: `Tongs`, `SmithHammer`, `Saw`, or `Scissors` (50% chance)

### Reagents for non-mages

Even non-mage players sometimes carried a few reagents. 20% chance to add 1–3 stacks of random reagents (qty 5–15 each), drawn from the full 8-reagent pool.

### Misc

- 30% chance: `Torch` (qty 1–3)
- 15% chance: `Candle`

---

## 5. Call Order in `InitOutfit` and Constructor

Update `InitOutfit()`:
```
InitHair()
InitClothing()      ← tweaked (hues, shoe variety, no forced robe)
InitArmor()         ← rewritten
InitFashionLayer()  ← new
InitWeapon()        ← weapon pool expanded
```

Update constructor body:
```
InitPersona()
InitBody()
InitStats()
InitSkills()
InitOutfit()
if (m_UsesMagic) InitReagents()   ← already exists
InitBackpack()                     ← new, called last
StartSkillTimer()
```

---

## 6. Serialization Impact

**None.** All equipment and backpack items are added as `Item` objects that serialize automatically through the normal RunUO world save. No new fields need to be added to `Serialize`/`Deserialize`.

---

## 7. Item Class Name Verification Checklist

Before coding, confirm these class names exist under `Scripts/Items/`:
- `Halberd`, `Bardiche`, `ExecutionersAxe`, `WarAxe`, `TwoHandedAxe` — Swords skill 2H weapons
- `Kryss` — Fencing
- `QuarterStaff` — Macing or separate
- `BodySash`, `Cloak`, `HalfApron`, `FullApron`, `Skirt`, `ShortPants` — clothing items
- `RawCutLeather`, `Leather` — resource items
- `IronIngot`, `ShadowIronIngot` — smith resources
- `Board` — carpentry resource
- `Tongs`, `SmithHammer`, `Saw`, `Scissors` — tools
- `Bone` — misc item
- `Citrine`, `Tourmaline`, `Amethyst`, `Sapphire`, `Ruby`, `Emerald`, `Diamond` — gems
- `Bread`, `CheeseWheel`, `Grapes`, `Watermelon` — food
- `Torch`, `Candle` — light sources
- `CloseHelm` — plate helm variant
- `Boots` — footwear alternative to `Shoes`
- `ThighBoots` — footwear alternative to `Shoes`

Some may have different names or may not exist in this pre-T2A script set — substitute with the nearest available equivalent during implementation.

---

## 8. Files to Touch

| File | Change |
|------|--------|
| `Scripts/Mobiles/PlayerBot/PlayerBot.cs` | Rewrite `InitArmor`, `InitClothing`, `InitWeapon`/`GenerateWeapon`; add `InitFashionLayer`, `InitBackpack`, `RandomPlayerHue` helper and `PlayerHues` array |

No other files need changes.
