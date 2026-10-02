using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Audio;

/// <summary>
/// Favorite devices per flow, highest priority first: the only devices an
/// automatic switch may land on. Persisted in settings.json under "Favorites".
/// </summary>
public sealed class FavoritesOptions
{
    public List<FavoriteDevice> Render { get; set; } = new();

    public List<FavoriteDevice> Capture { get; set; } = new();

    public List<FavoriteDevice> For(DataFlow flow) => flow == DataFlow.Capture ? Capture : Render;
}

public sealed class FavoriteDevice
{
    public string DeviceId { get; set; } = "";

    /// <summary>Friendly name, stored for display when the device is offline.</summary>
    public string DeviceName { get; set; } = "";
}
