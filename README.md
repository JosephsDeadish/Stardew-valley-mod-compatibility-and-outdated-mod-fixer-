# Stardew-valley-mod-compatibility-and-outdated-mod-fixer-

SMAPI mod project focused on plug-and-play compatibility handling:

- Detects installed portrait mods and indexes available NPC portraits.
- If Portraiture is installed, automatically syncs detected portrait files into Portraiture folders (no manual moving needed).
- Detects multiple installed body/adult/body-override style mods and enables runtime mediation/logging for conflicts.
- Provides optional Generic Mod Config Menu (GMCM) integration:
  - Toggle portrait randomization.
  - Prefer animated portrait sources.
  - Pick per-NPC preferred portrait source mod.
- If GMCM is not installed, auto-selection still works using discovered portrait sources.

## Build

This is a SMAPI mod project under:

`/home/runner/work/Stardew-valley-mod-compatibility-and-outdated-mod-fixer-/Stardew-valley-mod-compatibility-and-outdated-mod-fixer-/StardewModCompatibilityFixer`

Build with `dotnet build` in that folder (requires a local SMAPI/Stardew game install configured for ModBuildConfig).
