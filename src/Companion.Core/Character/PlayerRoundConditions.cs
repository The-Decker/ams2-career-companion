using Companion.Core.Packs;

namespace Companion.Core.Character;

/// <summary>
/// The season-relative race-length band persisted before a round is staged. Neutral is canonical
/// when the round has no positive lap count or sits exactly on the season median.
/// </summary>
public enum PlayerRoundLengthBand
{
    Neutral,
    Short,
    Long,
}

/// <summary>
/// Versioned pre-race INPUT facts for conditional player-car effects. The track id and derived
/// length band make a stale or tampered row fail validation against the pinned pack.
/// </summary>
public sealed record PlayerRoundConditionsInput
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public int ProgressionVersion { get; init; } = CharacterLevelProgression.Level300Version;
    public required int Round { get; init; }
    public required string TrackId { get; init; }
    public required bool IsWet { get; init; }
    public required PlayerRoundLengthBand LengthBand { get; init; }
}

/// <summary>
/// Pure preparation, validation, and condition-token projection for persisted pre-race facts.
/// Nothing here reads ambient weather or mutable application state: inference uses only the pinned
/// pack and race-length classification mirrors the replay fold exactly.
/// </summary>
public static class PlayerRoundConditions
{
    private static readonly HashSet<string> DryWeather = new(StringComparer.OrdinalIgnoreCase)
    {
        "Clear",
        "Light Cloud",
        "Medium Cloud",
        "Heavy Cloud",
        "Overcast",
        "Foggy",
        "Hazy",
    };

    private static readonly HashSet<string> WetWeather = new(StringComparer.OrdinalIgnoreCase)
    {
        "Light Rain",
        "Rain",
        "Heavy Rain",
        "Storm",
        "Thunderstorm",
    };

    public static PlayerRoundConditionsInput Prepare(SeasonPack pack, int round, bool isWet)
    {
        ArgumentNullException.ThrowIfNull(pack);
        PackRound packRound = FindRound(pack, round);

        return new PlayerRoundConditionsInput
        {
            Round = round,
            TrackId = packRound.Track.Id,
            IsWet = isWet,
            LengthBand = CanonicalLengthBand(pack, round),
        };
    }

    /// <summary>Rejects a row that is not the canonical payload for its pinned pack and key.</summary>
    public static void Validate(
        PlayerRoundConditionsInput input,
        SeasonPack pack,
        int journalRound)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(pack);

        if (input.Version != PlayerRoundConditionsInput.CurrentVersion)
            throw new InvalidOperationException(
                $"Unsupported player round-conditions version {input.Version}.");
        if (input.ProgressionVersion != CharacterLevelProgression.Level300Version)
            throw new InvalidOperationException(
                $"Player round conditions require progression version {CharacterLevelProgression.Level300Version}.");
        if (input.Round != journalRound)
            throw new InvalidOperationException(
                $"Player round-conditions row {journalRound} contains round {input.Round}.");

        PackRound packRound = FindRound(pack, input.Round);
        if (!string.Equals(input.TrackId, packRound.Track.Id, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Player round-conditions track '{input.TrackId}' does not match pinned track '{packRound.Track.Id}'.");

        PlayerRoundLengthBand canonical = CanonicalLengthBand(pack, input.Round);
        if (input.LengthBand != canonical)
            throw new InvalidOperationException(
                $"Player round-conditions length band {input.LengthBand} does not match canonical band {canonical}.");
    }

    /// <summary>
    /// Projects only the closed conditional-effect vocabulary: exactly one weather token and at
    /// most one season-relative distance token.
    /// </summary>
    public static IReadOnlySet<string> ActiveConditions(PlayerRoundConditionsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var conditions = new HashSet<string>(StringComparer.Ordinal)
        {
            input.IsWet ? "wetRound" : "dryRound",
        };

        switch (input.LengthBand)
        {
            case PlayerRoundLengthBand.Neutral:
                break;
            case PlayerRoundLengthBand.Short:
                conditions.Add("shortRace");
                break;
            case PlayerRoundLengthBand.Long:
                conditions.Add("longRace");
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown player round-conditions length band {(int)input.LengthBand}.");
        }

        return conditions;
    }

    /// <summary>
    /// Conservatively infers the authored race weather. Every scoring race must resolve from its
    /// own slots (or, when those are null, the setup-guide fallback) and all races must agree.
    /// Explicitly empty, blank, mixed, dynamic, and unknown weather remains a required user choice.
    /// </summary>
    public static bool? TryInferIsWet(SeasonPack pack, int round)
    {
        ArgumentNullException.ThrowIfNull(pack);
        PackRound? packRound = pack.Season.Rounds.FirstOrDefault(candidate => candidate.Round == round);
        if (packRound is null)
            return null;

        IReadOnlyList<string>? fallback = packRound.SetupGuide?.Session.WeatherSlots;
        if (packRound.Weekend is null)
            return ClassifyWeather(fallback);
        if (packRound.Weekend.Races.Count == 0)
            return null;

        bool? consensus = null;
        foreach (PackWeekendRace race in packRound.Weekend.Races)
        {
            bool? raceIsWet = ClassifyWeather(race.WeatherSlots ?? fallback);
            if (raceIsWet is null)
                return null;
            if (consensus is not null && consensus.Value != raceIsWet.Value)
                return null;
            consensus = raceIsWet;
        }

        return consensus;
    }

    private static bool? ClassifyWeather(IReadOnlyList<string>? slots)
    {
        if (slots is null || slots.Count == 0)
            return null;

        bool? classification = null;
        foreach (string? authoredSlot in slots)
        {
            if (string.IsNullOrWhiteSpace(authoredSlot))
                return null;

            string slot = authoredSlot.Trim();
            bool? slotIsWet = WetWeather.Contains(slot)
                ? true
                : DryWeather.Contains(slot)
                    ? false
                    : null;
            if (slotIsWet is null)
                return null;
            if (classification is not null && classification.Value != slotIsWet.Value)
                return null;
            classification = slotIsWet;
        }

        return classification;
    }

    private static PlayerRoundLengthBand CanonicalLengthBand(SeasonPack pack, int round)
    {
        var laps = pack.Season.Rounds
            .Select(candidate => candidate.Laps)
            .Where(lapCount => lapCount > 0)
            .OrderBy(lapCount => lapCount)
            .ToList();
        if (laps.Count == 0)
            return PlayerRoundLengthBand.Neutral;

        // Keep this arithmetic byte-for-byte equivalent to ReplayService.RaceLengthToken: for an
        // even calendar, average the two centre int lap counts as a double.
        double median = laps.Count % 2 == 1
            ? laps[laps.Count / 2]
            : (laps[laps.Count / 2 - 1] + laps[laps.Count / 2]) / 2.0;
        int thisLaps = pack.Season.Rounds.FirstOrDefault(candidate => candidate.Round == round)?.Laps ?? 0;
        if (thisLaps <= 0 || thisLaps == median)
            return PlayerRoundLengthBand.Neutral;
        return thisLaps > median ? PlayerRoundLengthBand.Long : PlayerRoundLengthBand.Short;
    }

    private static PackRound FindRound(SeasonPack pack, int round) =>
        pack.Season.Rounds.FirstOrDefault(candidate => candidate.Round == round)
        ?? throw new InvalidOperationException($"Pinned pack has no round {round}.");
}
