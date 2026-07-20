using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Audio;

/// <summary>
/// On-demand audio queries and actions for the UI: list devices, set the
/// default / default-communication device (updating the pins so the monitor
/// adopts the choice), and read endpoint volume.
/// </summary>
public sealed class AudioController(PinStore store, ILogger<AudioController> logger) : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();

    public IReadOnlyList<AudioDeviceInfo> List(DataFlow flow)
    {
        var defaultId = TryDefaultId(flow, Role.Multimedia);
        var commId = TryDefaultId(flow, Role.Communications);
        var result = new List<AudioDeviceInfo>();
        foreach (var d in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            result.Add(new AudioDeviceInfo(d.ID, d.FriendlyName, flow, d.ID == defaultId, d.ID == commId));
        }
        return result;
    }

    /// <summary>
    /// Make <paramref name="id"/> the default (Console+Multimedia) or the
    /// default-communication (Communications) device for its flow, and pin it so
    /// the monitor treats this as an adopted manual choice.
    /// </summary>
    public void SetDefault(string id, DataFlow flow, bool communications)
    {
        if (communications)
        {
            SetRole(id, Role.Communications);
            store.Set(new EndpointKey(flow, Role.Communications), id);
        }
        else
        {
            SetRole(id, Role.Console);
            SetRole(id, Role.Multimedia);
            store.Set(new EndpointKey(flow, Role.Console), id);
            store.Set(new EndpointKey(flow, Role.Multimedia), id);
        }
        store.Save();
        logger.LogInformation("Set default {Flow} {Kind} -> {Id}", flow, communications ? "comm" : "default", id);
    }

    public double GetVolume(string id) => WithDevice(id, d => d.AudioEndpointVolume.MasterVolumeLevelScalar);

    public bool GetMute(string id) => WithDevice(id, d => d.AudioEndpointVolume.Mute);

    private T WithDevice<T>(string id, Func<MMDevice, T> read)
    {
        using var d = enumerator.GetDevice(id);
        return read(d);
    }

    private string? TryDefaultId(DataFlow flow, Role role)
        => enumerator.HasDefaultAudioEndpoint(flow, role)
            ? enumerator.GetDefaultAudioEndpoint(flow, role).ID
            : null;

    private static void SetRole(string id, Role role)
    {
        var hr = PolicyConfig.SetDefault(id, role);
        if (hr != 0)
        {
            throw new InvalidOperationException($"SetDefaultEndpoint({role}) failed, hr=0x{hr:X}");
        }
    }

    public void Dispose() => enumerator.Dispose();
}
