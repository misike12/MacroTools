# Macro Deck 3 plugins

Five independent Windows plugins for Macro Deck 3, built from one solution:

- **Windows Media Control** (`com.misu.windows-media`) — SMTC media playback, CoreAudio volume, devices, artwork, Now Playing widget.
- **Screen Control** (`com.misu.screen-control`) — DDC monitor brightness/input/power plus window and virtual-desktop control.
- **Timers** (`com.misu.timers`) — countdowns, stopwatch, Pomodoro cycles and a Focus Timer widget.
- **CS:MD** (`com.misu.csmd`) — Counter-Strike 2 live match state over Game State Integration.
- **R6MD** (`com.misu.r6md`) — Rainbow Six Siege match tracking over replay files, with an optional live bridge.

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

Besides the fixed variables above, the plugin offers a browsable **App volumes** catalog: every app currently in the Windows volume mixer appears as its own writable 0–100 variable. To put a Spotify slider on your deck, add a **Slider** widget, bind it to a variable, browse to App volumes and pick the app. Both `Spotify` and `Spotify.exe` spellings resolve to the same app, and the binding survives app restarts (it simply reads unavailable while the app has no audio session). The list refreshes live as apps come and go (needs host beta.4 or newer).

### Events (4)

`track-changed` (title/artist/album/app), `playback-changed` (status/isPlaying), `volume-changed` (volume/muted), `mute-changed` (muted).

### Extras

- **Configuration page** — open the integration to find Playback, Volume, Live updates, Events and Advanced sections: preferred app, default seek/volume steps, a maximum-volume safety clamp, poll intervals, per-event toggles, SMTC timeouts, artwork cache size and button cover art. Advanced fields live under their own section, including a reset-to-defaults switch.
- **Now Playing widget** — cover art, title/artist/album, live animated progress bar and prev/play/next buttons, with a configuration view (toggle album, progress and controls). Pressing the tile can run your own flows, and the standard background/label appearance applies.
- **Music player provider** (`system` instance) with real album artwork for the native Music widget.
- **Album art on buttons** — the Play/Pause action supplies the current cover as its button icon, falling back to the configured icon.
- **Button states** — Play/Pause reports playing/paused/stopped, the system/mic mute toggles report muted/unmuted, and the per-app mute toggle reports the configured app's own mute state, so buttons follow the real state with per-state styling.

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

Besides the fixed variables, the plugin offers a browsable **Monitors** catalog: every monitor Windows sees appears as its own writable 0–100 brightness variable (`monitor-1-brightness`, `monitor-2-brightness`, ...). To put a slider for the second monitor on your deck, add a **Slider** widget, bind it to a variable, browse to Monitors and pick it. This is how non-primary monitors get sliders; the binding reads unavailable while that monitor is unplugged. The list refreshes live when monitors are plugged or unplugged (needs host beta.4 or newer).

The **Monitor Brightness widget** uses a relative-drag slider (drag anywhere to nudge, taps move nothing). Double-tap the slider to jump straight back to full brightness. Pressing the tile can run your own flows, and the standard background/label appearance applies.

Power, input and always-on-top actions report button states (on/standby/off, the current input, pinned/unpinned). When Windows reports no monitors at all, the integration raises a resolvable warning instead of failing silently.

Brightness works on monitors without DDC/CI too (e.g. early-2000s panels that only speak VESA DDC 2B): when the backlight cannot be driven over VCP `0x10`, the plugin scales that display's GPU gamma ramp instead. Some GPU drivers (notably NVIDIA) reject gamma ramps darker than 50%, so below that a click-through black veil covers the display the rest of the way down to black; the veil never takes focus, passes input through, and hides at full brightness. Input and power switching genuinely need DDC and report honestly when the monitor has none; input cycling stays within the inputs the monitor advertises in its capabilities string.

## Timers

Countdowns and a stopwatch for automations. The countdown runs on a background timer: pausing keeps the time left, re-starting replaces the running one, and a zero-length start fires the event immediately.

### Actions (14)

Start countdown (hours/minutes/seconds, default 5 minutes, optional label) / Pause / Resume / Cancel, Pause-or-resume toggle, Adjust countdown (add or remove seconds; adjusting past zero finishes it at once), Start / Stop / Reset stopwatch, Start-or-stop toggle, Start Pomodoro (focus/short-break/long-break lengths, rounds, auto-advance) / Stop Pomodoro / Skip phase / Pause-or-resume toggle.

