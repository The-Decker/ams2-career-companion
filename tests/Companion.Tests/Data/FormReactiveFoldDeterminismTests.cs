using Companion.Core.Career;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// Determinism gate for Ratings Phase 3 — the FOLD reacts to the pack's per-race
/// <see cref="SeasonDefinition.DriverForm"/> when the career is <see cref="PlayerCareerState.FormAware"/>.
/// Proves three things: (1) the overlay nudges an AI seat's pace rating by the round's form and leaves
/// the PLAYER seat untouched (a hot rival moves the field, never the player's own strength); (2) the
/// fold actually consumes it — a FormAware career's folded pace anchor differs from an otherwise
/// identical form-inert one; (3) the FormAware career re-simulates BYTE-IDENTICALLY. The sibling
/// <see cref="FormFoldDeterminismTests"/> is the OFF-path gate (a pre-Phase-3 career folds form-inert).
/// </summary>
public sealed class FormReactiveFoldDeterminismTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-form-reactive-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The round-1 form overlay: a big up-nudge on the AI (brabham) and a down-nudge on the
    /// player (hulme) — large enough that a leak into the fold would diverge. The player's is excluded,
    /// so the resolved player seat must stay at its baseline.</summary>
    private static SeasonPack FormPack() => TestPackBuilder.TwoRoundPack() is var basePack
        ? basePack with
        {
            Season = basePack.Season with
            {
                DriverForm = new Dictionary<int, IReadOnlyDictionary<string, PackDriverForm>>
                {
                    [1] = new Dictionary<string, PackDriverForm>
                    {
                        ["driver.brabham"] = new() { RaceSkill = 0.07, QualifyingSkill = 0.05 },
                        ["driver.hulme"] = new() { RaceSkill = -0.06, QualifyingSkill = -0.04 },
                    },
                    [2] = new Dictionary<string, PackDriverForm>
                    {
                        ["driver.brabham"] = new() { RaceSkill = -0.05, QualifyingSkill = -0.03 },
                    },
                },
            },
        }
        : throw new InvalidOperationException();

    [Fact]
    public void PerRaceForm_NudgesTheRivalSeat_ButNeverThePlayerSeat()
    {
        var pack = FormPack();
        var playerSeat = new PlayerSeat { Ams2LiveryName = TestPackBuilder.StockLivery2 }; // driver.hulme

        // Both drivers baseline at RaceSkill 0.80 / QualifyingSkill 0.85 (TestPackBuilder.Driver).
        var off = RoundGridResolver.Resolve(pack, 1, playerSeat, applyWeekendForm: false);
        var on = RoundGridResolver.Resolve(pack, 1, playerSeat, applyWeekendForm: true);

        GridSeat Rival(GridPlan g) => g.Seats.Single(s => s.DriverId == "driver.brabham");
        GridSeat Player(GridPlan g) => g.Seats.Single(s => s.IsPlayer);

        // Off: the overlay is inert — baseline verbatim.
        Assert.Equal(0.80, Rival(off).Ratings.RaceSkill, 6);
        // On: the rival gets the additive, clamped nudge (0.80 + 0.07); qualifying too (0.85 + 0.05).
        Assert.Equal(0.87, Rival(on).Ratings.RaceSkill, 6);
        Assert.Equal(0.90, Rival(on).Ratings.QualifyingSkill, 6);

        // The player seat is EXCLUDED — its down-nudge (-0.06) is never applied, on either path.
        Assert.Equal(0.80, Player(off).Ratings.RaceSkill, 6);
        Assert.Equal(0.80, Player(on).Ratings.RaceSkill, 6);
        Assert.Equal(0.85, Player(on).Ratings.QualifyingSkill, 6);
    }

    [Fact]
    public void FormAwareCareer_ReactsInTheFold_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        var pack = FormPack();
        TestPackBuilder.Write(pack, packDirectory);

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 20260708;

        double anchorOn = FoldTwoRounds(packDirectory, environment, seed, "form-on.ams2career", formAware: true, out string onPath);
        double anchorOff = FoldTwoRounds(packDirectory, environment, seed, "form-off.ams2career", formAware: false, out _);

        // The fold CONSUMES the overlay: the FormAware career's round-1 pace anchor differs from the
        // form-inert one, given identical results — a hot rival (brabham 0.80 -> 0.87) reshapes the
        // field the player's pace is measured against. (Both are non-zero: the anchor calibrated.)
        Assert.NotEqual(0.0, anchorOn);
        Assert.NotEqual(anchorOff, anchorOn);

        // ...and the FormAware career re-simulates byte-identically (the whole point of the gate).
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(onPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        Assert.True(StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!.FormAware);

        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };

        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.True(report.ComparedRows > 0);
    }

    /// <summary>Creates a career (FormAware or not), applies both rounds classifying the whole grid,
    /// and returns the folded round-1 pace anchor.</summary>
    private double FoldTwoRounds(
        string packDirectory, CareerEnvironment environment, long seed, string fileName, bool formAware, out string careerPath)
    {
        careerPath = Path.Combine(_root, "careers", fileName);
        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = formAware ? "Form On" : "Form Off",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       FormAware = formAware,
                   },
                   environment))
        {
            for (int round = 0; round < 2; round++)
            {
                var seats = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = seats.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        return StateStore.ReadRoundPlayerState(db, seasonId, 1)!.Player.PaceAnchor;
    }
}
