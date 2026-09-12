using System.Collections.Concurrent;
using System.Numerics;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;
using CsMd.Gsi;
using Serilog;

namespace CsMd.Places;

public sealed record PlaceVolume(string Token, string Name, Vector3 Min, Vector3 Max)
{
	public double VolumeSize =>
		Math.Max(0, Max.X - Min.X) * Math.Max(0, Max.Y - Min.Y) * Math.Max(0, Max.Z - Min.Z);
}

// Map callouts ("Palace", "Bombsite A") read out of the game's own files: every
// official map ships env_cs_place volumes in its VPK, and those carry the mapper's
// place names plus world-space bounds. Extracted once per map (re-checked when the
// VPK changes) and matched by point-in-box, so the deck can show where anyone is.
public class PlaceStore
{
	private static readonly Dictionary<string, string> NameOverrides = new(StringComparer.Ordinal)
	{
		["TopofMid"] = "Top of Mid",
	};

	private readonly ILogger _logger;
	private readonly Func<string?> _csgoDirectory;
	private readonly ConcurrentDictionary<string, CachedMap> _cache = new(StringComparer.OrdinalIgnoreCase);

	public PlaceStore(ILogger logger)
		: this(logger, GsiConfig.FindCsDirectory)
	{
	}

	internal PlaceStore(ILogger logger, Func<string?> csgoDirectory)
	{
		_logger = logger.ForContext<PlaceStore>();
		_csgoDirectory = csgoDirectory;
	}

	public virtual IReadOnlyList<PlaceVolume> GetPlaces(string? mapName)
	{
		if (string.IsNullOrWhiteSpace(mapName))
		{
			return [];
		}

		var vpkPath = VpkPathFor(mapName);
		if (vpkPath is null)
		{
			return [];
		}

		DateTime lastWrite;
		try
		{
			lastWrite = File.GetLastWriteTimeUtc(vpkPath);
		}
		catch (Exception)
		{
			return [];
		}

		if (_cache.TryGetValue(mapName, out var cached) && cached.VpkWrite == lastWrite)
		{
			return cached.Places;
		}

		var places = Extract(vpkPath);
		_cache[mapName] = new CachedMap(lastWrite, places);
		return places;
	}

	public virtual string? FindPlace(string? mapName, double x, double y, double z)
	{
		var places = GetPlaces(mapName);
		if (places.Count == 0)
		{
			return null;
		}

		var point = new Vector3((float)x, (float)y, (float)z);
		PlaceVolume? best = null;
		foreach (var place in places.OrderBy(p => p.VolumeSize))
		{
			if (Contains(place, point, 8))
			{
				best ??= place;
				break;
			}
		}

		if (best is not null)
		{
			return best.Name;
		}

		PlaceVolume? nearest = null;
		var nearestDistance = 400.0;
		foreach (var place in places)
		{
			var distance = DistanceXy(place, point);
			if (distance <= nearestDistance)
			{
				nearestDistance = distance;
				nearest = place;
			}
		}

		return nearest?.Name;
	}

