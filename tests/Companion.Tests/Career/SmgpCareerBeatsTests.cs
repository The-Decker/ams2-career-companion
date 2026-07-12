using Companion.Core.Smgp;

namespace Companion.Tests.Career;

/// <summary>
/// The pure milestone-timeline detector (Slice 1): given shaped per-season/per-round facts, it emits an
/// ordered career story. These tests pin the detection + ordering rules that the ViewModels feed real folded
/// state into — the "firsts" fire once across the career, promotions/demotions come from seat-tier moves,
/// rivalries come from the journal trigger (folded into a lost battle's tier drop), and the finale unlocks at
/// the 17-season summit.
/// </summary>
public sealed class SmgpCareerBeatsTests
{
    private static SmgpNarrativeRound Round(
        string venue, int? finish = null, bool pole = false, bool scored = false,
        string team = "Bullets", int prestige = 3, string? won = null, string? lost = null,
        int floorLosses = 0, bool careerOver = false) => new()
    {
        Venue = venue, Finish = finish, Pole = pole, ScoredPointsCumulative = scored,
        SeatTeamName = team, SeatPrestige = prestige, RivalryWonOver = won, RivalryLostTo = lost,
        FloorLosses = floorLosses, CareerOver = careerOver,
    };

    private static SmgpNarrativeSeason Season(
        int ordinal, IReadOnlyList<SmgpNarrativeRound> rounds, string startTeam = "Bullets", int startPrestige = 3,
        bool complete = false, bool champion = false, bool campaignComplete = false, bool flawless = false) => new()
    {
        Ordinal = ordinal, StartSeatTeamName = startTeam, StartSeatPrestige = startPrestige,
        Rounds = rounds, Complete = complete, PlayerChampion = champion,
        CampaignComplete = campaignComplete, CampaignFlawless = flawless,
    };

    [Fact]
    public void No_seasons_yields_no_beats()
    {
        Assert.Empty(SmgpCareerBeats.Detect([]));
    }

    [Fact]
    public void A_winning_debut_fires_arrived_then_every_first_once_in_order()
    {
        var beats = SmgpCareerBeats.Detect(
        [
            Season(1,
            [
                Round("Monaco", finish: 1, pole: true, scored: true),
                Round("Silverstone", finish: 1, pole: true, scored: true), // no NEW firsts
            ]),
        ]);

        var kinds = beats.Select(b => b.Kind).ToList();
        // Arrived leads; the round-1 firsts escalate points → top5 → podium → win, then pole.
        Assert.Equal(
        [
            SmgpBeatKind.Arrived,
            SmgpBeatKind.FirstStart,
            SmgpBeatKind.FirstPoints,
            SmgpBeatKind.FirstTop5,
            SmgpBeatKind.FirstPodium,
            SmgpBeatKind.FirstWin,
            SmgpBeatKind.FirstPole,
        ], kinds);
        // Each "first" appears exactly once — round 2 adds nothing.
        Assert.Single(beats, b => b.Kind == SmgpBeatKind.FirstWin);
        Assert.Equal("Season 1", beats[0].WhenLabel);
        Assert.Equal("Season 1 · Monaco", beats[1].WhenLabel);
    }

    [Fact]
    public void Firsts_spread_across_rounds_fire_when_first_achieved()
    {
        var beats = SmgpCareerBeats.Detect(
        [
            Season(1,
            [
                Round("Monaco", finish: 12),                 // start only
                Round("Spa", finish: 4, scored: true),       // points + top5
                Round("Monza", finish: 2),                   // podium
                Round("Suzuka", finish: 1, pole: true),      // win + pole
            ]),
        ]);

        string When(SmgpBeatKind k) => beats.First(b => b.Kind == k).WhenLabel;
        Assert.Equal("Season 1 · Monaco", When(SmgpBeatKind.FirstStart));
        Assert.Equal("Season 1 · Spa", When(SmgpBeatKind.FirstPoints));
        Assert.Equal("Season 1 · Spa", When(SmgpBeatKind.FirstTop5));
        Assert.Equal("Season 1 · Monza", When(SmgpBeatKind.FirstPodium));
        Assert.Equal("Season 1 · Suzuka", When(SmgpBeatKind.FirstWin));
        Assert.Equal("Season 1 · Suzuka", When(SmgpBeatKind.FirstPole));
    }

    [Fact]
    public void A_new_season_marks_a_milestone_and_a_promotion_when_the_seat_climbed()
    {
        var beats = SmgpCareerBeats.Detect(
        [
            Season(1, [Round("Monaco", finish: 1, scored: true)], startTeam: "Bullets", startPrestige: 3,
                complete: true, champion: true),
            // Signed up a tier for season 2.
            Season(2, [Round("Monaco", finish: 3, team: "Madonna", prestige: 5)], startTeam: "Madonna", startPrestige: 5),
        ]);

        Assert.Contains(beats, b => b.Kind == SmgpBeatKind.Title && b.WhenLabel == "Season 1");
        var milestone = Assert.Single(beats, b => b.Kind == SmgpBeatKind.SeasonMilestone);
        Assert.Equal("Season 2", milestone.WhenLabel);
        var promo = Assert.Single(beats, b => b.Kind == SmgpBeatKind.Promotion);
        Assert.Contains("MADONNA", promo.Headline);
    }

