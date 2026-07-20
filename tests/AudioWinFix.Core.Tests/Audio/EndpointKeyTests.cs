using AudioWinFix.Core.Audio;
using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Tests.Audio;

public class EndpointKeyTests
{
    [Fact]
    public void EqualByValue_UsableAsDictionaryKey()
    {
        var d = new Dictionary<EndpointKey, string>
        {
            [new EndpointKey(DataFlow.Render, Role.Communications)] = "hs",
        };
        Assert.Equal("hs", d[new EndpointKey(DataFlow.Render, Role.Communications)]);
        Assert.False(d.ContainsKey(new EndpointKey(DataFlow.Capture, Role.Communications)));
    }
}
