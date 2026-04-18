# Stardew Mod Compatibility Fixer

Plug-and-play SMAPI mod — no requirements, no configuration required.
Drop it in your `Mods` folder (or install via Stardrop) and it handles the rest.

---

## Features

### Portrait compatibility
- Scans every installed mod for portrait image files and groups them per NPC.
- When **Portraiture** is installed, automatically mirrors detected portrait files into its folder — no manual moving required.
- When **GMCMOptions** (`spacechase0.GMCMOptions`) is also installed, the GMCM settings page shows a scrollable **portrait image preview** for each NPC so you can compare portraits side-by-side before choosing.
- When neither is installed, portrait selection still works through the standard GMCM text dropdown.

### Adult mod scene-context routing
Detects known adult mods (Lewdew Valley, XTardew Valley, Valley Girls, Harem Valley, Swim Mod, Naked Farmer, Swimsuits, Dark Club, and more) and:
- Logs body-compatibility status for every pair of installed adult mods.
- When a scene belonging to a specific adult mod is running, **forces that mod's portraits and sprites** for the duration — preventing asset bleed between incompatible mods.
- Compatible body pairs (e.g. XTardew + Valley Girls) can share sprites freely outside of dedicated scenes.
- Scans each adult mod's Content Patcher packs to build an event-ID map, so the correct mod family is identified automatically as soon as an event starts.

### Unknown / unregistered adult mod detection
If you install an adult mod that isn't in the static registry, the mod heuristically detects it from its name/ID/description keywords (nsfw, adult, lewd, hentai, etc.) and adds it as a generic adult mod. It logs a notice and treats it conservatively (FemaleMale scenes, priority 3) so it participates in compatibility routing rather than being silently ignored.

### Sprite (character sheet) routing
- Indexes `Characters/` sprite sheets across all mods exactly as it does portraits.
- For player sprites (`Characters/Farmer_*`), respects the "Force player body mod" config option.
- Filters incompatible body mods from the candidate list before selecting a sprite source.
- Respects per-NPC sprite source overrides set in GMCM.

### Gender override system
- **Player gender override** — set to Male, Female, or Auto (actual character gender). Affects which scene-type is derived and which body sprite is routed.
- **Per-NPC gender override** — override any NPC's effective gender. Useful for role-playing as or with NPCs using custom-gendered sprites.

### Scene type preferences (FM / FF / MM)
For each NPC, you can set a preferred scene interaction type:
- **FM** — Female protagonist × Male NPC
- **FF** — Female protagonist × Female NPC
- **MM** — Male protagonist × Male NPC

Options are automatically greyed out (prefixed with ⚠) in GMCM when they conflict with your current gender override settings. During a scene, the sprite routing layer picks assets from the installed adult mod that support the chosen scene type.

### Legacy / outdated mod detection & patching
Runs at game launch and produces a detailed log summary:
- Flags mods with a `MinimumApiVersion` older than SMAPI 3.0.0 that may use deprecated APIs.
- Scans every `content.json` CP pack for old CP `Format` versions (< 2.0.0) and old asset target paths renamed in 1.6.
- Registers **live shims** via SMAPI's asset pipeline so old paths continue to work:
  - `TileSheets/BuffsIcons` → `TileSheets/Buffs`
  - `Data/ObjectInformation` → converted from `Data/Objects`
  - `Data/BigCraftablesInformation` → converted from `Data/BigCraftables`
  - `Data/NPCDispositions` → converted from `Data/Characters`

### Optional GMCM (spacechase0.GenericModConfigMenu)
When GMCM is installed a full settings page appears:
- Portrait settings (Portraiture sync, randomisation, animated preference)
- Adult mod settings (force body mod, player gender override)
- **Per-NPC portrait source** with image previews (requires GMCMOptions) or text dropdown
- **Per-NPC sprite source** override
- **Per-NPC gender override**
- **Per-NPC scene type preference** (FM/FF/MM, incompatible options flagged ⚠)

---

## Installation (Stardrop)

1. Open Stardrop Mod Manager and click **Add from file** or **Add from URL**.
2. Point it at the mod zip — Stardrop places the folder under `Stardew Valley/Mods/`.
3. Launch the game through Stardrop. No further setup needed.

## Manual installation

1. Build the project with `dotnet build` (requires a local Stardew Valley + SMAPI install via `ModBuildConfig`).
2. Copy the output folder `StardewModCompatibilityFixer/` into `Stardew Valley/Mods/`.
3. Launch via the SMAPI launcher.

---

## Project structure

```
StardewModCompatibilityFixer/
  ModEntry.cs                 — SMAPI mod entrypoint, asset routing
  ModConfig.cs                — User-facing configuration model
  AdultModDefinition.cs       — Data type: describes an adult mod family
  AdultModRegistry.cs         — Static knowledge base + runtime resolver (known + heuristic)
  ContentPackEventScanner.cs  — Parses CP packs to map event IDs → adult mod families
  SceneContextTracker.cs      — Tick-level tracker for active adult scenes
  SceneTypeFlags.cs           — [Flags] enum: FemaleMale / FemaleFemale / MaleMale
  GenderContext.cs            — Player & NPC effective-gender resolution
  ScenePreSelector.cs         — Scene type availability, compatibility, and user preferences
  PortraitSource.cs           — Portrait asset record
  SpriteSource.cs             — Sprite sheet record
  LegacyCompatPatcher.cs      — Outdated mod detection + 1.5→1.6 live shims
  IGenericModConfigMenuApi.cs — GMCM integration interface
  IGMCMOptionsApi.cs          — Optional GMCMOptions image-carousel interface
  manifest.json               — SMAPI mod manifest (v1.2.0)
```
