using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Companion.Ams2.CustomAi;

namespace Companion.Ams2.Skins;

/// <summary>The outcome of activating (or deactivating) a livery in a community override file.</summary>
public sealed record LiveryActivationResult
{
    public required bool Success { get; init; }

    /// <summary>The slot the livery was assigned when activated, or null on failure/deactivate.</summary>
    public int? Slot { get; init; }

    /// <summary>The timestamped backup taken before the write (never null on a successful write —
    /// the community file is snapshotted first, always).</summary>
    public string? BackupPath { get; init; }

    public required string Message { get; init; }

    public static LiveryActivationResult Failed(string message) =>
        new() { Success = false, Message = message };
}

/// <summary>
/// Turns a community skin-pack livery ON or OFF by editing the vehicle's <c>USER_OVERRIDES</c>
/// XML, the same job a community "livery selector" does, done safely from inside the app. AMS2
/// loads a <c>LIVERY_OVERRIDE</c> only when its <c>LIVERY</c> attribute is a real slot number; a
/// pack ships extra liveries as <c>LIVERY="##"</c> placeholders that never appear in-game until a
/// selector assigns them a slot. Activation assigns the next free slot; deactivation puts the
/// placeholder back.
///
/// Every edit is a MINIMAL, in-place TEXTUAL replacement of the one element's <c>LIVERY</c>
/// attribute, the rest of the file is preserved byte-for-byte. Community override files are
/// frequently not well-formed XML, so the writer never re-serializes (that could corrupt them);
/// it operates on the raw text with the same tolerance the scanner reads them with.
/// </summary>
public static class LiveryOverrideWriter
{
    /// <summary>Custom livery slots start at 51 (1..50 are stock/DLC).</summary>
    public const int FirstCustomSlot = 51;

    private static readonly Regex OverrideTag = new(
        @"<\s*LIVERY_OVERRIDE\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NameAttr = new(
        @"\bNAME\s*=\s*""([^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LiveryAttr = new(
        @"\bLIVERY\s*=\s*""([^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NumericLivery = new(
        @"\bLIVERY\s*=\s*""(\d+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The smallest custom slot (>= <see cref="FirstCustomSlot"/>) not already used by a
    /// numeric <c>LIVERY</c> in the file, the slot activation assigns next. Gaps are filled.</summary>
    public static int NextFreeSlot(string xml)
    {
        // Only COUNT slots AMS2 actually loads, strip <!-- --> comments first, so the "##"
        // placeholder examples + numbered example slots that live inside comments never occupy a
        // slot number (the AMS2 diagnosis: only non-commented numeric slots are real).
        string live = LenientXml.StripComments(xml);
        var used = new HashSet<int>();
        foreach (Match m in NumericLivery.Matches(live))
            if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                used.Add(n);

        int slot = FirstCustomSlot;
        while (used.Contains(slot))
            slot++;
        return slot;
    }

    /// <summary>Every non-commented <c>LIVERY_OVERRIDE</c> in the file: its NAME and whether it is
    /// ACTIVE (a numeric <c>LIVERY</c> slot AMS2 loads) vs an inactive placeholder. The read side of
    /// <see cref="Activate"/>/<see cref="Deactivate"/>, the per-race activator uses it to decide which
    /// liveries to switch on and which to park. Commented-out entries (AMS2 never loads them) are
    /// skipped, exactly as the writer refuses to edit them.</summary>
    public static IReadOnlyList<(string Name, bool Active)> Liveries(string xml)
    {
        var comments = CommentSpans(xml);
        var list = new List<(string Name, bool Active)>();
        foreach (Match tag in OverrideTag.Matches(xml))
        {
            if (IsInComment(tag.Index, comments))
                continue;
            var name = NameAttr.Match(tag.Value);
            if (!name.Success)
                continue;
            var livery = LiveryAttr.Match(tag.Value);
            bool active = livery.Success &&
                int.TryParse(livery.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            list.Add((name.Groups[1].Value, active));
        }
        return list;
    }

    /// <summary>Whether the numeric slot is free (not already used by a NON-commented entry).</summary>
    public static bool SlotIsFree(string xml, int slot)
    {
        foreach (Match m in NumericLivery.Matches(LenientXml.StripComments(xml)))
            if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) &&
                n == slot)
                return false;
        return true;
    }

    // Comment awareness (refusing to edit a LIVERY_OVERRIDE inside <!-- -->) is shared with every
    // other community-XML reader/editor via LenientXml.CommentSpans/IsInComment.
    private static IReadOnlyList<(int Start, int End)> CommentSpans(string xml) =>
        LenientXml.CommentSpans(xml);

    private static bool IsInComment(int index, IReadOnlyList<(int Start, int End)> spans) =>
        LenientXml.IsInComment(index, spans);

