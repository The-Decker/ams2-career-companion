namespace Companion.Core.Career;

/// <summary>
/// How hard the player's OWN accident-DNF was (character death &amp; injury, docs/dev/character-death-injury.md
/// §3.1). A raw player input the sim cannot re-derive, captured on the result envelope ONLY for the player's
/// own accident (<c>"a"</c>) DNF. It feeds ONLY the (Slice 3) injury roll, it never changes scoring or OPI.
/// Null on every non-accident DNF and every pre-feature save; serialized as a camelCase string when present.
/// </summary>
public enum AccidentSeverity
{
    /// <summary>A light shunt, rarely injury-sustaining.</summary>
    Light,

    /// <summary>A medium crash, the default when the player marks an accident.</summary>
    Medium,

    /// <summary>A heavy shunt, meaningfully more dangerous.</summary>
    Heavy,
}
