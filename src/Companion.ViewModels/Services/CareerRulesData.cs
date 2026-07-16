using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.News;
using Companion.Core.Smgp;

namespace Companion.ViewModels.Services;

/// <summary>
/// The app-shipped career rules data the sim consumes (the exe-adjacent data\rules folder):
/// aging curves, team archetypes, the headline bank, and the generative news-article corpora.
/// Loaded once per environment and fed unchanged into every fold and season end, so the live
/// path and replay see identical inputs (docs/dev/career-sim.md, Replay contract). The article
/// corpora (data\rules\news\*.json) drive the read-only News feed's expanded bodies — a pure
/// projection, so they are NOT part of any fold input.
/// </summary>
public sealed record CareerRulesData
{
    public required AgingCurveSet AgingCurves { get; init; }

    public required TeamArchetypeCatalog Archetypes { get; init; }

    public required HeadlineBank Headlines { get; init; }

    /// <summary>The generative news-article grammar merged from every corpus under
    /// <c>data\rules\news\</c>. Empty (no bodies) when the folder is absent — the feed then
    /// keeps each headline as the whole story.</summary>
    public required NewsArticleBank NewsArticles { get; init; }

    /// <summary>The driver-character rules (<c>perks.json</c>): creation budget, stat mapping,
    /// XP curve, the 42 perks and 13 archetype presets. Consumed by the character creation wizard
    /// and — once a character career is simulated — by <c>PerkResolver</c>/<c>XpMath</c>. Loaded
    /// eagerly like the other rules so the live and replay paths see the identical instance.</summary>
    public required CharacterRules Character { get; init; }

    /// <summary>The progression-v2 Racing DNA catalog. Definitions are resolved by exact
    /// (id, version); no fold may silently substitute a later balance version.</summary>
    public required RacingDnaCatalog RacingDna { get; init; }

    /// <summary>The isolated progression-v2 90-skill graph plus seven attribute rails. It never
    /// extends the legacy perks.json membership; v2 ownership resolves through this catalog only.</summary>
    public required MasterySkillCatalog MasterySkills { get; init; }

    /// <summary>The SMGP rivals' per-driver, per-mood trash-talk (<c>data\rules\smgp\rival-quotes.json</c>).
    /// DISPLAY-ONLY — the briefing quote is never a fold input; empty (the deadpan default) when the
    /// file is absent, so a non-SMGP install or an un-updated data folder is unaffected.</summary>
    public required SmgpRivalQuotes SmgpRivalQuotes { get; init; }

    /// <summary>Venue-keyed SMGP pit-wall advice. DISPLAY-ONLY; empty fallback when absent.</summary>
    public required SmgpPitCrewAdvice SmgpPitCrewAdvice { get; init; }

    /// <summary>The SMGP-universe "What Really Happened" almanac (<c>data\rules\smgp\what-really-happened.json</c>):
    /// the SEGA world's own legend of every calendar circuit, revealed on the History tab once the
    /// player has raced it. DISPLAY-ONLY — never a fold input; empty when the file is absent (the
    /// History panel then simply hides).</summary>
    public required SmgpWhatReallyHappened SmgpWhatReallyHappened { get; init; }

    /// <summary>Per-team SMGP-world quotes + multi-paragraph history (<c>data\rules\smgp\team-profiles.json</c>),
    /// shown on the promotion/demotion screen when the player joins a team. DISPLAY-ONLY — never a fold
    /// input; empty when the file is absent (the team story then simply omits).</summary>
    public required SmgpTeamProfiles SmgpTeamProfiles { get; init; }

    /// <summary>Per-driver SMGP-world biographies (epithet + ~3-paragraph bio + quotes)
    /// (<c>data\rules\smgp\driver-profiles.json</c>), shown on the Paddock driver-preview tab.
    /// DISPLAY-ONLY — never a fold input; empty when the file is absent (bios then simply omit).</summary>
    public required SmgpDriverProfiles SmgpDriverProfiles { get; init; }

    /// <summary>Predetermined SMGP driver career stats — the world's history before the player arrived
    /// (<c>data\rules\smgp\driver-stats.json</c>), shown on the Paddock tab. DISPLAY-ONLY — never a fold
    /// input; empty when the file is absent (stats then simply omit).</summary>
    public required SmgpDriverStats SmgpDriverStats { get; init; }

