using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Audio;

/// <summary>
/// Favorite devices per tray device group, highest priority first: the only
/// devices an automatic switch may land on. "Default" lists cover the Console
/// and Multimedia roles, "Communications" lists the Communications role; an
/// empty list means no whitelist for that group. Persisted in settings.json
/// under "Favorites".
/// </summary>
public sealed class FavoritesOptions
{
    public List<FavoriteDevice> Render { get; set; } = new();

    public List<FavoriteDevice> RenderCommunications { get; set; } = new();

    public List<FavoriteDevice> Capture { get; set; } = new();

    public List<FavoriteDevice> CaptureCommunications { get; set; } = new();

    public List<FavoriteDevice> For(DataFlow flow, Role role) => (flow, role) switch
    {
        (DataFlow.Capture, Role.Communications) => CaptureCommunications,
        (DataFlow.Capture, _) => Capture,
        (_, Role.Communications) => RenderCommunications,
        _ => Render,
    };
}

public sealed class FavoriteDevice
{
    public string DeviceId { get; set; } = "";

    /// <summary>Friendly name, stored for display when the device is offline.</summary>
    public string DeviceName { get; set; } = "";
}