Leaving a number field blank keeps its default (blank minutes means 5, not 0): type an explicit `0` for a seconds-only timer.

### Variables (15)

`countdown_remaining_seconds`, `countdown_text` (`m:ss` or `h:mm:ss`), `countdown_running`, `countdown_label`, `countdown_progress_percent`, `stopwatch_elapsed_seconds`, `stopwatch_text`, `stopwatch_running`, `pomodoro_phase` (`idle`/`focus`/`short-break`/`long-break`), `pomodoro_remaining_seconds`, `pomodoro_phase_text`, `pomodoro_label`, `pomodoro_round`, `pomodoro_running`, `pomodoro_progress_percent` (all refresh every second, all read-only).

### Events (2)

`countdown-finished` (label/seconds), `pomodoro-phase-changed` (phase/round/label).

The toggle actions report button states: countdown and stopwatch show running/paused, Pomodoro shows its live phase (focus/short-break/long-break/idle), so buttons follow the timers with per-state styling.

### Focus timer widget

A deck widget with a big remaining-time hero, phase caption, round dots, a live progress bar and start/pause/resume/skip/reset controls. Its configuration page picks the mode (countdown, stopwatch or Pomodoro), the Pomodoro lengths/rounds/auto-advance, which parts show, compact mode and the accent color. The widget buttons drive the timers directly, so it works standalone with no extra buttons. Pressing the tile can run your own flows, and the standard background/label/accent appearance applies.

### Pomodoro mode

A full focus cycle on its own isolated timer, so it never disturbs a manually started countdown: N focus rounds, short breaks between them, a long break every Nth round, then back to idle. With auto-advance on, phases flow into each other and `pomodoro-phase-changed` fires each time (wire it to a notification automation for the classic ring). With it off, each phase waits paused at full length until resumed or skipped.

## CS:MD

Live Counter-Strike 2 match state on your deck via Game State Integration: the game pushes every change over HTTP, the plugin turns it into variables and events. No memory reading, nothing VAC-risky.

### Setup

