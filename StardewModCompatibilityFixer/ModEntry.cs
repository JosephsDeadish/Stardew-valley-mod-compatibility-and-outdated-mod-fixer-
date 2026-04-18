using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StardewModCompatibilityFixer;

public sealed class ModEntry : Mod
{
    private static readonly string[] PortraitureIds = ["Pepper.Portraiture", "jok.Portraiture", "Portraiture"];
    private static readonly string[] BodyModIdHints = ["xtardew", "lewdew", "valleygirls", "harem", "swim", "naked", "swimsuit", "darkclub"];
    private static readonly HashSet<string> SupportedImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    private readonly Dictionary<string, List<PortraitSource>> portraitSourcesByNpc = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IModInfo> activeBodyMods = [];
    private ModConfig config = new();
    private int randomSeed;
    private bool portraitureInstalled;

    public override void Entry(IModHelper helper)
    {
        this.config = helper.ReadConfig<ModConfig>();
        this.randomSeed = (int)DateTime.UtcNow.Ticks;

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.RefreshCompatibilityState();
        this.RegisterConfigMenu();
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.RefreshCompatibilityState();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.randomSeed = unchecked(this.randomSeed * 31 + Game1.uniqueIDForThisGame.GetHashCode() + Game1.dayOfMonth);
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.Name.IsEquivalentTo("Portraits", useBaseName: true) && !e.Name.BaseName.StartsWith("Portraits/", StringComparison.OrdinalIgnoreCase))
            return;

        string npcName = this.TryGetNpcName(e.Name.BaseName);
        if (string.IsNullOrWhiteSpace(npcName))
            return;

        PortraitSource? selected = this.SelectPortraitSource(npcName);
        if (selected is null || !File.Exists(selected.FilePath))
            return;

