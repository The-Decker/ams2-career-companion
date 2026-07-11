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

    /// <summary>The SMGP rivals' per-driver, per-mood trash-talk (<c>data\rules\smgp\rival-quotes.json</c>).
    /// DISPLAY-ONLY — the briefing quote is never a fold input; empty (the deadpan default) when the
    /// file is absent, so a non-SMGP install or an un-updated data folder is unaffected.</summary>
    public required SmgpRivalQuotes SmgpRivalQuotes { get; init; }

    /// <summary>The SMGP-universe "What Really Happened" almanac (<c>data\rules\smgp\what-really-happened.json</c>):
    /// the SEGA world's own legend of every calendar circuit, revealed on the History tab once the
    /// player has raced it. DISPLAY-ONLY — never a fold input; empty when the file is absent (the
    /// History panel then simply hides).</summary>
    public required SmgpWhatReallyHappened SmgpWhatReallyHappened { get; init; }

    public static CareerRulesData Load(string rulesDirectory) => new()
    {
        AgingCurves = AgingCurveSet.Parse(Read(rulesDirectory, "career-aging-curves.json")),
        Archetypes = TeamArchetypeCatalog.Parse(Read(rulesDirectory, "career-team-archetypes.json")),
        Headlines = HeadlineBank.Parse(Read(rulesDirectory, "career-headline-templates.json")),
        NewsArticles = NewsArticleBank.LoadDirectory(Path.Combine(rulesDirectory, "news")),
        Character = CharacterRules.Parse(Read(rulesDirectory, "perks.json")),
        SmgpRivalQuotes = SmgpRivalQuotes.Load(rulesDirectory),
        SmgpWhatReallyHappened = SmgpWhatReallyHappened.Load(rulesDirectory),
    };

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
