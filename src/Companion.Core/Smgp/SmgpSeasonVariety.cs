using Companion.Core.Determinism;
using Companion.Core.Packs;

namespace Companion.Core.Smgp;

/// <summary>
/// SMGP season-to-season variety (Mike: "the second year is all the races random and the weather
/// much different than the first season"). Given the 1-based season ORDINAL, returns the calendar
/// that season should run:
/// <list type="bullet">
///   <item>Season 1 (or any non-SMGP pack), the authored pack, verbatim: the known baseline.</item>
///   <item>Season 2+ of an SMGP career, a seeded shuffle of every round's venue EXCEPT the finale
///     (kept as the Super Monaco GP climax), plus fresh per-round weather. Each ordinal draws a
///     different, deterministic calendar from the master seed, so a re-open (or any replay) shows
///     the same year again.</item>
/// </list>
///
/// <para>This is a deterministic FOLD INPUT. Race names reach derived headlines, while laps and
/// persisted weather can select character-effect conditions. Call sites therefore gate it per
/// career and apply the same pure transform to the pinned pack on both live and replay paths.
/// A legacy career whose gate is absent keeps the authored calendar byte-identically.</para>
/// </summary>
public static class SmgpSeasonVariety
{
    public static SeasonPack ForSeason(SeasonPack pack, int seasonOrdinal, long masterSeed)
    {
        if (seasonOrdinal <= 1 ||
            !string.Equals(pack.Manifest.CareerStyle, SmgpRules.CareerStyle, StringComparison.Ordinal) ||
            pack.Season.Rounds.Count < 3)
            return pack;

        var rounds = pack.Season.Rounds;
        int n = rounds.Count;

        // A deterministic Fisher-Yates over positions 0..n-2, the finale (n-1) stays Monaco.
        var order = new int[n - 1];
        for (int i = 0; i < order.Length; i++)
            order[i] = i;
        var shuffle = new Pcg32(Mix(masterSeed, seasonOrdinal, 0xADE), 0x5CE1);
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = shuffle.NextInt(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var newRounds = new List<PackRound>(n);
        for (int pos = 0; pos < n; pos++)
        {
            PackRound slot = rounds[pos];                                // keeps number/date/grid/points/timing
            PackRound venue = pos == n - 1 ? slot : rounds[order[pos]];  // the venue that lands at this position

            PackSetupGuide? setup = slot.SetupGuide is { } sg
                ? sg with
                {
                    Notes = venue.SetupGuide?.Notes ?? sg.Notes,        // the venue's note travels with it
                    Session = sg.Session with { WeatherSlots = Slots(masterSeed, seasonOrdinal, slot.Round, 3) },
                }
                : slot.SetupGuide;

            newRounds.Add(slot with
            {
                Name = venue.Name,
                Track = venue.Track,
                Laps = venue.Laps,
                History = venue.History,
                SetupGuide = setup,
                Weekend = Weathered(slot.Weekend, masterSeed, seasonOrdinal, slot.Round),
            });
        }

        return pack with { Season = pack.Season with { Rounds = newRounds } };
    }

    /// <summary>Re-weathers a round's weekend sessions (practice / qualifying / each race). Null
    /// weekend stays null; a session's other fields (label, duration) are untouched.</summary>
    private static PackWeekend? Weathered(PackWeekend? weekend, long seed, int ord, int round)
    {
        if (weekend is null)
            return null;
        return weekend with
        {
            Practice = weekend.Practice is { } p ? p with { WeatherSlots = Slots(seed, ord, round, 0) } : null,
            Qualifying = weekend.Qualifying is { } q ? q with { WeatherSlots = Slots(seed, ord, round, 1) } : null,
            Races = weekend.Races
                .Select((r, i) => r with { WeatherSlots = Slots(seed, ord, round, 2 + i) })
                .ToList(),
        };
    }

    // Weather pools by weekend "character", AMS2 display labels (see tools/author_weather.cs).
    private static readonly string[] Dry = ["Clear", "Clear", "Light Cloud", "Medium Cloud", "Hazy", "Overcast"];
    private static readonly string[] Showery = ["Overcast", "Medium Cloud", "Light Rain", "Light Rain", "Rain", "Overcast"];
    private static readonly string[] Wet = ["Overcast", "Rain", "Rain", "Heavy Rain", "Storm", "Light Rain"];

    /// <summary>Four seeded weather slots for one session. The whole ROUND shares a "character"
    /// (dry ~55% / showery ~30% / wet ~15%) so a weekend is coherent, while each session draws its
    /// own four slots, a wet weekend can still have a drier practice than its race.</summary>
    private static IReadOnlyList<string> Slots(long seed, int ord, int round, int session)
    {
        double character = new Pcg32(Mix(seed, ord, (uint)(round * 131 + 17)), 0xC1A).NextDouble();
        string[] pool = character < 0.55 ? Dry : character < 0.85 ? Showery : Wet;

        var rng = new Pcg32(Mix(seed, ord, (uint)(round * 977 + session * 31 + 5)), 0x51D);
        var slots = new string[4];
        for (int i = 0; i < 4; i++)
            slots[i] = pool[rng.NextInt(0, pool.Length)];
        return slots;
    }

    /// <summary>FNV-1a 64 over (masterSeed, seasonOrdinal, salt), a stable, build-independent seed
    /// for a Pcg32 stream, so the same career re-derives the same season calendar every time.</summary>
    private static ulong Mix(long seed, int ord, uint salt)
    {
        unchecked
        {
            ulong h = 1469598103934665603UL;
            void MixByte(byte b) { h ^= b; h *= 1099511628211UL; }
            for (int i = 0; i < 8; i++) MixByte((byte)((ulong)seed >> (i * 8)));
            for (int i = 0; i < 4; i++) MixByte((byte)((uint)ord >> (i * 8)));
            for (int i = 0; i < 4; i++) MixByte((byte)(salt >> (i * 8)));
            return h == 0 ? 0x9E3779B97F4A7C15UL : h; // Pcg32 seeds off zero fine, but keep it lively
        }
    }
}
