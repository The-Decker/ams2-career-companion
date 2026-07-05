using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.Tests.Packs;

/// <summary>
/// One failing-case test per structural rule, driven off a minimal valid two-round pack
/// built directly as DTOs (the loader has its own tests).
/// </summary>
public class PackStructuralValidatorTests
{
    // ---------- pack builders ----------

    private static PackManifest Manifest() => new()
    {
        PackId = "test-pack",
        Name = "Test Pack",
        Version = "1.0.0",
        FormatVersion = 1,
    };

    private static CatalogSeason Points() => new()
    {
        RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
    };

    private static PackTeam Team(string id) => new()
    {
        Id = id,
        Name = id,
        CarVehicleIds = ["formula_vintage_g1m2"],
    };

    private static PackDriverRatings Ratings(double value) => new()
    {
        RaceSkill = value,
        QualifyingSkill = value,
        Aggression = value,
        Defending = value,
        Stamina = value,
        Consistency = value,
        StartReactions = value,
        WetSkill = value,
        TyreManagement = value,
        AvoidanceOfMistakes = value,
    };

    private static PackDriver Driver(string id, double rating = 0.8) => new()
    {
        Id = id,
        Name = id,
        Ratings = Ratings(rating),
    };

    private static PackRound Round(
        int number, string date, bool championship = true, int laps = 40, int opponents = 5) => new()
    {
        Round = number,
        Name = $"Round {number}",
        Date = date,
        Championship = championship,
        Track = new PackTrackRef { Id = "kyalami_historic" },
        Laps = laps,
        SetupGuide = new PackSetupGuide { Session = new PackSessionSettings { Opponents = opponents } },
    };

    private static PackEntry Entry(
        string driverId, string number, string rounds, string livery, string teamId = "team.brabham") => new()
    {
        TeamId = teamId,
        DriverId = driverId,
        Number = number,
        Rounds = rounds,
        Ams2LiveryName = livery,
    };

    private static PackGuestEntry Guest(
        string driverId, string livery, string teamId = "team.brabham") => new()
    {
        TeamId = teamId,
        DriverId = driverId,
        Number = "31",
        Ams2LiveryName = livery,
    };

    private static SeasonPack ValidPack() => new()
    {
        Manifest = Manifest(),
        Season = new SeasonDefinition
        {
            Year = 1967,
            SeriesName = "Test Championship",
            Ams2Class = "F-Vintage_Gen1",
            PointsSystem = Points(),
            Rounds = [Round(1, "1967-01-02"), Round(2, "1967-05-07")],
        },
        Teams = [Team("team.brabham")],
        Drivers = [Driver("driver.brabham"), Driver("driver.hulme")],
        Entries =
        [
            Entry("driver.brabham", "1", "1-2", "Livery #1"),
            Entry("driver.hulme", "2", "1-2", "Livery #2"),
        ],
    };

    private static PackValidationReport Validate(SeasonPack pack) =>
        PackStructuralValidator.Validate(pack);

    private static void AssertError(PackValidationReport report, string substring) =>
        Assert.Contains(report.Issues,
            i => i.Severity == PackIssueSeverity.Error && i.Message.Contains(substring));

    private static void AssertWarning(PackValidationReport report, string substring) =>
        Assert.Contains(report.Issues,
            i => i.Severity == PackIssueSeverity.Warning && i.Message.Contains(substring));

    private static void AssertNoErrors(PackValidationReport report) =>
        Assert.False(report.HasErrors,
            "Unexpected errors:\n" + string.Join("\n", report.Issues.Select(i => $"{i.Severity}: {i.Message}")));

    // ---------- the happy path ----------

    [Fact]
    public void Validate_MinimalValidPack_ProducesNoIssues()
    {
        var report = Validate(ValidPack());

        Assert.Empty(report.Issues);
        Assert.False(report.HasErrors);
    }

    // ---------- id uniqueness + references ----------

