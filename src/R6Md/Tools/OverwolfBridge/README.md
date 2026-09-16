# R6MD Overwolf bridge (optional)

A tiny private Overwolf app that forwards Rainbow Six Siege match data to the
R6MD plugin over loopback. Everything stays on this machine: the app posts to
`http://127.0.0.1:32175/r6/event`, and the plugin only ever surfaces what the
HUD and scoreboard already show (score, roster with HP, phases, kills and
round/match outcomes). No memory reading, no credentials, no accounts.

Without this bridge (and without Overwolf installed at all) the plugin works
exactly as before, purely from the game's replay files. The bridge only adds
*immediacy*: counters and phases tick live instead of landing per round.

## Install

1. Install Overwolf and log in.
2. Open Overwolf Settings → About → Development options (or the Creator
   tools) and enable loading unpacked apps / developer mode.
3. Load this folder (`manifest.json`) as an unpacked app.
4. If you changed the port or set a token in the plugin's R6MD settings
   (Live feed step), edit the `PORT` / `TOKEN` constants at the top of
   `background.js` to match, then reload the app.
5. In the plugin settings, enable the Overwolf bridge. Play a match: the
   `ow-connected` variable flips true and the widget's numbers start moving
   live.

## Contract

- `POST /r6/event`, JSON body, optional `Authorization: Bearer <token>`.
- `{"type":"info","phase","map","mode","blue","orange","players":[{name,team,operator,kills,deaths,hp,local}]}` replaces the live frame.
- `{"type":"event","name":"kill"|"headshot"|"death","player","target","headshot"}` announces one elimination. GEP kill events carry no names, so
  nameless rows move the counters without touching the feed; attributed rows
  behave like replay rows, and the replay pass will not duplicate them.
- `{"type":"event","name":"roundOutcome"|"matchOutcome","outcome":"victory"|"defeat"}` announces results from your perspective.
- Anything else answers 400. A wrong token answers 401.

## Notes

- GEP only runs while this app is loaded and the game is Siege (game id
  10826). If the widget shows replay data but no live values, this app is
  the part that is not running.
- The GEP field shapes drift with game updates; this mapping is defensive
  (missing fields read as empty, never as errors). If a season renames
  something, the live layer quietly degrades to replay data.
- Session totals (kills, deaths, streaks) always accrue from replays, never
  from live events, so nothing is ever counted twice.
