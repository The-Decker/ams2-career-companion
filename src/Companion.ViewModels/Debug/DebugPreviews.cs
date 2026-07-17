using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Packs;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Debug;

/// <summary>
/// TIER-2 preview builders (dynasty-passport-roadmap.md Piece 2, §6 of the build brief): each returns
/// a <see cref="PreviewCareerSession"/> seeded with the canned projections that route the real shell
/// wiring (<see cref="Shell.HomeViewModel"/>'s constructor) to a target screen. Every one is
/// display-only and DB-free — a preview never creates or writes a <c>.ams2career</c>.
/// </summary>
public static class DebugPreviews
{
    /// <summary>Racing Passport — <see cref="CareerExperienceModes.RacingPassport"/> is
    /// <c>IsAvailable=false</c> in the menu AND throws at creation, so it is genuinely unbuildable.
    /// This is the ONLY way to look at it: a preview hub labelled as a Passport preview.</summary>
    public static PreviewCareerSession RacingPassport(SeasonPack pack)
    {
        var session = new PreviewCareerSession(pack, new CareerSummary
        {
            CareerName = "Racing Passport (preview)",
            SeasonYear = pack.Season.Year,
            SeriesName = pack.Season.SeriesName,
            CurrentRound = 1,
            RoundCount = pack.Season.Rounds.Count,
            PlayerDriverId = "driver.player-donor",
            PlayerLiveryName = DebugPreviewPack.PlayerLivery,
        }).WithGridFromPack();
        return session;
    }

    /// <summary>An arbitrary character level (1–300) on the Driver tab — the classic "preview a
    /// progression screen I cannot grind to". Builds a real <see cref="CharacterDossier"/> at the
    /// requested level from the character rules, so the projection is authentic even though no fold
    /// ever produced it. <paramref name="racingDnaId"/> swaps the preview character's Racing DNA to
    /// any catalog identity (a contextual DNA would fail REAL creation validation — a preview never
    /// creates, and the dossier renders the authored identity straight from the catalog);
    /// <paramref name="completedSeasons"/> sets the mastery-track position (&lt; 0 = fully mastered).</summary>
    public static PreviewCareerSession Level(
        SeasonPack pack,
        CharacterRules characterRules,
        RacingDnaCatalog? dna,
        MasterySkillCatalog? mastery,
        int level,
        string? racingDnaId = null,
        int completedSeasons = -1)
    {
        var character = DebugCareerFactory.DefaultCharacter($"Level {level} Driver");
        if (racingDnaId is not null &&
            dna?.Definitions.FirstOrDefault(
                d => string.Equals(d.Id, racingDnaId, StringComparison.Ordinal)) is { } definition)
        {
            character = character with
            {
                RacingDnaId = definition.Id,
                RacingDnaVersion = definition.Version,
                RacingDnaChoice = definition.Choice is { Required: true } choice
                    ? choice.Options.FirstOrDefault()
                    : null,
            };
        }
        int clamped = Math.Clamp(level, 1, CharacterLevelProgression.Level300Max);
        long xp = CharacterLevelProgression.CumulativeXpToLevel(
            character.ProgressionVersion, clamped, characterRules);

        // A v2 dossier's Skill-Point balance is gated by a pinned campaign plan; a preview has no real
        // career, so synthesize a fully-mastered SMGP replica plan (17 identical 16-round seasons)
        // purely to compute a plausible SP figure. The plan is not tied to the preview pack.
        var plan = CampaignProgressionPlan.CreateSmgp(new PinnedCampaignSeason
        {
            PackId = "smgp-preview",
            PackVersion = "1.0.0",
            Sha256 = new string('0', 64),
            Year = 1990,
            ChampionshipRoundCount = 16,
        });
        int seasons = Math.Clamp(
            completedSeasons < 0 ? plan.MasterySeason : completedSeasons,
            0,
            plan.MasterySeason);

        var dossier = CharacterDossier.Build(
            character, clamped, xp, characterRules,
            age: character.Age,
            campaignProgressionPlan: plan,
            completedSeasons: seasons,
            masterySkills: mastery,
            racingDnaCatalog: dna);

        int previewSp = Math.Max(1, dossier.CpUnspent);
        var tree = SkillTree.Build(character, clamped, previewSp, characterRules);

        return new PreviewCareerSession(pack, new CareerSummary
        {
            CareerName = $"Level {clamped} preview",
            SeasonYear = pack.Season.Year,
            SeriesName = pack.Season.SeriesName,
            CurrentRound = 1,
            RoundCount = pack.Season.Rounds.Count,
            PlayerDriverId = "driver.player-donor",
            PlayerLiveryName = DebugPreviewPack.PlayerLivery,
            PlayerPosition = 1,
            Reputation = 74.0,
            Opi = 0.6,
        })
        {
            Dossier = dossier,
            Tree = tree,
            SkillPoints = previewSp,
            Identity = ("driver.player-donor", character.Name),
            TeamName = "Preview Racing",
        }.WithGridFromPack();
    }

