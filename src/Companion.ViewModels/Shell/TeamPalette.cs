namespace Companion.ViewModels.Shell;

/// <summary>
/// A team's accent COLOUR for the cinematic starting grid (and future team-coloured chrome) — the
/// hex string a card's position box / name accent / edge glow use. SMGP's teams get curated colours
/// (inferred from the mode's liveries); any other team (or an unmapped one) falls back to a stable,
/// distinct hue derived from its id, so every card is still team-coloured. Display-only — never a
/// fold input. Phase 2 will move the curated table to an editable data file.
/// </summary>
public static class TeamPalette
{
    // Curated SMGP team accents (best-fit from the mode's car liveries; easy to retune per team).
    private static readonly IReadOnlyDictionary<string, string> Curated =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["team.madonna"] = "#E10600", // scarlet — the crown
            ["team.bullets"] = "#F5333F", // insurgent red
            ["team.tyrant"] = "#12A594", // teal
            ["team.bestowal"] = "#8BC53F", // lime
            ["team.millions"] = "#F4C20D", // gold
            ["team.losel"] = "#2E9E5B", // green
            ["team.may"] = "#28B7C9", // cyan
            ["team.blanche"] = "#2D6CDF", // blue
            ["team.joke"] = "#59B85A", // grass green
            ["team.dardan"] = "#8E5CE0", // violet
            ["team.minarae"] = "#F2B01E", // amber (yellow helmet team)
            ["team.lares"] = "#E0662A", // orange
            ["team.feet"] = "#D64C8B", // magenta
            ["team.zeroforce"] = "#7A8896", // slate — the floor
        };

    /// <summary>The team's accent colour as a "#RRGGBB" hex string — curated where known, else a
    /// stable per-team hue so every card still reads as team-coloured.</summary>
    public static string For(string? teamId)
    {
        if (string.IsNullOrEmpty(teamId))
            return "#8A929E";
        if (Curated.TryGetValue(teamId, out var hex))
            return hex;
        return DerivedHue(teamId);
    }

    /// <summary>A deterministic, pleasant accent for an unmapped team: FNV-1a(teamId) → a hue on a
    /// fixed saturation/lightness so the colour is vivid, distinct and stable across runs.</summary>
    private static string DerivedHue(string teamId)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in teamId) { h ^= c; h *= 16777619; }
            double hue = h % 360u;
            return HslToHex(hue, 0.62, 0.55);
        }
    }

    private static string HslToHex(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return $"#{Byte(r + m):X2}{Byte(g + m):X2}{Byte(b + m):X2}";
    }

    private static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
}
