# AudioWinFix Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** A Windows tray app that pins the default playback/recording/communication audio devices, reverting Windows-initiated auto-switches on device plug/unplug while silently adopting manual switches.

**Architecture:** WinForms tray app on the .NET generic host. `AudioWinFix.App` (tray + hosting) wraps `AudioWinFix.Core` (the new domain: a heuristic monitor over NAudio's `IMMNotificationClient`, an `IPolicyConfig` COM interop to set defaults, and JSON pin persistence). The entire host/tray/i18n/auto-start/Velopack/signing scaffold is lifted from `Kenshin9977/Discord-Overlay` and retargeted; the only genuinely new code is `AudioWinFix.Core/Audio/*`.

**Tech Stack:** .NET 10 (`net10.0-windows`), WinForms, NAudio (CoreAudio notifications + enumeration), hand-rolled `IPolicyConfig` COM interop, Serilog, Microsoft.Extensions.Hosting, Velopack (installer + updater), xUnit. Spec: `docs/superpowers/specs/2026-07-20-audiowinfix-design.md`.

---

## Execution environment (read first)

**Source of truth is this Mac.** Edit + commit here. **All builds and tests run on the Windows box** — `AudioWinFix.Core` references NAudio's WASAPI/CoreAudio types, which only build on Windows, so `dotnet build`/`dotnet test` cannot run on macOS.

Dev loop per task:
1. Edit + commit on the Mac.
2. Get the code onto Windows: `git pull` in `C:\Users\kensh\CodeProjects\AudioWinFix` (clone it there once, first task). Pushing to `origin` for this is fine **only after asking the user** — otherwise hand the pull to the user.
3. Build/test on Windows over SSH. Helper pattern (from repo CLAUDE.md context):
   ```bash
   KEY=~/.ssh/id_ed25519_usbscope_win; HOST=kensh@desktop-pef6pc8.tailb16ce5.ts.net
   ssh -o BatchMode=yes -i $KEY $HOST 'powershell -NoProfile -Command -' <<'PS1' 2>&1 | LC_ALL=C tr -d '\r'
   $env:Path="$env:USERPROFILE\.dotnet;"+$env:Path
   cd C:\Users\kensh\CodeProjects\AudioWinFix
   git pull --ff-only
   dotnet test -c Release
   PS1
   ```
   For complex scripts, write a `.ps1`, `scp` it, and run with `-File` (heredoc returns empty output on complex scripts — see CLAUDE.md).
4. `dotnet build AudioWinFix.slnx -c Release` — never pass `-r <RID>` to the solution (NETSDK1134).

**Commit rule for this repo:** no `Co-Authored-By` / Claude trailer in any commit (user requirement).

**Token-rename map** when copying any file from the `Discord-Overlay` template:
`Discord-Overlay`→`AudioWinFix`, `DiscordOverlay`→`AudioWinFix`, `DiscordOverlay.App`→`AudioWinFix.App`, `DiscordOverlay.Core`→`AudioWinFix.Core`, assembly `DiscordOverlay`→`AudioWinFix`.
Get the template with: `gh repo clone Kenshin9977/Discord-Overlay /tmp/dov -- --depth 1`.

---

## Phase A — Scaffold (retarget from template)

### Task A1: Clone template + copy build infrastructure

**Files (create in repo root, copied from the template, tokens renamed, Discord-specific bits stripped):**
- `global.json` — copy verbatim (SDK 10.0.x pin).
- `Directory.Build.props` — copy; set `<Product>AudioWinFix</Product>`, `<RepositoryUrl>https://github.com/Kenshin9977/AudioWinFix</RepositoryUrl>`, reset `<Version>`/`<AssemblyVersion>`/`<FileVersion>` to `0.1.0`.
- `Directory.Packages.props` — copy, then **strip** `OBSClient`, `System.Security.Cryptography.ProtectedData`, `Microsoft.Extensions.Http`; **add** `<PackageVersion Include="NAudio" Version="2.2.1" />`.
- `dotnet-tools.json` — copy verbatim (declares `vpk`).
- `.gitignore`, `.gitattributes`, `.vscode/settings.json` — copy verbatim.
- `.github/dependabot.yml` — copy verbatim.

**Step 1:** Clone template, copy the files, apply the rename map, make the `Directory.Packages.props` edits above.

**Step 2:** Sanity-check no stray `DiscordOverlay`/`OBS`/`Discord` tokens remain: `grep -ri 'discord\|obs\b' global.json Directory.*.props dotnet-tools.json` → expect no matches.

**Step 3: Commit.**
```bash
git add global.json Directory.Build.props Directory.Packages.props dotnet-tools.json .gitignore .gitattributes .vscode .github/dependabot.yml
git commit -m "chore: build infrastructure (SDK pin, central packages, tools)"
```

### Task A2: Solution + empty project skeletons

**Files:**
- Create `AudioWinFix.slnx` — copy `Discord-Overlay.slnx`, rename project paths to the three below.
- Create `src/AudioWinFix.Core/AudioWinFix.Core.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net10.0-windows</TargetFramework>
      <RootNamespace>AudioWinFix.Core</RootNamespace>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
      <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
      <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
      <PackageReference Include="Microsoft.Extensions.Options" />
      <PackageReference Include="NAudio" />
    </ItemGroup>
    <ItemGroup>
      <InternalsVisibleTo Include="AudioWinFix.Core.Tests" />
    </ItemGroup>
  </Project>
  ```
  > Note: `net10.0-windows` (not plain `net10.0`) because NAudio's CoreAudio API is Windows-only.
- Create `src/AudioWinFix.App/AudioWinFix.App.csproj` — copy `DiscordOverlay.App.csproj`, rename tokens, `<AssemblyName>AudioWinFix</AssemblyName>`, `<ApplicationIcon>..\..\AudioWinFix.ico</ApplicationIcon>`. Keep the Serilog + Velopack + Hosting package refs; it already references the Core project.
- Create `tests/AudioWinFix.Core.Tests/AudioWinFix.Core.Tests.csproj` — copy the template's test csproj, rename tokens, target `net10.0-windows`, keep `xunit` + `Microsoft.NET.Test.Sdk` + `coverlet.collector` + `xunit.runner.visualstudio`, project-reference `AudioWinFix.Core`.
- Create placeholder `AudioWinFix.ico` — copy `Discord-Overlay.ico` for now (real icon in Task D0).

**Step 1:** Create the four project files + slnx + placeholder icon.

**Step 2: Build on Windows** (empty projects must compile):
```
dotnet build AudioWinFix.slnx -c Release
```
Expected: `Build succeeded`.

**Step 3: Commit.**
```bash
git add AudioWinFix.slnx AudioWinFix.ico src tests
git commit -m "chore: solution and empty App/Core/Tests projects"
```

### Task A3: Copy host plumbing (no domain logic yet)

Copy these from the template, rename tokens, and **remove Discord/OBS references** (they'll fail to compile until the Core exists — that's fine, this task ends compiling only after the stubs in A4):
- `src/AudioWinFix.App/Hosting/UiDispatcher.cs` — copy verbatim (tokens only). No changes needed; it's domain-agnostic.
- `src/AudioWinFix.App/Hosting/AutoStartManager.cs` — copy; change `RegistryValueName` to `"AudioWinFix"`. Otherwise verbatim.
- `src/AudioWinFix.App/appsettings.json` — copy verbatim.
- `src/AudioWinFix.App/Hosting/AppUpdater.cs` — copy verbatim (Velopack update check; domain-agnostic). Keep as-is.

**Step 1:** Copy the four files with renames.

**Step 2: Commit** (build happens after A4 wires them):
```bash
git add src/AudioWinFix.App/Hosting/UiDispatcher.cs src/AudioWinFix.App/Hosting/AutoStartManager.cs src/AudioWinFix.App/Hosting/AppUpdater.cs src/AudioWinFix.App/appsettings.json
git commit -m "chore: copy domain-agnostic host plumbing (dispatcher, autostart, updater)"
```

---

## Phase B — Core domain logic (TDD)

### Task B1: `SwitchAction` + `SwitchDecision.Decide` — the heuristic (pure, fully tested)

This is the heart of the app and the one piece with real logic. Pure function, no NAudio types → straightforward TDD.

**Files:**
- Create: `src/AudioWinFix.Core/Audio/SwitchDecision.cs`
- Test: `tests/AudioWinFix.Core.Tests/Audio/SwitchDecisionTests.cs`

**Step 1: Write the failing tests.**
```csharp
using AudioWinFix.Core.Audio;
using Xunit;

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
```

**Step 2: Run — expect FAIL** (`SwitchDecision` not defined). On Windows: `dotnet test tests/AudioWinFix.Core.Tests -c Release`.

**Step 3: Implement.**
```csharp
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
```

**Step 4: Run — expect PASS** (5 passed).

**Step 5: Commit.**
```bash
git add src/AudioWinFix.Core/Audio/SwitchDecision.cs tests/AudioWinFix.Core.Tests/Audio/SwitchDecisionTests.cs
git commit -m "feat(core): auto-vs-manual switch heuristic"
```

### Task B2: `EndpointKey` — (flow, role) pin key

**Files:**
- Create: `src/AudioWinFix.Core/Audio/EndpointKey.cs`
- Test: `tests/AudioWinFix.Core.Tests/Audio/EndpointKeyTests.cs`

We use NAudio's `DataFlow` and `Role` enums directly to avoid a mapping layer.

**Step 1: Failing test** (records give value equality + dictionary-key behavior; one test locks it in):
```csharp
using AudioWinFix.Core.Audio;
using NAudio.CoreAudioApi;
using Xunit;

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
```

**Step 2: Run — expect FAIL.**

**Step 3: Implement.**
```csharp
using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Audio;

/// <summary>A default-device slot: one (data-flow, role) pair, e.g. (Render, Communications).</summary>
public readonly record struct EndpointKey(DataFlow Flow, Role Role);
```

**Step 4: Run — expect PASS. Step 5: Commit** (`feat(core): endpoint key`).

### Task B3: `PinStore` — persist pins to `%APPDATA%\AudioWinFix\pins.json`

**Files:**
- Create: `src/AudioWinFix.Core/Audio/PinStore.cs`
- Test: `tests/AudioWinFix.Core.Tests/Audio/PinStoreTests.cs`

**Step 1: Failing test** (round-trip via an explicit path; the default-path property is exercised in the app, not the test):
```csharp
using AudioWinFix.Core.Audio;
using NAudio.CoreAudioApi;
using Xunit;

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
```

**Step 2: Run — expect FAIL.**

**Step 3: Implement.** JSON can't key a dictionary on a struct cleanly, so persist a flat list of `{Flow, Role, DeviceId}` rows.
```csharp
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
```

**Step 4: Run — expect PASS (3 tests). Step 5: Commit** (`feat(core): pin persistence`).

### Task B4: `PolicyConfig` — `IPolicyConfig` COM interop (set default endpoint)

No maintained NuGet exposes this. It's a fixed COM vtable — the method **order matters** even for the unused slots. Integration-verified on Windows, not unit-tested.

**Files:**
- Create: `src/AudioWinFix.Core/Audio/PolicyConfig.cs`

**Step 1: Implement.**
```csharp
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Audio;

/// <summary>
/// Sets the Windows default audio endpoint for a given role via the undocumented
/// IPolicyConfig COM interface (same one the Sound control panel uses).
/// ponytail: the full vtable is declared even though only SetDefaultEndpoint is
/// called — COM dispatches by slot, so the order and count must match exactly.
/// Verified on Win10/Win11 x64. If SetDefaultEndpoint no-ops on a future build,
/// the IPolicyConfigVista variant (different slot order) is the fallback.
/// </summary>
public static class PolicyConfig
{
    public static int SetDefault(string deviceId, Role role)
    {
        var client = (IPolicyConfig)new CPolicyConfigClient();
        try { return client.SetDefaultEndpoint(deviceId, role); }
        finally { Marshal.ReleaseComObject(client); }
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private sealed class CPolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat(string id, bool @default, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string id, bool @default, IntPtr def, IntPtr min);
        [PreserveSig] int SetProcessingPeriod(string id, IntPtr period);
        [PreserveSig] int GetShareMode(string id, IntPtr mode);
        [PreserveSig] int SetShareMode(string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string id, bool store, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(string id, bool store, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint(string id, Role role); // slot 10
        [PreserveSig] int SetEndpointVisibility(string id, bool visible);
    }
}
```

**Step 2: Manual verification on Windows** — this is not unit-testable (mutates OS state). Deferred to Task C-end smoke test. For now just confirm it compiles: `dotnet build src/AudioWinFix.Core -c Release`.

**Step 3: Commit** (`feat(core): IPolicyConfig interop to set default endpoint`).

### Task B5: `AudioMonitor` — wire NAudio notifications to the heuristic

Implements NAudio's `IMMNotificationClient`, tracks the last device-arrival tick, seeds pins from current defaults, and applies `SwitchDecision`. Integration-verified on Windows (COM callbacks aren't unit-tested); the decision logic it calls is already covered by B1.

