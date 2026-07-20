# AudioWinFix — Design

**Date:** 2026-07-20
**Status:** Approved (design), pending implementation plan
**Repo:** `Kenshin9977/AudioWinFix` (to be created)

## Problem

Windows keeps re-picking the default playback / recording / communication
device every time a new audio device is plugged in (headset, monitor over
HDMI, USB DAC, dock…). The user wants their chosen devices to *stick* through
those plug/unplug events — **without** losing the ability to switch manually
through the normal Windows Sound panel.

Existing tools don't fit:
- **SoundSwitch** (OSS) — its "Force profile" trigger overrides *manual*
  switches too; it can't tell "user chose this" from "Windows auto-switched".
- **SoundVolumeView** (NirSoft, closed) + Task Scheduler — CLI reset, same
  problem, and closed-source.
- Registry `Role` DWORD hack / disabling devices — fragile, all-or-nothing.

The differentiator: **suppress automatic switching, respect manual switching.**

## Goal

A background Windows tray app that pins the default **playback**, **recording**,
and **communication** endpoints, reverts only Windows-initiated auto-switches,
and silently adopts any manual switch as the new pin.

## Non-goals

- No per-app audio routing, volume control, or hotkey switching (SoundSwitch's
  territory). This tool does one thing.
- No macOS/Linux. Windows-only.
- No cloud, no telemetry, no account.

## Behavior model — heuristic (approved)

Windows exposes default-device changes and device add/remove events through
`IMMNotificationClient`. The core loop:

1. On startup, seed the pins from the current defaults (or load persisted pins).
2. Record `lastDeviceEventUtc` on every `DeviceAdded` / `DeviceRemoved` /
   `DeviceStateChanged`.
3. On `DefaultDeviceChanged(flow, role, newId)`:
   - **newId == pinned[(flow, role)]** → ignore. This is the echo of our own
     revert; it's what prevents a feedback loop.
   - **now − lastDeviceEventUtc < threshold** → treat as an *auto-switch* caused
     by the hardware event → **revert** to the pinned device via
     `IPolicyConfig.SetDefaultEndpoint(pinnedId, role)`.
   - **otherwise** → treat as a *manual* switch → update
     `pinned[(flow, role)] = newId` and persist. The tool stays invisible.

Pins are keyed by `(DataFlow, Role)`. Windows fires `DefaultDeviceChanged`
per role, so the three user-facing "defaults" map to the roles we track:
- **Default device** → `eConsole` + `eMultimedia`
- **Default communication device** → `eCommunications`
- for both `eRender` (playback) and `eCapture` (recording).

### Configurable threshold

The auto-vs-manual window (default **3000 ms**) is user-configurable via
settings (see Config). Rationale: plug-to-auto-switch latency varies by machine
and device; a fixed value is a guess.

## Architecture

**Reuse the `Kenshin9977/Discord-Overlay` scaffold wholesale.** It is the same
shape (WinForms tray app on the .NET generic host) by the same author, and it
already carries every cross-cutting concern this project needs. We keep its
structure and replace only the domain layer.

```
AudioWinFix/
├─ global.json                     # SDK 10.0.x pin — reuse as-is
├─ Directory.Build.props           # reuse
├─ Directory.Packages.props        # reuse + add NAudio
├─ dotnet-tools.json               # vpk tool — reuse
├─ AudioWinFix.slnx
├─ AudioWinFix.ico                 # new icon
├─ build/                          # publish.ps1 + sign-remote.sh + vps/ — reuse, retarget names
├─ .github/
│  ├─ actions/sign-windows/        # reuse verbatim
│  └─ workflows/{ci,release,codeql,sign-health-check}.yml   # reuse, retarget artifact names
├─ docs/SIGNING.md                 # reuse
├─ src/
│  ├─ AudioWinFix.App/             # tray + hosting — adapt from DiscordOverlay.App
│  │  ├─ Program.cs                # generic host bootstrap — reuse pattern
│  │  ├─ Hosting/TrayApplicationContext.cs   # NotifyIcon + menu — rewrite menu
│  │  ├─ Hosting/AppHostedService.cs         # starts AudioMonitor — adapt
│  │  ├─ Hosting/AutoStartManager.cs         # HKCU Run key — reuse (rename value)
│  │  ├─ Hosting/UiDispatcher.cs             # reuse
│  │  ├─ Settings/SettingsForm.cs            # threshold + language — rewrite
│  │  ├─ Resources/Strings.cs                # EN + FR — reuse pattern, new keys
│  │  └─ appsettings.json
│  └─ AudioWinFix.Core/            # NEW — the only genuinely new code
│     ├─ Audio/AudioMonitor.cs               # IMMNotificationClient + heuristic loop
│     ├─ Audio/IAudioMonitor.cs
│     ├─ Audio/PolicyConfig.cs               # IPolicyConfig COM interop (set default)
│     ├─ Audio/EndpointKey.cs                # (DataFlow, Role) record
│     ├─ Audio/PinStore.cs                   # persist pins to %APPDATA%\AudioWinFix\pins.json
│     └─ AppConfig.cs                        # ThresholdMs, Language
└─ tests/
   └─ AudioWinFix.Core.Tests/               # heuristic logic tests
```

### Components (the new code)

- **`AudioMonitor`** — owns an `MMDeviceEnumerator` (NAudio) and registers an
  `IMMNotificationClient`. Implements the heuristic in Behavior model. Thread
  note: NAudio fires callbacks on a COM thread; the pin dictionary is guarded by
  a lock, and `IPolicyConfig` calls are made directly from the callback (COM
  apartment-safe). It exposes the current pins for the tray tooltip and a
  `Pause`/`Resume` gate.

