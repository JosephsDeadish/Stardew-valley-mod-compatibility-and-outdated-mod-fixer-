using StardewModdingAPI;
using StardewValley;

namespace StardewModCompatibilityFixer;

/// <summary>
/// Watches the game's event state every tick and maintains <see cref="ActiveSceneFamilyName"/>:
/// the adult-mod family whose scene is currently playing, or <c>null</c> outside a known scene.
/// </summary>
internal sealed class SceneContextTracker
{
    private readonly Dictionary<string, string> eventIdToFamily;
    private readonly IMonitor monitor;
    private bool wasEventUpLastTick;

    /// <summary>
    /// The family name of the adult mod currently running a scene,
    /// or <c>null</c> when no known adult scene is active.
    /// </summary>
    public string? ActiveSceneFamilyName { get; private set; }

    public SceneContextTracker(Dictionary<string, string> eventIdToFamily, IMonitor monitor)
    {
        this.eventIdToFamily = eventIdToFamily;
        this.monitor = monitor;
    }

    /// <summary>Must be called once per game tick (e.g. from <c>GameLoop.UpdateTicked</c>).</summary>
    public void Update()
    {
        bool isEventUp = Game1.eventUp && Game1.CurrentEvent is not null;

        if (isEventUp && !this.wasEventUpLastTick)
            this.OnEventStarted();
        else if (!isEventUp && this.wasEventUpLastTick)
            this.OnEventEnded();

        this.wasEventUpLastTick = isEventUp;
    }

    // -------------------------------------------------------------------------

    private void OnEventStarted()
    {
        // Game1.CurrentEvent.id is a string in SDV 1.6+
        string? eventId = Game1.CurrentEvent?.id?.ToString();

        if (eventId is not null && this.eventIdToFamily.TryGetValue(eventId, out string? family))
        {
            this.ActiveSceneFamilyName = family;
            this.monitor.Log(
                $"[CompatFixer] Adult scene started — mod family: {family} (event {eventId}).",
                LogLevel.Debug);
        }
        else
        {
            this.ActiveSceneFamilyName = null;
        }
    }

    private void OnEventEnded()
    {
        if (this.ActiveSceneFamilyName is not null)
            this.monitor.Log("[CompatFixer] Adult scene ended — normal asset routing restored.", LogLevel.Debug);

        this.ActiveSceneFamilyName = null;
    }
}