	public static string Prettify(string token)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return string.Empty;
		}

		token = token.Trim();
		if (NameOverrides.TryGetValue(token, out var known))
		{
			return known;
		}

		var builder = new System.Text.StringBuilder();
		for (var i = 0; i < token.Length; i++)
		{
			var ch = token[i];
			var previous = i > 0 ? token[i - 1] : '\0';
			var next = i + 1 < token.Length ? token[i + 1] : '\0';
			if (i > 0 && char.IsUpper(ch)
				&& (char.IsLower(previous) || char.IsDigit(previous)
					|| (char.IsUpper(previous) && char.IsLower(next))))
			{
				builder.Append(' ');
			}

			builder.Append(ch);
		}

		return builder.ToString();
	}

	private string? VpkPathFor(string mapName)
	{
		string? csgo;
		try
		{
			csgo = _csgoDirectory();
		}
		catch (Exception)
		{
			return null;
		}

		if (string.IsNullOrWhiteSpace(csgo))
		{
			return null;
		}

		var maps = Path.GetFullPath(Path.Combine(csgo, "..", "maps"));
		var file = Path.Combine(maps, mapName.Trim() + ".vpk");
		try
		{
			return File.Exists(file) ? file : null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private List<PlaceVolume> Extract(string vpkPath)
	{
		try
		{
			using var package = new Package();
			package.SetFileName(vpkPath);
			using var stream = File.OpenRead(vpkPath);
			package.Read(stream);

			var entries = package.Entries;
			if (entries is null)
			{
				return [];
			}

			var vents = entries
				.Where(entry => entry.Key.Contains("vents_c", StringComparison.OrdinalIgnoreCase))
				.SelectMany(entry => entry.Value)
				.FirstOrDefault();
			if (vents is null)
			{
				return [];
			}

			package.ReadEntry(vents, out var ventsRaw);
			using var ventsStream = new MemoryStream(ventsRaw);
			using var ventsResource = new Resource();
			ventsResource.Read(ventsStream);
			if (ventsResource.DataBlock is not EntityLump lump)
			{
				return [];
			}

			var models = entries
				.Where(entry => entry.Key.Contains("vmdl_c", StringComparison.OrdinalIgnoreCase))
				.SelectMany(entry => entry.Value)
				.ToList();

			var places = new List<PlaceVolume>();
			foreach (var entity in lump.GetEntities())
			{
				try
				{
					var volume = ReadPlace(package, models, entity);
					if (volume is not null)
					{
						places.Add(volume);
					}
				}
				catch (Exception ex)
				{
					_logger.Debug(ex, "Place entity skipped.");
				}
			}

			return places;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Place extraction failed.");
			return [];
		}
	}

	private static string? EntityText(EntityLump.Entity entity, string name)
	{
		try
		{
			if (!entity.Keys.Contains(name))
			{
				return null;
			}

			return entity[name]?.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static PlaceVolume? ReadPlace(
		Package package,
		IReadOnlyList<PackageEntry> models,
		EntityLump.Entity entity)
	{
		if (EntityText(entity, "classname") != "env_cs_place")
		{
			return null;
		}

		var token = EntityText(entity, "place_name");
		var model = EntityText(entity, "model")?.Replace("vmdl", "vmdl_c").Replace("\\", "/");
		if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(model))
		{
			return null;
		}

		var modelEntry = models.FirstOrDefault(e => string.Equals(e.GetFullPath(), model, StringComparison.OrdinalIgnoreCase));
		if (modelEntry is null)
		{
			return null;
		}

		package.ReadEntry(modelEntry, out var modelRaw);
		using var modelStream = new MemoryStream(modelRaw);
		using var modelResource = new Resource();
		modelResource.Read(modelStream);
		if (modelResource.GetBlockByType(BlockType.PHYS) is not PhysAggregateData phys
			|| phys.Parts.Length == 0)
		{
			return null;
		}

		var hulls = phys.Parts[0].Shape.Hulls;
		if (hulls is null || hulls.Length == 0)
		{
			return null;
		}

		var hull = hulls[0].Shape;
		if (hull.Max.X <= hull.Min.X || hull.Max.Y <= hull.Min.Y || hull.Max.Z <= hull.Min.Z)
		{
			return null;
		}

		var origin = entity.GetVector3Property("origin", Vector3.Zero);
		return new PlaceVolume(token, Prettify(token), hull.Min + origin, hull.Max + origin);
	}

	private static bool Contains(PlaceVolume place, Vector3 point, float slack) =>
		point.X >= place.Min.X - slack && point.X <= place.Max.X + slack
			&& point.Y >= place.Min.Y - slack && point.Y <= place.Max.Y + slack
			&& point.Z >= place.Min.Z - slack && point.Z <= place.Max.Z + slack;

	private static double DistanceXy(PlaceVolume place, Vector3 point)
	{
		var dx = point.X < place.Min.X ? place.Min.X - point.X : point.X > place.Max.X ? point.X - place.Max.X : 0;
		var dy = point.Y < place.Min.Y ? place.Min.Y - point.Y : point.Y > place.Max.Y ? point.Y - place.Max.Y : 0;
		return Math.Sqrt(dx * dx + dy * dy);
	}

	private sealed record CachedMap(DateTime VpkWrite, IReadOnlyList<PlaceVolume> Places);
}