    /// <summary>
    /// Activates the FIRST placeholder <c>LIVERY_OVERRIDE</c> whose NAME matches
    /// <paramref name="liveryName"/> exactly (case-sensitive), sets its <c>LIVERY</c> to
    /// <paramref name="slot"/>. Returns the edited text, or null when no matching placeholder
    /// exists (already active, or the NAME is not in the file). Only the one attribute changes.
    /// </summary>
    public static string? Activate(string xml, string liveryName, int slot)
    {
        var comments = CommentSpans(xml);
        foreach (Match tag in OverrideTag.Matches(xml))
        {
            if (IsInComment(tag.Index, comments))
                continue; // a commented-out placeholder, AMS2 never loads it, so never edit it

            var name = NameAttr.Match(tag.Value);
            if (!name.Success || !string.Equals(name.Groups[1].Value, liveryName, StringComparison.Ordinal))
                continue;

            var livery = LiveryAttr.Match(tag.Value);
            // Skip an entry that is already active (numeric slot), activation targets placeholders.
            if (livery.Success &&
                int.TryParse(livery.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue;

            string newTag = livery.Success
                ? tag.Value[..livery.Index] + $"LIVERY=\"{slot.ToString(CultureInfo.InvariantCulture)}\"" +
                  tag.Value[(livery.Index + livery.Length)..]
                : InsertLiverySlot(tag.Value, slot);

            return xml[..tag.Index] + newTag + xml[(tag.Index + tag.Length)..];
        }
        return null;
    }

    /// <summary>
    /// Deactivates the FIRST active <c>LIVERY_OVERRIDE</c> whose NAME matches
    /// <paramref name="liveryName"/> exactly, puts its <c>LIVERY</c> back to <c>##</c>. Returns
    /// the edited text, or null when the NAME has no active entry to turn off.
    /// </summary>
    public static string? Deactivate(string xml, string liveryName)
    {
        var comments = CommentSpans(xml);
        foreach (Match tag in OverrideTag.Matches(xml))
        {
            if (IsInComment(tag.Index, comments))
                continue;

            var name = NameAttr.Match(tag.Value);
            if (!name.Success || !string.Equals(name.Groups[1].Value, liveryName, StringComparison.Ordinal))
                continue;

            var livery = LiveryAttr.Match(tag.Value);
            if (!livery.Success ||
                !int.TryParse(livery.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue; // not active, nothing to turn off here

            string newTag = tag.Value[..livery.Index] + "LIVERY=\"##\"" + tag.Value[(livery.Index + livery.Length)..];
            return xml[..tag.Index] + newTag + xml[(tag.Index + tag.Length)..];
        }
        return null;
    }

    private static string InsertLiverySlot(string tag, int slot)
    {
        // Insert LIVERY="slot" immediately after the element name when the tag has no LIVERY attr.
        var m = Regex.Match(tag, @"<\s*LIVERY_OVERRIDE\b", RegexOptions.IgnoreCase);
        int at = m.Index + m.Length;
        return tag[..at] + $" LIVERY=\"{slot.ToString(CultureInfo.InvariantCulture)}\"" + tag[at..];
    }

    // ---------- backup-first file I/O ----------

    private const string BackupDirectoryName = "_companion-backups";

    /// <summary>
    /// Activates <paramref name="liveryName"/> in the override file at <paramref name="overrideXmlPath"/>,
    /// snapshotting the file first (a MINIMAL edit is written only after the backup succeeds). The
    /// slot is the next free one unless <paramref name="slot"/> is given. Returns the result carrying
    /// the assigned slot + backup path, or a failure the caller surfaces. Never throws on the normal
    /// "not found / already active" paths.
    /// </summary>
    public static LiveryActivationResult ActivateInFile(
        string overrideXmlPath, string liveryName, DateTimeOffset now, int? slot = null, int? maxSlot = null)
    {
        string xml;
        try
        {
            xml = File.ReadAllText(overrideXmlPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LiveryActivationResult.Failed($"Could not read the skin file: {ex.Message}");
        }

        // AMS2 loads ONLY the single loose <vehicle>.xml (verified against the exe's ReplacementSystem
        // loader), sibling files like <vehicle>_dist.xml are inert templates it never reads. So the
        // free-slot search is scoped to THIS file only (comments stripped inside NextFreeSlot).
        int assigned = slot ?? NextFreeSlot(xml);
        if (!SlotIsFree(xml, assigned))
            return LiveryActivationResult.Failed(
                $"Slot {assigned} is already in use in {Path.GetFileName(overrideXmlPath)}.");

        // Respect the class's livery cap, AMS2 will not load a slot beyond the car's livery count,
        // so writing one would silently do nothing. Refuse loudly instead.
        if (maxSlot is { } max && assigned > max)
            return LiveryActivationResult.Failed(
                $"This class is at its livery limit ({max - FirstCustomSlot + 1} liveries), AMS2 can't show another. " +
                "Deactivate a livery you don't need first, then activate this one.");

        string? edited = Activate(xml, liveryName, assigned);
        if (edited is null)
            return LiveryActivationResult.Failed(
                $"“{liveryName}” has no inactive placeholder to activate in {Path.GetFileName(overrideXmlPath)} " +
                "(it may already be active, or the name may differ).");

        string backup;
        try
        {
            backup = Backup(overrideXmlPath, now);
            File.WriteAllText(overrideXmlPath, edited, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LiveryActivationResult.Failed($"Could not write the skin file: {ex.Message}");
        }

        return new LiveryActivationResult
        {
            Success = true,
            Slot = assigned,
            BackupPath = backup,
            Message = $"Activated “{liveryName}” as livery slot {assigned}. AMS2 will show it after a restart. " +
                      $"Original backed up to {Path.GetFileName(backup)}.",
        };
    }

    /// <summary>Timestamped snapshot of the override file into a <c>_companion-backups</c> subfolder
    /// beside it, mirrors the custom-AI backup contract (never overwrite a community file without a
    /// snapshot first). Same-second collisions get a <c>-n</c> suffix.</summary>
    public static string Backup(string overrideXmlPath, DateTimeOffset now)
    {
        string directory = Path.GetDirectoryName(overrideXmlPath)!;
        string backupDir = Path.Combine(directory, BackupDirectoryName);
        Directory.CreateDirectory(backupDir);

        string name = Path.GetFileNameWithoutExtension(overrideXmlPath);
        string stem = Path.Combine(backupDir, $"{name}.{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");
        string backupPath = stem + ".xml";
        for (int n = 2; File.Exists(backupPath); n++)
            backupPath = $"{stem}-{n}.xml";
        File.Copy(overrideXmlPath, backupPath, overwrite: false);
        return backupPath;
    }
}