**Files:**
- Create: `src/AudioWinFix.Core/Audio/IAudioMonitor.cs`
- Create: `src/AudioWinFix.Core/Audio/AudioMonitor.cs`
- Create: `src/AudioWinFix.Core/Audio/AudioMonitorOptions.cs`

**Step 1: Implement options + interface.**
```csharp
namespace AudioWinFix.Core.Audio;

public sealed class AudioMonitorOptions
{
    /// <summary>Auto-vs-manual window in ms. A default change within this of a
    /// plug/unplug is treated as a Windows auto-switch and reverted.</summary>
    public int ThresholdMs { get; set; } = 3000;
}
```
```csharp
namespace AudioWinFix.Core.Audio;

public interface IAudioMonitor : IDisposable
{
    /// <summary>When true, the monitor observes but never reverts (user pause).</summary>
    bool Paused { get; set; }
    void Start();
    /// <summary>Human-readable snapshot of the current pins, for the tray tooltip.</summary>
    string DescribePins();
}
```

**Step 2: Implement the monitor.**
```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioWinFix.Core.Audio;

public sealed class AudioMonitor : IAudioMonitor, IMMNotificationClient
{
    // The roles that map to Windows' two user-facing defaults, for both flows.
    private static readonly Role[] Roles = [Role.Console, Role.Multimedia, Role.Communications];
    private static readonly DataFlow[] Flows = [DataFlow.Render, DataFlow.Capture];

    private readonly MMDeviceEnumerator enumerator = new();
    private readonly PinStore store;
    private readonly IOptionsMonitor<AudioMonitorOptions> options;
    private readonly ILogger<AudioMonitor> logger;
    private readonly Lock gate = new();

    private long lastDeviceEventTick;
    private bool registered;

    public AudioMonitor(PinStore store, IOptionsMonitor<AudioMonitorOptions> options, ILogger<AudioMonitor> logger)
    {
        this.store = store;
        this.options = options;
        this.logger = logger;
    }

    public bool Paused { get; set; }

    public void Start()
    {
        store.Load();
        SeedMissingPinsFromCurrentDefaults();
        store.Save();
        enumerator.RegisterEndpointNotificationCallback(this);
        registered = true;
        logger.LogInformation("AudioMonitor started. Pins: {Pins}", DescribePins());
    }

    private void SeedMissingPinsFromCurrentDefaults()
    {
        foreach (var flow in Flows)
        foreach (var role in Roles)
        {
            var key = new EndpointKey(flow, role);
            if (store.Get(key) is not null) continue;
            if (!enumerator.HasDefaultAudioEndpoint(flow, role)) continue;
            store.Set(key, enumerator.GetDefaultAudioEndpoint(flow, role).ID);
        }
    }

    // --- IMMNotificationClient (fires on a COM thread) ---

    public void OnDeviceAdded(string id) => MarkDeviceEvent();
    public void OnDeviceRemoved(string id) => MarkDeviceEvent();
    public void OnDeviceStateChanged(string id, DeviceState newState) => MarkDeviceEvent();
    public void OnPropertyValueChanged(string id, PropertyKey key) { }

    private void MarkDeviceEvent()
    {
        lock (gate) { lastDeviceEventTick = Environment.TickCount64; }
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string newDefaultId)
    {
        if (string.IsNullOrEmpty(newDefaultId)) return; // no default (all unplugged)
        var key = new EndpointKey(flow, role);

        double sinceEvent;
        string? pinned;
        lock (gate)
        {
            sinceEvent = Environment.TickCount64 - lastDeviceEventTick;
            pinned = store.Get(key);
        }

        var action = SwitchDecision.Decide(newDefaultId, pinned, sinceEvent, options.CurrentValue.ThresholdMs);
        switch (action)
        {
            case SwitchAction.Ignore:
                return;
            case SwitchAction.Revert when !Paused:
                logger.LogInformation("Reverting {Flow}/{Role} → pinned {Pin}", flow, role, pinned);
                var hr = PolicyConfig.SetDefault(pinned!, role);
                if (hr != 0) logger.LogWarning("SetDefault failed (hr=0x{Hr:X})", hr);
                return;
            case SwitchAction.Revert: // Paused → fall through and adopt so pins track reality
            case SwitchAction.Adopt:
                store.Set(key, newDefaultId);
                store.Save();
                logger.LogInformation("Adopted {Flow}/{Role} → {Id}", flow, role, newDefaultId);
                return;
        }
    }

    public string DescribePins()
    {
        var parts = new List<string>();
        foreach (var flow in Flows)
        foreach (var role in Roles)
        {
            if (store.Get(new EndpointKey(flow, role)) is not { } id) continue;
            var name = SafeName(id);
            if (name is not null) parts.Add($"{flow}/{role}: {name}");
        }
        return parts.Count == 0 ? "no pins" : string.Join("\n", parts);
    }

    private string? SafeName(string id)
    {
        try { return enumerator.GetDevice(id)?.FriendlyName; }
        catch { return null; } // device gone
    }

    public void Dispose()
    {
        if (registered) enumerator.UnregisterEndpointNotificationCallback(this);
        enumerator.Dispose();
    }
}
```

