namespace AudioWinFix.Core.Audio;

/// <summary>Volume locks, one entry per physical device the user chose to pin. Persisted in settings.json under "Volume".</summary>
public sealed class VolumeOptions
{
    public List<VolumeLockEntry> Locks { get; set; } = new();
}

public sealed class VolumeLockEntry
{
    public string DeviceId { get; set; } = "";

    /// <summary>Friendly name, stored for display when the device is offline.</summary>
    public string DeviceName { get; set; } = "";

    /// <summary>Target master volume, 0.0–1.0.</summary>
    public double Level { get; set; }

    public bool Muted { get; set; }
}
