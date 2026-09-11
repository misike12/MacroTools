# Macro Deck 3 plugins

Three independent Windows plugins for Macro Deck 3, built from one solution:

- **Windows Media Control** (`com.misu.windows-media`) — SMTC media playback, CoreAudio volume, devices, artwork, Now Playing widget.
- **Screen Control** (`com.misu.screen-control`) — DDC monitor brightness/input plus window and virtual-desktop control.
- **Timers** (`com.misu.timers`) — countdowns, stopwatch and a countdown-finished event.

## Windows Media Control

Control Windows media playback from Macro Deck: transport keys, seeking, shuffle and repeat, system and per-app volume, output device switching, live track variables, album artwork, a Now Playing deck widget and a music player provider. Built on the Windows System Media Transport Controls (SMTC) and Core Audio, so it works with Spotify, browsers and any other SMTC-aware app.

### Actions (41)

All actions are Windows-only and accept an optional `App` filter (substring match on the app id, e.g. `Spotify`). Empty means the active session.

| Action | What it does |
| --- | --- |
| Play / Pause / Play-Pause | Transport control. Play/Pause also drives the button state (playing/paused/stopped). |
| Stop | Stops playback. A no-op success when nothing is playing. |
| Next / Previous | Skip tracks. |
| Fast forward / Rewind | Uses the app's own FF/RW when supported, otherwise skips ±10 s. |
| Seek forward / Seek backward | Jump by N seconds (default 10). |
| Seek to position | Jump to an absolute position in seconds. |
| Volume up / Volume down | System volume by N percent (default 5). |
| Set volume | System volume to an exact value (0–100). |
| Mute / Unmute / Mute-Unmute | System mute control. |
| Toggle shuffle / Set shuffle | Shuffle via SMTC (works where the app supports it). |
| Cycle repeat / Set repeat | Step through off, repeat all and repeat one. |
| Set app volume / Adjust app volume | One app's volume (e.g. Spotify) without touching system volume. |
| Mute app / Unmute app / Mute-or-unmute app | Per-app mute control. |
| Set output device / Next output device | Switch the default audio output, e.g. headphones to speakers. |
| Set input device / Next input device | Switch the default audio input (microphone). |
| Set mic volume / Mute / Unmute / Mute-or-unmute mic | Microphone level and mute control. |
| Focus app | Bring an app forward and mute everything else. |
| Sleep timer | Pause playback after N minutes (background, re-armable; negative cancels). |
| Fade out and pause / Fade in and play | Volume fade-out into pause, fade-in from silence (restores volume on failure). |
| Mute / Unmute / Mute-or-unmute system sounds | Windows system-sounds mute control. |

Shuffle and repeat go through real SMTC control calls and report honestly when an app refuses them. Every transport, seek, shuffle and repeat action accepts an optional `App` filter (substring match on the app id).

### Variables (40)

`title`, `artist`, `album`, `album_artist`, `genres`, `track_number`, `track_count`, `subtitle`, `source_app`, `playback_status` (`playing`/`paused`/`stopped`/`no-media`), `playback_type` (`music`/`video`/`image`/`unknown`), `playback_rate`, `is_playing`, `is_live`, `has_media`, `position_seconds`, `duration_seconds`, `position_text` / `duration_text` (`m:ss`), `progress_percent`, `volume_percent`, `is_muted`, `mic_volume_percent`, `is_mic_muted`, `mic_level_percent`, `system_level_percent`, `active_apps`, `shuffle_enabled`, `repeat_mode`, plus per-action support flags `can_play`, `can_pause`, `can_stop`, `can_next`, `can_previous`, `can_seek`, `can_shuffle`, `can_repeat`, the current `default_device` and `default_input_device` names and the cover-derived `cover_accent` hex color.

