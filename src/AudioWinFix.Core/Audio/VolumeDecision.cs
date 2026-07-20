namespace AudioWinFix.Core.Audio;

public static class VolumeDecision
{
    // Volume steps on Windows are coarser than this, and the target is always a
    // value we read back from the device (a realizable step), so re-applying it
    // echoes an equal value — no restore loop.
    public const double Epsilon = 0.005;

    /// <summary>True if the current level/mute has drifted from the locked target and must be restored.</summary>
    public static bool NeedsRestore(double currentLevel, bool currentMuted, double targetLevel, bool targetMuted)
        => currentMuted != targetMuted || Math.Abs(currentLevel - targetLevel) > Epsilon;
}