1. Open the CS:MD integration in Macro Deck and walk through its one-time setup (connection port, optional auth token, optional Steam ID to follow, position tracking, event toggles).
2. Run the **Install GSI config** action once. It finds your CS2 install through Steam and writes `gamestate_integration_csmacrodeck.cfg` (backing up any same-named file first). If it cannot find the game, copy the file by hand into `...\Counter-Strike Global Offensive\game\csgo\cfg\`.
3. Play. `game-connected` flips true on the first push.

### Actions (4)

Install GSI config / Reset session stats / Simulate a match (injects fake live data so the deck can be arranged without running the game) / Simulate an event (fires one match event with the current state so automations can be tested). Simulate reports a live/idle button state, and Install reports ready/missing; a GSI port conflict raises a resolvable error instead of failing silently.

### Variables (68)

Connection (`gsi_connected`), map (`map_name`, `map_mode`, `map_phase`, `map_round`, `ct_score`, `t_score`, `ct_name`, `t_name`, `ct_timeouts`, `t_timeouts`), round (`round_phase`, `bomb_state`, `phase_ends_in`, `countdown_phase`), player (`my_team`, `player_name`, `player_activity`, `player_clan`, `alive`, `health`, `armor`, `helmet`, `defusekit`, `flashed`, `smoked`, `burning`, `money`, `equip_value`, `weapon`, `weapon_type`, `ammo_clip`, `ammo_reserve`, `kills`, `deaths`, `assists`, `mvps`, `score`, `round_kills`, `round_headshots`, `round_damage`, `round_history`, `smokes_active`, `fire_active`, `grenades_active`), position (`pos_x`, `pos_y`, `pos_z`, `place_name`, `position_source`, `facing_yaw`), bomb (`bomb_countdown`, `bomb_carrier`) and session (`session_kills`, `session_deaths`, `session_kd`, `session_damage`, `session_hs`, `hs_rate`, `session_adr`, `kill_streak`, `best_streak`, `top_weapon`, `top_weapon_kills`, `rounds_played`, `match_elapsed`, `loss_bonus`, `last_chat`). Player values follow whoever you observe, or only your Steam ID when one is configured; everything reads unavailable while no match data is flowing.

Place names (`Mid`, `Bombsite A`, …) come from the map's own `env_cs_place` volumes, read out of the game files the same way community tools do it — every tagged official map is covered, workshop maps too when their mapper tagged them. Kills and deaths carry their place too.

One Valve rule shapes the position variables: the game only sends coordinates and the `allplayers` block to spectators (GOTV or observing a match). Position tracking fills the gap from the console log where the game permits it: add `-condebug -conclearlog +bind scancode104 exec csmd_position` to the CS2 launch options in Steam, run **Install GSI config** (it also writes the `csmd_position` helper file), restart the game, then enable tracking in the integration setup. The plugin taps the trigger key on a timer while CS2 is focused, reads each `getpos` answer from `console.log`, and feeds `pos_x`, `pos_y`, `pos_z` and `place_name` while you are alive and playing yourself. Both `getpos` variants are cheat-gated, so on official servers the game refuses the command and tracking stays quiet; it works in practice with `sv_cheats 1` and anywhere else the command is allowed. The `position-source` variable reports where coordinates come from (`console`, `gsi`, `waiting`, `no-log`, `off`). Spectate any match and they populate from the game feed directly; the **Simulate a match** action injects a Mirage Middle position so the tiles can be arranged and verified without the game.

### Events (14)

`round-started`, `round-ended`, `round-won` / `round-lost` (only when your team is known), `bomb-planted` (site), `bomb-defused`, `bomb-exploded`, `player-died`, `player-kill` (player, weapon), `match-started`, `match-ended` (winner, scores), `streak-milestone` (streak), `place-changed` (place), `chat-message` (player, scope, text). Each group can be toggled in setup. Kills, deaths, bomb moments and round results also flow into the Match HUD event feed.

### Widget (1)

Match HUD: a broadcast-style scorebug (team color zones, big tabular scores, merged round/phase line, live clock, heartbeat live, blinking match-point and breathing winner pills, reactive frame, round-history dots on a timeline rail, map line with pin), a player plate (team-colored name, health ring gauge with state-colored number and armor bar, loadout with crosshair caption, money, KDA grid with semantic colors, round and top-weapon lines), health-trend, per-round-damage and economy graphs, a bomb panel with a countdown ring around the live-ticking clock plus a gradient countdown bar, icon-led info strips (round, streak, timeouts, match time), status pills (alive state, streak, place, bomb, smoke, fire, flash, helmet, kit), a compass with smooth needle, a tracking line (place, coordinates, facing) and a configurable event feed with accent leader dots. Key regions carry screen-reader labels. Swipe the scorebug or the tab bar to flip between the Match, Player and Intel pages (tapping the scorebug advances one page, for readers without touch). Sections, graphs, feed length and compact mode are configurable per widget; the full HUD shows on tiles two cells wide or more, narrower tiles get a compact scorebug automatically. Pressing the tile can run your own flows, and the standard background/label appearance applies.

Deliberately out of scope: Steam Web API history (needs an API key and offers no live data; GSI is the live API) and sending commands into the game (CS2 exposes no such channel).

## R6MD

Rainbow Six Siege match tracking on your deck from the game's own MatchReplay `.rec` files (parsed by a vendored `r6-dissect` build), plus an optional Overwolf bridge receiver for live scores, rosters, HP and phases. Round-granular by nature since replays land per round. No memory reading (BattlEye cannot tell HUD-only intent from cheats), no Overwolf dependency and no Ubisoft credentials.

### Setup

1. Open the R6MD integration and walk through its setup (replay folder discovery across Steam/Ubisoft installs, optional Overwolf bridge port/token, event toggles).
2. Play. New replay files are picked up as rounds complete; the Simulate action injects a sample round for arranging tiles.

### Actions (4) / Variables (44) / Events (11)

Simulate a match / Reset session stats / Rescan replays / Open replay folder. Simulate reports a tracking/idle button state. Variables cover connection, map/mode/site, round and scores, rosters, kill feed, session stats, streaks and Overwolf live state. Events: `kill`, `headshot`, `your-kill`, `your-death`, `round-won`, `round-lost`, `match-won`, `match-lost`, `ace`, `clutch`, `streak-milestone`. A missing MatchReplay folder raises a resolvable warning instead of failing silently.

### Widget (1)

Match HUD: scorebug with round history, team rosters, kill feed and session line, with Simulate/Open-folder idle actions. Pressing the tile can run your own flows, and the standard background/label appearance applies.

## Plugin messaging (needs host beta.12 or newer)

Every plugin also publishes on Macro Deck's message bus (`host:messaging` permission), so other plugins and automations can react without touching the deck:

- **Timers** — `timers.countdown.finished` (label/seconds), `timers.pomodoro.phase-changed` (phase/round/label); ask `timers.state.get` for the full snapshot.
- **R6MD** — `r6md.<event-id>` mirrors all 11 match events; ask `r6md.state.get` for scores, rosters and session.
- **Windows Media Control** — `windows-media.track-changed`, `playback-changed`, `volume-changed`, `mute-changed`; ask `windows-media.state.get` for the now-playing snapshot.
- **Screen Control** — `screen.monitors.changed` when the monitor set changes; ask `screen.state.get` for count, brightness, input and focused window.
- **CS:MD** — `csmd.<event-id>` mirrors all 14 match events; ask `csmd.score.get` for map, scores and phase.

## Requirements

- Windows x64.
- Macro Deck `>=3.0.0-beta.13` (host).
- .NET 10 SDK (to build).

## Install

Build the artifact(s), then install from the Macro Deck desktop app (double-click the file or use install-from-file):

```bash
macrodeck-plugin build --source src/WindowsMediaControl --output ./artifacts
macrodeck-plugin build --source src/ScreenControl --output ./artifacts
macrodeck-plugin build --source src/Timers --output ./artifacts
macrodeck-plugin build --source src/CsMd --output ./artifacts
macrodeck-plugin build --source src/R6Md --output ./artifacts
```

`scripts/install-plugin.ps1` installs packed artifacts into the running host over
loopback, one at a time, with per-artifact timing:

```powershell
./scripts/install-plugin.ps1 ./artifacts/com.misu.timers-1.1.3.macroDeckPlugin
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
macrodeck-plugin test --project src/CsMd --report markdown --output conformance-csmd.md
```

Conformance for Windows Media Control runs against a no-op audio backend on purpose: the suite drives every
action including volume and device switches, and the no-op keeps it from touching
real hardware. Live hardware behavior is covered by the `*LiveTests` fixtures and
manual verification instead.

Timers conformance is side-effect free (countdowns and the stopwatch touch nothing
outside the process). CS:MD conformance is side-effect free too, except the Install
action writes the game config when it finds a CS2 install (its documented purpose;
without one it fails honestly). Screen Control has no conformance run checked in: its suite
would drive real monitor brightness, input switches and window focus with default
parameters, so it is verified through unit tests plus `macrodeck-plugin build`,
`validate --artifact` and `inspect` instead.

`dotnet tool install --global MacroDeck.Plugin.Cli --version 3.0.0-beta.13` provides `macrodeck-plugin` (pinned: `--prerelease` resolves an older line that cannot pack current manifests).

## Releasing

Bump `"version"` in one of `src/*/manifest.json` and push to `master`.
`.github/workflows/release.yml` notices the version has no release yet, runs the
tests once, then packs and attaches each missing artifact to its own GitHub
release. Tags are namespaced per plugin (`v<version>` for Windows Media Control,
`<slug>-v<version>` for the others, e.g. `csmd-v1.15.0`), so the
five plugins version independently.
Pushes without a version bump are no-ops. Versions containing `-`
(e.g. `1.11.0-beta.1`) are published as pre-releases.

## Project layout

```
src/WindowsMediaControl/   com.misu.windows-media (SMTC + CoreAudio, widget, config flow)
src/ScreenControl/         com.misu.screen-control (DDC monitors, Win32 windows/desktops)
src/Timers/                com.misu.timers (countdowns, stopwatch, Pomodoro, widget)
src/CsMd/                  com.misu.csmd (CS2 GSI listener, Match HUD widget, config flow)
src/R6Md/                  com.misu.r6md (Siege replay watcher, Match HUD widget, config flow)
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