        e.LoadFrom(
            () => this.LoadTexture(selected.FilePath),
            AssetLoadPriority.Low
        );
    }

    private void RefreshCompatibilityState()
    {
        this.portraitureInstalled = this.ModRegistry.GetAll().Any(mod => PortraitureIds.Contains(mod.Manifest.UniqueID, StringComparer.OrdinalIgnoreCase));

        this.IndexPortraitMods();
        this.IndexBodyMods();

        if (this.portraitureInstalled && this.config.EnableAutoPortraitureSync)
            this.SyncPortraitsToPortraiture();
    }

    private void IndexPortraitMods()
    {
        this.portraitSourcesByNpc.Clear();

        foreach (IModInfo modInfo in this.ModRegistry.GetAll())
        {
            if (!Directory.Exists(modInfo.DirectoryPath))
                continue;

            IEnumerable<string> portraitCandidates = Directory
                .EnumerateFiles(modInfo.DirectoryPath, "*.*", SearchOption.AllDirectories)
                .Where(path => SupportedImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Where(path =>
                {
                    string normalized = path.Replace('\\', '/');
                    return normalized.Contains("/Portrait", StringComparison.OrdinalIgnoreCase)
                           || normalized.Contains("/portrait", StringComparison.OrdinalIgnoreCase);
                });

            foreach (string filePath in portraitCandidates)
            {
                string npcName = this.GetNpcNameFromFile(filePath);
                if (string.IsNullOrWhiteSpace(npcName))
                    continue;

                bool isAnimated = this.IsAnimatedPortrait(filePath, modInfo.Manifest.UniqueID, modInfo.Manifest.Name);
                var source = new PortraitSource(npcName, modInfo.Manifest.UniqueID, filePath, isAnimated);

                if (!this.portraitSourcesByNpc.TryGetValue(npcName, out List<PortraitSource>? list))
                {
                    list = [];
                    this.portraitSourcesByNpc[npcName] = list;
                }

                if (!list.Any(existing => string.Equals(existing.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
                    list.Add(source);
            }
        }

        this.Monitor.Log($"Indexed {this.portraitSourcesByNpc.Count} NPC portrait groups across installed mods.", LogLevel.Info);
    }

    private void IndexBodyMods()
    {
        this.activeBodyMods.Clear();

        foreach (IModInfo modInfo in this.ModRegistry.GetAll())
        {
            string idAndName = $"{modInfo.Manifest.UniqueID}|{modInfo.Manifest.Name}";
            if (BodyModIdHints.Any(hint => idAndName.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                this.activeBodyMods.Add(modInfo);
        }

        if (this.activeBodyMods.Count > 1)
        {
            string loaded = string.Join(", ", this.activeBodyMods.Select(mod => mod.Manifest.UniqueID));
            this.Monitor.Log($"Detected multiple body mods ({loaded}). Compatibility mediation is active.", LogLevel.Warn);
        }
        else if (this.activeBodyMods.Count == 1)
        {
            this.Monitor.Log($"Detected body mod: {this.activeBodyMods[0].Manifest.UniqueID}.", LogLevel.Info);
        }
    }

    private void SyncPortraitsToPortraiture()
    {
        IModInfo? portraiture = this.ModRegistry.GetAll()
            .FirstOrDefault(mod => PortraitureIds.Contains(mod.Manifest.UniqueID, StringComparer.OrdinalIgnoreCase));

        if (portraiture is null || !Directory.Exists(portraiture.DirectoryPath))
            return;

        string portraitureRoot = Path.Combine(portraiture.DirectoryPath, "Portraits");
        Directory.CreateDirectory(portraitureRoot);

        foreach (List<PortraitSource> sources in this.portraitSourcesByNpc.Values)
        {
            foreach (PortraitSource source in sources)
            {
                string destinationFolder = Path.Combine(portraitureRoot, this.SanitizeFolderName(source.ModUniqueId));
                Directory.CreateDirectory(destinationFolder);

                string destinationPath = Path.Combine(destinationFolder, Path.GetFileName(source.FilePath));
                if (File.Exists(destinationPath))
                    continue;

                File.Copy(source.FilePath, destinationPath, overwrite: false);
            }
        }
    }

    private void RegisterConfigMenu()
    {
        IGenericModConfigMenuApi? gmcmApi = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcmApi is null)
            return;

        gmcmApi.Register(
            this.ModManifest,
            reset: () => this.config = new ModConfig(),
            save: () => this.Helper.WriteConfig(this.config)
        );

        gmcmApi.AddBoolOption(
            this.ModManifest,
            () => this.config.EnableAutoPortraitureSync,
            value => this.config.EnableAutoPortraitureSync = value,
            () => "Auto-sync portraits to Portraiture",
            () => "If Portraiture is installed, copy detected portrait files into Portraiture automatically."
        );

        gmcmApi.AddBoolOption(
            this.ModManifest,
            () => this.config.EnablePortraitRandomization,
            value => this.config.EnablePortraitRandomization = value,
            () => "Randomize portraits",
            () => "Choose a random available portrait source each time an NPC portrait is requested."
        );

        gmcmApi.AddBoolOption(
            this.ModManifest,
            () => this.config.PreferAnimatedPortraits,
            value => this.config.PreferAnimatedPortraits = value,
            () => "Prefer animated portraits",
            () => "Prioritize portrait sources detected as animated when available."
        );

        if (this.portraitSourcesByNpc.Count == 0)
            return;

        gmcmApi.AddSectionTitle(this.ModManifest, () => "Per-NPC portrait selection");
        gmcmApi.AddParagraph(this.ModManifest, () => "Set a preferred source mod for specific NPC portraits.");

        foreach (string npcName in this.portraitSourcesByNpc.Keys.OrderBy(name => name))
        {
            string[] allowedValues = this.portraitSourcesByNpc[npcName]
                .Select(source => source.ModUniqueId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .ToArray();

            if (allowedValues.Length == 0)
                continue;

            gmcmApi.AddTextOption(
                this.ModManifest,
                getValue: () => this.config.PreferredPortraitModByNpc.TryGetValue(npcName, out string? value) ? value : allowedValues[0],
                setValue: value => this.config.PreferredPortraitModByNpc[npcName] = value,
                name: () => npcName,
                tooltip: () => $"Preferred portrait source for {npcName}.",
                allowedValues: allowedValues
            );
        }
    }

    private PortraitSource? SelectPortraitSource(string npcName)
    {
        if (!this.portraitSourcesByNpc.TryGetValue(npcName, out List<PortraitSource>? sources) || sources.Count == 0)
            return null;

        if (this.config.PreferredPortraitModByNpc.TryGetValue(npcName, out string? preferredModId))
        {
            PortraitSource? preferred = sources.FirstOrDefault(source => string.Equals(source.ModUniqueId, preferredModId, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
                return preferred;
        }

        IEnumerable<PortraitSource> candidates = sources;
        if (this.config.PreferAnimatedPortraits && sources.Any(source => source.IsAnimated))
            candidates = sources.Where(source => source.IsAnimated);

        PortraitSource[] ordered = candidates
            .OrderBy(source => source.ModUniqueId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ordered.Length == 0)
            return null;

        if (!this.config.EnablePortraitRandomization)
            return ordered[0];

        int seed = HashCode.Combine(this.randomSeed, npcName, Game1.ticks);
        int index = Math.Abs(seed % ordered.Length);
        return ordered[index];
    }

    private Texture2D LoadTexture(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        return Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
    }

    private string GetNpcNameFromFile(string filePath)
    {
        string raw = Path.GetFileNameWithoutExtension(filePath).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        int splitAt = raw.IndexOfAny(['_', '-', ' ', '.']);
        if (splitAt > 0)
            raw = raw[..splitAt];

        return raw;
    }

    private string TryGetNpcName(string baseAssetName)
    {
        string normalized = baseAssetName.Replace('\\', '/');
        int slashIndex = normalized.LastIndexOf('/');
        if (slashIndex < 0 || slashIndex == normalized.Length - 1)
            return string.Empty;

        return normalized[(slashIndex + 1)..];
    }

    private bool IsAnimatedPortrait(string filePath, string uniqueId, string modName)
    {
        string normalized = filePath.Replace('\\', '/');
        return normalized.Contains("animated", StringComparison.OrdinalIgnoreCase)
               || Path.GetExtension(filePath).Equals(".gif", StringComparison.OrdinalIgnoreCase)
               || uniqueId.Contains("animated", StringComparison.OrdinalIgnoreCase)
               || modName.Contains("animated", StringComparison.OrdinalIgnoreCase);
    }

    private string SanitizeFolderName(string folderName)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            folderName = folderName.Replace(invalid, '_');

        return folderName;
    }
}
