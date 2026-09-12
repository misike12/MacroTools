using System.Net.Http.Json;
using System.Text.Json;
using CsMd.Gsi;
using NUnit.Framework;

namespace CsMd.Tests;

[TestFixture]
public sealed class GsiTests
{
	private static readonly JsonSerializerOptions Strict = new() { PropertyNameCaseInsensitive = true };

	[Test]
	public void Tolerant_parsing_survives_quirks()
	{
		const string json = """
		{
			"provider": { "name": "Counter-Strike 2", "appid": "730", "version": 1, "steamid": "76561198000000000", "timestamp": 1, "unknown_future": true },
			"map": { "mode": "competitive", "name": "de_mirage", "phase": "live", "round": "5", "team_ct": { "score": "3", "name": "CTs" }, "team_t": { "score": 1 } },
			"round": { "phase": "live", "win_team": null, "bomb": "" },
			"player": {
				"steamid": "76561198000000000", "name": "Me", "team": "CT", "activity": "playing",
				"state": { "health": "100", "armor": 100, "helmet": true, "flashed": 0, "money": 16000 },
				"weapons": { "weapon_0": { "name": "weapon_ak47", "state": "active", "ammo_clip": "30", "ammo_reserve": 90 } },
				"match_stats": { "kills": 4, "assists": 1, "deaths": 2, "mvps": 0, "score": 10 }
			},
			"phase_countdowns": { "phase": "live", "phase_ends_in": "95.5" },
			"bomb": { "state": "carried" },
			"grenades": { "1": { "owner": "76561198000000000", "type": "smoke", "lifetime": "18.0", "effecttime": 12.0 } }
		}
		""";

		var payload = JsonSerializer.Deserialize<GsiPayload>(json, GsiJson.Options);

		Assert.That(payload, Is.Not.Null);
		Assert.That(payload!.Provider!.AppId, Is.EqualTo(730));
		Assert.That(payload.Map!.Round, Is.EqualTo(5));
		Assert.That(payload.Map.TeamCt!.Score, Is.EqualTo(3));
		Assert.That(payload.Player!.State!.Health, Is.EqualTo(100));
		Assert.That(payload.Player.State.Money, Is.EqualTo(16000));
		Assert.That(payload.Player.Weapons!["weapon_0"].AmmoClip, Is.EqualTo(30));
		Assert.That(payload.PhaseCountdowns!.PhaseEndsIn, Is.EqualTo(95.5));
		Assert.That(payload.Grenades!["1"].EffectTime, Is.EqualTo(12.0));
	}

