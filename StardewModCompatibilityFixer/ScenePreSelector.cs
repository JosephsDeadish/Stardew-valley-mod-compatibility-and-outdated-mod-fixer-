namespace StardewModCompatibilityFixer;

/// <summary>
/// Manages per-NPC scene-type preferences and checks which scene types are
/// actually compatible given the currently installed adult mods and active sprite selections.
///
/// Used to grey-out incompatible options in GMCM and to route sprite assets
/// when an adult scene begins.
/// </summary>
internal sealed class ScenePreSelector
{
    private readonly ModConfig config;
    private readonly List<ResolvedAdultMod> activeAdultMods;
    private readonly GenderContext genderContext;

    public ScenePreSelector(
        ModConfig config,
        List<ResolvedAdultMod> activeAdultMods,
        GenderContext genderContext)
    {
        this.config          = config;
        this.activeAdultMods = activeAdultMods;
        this.genderContext   = genderContext;
    }

    // -------------------------------------------------------------------------
    // Available / supported scene types
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all <see cref="SceneTypeFlags"/> values that are supported by at least one
    /// of the currently installed adult mods.
    /// </summary>
    public IReadOnlyList<SceneTypeFlags> GetGloballyAvailableSceneTypes()
    {
        var result    = new HashSet<SceneTypeFlags>();
        SceneTypeFlags combined = SceneTypeFlags.None;

        foreach (ResolvedAdultMod resolved in this.activeAdultMods)
            combined |= resolved.Definition.SupportedSceneTypes;

        foreach (SceneTypeFlags flag in Enum.GetValues<SceneTypeFlags>())
        {
            if (flag != SceneTypeFlags.None && combined.HasFlag(flag))
                result.Add(flag);
        }

        // Always include FM as a safe fallback if nothing specific is found
        if (result.Count == 0)
            result.Add(SceneTypeFlags.FemaleMale);

        return result.OrderBy(f => f).ToList();
    }

    /// <summary>
    /// Returns all scene types available for a specific active adult mod family.
    /// </summary>
    public IReadOnlyList<SceneTypeFlags> GetAvailableSceneTypesForFamily(string familyName)
    {
        var result = new List<SceneTypeFlags>();
        AdultModDefinition? def = AdultModRegistry.KnownDefinitions
            .FirstOrDefault(d => d.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase));

        if (def is null)
        {
            result.Add(SceneTypeFlags.FemaleMale);
            return result;
        }

        foreach (SceneTypeFlags flag in Enum.GetValues<SceneTypeFlags>())
        {
            if (flag != SceneTypeFlags.None && def.SupportedSceneTypes.HasFlag(flag))
                result.Add(flag);
        }

        if (result.Count == 0)
            result.Add(SceneTypeFlags.FemaleMale);

        return result;
    }

    // -------------------------------------------------------------------------
    // Compatibility check
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true when the given scene type is compatible with the currently effective
    /// player and NPC genders/sprites.  Incompatible types should be greyed-out in GMCM.
    /// </summary>
    public bool IsSceneTypeCompatible(SceneTypeFlags sceneType, string npcName)
    {
        SceneTypeFlags derived = this.genderContext.DeriveSceneType(npcName);

        // Direct match — always compatible
        if (derived == sceneType)
            return true;

        // If the player has no gender override, any type is optionally selectable
        if (!this.genderContext.IsPlayerGenderOverridden)
            return true;

        // With a forced gender, only the derived type is strictly compatible
        return false;
    }

    // -------------------------------------------------------------------------
    // User preference
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the preferred scene type code ("FM"/"FF"/"MM") for the given NPC,
    /// defaulting to the type derived from current genders.
    /// </summary>
    public string GetPreferredSceneTypeCode(string npcName)
    {
        if (this.config.SceneTypePreference.TryGetValue(npcName, out string? pref)
            && !string.IsNullOrEmpty(pref))
            return pref;

        return this.genderContext.DeriveSceneType(npcName).ToCode();
    }

    /// <summary>
    /// Returns the user's preferred <see cref="SceneTypeFlags"/> for the given NPC.
    /// </summary>
    public SceneTypeFlags GetPreferredSceneType(string npcName)
        => SceneTypeFlagsExtensions.FromCode(this.GetPreferredSceneTypeCode(npcName));

    /// <summary>
    /// Builds the ordered list of option codes for use in an GMCM text-option dropdown,
    /// marking incompatible options with a ⚠ prefix.
    /// </summary>
    public string[] BuildSceneTypeOptions(string npcName)
    {
        var available = this.GetGloballyAvailableSceneTypes();
        return available.Select(st =>
        {
            bool compat = this.IsSceneTypeCompatible(st, npcName);
            return compat ? st.ToCode() : $"⚠ {st.ToCode()}";
        }).ToArray();
    }
}
