using StardewModdingAPI;

namespace StardewModCompatibilityFixer;

/// <summary>Pairing of an <see cref="AdultModDefinition"/> with the actually-installed <see cref="IModInfo"/>.</summary>
internal sealed record ResolvedAdultMod(AdultModDefinition Definition, IModInfo Info);

/// <summary>
/// Static knowledge base of known adult mod families with helpers for runtime resolution,
/// compatibility checking, and asset-ownership queries.
/// </summary>
internal static class AdultModRegistry
{
    /// <summary>All known adult mod definitions.  Add entries here to support new mods automatically.</summary>
    internal static readonly AdultModDefinition[] KnownDefinitions =
    [
        new()
        {
            FamilyName = "LewdewValley",
            IdPatterns = ["lewdew"],
            OwnedAssetPatterns = ["Characters/", "Portraits/"],
            CompatibleFamilyNames = [],
            Priority = 10,
            SupportedSceneTypes = SceneTypeFlags.FemaleMale | SceneTypeFlags.FemaleFemale
        },
        new()
        {
            FamilyName = "XTardewValley",
            IdPatterns = ["xtardew"],
            OwnedAssetPatterns = ["Characters/", "Portraits/", "Characters/Farmer_"],
            CompatibleFamilyNames = ["ValleyGirls"],
            Priority = 8,
            SupportedSceneTypes = SceneTypeFlags.FemaleMale | SceneTypeFlags.FemaleFemale | SceneTypeFlags.MaleMale
        },
        new()
        {
            FamilyName = "ValleyGirls",
            IdPatterns = ["valleygirl", "valley_girl", "valley girls"],
            OwnedAssetPatterns = ["Characters/"],
            CompatibleFamilyNames = ["XTardewValley", "HaremValley"],
            Priority = 5,
            SupportedSceneTypes = SceneTypeFlags.FemaleMale | SceneTypeFlags.FemaleFemale
        },
        new()
        {
            FamilyName = "HaremValley",
            IdPatterns = ["harem"],
            OwnedAssetPatterns = ["Characters/", "Portraits/"],
            CompatibleFamilyNames = ["ValleyGirls"],
            Priority = 7,
            SupportedSceneTypes = SceneTypeFlags.FemaleMale
        },
        new()
        {
            FamilyName = "SwimMod",
            IdPatterns = ["aedenthorn.swim", "swimmod", "swim_mod"],
            OwnedAssetPatterns = ["Characters/Farmer_"],
            CompatibleFamilyNames = ["NakedFarmer", "Swimsuits"],
            Priority = 4,
            SupportedSceneTypes = SceneTypeFlags.FemaleMale
        },
        new()
        {
            FamilyName = "NakedFarmer",
            IdPatterns = ["naked", "nakedfarmer", "naked_farmer"],
            OwnedAssetPatterns = ["Characters/Farmer_"],
            CompatibleFamilyNames = ["SwimMod", "Swimsuits"],
            Priority = 4,
            SupportedSceneTypes = SceneTypeFlags.FemaleMale | SceneTypeFlags.FemaleFemale | SceneTypeFlags.MaleMale
        },
        new()
        {
            FamilyName = "Swimsuits",
            IdPatterns = ["swimsuit"],
            OwnedAssetPatterns = ["Characters/Farmer_"],
            CompatibleFamilyNames = ["SwimMod", "NakedFarmer"],
            Priority = 4,
            SupportedSceneTypes = SceneTypeFlags.FemaleMale
        },
        new()
        {
            FamilyName = "DarkClub",
            IdPatterns = ["darkclub", "dark_club", "dark club"],
            OwnedAssetPatterns = ["Characters/", "Portraits/"],
            CompatibleFamilyNames = [],
            Priority = 6,
            SupportedSceneTypes = SceneTypeFlags.FemaleMale | SceneTypeFlags.FemaleFemale | SceneTypeFlags.MaleMale
        }
    ];

    // -------------------------------------------------------------------------
    // Keywords used to heuristically identify unknown adult mods
    // -------------------------------------------------------------------------

