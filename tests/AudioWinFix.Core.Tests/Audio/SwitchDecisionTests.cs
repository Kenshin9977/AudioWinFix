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

    // RevertTarget: where a Revert goes once the pin may be unplugged.

    private static Func<string, bool> Active(params string[] ids) => ids.Contains;

    [Fact]
    public void PinPluggedIn_RevertsToPin() // favorites never override the pin
        => Assert.Equal("X",
            SwitchDecision.RevertTarget(newId: "Y", pinnedId: "X", favorites: ["F"], isActive: Active("X", "Y", "F")));

    [Fact]
    public void PinGone_NoFavorites_KeepsWindowsChoice()
        => Assert.Null(
            SwitchDecision.RevertTarget(newId: "Y", pinnedId: "X", favorites: [], isActive: Active("Y")));

    [Fact]
    public void PinGone_WindowsPickedAFavorite_KeepsIt()
        => Assert.Null(
            SwitchDecision.RevertTarget(newId: "G", pinnedId: "X", favorites: ["F", "G"], isActive: Active("F", "G")));

    [Fact]
    public void PinGone_WindowsPickedOutsideList_GoesToFirstPluggedFavorite()
        => Assert.Equal("G",
            SwitchDecision.RevertTarget(newId: "Y", pinnedId: "X", favorites: ["F", "G", "H"], isActive: Active("Y", "G", "H")));

    [Fact]
    public void PinGone_NoFavoritePluggedIn_KeepsWindowsChoice()
        => Assert.Null(
            SwitchDecision.RevertTarget(newId: "Y", pinnedId: "X", favorites: ["F"], isActive: Active("Y")));

    [Fact]
    public void FavoriteMatch_IgnoresCase() // endpoint IDs are compared case-insensitively everywhere else
        => Assert.Null(
            SwitchDecision.RevertTarget(newId: "{0.0.0}.abc", pinnedId: "X", favorites: ["{0.0.0}.ABC"], isActive: Active()));
}
