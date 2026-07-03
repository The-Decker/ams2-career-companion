using Companion.Core.Career;
using Companion.Core.Determinism;

namespace Companion.Tests.Career;

public class SeasonEndPipelineTests
{
    // ---------- determinism ----------

    [Fact]
    public void SameInputsAndSeedProduceElementWiseIdenticalJournals()
    {
        var first = SeasonEndPipeline.Run(CareerTestData.Context());
        var second = SeasonEndPipeline.Run(CareerTestData.Context());

        Assert.Equal(first.Events.Count, second.Events.Count);
        for (int i = 0; i < first.Events.Count; i++)
            Assert.Equal(first.Events[i], second.Events[i]);

        Assert.Equal(first.Player, second.Player);
        Assert.Equal(first.Drivers, second.Drivers);
        Assert.Equal(first.Teams, second.Teams);
        Assert.Equal(first.Offers, second.Offers);
    }

    [Fact]
    public void DifferentMasterSeedsAreDifferentWorlds()
    {
        // Not every row differs (canon events don't roll), but headline selections and rolls do:
        // across a handful of seeds at least one journal must diverge.
        var baseline = SeasonEndPipeline.Run(CareerTestData.Context(masterSeed: 1));
        bool anyDifferent = false;
        for (ulong seed = 2; seed <= 6 && !anyDifferent; seed++)
        {
            var other = SeasonEndPipeline.Run(CareerTestData.Context(masterSeed: seed));
            anyDifferent = !baseline.Events.SequenceEqual(other.Events);
        }
        Assert.True(anyDifferent);
    }

    // ---------- stream isolation across steps ----------

    [Fact]
    public void AddingADriverNeverChangesAnotherDriversRolls()
    {
        var baseline = SeasonEndPipeline.Run(CareerTestData.Context());

        var extended = CareerTestData.DriverStates().ToList();
        extended.Add(new DriverCareerState { DriverId = "driver.extra", Age = 27 });
        var withExtra = SeasonEndPipeline.Run(CareerTestData.Context(drivers: extended));

        // Aging, retirement, and foreshadow rows for every original driver must be identical
        // — rolls are keyed per entity, so a new consumer cannot shift anyone's sequence.
        foreach (string driverId in CareerTestData.DriverStates().Select(d => d.DriverId))
        {
            Assert.Equal(
                RowsFor(baseline, driverId),
                RowsFor(withExtra, driverId));
            Assert.Equal(
                baseline.Drivers.Single(d => d.DriverId == driverId),
                withExtra.Drivers.Single(d => d.DriverId == driverId));
        }

        static List<JournalEvent> RowsFor(SeasonEndResult result, string entity) =>
            result.Events
                .Where(e => e.Entity == entity &&
                            e.Phase is JournalPhases.DriverAging
                                or JournalPhases.Retirement
                                or JournalPhases.RetirementForeshadow)
                .ToList();
    }

    // ---------- step 1: standings ----------

    [Fact]
    public void JournalOpensWithChampionshipRowsInStandingsOrder()
    {
        var result = SeasonEndPipeline.Run(CareerTestData.Context());

        // 6 drivers + 3 constructors, all classified in the synthetic season.
        var rows = result.Events.Take(9).ToList();
        Assert.All(rows, e => Assert.Equal(JournalPhases.Championship, e.Phase));
        Assert.Equal("driver.old", rows[0].Entity);         // champion
        Assert.Contains("\"position\":1", rows[0].DeltaJson);
        Assert.Equal("team.min", rows[6].Entity);           // constructors champion
        Assert.All(rows, e => Assert.Equal("standings-final", e.Cause));
    }

    // ---------- step 2: player finals ----------

    [Fact]
    public void PlayerSeasonFinalAppliesChampionshipBonusAndBumpsSeasonCount()
    {
        var result = SeasonEndPipeline.Run(CareerTestData.Context(playerReputation: 40.0));

        // Player is P3 in a tier-3 team: +6 × 1.5 = +9.
        Assert.Equal(49.0, result.Player.Reputation, 12);
        Assert.Equal(2, result.Player.SeasonsCompleted);

        var repRow = result.Events.Single(e =>
            e.Phase == JournalPhases.PlayerReputation && e.Cause == "season-final");
        Assert.Contains("\"championshipPosition\":3", repRow.DeltaJson);
    }