    private static readonly string[] AdultHeuristicKeywords =
    [
        "lewdew", "xtardew", "nsfw", "adult", "hentai", "lewd", "sexy", "ecchi",
        "18+", "mature", "nude", "naked", "erotic", "xxx", "h-scene", "hscene",
        "swimsuit", "underwear", "lingerie", "darkclub", "valleygirl", "harem",
        "bikini", "uncensored", "naughty", "risque"
    ];

    // -------------------------------------------------------------------------
    // Runtime resolution helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Matches all installed mods against the known definitions and returns
    /// a <see cref="ResolvedAdultMod"/> for every match.
    /// </summary>
    public static List<ResolvedAdultMod> Resolve(IEnumerable<IModInfo> allMods)
    {
        var result = new List<ResolvedAdultMod>();
        foreach (IModInfo modInfo in allMods)
        {
            string searchTarget = $"{modInfo.Manifest.UniqueID}|{modInfo.Manifest.Name}";
            foreach (AdultModDefinition def in KnownDefinitions)
            {
                if (def.IdPatterns.Any(p => searchTarget.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(new ResolvedAdultMod(def, modInfo));
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Heuristically resolves mods that are not in the static knowledge base but appear to
    /// be adult mods based on their ID, name, or description keywords.
    /// Returns synthetic <see cref="ResolvedAdultMod"/> entries for each heuristic match.
    /// Already-resolved mods (from <paramref name="alreadyResolved"/>) are skipped.
    /// </summary>
    public static List<ResolvedAdultMod> ResolveUnknown(
        IEnumerable<IModInfo> allMods,
        ICollection<ResolvedAdultMod> alreadyResolved)
    {
        var knownIds = alreadyResolved
            .Select(r => r.Info.Manifest.UniqueID)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new List<ResolvedAdultMod>();

        foreach (IModInfo modInfo in allMods)
        {
            if (knownIds.Contains(modInfo.Manifest.UniqueID))
                continue;

            string searchTarget = $"{modInfo.Manifest.UniqueID}|{modInfo.Manifest.Name}"
                + $"|{modInfo.Manifest.Description}";

            if (!AdultHeuristicKeywords.Any(kw =>
                    searchTarget.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Create a synthetic definition for this unknown mod
            var syntheticDef = new AdultModDefinition
            {
                FamilyName          = $"Unknown_{SanitizeName(modInfo.Manifest.UniqueID)}",
                IdPatterns          = [modInfo.Manifest.UniqueID],
                OwnedAssetPatterns  = ["Characters/", "Portraits/"],
                CompatibleFamilyNames = [],
                Priority            = 3,
                SupportedSceneTypes = SceneTypeFlags.FemaleMale  // conservative default
            };
            result.Add(new ResolvedAdultMod(syntheticDef, modInfo));
        }

        return result;
    }

    /// <summary>Returns true when two adult mod families are marked as body-compatible.</summary>
    public static bool AreCompatible(string familyA, string familyB)
    {
        AdultModDefinition? def = KnownDefinitions.FirstOrDefault(
            d => d.FamilyName.Equals(familyA, StringComparison.OrdinalIgnoreCase));
        return def?.CompatibleFamilyNames
            .Any(n => n.Equals(familyB, StringComparison.OrdinalIgnoreCase)) ?? false;
    }

    /// <summary>
    /// Returns the highest-priority active mod that has an ownership claim on the given asset path.
    /// Returns <c>null</c> when no active mod claims the asset.
    /// </summary>
    public static ResolvedAdultMod? GetHighestPriorityOwner(string assetName, IEnumerable<ResolvedAdultMod> activeMods)
    {
        return activeMods
            .Where(r => r.Definition.OwnedAssetPatterns
                .Any(p => assetName.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.Definition.Priority)
            .FirstOrDefault();
    }

    // -------------------------------------------------------------------------

    private static string SanitizeName(string raw)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
            raw = raw.Replace(c, '_');
        return raw;
    }
}
