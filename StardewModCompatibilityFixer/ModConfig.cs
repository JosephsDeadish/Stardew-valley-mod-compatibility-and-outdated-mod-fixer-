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

    /// <summary>Maps NPC name (e.g. "Abigail") to the preferred source mod's UniqueID for portraits.</summary>
    public Dictionary<string, string> PreferredPortraitModByNpc { get; set; } = new();

    // ---- Sprite options ----

    /// <summary>Maps NPC name (e.g. "Abigail") to the preferred source mod's UniqueID for sprite sheets.</summary>
    public Dictionary<string, string> PreferredSpriteModByNpc { get; set; } = new();

    // ---- Adult mod / body sprite options ----

    /// <summary>
    /// Force a specific adult-mod family's sprites for the player character (e.g. "NakedFarmer").
    /// Null or "(auto)" = automatic selection based on priority.
    /// </summary>
    public string? ForceBodyModForPlayer { get; set; } = null;

    /// <summary>
    /// Override resolution priority per adult-mod family name.
    /// Key = family name (e.g. "LewdewValley"), Value = custom priority (higher wins).
    /// </summary>
    public Dictionary<string, int> AdultModPriorityOverrides { get; set; } = new();

    // ---- Gender overrides ----

    /// <summary>
    /// Override the player's effective gender for scene-type and sprite routing.
    /// "Auto" (default) uses the actual Farmer gender.
    /// "Male" or "Female" forces a specific gender regardless of character creation choice.
    /// </summary>
    public string PlayerGenderOverride { get; set; } = "Auto";

    /// <summary>
    /// Per-NPC gender override.
    /// Key = NPC name, Value = "Male" or "Female".
    /// When set, routes the NPC's sprites to gender-appropriate sources and affects
    /// which scene type (FM/FF/MM) is derived for that NPC.
    /// </summary>
    public Dictionary<string, string> NpcGenderOverrides { get; set; } = new();

    // ---- Scene type preferences ----

    /// <summary>
    /// Per-NPC preferred scene interaction type.
    /// Key = NPC name, Value = code: "FM" (female-male), "FF" (female-female), "MM" (male-male).
    /// Used to choose the correct adult mod animation and sprite set before a scene begins.
    /// Incompatible types (given current gender overrides / installed mods) are flagged with ⚠ in GMCM.
    /// </summary>
    public Dictionary<string, string> SceneTypePreference { get; set; } = new();
}