    [Fact]
    public void SeasonsCompletedIncrementIsJournaled()
    {
        // Journal/state parity: the experience bump is a state change like any other.
        var result = SeasonEndPipeline.Run(CareerTestData.Context());

        var row = result.Events.Single(e => e.Phase == JournalPhases.PlayerExperience);
        Assert.Equal("player", row.Entity);
        Assert.Equal("season-final", row.Cause);
        Assert.Contains("\"from\":1", row.DeltaJson);
        Assert.Contains("\"to\":2", row.DeltaJson);
    }

    // ---------- step 3: aging ----------

    [Fact]
    public void EveryActiveDriverAgesOneSeason()
    {
        var result = SeasonEndPipeline.Run(CareerTestData.Context());

        foreach (var before in CareerTestData.DriverStates())
        {
            var after = result.Drivers.Single(d => d.DriverId == before.DriverId);
            Assert.Equal(before.Age + 1, after.Age);
        }
        Assert.Equal(
            CareerTestData.DriverStates().Count,
            result.Events.Count(e => e.Phase == JournalPhases.DriverAging));
    }

    [Fact]
    public void YoungDriversHoldOrImproveOldDriversDecline()
    {
        var result = SeasonEndPipeline.Run(CareerTestData.Context());

        // driver.a turns 26 (pre-peak, rise 0.004 ± 0.004 noise): delta cannot be negative
        // beyond noise, and driver.old turns 41 (deep past peak): must lose skill.
        var young = result.Drivers.Single(d => d.DriverId == "driver.a");
        Assert.True(young.RaceSkillDelta > -0.001,
            $"A 26-year-old must not decline ({young.RaceSkillDelta}).");

        var old = result.Drivers.Single(d => d.DriverId == "driver.old");
        Assert.True(old.RaceSkillDelta < 0.0,
            $"A 41-year-old must decline ({old.RaceSkillDelta}).");
    }

    // ---------- step 4: retirements + foreshadowing ----------

    [Fact]
    public void CanonRetirementsFireExactlyOnSchedule()
    {
        var result = SeasonEndPipeline.Run(CareerTestData.Context());

        // canon_now retires this season, cause canon, no roll involved.
        Assert.True(result.Drivers.Single(d => d.DriverId == "driver.canon_now").Retired);
        var row = result.Events.Single(e =>
            e.Phase == JournalPhases.Retirement && e.Entity == "driver.canon_now");
        Assert.Equal("canon", row.Cause);

        // canon_next is scheduled for NEXT season: still racing today...
        Assert.False(result.Drivers.Single(d => d.DriverId == "driver.canon_next").Retired);
    }

    [Fact]
    public void DriverRetiringNextSeasonGetsTheForeshadowHeadline()
    {
        var result = SeasonEndPipeline.Run(CareerTestData.Context());

        // ...but the seeded foreshadowing knows: "considering their future" this season.
        // (Other veterans may foreshadow via the hazard peek too; canon_next is guaranteed.)
        var foreshadows = result.Events
            .Where(e => e.Phase == JournalPhases.RetirementForeshadow)
            .ToList();
        Assert.Contains(foreshadows, e => e.Entity == "driver.canon_next");
        Assert.All(foreshadows, e => Assert.Equal("considering-future", e.Cause));

        var headlines = result.Events
            .Where(e => e.Phase == JournalPhases.Headline && e.Cause == "considering-future")
            .ToList();
        Assert.Contains(headlines, e => e.DeltaJson.Contains("Charlie Canon"));
    }

    [Fact]
    public void RetirementRowsCarryHazardAndRollForTheWhyInspector()
    {
        // driver.old carries a ~95% seasonal hazard: across ten seeds at least one
        // age-performance retirement must fire, and every such row explains itself.
        var hazardRows = new List<JournalEvent>();
        for (ulong seed = 1; seed <= 10; seed++)
        {
            hazardRows.AddRange(SeasonEndPipeline.Run(CareerTestData.Context(masterSeed: seed)).Events
                .Where(e => e.Phase == JournalPhases.Retirement && e.Cause == "age-performance"));
        }

        Assert.NotEmpty(hazardRows);
        Assert.All(hazardRows, row =>
        {
            Assert.Contains("\"hazard\":", row.DeltaJson);
            Assert.Contains("\"roll\":", row.DeltaJson);
        });
    }

    // ---------- step 5: seat market ----------

