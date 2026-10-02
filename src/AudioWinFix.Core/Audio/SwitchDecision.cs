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

    /// <summary>
    /// Where a <see cref="SwitchAction.Revert"/> should go, or null to leave
    /// Windows' choice in place. The pin wins while it is plugged in. Once it is
    /// gone the favorites act as a whitelist: a favorite Windows picked is kept,
    /// anything else is replaced by the first favorite that is plugged in.
    /// Without favorites, or with none plugged in, there is nothing better to
    /// offer than what Windows chose.
    /// </summary>
    public static string? RevertTarget(
        string newId,
        string pinnedId,
        IReadOnlyList<string> favorites,
        Func<string, bool> isActive)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(isActive);

        if (isActive(pinnedId)) return pinnedId;
        if (favorites.Contains(newId, StringComparer.OrdinalIgnoreCase)) return null;
        return favorites.FirstOrDefault(isActive);
    }
}
