using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace StardewModCompatibilityFixer;

/// <summary>
/// Optional API surface for <c>spacechase0.GMCMOptions</c>.
/// When that mod is installed, we use <see cref="AddImageOption"/> to display
/// an actual portrait thumbnail for each NPC portrait source choice.
/// If it is absent, we fall back to the standard text-dropdown in GMCM.
/// </summary>
public interface IGMCMOptionsApi
{
    /// <summary>
    /// Adds an image-carousel option.
    /// The player scrolls through indices 0 .. maxValue (inclusive) and sees
    /// the texture returned by <paramref name="getTexture"/> for each index.
    /// </summary>
    void AddImageOption(
        IManifest mod,
        Func<uint>           getValue,
        Action<uint>         setValue,
        Func<string>         name,
        Func<uint>           getMaxValue,
        Func<uint, Texture2D?> getTexture,
        Func<uint, string>?  getLabel          = null,
        int                  maxImageHeight    = 256,
        int                  maxImageWidth     = 256,
        Func<string>?        tooltip           = null,
        string?              fieldId           = null);
}
