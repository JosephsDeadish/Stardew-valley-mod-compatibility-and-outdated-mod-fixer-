using System.Collections.Generic;

namespace StardewModCompatibilityFixer;

internal sealed class ModConfig
{
    // ---- Portrait options ----

    /// <summary>When Portraiture is installed, automatically copy detected portrait files into its folder.</summary>
    public bool EnableAutoPortraitureSync { get; set; } = true;

    /// <summary>Pick a random available portrait source for each NPC every in-game day.</summary>
    public bool EnablePortraitRandomization { get; set; } = false;

    /// <summary>Seed for portrait randomisation.  0 = derive from the save's unique ID + day.</summary>
    public int RandomizationSeed { get; set; } = 0;

    /// <summary>Prefer animated portrait sources (e.g. from Animated Portraits mods) when available.</summary>
    public bool PreferAnimatedPortraits { get; set; } = true;

    /// <summary>Maps NPC name (e.g. "Abigail") to the preferred source mod's UniqueID.</summary>
    public Dictionary<string, string> PreferredPortraitModByNpc { get; set; } = new();

    // ---- Adult mod / body sprite options ----

    /// <summary>
    /// Force a specific adult-mod family's sprites for the player character (e.g. "NakedFarmer").
    /// Null = automatic selection based on priority.
    /// </summary>
    public string? ForceBodyModForPlayer { get; set; } = null;

    /// <summary>
    /// Override resolution priority per adult-mod family name.
    /// Key = family name (e.g. "LewdewValley"), Value = custom priority (higher wins).
    /// </summary>
    public Dictionary<string, int> AdultModPriorityOverrides { get; set; } = new();
}