    /// <summary>A death screen without dying — the fatal terminal surface (character death &amp; injury
    /// §6). Seeds a deceased mortality status + a rich <see cref="DeathScreenModel"/> so
    /// <see cref="Shell.HomeViewModel"/> routes to the death screen exactly as a live fatal round does.</summary>
    public static PreviewCareerSession Death(SeasonPack pack, MortalityMode mode = MortalityMode.Hardcore)
    {
        var record = new CareerRecordsBook
        {
            BestFinish = 1,
            Wins = 12,
            Podiums = 34,
            TotalPoints = 512,
            Championships = 2,
            SeasonsRaced = 6,
            LongestWinStreak = 4,
            LongestPodiumStreak = 9,
        };
        var seasons = new[]
        {
            new CareerSeasonCard
            {
                SeasonYear = pack.Season.Year,
                PlayerPosition = 1,
                RoundsApplied = pack.Season.Rounds.Count,
                RoundCount = pack.Season.Rounds.Count,
                IsComplete = true,
                ChampionName = "Debug Driver",
                PlayerIsChampion = true,
                Headlines = ["A champion's season, cut short."],
            },
        };
        var death = DeathScreenModel.Build(
            mode, "Debug Driver", age: 29,
            severity: AccidentSeverity.Heavy, venue: pack.Season.Rounds[0].Name, round: 1,
            record: record, seasons: seasons, restoreSlots: []);

        return new PreviewCareerSession(pack)
        {
            MortalityStatus = new PlayerMortalityStatus
            {
                Mode = mode,
                Deceased = true,
                SeasonEndingInjury = false,
                RaceSuspensionRemaining = 0,
                CareerFileDeleted = false,
            },
            Death = death,
        }.WithGridFromPack();
    }

    /// <summary>The SMGP career-over (Zeroforce floor knock-out): a briefing whose
    /// <c>CareerOver</c> flag is set, which <see cref="Briefing.BriefingViewModel.SmgpCareerOver"/>
    /// projects and the shell renders as the terminal SMGP surface.</summary>
    public static PreviewCareerSession SmgpCareerOver(SeasonPack pack)
    {
        return new PreviewCareerSession(pack)
        {
            SmgpBriefing = new SmgpBriefingModel
            {
                RoundHeader = "MONACO · ROUND 1",
                SeasonLine = "SEASON  P8 · 0 PTS",
                CareerLine = "CAREER  0 WINS",
                AdviceLine = "The floor gave way.",
                Titles = 0,
                SeasonOrdinal = 3,
                SeasonsTotal = Companion.Core.Smgp.SmgpRules.CampaignSeasons,
                CareerOver = true,
                Rivals = [],
            },
        }.WithGridFromPack();
    }

    /// <summary>The injured sit-out screen (character death &amp; injury §5): a minor suspension or a
    /// season-ending injury the shell auto-simulates around.</summary>
    public static PreviewCareerSession SitOut(SeasonPack pack, bool seasonEnding)
    {
        return new PreviewCareerSession(pack)
        {
            SitOut = new SitOutStatus
            {
                RaceSuspensionRemaining = seasonEnding ? 0 : 2,
                SeasonEnding = seasonEnding,
                Headline = seasonEnding
                    ? "SEASON OVER — recovering"
                    : "INJURED — auto-simulating round (2 remaining)",
            },
        }.WithGridFromPack();
    }

    /// <summary>The 17-season SMGP campaign FINALE (Mike's "final final screen"). Seeds a complete
    /// season + a finale model so the shell shows the special.jpg / ultimate.jpg celebration.</summary>
    public static PreviewCareerSession Finale(SeasonPack pack, bool flawless)
    {
        return new PreviewCareerSession(pack, new CareerSummary
        {
            CareerName = "Campaign finale (preview)",
            SeasonYear = pack.Season.Year,
            SeriesName = pack.Season.SeriesName,
            CurrentRound = pack.Season.Rounds.Count,
            RoundCount = pack.Season.Rounds.Count,
            PlayerDriverId = "driver.player-donor",
            PlayerLiveryName = DebugPreviewPack.PlayerLivery,
            SeasonComplete = true,
            PlayerPosition = 1,
        })
        {
            Finale = new SmgpFinaleModel
            {
                Headline = flawless ? "SEVENTEEN CROWNS" : "THE LONG ROAD, CONQUERED",
                Subhead = flawless
                    ? "Champion in every season of the grand campaign."
                    : "All seventeen seasons survived.",
                IsFlawless = flawless,
                HeroImageKey = flawless ? "ultimate" : "special",
                Record = flawless
                    ? ["17 seasons conquered", "17 titles won"]
                    : ["17 seasons conquered", "3 titles won"],
            },
            Review = new SeasonReviewModel
            {
                SeasonYear = pack.Season.Year,
                PlayerPosition = 1,
                FinalReputation = 92.0,
                FinalOpi = 1.2,
                Headlines = ["The campaign is complete."],
                Offers = [],
            },
        }.WithGridFromPack();
    }

    /// <summary>The promotion / demotion screen model (3c-3), for a leaf-screen preview. A promotion
    /// is accept/decline; a demotion only acknowledges.</summary>
    public static SmgpPromotionModel Promotion(bool demotion) => new()
    {
        Kind = demotion ? SmgpPromotionKind.Demotion : SmgpPromotionKind.PromotionOffer,
        Headline = demotion ? "RELEGATED TO ZEROFORCE" : "AN OFFER FROM MADONNA",
        TeamName = demotion ? "Zeroforce" : "Madonna",
        TeamPhotoKey = demotion ? "zeroforce" : "madonna",
        PlayerImageKey = demotion ? "player.zeroforce" : "player.madonna",
        Motto = demotion ? "Every empire ends." : "Only the summit remains.",
        History = demotion
            ? ["Zeroforce runs the back of the grid — where careers go to be forgotten."]
            : ["Madonna is the summit of the grid, and they have come for you."],
        Quotes = demotion ? ["\"Prove yourself again.\""] : ["\"You have earned this.\""],
        RivalName = demotion ? null : "A. Senna",
    };
}
