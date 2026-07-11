using System.Globalization;
using System.Text.RegularExpressions;
using Companion.Ams2.CustomAi;
using Companion.Ams2.Scenarios;

namespace Companion.Ams2.Skins;

/// <summary>Outcome of one model file's active-set rewrite.</summary>
public sealed record ActiveSetResult
{
    public required bool Changed { get; init; }
    public int Activated { get; init; }
    public IReadOnlyList<string> Displaced { get; init; } = [];
    public IReadOnlyList<string> NotFound { get; init; } = [];
    public string? BackupPath { get; init; }
}

/// <summary>
/// Swaps a 1985-style override file's ACTIVE livery set for a round's entry list. Those packs
/// ship a fixed budget of active <c>LIVERY_OVERRIDE</c> slots (51–60) plus ~20 alternates kept
/// INSIDE one giant comment with manual copy-paste instructions — this does the pack's own
/// documented procedure automatically: an alternate the round needs is COPIED out of the comment
/// into the slot of an active car the round does not field. The comment (and every alternate in
/// it) is preserved verbatim for future swaps; edits are line-level and never re-serialize the
/// (malformed) XML. Backup-first. Purely a skin-file operation — never the career DB / sim.
/// </summary>
public static class ActiveSetRewriter
{
    private static readonly Regex LiveryOpen =
        new(@"^\s*<LIVERY_OVERRIDE\s+LIVERY=""(?<slot>[^""]*)""\s+NAME=""(?<name>[^""]*)""",
            RegexOptions.Compiled);
    private static readonly Regex LiveryClose = new(@"</LIVERY_OVERRIDE>", RegexOptions.Compiled);
    private static readonly Regex SlotAttr = new(@"LIVERY=""[^""]*""", RegexOptions.Compiled);

    /// <summary>An alternate block found INSIDE a comment: its line span and NAME.</summary>
    public sealed record AlternateBlock(int StartLine, int EndLine, string Name);

    /// <summary>Every LIVERY_OVERRIDE block living inside <c>&lt;!-- --&gt;</c> comments — the
    /// 1985-style alternates. Blocks the game loads (outside comments) are NOT returned.</summary>
    public static IReadOnlyList<AlternateBlock> AlternateBlocks(IReadOnlyList<string> lines)
    {
        var comments = LenientXml.CommentSpans(string.Join("\n", lines));
        var lineStart = new int[lines.Count];
        for (int i = 1; i < lines.Count; i++)
            lineStart[i] = lineStart[i - 1] + lines[i - 1].Length + 1;

        var blocks = new List<AlternateBlock>();
        for (int i = 0; i < lines.Count; i++)
        {
            var m = LiveryOpen.Match(lines[i]);
            if (!m.Success || !LenientXml.IsInComment(lineStart[i] + m.Index, comments))
                continue;
            int end = i;
            for (int j = i; j < lines.Count; j++)
            {
                if (LiveryClose.IsMatch(lines[j])) { end = j; break; }
                if (j > i && LiveryOpen.IsMatch(lines[j])) { end = j - 1; break; }
                end = j;
            }
            blocks.Add(new AlternateBlock(i, end, m.Groups["name"].Value));
            i = end;
        }
        return blocks;
    }

    /// <summary>Names available in the file for activation: currently ACTIVE blocks plus the
    /// commented alternates. The caller intersects the round's grid liveries with this to know
    /// which names this model file can field at all.</summary>
    public static (IReadOnlyList<string> Active, IReadOnlyList<string> Alternates) AvailableNames(string xml)
    {
        var lines = SplitLines(xml);
        return (BubbleCarGraft.BlockGroups(lines).Select(g => g.Name).ToList(),
                AlternateBlocks(lines).Select(b => b.Name).ToList());
    }