    [Fact]
    public void A_mid_season_seat_drop_is_a_demotion()
    {
        var beats = SmgpCareerBeats.Detect(
        [
            Season(1,
            [
                Round("Monaco", finish: 8, team: "Bullets", prestige: 3),
                Round("Spa", finish: 14, team: "Zeroforce", prestige: 2), // dropped a tier
            ]),
        ]);

        var demo = Assert.Single(beats, b => b.Kind == SmgpBeatKind.Demotion);
        Assert.Equal("Season 1 · Spa", demo.WhenLabel);
        Assert.Contains("ZEROFORCE", demo.Headline);
    }

    [Fact]
    public void A_two_wins_offer_is_a_rivalry_won_from_the_journal_trigger()
    {
        var beats = SmgpCareerBeats.Detect(
        [
            Season(1, [Round("Monaco", finish: 4, won: "G. Ceara")]),
        ]);

        var won = Assert.Single(beats, b => b.Kind == SmgpBeatKind.RivalryEarned);
        Assert.Contains("G. Ceara", won.Detail);
    }

    [Fact]
    public void A_forfeit_folds_its_tier_drop_into_one_rivalry_lost_beat()
    {
        var beats = SmgpCareerBeats.Detect(
        [
            Season(1,
            [
                Round("Monaco", finish: 10, team: "Bullets", prestige: 3),
                Round("Spa", finish: 15, team: "Zeroforce", prestige: 2, lost: "G. Ceara"), // forfeit + drop
            ]),
        ]);

        var lost = Assert.Single(beats, b => b.Kind == SmgpBeatKind.RivalryLost);
        Assert.Contains("G. Ceara", lost.Detail);
        Assert.Contains("Zeroforce", lost.Detail);
        // The tier drop is folded into the rivalry-lost beat — no separate demotion for the same round.
        Assert.DoesNotContain(beats, b => b.Kind == SmgpBeatKind.Demotion);
    }

    [Fact]
    public void Reaching_one_loss_from_the_floor_is_a_near_miss()
    {
        var beats = SmgpCareerBeats.Detect(
        [
            Season(1,
            [
                Round("Monaco", team: "Zeroforce", prestige: 2, floorLosses: 1),
                Round("Spa", team: "Zeroforce", prestige: 2, floorLosses: SmgpRules.FloorLossLimit - 1),
            ]),
        ]);

        var near = Assert.Single(beats, b => b.Kind == SmgpBeatKind.NearMiss);
        Assert.Equal("Season 1 · Spa", near.WhenLabel);
    }

    [Fact]
    public void Career_over_ends_the_timeline_with_a_knock_out_beat()
    {
        var beats = SmgpCareerBeats.Detect(
        [
            Season(1,
            [
                Round("Monaco", team: "Zeroforce", prestige: 2, floorLosses: 3),
                Round("Spa", team: "Zeroforce", prestige: 2, floorLosses: 4, careerOver: true, lost: "G. Ceara"),
                Round("Monza"), // never reached — the career ended
            ]),
            Season(2, [Round("Monaco")]), // never reached
        ]);

        Assert.Contains(beats, b => b.Kind == SmgpBeatKind.Demotion && b.Headline.Contains("OUT OF THE SMGP"));
        // Nothing after the knock-out.
        Assert.DoesNotContain(beats, b => b.WhenLabel.Contains("Monza"));
        Assert.DoesNotContain(beats, b => b.WhenLabel == "Season 2");
    }

    [Fact]
    public void Completing_seventeen_seasons_unlocks_the_finale()
    {
        var beats = SmgpCareerBeats.Detect(
        [
            Season(SmgpRules.CampaignSeasons, [Round("Monaco", finish: 1, scored: true)],
                complete: true, campaignComplete: true, flawless: false),
        ]);

        var finale = Assert.Single(beats, b => b.Kind == SmgpBeatKind.Finale);
        Assert.Equal("SEVENTEEN CONQUERED", finale.Headline);
    }

    [Fact]
    public void A_flawless_campaign_earns_the_emperor_finale()
    {
        var beats = SmgpCareerBeats.Detect(
        [
            Season(SmgpRules.CampaignSeasons, [Round("Monaco", finish: 1, scored: true)],
                complete: true, champion: true, campaignComplete: true, flawless: true),
        ]);

        var finale = Assert.Single(beats, b => b.Kind == SmgpBeatKind.Finale);
        Assert.Equal("THE EMPEROR RUN", finale.Headline);
    }
}
