using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.Core.Smgp;

/// <summary>
/// Pure between-season SMGP grid reshuffle: the previous championship order assigns drivers to
/// next season's cars from highest-prestige to lowest. The player's current car is reserved, and
/// A. Senna stays in his authored Madonna seat. No draw is needed because the standings engine's
/// countback is already deterministic. Applied identically to the live and replay runtime packs.
/// </summary>
public static class SmgpGridReshuffle
{
    public const string BenchmarkDriverId = "driver.ayrton_senna";

    public static SeasonPack ForNextSeason(
        SeasonPack pack,
        StandingsSnapshot previousFinal,
        string playerSeatLivery)
    {
        if (!string.Equals(pack.Manifest.CareerStyle, SmgpRules.CareerStyle, StringComparison.Ordinal))
            return pack;

        var teams = pack.Teams.ToDictionary(team => team.Id, StringComparer.Ordinal);
        var benchmarkEntry = pack.Entries.FirstOrDefault(entry =>
            string.Equals(entry.DriverId, BenchmarkDriverId, StringComparison.Ordinal));
        var movableEntries = pack.Entries
            .Where(entry => !string.Equals(entry.Ams2LiveryName, playerSeatLivery, StringComparison.Ordinal))
            .Where(entry => benchmarkEntry is null || !string.Equals(
                entry.Ams2LiveryName, benchmarkEntry.Ams2LiveryName, StringComparison.Ordinal))
            .ToList();
        if (movableEntries.Count < 2)
            return pack;

        var movableDrivers = movableEntries.Select(entry => entry.DriverId)
            .ToHashSet(StringComparer.Ordinal);
        var ranked = previousFinal.Drivers
            .Where(row => !string.Equals(
                row.DriverId, RoundGridResolver.SyntheticPlayerDriverId, StringComparison.Ordinal))
            .Where(row => movableDrivers.Contains(row.DriverId))
            .OrderBy(row => row.Position ?? int.MaxValue)
            .ThenByDescending(row => row.CountedPoints)
            .ThenBy(row => row.DriverId, StringComparer.Ordinal)
            .Select(row => row.DriverId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // The authored occupant of the player's car was benched and may have no standings row.
        // Append every such missing driver in authored order so the permutation remains complete.
        foreach (string driverId in movableEntries.Select(entry => entry.DriverId))
            if (!ranked.Contains(driverId, StringComparer.Ordinal))
                ranked.Add(driverId);
        if (ranked.Count != movableEntries.Count)
            return pack;

        var targetLiveries = movableEntries
            .Select((entry, index) => new
            {
                entry.Ams2LiveryName,
                Prestige = teams.GetValueOrDefault(entry.TeamId)?.Prestige ?? 0,
                Index = index,
            })
            .OrderByDescending(target => target.Prestige)
            .ThenBy(target => target.Index)
            .Select(target => target.Ams2LiveryName)
            .ToList();
        var driverByLivery = targetLiveries.Zip(ranked)
            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);

        return pack with
        {
            Entries = pack.Entries.Select(entry =>
                driverByLivery.TryGetValue(entry.Ams2LiveryName, out string? driverId)
                    ? entry with { DriverId = driverId }
                    : entry).ToList(),
        };
    }
}
