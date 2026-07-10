using System.Text.RegularExpressions;
using Companion.Ams2.CustomAi;
using Companion.Ams2.Packs;
using Companion.Core.Packs;

namespace Companion.Tests.Packs;

/// <summary>
/// Roster-drift guard for the SMGP replica pack (docs/dev/audits/audit-smgp-roster.md): the
/// skin override XMLs mirrored at <c>data/ams2/skin-seasons/smgp/</c> are the SOURCE OF TRUTH
/// for the fictional universe — <c>entries.json.ams2LiveryName</c> is the load-bearing binding
/// the staged AI file carries into the game, so every entry must byte-match a skin
/// <c>LIVERY_OVERRIDE NAME</c> (typos and all: "P. Kilnger" binds, "P. Klinger" pool-fills).
/// This suite failed for real once — the McLaren entries said "Iris #1"/"Azalea #8" while the
/// installed Kobra Fleetworks liveries (v1.1+) are #33/#34, so both cars pool-filled at staging.
/// </summary>
public class SmgpRosterDriftTests
{
    private static string PackDirectory =>
        Path.Combine(AppContext.BaseDirectory, "packs", "smgp-1");

    private static string SkinSetDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ams2", "skin-seasons", "smgp");

    private static readonly Lazy<SeasonPack> Pack = new(() => PackLoader.Parse(
        Read("pack.json"), Read("season.json"), Read("teams.json"),
        Read("drivers.json"), Read("entries.json")));

    private static string Read(string filePart)
    {
        string path = Path.Combine(PackDirectory, filePart);
        Assert.True(File.Exists(path), $"Pack file '{path}' missing — check the packs None-Include.");
        return File.ReadAllText(path);
    }

    /// <summary>Every active (uncommented) LIVERY_OVERRIDE NAME in the mirrored smgp skin set —
    /// the full 34-name universe (four formula_classic_g3m models + the mclaren_mp45b mod).</summary>
    private static readonly Lazy<IReadOnlyList<string>> SkinNames = new(() =>
    {
        Assert.True(Directory.Exists(SkinSetDirectory),
            $"'{SkinSetDirectory}' missing — check the skin-seasons None-Include.");
        var names = new List<string>();
        foreach (string file in Directory.EnumerateFiles(SkinSetDirectory, "*.xml").Order(StringComparer.Ordinal))
        {
            string text = LenientXml.StripComments(File.ReadAllText(file));
            foreach (var (_, name) in LenientXml.ExtractElementAttributePairs(
                text, "LIVERY_OVERRIDE", "LIVERY", "NAME"))
                names.Add(name);
        }
        return names;
    });

    [Fact]
    public void SkinSet_CarriesTheFull34NameUniverse()
    {
        Assert.Equal(34, SkinNames.Value.Count);
        Assert.Equal(34, SkinNames.Value.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryEntryLivery_ExistsVerbatimInTheSkinSet()
    {
        var universe = SkinNames.Value.ToHashSet(StringComparer.Ordinal);
        foreach (var entry in Pack.Value.Entries)
            Assert.True(universe.Contains(entry.Ams2LiveryName),
                $"entries.json binds '{entry.Ams2LiveryName}' ({entry.DriverId}) but no skin " +
                "override NAME matches byte-for-byte — the car will pool-fill in-game.");
    }

    [Fact]
    public void EveryEntryNumber_MatchesTheNumberEmbeddedInItsLiveryName()
    {
        foreach (var entry in Pack.Value.Entries)
        {
            var m = Regex.Match(entry.Ams2LiveryName, @"#(\d+)");
            Assert.True(m.Success, $"'{entry.Ams2LiveryName}' carries no #number.");
            Assert.Equal(m.Groups[1].Value, entry.Number);
        }
    }

    [Fact]
    public void EveryEntryLivery_LeadsWithItsTeamDisplayName()
    {
        var teamById = Pack.Value.Teams.ToDictionary(t => t.Id, StringComparer.Ordinal);
        foreach (var entry in Pack.Value.Entries)
            Assert.StartsWith(teamById[entry.TeamId].Name + " ", entry.Ams2LiveryName, StringComparison.Ordinal);
    }

    /// <summary>The unbound skin names must all be the authored RESERVES (drivers.json rows with
    /// no season entry) — a skin nobody owns means a roster hole, an entry short means drift.
    /// (The design-doc typo corrections, Elssler/Klinger, are both BOUND entries, so every
    /// reserve display name matches its skin initial + surname exactly.)</summary>
    [Fact]
    public void EveryUnboundSkinName_BelongsToAnAuthoredReserveDriver()
    {
        var bound = Pack.Value.Entries.Select(e => e.Ams2LiveryName).ToHashSet(StringComparer.Ordinal);
        var entryDrivers = Pack.Value.Entries.Select(e => e.DriverId).ToHashSet(StringComparer.Ordinal);
        var reserves = Pack.Value.Drivers.Where(d => !entryDrivers.Contains(d.Id)).ToList();

        var unbound = SkinNames.Value.Where(n => !bound.Contains(n)).ToList();
        Assert.Equal(reserves.Count, unbound.Count);

        foreach (string name in unbound)
        {
            var m = Regex.Match(name, @"#\d+\s+(\S)\.\s*(.+)$");
            Assert.True(m.Success, $"unbound skin '{name}' has no 'X. Surname' tail to match.");
            Assert.True(reserves.Any(d =>
                    d.Name.StartsWith(m.Groups[1].Value, StringComparison.Ordinal) &&
                    d.Name.EndsWith(m.Groups[2].Value, StringComparison.Ordinal)),
                $"unbound skin '{name}' matches no authored reserve driver — roster hole.");
        }
    }
}
