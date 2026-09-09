# Windows Media Control for Macro Deck 3

Control Windows media playback from Macro Deck: transport keys, seeking, shuffle and repeat, system and per-app volume, output device switching, live track variables, album artwork, a Now Playing deck widget and a music player provider. Built on the Windows System Media Transport Controls (SMTC) and Core Audio, so it works with Spotify, browsers and any other SMTC-aware app.

## Features

### Actions (28)

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

Shuffle and repeat go through real SMTC control calls and report honestly when an app refuses them. Every transport, seek, shuffle and repeat action accepts an optional `App` filter (substring match on the app id).

### Variables (34)

`title`, `artist`, `album`, `album_artist`, `genres`, `track_number`, `track_count`, `subtitle`, `source_app`, `playback_status` (`playing`/`paused`/`stopped`/`no-media`), `playback_type` (`music`/`video`/`image`/`unknown`), `playback_rate`, `is_playing`, `is_live`, `has_media`, `position_seconds`, `duration_seconds`, `position_text` / `duration_text` (`m:ss`), `progress_percent`, `volume_percent`, `is_muted`, `shuffle_enabled`, `repeat_mode`, plus per-action support flags `can_play`, `can_pause`, `can_stop`, `can_next`, `can_previous`, `can_seek`, `can_shuffle`, `can_repeat`, the current `output_device` name and the cover-derived `cover_accent` hex color.

Writable: `volume_percent`, `is_muted`, `position_seconds` and `progress_percent` (writing seeks; progress commits on release so slider drags don't stutter playback).

### App volume sliders

Besides the fixed variables above, the plugin offers a browsable **App volumes** catalog: every app currently in the Windows volume mixer appears as its own writable 0–100 variable. To put a Spotify slider on your deck, add a **Slider** widget, bind it to a variable, browse to App volumes and pick the app. Both `Spotify` and `Spotify.exe` spellings resolve to the same app, and the binding survives app restarts (it simply reads unavailable while the app has no audio session).

### Events (4)

`track-changed` (title/artist/album/app), `playback-changed` (status/isPlaying), `volume-changed` (volume/muted), `mute-changed` (muted).

### Extras

- **Configuration page** — open the integration to find Playback, Volume, Live updates, Events and Advanced sections: preferred app, default seek/volume steps, a maximum-volume safety clamp, poll intervals, per-event toggles, SMTC timeouts, artwork cache size and button cover art. Advanced fields live under their own section, including a reset-to-defaults switch.
- **Now Playing widget** — title/artist/album, live animated progress bar and prev/play/next buttons, with a configuration view (toggle album, progress and controls).
- **Music player provider** (`system` instance) with real album artwork for the native Music widget.
- **Album art on buttons** — the Play/Pause action supplies the current cover as its button icon, falling back to the configured icon.

## Requirements

- Windows x64.
- Macro Deck `>=3.0.0-beta.2` (host).
- .NET 10 SDK (to build).

## Install

Build the artifact, then install it from the Macro Deck desktop app (double-click the file or use install-from-file):

```bash
macrodeck-plugin build --source src/WindowsMediaControl --output ./artifacts
```

The locally packed artifact is unsigned, so Macro Deck asks for an explicit confirmation on install. (Store releases are signed server-side by the Creator Portal.)

Adding the configuration page means the integration starts disabled until its one-time setup is completed: open the plugin in Macro Deck, walk through the five short steps and it enables itself. Every number field shows its default as a hint; leaving one empty keeps that default. Later edits apply live without restarting.

For development, press F5 with the **Macro Deck - Real Host** launch profile instead: approve the pairing prompt once (Developer Mode must be on) and later runs reuse the stored credential. See `src/WindowsMediaControl/Properties/launchSettings.json`.

## Develop

```bash
dotnet build
dotnet test
macrodeck-plugin test --project src/WindowsMediaControl --report markdown --output conformance.md
```

`dotnet tool install --global MacroDeck.Plugin.Cli --prerelease` provides `macrodeck-plugin`.

## Project layout

```
src/WindowsMediaControl/
  Program.cs             host builder, DI wiring
  manifest.json          plugin identity (com.misu.windows-media) and win-x64 entrypoint
  macrodeck-build.json   self-contained publish recipe for `macrodeck-plugin build`
  PluginIntegration.cs   actions, variables, events, music player, widget wiring, poll loop
  Media/                 SMTC service (WindowsMediaControlService), CoreAudio volume, snapshot model
  Actions/               transport, seek and volume actions plus shared parameter helpers
  Widgets/               Now Playing widget type, sessions, configuration and previews
  Localization/Strings.resx   every user-facing string (localized, no literals in code)
  Assets/icon.svg        plugin icon
tests/WindowsMediaControl.Tests/
  PluginIntegrationTests.cs   behaviour tests against a fake media service
  FakeMediaControlService.cs  controllable stand-in for SMTC/audio
```

## Notes and limits

- SMTC is read-heavy by nature: snapshots are polled every 2 s and every SMTC call is bounded by a short timeout so a wedged media stack can never stall an action, init or shutdown.
- `invalidate-icon` is deliberately not called: the operation is not understood by current hosts and only produces error logs. Artwork refreshes on the 30 s icon poll instead.
- Widget type registration is idempotent across reconnects; the host retains the catalog and `GetWidgetTypes()` serves recovery from process start.

## License

MIT - see [LICENSE](LICENSE).
