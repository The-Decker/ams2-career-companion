using System.Text.Json.Serialization;
using Companion.Core.Character;

namespace Companion.Core.Career;

/// <summary>Stable serialized ids for the three Alpha 1.0 career experiences. These are strings,
/// rather than an enum, so an unknown save value is rejected instead of being accepted as a numeric
/// enum value.</summary>
public static class CareerExperienceModes
{
    public const string GrandPrixDynasty = "grandPrixDynasty";
    public const string Smgp = "smgp";
    public const string RacingPassport = "racingPassport";

    public static bool IsKnown(string? mode) => mode is GrandPrixDynasty or Smgp or RacingPassport;

    public static bool IsBoundedCampaign(string? mode) => mode is GrandPrixDynasty or Smgp;
}

/// <summary>One immutable occurrence in a bounded campaign. The round count is stored with the
/// pack identity so the XP scale can be audited without rereading a mutable pack directory.</summary>
public sealed record PinnedCampaignSeason
{
    public required string PackId { get; init; }
    public required string PackVersion { get; init; }
    public required string Sha256 { get; init; }
    public required int Year { get; init; }
    public required int ChampionshipRoundCount { get; init; }
}

/// <summary>The creation-time progression horizon for Dynasty and SMGP. Membership, ordering and
/// rational XP scale are saved once and never rediscovered from later pack installs.</summary>
public sealed record CampaignProgressionPlan
{
    public const int CurrentVersion = 1;
    public const long MasteryReferenceXp = 15_680;

    public int Version { get; init; } = CurrentVersion;
    public required string Mode { get; init; }
    public required int StartYear { get; init; }
    public required int EndYear { get; init; }
    public required IReadOnlyList<PinnedCampaignSeason> PinnedSeasonSequence { get; init; }
    public required int TotalSeasons { get; init; }
    public required int MasterySeason { get; init; }
    public required long PlannedReferenceXp { get; init; }
    public required long XpScaleNumerator { get; init; }
    public required long XpScaleDenominator { get; init; }
    public required int MaxLevel { get; init; }

    /// <summary>Builds a normalized, reduced plan from an explicit ordered sequence. The caller
    /// decides membership once; this method performs no discovery or I/O.</summary>
    public static CampaignProgressionPlan Create(
        string mode,
        int startYear,
        int endYear,
        IEnumerable<PinnedCampaignSeason> pinnedSeasonSequence)
    {
        ArgumentNullException.ThrowIfNull(pinnedSeasonSequence);
        var sequence = pinnedSeasonSequence
            .Select(s => s with { })
            .ToArray();
        if (sequence.Length == 0)
            throw new ArgumentException("A bounded campaign needs at least one pinned season.", nameof(pinnedSeasonSequence));

        long plannedReferenceXp = PlannedReference(sequence);
        long gcd = GreatestCommonDivisor(MasteryReferenceXp, plannedReferenceXp);
        var plan = new CampaignProgressionPlan
        {
            Mode = mode,
            StartYear = startYear,
            EndYear = endYear,
            PinnedSeasonSequence = Array.AsReadOnly(sequence),
            TotalSeasons = sequence.Length,
            MasterySeason = Math.Max(1, sequence.Length - 1),
            PlannedReferenceXp = plannedReferenceXp,
            XpScaleNumerator = MasteryReferenceXp / gcd,
            XpScaleDenominator = plannedReferenceXp / gcd,
            MaxLevel = CharacterLevelProgression.Level300Max,
        };
        plan.Validate();
        return plan;
    }

    /// <summary>Creates SMGP's 17 ordinal seasons over the one pinned replica pack.</summary>
    public static CampaignProgressionPlan CreateSmgp(PinnedCampaignSeason season)
    {
        ArgumentNullException.ThrowIfNull(season);
        var sequence = Enumerable.Range(0, Companion.Core.Smgp.SmgpRules.CampaignSeasons)
            .Select(offset => season with { Year = checked(season.Year + offset) })
            .ToArray();
        return Create(
            CareerExperienceModes.Smgp,
            season.Year,
            sequence[^1].Year,
            sequence);
    }

