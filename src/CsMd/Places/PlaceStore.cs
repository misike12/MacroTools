using System.Collections.Concurrent;
using System.Numerics;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using CsMd.Gsi;
using Serilog;

namespace CsMd.Places;

public sealed record PlaceVolume(string Token, string Name, Vector3 Min, Vector3 Max)
{
	public double VolumeSize =>
		Math.Max(0, Max.X - Min.X) * Math.Max(0, Max.Y - Min.Y) * Math.Max(0, Max.Z - Min.Z);
}

// Map callouts ("Palace", "Bombsite A") read out of the game's own files: every
// tagged map ships env_cs_place volumes, either in its own VPK under the game's maps
// folder or nested inside a workshop item. Bounds come from the volumes' physics
// hulls (mesh bounds where a hull is missing), names are the mappers' own tokens,
// prettified for display. Extracted once per map and matched by point-in-box, so the
// deck can show where anyone is standing.
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

		mapName = mapName.Trim();
		var resolved = ResolveMap(mapName);
		if (resolved is null)
		{
			return [];
		}

		DateTime lastWrite;
		try
		{
			lastWrite = File.GetLastWriteTimeUtc(resolved.VpkPath);
		}
		catch (Exception)
		{
			return [];
		}

		if (_cache.TryGetValue(mapName, out var cached)
			&& string.Equals(cached.VpkPath, resolved.VpkPath, StringComparison.OrdinalIgnoreCase)
			&& cached.VpkWrite == lastWrite
			&& File.Exists(resolved.VpkPath))
		{
			return cached.Places;
		}

		var places = Extract(resolved);
		_cache[mapName] = new CachedMap(resolved.VpkPath, lastWrite, places);
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

	private ResolvedMap? ResolveMap(string mapName)
	{
		var shortName = mapName.Contains('/') ? mapName[(mapName.LastIndexOf('/') + 1)..] : mapName;
		var official = OfficialVpkPath(shortName);
		if (official is not null)
		{
			return new ResolvedMap(official, null);
		}

		return ResolveWorkshopMap(shortName);
	}

	private string? OfficialVpkPath(string mapName)
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

		var file = Path.GetFullPath(Path.Combine(csgo, "..", "maps", mapName + ".vpk"));
		try
		{
			if (!File.Exists(file))
			{
				return null;
			}

			using var probe = File.OpenRead(file);
			var probePackage = new Package();
			probePackage.SetFileName(file);
			try
			{
				probePackage.Read(probe);
			}
			catch (Exception)
			{
				return null;
			}

			return HasVents(probePackage) ? file : null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private ResolvedMap? ResolveWorkshopMap(string mapName)
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

		var workshop = WorkshopDirectory(csgo);
		string[] dirs;
		try
		{
			if (workshop is null || !Directory.Exists(workshop))
			{
				return null;
			}

			dirs = Directory.GetDirectories(workshop);
		}
		catch (Exception)
		{
			return null;
		}

		foreach (var dir in dirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
		{
			string[] vpks;
			try
			{
				vpks = Directory.GetFiles(dir, "*.vpk", SearchOption.TopDirectoryOnly);
			}
			catch (Exception)
			{
				continue;
			}

			foreach (var vpk in vpks.OrderByDescending(v => v.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase)))
			{
				try
				{
					using var package = OpenPackage(vpk);
					if (package is null)
					{
						continue;
					}

					var nested = FindNestedMap(package.Package, mapName);
					if (nested is not null)
					{
						return new ResolvedMap(vpk, nested);
					}
				}
				catch (Exception ex)
				{
					_logger.Debug(ex, "Workshop scan skipped.");
				}
			}
		}

		return null;
	}

	private static string? WorkshopDirectory(string csgoDirectory)
	{
		try
		{
			var current = Path.GetFullPath(csgoDirectory);
			for (var i = 0; i < 8 && current is not null; i++)
			{
				if (string.Equals(Path.GetFileName(current), "steamapps", StringComparison.OrdinalIgnoreCase))
				{
					return Path.Combine(current, "workshop", "content", "730");
				}

				current = Path.GetDirectoryName(current);
			}
		}
		catch (Exception)
		{
		}

		return null;
	}

	private List<PlaceVolume> Extract(ResolvedMap resolved)
	{
		try
		{
			using var outer = OpenPackage(resolved.VpkPath);
			if (outer is null)
			{
				return [];
			}

			if (resolved.NestedMap is null)
			{
				return ExtractFromPackage(outer.Package, null);
			}

			var nestedBytes = ReadFile(outer.Package, resolved.NestedMap);
			if (nestedBytes is null)
			{
				return [];
			}

			using var nested = OpenBytes(nestedBytes, resolved.VpkPath);
			return nested is null ? [] : ExtractFromPackage(nested.Package, outer.Package);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Place extraction failed.");
			return [];
		}
	}

	private sealed class OpenedPackage : IDisposable
	{
		public OpenedPackage(Package package, Stream stream)
		{
			Package = package;
			Stream = stream;
		}

		public Package Package { get; }

		private Stream Stream { get; }

		public void Dispose()
		{
			try
			{
				Stream.Dispose();
			}
			catch (Exception)
			{
			}
		}
	}

	private static OpenedPackage? OpenPackage(string vpkPath)
	{
		FileStream? stream = null;
		try
		{
			stream = File.OpenRead(vpkPath);
			var package = new Package();
			package.SetFileName(vpkPath);
			package.Read(stream);
			return new OpenedPackage(package, stream);
		}
		catch (Exception)
		{
			try
			{
				stream?.Dispose();
			}
			catch (Exception)
			{
			}

			return null;
		}
	}

	private static OpenedPackage? OpenBytes(byte[] vpkBytes, string fileName)
	{
		MemoryStream? stream = null;
		try
		{
			stream = new MemoryStream(vpkBytes, writable: false);
			var package = new Package();
			package.SetFileName(fileName);
			package.Read(stream);
			return new OpenedPackage(package, stream);
		}
		catch (Exception ex)
		{
			try
			{
				stream?.Dispose();
			}
			catch (Exception)
			{
			}

			return null;
		}
	}

	private static bool HasVents(Package package)
	{
		try
		{
			return package.Entries?.Any(entry =>
				entry.Key.Contains("vents_c", StringComparison.OrdinalIgnoreCase)) == true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static string? FindNestedMap(Package package, string mapName)
	{
		try
		{
			var entries = package.Entries;
			if (entries is null)
			{
				return null;
			}

			foreach (var entry in entries.SelectMany(e => e.Value))
			{
				var full = entry.GetFullPath();
				var file = full.Contains('/') ? full[(full.LastIndexOf('/') + 1)..] : full;
				if (file.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase)
					&& string.Equals(Path.GetFileNameWithoutExtension(file), mapName, StringComparison.OrdinalIgnoreCase))
				{
					return full;
				}
			}
		}
		catch (Exception)
		{
		}

		return null;
	}

	private static byte[]? ReadFile(Package package, string fullPath)
	{
		try
		{
			var entries = package.Entries;
			if (entries is null)
			{
				return null;
			}

			var match = entries
				.SelectMany(entry => entry.Value)
				.FirstOrDefault(entry => string.Equals(entry.GetFullPath(), fullPath, StringComparison.OrdinalIgnoreCase));
			if (match is null)
			{
				return null;
			}

			package.ReadEntry(match, out var raw);
			return raw;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private List<PlaceVolume> ExtractFromPackage(Package package, Package? fallbackModels)
	{
		var vents = new List<PackageEntry>();
		try
		{
			var entries = package.Entries;
			if (entries is null)
			{
				return [];
			}

			vents.AddRange(entries
				.Where(entry => entry.Key.Contains("vents_c", StringComparison.OrdinalIgnoreCase))
				.SelectMany(entry => entry.Value));
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Place vents lookup failed.");
			return [];
		}

		if (vents.Count == 0)
		{
			return [];
		}

		var models = new List<PackageEntry>();
		try
		{
			models.AddRange(package.Entries!
				.Where(entry => entry.Key.Contains("vmdl_c", StringComparison.OrdinalIgnoreCase))
				.SelectMany(entry => entry.Value));
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Place model lookup failed.");
		}

		var places = new List<PlaceVolume>();
		foreach (var vent in vents)
		{
			try
			{
				package.ReadEntry(vent, out var ventsRaw);
				using var ventsStream = new MemoryStream(ventsRaw);
				using var ventsResource = new Resource();
				ventsResource.Read(ventsStream);
				if (ventsResource.DataBlock is not EntityLump lump)
				{
					continue;
				}

				foreach (var entity in lump.GetEntities())
				{
					try
					{
						var volume = ReadPlace(package, fallbackModels, models, entity);
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
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Place vents entry skipped.");
			}
		}

		return places;
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
		Package? fallbackModels,
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
		byte[]? modelRaw = null;
		if (modelEntry is not null)
		{
			modelRaw = ReadFile(package, modelEntry.GetFullPath());
		}

		modelRaw ??= fallbackModels is null ? null : ReadFile(fallbackModels, model);
		if (modelRaw is null)
		{
			return null;
		}

		var bounds = ModelBounds(modelRaw);
		if (bounds is null)
		{
			return null;
		}

		var origin = entity.GetVector3Property("origin", System.Numerics.Vector3.Zero);
		var (min, max) = bounds.Value;
		return new PlaceVolume(token, Prettify(token), min + origin, max + origin);
	}

	private static (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max)? ModelBounds(byte[] modelRaw)
	{
		try
		{
			using var modelStream = new MemoryStream(modelRaw);
			using var modelResource = new Resource();
			modelResource.Read(modelStream);
			if (modelResource.GetBlockByType(BlockType.PHYS) is not PhysAggregateData phys)
			{
				return null;
			}

			var found = false;
			var min = new System.Numerics.Vector3(float.MaxValue);
			var max = new System.Numerics.Vector3(float.MinValue);
			foreach (var part in phys.Parts)
			{
				foreach (var hull in part.Shape.Hulls ?? [])
				{
					Union(ref found, ref min, ref max, hull.Shape.Min, hull.Shape.Max);
				}

				foreach (var mesh in part.Shape.Meshes ?? [])
				{
					Union(ref found, ref min, ref max, mesh.Shape.Min, mesh.Shape.Max);
				}

				foreach (var sphere in part.Shape.Spheres ?? [])
				{
					var radius = new System.Numerics.Vector3(sphere.Shape.Radius);
					Union(ref found, ref min, ref max, sphere.Shape.Center - radius, sphere.Shape.Center + radius);
				}

				foreach (var capsule in part.Shape.Capsules ?? [])
				{
					foreach (var center in capsule.Shape.Center ?? [])
					{
						var radius = new System.Numerics.Vector3(capsule.Shape.Radius);
						Union(ref found, ref min, ref max, center - radius, center + radius);
					}
				}
			}

			return found && min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z ? (min, max) : null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static void Union(
		ref bool found,
		ref System.Numerics.Vector3 min,
		ref System.Numerics.Vector3 max,
		System.Numerics.Vector3 partMin,
		System.Numerics.Vector3 partMax)
	{
		if (partMax.X <= partMin.X || partMax.Y <= partMin.Y || partMax.Z <= partMin.Z)
		{
			return;
		}

		found = true;
		min = new System.Numerics.Vector3(
			Math.Min(min.X, partMin.X), Math.Min(min.Y, partMin.Y), Math.Min(min.Z, partMin.Z));
		max = new System.Numerics.Vector3(
			Math.Max(max.X, partMax.X), Math.Max(max.Y, partMax.Y), Math.Max(max.Z, partMax.Z));
	}

	private static bool Contains(PlaceVolume place, System.Numerics.Vector3 point, float slack) =>
		point.X >= place.Min.X - slack && point.X <= place.Max.X + slack
			&& point.Y >= place.Min.Y - slack && point.Y <= place.Max.Y + slack
			&& point.Z >= place.Min.Z - slack && point.Z <= place.Max.Z + slack;

	private static double DistanceXy(PlaceVolume place, System.Numerics.Vector3 point)
	{
		var dx = point.X < place.Min.X ? place.Min.X - point.X : point.X > place.Max.X ? point.X - place.Max.X : 0;
		var dy = point.Y < place.Min.Y ? place.Min.Y - point.Y : point.Y > place.Max.Y ? point.Y - place.Max.Y : 0;
		return Math.Sqrt(dx * dx + dy * dy);
	}

	private sealed record ResolvedMap(string VpkPath, string? NestedMap);

	private sealed record CachedMap(string VpkPath, DateTime VpkWrite, IReadOnlyList<PlaceVolume> Places);
}

