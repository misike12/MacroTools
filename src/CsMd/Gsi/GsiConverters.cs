using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CsMd.Gsi;

// Per-field tolerance for whatever the game actually posts. Without these, one
// mistyped value (a boolean sent as 1, a number sent as a string) fails the whole
// payload and every variable freezes. With them, that single field falls back to
// its default and the rest of the snapshot survives.
internal sealed class TolerantBoolConverter : JsonConverter<bool>
{
	public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType switch
		{
			JsonTokenType.True => true,
			JsonTokenType.False => false,
			JsonTokenType.Number when reader.TryGetInt32(out var number) => number != 0,
			JsonTokenType.String when bool.TryParse(reader.GetString(), out var text) => text,
			JsonTokenType.String when int.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var number) => number != 0,
			_ => false,
		};

	public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
		writer.WriteBooleanValue(value);
}

internal sealed class TolerantIntConverter : JsonConverter<int>
{
	public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType switch
		{
			JsonTokenType.Number when reader.TryGetInt32(out var number) => number,
			JsonTokenType.Number when reader.TryGetDouble(out var fuzzy) => (int)Math.Round(fuzzy),
			JsonTokenType.String when int.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var text) => text,
			JsonTokenType.String when double.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var fuzzy) => (int)Math.Round(fuzzy),
			JsonTokenType.True => 1,
			JsonTokenType.False => 0,
			_ => 0,
		};

	public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
		writer.WriteNumberValue(value);
}

internal sealed class TolerantLongConverter : JsonConverter<long>
{
	public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType switch
		{
			JsonTokenType.Number when reader.TryGetInt64(out var number) => number,
			JsonTokenType.Number when reader.TryGetDouble(out var fuzzy) => (long)Math.Round(fuzzy),
			JsonTokenType.String when long.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var text) => text,
			JsonTokenType.String when double.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var fuzzy) => (long)Math.Round(fuzzy),
			_ => 0,
		};

	public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
		writer.WriteNumberValue(value);
}

internal sealed class TolerantDoubleConverter : JsonConverter<double>
{
	public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType switch
		{
			JsonTokenType.Number when reader.TryGetDouble(out var number) => number,
			JsonTokenType.String when double.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var text) => text,
			JsonTokenType.True => 1,
			JsonTokenType.False => 0,
			_ => 0,
		};

	public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
		writer.WriteNumberValue(value);
}

internal sealed class TolerantStringConverter : JsonConverter<string?>
{
	public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType switch
		{
			JsonTokenType.Null => null,
			JsonTokenType.String => reader.GetString(),
			JsonTokenType.Number when reader.TryGetInt64(out var number) => number.ToString(CultureInfo.InvariantCulture),
			JsonTokenType.Number when reader.TryGetDouble(out var fuzzy) => fuzzy.ToString(CultureInfo.InvariantCulture),
			JsonTokenType.True => "true",
			JsonTokenType.False => "false",
			_ => null,
		};

	public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
	{
		if (value is null)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteStringValue(value);
		}
	}
}