**Step 3: Build on Windows** — `dotnet build AudioWinFix.slnx -c Release`. Expected: succeeds.

**Step 4: Register in DI.** Create `src/AudioWinFix.Core/Audio/AudioServiceCollectionExtensions.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;

namespace AudioWinFix.Core.Audio;

public static class AudioServiceCollectionExtensions
{
    public static IServiceCollection AddAudioMonitor(this IServiceCollection services)
    {
        services.AddSingleton<PinStore>(_ => new PinStore());
        services.AddSingleton<IAudioMonitor, AudioMonitor>();
        return services;
    }
}
```

**Step 5: Commit** (`feat(core): audio monitor wiring NAudio notifications to heuristic`).

---

## Phase C — App wiring (tray, settings, i18n)

### Task C1: `Strings.cs` — EN + FR

**Files:**
- Create: `src/AudioWinFix.App/Resources/Strings.cs`

Reuse the template's exact pattern (static `En`/`Fr` dictionaries keyed by string, `CultureInfo.CurrentUICulture` selects, English fallback; a `Get(key)` indexer + named static props). Keys needed (define both languages):

| Key | EN | FR |
|---|---|---|
| `AppName` | `AudioWinFix` | `AudioWinFix` |
| `TrayTooltipPrefix` | `AudioWinFix — pinned devices:` | `AudioWinFix — appareils épinglés :` |
| `TrayPaused` | `AudioWinFix — paused` | `AudioWinFix — en pause` |
| `MenuPause` | `Pause` | `Pause` |
| `MenuResume` | `Resume` | `Reprendre` |
| `MenuSettings` | `Settings…` | `Paramètres…` |
| `MenuStartWithWindows` | `Start with Windows` | `Démarrer avec Windows` |
| `MenuOpenLogFolder` | `Open log folder` | `Ouvrir le dossier des journaux` |
| `MenuQuit` | `Quit` | `Quitter` |
| `SettingsTitle` | `AudioWinFix — Settings` | `AudioWinFix — Paramètres` |
| `SettingsThresholdLabel` | `Auto-switch grace window (ms):` | `Fenêtre anti-bascule auto (ms) :` |
| `SettingsThresholdHelp` | `A default-device change within this delay of plugging a device is treated as automatic and reverted. Manual changes made later are kept.` | `Un changement d'appareil par défaut survenant dans ce délai après un branchement est considéré comme automatique et annulé. Les changements manuels ultérieurs sont conservés.` |
| `SettingsLanguageLabel` | `Language:` | `Langue :` |
| `SettingsLanguageAuto` | `Auto (system)` | `Auto (système)` |
| `SettingsSave` | `Save` | `Enregistrer` |
| `SettingsCancel` | `Cancel` | `Annuler` |

