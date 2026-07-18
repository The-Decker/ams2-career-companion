using System.Text.Json;
using Companion.Core.Json;

namespace Companion.Data;

/// <summary>CoreJson conventions (camelCase, enums as camelCase strings, Rational as string)
/// but single-line, so a state blob or journal delta is one readable DB cell. Serialization
/// output is deterministic for the DTO shapes the Data layer stores, the same options are
/// used when regenerating journal deltas during replay, which is what makes the byte-compare
/// meaningful.</summary>
internal static class DataJson
{
    public static readonly JsonSerializerOptions Cell = new(CoreJson.Options)
    {
        WriteIndented = false,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Cell);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Cell)
        ?? throw new InvalidDataException(
            $"A stored {typeof(T).Name} cell deserialized to null, the career file is damaged.");
}
