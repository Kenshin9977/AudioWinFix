using AudioWinFix.Core.Audio;
using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Tests.Audio;

public class FavoritesOptionsTests
{
    private static readonly FavoritesOptions Options = new()
    {
        Render = [new() { DeviceId = "out" }],
        RenderCommunications = [new() { DeviceId = "out-comm" }],
        Capture = [new() { DeviceId = "mic" }],
        CaptureCommunications = [new() { DeviceId = "mic-comm" }],
    };

    [Theory]
    [InlineData(DataFlow.Render, Role.Console, "out")]
    [InlineData(DataFlow.Render, Role.Multimedia, "out")]
    [InlineData(DataFlow.Render, Role.Communications, "out-comm")]
    [InlineData(DataFlow.Capture, Role.Console, "mic")]
    [InlineData(DataFlow.Capture, Role.Multimedia, "mic")]
    [InlineData(DataFlow.Capture, Role.Communications, "mic-comm")]
    public void EachRole_ReadsItsOwnList(DataFlow flow, Role role, string expected)
        => Assert.Equal(expected, Assert.Single(Options.For(flow, role)).DeviceId);
}
