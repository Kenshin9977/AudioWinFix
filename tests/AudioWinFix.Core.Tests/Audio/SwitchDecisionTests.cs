using AudioWinFix.Core.Audio;

namespace AudioWinFix.Core.Tests.Audio;

public class SwitchDecisionTests
{
    [Fact]
    public void SameAsPin_IsIgnored() // our own revert echo
        => Assert.Equal(SwitchAction.Ignore,
            SwitchDecision.Decide(newId: "X", pinnedId: "X", msSinceDeviceEvent: 10, thresholdMs: 3000));

    [Fact]
    public void DifferentWithinWindow_IsRevert() // Windows auto-switched on a plug event
        => Assert.Equal(SwitchAction.Revert,
            SwitchDecision.Decide(newId: "Y", pinnedId: "X", msSinceDeviceEvent: 500, thresholdMs: 3000));

    [Fact]
    public void DifferentOutsideWindow_IsAdopt() // user switched manually
        => Assert.Equal(SwitchAction.Adopt,
            SwitchDecision.Decide(newId: "Y", pinnedId: "X", msSinceDeviceEvent: 8000, thresholdMs: 3000));

    [Fact]
    public void AtExactlyThreshold_IsAdopt() // boundary: window is [0, threshold)
        => Assert.Equal(SwitchAction.Adopt,
            SwitchDecision.Decide(newId: "Y", pinnedId: "X", msSinceDeviceEvent: 3000, thresholdMs: 3000));

    [Fact]
    public void NoPinYet_IsAdopt() // nothing pinned for this role → adopt whatever is default
        => Assert.Equal(SwitchAction.Adopt,
            SwitchDecision.Decide(newId: "Y", pinnedId: null, msSinceDeviceEvent: 10, thresholdMs: 3000));
}
