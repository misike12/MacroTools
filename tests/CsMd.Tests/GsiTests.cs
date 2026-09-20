using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using CsMd.Gsi;
using CsMd.Places;
using NUnit.Framework;
using Serilog;

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
	public void Mistyped_fields_degrade_singly_not_wholly()
	{
		const string json = """
		{
			"provider": { "name": 730, "appid": "730", "version": 1, "steamid": null, "timestamp": "99" },
			"map": { "mode": "casual", "name": "de_dust2", "phase": "live", "round": 1 },
			"player": {
				"steamid": "76561198000000000", "name": null, "team": "CT", "activity": "playing",
				"state": { "health": "100", "armor": 100.0, "helmet": 1, "defusekit": "true", "flashed": 0, "smoked": 0, "burning": 0, "money": 800, "round_kills": 2, "round_killhs": 1, "round_totaldmg": 250, "equip_value": 4700 },
				"weapons": { "weapon_0": { "name": "weapon_ak47", "type": "Rifle", "state": "active", "ammo_clip": "30", "ammo_reserve": 90 } },
				"match_stats": { "kills": 4, "assists": 1, "deaths": 2, "mvps": 0, "score": 10 }
			}
		}
		""";

		var payload = JsonSerializer.Deserialize<GsiPayload>(json, GsiJson.Options);

		Assert.That(payload, Is.Not.Null);
		Assert.That(payload!.Provider!.Name, Is.EqualTo("730"));
		Assert.That(payload.Provider.AppId, Is.EqualTo(730));
		Assert.That(payload.Provider.SteamId, Is.Null);
		Assert.That(payload.Provider.Timestamp, Is.EqualTo(99));
		Assert.That(payload.Player!.Name, Is.Null);
		Assert.That(payload.Player.State!.Health, Is.EqualTo(100));
		Assert.That(payload.Player.State.Armor, Is.EqualTo(100));
		Assert.That(payload.Player.State.Helmet, Is.True);
		Assert.That(payload.Player.State.DefuseKit, Is.True);
		Assert.That(payload.Player.State.RoundKills, Is.EqualTo(2));
		Assert.That(payload.Player.State.RoundHeadshots, Is.EqualTo(1));
		Assert.That(payload.Player.State.RoundDamage, Is.EqualTo(250));
		Assert.That(payload.Player.Weapons!["weapon_0"].AmmoClip, Is.EqualTo(30));
	}

	[Test]
	public async Task Streaks_top_weapon_rounds_and_damage_track()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var ct = TestContext.CurrentContext.CancellationToken;

		Task<HttpResponseMessage> Post(object body) =>
			http.PostAsync(uri, JsonContent.Create(body), ct);
		object State(int health, int kills, int deaths, int damage, string weapon) => new
		{
			map = new { mode = "competitive", name = "de_mirage", phase = "live", round = 5 },
			round = new { phase = "live" },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				clan = "TAG",
				team = "CT",
				state = new { health, armor = 100, helmet = true, money = 2400, round_kills = kills, round_killhs = 0, round_totaldmg = damage, equip_value = 4500 },
				weapons = new Dictionary<string, object>
				{
					["weapon_0"] = new { name = weapon, type = "Rifle", state = "active", ammo_clip = 30, ammo_reserve = 90 },
				},
				match_stats = new { kills, assists = 0, deaths, mvps = 0, score = kills * 2 },
			},
		};

		await Post(State(100, 0, 0, 0, "weapon_ak47"));
		await Post(State(100, 2, 0, 250, "weapon_ak47"));
		await Post(State(0, 2, 1, 250, "weapon_ak47"));
		await Post(State(100, 3, 1, 60, "weapon_awp"));

		var snapshot = await WaitForSnapshotAsync(gsi, ct);
		var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
		while ((snapshot.KillStreak != 1 || snapshot.BestStreak != 2) && DateTimeOffset.UtcNow < deadline)
		{
			await Task.Delay(50, ct);
			snapshot = gsi.Snapshot();
		}

		Assert.That(snapshot.KillStreak, Is.EqualTo(1));
		Assert.That(snapshot.BestStreak, Is.EqualTo(2));
		Assert.That(snapshot.TopWeapon, Is.EqualTo("AK-47"));
		Assert.That(snapshot.TopWeaponKills, Is.EqualTo(2));
		Assert.That(snapshot.Clan, Is.EqualTo("TAG"));
		Assert.That(snapshot.RoundsPlayed, Is.EqualTo(1));

		gsi.ResetSessionStats();
		snapshot = gsi.Snapshot();
		Assert.That(snapshot.KillStreak, Is.EqualTo(0));
		Assert.That(snapshot.BestStreak, Is.EqualTo(0));
		Assert.That(snapshot.TopWeapon, Is.Null);
		Assert.That(snapshot.RoundsPlayed, Is.EqualTo(0));
		Assert.That(snapshot.SessionDamage, Is.EqualTo(0));
	}

	[Test]
	public void Loss_bonus_follows_consecutive_losses()
	{
		Assert.That(GsiService.LossBonusOf(string.Empty, "CT"), Is.EqualTo(0));
		Assert.That(GsiService.LossBonusOf("CCT", null), Is.EqualTo(0));
		Assert.That(GsiService.LossBonusOf("CCT", "CT"), Is.EqualTo(2400));
		Assert.That(GsiService.LossBonusOf("CCT", "T"), Is.EqualTo(0));
		Assert.That(GsiService.LossBonusOf("TT", "T"), Is.EqualTo(0));
		Assert.That(GsiService.LossBonusOf("CTT", "CT"), Is.EqualTo(2900));
		Assert.That(GsiService.LossBonusOf("TTT", "CT"), Is.EqualTo(3400));
		Assert.That(GsiService.LossBonusOf("TTTTTTTT", "CT"), Is.EqualTo(3400));
		Assert.That(GsiService.LossBonusOf("CT?", "T"), Is.EqualTo(0));
	}

	[Test]
	public async Task Streak_milestones_fire_at_thresholds()
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
		var ct = TestContext.CurrentContext.CancellationToken;

		Task<HttpResponseMessage> Post(int kills) => http.PostAsync(uri, JsonContent.Create(new
		{
			map = new { mode = "competitive", name = "de_mirage", phase = "live", round = 5 },
			round = new { phase = "live" },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				state = new { health = 100 },
				weapons = new Dictionary<string, object>
				{
					["weapon_0"] = new { name = "weapon_ak47", state = "active", ammo_clip = 30, ammo_reserve = 90 },
				},
				match_stats = new { kills, assists = 0, deaths = 0, mvps = 0, score = kills * 2 },
			},
		}), ct);

		await Post(0);
		await Post(3);
		await Post(5);

		var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		List<GsiMatchEvent> copy;
		do
		{
			await Task.Delay(50, ct);
			lock (seen)
			{
				copy = seen.ToList();
			}
		}
		while (copy.Count(e => e.EventId == GsiEventIds.StreakMilestone) < 2 && DateTimeOffset.UtcNow < deadline);

		var milestones = copy
			.Where(e => e.EventId == GsiEventIds.StreakMilestone)
			.Select(e => (double)e.Payload["streak"]!)
			.OrderBy(streak => streak)
			.ToList();

		Assert.That(milestones, Is.EqualTo([3.0, 5.0]));
	}

	[Test]
	public async Task Place_changes_fire_once_per_area()
	{
		var maps = new Dictionary<string, IReadOnlyList<PlaceVolume>>
		{
			["de_test"] = [
				new PlaceVolume("Middle", "Middle", new Vector3(-100, -100, -50), new Vector3(100, 100, 50)),
				new PlaceVolume("Site", "Site", new Vector3(200, 200, -50), new Vector3(300, 300, 50)),
			],
		};
		using var gsi = new GsiService(TestLogger(), new StubPlaces(maps));
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
		var ct = TestContext.CurrentContext.CancellationToken;

		Task<HttpResponseMessage> Post(string position) => http.PostAsync(uri, JsonContent.Create(new
		{
			map = new { name = "de_test", phase = "live" },
			player = new
			{
				steamid = "1",
				name = "Me",
				position,
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
		}), ct);

		await Post("0, 0, 0");
		await Post("10, 10, 0");
		await Post("250, 250, 0");
		await Post("260, 260, 0");

		var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		List<GsiMatchEvent> copy;
		do
		{
			await Task.Delay(50, ct);
			lock (seen)
			{
				copy = seen.ToList();
			}
		}
		while (!copy.Any(e => e.EventId == GsiEventIds.PlaceChanged) && DateTimeOffset.UtcNow < deadline);

		var changes = copy.Where(e => e.EventId == GsiEventIds.PlaceChanged).ToList();

		Assert.That(changes.Count, Is.EqualTo(1));
		Assert.That(changes[0].Payload["place"], Is.EqualTo("Site"));
	}

	[Test]
	public async Task Round_change_banks_previous_round_damage()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var ct = TestContext.CurrentContext.CancellationToken;

		Task<HttpResponseMessage> Post(int round, int damage) => http.PostAsync(uri, JsonContent.Create(new
		{
			map = new { mode = "competitive", name = "de_mirage", phase = "live", round },
			round = new { phase = "live" },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				state = new { health = 100, round_totaldmg = damage },
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
		}), ct);

		await Post(5, 300);
		await Post(6, 20);

		var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
		GsiSnapshot snapshot;
		do
		{
			await Task.Delay(50, ct);
			snapshot = gsi.Snapshot();
		}
		while (snapshot.SessionDamage != 300 && DateTimeOffset.UtcNow < deadline);

		Assert.That(snapshot.SessionDamage, Is.EqualTo(300));
		Assert.That(snapshot.RoundsPlayed, Is.EqualTo(2));
	}

	[Test]
	public async Task Timeout_grenade_countdown_clan_surface()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var ct = TestContext.CurrentContext.CancellationToken;

		await http.PostAsync(uri, JsonContent.Create(new
		{
			map = new
			{
				mode = "competitive",
				name = "de_mirage",
				phase = "live",
				round = 5,
				team_ct = new { score = 3, timeouts_remaining = 2 },
				team_t = new { score = 1, timeouts_remaining = 0 },
			},
			round = new { phase = "live" },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				clan = "TAG",
				team = "CT",
				state = new { health = 100 },
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
			phase_countdowns = new { phase = "live", phase_ends_in = 100.0 },
			grenades = new Dictionary<string, object>
			{
				["1"] = new { owner = "1", type = "smoke", lifetime = 18.0, effecttime = 12.0 },
				["2"] = new { owner = "1", type = "flashbang", lifetime = 1.0, effecttime = 0.0 },
			},
		}), ct);

		var snapshot = await WaitForSnapshotAsync(gsi, ct);

		Assert.That(snapshot.TimeoutsCt, Is.EqualTo(2));
		Assert.That(snapshot.TimeoutsT, Is.EqualTo(0));
		Assert.That(snapshot.CountdownPhase, Is.EqualTo("live"));
		Assert.That(snapshot.GrenadesActive, Is.EqualTo(2));
		Assert.That(snapshot.Clan, Is.EqualTo("TAG"));
	}

	[Test]
	public async Task Round_wins_build_history_oldest_first()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var ct = TestContext.CurrentContext.CancellationToken;

		await http.PostAsync(uri, JsonContent.Create(new
		{
			map = new
			{
				mode = "competitive",
				name = "de_mirage",
				phase = "live",
				round = 4,
				round_wins = new Dictionary<string, object>
				{
					["3"] = "t_win_bomb",
					["1"] = "ct_win_elimination",
					["2"] = "ct_win_time",
					["bogus"] = "ct_win_elimination",
					["4"] = "overtime",
				},
			},
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				state = new { health = 100 },
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
		}), ct);

		var snapshot = await WaitForSnapshotAsync(gsi, ct);

		Assert.That(snapshot.RoundHistory, Is.EqualTo("CCT?"));
	}

	[Test]
	public async Task Terminal_bomb_states_hide_outside_round_end()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var ct = TestContext.CurrentContext.CancellationToken;

		Task<HttpResponseMessage> Post(object body) =>
			http.PostAsync(uri, JsonContent.Create(body), ct);
		Task<GsiSnapshot> Snap() => WaitForSnapshotAsync(gsi, ct);

		await Post(new
		{
			map = new { mode = "competitive", name = "de_mirage", phase = "live", round = 6 },
			round = new { phase = "freezetime" },
			bomb = new { state = "defused" },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				state = new { health = 100 },
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
		});
		Assert.That((await Snap()).BombState, Is.Null);

		await Post(new
		{
			map = new { mode = "competitive", name = "de_mirage", phase = "live", round = 6 },
			round = new { phase = "over", win_team = "CT" },
			bomb = new { state = "defused" },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				state = new { health = 100 },
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
		});
		Assert.That((await Snap()).BombState, Is.EqualTo("defused"));
	}

	[Test]
	public async Task Weapon_types_prettify_for_display()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var ct = TestContext.CurrentContext.CancellationToken;

		await http.PostAsync(uri, JsonContent.Create(new
		{
			map = new { mode = "competitive", name = "de_mirage", phase = "live", round = 5 },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				state = new { health = 100 },
				weapons = new Dictionary<string, object>
				{
					["weapon_0"] = new { name = "weapon_awp", type = "SniperRifle", state = "active", ammo_clip = 5, ammo_reserve = 30 },
				},
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
		}), ct);

		var snapshot = await WaitForSnapshotAsync(gsi, ct);

		Assert.That(snapshot.WeaponType, Is.EqualTo("Sniper Rifle"));
	}

	[Test]
	public async Task Round_bomb_fills_in_for_a_missing_bomb_block()
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
		var ct = TestContext.CurrentContext.CancellationToken;

		await http.PostAsync(uri, JsonContent.Create(new
		{
			map = new { mode = "competitive", name = "de_mirage", phase = "live", round = 5 },
			round = new { phase = "over", bomb = "planted" },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				state = new { health = 100 },
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
		}), ct);

		var snapshot = await WaitForSnapshotAsync(gsi, ct);

		Assert.That(snapshot.BombState, Is.EqualTo("planted"));

		var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
		GsiMatchEvent? planted = null;
		while (DateTimeOffset.UtcNow < deadline && planted is null)
		{
			await Task.Delay(50, ct);
			lock (seen)
			{
				planted = seen.FirstOrDefault(e => e.EventId == GsiEventIds.BombPlanted);
			}
		}

		Assert.That(planted, Is.Not.Null);
		Assert.That(planted!.Payload["site"], Is.EqualTo(string.Empty));
	}

	[Test]
	public async Task Bomb_site_keeps_only_real_sites()
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
		var ct = TestContext.CurrentContext.CancellationToken;

		Task<HttpResponseMessage> Post(object body) =>
			http.PostAsync(uri, JsonContent.Create(body), ct);
		object State(string? bomb, string? roundBomb) => new
		{
			map = new { mode = "competitive", name = "de_mirage", phase = "live", round = 5 },
			round = new { phase = "live", bomb = roundBomb },
			bomb = bomb is null ? null : new { state = bomb },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				state = new { health = 100 },
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
		};

		await Post(State("carried", null));
		await Post(State("planted", "b"));

		var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		GsiMatchEvent? planted = null;
		while (DateTimeOffset.UtcNow < deadline && planted is null)
		{
			await Task.Delay(50, ct);
			lock (seen)
			{
				planted = seen.FirstOrDefault(e => e.EventId == GsiEventIds.BombPlanted);
			}
		}

		Assert.That(planted, Is.Not.Null);
		Assert.That(planted!.Payload["site"], Is.EqualTo("B"));
	}

	[Test]
	public async Task Top_level_round_wins_win_over_nested()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var ct = TestContext.CurrentContext.CancellationToken;

		await http.PostAsync(uri, JsonContent.Create(new
		{
			map = new
			{
				mode = "competitive",
				name = "de_mirage",
				phase = "live",
				round = 2,
				round_wins = new Dictionary<string, object> { ["1"] = "ct_win_elimination" },
			},
			map_round_wins = new Dictionary<string, object> { ["1"] = "t_win_elimination" },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				state = new { health = 100 },
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
		}), ct);

		var snapshot = await WaitForSnapshotAsync(gsi, ct);

		Assert.That(snapshot.RoundHistory, Is.EqualTo("T"));
	}

	[Test]
	public async Task Allgrenades_block_counts_like_grenades()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var ct = TestContext.CurrentContext.CancellationToken;

		await http.PostAsync(uri, JsonContent.Create(new
		{
			map = new { mode = "competitive", name = "de_mirage", phase = "live", round = 5 },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				state = new { health = 100 },
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
			allgrenades = new Dictionary<string, object>
			{
				["1"] = new { owner = "1", type = "smoke", lifetime = 18.0, effecttime = 12.0 },
			},
		}), ct);

		var snapshot = await WaitForSnapshotAsync(gsi, ct);

		Assert.That(snapshot.SmokesActive, Is.EqualTo(1));
	}

	[Test]
	public async Task Empty_probe_posts_do_not_fake_a_connection()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var ct = TestContext.CurrentContext.CancellationToken;

		await http.PostAsync(uri, JsonContent.Create(new { }), ct);
		await Task.Delay(500, ct);

		Assert.That(gsi.Snapshot().Connected, Is.False);
	}

	private static async Task<GsiSnapshot> WaitForSnapshotAsync(GsiService gsi, CancellationToken cancellationToken)
	{
		var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
		GsiSnapshot snapshot;
		do
		{
			await Task.Delay(50, cancellationToken);
			snapshot = gsi.Snapshot();
		}
		while (!snapshot.Connected && DateTimeOffset.UtcNow < deadline);

		Assert.That(snapshot.Connected, Is.True);
		return snapshot;
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
		Assert.That(snapshot.Weapon, Is.EqualTo("M4A4"));
		Assert.That(snapshot.AmmoClip, Is.EqualTo(20));
		Assert.That(snapshot.Kills, Is.EqualTo(5));
		gsi.Stop();
		Assert.That(gsi.IsListening, Is.False);
	}

	[Test]
	public async Task Late_join_backfills_completed_rounds_into_match_clock()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var ct = TestContext.CurrentContext.CancellationToken;

		// First packet already shows 15 decided rounds: the clock must estimate
		// those instead of starting near zero.
		await http.PostAsync(uri, JsonContent.Create(new
		{
			map = new { mode = "casual", name = "de_mirage", phase = "live", round = 16, team_ct = new { score = 8 }, team_t = new { score = 7 } },
			round = new { phase = "live" },
			player = new { steamid = "76561198000000000", name = "Me", team = "CT", state = new { health = 100 } },
		}), ct);

		var snapshot = await WaitForSnapshotAsync(gsi, ct);

		Assert.That(snapshot.MatchElapsed, Is.GreaterThanOrEqualTo(1400.0));
		Assert.That(snapshot.MatchElapsed, Is.LessThan(1800.0));
	}

	[Test]
	public async Task Fresh_match_starts_clock_near_zero()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var ct = TestContext.CurrentContext.CancellationToken;

		await http.PostAsync(uri, JsonContent.Create(new
		{
			map = new { mode = "casual", name = "de_mirage", phase = "live", round = 0, team_ct = new { score = 0 }, team_t = new { score = 0 } },
			round = new { phase = "live" },
			player = new { steamid = "76561198000000000", name = "Me", team = "CT", state = new { health = 100 } },
		}), ct);

		var snapshot = await WaitForSnapshotAsync(gsi, ct);

		Assert.That(snapshot.MatchElapsed, Is.LessThan(30.0));
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
		Assert.That(kill!.Payload["weapon"], Is.EqualTo("AK-47"));

		Assert.That(gsi.Snapshot().SessionKills, Is.EqualTo(2));
		Assert.That(gsi.Snapshot().SessionDeaths, Is.EqualTo(1));
		gsi.ResetSessionStats();
		Assert.That(gsi.Snapshot().SessionKills, Is.EqualTo(0));
	}

	[Test]
	public async Task Position_bomb_and_carrier_surface()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";

		var response = await http.PostAsync(uri, JsonContent.Create(new
		{
			map = new { mode = "competitive", name = "de_mirage", phase = "live", round = 5 },
			round = new { phase = "live" },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				position = "100.5, -200.25, 64",
				state = new { health = 100 },
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
			allplayers = new Dictionary<string, object>
			{
				["76561198000000001"] = new { name = "Carrier", team = "T" },
			},
			bomb = new { state = "carried", player = "76561198000000001", countdown = "35.0" },
		}), TestContext.CurrentContext.CancellationToken);

		Assert.That((int)response.StatusCode, Is.EqualTo(200));
		var snapshot = await WaitForAsync(
			() => gsi.Snapshot().HasPosition,
			TestContext.CurrentContext.CancellationToken);

		Assert.That(snapshot, Is.True);
		var state = gsi.Snapshot();
		Assert.That(state.PosX, Is.EqualTo(100.5));
		Assert.That(state.PosY, Is.EqualTo(-200.25));
		Assert.That(state.PosZ, Is.EqualTo(64.0));
		Assert.That(state.BombCountdown, Is.EqualTo(35.0));
		Assert.That(state.BombCarrier, Is.EqualTo("Carrier"));
	}

	[Test]
	public async Task Malformed_position_stays_unavailable()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.Start(0, null);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";

		await http.PostAsync(uri, JsonContent.Create(new
		{
			map = new { name = "de_mirage", phase = "live" },
			player = new { steamid = "1", name = "Me", position = "somewhere" },
		}), TestContext.CurrentContext.CancellationToken);

		await Task.Delay(500, TestContext.CurrentContext.CancellationToken);
		Assert.That(gsi.Snapshot().HasPosition, Is.False);
	}

	private static async Task<bool> WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
	{
		var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
		while (DateTimeOffset.UtcNow < deadline)
		{
			if (condition())
			{
				return true;
			}

			await Task.Delay(50, cancellationToken);
		}

		return condition();
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
	public void Position_helper_uses_plain_getpos()
	{
		Assert.That(GsiConfig.PositionCommands(), Is.EqualTo("getpos\n"));
	}

	[Test]
	public void Cfg_renders_with_and_without_token()
	{
		var plain = GsiConfig.Render(32075, null);
		Assert.That(plain, Does.Contain("\"uri\"          \"http://127.0.0.1:32075/gsi\""));
		Assert.That(plain, Does.Not.Contain("\"auth\""));
		Assert.That(plain, Does.Contain("\"player_match_stats\" \"1\""));
		Assert.That(plain, Does.Contain("\"player_position\" \"1\""));
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

	[Test]
	public void Place_tokens_prettify()
	{
		Assert.That(PlaceStore.Prettify("BombsiteA"), Is.EqualTo("Bombsite A"));
		Assert.That(PlaceStore.Prettify("CTSpawn"), Is.EqualTo("CT Spawn"));
		Assert.That(PlaceStore.Prettify("TRamp"), Is.EqualTo("T Ramp"));
		Assert.That(PlaceStore.Prettify("TopofMid"), Is.EqualTo("Top of Mid"));
		Assert.That(PlaceStore.Prettify("Middle"), Is.EqualTo("Middle"));
		Assert.That(PlaceStore.Prettify(""), Is.Empty);
	}

	[Test]
	public void Place_lookup_prefers_smallest_container_then_nearest()
	{
		var store = new StubPlaces(new Dictionary<string, IReadOnlyList<PlaceVolume>>
		{
			["de_test"] = [
				new PlaceVolume("BigZone", "BigZone", new Vector3(0, 0, 0), new Vector3(100, 100, 100)),
				new PlaceVolume("Site", "Site", new Vector3(10, 10, 0), new Vector3(20, 20, 50)),
			],
		});

		Assert.That(store.FindPlace("de_test", 15, 15, 10), Is.EqualTo("Site"));
		Assert.That(store.FindPlace("de_test", 70, 70, 10), Is.EqualTo("BigZone"));
		Assert.That(store.FindPlace("de_test", 5000, 5000, 0), Is.Null);
		Assert.That(store.FindPlace(null, 15, 15, 10), Is.Null);
		Assert.That(store.FindPlace("de_missing", 15, 15, 10), Is.Null);
	}

	private sealed class StubPlaces(Dictionary<string, IReadOnlyList<PlaceVolume>> maps) : PlaceStore(new LoggerConfiguration().CreateLogger())
	{
		public override IReadOnlyList<PlaceVolume> GetPlaces(string? mapName) =>
			mapName is not null && maps.TryGetValue(mapName, out var places) ? places : [];
	}

	[Test]
	public async Task Kill_event_carries_the_resolved_place()
	{
		var maps = new Dictionary<string, IReadOnlyList<PlaceVolume>>
		{
			["de_test"] = [
				new PlaceVolume("Middle", "Middle", new Vector3(-100, -100, -50), new Vector3(100, 100, 50)),
			],
		};
		using var gsi = new GsiService(TestLogger(), new StubPlaces(maps));
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
		var ct = TestContext.CurrentContext.CancellationToken;

		Task<HttpResponseMessage> Post(object body) => http.PostAsync(uri, JsonContent.Create(body), ct);
		await Post(new
		{
			map = new { name = "de_test", phase = "live" },
			player = new
			{
				steamid = "1",
				name = "Me",
				position = "0, 0, 0",
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
		});
		await Post(new
		{
			map = new { name = "de_test", phase = "live" },
			player = new
			{
				steamid = "1",
				name = "Me",
				position = "0, 0, 0",
				match_stats = new { kills = 1, assists = 0, deaths = 0, mvps = 0, score = 2 },
			},
		});

		var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		GsiMatchEvent? kill = null;
		while (DateTimeOffset.UtcNow < deadline && kill is null)
		{
			lock (seen)
			{
				kill = seen.FirstOrDefault(e => e.EventId == GsiEventIds.PlayerKill);
			}

			await Task.Delay(50, ct);
		}

		Assert.That(kill, Is.Not.Null);
		Assert.That(kill!.Payload["place"], Is.EqualTo("Middle"));
		Assert.That(gsi.Snapshot().PlaceName, Is.EqualTo("Middle"));
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
