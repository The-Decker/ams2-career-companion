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
/// copying a round-specific variant onto the active <c>&lt;model&gt;.xml</c>, the menu is just a
/// front-end for those copies. This reader lets the app do the same swap itself so the player never
/// has to run the .bat.
///
/// Only the livery-OVERRIDE copies are returned; the batch's copy of its own custom-AI file
/// (<c>CustomAIDrivers\...</c>) is deliberately excluded, the app writes that file itself (with the
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

    // A confirm-then-install dispatch: the round's menu section prints its changes, asks Y/N, then
    // `goto INSTALL_1996_SANMARINO` (some packs `call :INSTALL_...`). The newer selector packs
    // (1995 F-Edge, 1996-1997 F-V10 G1, 2010 F-V8 G3, 2016 F-Hybrid G1) split the COPY block into a
    // separate INSTALL_ section this way; the 1988 pack copies inline with no such hop. Only
    // INSTALL_-prefixed jumps are followed, never the section's own `goto main_menu` / `goto 1996`
    // (the cancel / back-to-menu paths), which would wander into unrelated sections.
    private static readonly Regex InstallJump =
        new("""^\s*(?:goto|call)\s+:?(INSTALL_\S+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <param name="seasonLabel">The batch label whose sub-menu lists the real-season rounds (default
    /// <c>:1988</c>; production passes <c>:&lt;pack year&gt;</c>, a class's selector can serve several
    /// years, e.g. <c>[F1_1996-1997]…</c> has both <c>:1996</c> and <c>:1997</c> menus). Rounds are read
    /// from that label's section up to the next top-level label (e.g. <c>:1997</c> / <c>:WHAT_IF</c>), so
    /// the other season's menu and the "what-if" fictional scenarios are ignored.</param>
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

        // 2. each label's section -> its livery-override COPY swaps, following a confirm section's
        //    `goto INSTALL_*` hop to the install block that holds the actual copies.
        var result = new Dictionary<int, IReadOnlyList<ScenarioSwap>>();
        foreach (var (round, label) in roundToLabel)
        {
            var swaps = new List<ScenarioSwap>();
            CollectSwaps(lines, label, swaps, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (swaps.Count > 0)
                result[round] = swaps;
        }
        return result;
    }

    /// <summary>Accumulates the livery-override COPY swaps reachable from section
    /// <paramref name="label"/>: the copies written directly in it (the 1988 pack) plus those in any
    /// <c>INSTALL_*</c> block it dispatches to (the confirm-then-install packs). Depth-first and
    /// visited-guarded so a section reached twice, or an INSTALL block that jumps onward, cannot loop.
    /// Only livery OVERRIDE copies are kept; the pack's own <c>CustomAIDrivers\</c> copy (including the
    /// PERF_MODE-conditional variants) is excluded, the app writes that file itself.</summary>
    private static void CollectSwaps(string[] lines, string label, List<ScenarioSwap> swaps, HashSet<string> visited)
    {
        if (!visited.Add(label))
            return;
        int section = IndexOfLabel(lines, ":" + label, 0);
        if (section < 0)
            return;
        for (int i = section + 1; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith(':'))
                break; // next section

            var jump = InstallJump.Match(lines[i]);
            if (jump.Success)
            {
                CollectSwaps(lines, jump.Groups[1].Value.Trim(), swaps, visited);
                continue;
            }

            var m = Copy.Match(lines[i]);
            if (!m.Success)
                continue;
            string src = Normalize(m.Groups[1].Value);
            string dst = Normalize(m.Groups[2].Value);
            if (src.Contains("Overrides", StringComparison.OrdinalIgnoreCase) &&
                !src.Contains("CustomAIDrivers", StringComparison.OrdinalIgnoreCase))
                swaps.Add(new ScenarioSwap(src, dst));
        }
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
