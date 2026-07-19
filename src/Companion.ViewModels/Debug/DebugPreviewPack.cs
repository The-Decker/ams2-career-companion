using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Core.Smgp;

namespace Companion.ViewModels.Debug;

/// <summary>
/// A self-contained, in-memory minimal <see cref="SeasonPack"/> for the developer debug menu's
/// TIER-2 previews (dynasty-passport-roadmap.md Piece 2). Mirrors the shape of the test suite's
/// minimal pack so a <see cref="PreviewCareerSession"/> can host any View with real pack identity
/// (year, teams, drivers, a grid) WITHOUT reading a career database off disk. Nothing here is ever
/// pinned, folded, or written, it exists only to feed display projections, so a preview can never
/// perturb the deterministic-replay contract or create a <c>.ams2career</c> file.
/// </summary>
public static class DebugPreviewPack
{
    private const string Class = "F-Vintage_Gen1";
    private const string Car = "formula_vintage_g1m2";
    private const string Track = "kyalami_historic";

    /// <summary>The player's livery on the built grid, the seat a preview treats as "you".</summary>
    public const string PlayerLivery = "Stock Livery #2";

    /// <summary>Builds a two-round preview pack for <paramref name="year"/>. When
    /// <paramref name="smgp"/> is true it carries the SMGP career style so a preview hub renders
    /// with the SMGP identity; the fold-driving projections are supplied by the fake session, not
    /// this pack, so the style is purely cosmetic here.</summary>
    public static SeasonPack Build(int year = 1967, bool smgp = false, string? seriesName = null) => new()
    {
        Manifest = new PackManifest
        {
            PackId = smgp ? "smgp-preview" : $"debug-preview-{year}",
            Name = smgp ? "SMGP (preview)" : $"Debug Preview {year}",
            Version = "1.0.0",
            FormatVersion = 1,
            CareerStyle = smgp ? SmgpRules.CareerStyle : "",
        },
        Season = new SeasonDefinition
        {
            Year = year,
            SeriesName = seriesName ?? (smgp ? "Super Monaco GP" : $"Debug Championship {year}"),
            Ams2Class = Class,
            PointsSystem = new CatalogSeason
            {
                RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
                Constructors = new CatalogConstructors { BestCarOnly = true, BestN = "sameAsDrivers" },
                DriversBestN = new CatalogBestN { WholeSeason = 2 },
            },
            Rounds =
            [
                Round(1, $"{year}-01-02"),
                Round(2, $"{year}-05-07"),
            ],
        },
        Teams =
        [
            new PackTeam
            {
                Id = "team.one",
                Name = "Preview Racing",
                CarVehicleIds = [Car],
                Reliability = 0.93,
                Prestige = 4,
                BudgetTier = 5,
            },
            new PackTeam
            {
                Id = "team.two",
                Name = "Debug Motors",
                CarVehicleIds = [Car],
                Reliability = 0.90,
                Prestige = 3,
                BudgetTier = 4,
            },
        ],
        Drivers = [Driver("driver.rival"), Driver("driver.player-donor")],
        Entries =
        [
            Entry("team.one", "driver.rival", "1", "Stock Livery #1"),
            Entry("team.two", "driver.player-donor", "2", PlayerLivery),
        ],
    };

    private static PackRound Round(int number, string date) => new()
    {
        Round = number,
        Name = $"Round {number}",
        Date = date,
        Track = new PackTrackRef { Id = Track },
        Laps = 40,
        SetupGuide = new PackSetupGuide { Session = new PackSessionSettings { Opponents = 1 } },
    };

    private static PackDriver Driver(string id) => new()
    {
        Id = id,
        Name = id == "driver.rival" ? "Rival Driver" : "Donor Driver",
        Country = "GBR",
        Ratings = new PackDriverRatings
        {
            RaceSkill = 0.8,
            QualifyingSkill = 0.85,
            Aggression = 0.5,
            Defending = 0.5,
            Stamina = 0.8,
            Consistency = 0.8,
            StartReactions = 0.8,
            WetSkill = 0.8,
            TyreManagement = 0.8,
            AvoidanceOfMistakes = 0.8,
        },
    };

    private static PackEntry Entry(string teamId, string driverId, string number, string livery) => new()
    {
        TeamId = teamId,
        DriverId = driverId,
        Number = number,
        Rounds = "1-2",
        Ams2LiveryName = livery,
    };
}
