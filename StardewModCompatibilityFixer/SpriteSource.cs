namespace StardewModCompatibilityFixer;

/// <summary>A discovered character sprite sheet from an installed mod.</summary>
internal sealed record SpriteSource(
    /// <summary>SMAPI asset name, e.g. <c>Characters/Abigail</c> or <c>Characters/Farmer_base</c>.</summary>
    string AssetName,
    string ModUniqueId,
    string FilePath,
    /// <summary>Family name of the adult mod that owns this sprite, or <c>null</c> for non-adult mods.</summary>
    string? AdultModFamily = null,
    /// <summary>
    /// Bitmask of scene types this sprite source is known to support.
    /// Defaults to FemaleMale when unknown.
    /// </summary>
    SceneTypeFlags SupportedSceneTypes = SceneTypeFlags.FemaleMale);
