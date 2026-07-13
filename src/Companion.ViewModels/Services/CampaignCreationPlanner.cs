using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Data;

namespace Companion.ViewModels.Services;

/// <summary>One fully parsed, byte-pinned pack prepared before the career transaction starts.</summary>
internal sealed record PreparedCampaignPack
{
    public required string Directory { get; init; }
    public required SeasonPack Pack { get; init; }
    public required byte[] EnvelopeBytes { get; init; }
    public required string Sha256 { get; init; }
    /// <summary>Hash before any allowed creation transform. For ordinary future packs it equals
    /// <see cref="Sha256"/>; the selected pack may have a transformed pinned hash.</summary>
    public required string SourceSha256 { get; init; }

    public PinnedCampaignSeason Snapshot(int? year = null) => new()
    {
        PackId = Pack.Manifest.PackId,
        PackVersion = Pack.Manifest.Version,
        Sha256 = Sha256,
        Year = year ?? Pack.Season.Year,
        ChampionshipRoundCount = Pack.Season.Rounds.Count(r => r.Championship),
    };

    public static PreparedCampaignPack From(
        SeasonPackFiles files,
        SeasonPack pack,
        string? sourceSha256 = null)
    {
        byte[] bytes = files.ToPinnedEnvelope().ToBytes();
        string sha256 = PinnedPackEnvelope.Sha256Of(bytes);
        return new PreparedCampaignPack
        {
            Directory = Path.GetFullPath(files.Directory),
            Pack = pack,
            EnvelopeBytes = bytes,
            Sha256 = sha256,
            SourceSha256 = sourceSha256 ?? sha256,
        };
    }
}

/// <summary>The authoritative v2 creation input plus every distinct pack blob it references.</summary>
internal sealed record CampaignCreationPreparation
{
    public required CampaignProgressionPlan Plan { get; init; }
    public required CharacterCreationInput CharacterInput { get; init; }
    public required IReadOnlyList<PreparedCampaignPack> DistinctPacks { get; init; }
}

/// <summary>Builds a bounded v2 plan from the creation-time pack catalog. It performs discovery once;
/// later continuation consumes only the stored plan and these pre-pinned bytes.</summary>
internal static class CampaignCreationPlanner
{
    public static CampaignCreationPreparation? Prepare(
        CareerCreationRequest request,
        CareerEnvironment environment,
        PreparedCampaignPack selected)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(selected);

        string? mode = request.ExperienceMode;
        if (mode is null)
        {
            if (request.Character?.ProgressionVersion >= CharacterLevelProgression.Level300Version)
                throw new InvalidOperationException(
                    "A new progression-v2 character requires an explicit career experience mode.");
            return null;
        }

        if (!CareerExperienceModes.IsKnown(mode))
            throw new InvalidOperationException($"Unknown career experience mode '{mode}'.");
        if (mode == CareerExperienceModes.RacingPassport)
            throw new InvalidOperationException(
                "Racing Passport requires its portfolio activity ledger and cannot be created as a single career file yet.");
        if (request.Character is not { ProgressionVersion: CharacterLevelProgression.Level300Version } character)
            throw new InvalidOperationException("An explicit Alpha experience mode requires a progression-v2 character.");

        bool selectedIsSmgp = string.Equals(
            selected.Pack.Manifest.CareerStyle,
            SmgpRules.CareerStyle,
            StringComparison.Ordinal);
        if (mode == CareerExperienceModes.Smgp && !selectedIsSmgp)
            throw new InvalidOperationException("SMGP mode requires an SMGP-styled season pack.");
        if (mode == CareerExperienceModes.GrandPrixDynasty && selectedIsSmgp)
            throw new InvalidOperationException("Grand Prix Dynasty cannot start from an SMGP pack.");

        var selectedReport = PackStructuralValidator.Validate(selected.Pack);
        if (selectedReport.HasErrors)
            throw new InvalidOperationException(
                "The selected pack is not structurally playable: " +
                string.Join(" | ", selectedReport.Issues
                    .Where(i => i.Severity == PackIssueSeverity.Error)
                    .Select(i => i.Message)));

