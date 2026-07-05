using Companion.Core.Career;

namespace Companion.ViewModels.Services;

/// <summary>
/// The app-shipped career rules data the sim consumes (the exe-adjacent data\rules folder):
/// aging curves, team archetypes, and the headline bank. Loaded once per environment and fed
/// unchanged into every fold and season end, so the live path and replay see identical inputs
/// (docs/dev/career-sim.md, Replay contract).
/// </summary>
public sealed record CareerRulesData
{
    public required AgingCurveSet AgingCurves { get; init; }

    public required TeamArchetypeCatalog Archetypes { get; init; }

    public required HeadlineBank Headlines { get; init; }

    public static CareerRulesData Load(string rulesDirectory) => new()
    {
        AgingCurves = AgingCurveSet.Parse(Read(rulesDirectory, "career-aging-curves.json")),
        Archetypes = TeamArchetypeCatalog.Parse(Read(rulesDirectory, "career-team-archetypes.json")),
        Headlines = HeadlineBank.Parse(Read(rulesDirectory, "career-headline-templates.json")),
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
