using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Audio;

/// <summary>
/// Holds each locked endpoint's master volume/mute at its target: any deviation
/// (a game, an app, anything) is restored. There is no way to tell a deliberate
/// change from an involuntary one for volume, so the lock is explicit — the
/// target only changes when the user re-locks via Settings.
///
/// No feedback loop: we only restore when the value differs from the target, and
/// our restore sets it *to* the target, so the echo notification matches and is
/// ignored (same trick as the default-device revert).
/// </summary>
public sealed class VolumeGuard(IOptionsMonitor<VolumeOptions> options, ILogger<VolumeGuard> logger) : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly Dictionary<string, Watched> watched = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock gate = new();
    private IDisposable? changeSubscription;

    private sealed class Watched
    {
        public required MMDevice Device { get; init; }
        public required AudioEndpointVolumeNotificationDelegate Handler { get; init; }
    }

    /// <summary>When true, observe but do not restore (shares the tray Pause).</summary>
    public bool Paused { get; set; }

    public void Start()
    {
        Reconcile();
        changeSubscription = options.OnChange(_ => Reconcile());
    }

    // Watch exactly the set of locked devices; re-run whenever the lock list changes.
    private void Reconcile()
    {
        lock (gate)
        {
            var desired = options.CurrentValue.Locks
                .Select(l => l.DeviceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var id in watched.Keys.Where(k => !desired.Contains(k)).ToList())
            {
                Unwatch(id);
            }

            foreach (var id in desired.Where(d => !watched.ContainsKey(d)))
            {
                try
                {
                    var dev = enumerator.GetDevice(id);
                    AudioEndpointVolumeNotificationDelegate handler =
                        data => Enforce(id, data.MasterVolume, data.Muted);
                    dev.AudioEndpointVolume.OnVolumeNotification += handler;
                    watched[id] = new Watched { Device = dev, Handler = handler };
                    var vol = dev.AudioEndpointVolume;
                    Enforce(id, vol.MasterVolumeLevelScalar, vol.Mute); // enforce immediately on lock
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Cannot watch volume for {Id} (device offline?)", id);
                }
            }
        }
    }

    private void Enforce(string id, double currentLevel, bool currentMuted)
    {
        if (Paused)
        {
            return;
        }

        var entry = options.CurrentValue.Locks
            .FirstOrDefault(l => string.Equals(l.DeviceId, id, StringComparison.OrdinalIgnoreCase));
        if (entry is null || !VolumeDecision.NeedsRestore(currentLevel, currentMuted, entry.Level, entry.Muted))
        {
            return;
        }

        try
        {
            MMDevice device;
            lock (gate)
            {
                if (!watched.TryGetValue(id, out var w)) return;
                device = w.Device;
            }
            device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(entry.Level, 0, 1);
            device.AudioEndpointVolume.Mute = entry.Muted;
            logger.LogInformation("Restored volume {Id} -> {Level:P0} mute={Mute}", id, entry.Level, entry.Muted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore volume {Id}", id);
        }
    }

    private void Unwatch(string id)
    {
        if (!watched.TryGetValue(id, out var w)) return;
        try
        {
            w.Device.AudioEndpointVolume.OnVolumeNotification -= w.Handler;
            w.Device.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error unwatching {Id}", id);
        }
        watched.Remove(id);
    }

    public void Dispose()
    {
        changeSubscription?.Dispose();
        lock (gate)
        {
            foreach (var id in watched.Keys.ToList())
            {
                Unwatch(id);
            }
        }
        enumerator.Dispose();
    }
}