Writable: `volume_percent`, `is_muted`, `mic_volume_percent`, `is_mic_muted`, `position_seconds` and `progress_percent` (writing seeks; progress commits on release so slider drags don't stutter playback).

### App volume sliders

Besides the fixed variables above, the plugin offers a browsable **App volumes** catalog: every app currently in the Windows volume mixer appears as its own writable 0–100 variable. To put a Spotify slider on your deck, add a **Slider** widget, bind it to a variable, browse to App volumes and pick the app. Both `Spotify` and `Spotify.exe` spellings resolve to the same app, and the binding survives app restarts (it simply reads unavailable while the app has no audio session).

### Events (4)

`track-changed` (title/artist/album/app), `playback-changed` (status/isPlaying), `volume-changed` (volume/muted), `mute-changed` (muted).

### Extras

- **Configuration page** — open the integration to find Playback, Volume, Live updates, Events and Advanced sections: preferred app, default seek/volume steps, a maximum-volume safety clamp, poll intervals, per-event toggles, SMTC timeouts, artwork cache size and button cover art. Advanced fields live under their own section, including a reset-to-defaults switch.
- **Now Playing widget** — title/artist/album, live animated progress bar and prev/play/next buttons, with a configuration view (toggle album, progress and controls).
- **Music player provider** (`system` instance) with real album artwork for the native Music widget.
- **Album art on buttons** — the Play/Pause action supplies the current cover as its button icon, falling back to the configured icon.

## Screen Control

Control monitors and windows from the deck. Monitor control uses DDC/CI over `dxva2` (works on external monitors that expose brightness/input VCP codes; most laptop panels do not).

### Actions (14)

| Action | What it does |
| --- | --- |
| Set monitor brightness | Brightness to an exact value (0–100, default 80) on monitor N (default 1). |
| Adjust monitor brightness | Raise or lower brightness by a step (default +5); zero is a no-op success. |
| Set monitor input | Switch a monitor to HDMI 1/2, DisplayPort 1/2 or DVI over VCP `0x60`. |
| Next monitor input | Cycle a monitor to its next input (HDMI1 → HDMI2 → DP1 → DP2 → DVI). |
| Set monitor power | Turn a monitor on, or put it into standby/off over VCP `0xD6`. |
| Focus window | Bring a window to the front (substring match on title or process name). |
| Minimize / Maximize / Restore window | Window state; empty filter means the focused window. |
| Close window | Ask a window to close itself (`WM_CLOSE`). |
| Toggle always on top | Pin a window above all others, or unpin it again. |
| Snap window | Move a window into the left or right half of its monitor. |
| Next / Previous virtual desktop | Switch desktops via Win+Ctrl+Left/Right. |

### Variables (6)

`monitor_count` (how many monitors Windows sees), writable `primary_brightness` (writing it sets the primary monitor brightness), `primary_input` (the primary monitor's current input: `hdmi1`, `dp1`, `dvi`, ...), `focused_window_title`, `focused_window_process` and writable `focused_window_topmost` (writing it pins or unpins the focused window).

Besides the fixed variables, the plugin offers a browsable **Monitors** catalog: every monitor Windows sees appears as its own writable 0–100 brightness variable (`monitor-1-brightness`, `monitor-2-brightness`, ...). To put a slider for the second monitor on your deck, add a **Slider** widget, bind it to a variable, browse to Monitors and pick it. This is how non-primary monitors get sliders; the binding reads unavailable while that monitor is unplugged.

Brightness works on monitors without DDC/CI too (e.g. early-2000s panels that only speak VESA DDC 2B): when the backlight cannot be driven over VCP `0x10`, the plugin scales that display's GPU gamma ramp instead. Input and power switching genuinely need DDC and report honestly when the monitor has none; input cycling stays within the inputs the monitor advertises in its capabilities string.

## Timers

Countdowns and a stopwatch for automations. The countdown runs on a background timer: pausing keeps the time left, re-starting replaces the running one, and a zero-length start fires the event immediately.

### Actions (10)

Start countdown (hours/minutes/seconds, default 5 minutes, optional label) / Pause / Resume / Cancel, Pause-or-resume toggle, Adjust countdown (add or remove seconds; adjusting past zero finishes it at once), Start / Stop / Reset stopwatch, Start-or-stop toggle.

### Variables (8)

`countdown_remaining_seconds`, `countdown_text` (`m:ss` or `h:mm:ss`), `countdown_running`, `countdown_label`, `countdown_progress_percent`, `stopwatch_elapsed_seconds`, `stopwatch_text`, `stopwatch_running` (all refresh every second, all read-only).

### Events (1)

`countdown-finished` (label/seconds).

## Requirements

- Windows x64.
- Macro Deck `>=3.0.0-beta.3` (host).
- .NET 10 SDK (to build).

## Install

Build the artifact(s), then install from the Macro Deck desktop app (double-click the file or use install-from-file):

```bash
macrodeck-plugin build --source src/WindowsMediaControl --output ./artifacts
macrodeck-plugin build --source src/ScreenControl --output ./artifacts
macrodeck-plugin build --source src/Timers --output ./artifacts
```

The locally packed artifacts are unsigned, so Macro Deck asks for an explicit confirmation on install. (Store releases are signed server-side by the Creator Portal.)

Headless alternative over plain HTTP on the host machine (no auth on the loopback listener; the TLS port requires login). The port lives in `%TEMP%\macro-deck-host.port`:

```powershell
$port = Get-Content "$env:TEMP\macro-deck-host.port"
$body = @{ path = "C:\path\to\com.misu.windows-media-1.12.0.macroDeckPlugin"; force = $false; allowUnsigned = $true } | ConvertTo-Json -Compress
Invoke-WebRequest -Uri "http://127.0.0.1:$port/api/plugin-installation/install-path" -Method Post -ContentType "application/json" -Body $body
```

`GET /api/plugin-installation` on the same port lists what is installed. Uploading the file bytes instead works via `POST /api/plugin-installation/install?allowUnsigned=true` as multipart form data (`file` field).

Adding the configuration page means the integration starts disabled until its one-time setup is completed: open the plugin in Macro Deck, walk through the five short steps and it enables itself. Every number field shows its default as a hint; leaving one empty keeps that default. Later edits apply live without restarting.

For development, press F5 with the **Macro Deck - Real Host** launch profile instead: approve the pairing prompt once (Developer Mode must be on) and later runs reuse the stored credential. See `src/WindowsMediaControl/Properties/launchSettings.json`.

## Develop

```bash
dotnet build
dotnet test
$env:WINDOWS_MEDIA_CONTROL_NOOP_AUDIO = "1"
macrodeck-plugin test --project src/WindowsMediaControl --report markdown --output conformance.md
Remove-Item Env:\WINDOWS_MEDIA_CONTROL_NOOP_AUDIO
macrodeck-plugin test --project src/Timers --report markdown --output conformance-timers.md
```

Conformance for Windows Media Control runs against a no-op audio backend on purpose: the suite drives every
action including volume and device switches, and the no-op keeps it from touching
real hardware. Live hardware behavior is covered by the `*LiveTests` fixtures and
manual verification instead.

Timers conformance is side-effect free (countdowns and the stopwatch touch nothing
outside the process). Screen Control has no conformance run checked in: its suite
would drive real monitor brightness, input switches and window focus with default
parameters, so it is verified through unit tests plus `macrodeck-plugin build`,
`validate --artifact` and `inspect` instead.

`dotnet tool install --global MacroDeck.Plugin.Cli --prerelease` provides `macrodeck-plugin`.

## Releasing

Bump `"version"` in one of `src/*/manifest.json` and push to `master`.
`.github/workflows/release.yml` notices the version has no release yet, runs the
tests once, then packs and attaches each missing artifact to its own GitHub
release. Tags are namespaced per plugin (`v<version>` for Windows Media Control,
`screen-control-v<version>` and `timers-v<version>` for the others), so the
three plugins version independently.
Pushes without a version bump are no-ops. Versions containing `-`
(e.g. `1.11.0-beta.1`) are published as pre-releases.

## Project layout

```
src/WindowsMediaControl/   com.misu.windows-media (SMTC + CoreAudio, widget, config flow)
src/ScreenControl/         com.misu.screen-control (DDC monitors, Win32 windows/desktops)
src/Timers/                com.misu.timers (countdowns, stopwatch, events)
tests/                     one test project per plugin (fakes, no hardware)
```

Each plugin has the same shape:

```
  Program.cs             host builder, DI wiring
  manifest.json          plugin identity and win-x64 entrypoint
  macrodeck-build.json   self-contained publish recipe for `macrodeck-plugin build`
  PluginIntegration.cs   capability wiring
  Actions/               action definitions plus shared parameter helpers
  Localization/Strings.resx   every user-facing string (localized, no literals in code)
  Assets/icon.svg        plugin icon
```

Windows Media Control additionally carries `Media/` (SMTC service, CoreAudio
volume, snapshot/artwork helpers), `Widgets/` (Now Playing widget) and `Config/`
(settings model, config flow, settings reader).

## Notes and limits

- SMTC is read-heavy by nature: snapshots are polled every 2 s and every SMTC call is bounded by a short timeout so a wedged media stack can never stall an action, init or shutdown.
- `invalidate-icon` is deliberately not called: the operation is not understood by current hosts and only produces error logs. Artwork refreshes on the 30 s icon poll instead.
- Widget type registration is idempotent across reconnects; the host retains the catalog and `GetWidgetTypes()` serves recovery from process start.

## License

MIT - see [LICENSE](LICENSE).