    [Fact]
    public void Validate_DuplicateTeamId_IsAnError()
    {
        var pack = ValidPack() with { Teams = [Team("team.brabham"), Team("team.brabham")] };

        AssertError(Validate(pack), "Duplicate team id 'team.brabham'");
    }

    [Fact]
    public void Validate_DuplicateDriverId_IsAnError()
    {
        var pack = ValidPack() with
        {
            Drivers = [Driver("driver.brabham"), Driver("driver.brabham"), Driver("driver.hulme")],
        };

        AssertError(Validate(pack), "Duplicate driver id 'driver.brabham'");
    }

    [Fact]
    public void Validate_EntryReferencingUnknownTeam_IsAnError()
    {
        var pack = ValidPack() with
        {
            Entries =
            [
                Entry("driver.brabham", "1", "1-2", "Livery #1", teamId: "team.ghost"),
                Entry("driver.hulme", "2", "1-2", "Livery #2"),
            ],
        };

        AssertError(Validate(pack), "unknown team 'team.ghost'");
    }

    [Fact]
    public void Validate_EntryReferencingUnknownDriver_IsAnError()
    {
        var pack = ValidPack() with
        {
            Entries =
            [
                Entry("driver.ghost", "1", "1-2", "Livery #1"),
                Entry("driver.hulme", "2", "1-2", "Livery #2"),
            ],
        };

        AssertError(Validate(pack), "unknown driver 'driver.ghost'");
    }