    /// <summary>Rejects a corrupt or hand-authored plan before it can become a new career input.</summary>
    public void Validate()
    {
        if (Version != CurrentVersion)
            throw new NotSupportedException(
                $"Campaign progression plan version {Version} is not supported by this build.");
        if (!CareerExperienceModes.IsBoundedCampaign(Mode))
            throw new InvalidOperationException(
                $"Campaign progression mode '{Mode}' is not a supported bounded campaign.");
        if (PinnedSeasonSequence is null || PinnedSeasonSequence.Count == 0)
            throw new InvalidOperationException("A bounded campaign needs at least one pinned season.");
        if (StartYear > EndYear)
            throw new InvalidOperationException("Campaign start year must not be after its end year.");

        foreach (var season in PinnedSeasonSequence)
        {
            if (season is null)
                throw new InvalidOperationException("Pinned campaign seasons cannot contain null entries.");
            if (string.IsNullOrWhiteSpace(season.PackId) || string.IsNullOrWhiteSpace(season.PackVersion))
                throw new InvalidOperationException("Every pinned campaign season needs a pack id and version.");
            if (string.IsNullOrEmpty(season.Sha256) ||
                season.Sha256.Length != 64 ||
                season.Sha256.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidOperationException("Every pinned campaign season needs a 64-digit SHA-256 hash.");
            if (season.ChampionshipRoundCount <= 0)
                throw new InvalidOperationException("Every pinned campaign season needs at least one championship round.");
            if (season.Year < StartYear || season.Year > EndYear)
                throw new InvalidOperationException(
                    $"Pinned season {season.Year} is outside the campaign range {StartYear}-{EndYear}.");
        }

        if (PinnedSeasonSequence[0].Year != StartYear)
            throw new InvalidOperationException("The first pinned season must match the campaign start year.");
        for (int i = 1; i < PinnedSeasonSequence.Count; i++)
            if (PinnedSeasonSequence[i].Year <= PinnedSeasonSequence[i - 1].Year)
                throw new InvalidOperationException("Pinned campaign seasons must be in strictly increasing year order.");

        if (Mode == CareerExperienceModes.GrandPrixDynasty && EndYear != 2020)
            throw new InvalidOperationException("Grand Prix Dynasty's bounded historical horizon ends in 2020.");
        if (Mode == CareerExperienceModes.Smgp)
        {
            if (PinnedSeasonSequence.Count != Companion.Core.Smgp.SmgpRules.CampaignSeasons)
                throw new InvalidOperationException(
                    $"SMGP needs exactly {Companion.Core.Smgp.SmgpRules.CampaignSeasons} pinned seasons.");
            string packId = PinnedSeasonSequence[0].PackId;
            string version = PinnedSeasonSequence[0].PackVersion;
            string sha256 = PinnedSeasonSequence[0].Sha256;
            if (PinnedSeasonSequence.Any(s =>
                    !string.Equals(s.PackId, packId, StringComparison.Ordinal) ||
                    !string.Equals(s.PackVersion, version, StringComparison.Ordinal) ||
                    !string.Equals(s.Sha256, sha256, StringComparison.OrdinalIgnoreCase) ||
                    s.ChampionshipRoundCount != 16))
            {
                throw new InvalidOperationException(
                    "Every SMGP campaign season must reuse the same 16-round pinned replica pack.");
            }
            if (EndYear != checked(StartYear + Companion.Core.Smgp.SmgpRules.CampaignSeasons - 1) ||
                PinnedSeasonSequence[^1].Year != EndYear)
            {
                throw new InvalidOperationException("SMGP's 17 ordinal seasons must occupy one consecutive campaign range.");
            }
            for (int i = 1; i < PinnedSeasonSequence.Count; i++)
                if (PinnedSeasonSequence[i].Year != checked(PinnedSeasonSequence[i - 1].Year + 1))
                    throw new InvalidOperationException("SMGP campaign season years must be consecutive.");
        }

        if (TotalSeasons != PinnedSeasonSequence.Count)
            throw new InvalidOperationException("Plan totalSeasons must equal the pinned sequence count.");
        if (MasterySeason != Math.Max(1, TotalSeasons - 1))
            throw new InvalidOperationException("Plan masterySeason must be max(1, totalSeasons - 1).");
        if (MaxLevel != CharacterLevelProgression.Level300Max)
            throw new InvalidOperationException(
                $"Version-2 campaign plans must cap at level {CharacterLevelProgression.Level300Max}.");

        long expectedReference = PlannedReference(PinnedSeasonSequence);
        if (PlannedReferenceXp != expectedReference)
            throw new InvalidOperationException("Plan plannedReferenceXp does not match its pinned rounds.");
        long gcd = GreatestCommonDivisor(MasteryReferenceXp, expectedReference);
        if (XpScaleNumerator != MasteryReferenceXp / gcd ||
            XpScaleDenominator != expectedReference / gcd)
        {
            throw new InvalidOperationException(
                "Plan XP scale must be the reduced rational 15680/plannedReferenceXp.");
        }
    }

    public bool Equals(CampaignProgressionPlan? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return string.Equals(Mode, other.Mode, StringComparison.Ordinal)
            && Version == other.Version
            && StartYear == other.StartYear
            && EndYear == other.EndYear
            && TotalSeasons == other.TotalSeasons
            && MasterySeason == other.MasterySeason
            && PlannedReferenceXp == other.PlannedReferenceXp
            && XpScaleNumerator == other.XpScaleNumerator
            && XpScaleDenominator == other.XpScaleDenominator
            && MaxLevel == other.MaxLevel
            && PinnedSeasonSequence.SequenceEqual(other.PinnedSeasonSequence);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Version);
        hash.Add(Mode, StringComparer.Ordinal);
        hash.Add(StartYear);
        hash.Add(EndYear);
        hash.Add(TotalSeasons);
        hash.Add(MasterySeason);
        hash.Add(PlannedReferenceXp);
        hash.Add(XpScaleNumerator);
        hash.Add(XpScaleDenominator);
        hash.Add(MaxLevel);
        foreach (var season in PinnedSeasonSequence)
            hash.Add(season);
        return hash.ToHashCode();
    }

    private static long PlannedReference(IReadOnlyList<PinnedCampaignSeason> sequence)
    {
        int included = sequence.Count == 1 ? 1 : sequence.Count - 1;
        long total = 0;
        for (int i = 0; i < included; i++)
            total = checked(total + checked(40L * sequence[i].ChampionshipRoundCount + 340L));
        return total;
    }

    private static long GreatestCommonDivisor(long left, long right)
    {
        while (right != 0)
        {
            long next = left % right;
            left = right;
            right = next;
        }
        return Math.Abs(left);
    }
}
