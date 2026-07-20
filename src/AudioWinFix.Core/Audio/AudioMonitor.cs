using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioWinFix.Core.Audio;

public sealed class AudioMonitor : IAudioMonitor, IMMNotificationClient
{
    // The roles that map to Windows' two user-facing defaults, for both flows.
    private static readonly Role[] Roles = [Role.Console, Role.Multimedia, Role.Communications];
    private static readonly DataFlow[] Flows = [DataFlow.Render, DataFlow.Capture];

    private readonly MMDeviceEnumerator enumerator = new();
    private readonly PinStore store;
    private readonly IOptionsMonitor<AudioMonitorOptions> options;
    private readonly ILogger<AudioMonitor> logger;
    private readonly Lock gate = new();

    private long lastDeviceEventTick;
    private bool registered;

    public AudioMonitor(PinStore store, IOptionsMonitor<AudioMonitorOptions> options, ILogger<AudioMonitor> logger)
    {
        this.store = store;
        this.options = options;
        this.logger = logger;
    }

    public bool Paused { get; set; }

    public void Start()
    {
        store.Load();
        SeedMissingPinsFromCurrentDefaults();
        store.Save();
        enumerator.RegisterEndpointNotificationCallback(this);
        registered = true;
        logger.LogInformation("AudioMonitor started. Pins:\n{Pins}", DescribePins());
    }

    private void SeedMissingPinsFromCurrentDefaults()
    {
        foreach (var flow in Flows)
        foreach (var role in Roles)
        {
            var key = new EndpointKey(flow, role);
            if (store.Get(key) is not null) continue;
            if (!enumerator.HasDefaultAudioEndpoint(flow, role)) continue;
            store.Set(key, enumerator.GetDefaultAudioEndpoint(flow, role).ID);
        }
    }

    // --- IMMNotificationClient (fires on a COM thread) ---

    public void OnDeviceAdded(string id) => MarkDeviceEvent();

    public void OnDeviceRemoved(string id) => MarkDeviceEvent();

    public void OnDeviceStateChanged(string id, DeviceState newState) => MarkDeviceEvent();

    public void OnPropertyValueChanged(string id, PropertyKey key) { }

    private void MarkDeviceEvent()
    {
        lock (gate) { lastDeviceEventTick = Environment.TickCount64; }
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string newDefaultId)
    {
        if (string.IsNullOrEmpty(newDefaultId)) return; // no default (everything unplugged)
        var key = new EndpointKey(flow, role);

        double sinceEvent;
        string? pinned;
        lock (gate)
        {
            sinceEvent = Environment.TickCount64 - lastDeviceEventTick;
            pinned = store.Get(key);
        }

        var action = SwitchDecision.Decide(newDefaultId, pinned, sinceEvent, options.CurrentValue.ThresholdMs);
        switch (action)
        {
            case SwitchAction.Ignore:
                return;
            case SwitchAction.Revert when !Paused:
                logger.LogInformation("Reverting {Flow}/{Role} → pinned {Pin}", flow, role, pinned);
                var hr = PolicyConfig.SetDefault(pinned!, role);
                if (hr != 0) logger.LogWarning("SetDefault failed (hr=0x{Hr:X})", hr);
                return;
            case SwitchAction.Revert: // Paused → fall through and adopt so pins track reality
            case SwitchAction.Adopt:
                store.Set(key, newDefaultId);
                store.Save();
                logger.LogInformation("Adopted {Flow}/{Role} → {Id}", flow, role, newDefaultId);
                return;
        }
    }

    public string DescribePins()
    {
        var parts = new List<string>();
        foreach (var flow in Flows)
        foreach (var role in Roles)
        {
            if (store.Get(new EndpointKey(flow, role)) is not { } id) continue;
            var name = SafeName(id);
            if (name is not null) parts.Add($"{flow}/{role}: {name}");
        }
        return parts.Count == 0 ? "no pins" : string.Join("\n", parts);
    }

    private string? SafeName(string id)
    {
        try { return enumerator.GetDevice(id)?.FriendlyName; }
        catch { return null; } // device gone
    }

    public void Dispose()
    {
        if (registered) enumerator.UnregisterEndpointNotificationCallback(this);
        enumerator.Dispose();
    }
}
