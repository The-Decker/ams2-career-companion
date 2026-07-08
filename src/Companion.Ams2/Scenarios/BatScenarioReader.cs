using System.Text.RegularExpressions;

namespace Companion.Ams2.Scenarios;

/// <summary>One "activate this override file" operation: copy <see cref="SourceRelativePath"/> over
/// <see cref="TargetRelativePath"/> (both relative to the AMS2 install root). The target is the ACTIVE
/// per-model override AMS2 reads (e.g. <c>...\formula_classic_g2m1\formula_classic_g2m1.xml</c>); the
/// source is the round's variant (e.g. <c>..._01Brazil.xml</c>).</summary>
public sealed record ScenarioSwap(string SourceRelativePath, string TargetRelativePath);

/// <summary>
/// Reads a community "scenario selector" batch file (e.g. <c>[F1-1988]Scenarios_FClassicGen2.bat</c>)
/// and extracts, per race round, the livery-override files it activates. Those packs rotate which
/// liveries are on the grid PER RACE (a pre-qualifying season fields a different 26 each round) by
/// copying a round-specific variant onto the active <c>&lt;model&gt;.xml</c> — the menu is just a
/// front-end for those copies. This reader lets the app do the same swap itself so the player never
/// has to run the .bat.
///
/// Only the livery-OVERRIDE copies are returned; the batch's copy of its own custom-AI file
/// (<c>CustomAIDrivers\...</c>) is deliberately excluded — the app writes that file itself (with the
/// career's per-race grid + character), and it must not be clobbered by the pack's static roster.
/// </summary>
public static class BatScenarioReader
{
    // A real-season round entry in a menu: if "%choice%"=="7" goto 1988_PaulRicard
    private static readonly Regex RoundGoto =
        new("""^\s*if\s+"%choice%"=="(\d+)"\s+goto\s+(\S+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // COPY <source> <target>  (paths here contain no spaces)
    private static readonly Regex Copy =
        new("""^\s*COPY\s+(\S+)\s+(\S+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <param name="seasonLabel">The batch label whose sub-menu lists the real-season rounds (default
    /// <c>:1988</c>). Rounds are read from that label's section up to the next top-level label (e.g.
    /// <c>:FICT</c>), so the "what-if" fictional scenarios are ignored.</param>
    public static IReadOnlyDictionary<int, IReadOnlyList<ScenarioSwap>> Parse(
        string batText, string seasonLabel = ":1988")
    {
        ArgumentNullException.ThrowIfNull(batText);
        var lines = batText.Replace("\r\n", "\n").Split('\n');

        // 1. round number -> section label, scoped to the real-season menu.
        int start = IndexOfLabel(lines, seasonLabel, 0);
        int menuEnd = start < 0 ? -1 : IndexOfNextLabel(lines, start + 1);
        var roundToLabel = new Dictionary<int, string>();
        for (int i = start + 1; start >= 0 && i < (menuEnd < 0 ? lines.Length : menuEnd); i++)
        {
            var m = RoundGoto.Match(lines[i]);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int round))
                roundToLabel[round] = m.Groups[2].Value.Trim();
        }

        // 2. each label's section -> its livery-override COPY swaps.
        var result = new Dictionary<int, IReadOnlyList<ScenarioSwap>>();
        foreach (var (round, label) in roundToLabel)
        {
            int section = IndexOfLabel(lines, ":" + label, 0);
            if (section < 0)
                continue;
            var swaps = new List<ScenarioSwap>();
            for (int i = section + 1; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith(':'))
                    break; // next section
                var m = Copy.Match(lines[i]);
                if (!m.Success)
                    continue;
                string src = Normalize(m.Groups[1].Value);
                string dst = Normalize(m.Groups[2].Value);
                if (src.Contains("Overrides", StringComparison.OrdinalIgnoreCase) &&
                    !src.Contains("CustomAIDrivers", StringComparison.OrdinalIgnoreCase))
                    swaps.Add(new ScenarioSwap(src, dst));
            }
            if (swaps.Count > 0)
                result[round] = swaps;
        }
        return result;
    }

    private static int IndexOfLabel(string[] lines, string label, int from)
    {
        for (int i = from; i < lines.Length; i++)
            if (lines[i].Trim().Equals(label, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static int IndexOfNextLabel(string[] lines, int from)
    {
        for (int i = from; i < lines.Length; i++)
            if (lines[i].TrimStart().StartsWith(':') && lines[i].Trim().Length > 1)
                return i;
        return -1;
    }

    /// <summary>Strips a leading <c>.\</c> and normalizes separators, leaving a path relative to the
    /// AMS2 install root (e.g. <c>Vehicles\Textures\...\formula_classic_g2m1.xml</c>).</summary>
    private static string Normalize(string path) =>
        path.Trim().TrimStart('.').TrimStart('\\', '/').Replace('/', '\\');
}