    [Fact]
    public void VacatedSeatsFillFromThePoolAndMinnowsChasePayMoney()
    {
        var result = SeasonEndPipeline.Run(CareerTestData.Context());

        // canon_now's minnow seat is guaranteed vacant; with a 20 BU pay driver against a
        // slightly quicker penniless one, the minnow archetype must take the money.
        var filled = result.Events
            .Where(e => e.Phase == JournalPhases.SeatMarket && e.Cause == "vacancy-filled")
            .ToList();
        Assert.NotEmpty(filled);
        Assert.All(filled, e => Assert.Equal("team.min", e.Entity));
        Assert.Contains(filled, e => e.DeltaJson.Contains("\"hired\":\"driver.fa2\""));
    }

    [Fact]
    public void HiredFreeAgentEntersTheReturnedDriverStates()
    {
        // Journal/state parity: the fa2 hire above is a state change, so the hired driver
        // must be a returned driver state (age as authored, skills anchored via deltas
        // against the 0.5 pool-outsider baseline).
        var result = SeasonEndPipeline.Run(CareerTestData.Context());

        var hired = result.Drivers.Single(d => d.DriverId == "driver.fa2");
        Assert.Equal(30, hired.Age);
        Assert.Equal(0.60 - 0.5, hired.RaceSkillDelta, 12);
        Assert.False(hired.Retired);
    }

    [Fact]
    public void SeatMarketValuesCandidateReputation()
    {
        // Two otherwise-identical candidates: the reputed one must win the seat, because
        // scoring carries the contract's archetype-weighted rep term.
        var result = SeasonEndPipeline.Run(CareerTestData.Context(freeAgents:
        [
            new SeatCandidate { DriverId = "driver.nobody", RaceSkill = 0.66, Age = 28 },
            new SeatCandidate
            {
                DriverId = "driver.famous",
                RaceSkill = 0.66,
                Age = 28,
                Reputation = SeatCandidate.DefaultReputation(5),
            },
        ]));

        var filled = result.Events
            .First(e => e.Phase == JournalPhases.SeatMarket && e.Cause == "vacancy-filled");
        Assert.Contains("\"hired\":\"driver.famous\"", filled.DeltaJson);
        Assert.Contains("\"rep\":75", filled.DeltaJson);
    }

