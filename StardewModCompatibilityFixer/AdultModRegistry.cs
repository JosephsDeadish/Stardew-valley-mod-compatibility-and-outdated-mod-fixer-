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
            Priority = 10
        },
        new()
        {
            FamilyName = "XTardewValley",
            IdPatterns = ["xtardew"],
            OwnedAssetPatterns = ["Characters/", "Portraits/", "Characters/Farmer_"],
            CompatibleFamilyNames = ["ValleyGirls"],
            Priority = 8
        },
        new()
        {
            FamilyName = "ValleyGirls",
            IdPatterns = ["valleygirl", "valley_girl", "valley girls"],
            OwnedAssetPatterns = ["Characters/"],
            CompatibleFamilyNames = ["XTardewValley", "HaremValley"],
            Priority = 5
        },
        new()
        {
            FamilyName = "HaremValley",
            IdPatterns = ["harem"],
            OwnedAssetPatterns = ["Characters/", "Portraits/"],
            CompatibleFamilyNames = ["ValleyGirls"],
            Priority = 7
        },
        new()
        {
            FamilyName = "SwimMod",
            IdPatterns = ["aedenthorn.swim", "swimmod", "swim_mod"],
            OwnedAssetPatterns = ["Characters/Farmer_"],
            CompatibleFamilyNames = ["NakedFarmer", "Swimsuits"],
            Priority = 4
        },
        new()
        {
            FamilyName = "NakedFarmer",
            IdPatterns = ["naked", "nakedfarmer", "naked_farmer"],
            OwnedAssetPatterns = ["Characters/Farmer_"],
            CompatibleFamilyNames = ["SwimMod", "Swimsuits"],
            Priority = 4
        },
        new()
        {
            FamilyName = "Swimsuits",
            IdPatterns = ["swimsuit"],
            OwnedAssetPatterns = ["Characters/Farmer_"],
            CompatibleFamilyNames = ["SwimMod", "NakedFarmer"],
            Priority = 4
        },
        new()
        {
            FamilyName = "DarkClub",
            IdPatterns = ["darkclub", "dark_club", "dark club"],
            OwnedAssetPatterns = ["Characters/", "Portraits/"],
            CompatibleFamilyNames = [],
            Priority = 6
        }
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
}
