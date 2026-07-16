using Companion.Core.Determinism;

namespace Companion.Core.Career;

/// <summary>
/// Non-graphic, deterministic injury descriptions for the medical record and injury coverage.
/// A pure DISPLAY projection of an already-persisted accident outcome: the pick hashes the
/// outcome's own identity (season/round/outcome), drawing nothing from any RNG stream, so it can
/// never perturb the fold or replay — reopening the career always describes the same injury the
/// same way. In-game simulation flavour only, never a medical claim.
/// </summary>
public static class InjuryFlavor
{
    private static readonly string[] MinorInjuries =
    [
        "bruised ribs",
        "a heavily bruised shoulder",
        "a concussion under observation",
        "a wrist sprain",
        "whiplash strain",
        "a deep leg contusion",
        "a sprained ankle",
        "bruised vertebrae",
    ];

    private static readonly string[] SeasonEndingInjuries =
    [
        "a broken leg",
        "a back injury needing months of rest",
        "a fractured wrist and recovery programme",
        "a shoulder injury requiring surgery",
        "chest injuries needing a long convalescence",
    ];

    /// <summary>The deterministic description for one persisted accident outcome, or empty for
    /// outcomes that carry no injury description (none / death — a fatality is never captioned
    /// with clinical detail).</summary>
    public static string Describe(string outcome, int seasonOrdinal, int round)
    {
        string[] table = outcome switch
        {
            "minorInjury" => MinorInjuries,
            "seasonEnding" => SeasonEndingInjuries,
            _ => [],
        };
        if (table.Length == 0)
        {
            return "";
        }

        ulong hash = StableHash.Fnv1a64(
            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"injury-flavor|{seasonOrdinal}|{round}|{outcome}"));
        return table[(int)(hash % (ulong)table.Length)];
    }
}
