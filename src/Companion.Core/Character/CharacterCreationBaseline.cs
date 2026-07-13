using System.Text.Json.Serialization;

namespace Companion.Core.Character;

/// <summary>The lossless v2 reset target captured at character creation. Talent stats and meta stats
/// stay separate so a reset restores identity exactly instead of reverse-applying clamped upgrades.</summary>
public sealed record CharacterCreationBaseline
{
    public required IReadOnlyDictionary<string, double> Stats { get; init; }
    public required IReadOnlyDictionary<string, double> Meta { get; init; }
    public required IReadOnlyList<string> TraitIds { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ChosenFlavor { get; init; }

    public bool Equals(CharacterCreationBaseline? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return string.Equals(ChosenFlavor, other.ChosenFlavor, StringComparison.Ordinal)
            && TraitIds.SequenceEqual(other.TraitIds)
            && DictionaryEqual(Stats, other.Stats)
            && DictionaryEqual(Meta, other.Meta);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ChosenFlavor, StringComparer.Ordinal);
        foreach (string id in TraitIds)
            hash.Add(id, StringComparer.Ordinal);
        AddDictionary(ref hash, Stats);
        AddDictionary(ref hash, Meta);
        return hash.ToHashCode();
    }

    private static bool DictionaryEqual(
        IReadOnlyDictionary<string, double> left,
        IReadOnlyDictionary<string, double> right)
    {
        if (left.Count != right.Count)
            return false;
        foreach (var (key, value) in left)
            if (!right.TryGetValue(key, out double other) || other != value)
                return false;
        return true;
    }

    private static void AddDictionary(ref HashCode hash, IReadOnlyDictionary<string, double> values)
    {
        foreach (var (key, value) in values.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(value);
        }
    }
}
