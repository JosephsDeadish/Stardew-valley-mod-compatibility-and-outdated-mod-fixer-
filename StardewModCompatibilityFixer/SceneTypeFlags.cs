namespace StardewModCompatibilityFixer;

/// <summary>
/// Bitmask of scene-interaction types a given adult mod supports.
/// Used to determine which scene options are available / greyed-out before starting a scene.
/// </summary>
[Flags]
internal enum SceneTypeFlags
{
    None         = 0,
    /// <summary>Female protagonist with a male NPC.</summary>
    FemaleMale   = 1 << 0,
    /// <summary>Female protagonist with a female NPC.</summary>
    FemaleFemale = 1 << 1,
    /// <summary>Male protagonist with a male NPC.</summary>
    MaleMale     = 1 << 2,
}

/// <summary>Extension helpers for <see cref="SceneTypeFlags"/>.</summary>
internal static class SceneTypeFlagsExtensions
{
    /// <summary>Returns a short display code like "FM", "FF", "MM".</summary>
    public static string ToCode(this SceneTypeFlags f) => f switch
    {
        SceneTypeFlags.FemaleMale   => "FM",
        SceneTypeFlags.FemaleFemale => "FF",
        SceneTypeFlags.MaleMale     => "MM",
        _                           => f.ToString()
    };

    /// <summary>Parses a short code back to a flag value.</summary>
    public static SceneTypeFlags FromCode(string code) => code.ToUpperInvariant() switch
    {
        "FM" => SceneTypeFlags.FemaleMale,
        "FF" => SceneTypeFlags.FemaleFemale,
        "MM" => SceneTypeFlags.MaleMale,
        _    => SceneTypeFlags.FemaleMale
    };
}
