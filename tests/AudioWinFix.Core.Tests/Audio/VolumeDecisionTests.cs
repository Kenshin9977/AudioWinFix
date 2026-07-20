using AudioWinFix.Core.Audio;

namespace AudioWinFix.Core.Tests.Audio;

public class VolumeDecisionTests
{
    [Fact]
    public void LevelDrifted_NeedsRestore()
        => Assert.True(VolumeDecision.NeedsRestore(0.50, false, 0.80, false));

    [Fact]
    public void OnTarget_NoRestore()
        => Assert.False(VolumeDecision.NeedsRestore(0.80, false, 0.80, false));

    [Fact]
    public void MuteDiffers_NeedsRestore()
        => Assert.True(VolumeDecision.NeedsRestore(0.80, true, 0.80, false));

    [Fact]
    public void WithinEpsilon_NoRestore() // echo of our own restore (quantized value)
        => Assert.False(VolumeDecision.NeedsRestore(0.8003, false, 0.80, false));
}
