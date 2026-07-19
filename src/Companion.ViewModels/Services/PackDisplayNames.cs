using Companion.Core.Packs;

namespace Companion.ViewModels.Services;

/// <summary>
/// The ONE competitor-id → display-name rule for every screen (standings tabs, round matrix,
/// confirm movements, season review): driver ids resolve through the pack's drivers.json,
/// constructor keys through the pack's teams.json (career-mode constructor standings carry
/// the pack teamId, the grid seats' TeamId feeds the engine's ConstructorId, so
/// "team.brabham" resolves to the pack-authored historical name "Brabham-Repco"), and
/// anything unknown falls back to the raw id. Community packs author their own names;
/// nothing is hardcoded here.
/// </summary>
public static class PackDisplayNames
{
    /// <summary>A dictionary-backed resolver over <paramref name="pack"/>: driver name first,
    /// then team name, then the id itself. Build once per screen, not per row.</summary>
    public static Func<string, string> ResolverFor(SeasonPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var drivers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var driver in pack.Drivers)
            drivers[driver.Id] = driver.Name;

        var teams = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var team in pack.Teams)
            teams[team.Id] = team.Name;

        return id =>
            drivers.TryGetValue(id, out var driverName) ? driverName
            : teams.TryGetValue(id, out var teamName) ? teamName
            : id;
    }

    /// <summary>One-off resolution (see <see cref="ResolverFor"/> for the lookup order).</summary>
    public static string Resolve(SeasonPack pack, string competitorId) =>
        ResolverFor(pack)(competitorId);
}
