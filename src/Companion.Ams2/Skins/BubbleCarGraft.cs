using System.Text.RegularExpressions;

namespace Companion.Ams2.Skins;

/// <summary>
/// Grafts a "bubble" car's livery (a car outside a round's active pool — e.g. 1988 Coloni/Eurobrun on
/// a round they DNQ'd) into an override model file, taking a displaced same-model peer's slot. AMS2's
/// override files hold, per slot, THREE blocks — LIVERY_OVERRIDE (car), HELMET_OVERRIDE, OUTFIT_OVERRIDE
/// — all keyed by the same <c>LIVERY="NN"</c>. The community files are malformed (stray <c>--&gt;</c>
/// after each <c>&lt;/LIVERY_OVERRIDE&gt;</c>), so this works PURELY at the line level: it never
/// re-serializes, only lifts a car's block-group verbatim and renumbers its slot. The active model file
/// is regenerated from the pack's per-round variant on every stage (and backed up first), so a graft is
/// disposable/recoverable. Cosmetic (skin) only — never touches the sim.
/// </summary>
public static class BubbleCarGraft
{
    // A car's block-group runs from its "<LIVERY_OVERRIDE LIVERY=... NAME=..." line to the following
    // "</OUTFIT_OVERRIDE>" line (LIVERY -> HELMET -> OUTFIT, consecutive per slot).
    private static readonly Regex LiveryOpen =
        new(@"^\s*<LIVERY_OVERRIDE\s+LIVERY=""(?<slot>[^""]*)""\s+NAME=""(?<name>[^""]*)""",
            RegexOptions.Compiled);
    private static readonly Regex OutfitClose = new(@"</OUTFIT_OVERRIDE>", RegexOptions.Compiled);

    /// <summary>A car's contiguous block-group inside an override file (line span + slot + name).</summary>
    public sealed record BlockGroup(int StartLine, int EndLine, string Slot, string Name);

    /// <summary>Every car block-group in <paramref name="lines"/>, in file order.</summary>
    public static IReadOnlyList<BlockGroup> BlockGroups(IReadOnlyList<string> lines)
    {
        var groups = new List<BlockGroup>();
        for (int i = 0; i < lines.Count; i++)
        {
            var m = LiveryOpen.Match(lines[i]);
            if (!m.Success)
                continue;
            int end = i;
            for (int j = i; j < lines.Count; j++)
            {
                if (OutfitClose.IsMatch(lines[j])) { end = j; break; }
                // A malformed group with no OUTFIT close ends at the next livery block.
                if (j > i && LiveryOpen.IsMatch(lines[j])) { end = j - 1; break; }
                end = j;
            }
            groups.Add(new BlockGroup(i, end, m.Groups["slot"].Value, m.Groups["name"].Value));
            i = end;
        }
        return groups;
    }

    /// <summary>
    /// Replaces the block-group of <paramref name="displaceName"/> in <paramref name="activeXml"/> with
    /// the block-group of <paramref name="playerName"/> lifted from <paramref name="sourceXml"/>, renumbered
    /// to the displaced slot. Returns the edited active text, or null when either car's block-group is not
    /// found (nothing is written — the caller leaves the file untouched).
    /// </summary>
    public static string? Graft(string activeXml, string sourceXml, string playerName, string displaceName)
    {
        var activeLines = SplitLines(activeXml);
        var sourceLines = SplitLines(sourceXml);

        var target = BlockGroups(activeLines).FirstOrDefault(g => g.Name == displaceName);
        var donor = BlockGroups(sourceLines).FirstOrDefault(g => g.Name == playerName);
        if (target is null || donor is null)
            return null;

        string slot = target.Slot;
        // Lift the donor's lines and renumber every LIVERY="donorSlot" -> LIVERY="targetSlot" (the
        // LIVERY_OVERRIDE / HELMET_OVERRIDE / OUTFIT_OVERRIDE tags all carry the slot).
        var grafted = new List<string>();
        for (int i = donor.StartLine; i <= donor.EndLine; i++)
            grafted.Add(Regex.Replace(sourceLines[i], @"LIVERY=""" + Regex.Escape(donor.Slot) + @"""",
                @"LIVERY=""" + slot + @""""));

        var outLines = new List<string>(activeLines.Length);
        outLines.AddRange(activeLines.Take(target.StartLine));
        outLines.AddRange(grafted);
        outLines.AddRange(activeLines.Skip(target.EndLine + 1));
        string newline = activeXml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return string.Join(newline, outLines);
    }

    /// <summary>Names of every active (non-comment, block-group) car in the file, in slot order —
    /// the pool AMS2 shows for that model.</summary>
    public static IReadOnlyList<string> ActiveNames(string xml) =>
        BlockGroups(SplitLines(xml)).Select(g => g.Name).ToList();

    // Line split that round-trips CRLF or LF via a final join on "\n" (caller controls newline policy;
    // these community files are edited in place with the writer's minimal-diff convention).
    private static string[] SplitLines(string xml) => xml.Replace("\r\n", "\n").Split('\n');
}
