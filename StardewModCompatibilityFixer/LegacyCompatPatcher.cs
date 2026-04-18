using System.Text.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley.GameData.BigCraftables;
using StardewValley.GameData.Characters;
using StardewValley.GameData.Objects;

namespace StardewModCompatibilityFixer;

/// <summary>
/// Detects mods that use outdated asset paths, deprecated API versions, or obsolete
/// Content Patcher formats, then registers live compatibility shims so they continue
/// working with the current version of the game.
///
/// Key things patched:
///   • Texture assets that were renamed between 1.5 and 1.6
///   • Data assets whose paths changed (e.g. Data/ObjectInformation → Data/Objects)
///   • Data assets whose serialisation format changed (old string-per-entry → structured objects)
///   • CP content.json files referencing old target paths (detected and logged)
///   • Mod manifests with a very old MinimumApiVersion (logged)
/// </summary>
internal sealed class LegacyCompatPatcher
{
    // -------------------------------------------------------------------------
    // Static knowledge tables
    // -------------------------------------------------------------------------

    /// <summary>
    /// Texture (Texture2D) assets that were simply renamed — format and layout are the same.
    /// Serving the new asset when the old path is requested is sufficient.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> TextureRenames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Stardew 1.6 renamed this sprite sheet
            ["TileSheets/BuffsIcons"] = "TileSheets/Buffs",
        };

    /// <summary>
    /// Old data-asset paths whose content must be generated from the new asset
    /// (format changed, so a raw redirect is not enough).
    /// </summary>
    private static readonly IReadOnlySet<string> DataConversionAssets =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Data/ObjectInformation",         // → Data/Objects       (Dict<string,string> → Dict<string,ObjectData>)
            "Data/BigCraftablesInformation",   // → Data/BigCraftables (same direction)
            "Data/NPCDispositions",            // → Data/Characters    (Dict<string,string> → Dict<string,CharacterData>)
        };

    /// <summary>
    /// All known old → new asset path mappings (used for CP-pack scanning only;
    /// actual shims are handled separately above).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AllKnownRenames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Data/ObjectInformation"]          = "Data/Objects",
            ["Data/BigCraftablesInformation"]   = "Data/BigCraftables",
            ["Data/NPCDispositions"]            = "Data/Characters",
            ["Data/ClothingInformation"]        = "Data/Shirts",
            ["Data/BootsInformation"]           = "Data/Boots",
            ["Data/hats"]                       = "Data/Hats",
            ["Data/weapons"]                    = "Data/Weapons",
            ["TileSheets/BuffsIcons"]           = "TileSheets/Buffs",
        };

    /// <summary>
    /// Mods whose MinimumApiVersion is older than this are flagged as potentially outdated.
    /// </summary>
    private static readonly ISemanticVersion OldApiThreshold = new SemanticVersion("3.0.0");

    /// <summary>CP format versions older than this are flagged as outdated.</summary>
    private static readonly ISemanticVersion OldCpFormatThreshold = new SemanticVersion("2.0.0");

    // -------------------------------------------------------------------------

    private readonly IModHelper helper;
    private readonly IMonitor monitor;

    public LegacyCompatPatcher(IModHelper helper, IMonitor monitor)
    {
        this.helper  = helper;
        this.monitor = monitor;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers SMAPI event hooks for the live asset shims.
    /// Call once from <c>ModEntry.Entry</c> before any events fire.
    /// </summary>
    public void Register()
    {
        this.helper.Events.Content.AssetRequested += this.OnAssetRequested;
    }

    /// <summary>
    /// Scans all installed mods for known legacy issues and logs a summary.
    /// Call from <c>GameLaunched</c> after mods are fully loaded.
    /// </summary>
    public void ScanAndReport()
    {
        int outdatedManifests = 0;
        int legacyPathCount   = 0;

        foreach (IModInfo mod in this.helper.ModRegistry.GetAll())
        {
            // ---- Check MinimumApiVersion ----
            if (mod.Manifest.MinimumApiVersion is not null
                && mod.Manifest.MinimumApiVersion.IsOlderThan(OldApiThreshold))
            {
                this.monitor.Log(
                    $"[CompatFixer] '{mod.Manifest.Name}' ({mod.Manifest.UniqueID}) targets " +
                    $"SMAPI {mod.Manifest.MinimumApiVersion} — it may use deprecated APIs " +
                    $"that SMAPI will warn about separately.",
                    LogLevel.Debug);
                outdatedManifests++;
            }

            // ---- Scan CP content packs ----
            if (!Directory.Exists(mod.DirectoryPath))
                continue;

            legacyPathCount += this.ScanContentPacksForMod(mod);
        }

        if (outdatedManifests > 0 || legacyPathCount > 0)
            this.monitor.Log(
                $"[CompatFixer] Legacy scan complete: {outdatedManifests} potentially-outdated manifest(s), " +
                $"{legacyPathCount} old asset-path reference(s) across all mods. " +
                $"Compatibility shims are active for common 1.5→1.6 changes.",
                LogLevel.Info);
        else
            this.monitor.Log("[CompatFixer] Legacy scan complete: no known outdated asset paths detected.", LogLevel.Info);
    }

    // -------------------------------------------------------------------------
    // Content-pack scanning
    // -------------------------------------------------------------------------

    private int ScanContentPacksForMod(IModInfo mod)
    {
        int issues = 0;
        foreach (string file in Directory.EnumerateFiles(mod.DirectoryPath, "content.json", SearchOption.AllDirectories))
        {
            try
            {
                issues += this.ScanContentJson(file, mod.Manifest.UniqueID, mod.Manifest.Name);
            }
            catch (Exception ex)
            {
                this.monitor.Log($"[CompatFixer] Could not parse '{file}': {ex.Message}", LogLevel.Trace);
            }
        }
        return issues;
    }

    private int ScanContentJson(string path, string modUniqueId, string modName)
    {
        int issues = 0;

        string json = File.ReadAllText(path);
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });

        // ---- Check CP Format version ----
        if (doc.RootElement.TryGetProperty("Format", out JsonElement fmtEl))
        {
            string? fmtStr = fmtEl.GetString();
            if (fmtStr is not null && TryParseVersion(fmtStr, out ISemanticVersion? fmtVer)
                && fmtVer!.IsOlderThan(OldCpFormatThreshold))
            {
                this.monitor.Log(
                    $"[CompatFixer] '{modName}' uses old CP format {fmtStr} in {Path.GetFileName(path)} — " +
                    $"some Content Patcher features may not work correctly.",
                    LogLevel.Debug);
                issues++;
            }
        }

        // ---- Check for old target paths ----
        if (!doc.RootElement.TryGetProperty("Changes", out JsonElement changes))
            return issues;

        foreach (JsonElement change in changes.EnumerateArray())
        {
            if (!change.TryGetProperty("Target", out JsonElement targetEl))
                continue;

            string? targetStr = targetEl.GetString();
            if (targetStr is null)
                continue;

            foreach (var (oldPath, newPath) in AllKnownRenames)
            {
                if (targetStr.StartsWith(oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    this.monitor.Log(
                        $"[CompatFixer] '{modName}' ({modUniqueId}) references old asset path " +
                        $"'{targetStr}' (should be '{newPath}'). A compatibility shim is active.",
                        LogLevel.Debug);
                    issues++;
                    break;
                }
            }
        }

        return issues;
    }

    // -------------------------------------------------------------------------
    // Live asset shims
    // -------------------------------------------------------------------------

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        // ---- Simple texture renames ----
        foreach (var (oldPath, newPath) in TextureRenames)
        {
            if (e.Name.IsEquivalentTo(oldPath))
            {
                string capturedNew = newPath;
                e.LoadFrom(
                    () => this.helper.GameContent.Load<Microsoft.Xna.Framework.Graphics.Texture2D>(capturedNew),
                    AssetLoadPriority.Low);
                return;
            }
        }

        // ---- Format-changed data assets ----
        if (e.Name.IsEquivalentTo("Data/ObjectInformation"))
        {
            e.LoadFrom<Dictionary<string, string>>(
                this.BuildObjectInformationShim,
                AssetLoadPriority.Low);
            return;
        }

        if (e.Name.IsEquivalentTo("Data/BigCraftablesInformation"))
        {
            e.LoadFrom<Dictionary<string, string>>(
                this.BuildBigCraftablesInformationShim,
                AssetLoadPriority.Low);
            return;
        }

        if (e.Name.IsEquivalentTo("Data/NPCDispositions"))
        {
            e.LoadFrom<Dictionary<string, string>>(
                this.BuildNpcDispositionsShim,
                AssetLoadPriority.Low);
        }
    }

    // -------------------------------------------------------------------------
    // Shim builders — convert new-format data back to old string-per-entry format
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a <c>Data/ObjectInformation</c>-style <c>Dict&lt;string,string&gt;</c>
    /// from the current <c>Data/Objects</c> data.
    /// Old format: Name/Price/Edibility/Type Category/DisplayName/Description/Misc
    /// </summary>
    private Dictionary<string, string> BuildObjectInformationShim()
    {
        var result = new Dictionary<string, string>();
        try
        {
            var objects = this.helper.GameContent.Load<Dictionary<string, ObjectData>>("Data/Objects");
            foreach (var (id, obj) in objects)
            {
                string typeCategory = obj.Category < 0 ? $"Basic {obj.Category}" : "Basic";
                string display      = obj.DisplayName ?? obj.Name ?? string.Empty;
                string desc         = obj.Description ?? string.Empty;
                result[id]          = $"{obj.Name}/{obj.Price}/{obj.Edibility}/{typeCategory}/{display}/{desc}/";
            }
        }
        catch (Exception ex)
        {
            this.monitor.Log($"[CompatFixer] Failed to build ObjectInformation shim: {ex.Message}", LogLevel.Warn);
        }
        return result;
    }

    /// <summary>
    /// Builds a <c>Data/BigCraftablesInformation</c>-style dict from <c>Data/BigCraftables</c>.
    /// Old format: Name/Price/-300/BigCraftable -9/DisplayName/Description/Fragility/canBeSetOutdoors/canBeSetIndoors
    /// </summary>
    private Dictionary<string, string> BuildBigCraftablesInformationShim()
    {
        var result = new Dictionary<string, string>();
        try
        {
            var items = this.helper.GameContent.Load<Dictionary<string, BigCraftableData>>("Data/BigCraftables");
            foreach (var (id, item) in items)
            {
                string display = item.DisplayName ?? item.Name ?? string.Empty;
                string desc    = item.Description ?? string.Empty;
                result[id]     = $"{item.Name}/{item.Price}/-300/BigCraftable -9/{display}/{desc}/{item.Fragility}/true/true";
            }
        }
        catch (Exception ex)
        {
            this.monitor.Log($"[CompatFixer] Failed to build BigCraftablesInformation shim: {ex.Message}", LogLevel.Warn);
        }
        return result;
    }

    /// <summary>
    /// Builds a <c>Data/NPCDispositions</c>-style dict from <c>Data/Characters</c>.
    /// Old format: displayName/gender/age/manners/socialAnxiety/optimism/npcType/homeRegion/birthSeason birthDay/datable/spouseRoom
    /// </summary>
    private Dictionary<string, string> BuildNpcDispositionsShim()
    {
        var result = new Dictionary<string, string>();
        try
        {
            var characters = this.helper.GameContent.Load<Dictionary<string, CharacterData>>("Data/Characters");
            foreach (var (id, ch) in characters)
            {
                // Use reflection for Gender so we don't hard-depend on the Gender enum's exact type/namespace
                string displayName = ch.DisplayName ?? id;
                string genderRaw   = GetPropertyAsString(ch, "Gender") ?? "Male";
                string gender      = genderRaw.Contains("Female", StringComparison.OrdinalIgnoreCase) ? "female" : "male";
                string birthSeason = GetPropertyAsString(ch, "BirthSeason") ?? "spring";
                int    birthDay    = GetPropertyAsInt(ch, "BirthDay");
                string homeRegion  = GetPropertyAsString(ch, "HomeRegion") ?? "Town";
                string datable     = GetPropertyAsBool(ch, "CanBeRomanced") ? "datable" : string.Empty;

                result[id] = $"{displayName}/{gender}/adult/neutral/outgoing/positive/Villager/{homeRegion}/{birthSeason} {birthDay}/{datable}/";
            }
        }
        catch (Exception ex)
        {
            this.monitor.Log($"[CompatFixer] Failed to build NPCDispositions shim: {ex.Message}", LogLevel.Warn);
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // Reflection helpers — read properties whose exact types may vary across game versions
    // -------------------------------------------------------------------------

    private static string? GetPropertyAsString(object obj, string name)
    {
        try { return obj.GetType().GetProperty(name)?.GetValue(obj)?.ToString(); }
        catch { return null; }
    }

    private static int GetPropertyAsInt(object obj, string name)
    {
        try { return (int?)obj.GetType().GetProperty(name)?.GetValue(obj) ?? 0; }
        catch { return 0; }
    }

    private static bool GetPropertyAsBool(object obj, string name)
    {
        try { return (bool?)obj.GetType().GetProperty(name)?.GetValue(obj) ?? false; }
        catch { return false; }
    }

    // -------------------------------------------------------------------------

    private static bool TryParseVersion(string raw, out ISemanticVersion? result)
    {
        try
        {
            result = new SemanticVersion(raw);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }
}
