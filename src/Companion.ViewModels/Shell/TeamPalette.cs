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
    /// <summary>One team's authored primary/secondary livery identity. Single-colour teams repeat
    /// the primary so consumers can use the same two-colour binding contract without special cases.</summary>
    public readonly record struct TeamColors(string Primary, string Secondary);

    // Mike's canonical SMGP team colours. This is display-only presentation data: it never enters a
    // fold, journal row, grid scalar, result, or replay decision.
    private static readonly IReadOnlyDictionary<string, TeamColors> Curated =
        new Dictionary<string, TeamColors>(StringComparer.Ordinal)
        {
            ["team.iris"] = new("#F4F4F2", "#7137B8"),
            ["team.azalea"] = new("#F4F4F2", "#FF2D95"),
            ["team.bullets"] = new("#19CFF3", "#1749C7"),
            ["team.bestowal"] = new("#FFD500", "#73F20D"),
            ["team.millions"] = new("#2054C7", "#FFD500"),
            ["team.firenze"] = new("#E10600", "#E10600"),
            ["team.madonna"] = new("#E10600", "#FFD500"),
            ["team.lares"] = new("#111317", "#111317"),
            ["team.minarae"] = new("#FFD500", "#F4F4F2"),
            ["team.linden"] = new("#244BC4", "#244BC4"),
            ["team.dardan"] = new("#F0641E", "#F0641E"),
            ["team.rigel"] = new("#58EF24", "#58EF24"),
            ["team.serga"] = new("#17348F", "#19CFF3"),
            ["team.joke"] = new("#176B3A", "#176B3A"),
            ["team.losel"] = new("#FFD500", "#FFD500"),
            ["team.may"] = new("#19CFF3", "#19CFF3"),
            ["team.tyrant"] = new("#1749C7", "#F4F4F2"),
            ["team.blanche"] = new("#F4F4F2", "#1749C7"),

            // Remaining authored teams keep their established single-colour identities until the
            // product palette is explicitly frozen for them too.
            ["team.feet"] = new("#D64C8B", "#D64C8B"),
            ["team.cool"] = new("#A9B2BE", "#A9B2BE"),
            ["team.comet"] = new("#E7E8EA", "#E7E8EA"),
            ["team.orchis"] = new("#E4C319", "#E4C319"),
            ["team.moon"] = new("#28B7C9", "#28B7C9"),
            ["team.zeroforce"] = new("#7A8896", "#7A8896"),
        };

    /// <summary>The team's accent colour as a "#RRGGBB" hex string — curated where known, else a
    /// stable per-team hue so every card still reads as team-coloured.</summary>
    public static string For(string? teamId)
    {
        return ColorsFor(teamId).Primary;
    }

    /// <summary>The team's secondary livery colour, repeating the primary for a single-colour or
    /// derived team. UI surfaces can therefore render every team through one stable two-colour path.</summary>
    public static string SecondaryFor(string? teamId) => ColorsFor(teamId).Secondary;

    /// <summary>Returns the complete display-only colour identity for a team.</summary>
    public static TeamColors ColorsFor(string? teamId)
    {
        if (string.IsNullOrEmpty(teamId))
            return new("#8A929E", "#8A929E");
        if (Curated.TryGetValue(teamId, out TeamColors colors))
            return colors;
        string derived = DerivedHue(teamId);
        return new(derived, derived);
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
