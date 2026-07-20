namespace AudioWinFix.Core.Audio;

public enum SwitchAction
{
    /// <summary>New default already equals the pin — do nothing (prevents revert feedback loops).</summary>
    Ignore,
    /// <summary>Change happened right after a device plug/unplug — restore the pinned device.</summary>
    Revert,
    /// <summary>Change happened on its own — treat as a manual switch and make it the new pin.</summary>
    Adopt,
}

public static class SwitchDecision
{
    /// <summary>
    /// Classify a default-device change. The whole app is this decision:
    /// echo → Ignore, plug-triggered → Revert, user-driven → Adopt.
    /// </summary>
    public static SwitchAction Decide(string? newId, string? pinnedId, double msSinceDeviceEvent, double thresholdMs)
    {
        if (pinnedId is not null && string.Equals(newId, pinnedId, StringComparison.OrdinalIgnoreCase))
        {
            return SwitchAction.Ignore;
        }

        // ponytail: window is [0, threshold). A manual switch made within thresholdMs
        // of a plug event is misread as auto and reverted. Rare; threshold is user-tunable.
        return pinnedId is not null && msSinceDeviceEvent < thresholdMs
            ? SwitchAction.Revert
            : SwitchAction.Adopt;
    }
}
