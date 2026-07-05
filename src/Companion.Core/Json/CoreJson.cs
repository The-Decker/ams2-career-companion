using System.Text.Json;
using System.Text.Json.Serialization;
using Companion.Core.Numerics;

namespace Companion.Core.Json;

/// <summary>Serializes <see cref="Rational"/> as its canonical string form ("3", "1/7"),
/// which is what season packs and test fixtures store.</summary>
public sealed class RationalJsonConverter : JsonConverter<Rational>
{
    public override Rational Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => Rational.Parse(reader.GetString()!),
            JsonTokenType.Number => new Rational(reader.GetInt64()),
            _ => throw new JsonException($"Cannot read a Rational from a {reader.TokenType} token."),
        };

    public override void Write(Utf8JsonWriter writer, Rational value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>Shared serializer options for every JSON artifact the Core owns
/// (points systems, season packs, test fixtures).</summary>
public static class CoreJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters =
        {
            new RationalJsonConverter(),
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };
}
