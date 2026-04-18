namespace StardewModCompatibilityFixer;

/// <summary>A discovered portrait asset from an installed mod.</summary>
internal sealed record PortraitSource(
    /// <summary>SMAPI asset name, e.g. <c>Portraits/Abigail</c>.</summary>
    string AssetName,
    string ModUniqueId,
    string FilePath,
    bool IsAnimated,
    /// <summary>Family name of the adult mod that owns this portrait, or <c>null</c> for non-adult mods.</summary>
    string? AdultModFamily = null);
