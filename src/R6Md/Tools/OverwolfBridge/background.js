// R6MD Bridge: forwards Rainbow Six Siege (game id 10826) GEP data to the
// R6MD Macro Deck plugin over loopback. Personal use only: nothing here
// leaves this machine, and the plugin only ever surfaces what the HUD and
// scoreboard already show (score, roster, HP, phases, kills, round/match
// outcomes). Requires Overwolf with this app sideloaded; see README.md.
(function () {
  "use strict";

  var PORT = 32175;
  var TOKEN = "";
  var URL = "http://127.0.0.1:" + PORT + "/r6/event";
  var FEATURES = ["gep_internal", "game_info", "match", "match_info", "roster", "kill", "death", "me"];
  var RETRY_MS = 5000;

  function post(body) {
    try {
      var headers = { "Content-Type": "application/json" };
      if (TOKEN) {
        headers["Authorization"] = "Bearer " + TOKEN;
      }
      fetch(URL, { method: "POST", headers: headers, body: JSON.stringify(body) }).catch(function () {});
    } catch (e) { /* offline plugin: drop silently */ }
  }

  function text(value) {
    return value === null || value === undefined ? "" : String(value);
  }

  function number(value) {
    var n = Number(value);
    return isFinite(n) ? n : 0;
  }

  function requestFeatures() {
    try {
      overwolf.games.events.setRequiredFeatures(FEATURES, function (info) {
        if (!info || info.status !== "success") {
          setTimeout(requestFeatures, RETRY_MS);
        }
      });
    } catch (e) {
      setTimeout(requestFeatures, RETRY_MS);
    }
  }

  function rosterOf(info) {
    var out = [];
    try {
      var matchInfo = (info && info.match_info) || {};
      var players = matchInfo.players || info.roster || {};
      Object.keys(players).forEach(function (slot) {
        var p = players[slot] || {};
        if (!p.name) {
          return;
        }
        out.push({
          name: text(p.name),
          team: text(p.team).toLowerCase(),
          operator: text(p.operator),
          kills: number(p.kills),
          deaths: number(p.deaths),
          hp: typeof p.health === "number" ? Math.max(0, Math.round(p.health)) : -1,
          local: p.is_local === true
        });
      });
    } catch (e) { /* partial info: send what we have */ }
    return out;
  }

  function scoreOf(info, color) {
    try {
      var score = ((info && info.match_info) || {}).score || {};
      return number(score[color]);
    } catch (e) {
      return 0;
    }
  }

  function field(info, feature, key) {
    try {
      var section = (info && info[feature]) || {};
      return section[key];
    } catch (e) {
      return undefined;
    }
  }

  function onInfo(info) {
    if (!info) {
      return;
    }
    post({
      type: "info",
      phase: text(field(info, "match_info", "phase") || field(info, "game_info", "phase")),
      map: text(field(info, "match_info", "map_id")),
      mode: text(field(info, "match_info", "game_mode")),
      blue: scoreOf(info, "blue"),
      orange: scoreOf(info, "orange"),
      players: rosterOf(info)
    });
  }

  function onEvents(batch) {
    if (!batch || !batch.events) {
      return;
    }
    batch.events.forEach(function (ev) {
      if (!ev || !ev.name) {
        return;
      }
      var data = ev.data || "";
      switch (ev.name) {
        case "kill":
          post({ type: "event", name: "kill", player: "", target: "", headshot: false });
          break;
        case "headshot":
          post({ type: "event", name: "kill", player: "", target: "", headshot: true });
          break;
        case "death":
          post({ type: "event", name: "death", player: text(data) });
          break;
        case "killer":
          post({ type: "event", name: "death", player: text(data) });
          break;
        case "roundOutcome":
          post({ type: "event", name: "roundOutcome", outcome: text(data) });
          break;
        case "matchOutcome":
          post({ type: "event", name: "matchOutcome", outcome: text(data) });
          break;
        default:
          break;
      }
    });
  }

  try {
    overwolf.games.events.onInfoUpdates2.addListener(onInfo);
    overwolf.games.events.onNewEvents.addListener(onEvents);
  } catch (e) { /* listeners attach once the API is ready */ }

  requestFeatures();
})();
