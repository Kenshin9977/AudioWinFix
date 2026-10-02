using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Audio;

public sealed class AudioMonitor : IAudioMonitor
{
    // The roles that map to Windows' two user-facing defaults, for both flows.
    private static readonly Role[] Roles = [Role.Console, Role.Multimedia, Role.Communications];
    private static readonly DataFlow[] Flows = [DataFlow.Render, DataFlow.Capture];

    private readonly MMDeviceEnumerator enumerator = new();
    private readonly PinStore store;
    private readonly IOptionsMonitor<AudioMonitorOptions> options;
    private readonly IOptionsMonitor<FavoritesOptions> favorites;
    private readonly ILogger<AudioMonitor> logger;
    private readonly Lock gate = new();

    private long lastDeviceEventTick;
    private MMDeviceNotificationClient? notifications;

    // Default-device changes are handled one at a time, in arrival order, off
    // the audio worker thread (see OnDefaultDeviceChanged).
    private Task pending = Task.CompletedTask;

    public AudioMonitor(
        PinStore store,
        IOptionsMonitor<AudioMonitorOptions> options,
        IOptionsMonitor<FavoritesOptions> favorites,
        ILogger<AudioMonitor> logger)
    {
        this.store = store;
        this.options = options;
        this.favorites = favorites;
        this.logger = logger;
    }

    public bool Paused { get; set; }

    public void Start()
    {
        store.Load();
        SeedMissingPinsFromCurrentDefaults();
        store.Save();

        // Raised synchronously on the Windows audio worker thread (false), not
        // marshalled to whatever SynchronizationContext Start happens to run
        // on: the plug timestamp has to be taken when the event happens, not
        // when a busy UI thread gets round to it.
        notifications = enumerator.CreateNotificationClient(useSynchronizationContext: false);
        notifications.DeviceAdded += (_, _) => MarkDeviceEvent();
        notifications.DeviceRemoved += (_, _) => MarkDeviceEvent();
        notifications.DeviceStateChanged += (_, _) => MarkDeviceEvent();
        notifications.DefaultDeviceChanged += (_, e) => OnDefaultDeviceChanged(e.Flow, e.Role, e.DeviceId);
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

    // --- Notification handlers (audio worker thread: must not block) ---

    private void MarkDeviceEvent()
    {
        lock (gate) { lastDeviceEventTick = Environment.TickCount64; }
    }

    private void OnDefaultDeviceChanged(DataFlow flow, Role role, string? newDefaultId)
    {
        if (string.IsNullOrEmpty(newDefaultId)) return; // no default (everything unplugged)

        // Measured now, on the notifying thread; everything else waits for the
        // queue. The worker thread holds an audio-stack lock while it notifies,
        // so setting the default from here is exactly the call-back it forbids.
        double sinceEvent;
        lock (gate)
        {
            sinceEvent = Environment.TickCount64 - lastDeviceEventTick;
            pending = pending.ContinueWith(
                _ => HandleDefaultChange(flow, role, newDefaultId, sinceEvent),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }

    private void HandleDefaultChange(DataFlow flow, Role role, string newDefaultId, double sinceEvent)
    {
        try
        {
            ApplyDecision(flow, role, newDefaultId, sinceEvent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Handling default change {Flow}/{Role} -> {Id} failed", flow, role, newDefaultId);
        }
    }

    private void ApplyDecision(DataFlow flow, Role role, string newDefaultId, double sinceEvent)
    {
        var key = new EndpointKey(flow, role);
        var pinned = store.Get(key);

        var action = SwitchDecision.Decide(newDefaultId, pinned, sinceEvent, options.CurrentValue.ThresholdMs);
        switch (action)
        {
            case SwitchAction.Ignore:
                return;
            case SwitchAction.Revert when !Paused:
                var favoriteIds = favorites.CurrentValue.For(flow).Select(f => f.DeviceId).ToList();
                var target = SwitchDecision.RevertTarget(newDefaultId, pinned!, favoriteIds, IsActive);
                if (target is null)
                {
                    // Keep the pin rather than adopting: this was not the user's choice.
                    logger.LogInformation("Pinned {Flow}/{Role} {Pin} is gone; keeping {Id}", flow, role, pinned, newDefaultId);
                    return;
                }
                logger.LogInformation("Reverting {Flow}/{Role} → {Kind} {Target}",
                    flow, role, target == pinned ? "pinned" : "favorite", target);
                var hr = PolicyConfig.SetDefault(target, role);
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

    private bool IsActive(string id)
    {
        try
        {
            using var device = enumerator.GetDevice(id);
            return device.State == DeviceState.Active;
        }
        catch
        {
            return false; // unknown or removed endpoint
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
        notifications?.Dispose();
        enumerator.Dispose();
    }
}
