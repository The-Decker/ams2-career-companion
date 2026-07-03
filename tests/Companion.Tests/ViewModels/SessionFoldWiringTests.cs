using System.Text.Json;
using Companion.Core.Career;
using Companion.Data;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The M4/M5 integration wiring (docs/dev/m5-fix-integration.md, "App wiring"), proven on the
/// REAL packs/f1-1967 pack through the REAL session service: every Apply goes through the
/// unified fold (per-round journal events + round_player_state), the result screen's slider
/// lands in the raw-result envelope, the confirm headline previews the fold byte-exactly,
/// season completion produces the review with offers scored from the FOLDED final state, and
/// accept-one is journaled and survives a reopen.
/// </summary>
public sealed class SessionFoldWiringTests : IDisposable
{
    private const string PlayerLivery = "Brabham-Repco #2 D. Hulme";
    private const double Round1Slider = 95.0;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-fold-wiring-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite WAL sidecars can outlive the connection briefly on Windows.
        }
    }

    private string CareerPath => Path.Combine(_root, "career.ams2career");

    private CareerEnvironment Environment() =>
        ViewModelTestData.Environment(Path.Combine(_root, "docs"));

    [Fact]
    public void FullSeason_FoldsEveryRound_ReviewsOffersAndJournalsTheAcceptance()
    {
        var request = new CareerCreationRequest
        {
            PackDirectory = ViewModelTestData.RealPackDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Fold Wiring 1967",
            MasterSeed = 4242,
            PlayerLiveryName = PlayerLivery,
        };

        string? round1Headline = null;
        string acceptedTeam;
        int roundCount;

        using (var session = CareerSessionService.CreateCareer(request, Environment()))
        {
            roundCount = session.Pack.Season.Rounds.Count;
            Assert.Equal(11, roundCount);

            for (int round = 1; round <= roundCount; round++)
            {
                var grid = session.CurrentGrid();
                Assert.NotEmpty(grid);

                var draft = new ResultDraft
                {
                    Classified = grid.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                    // Round 1: the player says what they actually raced at; afterwards the
                    // prompt's prefill (the recommendation) is what Apply stores.
                    SliderUsed = round == 1 ? Round1Slider : null,
                };

                var confirm = session.Preview(draft);
                Assert.False(string.IsNullOrWhiteSpace(confirm.Headline));
                if (round == 1)
                    round1Headline = confirm.Headline;

                session.Apply(draft);

                // The home header reads the FOLDED player state after every Apply.
                var summary = session.Summary;
                Assert.NotNull(summary.Reputation);
                Assert.NotNull(summary.Opi);
                if (round >= 2)
                {
                    Assert.NotNull(summary.ReputationDelta);
                    Assert.NotNull(summary.OpiDelta);
                }

                if (round < roundCount)
                {
                    // Briefing + result screen share the round's recommendation.
                    int? recommendation = session.CurrentSliderRecommendation();
                    Assert.NotNull(recommendation);
                    Assert.InRange(recommendation.Value, 70, 120);
                    Assert.Equal(recommendation, session.CurrentBriefing()!.RecommendedSlider);
                }
            }

            Assert.True(session.Summary.SeasonComplete);
            Assert.Null(session.CurrentBriefing());

            // ---- season review: offers from the FOLDED final state, headline digest ----
            var review = session.SeasonReview();
            Assert.NotNull(review);
            Assert.Equal(1967, review.SeasonYear);
            Assert.NotEmpty(review.Offers);
            Assert.NotEmpty(review.Headlines);
            Assert.Null(review.AcceptedTeamId);
            Assert.All(review.Offers, o => Assert.False(o.Accepted));

            // ---- accept-one ----
            acceptedTeam = review.Offers[0].TeamId;
            session.AcceptOffer(acceptedTeam);

            var afterAccept = session.SeasonReview();
            Assert.NotNull(afterAccept);
            Assert.Equal(acceptedTeam, afterAccept.AcceptedTeamId);
            Assert.True(afterAccept.Offers.Single(o => o.TeamId == acceptedTeam).Accepted);
            Assert.Single(afterAccept.Offers, o => o.Accepted);

            // Accepting an unknown team is refused.
            Assert.Throws<InvalidOperationException>(() => session.AcceptOffer("team.no_such"));
        }

        // ---- the career file itself proves the wiring ----
        using (var db = CareerDatabase.Open(CareerPath))
        {
            long seasonId = CareerStore.ReadSeasons(db).Single().Id;

            // The envelope stored what the result screen asked: the round-1 slider.
            var stored = ResultStore.ReadSeasonResults(db, seasonId);
            Assert.Equal(roundCount, stored.Count);
            Assert.Equal(Round1Slider, stored[0].ToEnvelope().SliderUsed);
            Assert.All(stored, r => Assert.NotNull(r.ToEnvelope().SliderUsed));

            // Every round journaled its fold: standings + the player round update.
            var journal = JournalStore.ReadSeason(db, seasonId);
            for (int round = 1; round <= roundCount; round++)
            {
                var phases = journal.Where(r => r.Round == round).Select(r => r.Phase).ToList();
                Assert.Contains(DataJournalPhases.RoundStandings, phases);
                Assert.Contains(JournalPhases.RaceResult, phases);
                Assert.Contains(JournalPhases.PlayerOpi, phases);
                Assert.Contains(JournalPhases.PlayerReputation, phases);
                Assert.Contains(JournalPhases.PlayerPaceAnchor, phases);
            }

            // Preview == fold: the round-1 confirm headline is the journaled headline.
            var headlineRow = journal.FirstOrDefault(r =>
                r.Round == 1 && r.Phase == JournalPhases.Headline);
            Assert.NotNull(headlineRow); // 1967 bank covers the round causes
            using (var document = JsonDocument.Parse(headlineRow.DeltaJson))
                Assert.Equal(document.RootElement.GetProperty("text").GetString(), round1Headline);

            // Season end ran off the final round's FOLDED player state.
            var folded = StateStore.ReadRoundPlayerState(db, seasonId, roundCount);
            Assert.NotNull(folded);
            Assert.True(folded.Player.PaceAnchor > 0.0, "a full season must calibrate the anchor");
            Assert.Contains(journal, r => r.Phase == JournalPhases.Championship);
            Assert.Contains(journal, r => r.Phase == JournalPhases.PlayerExperience);

            // The acceptance is journaled as provenance (excluded from replay compare).
            var acceptedRow = journal.Single(r =>
                r.Phase == DataJournalPhases.CareerProvenance && r.Cause == "offer-accepted");
            Assert.Contains(acceptedTeam, acceptedRow.DeltaJson);
        }

        // ---- reopening the career keeps the completed review + the accepted offer ----
        using var reopened = CareerSessionService.OpenCareer(CareerPath, Environment());
        Assert.True(reopened.Summary.SeasonComplete);
        var reopenedReview = reopened.SeasonReview();
        Assert.NotNull(reopenedReview);
        Assert.Equal(acceptedTeam, reopenedReview.AcceptedTeamId);
        Assert.NotEmpty(reopenedReview.Headlines);
    }

    /// <summary>The M5 blame mapping for a customised player DNF: a custom "other" cause
    /// flagged as the driver's fault stores <see cref="DnfCause.DriverError"/>, while an
    /// un-flagged custom "other" keeps the no-blame default (null). The one-letter seam and
    /// the detail map are read together by the session.</summary>
    [Theory]
    [InlineData(true, DnfCause.DriverError)]
    [InlineData(false, null)]
    public void PlayerCustomOtherDnf_AttributionDrivesTheStoredBlame(bool driverAttributed, DnfCause? expected)
    {
        const string playerId = "driver.denny_hulme";
        var request = new CareerCreationRequest
        {
            PackDirectory = ViewModelTestData.RealPackDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Custom DNF 1967",
            MasterSeed = 7,
            PlayerLiveryName = PlayerLivery,
        };

        long seasonId;
        using (var session = CareerSessionService.CreateCareer(request, Environment()))
        {
            var grid = session.CurrentGrid();
            Assert.Contains(grid, s => s.DriverId == playerId);

            var draft = new ResultDraft
            {
                Classified = grid.Where(s => s.DriverId != playerId).Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string> { [playerId] = "o" },
                DidNotFinishDetail = new Dictionary<string, DnfDetail>
                {
                    [playerId] = new() { Text = "Engine fire", DriverAttributed = driverAttributed },
                },
                Disqualified = [],
            };

            session.Apply(draft);

            using var db = CareerDatabase.Open(CareerPath);
            seasonId = CareerStore.ReadSeasons(db).Single().Id;
            var envelope = ResultStore.ReadSeasonResults(db, seasonId)[0].ToEnvelope();
            Assert.Equal(expected, envelope.PlayerDnfCause);
        }
    }
}
