using System.Collections.Generic;

namespace StardewModCompatibilityFixer;

internal sealed class ModConfig
{
    public bool EnableAutoPortraitureSync { get; set; } = true;

    public bool EnablePortraitRandomization { get; set; }

    public bool PreferAnimatedPortraits { get; set; } = true;

    public Dictionary<string, string> PreferredPortraitModByNpc { get; set; } = new();
}