- **`PolicyConfig`** — the undocumented `IPolicyConfig` COM interop. One public
  method: `SetDefault(string deviceId, Role role)`. This is the piece no
  maintained NuGet exposes; ~40 lines of interop. Set per role so
  `eConsole`/`eMultimedia`/`eCommunications` are handled independently.

- **`PinStore`** — `Dictionary<EndpointKey, string deviceId>` seeded from
  current defaults at launch, persisted as JSON in
  `%APPDATA%\AudioWinFix\pins.json` (survives restart/crash). `System.Text.Json`.

- **`AppConfig`** — `ThresholdMs` (default 3000), `Language` (`auto`|`en`|`fr`).

### Dependency choice

- **NAudio** (maintained, modern .NET) for `MMDeviceEnumerator` +
  `IMMNotificationClient` — the fiddly notification plumbing.
- **`IPolicyConfig`** hand-rolled interop for *setting* the default (no
  maintained NuGet does this cleanly; the alternative, AudioSwitcher.AudioApi,
  is unmaintained since ~2019 and Reactive-heavy — dependency-rot risk on .NET
  10).
- Everything else (Serilog, Microsoft.Extensions.Hosting, Velopack) comes with
  the scaffold.

## Data flow

```
hardware plug/unplug
        │  (NAudio callback, COM thread)
        ▼
IMMNotificationClient.OnDeviceAdded/Removed/StateChanged  ──► record lastDeviceEventUtc
IMMNotificationClient.OnDefaultDeviceChanged(flow, role, newId)
        │
        ▼
   AudioMonitor.Decide()
        ├─ newId == pin            → ignore (revert echo)
        ├─ within threshold        → PolicyConfig.SetDefault(pin, role)   (revert)
        └─ outside threshold       → PinStore.Update(key, newId) + persist (adopt)
```

## Config & UI

Tray `NotifyIcon` context menu:
- **Pause / Resume** — gate the monitor (audio switches freely while paused).
- **Settings…** — small form: threshold slider/number (ms) + language dropdown
  (Auto / English / Français).
- **Start with Windows** — toggles the HKCU `...\Run` key (no elevation).
- **Quit**.

Tooltip shows the currently pinned devices.

Because the model auto-adopts manual switches, there is **no "re-pin" action** —
switching in the Windows Sound panel already updates the pin. That's the whole
point of the heuristic model.

## i18n

Reuse Discord-Overlay's `Strings.cs` pattern: a static dictionary per language
(EN, FR), selected from `CultureInfo.CurrentUICulture` with English fallback.
The Settings language dropdown overrides the culture at startup. All user-facing
strings (menu, settings, tooltips, notifications) go through `Strings`.

## Signing & distribution

Reuse the scaffold's pipeline unchanged in shape:
- **Velopack** (`vpk`) packs the self-contained single-file exe into
  `Setup.exe` + full/delta `.nupkg` + portable zip. Velopack is the installer
  **and** the auto-updater — no Inno Setup / MSIX needed.
- **Code signing** via the reusable `.github/actions/sign-windows` action +
  `build/sign-remote.sh`, which delegates per-file signing to a Certum
  SimplySign cloud cert over SSH on a VPS (`Kenshin9977/ssign`). Driven by
  `release.yml` on `v*` tags, with a hard post-pack gate that asks Windows
  (`Get-AuthenticodeSignature`) to confirm every artifact is **Valid and
  timestamped** before publishing.
- Secrets to configure on the new repo: `SIGN_SSH_HOST`, `SIGN_SSH_KEY`,
  `SIGN_SSH_PORT`, `SIGN_SSH_KNOWN_HOSTS`. If unset, CI produces unsigned
  binaries (stays green) — signing is additive.

Retargeting from the template = rename `Discord-Overlay`/`DiscordOverlay` →
`AudioWinFix`, swap the icon, and update artifact filenames in `release.yml` and
`publish.ps1`.

## Edge cases & ceilings (marked with `ponytail:` comments in code)

- **Manual switch inside the threshold window** — a manual switch made within
  `ThresholdMs` of plugging a device is misread as auto and reverted. Rare; the
  window is user-tunable, and lowering it shrinks the risk.
- **Pinned device unplugged** — when the pinned device is gone there's nothing
  to revert to, so the tool lets Windows' fallback stand; it re-pins as soon as
  the device returns (its ID is stable) and the user switches back, or stays on
  the fallback if the user accepts it (adopted as a manual switch).
- **Revert echo / feedback loop** — prevented by the `newId == pin → ignore`
  guard; setting the default back to the pin produces a `DefaultDeviceChanged`
  whose `newId` equals the pin, which is ignored.
- **Re-entrancy** — pin dictionary guarded by a lock; the COM callback thread is
  the only writer besides the settings form (marshalled via `UiDispatcher`).

## Testing

The heuristic is the only non-trivial logic, so it gets the tests:
`AudioWinFix.Core.Tests` drives `AudioMonitor.Decide()` (extracted as a pure
function over `(newId, pinnedId, msSinceDeviceEvent, thresholdMs)` returning
`Ignore | Revert | Adopt`) through xUnit:
- newId == pin → `Ignore`
- within window, newId != pin → `Revert`
- outside window, newId != pin → `Adopt`
- boundary at exactly `thresholdMs`

COM/NAudio layers are thin wrappers, not unit-tested (integration is manual on
the Windows box). `PinStore` round-trip (write→read JSON) gets one test.

## Open items for the plan

- Exact NAudio API surface for endpoint notifications and current-default query.
- `IPolicyConfig` GUID/vtable layout (Win10/11 variant) — verify on the target
  box.
- Whether to ship a first-run notification ("AudioWinFix is watching your audio
  devices") — small, deferrable.