        CampaignProgressionPlan plan;
        IReadOnlyList<PreparedCampaignPack> pins;
        if (mode == CareerExperienceModes.Smgp)
        {
            plan = CampaignProgressionPlan.CreateSmgp(selected.Snapshot());
            pins = [selected];
        }
        else
        {
            pins = DynastySequence(environment, selected);
            plan = CampaignProgressionPlan.Create(
                CareerExperienceModes.GrandPrixDynasty,
                selected.Pack.Season.Year,
                endYear: 2020,
                pins.Select(p => p.Snapshot()));
        }

        var input = new CharacterCreationInput
        {
            Profile = character,
            ExperienceMode = mode,
            CampaignProgressionPlan = plan,
        };
        input.ValidateForNewCareer();
        PlayerCarScalarPolicy.EnsureStagingCompatible(character, environment.Rules.Character);

        return new CampaignCreationPreparation
        {
            Plan = plan,
            CharacterInput = input,
            DistinctPacks = pins
                .GroupBy(p => (p.Pack.Manifest.PackId, p.Pack.Manifest.Version))
                .Select(g => g.First())
                .ToArray(),
        };
    }

    private static IReadOnlyList<PreparedCampaignPack> DynastySequence(
        CareerEnvironment environment,
        PreparedCampaignPack selected)
    {
        int startYear = selected.Pack.Season.Year;
        if (startYear is < 1960 or > 2020)
            throw new InvalidOperationException(
                "Grand Prix Dynasty must start within its historical 1960-2020 horizon.");

        var candidates = new List<PreparedCampaignPack> { selected };
        foreach (var discovered in PackDiscovery.Discover(environment.ResolvePackSearchRoots()))
        {
            if (discovered.Manifest is null || discovered.LoadError is not null || discovered.SeasonYear is null)
                continue;
            string directory = Path.GetFullPath(discovered.Directory);
            if (string.Equals(directory, selected.Directory, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var files = SeasonPackFiles.Read(directory);
                var pack = files.Parse();
                var prepared = PreparedCampaignPack.From(files, pack);
                if (string.Equals(
                        pack.Manifest.PackId,
                        selected.Pack.Manifest.PackId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        pack.Manifest.Version,
                        selected.Pack.Manifest.Version,
                        StringComparison.Ordinal))
                {
                    // A duplicate of the selected source is harmless, even when the selected bytes
                    // were legitimately transformed for this career. Different source bytes under
                    // the same id/version remain an immutable-identity violation.
                    if (!string.Equals(prepared.Sha256, selected.SourceSha256, StringComparison.Ordinal) &&
                        !string.Equals(prepared.Sha256, selected.Sha256, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Pack {pack.Manifest.PackId} {pack.Manifest.Version} appears with different " +
                            "source content in the creation catalog; bump the pack version or remove the duplicate.");
                    }
                    continue;
                }
                if (pack.Season.Year < startYear || pack.Season.Year > 2020 ||
                    string.Equals(pack.Manifest.CareerStyle, SmgpRules.CareerStyle, StringComparison.Ordinal) ||
                    PackStructuralValidator.Validate(pack).HasErrors)
                {
                    continue;
                }
                candidates.Add(prepared);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // An unreadable or invalid future pack is not faithful playable coverage and is not pinned.
            }
        }

        foreach (var identity in candidates.GroupBy(p =>
                     (p.Pack.Manifest.PackId, p.Pack.Manifest.Version)))
        {
            if (identity.Select(p => p.Sha256).Distinct(StringComparer.Ordinal).Skip(1).Any())
                throw new InvalidOperationException(
                    $"Pack {identity.Key.PackId} {identity.Key.Version} appears with different content " +
                    "in the creation catalog; bump the pack version or remove the duplicate.");
        }

        var sequence = new List<PreparedCampaignPack> { selected };
        sequence.AddRange(candidates
            .Where(p => p.Pack.Season.Year > startYear)
            .GroupBy(p => p.Pack.Season.Year)
            .OrderBy(g => g.Key)
            .Select(g => g
                .OrderBy(p => p.Pack.Manifest.PackId, StringComparer.Ordinal)
                .ThenBy(p => p.Directory, StringComparer.Ordinal)
                .First()));
        return sequence;
    }
}
