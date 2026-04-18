using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StardewModCompatibilityFixer;

public sealed class ModEntry : Mod
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private static readonly string[] PortraitureIds =
        ["Pepper.Portraiture", "jok.Portraiture", "Portraiture"];

    private static readonly HashSet<string> SupportedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp" };

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    // Portrait and sprite source indexes: asset name → list of sources across mods
    private readonly Dictionary<string, List<PortraitSource>> portraitSourcesByAsset =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<SpriteSource>> spriteSourcesByAsset =
        new(StringComparer.OrdinalIgnoreCase);

    // Portrait thumbnail cache for GMCMOptions image carousel (lazily populated)
    private readonly Dictionary<string, Texture2D?> portraitThumbnailCache =
        new(StringComparer.OrdinalIgnoreCase);

    private List<ResolvedAdultMod> activeAdultMods = [];
    private SceneContextTracker? sceneContext;
    private GenderContext? genderContext;
    private ScenePreSelector? scenePreSelector;
    private ModConfig config = new();
    private bool portraitureInstalled;
    private bool gmcmRegistered;
    private LegacyCompatPatcher? legacyPatcher;

    // -------------------------------------------------------------------------
    // Entry
    // -------------------------------------------------------------------------

    public override void Entry(IModHelper helper)
    {
        this.config        = helper.ReadConfig<ModConfig>();
        this.legacyPatcher = new LegacyCompatPatcher(helper, this.Monitor);

        // Register legacy shims BEFORE any assets can be requested
        this.legacyPatcher.Register();

        helper.Events.GameLoop.GameLaunched  += this.OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded    += this.OnSaveLoaded;
        helper.Events.GameLoop.UpdateTicked  += this.OnUpdateTicked;
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
    }

    // -------------------------------------------------------------------------
    // SMAPI event handlers
    // -------------------------------------------------------------------------

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.RefreshCompatibilityState();
        this.legacyPatcher?.ScanAndReport();
        this.RegisterConfigMenu();
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        // Mods don't change after launch, but re-index to pick up dynamic content packs.
        this.RefreshCompatibilityState();
        // Invalidate portrait thumbnail cache so textures are reloaded for the new save
        this.portraitThumbnailCache.Clear();
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        this.sceneContext?.Update();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        string baseName = e.Name.BaseName;

        // ---- Portrait sheets ----
        if (baseName.StartsWith("Portraits/", StringComparison.OrdinalIgnoreCase)
            || e.Name.IsEquivalentTo("Portraits", useBaseName: true))
        {
            this.TryProvidePortrait(e, NormalizeAssetName(baseName));
            return;
        }

        // ---- Character / sprite sheets (skip dialogue sub-path) ----
        if (baseName.StartsWith("Characters/", StringComparison.OrdinalIgnoreCase)
            && !baseName.StartsWith("Characters/Dialogue/", StringComparison.OrdinalIgnoreCase))
        {
            this.TryProvideSprite(e, NormalizeAssetName(baseName));
        }
    }

    // -------------------------------------------------------------------------
    // Asset provision
    // -------------------------------------------------------------------------

    private void TryProvidePortrait(AssetRequestedEventArgs e, string assetName)
    {
        if (!this.portraitSourcesByAsset.TryGetValue(assetName, out List<PortraitSource>? sources)
            || sources.Count == 0)
            return;

        PortraitSource? selected = this.SelectPortraitSource(assetName, sources);
        if (selected is null || !File.Exists(selected.FilePath))
            return;

        string filePath = selected.FilePath; // capture for lambda
        e.LoadFrom(() => this.LoadTexture(filePath), AssetLoadPriority.Low);
    }

    private void TryProvideSprite(AssetRequestedEventArgs e, string assetName)
    {
        if (!this.spriteSourcesByAsset.TryGetValue(assetName, out List<SpriteSource>? sources)
            || sources.Count == 0)
            return;

        SpriteSource? selected = this.SelectSpriteSource(assetName, sources);
        if (selected is null || !File.Exists(selected.FilePath))
            return;

        string filePath = selected.FilePath;
        e.LoadFrom(() => this.LoadTexture(filePath), AssetLoadPriority.Low);
    }

    // -------------------------------------------------------------------------
    // Compatibility state refresh
    // -------------------------------------------------------------------------

    private void RefreshCompatibilityState()
    {
        this.portraitureInstalled = this.ModRegistry.GetAll()
            .Any(mod => PortraitureIds.Contains(mod.Manifest.UniqueID, StringComparer.OrdinalIgnoreCase));

        // Step 1: known adult mod matching
        this.activeAdultMods = AdultModRegistry.Resolve(this.ModRegistry.GetAll());

        // Step 2: heuristic detection of unknown/unregistered adult mods
        List<ResolvedAdultMod> unknownMods = AdultModRegistry.ResolveUnknown(
            this.ModRegistry.GetAll(), this.activeAdultMods);

        if (unknownMods.Count > 0)
        {
            string unknownNames = string.Join(", ", unknownMods.Select(m =>
                $"'{m.Info.Manifest.Name}' ({m.Info.Manifest.UniqueID})"));
            this.Monitor.Log(
                $"[CompatFixer] Heuristically detected {unknownMods.Count} unregistered adult mod(s): {unknownNames}. " +
                $"They will be treated as generic adult mods with FemaleMale scene support.",
                LogLevel.Info);
            this.activeAdultMods.AddRange(unknownMods);
        }

        this.LogAdultModState();

        // Step 3: gender context and scene pre-selector
        this.genderContext    = new GenderContext(this.config);
        this.scenePreSelector = new ScenePreSelector(this.config, this.activeAdultMods, this.genderContext);

        this.IndexPortraitMods();
        this.IndexSpriteMods();

        // Build scene tracker using event-ID map derived from CP content packs
        var eventMap = ContentPackEventScanner.BuildEventIdMap(this.activeAdultMods, this.Monitor);
        this.sceneContext = new SceneContextTracker(eventMap, this.Monitor);

        if (this.portraitureInstalled && this.config.EnableAutoPortraitureSync)
            this.SyncPortraitsToPortraiture();
    }

    private void LogAdultModState()
    {
        if (this.activeAdultMods.Count == 0)
        {
            this.Monitor.Log("[CompatFixer] No adult mods detected.", LogLevel.Debug);
            return;
        }

        string names = string.Join(", ", this.activeAdultMods.Select(m =>
            $"{m.Definition.FamilyName} ({m.Info.Manifest.UniqueID})"));
        this.Monitor.Log($"[CompatFixer] Detected adult mods: {names}.", LogLevel.Info);

        for (int i = 0; i < this.activeAdultMods.Count; i++)
        {
            for (int j = i + 1; j < this.activeAdultMods.Count; j++)
            {
                string fA = this.activeAdultMods[i].Definition.FamilyName;
                string fB = this.activeAdultMods[j].Definition.FamilyName;
                bool compat = AdultModRegistry.AreCompatible(fA, fB);

                if (compat)
                    this.Monitor.Log($"[CompatFixer] {fA} ↔ {fB}: body-compatible, shared assets allowed.", LogLevel.Info);
                else
                    this.Monitor.Log($"[CompatFixer] {fA} ↔ {fB}: NOT body-compatible. Scene-context routing active.", LogLevel.Warn);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Portrait indexing
    // -------------------------------------------------------------------------

    private void IndexPortraitMods()
    {
        this.portraitSourcesByAsset.Clear();

        foreach (IModInfo modInfo in this.ModRegistry.GetAll())
        {
            if (!Directory.Exists(modInfo.DirectoryPath))
                continue;
            try
            {
                this.IndexPortraitsForMod(modInfo);
            }
            catch (Exception ex)
            {
                this.Monitor.Log(
                    $"[CompatFixer] Error indexing portraits for {modInfo.Manifest.UniqueID}: {ex.Message}",
                    LogLevel.Trace);
            }
        }

        this.Monitor.Log($"[CompatFixer] Indexed {this.portraitSourcesByAsset.Count} portrait asset(s).", LogLevel.Info);
    }

    private void IndexPortraitsForMod(IModInfo modInfo)
    {
        string? adultFamily = this.GetAdultFamily(modInfo.Manifest.UniqueID);

        foreach (string filePath in Directory.EnumerateFiles(modInfo.DirectoryPath, "*.*", SearchOption.AllDirectories))
        {
            if (!SupportedImageExtensions.Contains(Path.GetExtension(filePath)))
                continue;

            string? assetName = TryGetAssetName(modInfo.DirectoryPath, filePath, "Portraits");
            if (assetName is null)
                continue;

            bool isAnimated = IsAnimatedPortrait(filePath, modInfo.Manifest.UniqueID, modInfo.Manifest.Name);
            var source = new PortraitSource(assetName, modInfo.Manifest.UniqueID, filePath, isAnimated, adultFamily);

            if (!this.portraitSourcesByAsset.TryGetValue(assetName, out List<PortraitSource>? list))
            {
                list = [];
                this.portraitSourcesByAsset[assetName] = list;
            }

            if (!list.Any(s => string.Equals(s.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
                list.Add(source);
        }
    }

    // -------------------------------------------------------------------------
    // Sprite indexing
    // -------------------------------------------------------------------------

    private void IndexSpriteMods()
    {
        this.spriteSourcesByAsset.Clear();

        foreach (IModInfo modInfo in this.ModRegistry.GetAll())
        {
            if (!Directory.Exists(modInfo.DirectoryPath))
                continue;
            try
            {
                this.IndexSpritesForMod(modInfo);
            }
            catch (Exception ex)
            {
                this.Monitor.Log(
                    $"[CompatFixer] Error indexing sprites for {modInfo.Manifest.UniqueID}: {ex.Message}",
                    LogLevel.Trace);
            }
        }

        this.Monitor.Log($"[CompatFixer] Indexed {this.spriteSourcesByAsset.Count} sprite asset(s).", LogLevel.Info);
    }

    private void IndexSpritesForMod(IModInfo modInfo)
    {
        string? adultFamily = this.GetAdultFamily(modInfo.Manifest.UniqueID);
        SceneTypeFlags supportedScenes = this.activeAdultMods
            .FirstOrDefault(r => string.Equals(r.Info.Manifest.UniqueID, modInfo.Manifest.UniqueID, StringComparison.OrdinalIgnoreCase))
            ?.Definition.SupportedSceneTypes ?? SceneTypeFlags.FemaleMale;

        foreach (string filePath in Directory.EnumerateFiles(modInfo.DirectoryPath, "*.*", SearchOption.AllDirectories))
        {
            if (!SupportedImageExtensions.Contains(Path.GetExtension(filePath)))
                continue;

            string? assetName = TryGetAssetName(modInfo.DirectoryPath, filePath, "Characters");
            if (assetName is null)
                continue;

            var source = new SpriteSource(assetName, modInfo.Manifest.UniqueID, filePath, adultFamily, supportedScenes);

            if (!this.spriteSourcesByAsset.TryGetValue(assetName, out List<SpriteSource>? list))
            {
                list = [];
                this.spriteSourcesByAsset[assetName] = list;
            }

            if (!list.Any(s => string.Equals(s.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
                list.Add(source);
        }
    }

    // -------------------------------------------------------------------------
    // Source selection — portraits
    // -------------------------------------------------------------------------

    private PortraitSource? SelectPortraitSource(string assetName, List<PortraitSource> sources)
    {
        string npcName = GetNpcNameFromAsset(assetName);

        // 1. Scene context: use the owning adult mod's portrait exclusively
        if (this.sceneContext?.ActiveSceneFamilyName is string sceneFamilyName)
        {
            PortraitSource? sceneMatch = sources
                .Where(s => string.Equals(s.AdultModFamily, sceneFamilyName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (sceneMatch is not null)
                return sceneMatch;
        }

        // 2. Per-NPC user config preference
        if (this.config.PreferredPortraitModByNpc.TryGetValue(npcName, out string? preferredId))
        {
            PortraitSource? preferred = sources.FirstOrDefault(s =>
                string.Equals(s.ModUniqueId, preferredId, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
                return preferred;
        }

        // 3. Animated preference filter
        IEnumerable<PortraitSource> candidates = sources;
        if (this.config.PreferAnimatedPortraits && sources.Any(s => s.IsAnimated))
            candidates = sources.Where(s => s.IsAnimated);

        // 4. Priority sort (adult mod priority → mod ID → file path)
        PortraitSource[] ordered = candidates
            .OrderByDescending(s => this.GetModPriority(s.ModUniqueId))
            .ThenBy(s => s.ModUniqueId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.FilePath,    StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ordered.Length == 0)
            return null;

        if (!this.config.EnablePortraitRandomization)
            return ordered[0];

        // 5. Stable daily randomisation
        int baseSeed = this.config.RandomizationSeed != 0
            ? this.config.RandomizationSeed
            : (int)Game1.uniqueIDForThisGame;
        int seed  = HashCode.Combine(baseSeed, npcName, Game1.dayOfMonth);
        return ordered[Math.Abs(seed % ordered.Length)];
    }

    // -------------------------------------------------------------------------
    // Source selection — sprites
    // -------------------------------------------------------------------------

    private SpriteSource? SelectSpriteSource(string assetName, List<SpriteSource> sources)
    {
        string npcName = GetNpcNameFromAsset(assetName);

        // 1. Scene context: use the owning adult mod's sprite, filtered by active scene type
        if (this.sceneContext?.ActiveSceneFamilyName is string sceneFamilyName)
        {
            SceneTypeFlags preferredScene = this.scenePreSelector?.GetPreferredSceneType(npcName)
                ?? SceneTypeFlags.FemaleMale;

            SpriteSource? sceneMatch = sources
                .Where(s => string.Equals(s.AdultModFamily, sceneFamilyName, StringComparison.OrdinalIgnoreCase)
                            && s.SupportedSceneTypes.HasFlag(preferredScene))
                .OrderBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            // Fallback: any source from the scene's mod regardless of scene type
            sceneMatch ??= sources
                .Where(s => string.Equals(s.AdultModFamily, sceneFamilyName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (sceneMatch is not null)
                return sceneMatch;
        }

        // 2. For player sprites, honour the ForceBodyModForPlayer config
        if (assetName.Contains("Farmer_", StringComparison.OrdinalIgnoreCase)
            && this.config.ForceBodyModForPlayer is string forcedFamily
            && !string.IsNullOrEmpty(forcedFamily)
            && !forcedFamily.Equals("(auto)", StringComparison.OrdinalIgnoreCase))
        {
            SpriteSource? forced = sources.FirstOrDefault(s =>
                string.Equals(this.GetAdultFamily(s.ModUniqueId), forcedFamily, StringComparison.OrdinalIgnoreCase));
            if (forced is not null)
                return forced;
        }

        // 3. Per-NPC sprite override from config
        if (this.config.PreferredSpriteModByNpc.TryGetValue(npcName, out string? preferredSpriteId))
        {
            SpriteSource? preferredSprite = sources.FirstOrDefault(s =>
                string.Equals(s.ModUniqueId, preferredSpriteId, StringComparison.OrdinalIgnoreCase));
            if (preferredSprite is not null)
                return preferredSprite;
        }

        // 4. Filter incompatible body sources
        SpriteSource[] compatible = this.FilterCompatibleSprites(sources, assetName);
        if (compatible.Length == 0)
            return null;

        return compatible
            .OrderByDescending(s => this.GetModPriority(s.ModUniqueId))
            .ThenBy(s => s.ModUniqueId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private SpriteSource[] FilterCompatibleSprites(List<SpriteSource> sources, string assetName)
    {
        if (this.activeAdultMods.Count <= 1)
            return sources.ToArray();

        ResolvedAdultMod? owner = AdultModRegistry.GetHighestPriorityOwner(assetName, this.activeAdultMods);
        if (owner is null)
            return sources.ToArray();

        return sources.Where(s =>
        {
            // Always allow the owner's own files
            if (string.Equals(s.ModUniqueId, owner.Info.Manifest.UniqueID, StringComparison.OrdinalIgnoreCase))
                return true;

            // Allow non-adult mods (portrait mods, etc.)
            string? sourceFamily = this.GetAdultFamily(s.ModUniqueId);
            if (sourceFamily is null)
                return true;

            // Allow if the families are body-compatible
            return AdultModRegistry.AreCompatible(owner.Definition.FamilyName, sourceFamily);
        }).ToArray();
    }

    // -------------------------------------------------------------------------
    // Portraiture sync
    // -------------------------------------------------------------------------

    private void SyncPortraitsToPortraiture()
    {
        IModInfo? portraiture = this.ModRegistry.GetAll()
            .FirstOrDefault(mod => PortraitureIds.Contains(mod.Manifest.UniqueID, StringComparer.OrdinalIgnoreCase));

        if (portraiture is null || !Directory.Exists(portraiture.DirectoryPath))
            return;

        string portraitureRoot = Path.Combine(portraiture.DirectoryPath, "Portraits");
        Directory.CreateDirectory(portraitureRoot);

        foreach (List<PortraitSource> sources in this.portraitSourcesByAsset.Values)
        {
            foreach (PortraitSource source in sources)
            {
                try
                {
                    string destFolder = Path.Combine(portraitureRoot, SanitizeFolderName(source.ModUniqueId));
                    Directory.CreateDirectory(destFolder);

                    string destPath = Path.Combine(destFolder, Path.GetFileName(source.FilePath));
                    if (!File.Exists(destPath))
                        File.Copy(source.FilePath, destPath, overwrite: false);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"[CompatFixer] Could not sync {source.FilePath}: {ex.Message}", LogLevel.Trace);
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // GMCM registration
    // -------------------------------------------------------------------------

    private void RegisterConfigMenu()
    {
        if (this.gmcmRegistered)
            return;

        IGenericModConfigMenuApi? gmcm = this.Helper.ModRegistry
            .GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        // Optional image-carousel API (GMCMOptions mod)
        IGMCMOptionsApi? gmcmOpts = this.Helper.ModRegistry
            .GetApi<IGMCMOptionsApi>("spacechase0.GMCMOptions");

        gmcm.Register(
            this.ModManifest,
            reset: () => this.config = new ModConfig(),
            save:  () => this.Helper.WriteConfig(this.config));

        // ---- Portrait settings ----
        gmcm.AddSectionTitle(this.ModManifest, () => "Portrait Settings");

        gmcm.AddBoolOption(this.ModManifest,
            () => this.config.EnableAutoPortraitureSync,
            v  => this.config.EnableAutoPortraitureSync = v,
            () => "Auto-sync to Portraiture",
            () => "When Portraiture is installed, automatically copy detected portrait files into its folder.");

        gmcm.AddBoolOption(this.ModManifest,
            () => this.config.EnablePortraitRandomization,
            v  => this.config.EnablePortraitRandomization = v,
            () => "Randomise portraits",
            () => "Pick a random portrait source for each NPC once per in-game day.");

        gmcm.AddBoolOption(this.ModManifest,
            () => this.config.PreferAnimatedPortraits,
            v  => this.config.PreferAnimatedPortraits = v,
            () => "Prefer animated portraits",
            () => "Prioritise animated portrait sources (e.g. Animated Portraits mods) when available.");

        // ---- Adult mod / gender settings ----
        if (this.activeAdultMods.Count > 0)
        {
            gmcm.AddSectionTitle(this.ModManifest, () => "Adult Mod & Gender Settings");

            string familySummary = string.Join(", ", this.activeAdultMods.Select(m => m.Definition.FamilyName));
            gmcm.AddParagraph(this.ModManifest, () => $"Detected adult mods: {familySummary}.");

            string[] families     = this.activeAdultMods.Select(m => m.Definition.FamilyName).OrderBy(x => x).ToArray();
            string[] familiesAuto = ["(auto)", ..families];

            gmcm.AddTextOption(this.ModManifest,
                () => this.config.ForceBodyModForPlayer ?? "(auto)",
                v  => this.config.ForceBodyModForPlayer = v == "(auto)" ? null : v,
                () => "Force player body sprites",
                () => "Always use a specific adult mod's sprites for the player, even outside its scenes.",
                allowedValues: familiesAuto);

            gmcm.AddTextOption(this.ModManifest,
                () => this.config.PlayerGenderOverride ?? "Auto",
                v  => this.config.PlayerGenderOverride = v,
                () => "Player gender override",
                () => "Override the player's effective gender used for scene-type routing and sprite selection. " +
                      "'Auto' uses the actual character gender.",
                allowedValues: ["Auto", "Male", "Female"]);
        }

        // ---- Per-NPC portrait source (text or image carousel) ----
        bool anyPortraitChoice = this.portraitSourcesByAsset.Values.Any(list =>
            list.Select(s => s.ModUniqueId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

        if (anyPortraitChoice)
        {
            gmcm.AddSectionTitle(this.ModManifest, () => "Per-NPC Portrait Source");
            gmcm.AddParagraph(this.ModManifest, () =>
                gmcmOpts is not null
                    ? "Scroll through portrait previews to pick the source for each NPC."
                    : "Choose which mod's portrait to use for each NPC. Install GMCMOptions for image previews.");

            foreach (string assetName in this.portraitSourcesByAsset.Keys.OrderBy(k => k))
            {
                List<PortraitSource> allSources = this.portraitSourcesByAsset[assetName];
                string[] options = allSources
                    .Select(s => s.ModUniqueId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(v => v)
                    .ToArray();

                if (options.Length < 2)
                    continue;

                string capturedNpc     = GetNpcNameFromAsset(assetName);
                string[] capturedOpts  = options;

                if (gmcmOpts is not null)
                {
                    // Build deduplicated source list per unique mod
                    PortraitSource[] dedupSources = allSources
                        .GroupBy(s => s.ModUniqueId, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .OrderBy(s => s.ModUniqueId, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    this.RegisterImagePortraitOption(gmcm, gmcmOpts, capturedNpc, dedupSources);
                }
                else
                {
                    gmcm.AddTextOption(this.ModManifest,
                        getValue: () => this.config.PreferredPortraitModByNpc.TryGetValue(capturedNpc, out string? v)
                            ? v : capturedOpts[0],
                        setValue: v  => this.config.PreferredPortraitModByNpc[capturedNpc] = v,
                        name:     () => capturedNpc,
                        tooltip:  () => $"Portrait source for {capturedNpc}.",
                        allowedValues: capturedOpts);
                }
            }
        }

        // ---- Per-NPC sprite source ----
        bool anySpriteChoice = this.spriteSourcesByAsset.Values.Any(list =>
            list.Select(s => s.ModUniqueId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

        if (anySpriteChoice)
        {
            gmcm.AddSectionTitle(this.ModManifest, () => "Per-NPC Sprite Source");
            gmcm.AddParagraph(this.ModManifest, () =>
                "Override which mod's sprite sheet is used for each NPC outside of a dedicated adult scene.");

            foreach (string assetName in this.spriteSourcesByAsset.Keys.OrderBy(k => k))
            {
                string[] opts = this.spriteSourcesByAsset[assetName]
                    .Select(s => s.ModUniqueId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(v => v)
                    .ToArray();

                if (opts.Length < 2)
                    continue;

                string capturedNpc  = GetNpcNameFromAsset(assetName);
                string[] capturedOpts = opts;

                gmcm.AddTextOption(this.ModManifest,
                    getValue: () => this.config.PreferredSpriteModByNpc.TryGetValue(capturedNpc, out string? v)
                        ? v : capturedOpts[0],
                    setValue: v  => this.config.PreferredSpriteModByNpc[capturedNpc] = v,
                    name:     () => $"{capturedNpc} (sprite)",
                    tooltip:  () => $"Sprite sheet source for {capturedNpc}. The scene-context system overrides this during adult scenes.",
                    allowedValues: capturedOpts);
            }
        }

        // ---- Per-NPC gender override ----
        if (this.activeAdultMods.Count > 0)
        {
            gmcm.AddSectionTitle(this.ModManifest, () => "NPC Gender Overrides");
            gmcm.AddParagraph(this.ModManifest, () =>
                "Override a specific NPC's gender for scene-type routing. " +
                "Useful for role-playing as or with NPCs using non-default sprites.");

            var allNpcNames = this.portraitSourcesByAsset.Keys
                .Concat(this.spriteSourcesByAsset.Keys)
                .Select(GetNpcNameFromAsset)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            foreach (string npcName in allNpcNames)
            {
                string capturedNpc = npcName;
                gmcm.AddTextOption(this.ModManifest,
                    getValue: () => this.config.NpcGenderOverrides.TryGetValue(capturedNpc, out string? v) ? v : "Auto",
                    setValue: v  =>
                    {
                        if (v == "Auto")
                            this.config.NpcGenderOverrides.Remove(capturedNpc);
                        else
                            this.config.NpcGenderOverrides[capturedNpc] = v;
                    },
                    name:     () => $"{capturedNpc} gender",
                    tooltip:  () => $"Gender routing override for {capturedNpc}. 'Auto' uses the NPC's actual gender.",
                    allowedValues: ["Auto", "Male", "Female"]);
            }
        }

        // ---- Per-NPC scene type preference ----
        if (this.activeAdultMods.Count > 0 && this.scenePreSelector is not null)
        {
            var availableScenes = this.scenePreSelector.GetGloballyAvailableSceneTypes();
            if (availableScenes.Count > 1)
            {
                gmcm.AddSectionTitle(this.ModManifest, () => "Scene Type Preferences");
                gmcm.AddParagraph(this.ModManifest, () =>
                    "Choose which type of adult scene to play with each NPC. " +
                    "Options marked ⚠ are not compatible with your current gender overrides.");

                var allNpcNames = this.portraitSourcesByAsset.Keys
                    .Select(GetNpcNameFromAsset)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();

                foreach (string npcName in allNpcNames)
                {
                    string capturedNpc = npcName;
                    string[] sceneOpts = this.scenePreSelector.BuildSceneTypeOptions(capturedNpc);

                    if (sceneOpts.Length < 2)
                        continue;

                    gmcm.AddTextOption(this.ModManifest,
                        getValue: () => this.config.SceneTypePreference.TryGetValue(capturedNpc, out string? v) ? v : "FM",
                        setValue: v  => this.config.SceneTypePreference[capturedNpc] = v.TrimStart('⚠', ' '),
                        name:     () => $"{capturedNpc} scene",
                        tooltip:  () => $"Scene type preference for {capturedNpc}. ⚠ = incompatible with current gender settings.",
                        allowedValues: sceneOpts);
                }
            }
        }

        this.gmcmRegistered = true;
    }

    /// <summary>Registers an image-carousel portrait option via GMCMOptions.</summary>
    private void RegisterImagePortraitOption(
        IGenericModConfigMenuApi gmcm,
        IGMCMOptionsApi gmcmOpts,
        string npcName,
        PortraitSource[] sources)
    {
        if (sources.Length == 0)
            return;

        string capturedNpc     = npcName;
        PortraitSource[] capturedSources = sources;

        gmcmOpts.AddImageOption(
            this.ModManifest,
            getValue: () =>
            {
                if (!this.config.PreferredPortraitModByNpc.TryGetValue(capturedNpc, out string? modId))
                    return 0u;
                int idx = Array.FindIndex(capturedSources, s =>
                    string.Equals(s.ModUniqueId, modId, StringComparison.OrdinalIgnoreCase));
                return idx >= 0 ? (uint)idx : 0u;
            },
            setValue: idx =>
            {
                if (idx < capturedSources.Length)
                    this.config.PreferredPortraitModByNpc[capturedNpc] = capturedSources[idx].ModUniqueId;
            },
            name:         () => capturedNpc,
            getMaxValue:  () => (uint)(capturedSources.Length - 1),
            getTexture:   idx =>
            {
                if (idx >= (uint)capturedSources.Length)
                    return null;
                string filePath = capturedSources[(int)idx].FilePath;
                // Return cached thumbnail, or load+cache it
                if (!this.portraitThumbnailCache.TryGetValue(filePath, out Texture2D? tex))
                {
                    tex = this.TryLoadTexture(filePath);
                    this.portraitThumbnailCache[filePath] = tex;
                }
                return tex;
            },
            getLabel:     idx => idx < (uint)capturedSources.Length
                ? capturedSources[(int)idx].ModUniqueId
                : string.Empty,
            maxImageHeight: 128,
            maxImageWidth:  128,
            tooltip:      () => $"Portrait preview for {capturedNpc}. Scroll to choose the source mod.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Texture2D LoadTexture(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        return Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
    }

    private Texture2D? TryLoadTexture(string filePath)
    {
        try { return this.LoadTexture(filePath); }
        catch { return null; }
    }

    private string? GetAdultFamily(string modUniqueId)
    {
        return this.activeAdultMods
            .FirstOrDefault(m => string.Equals(m.Info.Manifest.UniqueID, modUniqueId, StringComparison.OrdinalIgnoreCase))
            ?.Definition.FamilyName;
    }

    private int GetModPriority(string modUniqueId)
    {
        ResolvedAdultMod? resolved = this.activeAdultMods.FirstOrDefault(m =>
            string.Equals(m.Info.Manifest.UniqueID, modUniqueId, StringComparison.OrdinalIgnoreCase));
        if (resolved is null)
            return 0;
        if (this.config.AdultModPriorityOverrides.TryGetValue(resolved.Definition.FamilyName, out int overridePriority))
            return overridePriority;
        return resolved.Definition.Priority;
    }

    /// <summary>
    /// Extracts the SMAPI asset name for a file relative to its mod root, given a folder key
    /// (e.g. <c>"Portraits"</c> or <c>"Characters"</c>).
    /// Returns <c>null</c> when the file does not live under that folder.
    /// </summary>
    private static string? TryGetAssetName(string modDirectory, string filePath, string folderKey)
    {
        try
        {
            string relative   = Path.GetRelativePath(modDirectory, filePath).Replace('\\', '/');
            string searchKey  = folderKey + "/";
            int    idx        = relative.IndexOf(searchKey, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;

            string remainder  = relative[(idx + searchKey.Length)..];
            // Only use the first path segment (ignore sub-folders)
            int nextSlash     = remainder.IndexOf('/');
            string assetFile  = nextSlash >= 0 ? remainder[..nextSlash] : remainder;
            string assetName  = Path.GetFileNameWithoutExtension(assetFile);

            return string.IsNullOrWhiteSpace(assetName) ? null : $"{folderKey}/{assetName}";
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeAssetName(string baseName) => baseName.Replace('\\', '/');

    private static string GetNpcNameFromAsset(string assetName)
    {
        int slash = assetName.LastIndexOf('/');
        return slash >= 0 && slash < assetName.Length - 1 ? assetName[(slash + 1)..] : assetName;
    }

    private static bool IsAnimatedPortrait(string filePath, string uniqueId, string modName)
    {
        string normalized = filePath.Replace('\\', '/');
        return normalized.Contains("animated", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(filePath).Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || uniqueId.Contains("animated", StringComparison.OrdinalIgnoreCase)
            || modName.Contains("animated", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
