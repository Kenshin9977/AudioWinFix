namespace AudioWinFix.Core.Audio;

public sealed class AudioMonitorOptions
{
    /// <summary>Auto-vs-manual window in ms. A default change within this of a
    /// plug/unplug is treated as a Windows auto-switch and reverted.</summary>
    public int ThresholdMs { get; set; } = 3000;
}
