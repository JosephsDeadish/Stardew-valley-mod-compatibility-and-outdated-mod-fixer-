using StardewValley;

namespace StardewModCompatibilityFixer;

/// <summary>
/// Resolves the effective gender for the player and for named NPCs, honouring
/// any per-entity overrides set in <see cref="ModConfig"/>.
///
/// The effective gender affects which scene-type animations are legal and which
/// body sprites are routed during a scene.
/// </summary>
internal sealed class GenderContext
{
    private readonly ModConfig config;

    public GenderContext(ModConfig config)
    {
        this.config = config;
    }

    // -------------------------------------------------------------------------
    // Player
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns "Male" or "Female" for the player, honouring <see cref="ModConfig.PlayerGenderOverride"/>.
    /// Falls back to the actual Farmer gender when the override is "Auto" or absent.
    /// </summary>
    public string GetEffectivePlayerGender()
    {
        if (!string.IsNullOrEmpty(this.config.PlayerGenderOverride)
            && !this.config.PlayerGenderOverride.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return this.config.PlayerGenderOverride;

        try
        {
            // SDV 1.6: Farmer.Gender is StardewValley.Gender enum (Male / Female)
            if (Game1.player is not null)
                return Game1.player.Gender.ToString();
        }
        catch { /* safe fallback */ }

        return "Male";
    }

    /// <summary>True when the player's gender has been manually overridden in config.</summary>
    public bool IsPlayerGenderOverridden =>
        !string.IsNullOrEmpty(this.config.PlayerGenderOverride)
        && !this.config.PlayerGenderOverride.Equals("Auto", StringComparison.OrdinalIgnoreCase);

    // -------------------------------------------------------------------------
    // NPC
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns "Male" or "Female" for the named NPC, honouring
    /// <see cref="ModConfig.NpcGenderOverrides"/>.
    /// </summary>
    public string GetEffectiveNpcGender(string npcName)
    {
        if (this.config.NpcGenderOverrides.TryGetValue(npcName, out string? overrideStr)
            && !string.IsNullOrEmpty(overrideStr))
            return overrideStr;

        try
        {
            NPC? npc = Game1.getCharacterFromName(npcName);
            if (npc is not null)
                return npc.Gender.ToString();
        }
        catch { /* safe fallback */ }

        return "Female";
    }

    // -------------------------------------------------------------------------
    // Scene-type derivation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Derives which <see cref="SceneTypeFlags"/> value corresponds to the current effective
    /// player + NPC genders.
    /// </summary>
    public SceneTypeFlags DeriveSceneType(string npcName)
    {
        string playerGender = this.GetEffectivePlayerGender();
        string npcGender    = this.GetEffectiveNpcGender(npcName);

        bool playerFemale = playerGender.StartsWith("F", StringComparison.OrdinalIgnoreCase);
        bool npcFemale    = npcGender.StartsWith("F", StringComparison.OrdinalIgnoreCase);

        return (playerFemale, npcFemale) switch
        {
            (true,  false) => SceneTypeFlags.FemaleMale,
            (false, true)  => SceneTypeFlags.FemaleMale,   // canonical FM (player is considered "protagonist")
            (true,  true)  => SceneTypeFlags.FemaleFemale,
            (false, false) => SceneTypeFlags.MaleMale,
        };
    }
}