    [Fact]
    public void Validate_GuestEntryReferencingUnknownIds_IsAnError()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[1] = rounds[1] with
        {
            GuestEntries = [Guest("driver.ghost", "Guest Livery", teamId: "team.ghost")],
        };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        var report = Validate(pack);
        AssertError(report, "guest entry (driver.ghost) references unknown team 'team.ghost'");
        AssertError(report, "guest entry references unknown driver 'driver.ghost'");
    }

    // ---------- round coverage + setup guides ----------

    [Fact]
    public void Validate_ChampionshipRoundWithoutEntries_IsAnError()
    {
        var pack = ValidPack() with
        {
            Entries =
            [
                Entry("driver.brabham", "1", "1", "Livery #1"),
                Entry("driver.hulme", "2", "1", "Livery #2"),
            ],
        };

        AssertError(Validate(pack), "Championship round 2 (Round 2) has no entries");
    }

    [Fact]
    public void Validate_GuestEntriesCountTowardRoundCoverage()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[1] = rounds[1] with { GuestEntries = [Guest("driver.hulme", "Guest Livery")] };
        pack = pack with
        {
            Season = pack.Season with { Rounds = rounds },
            Entries = [Entry("driver.brabham", "1", "1", "Livery #1")],
        };

        AssertNoErrors(Validate(pack));
    }

    [Fact]
    public void Validate_ChampionshipRoundWithoutSetupGuide_IsAnError()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[1] = rounds[1] with { SetupGuide = null };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        AssertError(Validate(pack), "Championship round 2 (Round 2) has no setupGuide");
    }

    [Fact]
    public void Validate_NonChampionshipRoundWithoutSetupGuide_IsOnlyAWarning()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[1] = rounds[1] with { Championship = false, SetupGuide = null };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        var report = Validate(pack);
        AssertNoErrors(report);
        AssertWarning(report, "Non-championship round 2 (Round 2) has no setupGuide");
    }

    [Fact]
    public void Validate_RoundWithZeroLaps_IsAnError()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = rounds[0] with { Laps = 0 };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        AssertError(Validate(pack), "laps=0");
    }

    [Fact]
    public void Validate_SetupGuideWithZeroOpponents_IsAnError()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = Round(1, "1967-01-02", opponents: 0);
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        AssertError(Validate(pack), "opponents=0");
    }

    [Fact]
    public void Validate_PlaceholderRoundWithoutRealVenue_IsAnError()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = rounds[0] with
        {
            Track = rounds[0].Track with { IsPlaceholder = true, RealVenue = null },
        };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        AssertError(Validate(pack), "placeholder track but names no realVenue");
    }

    [Fact]
    public void Validate_PlaceholderRoundWithRealVenue_IsClean()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = rounds[0] with
        {
            Track = rounds[0].Track with { IsPlaceholder = true, RealVenue = "Circuit Park Zandvoort" },
        };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        AssertNoErrors(Validate(pack));
    }

    // ---------- dates ----------

    [Fact]
    public void Validate_UnparseableDate_IsAnError()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[1] = rounds[1] with { Date = "May 7 1967" };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        AssertError(Validate(pack), "'May 7 1967' is not a valid yyyy-MM-dd date");
    }

    [Fact]
    public void Validate_DescendingDates_IsAnError()
    {
        var pack = ValidPack();
        pack = pack with
        {
            Season = pack.Season with { Rounds = [Round(1, "1967-05-07"), Round(2, "1967-01-02")] },
        };

        AssertError(Validate(pack), "calendar dates must ascend");
    }

    [Fact]
    public void Validate_EqualDates_IsOnlyAWarning()
    {
        var pack = ValidPack();
        pack = pack with
        {
            Season = pack.Season with { Rounds = [Round(1, "1967-01-02"), Round(2, "1967-01-02")] },
        };

        var report = Validate(pack);
        AssertNoErrors(report);
        AssertWarning(report, "shares its date 1967-01-02");
    }

    // ---------- round numbering ----------

    [Fact]
    public void Validate_NonContiguousRoundNumbers_IsAnError()
    {
        var pack = ValidPack();
        pack = pack with
        {
            Season = pack.Season with { Rounds = [Round(1, "1967-01-02"), Round(3, "1967-05-07")] },
        };

        AssertError(Validate(pack), "contiguous from 1");
    }

    [Fact]
    public void Validate_RoundNumbersNotStartingAtOne_IsAnError()
    {
        var pack = ValidPack();
        pack = pack with
        {
            Season = pack.Season with { Rounds = [Round(2, "1967-01-02"), Round(3, "1967-05-07")] },
        };

        AssertError(Validate(pack), "contiguous from 1");
    }

    [Fact]
    public void Validate_EmptyCalendar_IsAnError()
    {
        var pack = ValidPack();
        pack = pack with { Season = pack.Season with { Rounds = [] } };

        AssertError(Validate(pack), "has no rounds");
    }

    // ---------- points system ----------

    [Fact]
    public void Validate_PointsSystemThatCannotResolve_IsAnError()
    {
        // A 6+5 split-season best-N rule against a 2-round season must be flagged, not thrown.
        var pack = ValidPack();
        pack = pack with
        {
            Season = pack.Season with
            {
                PointsSystem = Points() with
                {
                    DriversBestN = new CatalogBestN
                    {
                        Split = new CatalogSplitSeason
                        {
                            FirstRounds = 6, FirstCount = 5, SecondRounds = 5, SecondCount = 4,
                        },
                    },
                },
            },
        };

        AssertError(Validate(pack), "pointsSystem does not resolve for 2 championship rounds");
    }

    [Fact]
    public void Validate_SeasonWithNoChampionshipRounds_IsAnError()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.Select(r => r with { Championship = false }).ToArray();
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        AssertError(Validate(pack), "no championship rounds");
    }

    // ---------- ratings ----------

    [Fact]
    public void Validate_RatingAboveOne_IsAnError()
    {
        var pack = ValidPack() with
        {
            Drivers = [Driver("driver.brabham", rating: 1.2), Driver("driver.hulme")],
        };

        AssertError(Validate(pack), "rating raceSkill=1.2 is outside 0..1");
    }

    [Fact]
    public void Validate_NegativeRating_IsAnError()
    {
        var pack = ValidPack() with
        {
            Drivers =
            [
                Driver("driver.brabham") with { Ratings = Ratings(0.8) with { WetSkill = -0.1 } },
                Driver("driver.hulme"),
            ],
        };

        AssertError(Validate(pack), "rating wetSkill=-0.1 is outside 0..1");
    }

    [Fact]
    public void Validate_TrackFormNudgeBeyondFiveHundredths_IsAWarning()
    {
        var pack = ValidPack() with
        {
            Drivers =
            [
                Driver("driver.brabham") with
                {
                    TrackForm = new Dictionary<string, double> { ["kyalami_historic"] = 0.2 },
                },
                Driver("driver.hulme"),
            ],
        };

        var report = Validate(pack);
        AssertNoErrors(report);
        AssertWarning(report, "trackForm nudge for 'kyalami_historic' is 0.2");
    }

    [Fact]
    public void Validate_AiOverrideRatingOutOfRange_IsAnError()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = rounds[0] with
        {
            AiOverrides = new Dictionary<string, PackRatingsPatch>
            {
                ["driver.brabham"] = new() { RaceSkill = 1.5 },
            },
        };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        AssertError(Validate(pack), "aiOverrides for 'driver.brabham': raceSkill=1.5 is outside 0..1");
    }

    [Fact]
    public void Validate_AiOverrideForUnknownDriver_IsAWarning()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = rounds[0] with
        {
            AiOverrides = new Dictionary<string, PackRatingsPatch>
            {
                ["driver.ghost"] = new() { RaceSkill = 0.9 },
            },
        };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        var report = Validate(pack);
        AssertNoErrors(report);
        AssertWarning(report, "aiOverrides references unknown driver 'driver.ghost'");
    }

    // ---------- livery binding ----------

    [Fact]
    public void Validate_TwoEntriesBindingTheSameLiveryInTheSameRound_IsAnError()
    {
        var pack = ValidPack() with
        {
            Entries =
            [
                Entry("driver.brabham", "1", "1-2", "Same Livery"),
                Entry("driver.hulme", "2", "1-2", "Same Livery"),
            ],
        };

        var report = Validate(pack);
        AssertError(report, "Livery 'Same Livery' is bound by more than one entry in round(s) 1, 2");
    }

    [Fact]
    public void Validate_SameLiveryInDisjointRounds_IsFine()
    {
        // A mid-season car swap: the livery changes hands, never doubled in one race.
        var pack = ValidPack() with
        {
            Entries =
            [
                Entry("driver.brabham", "1", "1", "Same Livery"),
                Entry("driver.hulme", "2", "2", "Same Livery"),
                Entry("driver.hulme", "2", "1", "Livery #2"),
                Entry("driver.brabham", "1", "2", "Livery #1"),
            ],
        };

        AssertNoErrors(Validate(pack));
    }

    [Fact]
    public void Validate_GuestEntryDuplicatingAnEntryLivery_IsAnError()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = rounds[0] with { GuestEntries = [Guest("driver.hulme", "Livery #1")] };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        AssertError(Validate(pack), "Livery 'Livery #1' is bound by more than one entry in round(s) 1");
    }

    // ---------- rounds ranges ----------

    [Fact]
    public void Validate_RoundsRangeBeyondTheCalendar_IsAnError()
    {
        var pack = ValidPack() with
        {
            Entries =
            [
                Entry("driver.brabham", "1", "1-3", "Livery #1"),
                Entry("driver.hulme", "2", "1-2", "Livery #2"),
            ],
        };

        AssertError(Validate(pack), "includes round 3, but the season has only 2 rounds");
    }

    [Fact]
    public void Validate_UnparseableRoundsRange_IsAnError()
    {
        var pack = ValidPack() with
        {
            Entries =
            [
                Entry("driver.brabham", "1", "1-x", "Livery #1"),
                Entry("driver.hulme", "2", "1-2", "Livery #2"),
            ],
        };

        AssertError(Validate(pack), "Entry #1 (driver.brabham)");
    }
}
