namespace StardewModCompatibilityFixer;

/// <summary>
/// Describes a known adult mod family and its asset-compatibility characteristics.
/// Instances are defined statically in <see cref="AdultModRegistry.KnownDefinitions"/>.
/// </summary>
internal sealed class AdultModDefinition
{
    /// <summary>Human-readable family name used for logging, config keys, and GMCM display.</summary>
    public string FamilyName { get; init; } = string.Empty;

    /// <summary>
    /// Substring patterns matched case-insensitively against a mod's UniqueID or Name
    /// to identify it as belonging to this family.
    /// </summary>
    public string[] IdPatterns { get; init; } = [];

    /// <summary>
    /// Asset-name substrings this mod "owns".  While an active scene from this mod is running,
    /// its version of any matching asset takes priority over other installed mods.
    /// </summary>
    public string[] OwnedAssetPatterns { get; init; } = [];

    /// <summary>
    /// Family names of other adult mods whose sprite/portrait sheets are body-compatible
    /// with this one (can be used interchangeably outside a dedicated scene).
    /// </summary>
    public string[] CompatibleFamilyNames { get; init; } = [];

    /// <summary>Resolution priority used when no scene context is active.  Higher value wins.</summary>
    public int Priority { get; init; } = 0;

    /// <summary>
    /// Bitmask of scene interaction types this mod's content supports.
    /// Used to determine which scene-type options are available or greyed-out in GMCM.
    /// Defaults to FemaleMale (the most common case).
    /// </summary>
    public SceneTypeFlags SupportedSceneTypes { get; init; } = SceneTypeFlags.FemaleMale;
}