**Step 1:** Implement following the template's `Strings.cs` shape.
**Step 2: Build** on Windows. **Step 3: Commit** (`feat(app): EN/FR localized strings`).

### Task C2: `AppConfig` (threshold + language) + store

**Files:**
- Create: `src/AudioWinFix.Core/AppConfig.cs` (mirrors the template's `AppConfigStore`, atomic `SaveAsync`/`LoadAsync`, path `%LOCALAPPDATA%\AudioWinFix\settings.json`):
  ```csharp
  public sealed class AppConfig
  {
      public AudioMonitorOptions Audio { get; set; } = new();
      /// <summary>"auto" | "en" | "fr".</summary>
      public string Language { get; set; } = "auto";
  }
  ```
  Reuse the template `AppConfigStore` load/save verbatim (rename folder to `AudioWinFix`, type to `AppConfig`).

**Step 1:** Implement. **Step 2: Build.** **Step 3: Commit** (`feat(core): app config (threshold + language)`).

### Task C3: `SettingsForm` — threshold + language

**Files:**
- Create: `src/AudioWinFix.App/Settings/SettingsForm.cs`

A minimal single-panel form (code-only WinForms, like the template): a numeric up/down for `ThresholdMs` (range 250–15000, step 250), a read-only help label (`SettingsThresholdHelp`), a language combo (Auto/English/Français), Save/Cancel. On Save: write `AppConfig` via `AppConfigStore.SaveAsync`; `settings.json` is `reloadOnChange`, so `IOptionsMonitor<AudioMonitorOptions>` picks up the new threshold live (no restart). Language change requires restart (`Application.Restart()` after a MessageBox, mirroring the template).

**Step 1:** Implement. **Step 2: Build.** **Step 3: Commit** (`feat(app): settings form`).

### Task C4: `TrayApplicationContext` — the menu

**Files:**
- Create: `src/AudioWinFix.App/Hosting/TrayApplicationContext.cs`

Adapt the template's tray context. Strip the OBS/Discord status items. Menu:
1. Pause/Resume toggle → flips `IAudioMonitor.Paused`, updates the item text between `MenuPause`/`MenuResume`.
2. Settings… → `OnSettingsClicked` opens `SettingsForm`.
3. Start with Windows → checkable, reflects/sets `AutoStartManager`.
4. Open log folder → opens `%LOCALAPPDATA%\AudioWinFix\logs`.
5. Quit → `lifetime.StopApplication()`.

A 1s `Timer` refreshes the tooltip from `IAudioMonitor.DescribePins()` (truncated to 127 chars — the `NotifyIcon.Text` limit; keep the template's `TooltipMaxLength` guard), or `TrayPaused` when paused. Constructor takes `IAudioMonitor`, `AutoStartManager`, `IUiDispatcher`, `IHostApplicationLifetime`, `ILogger`, `AppUpdater` (optional — keep "Check for updates" if you copy it).

**Step 1:** Implement. **Step 2: Build.** **Step 3: Commit** (`feat(app): tray menu`).

### Task C5: `AppHostedService` + `Program.cs` — bootstrap

**Files:**
- Create: `src/AudioWinFix.App/Hosting/AppHostedService.cs` — replaces the template's Discord/OBS orchestration with: `audioMonitor.Start()` in `ExecuteAsync`. No first-run wizard needed (the app self-seeds pins). Keep it tiny:
  ```csharp
  public sealed class AppHostedService(IAudioMonitor monitor, ILogger<AppHostedService> logger) : BackgroundService
  {
      protected override Task ExecuteAsync(CancellationToken stoppingToken)
      {
          try { monitor.Start(); }
          catch (Exception ex) { logger.LogError(ex, "AudioMonitor failed to start"); }
          return Task.CompletedTask;
      }
  }
  ```
- Create: `src/AudioWinFix.App/Program.cs` — adapt the template. Keep: `VelopackApp.Build().Run()`, `ApplicationConfiguration.Initialize()`, the WinForms sync-context install, Serilog file logging (folder `AudioWinFix`), the `settings.json` config layering. Replace the Discord/OBS service registrations with:
  ```csharp
  builder.Services.Configure<AudioMonitorOptions>(builder.Configuration.GetSection("Audio"));
  builder.Services.AddAudioMonitor();
  builder.Services.AddSingleton<IUiDispatcher>(_ => new UiDispatcher(uiSyncContext));
  builder.Services.AddSingleton<AutoStartManager>();
  builder.Services.AddSingleton<AppUpdater>();          // keep if updater copied
  builder.Services.AddHostedService<AppHostedService>();
  builder.Services.AddSingleton<TrayApplicationContext>();
  ```
  Before building the host, apply the language override: read `AppConfig.Language`; if `en`/`fr`, set `CultureInfo.CurrentUICulture`/`DefaultThreadCurrentUICulture` accordingly.

**Step 1:** Implement both. **Step 2: Build** `dotnet build AudioWinFix.slnx -c Release` — whole solution compiles. **Step 3: Run all tests** `dotnet test -c Release` — expect the B1–B3 tests green. **Step 4: Commit** (`feat(app): host bootstrap and audio monitor startup`).

### Task C6: End-to-end smoke test on the Windows box (manual)

Not automated — validates the COM interop and the heuristic against the real OS.

**Step 1:** `dotnet run --project src/AudioWinFix.App -c Release` on the Windows box (or run the published exe). Confirm the tray icon appears and the tooltip lists current defaults.
**Step 2:** In Windows Sound settings, set a specific speaker as default → confirm the tooltip updates (Adopt path).
**Step 3:** Plug/unplug a second audio device (or toggle one in Device Manager) → confirm the default snaps back to the pinned one within ~1s (Revert path). Check the log at `%LOCALAPPDATA%\AudioWinFix\logs`.
**Step 4:** Manually switch default in Sound settings (no plug event) → confirm it sticks and becomes the new pin (Adopt path).
**Step 5:** Pause via tray → confirm switches are no longer reverted. Resume → confirm reverting resumes.
**Step 6:** If any step fails, this is the debugging surface — most likely `IPolicyConfig` (Task B4) needs the Vista variant, or the threshold is too tight. Record findings; no commit unless code changes.

---

## Phase D — Build, package, sign, release

### Task D0: Real icon

**Files:** replace `AudioWinFix.ico` with a real multi-resolution icon (16/32/48/256). Ask the user, or generate a simple one (e.g. a pin over a speaker). **Commit** (`chore: app icon`).

### Task D1: `publish.ps1` + Velopack pack

**Files:**
- Create: `build/publish.ps1` — copy the template's, rename `PackId`/paths to `AudioWinFix`, icon path, project path `src/AudioWinFix.App/AudioWinFix.App.csproj`.

**Step 1:** Copy + retarget.
**Step 2: Build the installer on Windows:**
```
dotnet tool restore
.\build\publish.ps1 -Pack -PackVersion 0.1.0
```
Expected: `Releases\AudioWinFix-win-Setup.exe` + `AudioWinFix-0.1.0-full.nupkg` + portable zip produced.
**Step 3:** Install `Setup.exe` on the Windows box, confirm it launches to tray and auto-start works. **Step 4: Commit** (`build: velopack packaging script`).

### Task D2: Signing action + `sign-remote.sh` + VPS scripts

**Files:**
- Copy verbatim (no logic change, only artifact names): `.github/actions/sign-windows/action.yml`, `build/sign-remote.sh`, `build/vps/*`, `docs/SIGNING.md`. These delegate per-file signing to the Certum SimplySign cloud cert over SSH (`Kenshin9977/ssign`).

**Step 1:** Copy + rename tokens. **Step 2: Commit** (`build: remote code-signing (ssign on VPS)`).

### Task D3: CI + release workflows

**Files:**
- `.github/workflows/ci.yml` — copy, retarget: build + `dotnet test` on `windows-latest`, `global-json-file: global.json`.
- `.github/workflows/release.yml` — copy, retarget artifact filenames (`AudioWinFix-win-Setup.exe`, `AudioWinFix-<ver>-full.nupkg`, portable zip, `RELEASES`, `releases.win.json`, `assets.win.json`). Keep the **post-pack Authenticode gate** (`Get-AuthenticodeSignature` must be Valid + timestamped before publishing) verbatim — it's the whole point.
- `.github/workflows/codeql.yml`, `sign-health-check.yml` — copy, retarget.

**Step 1:** Copy + retarget. **Step 2:** Push a branch, confirm `ci.yml` goes green on GitHub Actions (build + tests on Windows). **Step 3: Configure secrets** (user does this — my env is blocked from server/secret actions): `SIGN_SSH_HOST`, `SIGN_SSH_KEY`, `SIGN_SSH_PORT`, `SIGN_SSH_KNOWN_HOSTS`. **Step 4: Commit** (`ci: build/test + signed release workflows`).

### Task D4: README (EN + FR)

**Files:** `README.md` + `README.fr.md` — what it does, the auto-vs-manual model, the tunable threshold, install from `Setup.exe`, "still switch manually in Windows Sound settings — the app adopts it." **Commit** (`docs: readme (en + fr)`).

### Task D5: First tagged release

**Step 1:** Bump `Directory.Build.props` version to `0.1.0`. **Step 2 (user-gated):** `git tag v0.1.0 && git push origin v0.1.0` triggers `release.yml` → signed `Setup.exe` on the GitHub Release. Confirm the Authenticode gate passed and the artifact is Valid + timestamped. **Commit** the version bump (`release: v0.1.0`).

---

## Notes / deferred (YAGNI until asked)

- **Console + Multimedia coupling:** we revert each role independently as its event fires; if a future Windows build changes them non-atomically and one drifts, revert both together in `OnDefaultDeviceChanged`. Not doing it now — mark with a `ponytail:` comment.
- **First-run notification** ("AudioWinFix is now watching your audio devices") — small, add if the user wants discoverability.
- **`IPolicyConfigVista` fallback** — only if B4 no-ops on some machine (see Task C6 step 6).
