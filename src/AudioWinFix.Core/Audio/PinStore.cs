using System.Text.Json;
using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Audio;

public sealed class PinStore
{
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudioWinFix", "pins.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string path;
    private readonly Dictionary<EndpointKey, string> pins = new();
    private readonly Lock gate = new();

    public PinStore(string? path = null) => this.path = path ?? DefaultFilePath;

    // JSON can't key a dictionary on a struct cleanly, so persist a flat row list.
    private sealed record Row(DataFlow Flow, Role Role, string DeviceId);

    public string? Get(EndpointKey key)
    {
        lock (gate) { return pins.TryGetValue(key, out var id) ? id : null; }
    }

    public void Set(EndpointKey key, string deviceId)
    {
        lock (gate) { pins[key] = deviceId; }
    }

    public void Load()
    {
        lock (gate)
        {
            pins.Clear();
            if (!File.Exists(path)) return;
            try
            {
                var rows = JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(path), JsonOptions);
                foreach (var r in rows ?? []) pins[new EndpointKey(r.Flow, r.Role)] = r.DeviceId;
            }
            catch (JsonException) { /* corrupt file → start empty */ }
        }
    }

    public void Save()
    {
        List<Row> rows;
        lock (gate) { rows = pins.Select(kv => new Row(kv.Key.Flow, kv.Key.Role, kv.Value)).ToList(); }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(rows, JsonOptions));
        File.Move(tmp, path, overwrite: true); // atomic-ish replace
    }
}