    [Fact]
    public void SeatMarketNoiseStreamIsKeyedByTeamAndVacatedDriver()
    {
        // Reproduce every journaled hire from first principles with the documented stream
        // key `offers|year|0|{teamId}->{vacatedBy}` — proving each vacancy rolls its own
        // noise sequence even when one team vacates two seats in a winter (the fixture's
        // vacancies are all team.min: canon_now guaranteed, driver.old hazard-dependent).
        var context = CareerTestData.Context();
        var result = SeasonEndPipeline.Run(context);

        var catalog = CareerTestData.LoadArchetypes();
        var archetype = catalog.ForTeam(1, null); // team.min is tier 1 → minnow
        var curve = CareerTestData.LoadAgingCurves().ForYear(1967);

        var filled = result.Events
            .Where(e => e.Phase == JournalPhases.SeatMarket && e.Cause == "vacancy-filled")
            .ToList();
        Assert.NotEmpty(filled);

        var pool = context.FreeAgents.ToList();
        foreach (var journalEvent in filled)
        {
            using var delta = System.Text.Json.JsonDocument.Parse(journalEvent.DeltaJson);
            string vacatedBy = delta.RootElement.GetProperty("vacatedBy").GetString()!;
            var stream = new StreamFactory(42).CreateStream(
                CareerStreams.Offers, 1967, 0, journalEvent.Entity + "->" + vacatedBy);

            SeatCandidate? best = null;
            double bestScore = double.NegativeInfinity;
            foreach (var candidate in pool)
            {
                double score = candidate.RaceSkill
                               + archetype.Weights.Rep * candidate.Reputation / 100.0
                               - 0.02 * Math.Max(0, candidate.Age - curve.PeakAgeEnd)
                               + archetype.PayDriverWeight * candidate.PayBudgetBu / 100.0
                               + 0.01 * (2.0 * stream.NextDouble() - 1.0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            Assert.Equal(best!.DriverId, delta.RootElement.GetProperty("hired").GetString());
            Assert.Equal(Math.Round(bestScore, 4), delta.RootElement.GetProperty("score").GetDouble(), 12);
            pool.Remove(best);
        }
    }

    [Fact]
    public void EmptyPoolLeavesTheVacancyJournaledButUnfilled()
    {
        var result = SeasonEndPipeline.Run(CareerTestData.Context(freeAgents: []));

        var unfilled = result.Events
            .Where(e => e.Phase == JournalPhases.SeatMarket && e.Cause == "vacancy-unfilled")
            .ToList();
        Assert.Contains(unfilled, e => e.Entity == "team.min");
        Assert.DoesNotContain(result.Events, e => e.Cause == "vacancy-filled");
    }

    // ---------- step 6: player offers ----------

    [Fact]
    public void OffersAreTierGatedByReputation()
    {
        // Rep 40 → 49 after finals: below the tier-5 floor (70), above tier-3 (30) and tier-1 (0).
        var modest = SeasonEndPipeline.Run(CareerTestData.Context(playerReputation: 40.0));
        Assert.Equal(2, modest.Offers.Count);
        Assert.DoesNotContain(modest.Offers, o => o.TeamId == "team.top");

        // Rep 80 → 89: every tier considers the player.
        var famous = SeasonEndPipeline.Run(CareerTestData.Context(playerReputation: 80.0));
        Assert.Equal(3, famous.Offers.Count);
        Assert.Contains(famous.Offers, o => o.TeamId == "team.top");
    }

    [Fact]
    public void OffersAreOrderedByScoreAndJournaled()
    {
        var result = SeasonEndPipeline.Run(CareerTestData.Context(playerReputation: 80.0));

        for (int i = 1; i < result.Offers.Count; i++)
            Assert.True(result.Offers[i - 1].Score >= result.Offers[i].Score);

        var rows = result.Events.Where(e => e.Phase == JournalPhases.OfferExtended).ToList();
        Assert.Equal(result.Offers.Count, rows.Count);
        Assert.Equal(result.Offers.Select(o => o.TeamId), rows.Select(r => r.Entity));
    }

    [Fact]
    public void OfferSalariesComeFromTheTierBands()
    {
        var result = SeasonEndPipeline.Run(CareerTestData.Context(playerReputation: 80.0));
        var catalog = CareerTestData.LoadArchetypes();

        foreach (var offer in result.Offers)
            Assert.Equal(catalog.SalaryOffer(offer.Tier, result.Player.Reputation), offer.SalaryBu);
    }

    // ---------- step 7: tier drift ----------

    [Fact]
    public void TierDriftIsBoundedToOneStepAndTheValidRange()
    {
        // The synthetic season maximally provokes drift (minnow wins, top team flops).
        // Across many seeds: every drift is ±1 and tiers stay in 1..5; at least one seed
        // actually drifts (p=0.5 per eligible team per seed).
        bool anyDrift = false;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var result = SeasonEndPipeline.Run(CareerTestData.Context(masterSeed: seed));
            foreach (var before in CareerTestData.TeamStates())
            {
                var after = result.Teams.Single(t => t.TeamId == before.TeamId);
                Assert.InRange(after.Tier, 1, 5);
                Assert.InRange(Math.Abs(after.Tier - before.Tier), 0, 1);
                anyDrift |= after.Tier != before.Tier;
            }

            foreach (var row in result.Events.Where(e => e.Phase == JournalPhases.TeamTier))
            {
                // Causes match the authored headline template keys (team.tier|promoted /
                // team.tier|relegated) — and with a bank present, the headline itself fires.
                Assert.True(row.Cause is "promoted" or "relegated");
                Assert.Contains("\"roll\":", row.DeltaJson);
                Assert.Contains(result.Events, e =>
                    e.Phase == JournalPhases.Headline &&
                    e.Entity == row.Entity &&
                    e.Cause == row.Cause);
            }
        }
        Assert.True(anyDrift, "Twenty maximally-provoked seasons must produce at least one drift.");
    }

    [Fact]
    public void TeamMeetingExpectationNeverDrifts()
    {
        // team.mid is expected P2 and finishes P2 in every seed.
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var result = SeasonEndPipeline.Run(CareerTestData.Context(masterSeed: seed));
            Assert.Equal(3, result.Teams.Single(t => t.TeamId == "team.mid").Tier);
        }
    }

    // ---------- digest ----------

    [Fact]
    public void SeasonDigestHeadlineClosesTheJournal()
    {
        var result = SeasonEndPipeline.Run(CareerTestData.Context());

        var digest = result.Events[^1];
        Assert.Equal(JournalPhases.Headline, digest.Phase);
        Assert.Equal("season", digest.Entity);
        Assert.Equal("season-digest", digest.Cause);
        Assert.Contains("Old Oscar", digest.DeltaJson); // the synthetic champion
    }

    [Fact]
    public void NoHeadlineBankMeansNoHeadlineRows()
    {
        var result = SeasonEndPipeline.Run(CareerTestData.Context(withHeadlines: false));
        Assert.DoesNotContain(result.Events, e => e.Phase == JournalPhases.Headline);
    }
}