    /// <summary>
    /// Rewrites <paramref name="path"/> so its active set fields <paramref name="desiredNames"/>:
    /// active blocks not in the set are displaced by alternates that are (slot reused); when no
    /// displaceable block remains, missing alternates are APPENDED on the next free slot while
    /// <paramref name="maxSlot"/> allows. Names found nowhere in the file are reported, not
    /// invented. No change needed → no write, no backup.
    /// </summary>
    public static ActiveSetResult Apply(
        string path, IReadOnlyCollection<string> desiredNames, int? maxSlot, DateTimeOffset now)
    {
        string xml = File.ReadAllText(path);
        var lines = SplitLines(xml).ToList();
        var active = BubbleCarGraft.BlockGroups(lines);
        var alternates = AlternateBlocks(lines);

        var desired = new HashSet<string>(desiredNames, StringComparer.Ordinal);
        var activeNames = new HashSet<string>(active.Select(g => g.Name), StringComparer.Ordinal);
        var missing = desiredNames.Where(n => !activeNames.Contains(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (missing.Count == 0)
            return new ActiveSetResult { Changed = false };

        // Displaceable = active blocks the round does not field, in file order.
        var displaceable = new Queue<BubbleCarGraft.BlockGroup>(
            active.Where(g => !desired.Contains(g.Name)));

        var usedSlots = new HashSet<int>(active
            .Select(g => int.TryParse(g.Slot, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) ? s : -1)
            .Where(s => s > 0));

        var notFound = new List<string>();
        var displacedNames = new List<string>();
        // (line span to replace | insert-at line, replacement lines)
        var replacements = new List<(int Start, int End, IReadOnlyList<string> NewLines)>();
        var appends = new List<IReadOnlyList<string>>();
        var usedAlternates = new HashSet<AlternateBlock>();
        int appendSlot = LiveryOverrideWriter.FirstCustomSlot;

        foreach (string name in missing)
        {
            var donor = alternates.FirstOrDefault(a =>
                string.Equals(a.Name, name, StringComparison.Ordinal) && !usedAlternates.Contains(a));
            if (donor is null)
            {
                notFound.Add(name);
                continue;
            }

            if (displaceable.TryDequeue(out var target))
            {
                usedAlternates.Add(donor);
                displacedNames.Add(target.Name);
                replacements.Add((target.StartLine, target.EndLine,
                    RenumberedBlock(lines, donor, target.Slot)));
            }
            else
            {
                // Nothing left to displace — append on the next free slot while the cap allows.
                while (usedSlots.Contains(appendSlot))
                    appendSlot++;
                if (maxSlot is { } cap && appendSlot > cap)
                {
                    notFound.Add(name);
                    continue;
                }
                usedAlternates.Add(donor);
                usedSlots.Add(appendSlot);
                appends.Add(RenumberedBlock(lines, donor,
                    appendSlot.ToString(CultureInfo.InvariantCulture)));
            }
        }

        if (replacements.Count == 0 && appends.Count == 0)
            return new ActiveSetResult { Changed = false, NotFound = notFound };

        // Apply replacements bottom-up so line indices stay valid.
        foreach (var (start, end, newLines) in replacements.OrderByDescending(r => r.Start))
        {
            lines.RemoveRange(start, end - start + 1);
            lines.InsertRange(start, newLines);
        }

        if (appends.Count > 0)
        {
            // Insert after the LAST active block (recomputed on the edited lines), before the
            // alternates comment.
            var editedActive = BubbleCarGraft.BlockGroups(lines);
            int insertAt = editedActive.Count > 0 ? editedActive[^1].EndLine + 1 : lines.Count;
            foreach (var block in appends)
            {
                lines.InsertRange(insertAt, block.Prepend(""));
                insertAt += block.Count + 1;
            }
        }

        string newline = xml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string backup = ScenarioApplier.BackUp(path, now);
        File.WriteAllText(path, string.Join(newline, lines), new System.Text.UTF8Encoding(false));

        return new ActiveSetResult
        {
            Changed = true,
            Activated = replacements.Count + appends.Count,
            Displaced = displacedNames,
            NotFound = notFound,
            BackupPath = backup,
        };
    }

    /// <summary>The donor block's lines with its <c>LIVERY="##"</c> (or any slot) renumbered to
    /// <paramref name="slot"/>. The donor lines stay in the comment untouched — this is a copy.</summary>
    private static IReadOnlyList<string> RenumberedBlock(
        IReadOnlyList<string> lines, AlternateBlock donor, string slot)
    {
        var block = new List<string>();
        for (int i = donor.StartLine; i <= donor.EndLine; i++)
        {
            string line = lines[i];
            if (i == donor.StartLine)
                line = SlotAttr.Replace(line, $"LIVERY=\"{slot}\"", 1);
            block.Add(line);
        }
        return block;
    }

    private static string[] SplitLines(string xml) => xml.Replace("\r\n", "\n").Split('\n');
}
