# Stardew Mod Compatibility Fixer

Plug-and-play SMAPI mod — no requirements, no configuration required.
Drop it in your `Mods` folder (or install via Stardrop) and it handles the rest.

---

## Features

### Portrait compatibility
- Scans every installed mod for portrait image files and groups them per NPC.
- When Portraiture is installed, automatically mirrors detected portrait files into its folder — no manual moving required.
- When Portraiture is **not** installed, intercepts SMAPI portrait asset requests and routes them from the correct source mod.

### Adult mod scene-context routing
Detects known adult mods (Lewdew Valley, XTardew Valley, Valley Girls, Harem Valley, Swim Mod, Naked Farmer, Swimsuits, Dark Club, and more) and:
- Logs body-compatibility status for every pair of installed adult mods.
- When a scene belonging to a specific adult mod is running, **forces that mod's portraits and sprites** for the duration — preventing asset bleed between incompatible mods.
- Compatible body pairs (e.g. XTardew + Valley Girls) can share sprites freely outside of dedicated scenes.
- Scans each adult mod's Content Patcher packs to build an event-ID map, so the correct mod family is identified automatically as soon as an event starts.

### Sprite (character sheet) routing
- Indexes `Characters/` sprite sheets across all mods exactly as it does portraits.
- For player sprites (`Characters/Farmer_*`), respects the "Force player body mod" config option.
- Filters incompatible body mods from the candidate list before selecting a sprite source.

### Legacy / outdated mod detection & patching
Runs at game launch and produces a detailed log summary:
- Flags mods with a `MinimumApiVersion` older than SMAPI 3.0.0 that may use deprecated APIs.
- Scans every `content.json` CP pack for:
  - Old CP `Format` versions (< 2.0.0) that may not behave correctly.
  - Asset target paths that were renamed or reformatted in Stardew Valley 1.6.
- Registers **live shims** via SMAPI's asset pipeline so old paths continue to work:
  - `TileSheets/BuffsIcons` → `TileSheets/Buffs` (transparent texture redirect)
  - `Data/ObjectInformation` → converted from `Data/Objects` (structured → legacy string format)
  - `Data/BigCraftablesInformation` → converted from `Data/BigCraftables`
  - `Data/NPCDispositions` → converted from `Data/Characters`

### Optional GMCM configuration (spacechase0.GenericModConfigMenu)
When Generic Mod Config Menu is installed a settings page appears with:
- Toggle: auto-sync portraits to Portraiture
- Toggle: randomise portraits (picks a different source each in-game day)
- Toggle: prefer animated portrait sources
- Dropdown: force a specific adult mod's body sprites for the player
- Per-NPC dropdown: pick which installed mod's portrait to use for each character

---

## Installation (Stardrop)

1. Open Stardrop Mod Manager and click **Add from file** or **Add from URL**.
2. Point it at the mod zip — Stardrop will place the folder under `Stardew Valley/Mods/`.
3. Launch the game through Stardrop. No further setup needed.

## Manual installation

1. Build the project with `dotnet build` (requires a local Stardew Valley + SMAPI install configured via `ModBuildConfig`).
2. Copy the output folder `StardewModCompatibilityFixer/` into `Stardew Valley/Mods/`.
3. Launch via the SMAPI launcher.

---

## Project structure

```
StardewModCompatibilityFixer/
  ModEntry.cs                 — SMAPI mod entrypoint, asset routing
  ModConfig.cs                — User-facing configuration model
  AdultModDefinition.cs       — Data type: describes an adult mod family
  AdultModRegistry.cs         — Static knowledge base + runtime resolver
  ContentPackEventScanner.cs  — Parses CP packs to map event IDs → adult mod families
  SceneContextTracker.cs      — Tick-level tracker for active adult scenes
  PortraitSource.cs           — Portrait asset record
  SpriteSource.cs             — Sprite sheet record
  LegacyCompatPatcher.cs      — Outdated mod detection + 1.5→1.6 live shims
  IGenericModConfigMenuApi.cs — GMCM integration interface
  manifest.json               — SMAPI mod manifest (v1.1.0)
```
