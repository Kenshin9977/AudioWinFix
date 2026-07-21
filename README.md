# AudioWinFix

[Français](README.fr.md)

A tiny Windows tray app that **pins your default audio devices** — playback,
recording, and communication — so Windows stops hijacking them every time you
plug in a headset, monitor, dock, or USB DAC. You can still switch devices
manually whenever you want; AudioWinFix only fights the *automatic* switches.

## Why

Windows loves to promote a freshly-connected device to "default" on its own.
Existing fixes either can't tell an automatic switch from one you made on
purpose (so they revert *everything*), or they're closed-source CLI hacks bolted
to Task Scheduler. AudioWinFix does exactly one thing, and does it invisibly.

## How it works

AudioWinFix watches Windows' default-device changes and device plug/unplug
events. When the default changes:

- **Right after a device was plugged/unplugged** (within a short, tunable grace
  window) → it's treated as an automatic switch and **reverted** to your pinned
  device.
- **On its own, with no recent plug event** → it's treated as a **manual**
  switch and silently **adopted** as the new pin.

So switching in the normal Windows Sound settings just works — that becomes your
new pinned device. Only Windows' unsolicited switches get undone.

It tracks all three of Windows' default roles (Console, Multimedia,
Communications) for both playback and recording.

## Install

1. Download `AudioWinFix-win-Setup.exe` from the
   [latest release](https://github.com/Kenshin9977/AudioWinFix/releases/latest).
2. Run it. It installs to `%LocalAppData%\AudioWinFix` (no admin needed) and
   starts to the system tray. Updates are delivered automatically (Velopack).

Binaries are Authenticode-signed (Certum, timestamped).

## Screenshots

Tray menu, and the default-device picker — set your default output / microphone
(and their communication variants) straight from the tray:

![Tray menu](assets/tray-menu.png) &nbsp; ![Default-device picker](assets/devices-menu.png)

Settings — the auto-switch grace window, the language, and per-device volume locks:

![Settings and volume locks](assets/settings.png)

## Use

AudioWinFix has no window — it lives in the tray. Right-click the tray icon:

- **Pause / Resume** — stop reverting temporarily (switches and volumes float
  freely while paused; pins still track whatever you land on).
- **Default devices** — set the default / default-communication device for
  output and microphone straight from the tray, without digging through the
  Windows menus. Your choice is adopted as the new pin.
- **Settings…** — the **grace window** (ms), the **language** (Auto / English /
  Français), and **volume locks** (see below).
- **Start with Windows** — per-user auto-start (no admin).
- **Check for updates**, **Open log folder**, **Quit**.

The tooltip lists the currently pinned devices.

### Volume locks

Windows (and some games — Black Ops 3 is notorious for resetting the mic level)
love to change device volume on their own. In **Settings → Volume locks**, tick
a device to freeze its current volume and mute; anything that changes it is
reverted. Unlike device switching, volume **can't** be auto-classified as
deliberate vs involuntary — there's no signal to tell a game's change from
yours — so the lock is explicit: to change a locked level, untick it, adjust in
Windows, tick it again, and save. Pausing (tray) releases all locks
temporarily.

### The grace window

Default **3000 ms**. A default-device change within this delay of a plug/unplug
counts as automatic and is reverted; a change made later is kept. Lower it if
your machine auto-switches fast and you want manual switches right after
plugging something to stick; raise it if auto-switches slip through.

## Files

- Pins: `%AppData%\AudioWinFix\pins.json`
- Settings: `%LocalAppData%\AudioWinFix\settings.json` (written by the Settings
  dialog)
- Logs: `%LocalAppData%\AudioWinFix\logs\` (daily rolling)

## Build from source

```bash
git clone https://github.com/Kenshin9977/AudioWinFix
cd AudioWinFix
dotnet build AudioWinFix.slnx -c Release
dotnet test AudioWinFix.slnx -c Release
# Self-contained single-file installer:
pwsh build/publish.ps1 -Pack -PackVersion 0.1.0
```

Requires the .NET 10 SDK (Windows). `AudioWinFix.Core` targets
`net10.0-windows` because it uses the Windows CoreAudio API, so it builds and
tests on Windows only.

## Layout

```
src/
  AudioWinFix.App/    WinForms tray app (entry, tray UI, settings, hosting)
  AudioWinFix.Core/   UI-free library
    Audio/            AudioMonitor, the switch heuristic, IPolicyConfig interop,
                      pin store
tests/
  AudioWinFix.Core.Tests/   xUnit tests (heuristic + pin persistence)
```

## Tech

- **NAudio** — CoreAudio device enumeration and default-change notifications.
- **IPolicyConfig** (COM interop) — sets the default endpoint per role, the same
  interface the Sound control panel uses.
- **Velopack** — installer + auto-update. **Serilog** — logging.