    /// <summary>The SMGP-universe SPONSOR board — fictional brands with stories/logos + the teams they back
    /// (<c>data\rules\smgp\sponsors.json</c>), shown on the Paddock's Sponsors tab (and the seed of the
    /// future Tycoon mode). DISPLAY-ONLY — never a fold input; empty when the file is absent.</summary>
    public required SmgpSponsors SmgpSponsors { get; init; }

    /// <summary>The SMGP "living world" dispatch corpus (<c>data\rules\smgp\dispatches.json</c>): templated
    /// in-world news bodies for the reactive per-round dispatch feed (Task 4). DISPLAY-ONLY — never a fold
    /// input; empty when the file is absent (the feed then falls back to each milestone's own detail line).</summary>
    public required SmgpDispatchCorpus SmgpDispatchCorpus { get; init; }

    /// <summary>Per-car arcade spec cards (machine/engine/power + ENG-TM-SUS-TIRE-BRA bars) for the
    /// character and rival screens, keyed by team or vehicle id (<c>data\rules\car-specs.json</c>).
    /// DISPLAY-ONLY — never a fold input; empty when the file is absent (the card then collapses).</summary>
    public required CarSpecCatalog CarSpecs { get; init; }

    /// <summary>The living-newsroom template library (<c>data\rules\newsroom\*.json</c>): multi-
    /// section article templates + era pools voicing the mode-agnostic event spine. DISPLAY-ONLY —
    /// never a fold input; empty when the folder is absent (the newsroom feed then simply omits).</summary>
    public required Companion.Core.Newsroom.NewsroomCorpus NewsroomCorpus { get; init; }

    /// <summary>The fictional editorial desks (<c>data\rules\newsroom\desks.json</c>). DISPLAY-ONLY;
    /// empty when absent (articles then carry no masthead).</summary>
    public required Companion.Core.Newsroom.NewsDesks NewsDesks { get; init; }

    public static CareerRulesData Load(string rulesDirectory)
    {
        var character = CharacterRules.Parse(Read(rulesDirectory, "perks.json"));
        var racingDna = RacingDnaCatalog.Parse(Read(rulesDirectory, "racing-dna-v2.json"), character);
        return new CareerRulesData
        {
            AgingCurves = AgingCurveSet.Parse(Read(rulesDirectory, "career-aging-curves.json")),
            Archetypes = TeamArchetypeCatalog.Parse(Read(rulesDirectory, "career-team-archetypes.json")),
            Headlines = HeadlineBank.Parse(Read(rulesDirectory, "career-headline-templates.json")),
            NewsArticles = NewsArticleBank.LoadDirectory(Path.Combine(rulesDirectory, "news")),
            Character = character,
            RacingDna = racingDna,
            MasterySkills = MasterySkillCatalog.Parse(
                Read(rulesDirectory, "mastery-skills-v2.json"), character, racingDna),
            SmgpRivalQuotes = SmgpRivalQuotes.Load(rulesDirectory),
            SmgpPitCrewAdvice = SmgpPitCrewAdvice.Load(rulesDirectory),
            SmgpWhatReallyHappened = SmgpWhatReallyHappened.Load(rulesDirectory),
            SmgpTeamProfiles = SmgpTeamProfiles.Load(rulesDirectory),
            SmgpDriverProfiles = SmgpDriverProfiles.Load(rulesDirectory),
            SmgpDriverStats = SmgpDriverStats.Load(rulesDirectory),
            SmgpSponsors = SmgpSponsors.Load(rulesDirectory),
            SmgpDispatchCorpus = SmgpDispatchCorpus.Load(rulesDirectory),
            CarSpecs = CarSpecCatalog.Load(rulesDirectory),
            NewsroomCorpus = Companion.Core.Newsroom.NewsroomCorpus.LoadDirectory(
                Path.Combine(rulesDirectory, "newsroom")),
            NewsDesks = Companion.Core.Newsroom.NewsDesks.Load(
                Path.Combine(rulesDirectory, "newsroom")),
        };
    }

    private static string Read(string rulesDirectory, string fileName)
    {
        string path = Path.Combine(rulesDirectory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Career rules file '{path}' is missing — the data\\rules folder must sit beside the exe.",
                path);
        return File.ReadAllText(path);
    }
}
