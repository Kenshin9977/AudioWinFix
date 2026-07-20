using AudioWinFix.Core.Audio;
using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Tests.Audio;

public class PinStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsPins()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pins-{Guid.NewGuid():N}.json");
        try
        {
            var store = new PinStore(path);
            store.Set(new EndpointKey(DataFlow.Render, Role.Console), "spk");
            store.Set(new EndpointKey(DataFlow.Capture, Role.Communications), "mic");
            store.Save();

            var reloaded = new PinStore(path);
            reloaded.Load();
            Assert.Equal("spk", reloaded.Get(new EndpointKey(DataFlow.Render, Role.Console)));
            Assert.Equal("mic", reloaded.Get(new EndpointKey(DataFlow.Capture, Role.Communications)));
            Assert.Null(reloaded.Get(new EndpointKey(DataFlow.Render, Role.Multimedia)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MissingFile_IsEmpty()
    {
        var store = new PinStore(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
        store.Load();
        Assert.Null(store.Get(new EndpointKey(DataFlow.Render, Role.Console)));
    }
}
