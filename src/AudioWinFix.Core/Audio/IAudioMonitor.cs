namespace AudioWinFix.Core.Audio;

public interface IAudioMonitor : IDisposable
{
    /// <summary>When true, the monitor observes but never reverts (user pause).</summary>
    bool Paused { get; set; }

    void Start();

    /// <summary>Human-readable snapshot of the current pins, for the tray tooltip.</summary>
    string DescribePins();
}
