using System.Text.Json;
using StardewModdingAPI;

namespace StardewModCompatibilityFixer;

/// <summary>
/// Scans Content Patcher <c>content.json</c> files inside each adult mod's folder to
/// discover which event IDs belong to that mod.  The resulting map is consumed by
/// <see cref="SceneContextTracker"/> to identify which adult mod's scene is running.
/// </summary>
internal static class ContentPackEventScanner
{
    /// <summary>
    /// For every resolved adult mod, scans its directory for CP event-data edits and
    /// returns a mapping of <c>eventId → adultModFamilyName</c>.
    /// </summary>
    public static Dictionary<string, string> BuildEventIdMap(
        IEnumerable<ResolvedAdultMod> activeMods,
        IMonitor monitor)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (ResolvedAdultMod resolved in activeMods)
        {
            try
            {
                ScanModDirectory(resolved.Info.DirectoryPath, resolved.Definition.FamilyName, map, monitor);
            }
            catch (Exception ex)
            {
                monitor.Log(
                    $"[CompatFixer] Warning: could not scan {resolved.Definition.FamilyName} for event IDs: {ex.Message}",
                    LogLevel.Debug);
            }
        }

        return map;
    }

    // -------------------------------------------------------------------------

    private static void ScanModDirectory(
        string directory,
        string familyName,
        Dictionary<string, string> map,
        IMonitor monitor)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (string file in Directory.EnumerateFiles(directory, "content.json", SearchOption.AllDirectories))
        {
            try
            {
                ExtractEventIds(file, familyName, map);
            }
            catch (Exception ex)
            {
                monitor.Log($"[CompatFixer] Skipping {file}: {ex.Message}", LogLevel.Trace);
            }
        }
    }

    private static void ExtractEventIds(string contentJsonPath, string familyName, Dictionary<string, string> map)
    {
        string json = File.ReadAllText(contentJsonPath);
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });

        if (!doc.RootElement.TryGetProperty("Changes", out JsonElement changes))
            return;

        foreach (JsonElement change in changes.EnumerateArray())
        {
            if (!change.TryGetProperty("Action", out JsonElement action))
                continue;
            if (!action.GetString()?.Equals("EditData", StringComparison.OrdinalIgnoreCase) ?? true)
                continue;
            if (!change.TryGetProperty("Target", out JsonElement target))
                continue;

            string? targetStr = target.GetString();
            if (targetStr is null || !targetStr.StartsWith("Data/Events/", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!change.TryGetProperty("Entries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Object)
                continue;

            foreach (JsonProperty entry in entries.EnumerateObject())
            {
                // Event keys look like "12345678/f NpcName 500/..." — take only the ID prefix
                string eventKey = entry.Name;
                int slash = eventKey.IndexOf('/');
                string eventId = slash >= 0 ? eventKey[..slash] : eventKey;

                if (!string.IsNullOrWhiteSpace(eventId) && !map.ContainsKey(eventId))
                    map[eventId] = familyName;
            }
        }
    }
}
