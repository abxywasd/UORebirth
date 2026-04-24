# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

UORebirth is a RunUO 2.1-based Ultima Online private server implementing the Pre-T2A (late 1997/early 1998) UO ruleset. It is a historical recreation of **UOGamers: Rebirth**, a shard that ran from 2004–2006. The world save included is from September 20, 2005. The custom work on top of the base archive lives in `Scripts/` and `RunUO-2.1/`.

## Build & Run

### Compile the RunUO core executable
Run from `RunUO-2.1/`:
```
"RunUO-2.1\Compile Server.bat"
```
This invokes `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` with `/optimize /unsafe /t:exe /d:NEWTIMERS /d:NEWPARENT` over all `Server/*.cs` files and outputs `RunUO.exe` to the project root.

### Compile the scripts assembly (for IDE / type-checking)
Open `Scripts/RebirthScripts.sln` in Visual Studio (targets .NET 4.0). The project builds `Scripts.dll` into `Scripts/Output/`. The server itself compiles and loads scripts at runtime from source — the `.sln` is primarily for IDE support.

### Start the server
Run `RunUO.exe` from the project root. The server compiles scripts from `Scripts/` on startup, loads world data from `Data/`, and deserializes the live world from `Saves/`.

### No automated tests
There is no unit test framework. Validation is done manually at runtime. `RunUO-2.1/Scripts/Misc/Test Center/TestCenter.cs` exists as a GM utility, not a test harness.

## Architecture

### Two-layer structure
- **`RunUO-2.1/Server/`** — The core C# framework (networking, serialization, mobile/item base classes, region system, event sink, scripting engine). Modified with patches from `Rebirth-Core-svn728.diff`.
- **`Scripts/`** — All game content: 1,400+ C# files compiled at startup and hot-reloaded. Everything in `Scripts/` runs in the `Server.*` namespace.

### Core patches (Rebirth-Core-svn728.diff)
Three key changes were made to the RunUO 2.1 core that all scripts depend on:
1. **ASCII packet conversion** — All outgoing message packets are converted from Unicode to ASCII before sending, because the target client era (1.0.1) predates Unicode support in UO.
2. **`Item.SendInfoTo()` made virtual** — Required for 1.0.1 single-click item name display (no context menus).
3. **`Item.Dupe()` method** — Added for item duplication support.

The precompiled `RunUO.exe` in the project root already includes these patches.

### Script loading
On startup, RunUO compiles all `.cs` files under `Scripts/` into a single in-memory assembly. `Data/Assemblies.cfg` lists the external .NET assemblies available to scripts (`System.dll`, `System.Xml.dll`, `System.Windows.Forms.dll`, etc.).

### World data flow
```
Data/Regions.xml          → World region definitions (guard zones, towns, dungeons)
Data/SpawnDefinitions.xml → Spawn templates referenced by map files
Data/WorldSpawn.xml       → Persistent world spawn entries (1MB, loaded at runtime)
Data/*.map                → Dynamic spawn maps loaded via [spawn add <mapfile>
Saves/Accounts/           → Account XML (manually editable for character linking)
Saves/Items/              → Binary serialized item world state
Saves/Mobiles/            → Binary serialized mobile world state
Saves/Guilds/             → Binary serialized guild state
```

### Custom systems added in this repo

**PlayerBot** (`Scripts/Mobiles/PlayerBot/`)
NPC bots that simulate player characters. Three files:
- `PlayerBot.cs` — Extends `BaseCreature` with `AIType.AI_PlayerBot`. Has `PlayerBotPersona`, melee/magic preferences, outfit/stats init, and `[CommandProperty]` GM accessors.
- `PlayerBotAI.cs` — Combat AI logic wired into the RunUO AI subsystem.
- `PlayerBotPersona.cs` — Profile enum (`PlayerKiller`, `Merchant`, etc.) and experience levels that drive bot behavior.

**T2A Content** (`Scripts/Mobiles/T2A/`)
The Second Age creatures (Ophidians, Titans, Cyclops, Frost Trolls, Hell Cats, Ostards, Ice Serpents) and their loot/stats, referenced by `Data/Lostlands.map`, `Data/Papua.map`, `Data/Delucia.map`, and `Data/Terathan_keep.map`.

**Spawn system** (`Scripts/Commands/Spawn.cs`, `Scripts/Regions/Spawning/`)
- `[spawn add <mapfile>` / `[spawn remove <mapfile>` — Admin commands to dynamically load/unload a `.map` spawn file.
- `SpawnDefinition.cs` — XML-deserialized template defining what creatures/items spawn and at what frequency.
- `SpawnEntry.cs` — A live, running spawner instance.

**Notoriety system** (`Scripts/Misc/Notoriety.cs`)
Pre-T2A notoriety (not Karma/Fame). Controls grey/red status. Tightly coupled to `Scripts/Mobiles/PlayerMobile.cs`.

### Key files for understanding game rules
- `Scripts/Mobiles/PlayerMobile.cs` (74KB) — All player character logic, skill gain formulas, stat systems, notoriety transitions, horse lag, talk-while-hidden, and the Resurrect Now option.
- `Scripts/Misc/Titles.cs` — Title assignment logic (karma + profession based).
- `Scripts/Misc/Notoriety.cs` — Grey/red notoriety rules.
- `Scripts/Items/Weapons/` — Original weapon modifiers including spellcasting weapons.
- `Scripts/Special/Christmas.cs` — 1997 Christmas present logic.

### Region and map system
Regions are defined in `Data/Regions.xml` and instantiated via `Scripts/Regions/`. The `GuardedRegion` subclass handles town guards. Spawn maps (`.map` files in `Data/`) are line-oriented text files listing creature types, counts, and coordinates tied to `SpawnDefinition` templates.

## Important constraints

- **Target client: UO 1.0.1** — No Unicode, no context menus, no AOS/SE/ML content. Any new messages sent from scripts go through the ASCII conversion in the core automatically.
- **No skill locks** — The skill atrophy system is intentional; do not add skill lock UI or mechanics.
- **No lockdowns/secures in houses** — House item storage works differently from post-T2A UO; items placed in houses simply persist without a lockdown system.
- **Windows/.NET 4.0 only** — The build system and RunUO core assume `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`.
- **World save is historical** — `Saves/` contains the original September 2005 world. Be careful with anything that triggers world serialization, as it overwrites these files.

## Linking a character to an account (from README.txt)
1. In-game: `[global interface where playermobile name = "CharName"`
2. Note the serial from `[get serial` (convert hex→decimal if needed).
3. Shut down server, edit `Saves/Accounts/accounts.xml`, add `<char index="0">SERIAL</char>` inside the account's `<chars>` block.