	[Test]
	public void Empty_and_garbage_payloads_do_not_throw()
	{
		Assert.DoesNotThrow(() => JsonSerializer.Deserialize<GsiPayload>("{}", GsiJson.Options));
		Assert.DoesNotThrow(() => JsonSerializer.Deserialize<GsiPayload>("{\"map\":null}", GsiJson.Options));
		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GsiPayload>("not json", GsiJson.Options));
	}

	[Test]
	public async Task Listener_accepts_posts_and_tracks_state()
	{
		using var gsi = new GsiService(TestLogger());
		Assert.That(gsi.Start(0, null), Is.True, "ephemeral port must bind");
		Assert.That(gsi.IsListening, Is.True);

		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var payload = JsonContent.Create(new
		{
			map = new { mode = "competitive", name = "de_mirage", phase = "live", round = 5 },
			round = new { phase = "live" },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				state = new { health = 87, armor = 100, helmet = true, money = 2400 },
				weapons = new Dictionary<string, object>
				{
					["weapon_0"] = new { name = "weapon_m4a1", state = "active", ammo_clip = 20, ammo_reserve = 60 },
				},
				match_stats = new { kills = 5, assists = 1, deaths = 2, mvps = 0, score = 12 },
			},
		});

		var response = await http.PostAsync(uri, payload, TestContext.CurrentContext.CancellationToken);

		Assert.That((int)response.StatusCode, Is.EqualTo(200));
		var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
		GsiSnapshot snapshot;
		do
		{
			await Task.Delay(50, TestContext.CurrentContext.CancellationToken);
			snapshot = gsi.Snapshot();
		}
		while (!snapshot.Connected && DateTimeOffset.UtcNow < deadline);

		Assert.That(snapshot.Connected, Is.True);
		Assert.That(snapshot.MapName, Is.EqualTo("de_mirage"));
		Assert.That(snapshot.Health, Is.EqualTo(87));
		Assert.That(snapshot.Weapon, Is.EqualTo("m4a1"));
		Assert.That(snapshot.AmmoClip, Is.EqualTo(20));
		Assert.That(snapshot.Kills, Is.EqualTo(5));
		gsi.Stop();
		Assert.That(gsi.IsListening, Is.False);
	}

	[Test]
	public async Task Kill_death_and_bomb_deltas_fire_events()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var seen = new List<GsiMatchEvent>();
		gsi.MatchEvent += (_, e) =>
		{
			lock (seen)
			{
				seen.Add(e);
			}
		};

		Task<HttpResponseMessage> Post(object body, CancellationToken ct) =>
			http.PostAsync(uri, JsonContent.Create(body), ct);
		var ct = TestContext.CurrentContext.CancellationToken;

		await Post(BaseState(100, 5, 2, "carried"), ct);
		await Post(BaseState(0, 5, 3, "carried"), ct);
		await Post(BaseState(100, 7, 3, "planted"), ct);
		await Post(BaseState(100, 7, 3, "exploded"), ct);

		var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		while (DateTimeOffset.UtcNow < deadline)
		{
			List<GsiMatchEvent> copy;
			lock (seen)
			{
				copy = seen.ToList();
			}

			var died = 0;
			var kills = 0;
			var planted = 0;
			var exploded = 0;
			foreach (var matchEvent in copy)
			{
				switch (matchEvent.EventId)
				{
					case GsiEventIds.PlayerDied: died++; break;
					case GsiEventIds.PlayerKill: kills++; break;
					case GsiEventIds.BombPlanted: planted++; break;
					case GsiEventIds.BombExploded: exploded++; break;
				}
			}

			if (died >= 1 && kills >= 2 && planted >= 1 && exploded >= 1)
			{
				break;
			}

			await Task.Delay(50, ct);
		}

		List<GsiMatchEvent> final;
		lock (seen)
		{
			final = seen.ToList();
		}

		var finalDied = 0;
		var finalKills = 0;
		var finalPlanted = 0;
		var finalExploded = 0;
		GsiMatchEvent? kill = null;
		foreach (var matchEvent in final)
		{
			switch (matchEvent.EventId)
			{
				case GsiEventIds.PlayerDied: finalDied++; break;
				case GsiEventIds.PlayerKill: finalKills++; kill ??= matchEvent; break;
				case GsiEventIds.BombPlanted: finalPlanted++; break;
				case GsiEventIds.BombExploded: finalExploded++; break;
			}
		}

		Assert.That(finalDied, Is.EqualTo(1));
		Assert.That(finalKills, Is.EqualTo(2));
		Assert.That(finalPlanted, Is.EqualTo(1));
		Assert.That(finalExploded, Is.EqualTo(1));
		Assert.That(kill, Is.Not.Null);
		Assert.That(kill!.Payload["weapon"], Is.EqualTo("ak47"));

		Assert.That(gsi.Snapshot().SessionKills, Is.EqualTo(2));
		Assert.That(gsi.Snapshot().SessionDeaths, Is.EqualTo(1));
		gsi.ResetSessionStats();
		Assert.That(gsi.Snapshot().SessionKills, Is.EqualTo(0));
	}

	[Test]
	public async Task Wrong_auth_token_is_ignored()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, "secret");
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";

		var response = await http.PostAsync(uri,
			JsonContent.Create(new { auth = new { token = "wrong" }, map = new { name = "de_mirage" } }),
			TestContext.CurrentContext.CancellationToken);

		Assert.That((int)response.StatusCode, Is.EqualTo(200));
		await Task.Delay(300, TestContext.CurrentContext.CancellationToken);
		Assert.That(gsi.Snapshot().Connected, Is.False);
	}

	[Test]
	public void Cfg_renders_with_and_without_token()
	{
		var plain = GsiConfig.Render(32075, null);
		Assert.That(plain, Does.Contain("\"uri\"          \"http://127.0.0.1:32075/gsi\""));
		Assert.That(plain, Does.Not.Contain("\"auth\""));
		Assert.That(plain, Does.Contain("\"player_match_stats\" \"1\""));
		Assert.That(plain, Does.Contain("\"bomb\" \"1\""));

		var authed = GsiConfig.Render(3000, "s3cret");
		Assert.That(authed, Does.Contain("\"token\" \"s3cret\""));
	}

	[Test]
	public void Steam_discovery_finds_cs_through_library_folders()
	{
		var root = Path.Combine(Path.GetTempPath(), "CsMdSteamTest", Guid.NewGuid().ToString("N"));
		try
		{
			var steam = Path.Combine(root, "Steam");
			var second = Path.Combine(root, "Games");
			var cfg = Path.Combine(second, "steamapps", "common", "Counter-Strike Global Offensive", "game", "csgo", "cfg");
			Directory.CreateDirectory(cfg);
			Directory.CreateDirectory(Path.Combine(second, "steamapps"));
			File.WriteAllText(Path.Combine(second, "steamapps", "appmanifest_730.acf"), "\"AppState\"{}");
			Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
			File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
				"\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"		\"" + second.Replace("\\", "\\\\") + "\"\n\t}\n}");

			Assert.That(GsiConfig.FindCsDirectory([steam]), Is.EqualTo(cfg));

			var (ok, detail) = GsiConfig.Install(32075, null, [steam]);
			Assert.That(ok, Is.True);
			Assert.That(File.ReadAllText(detail), Does.Contain("127.0.0.1:32075"));

			var (missing, _) = GsiConfig.Install(32075, null, [Path.Combine(root, "Nope")]);
			Assert.That(missing, Is.False);
		}
		finally
		{
			try
			{
				Directory.Delete(root, true);
			}
			catch (Exception)
			{
			}
		}
	}

	[Test]
	public void Vdf_paths_parse()
	{
		var paths = GsiConfig.ParseLibraryPaths("\"libraryfolders\"\n{\n\"0\"\n{\n\"path\" \"D:\\\\Games\\\\Steam\"\n}\n}").ToList();
		Assert.That(paths.Count, Is.EqualTo(1));
		Assert.That(paths[0], Is.EqualTo("D:\\Games\\Steam"));
		Assert.That(GsiConfig.ParseLibraryPaths("garbage {{{").ToList(), Is.Empty);
	}

	private static Serilog.Core.Logger TestLogger() => new Serilog.LoggerConfiguration().CreateLogger();

	private static object BaseState(int health, int kills, int deaths, string bomb) => new
	{
		map = new { mode = "competitive", name = "de_mirage", phase = "live", round = 5 },
		round = new { phase = "live" },
		player = new
		{
			steamid = "76561198000000000",
			name = "Me",
			team = "CT",
			state = new { health, armor = 100, helmet = true, money = 2400 },
			weapons = new Dictionary<string, object>
			{
				["weapon_0"] = new { name = "weapon_ak47", state = "active", ammo_clip = 30, ammo_reserve = 90 },
			},
			match_stats = new { kills, assists = 1, deaths, mvps = 0, score = 10 },
		},
		bomb = new { state = bomb },
	};
}
