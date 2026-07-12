using Companion.Core.Packs;

namespace Companion.Tests.Smgp;

/// <summary>Loads the shipped smgp-1 pack from the test output (copied via the packs None-Include) —
/// the roster the SMGP reference-data guards check for full coverage.</summary>
internal static class SmgpTestPack
{
    public static SeasonPack Load()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "packs", "smgp-1");
        return PackLoader.Parse(
            File.ReadAllText(Path.Combine(dir, "pack.json")),
            File.ReadAllText(Path.Combine(dir, "season.json")),
            File.ReadAllText(Path.Combine(dir, "teams.json")),
            File.ReadAllText(Path.Combine(dir, "drivers.json")),
            File.ReadAllText(Path.Combine(dir, "entries.json")));
    }
}
