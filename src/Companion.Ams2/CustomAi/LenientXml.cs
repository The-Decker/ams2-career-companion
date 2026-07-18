using System.Text;
using System.Text.RegularExpressions;

namespace Companion.Ams2.CustomAi;

/// <summary>
/// The lenient pre-processing shared by every reader of COMMUNITY-AUTHORED XML (custom-AI
/// NAMeS files, skin-pack livery overrides). Community files are routinely not well-formed:
/// header comments contain '--' runs (tables drawn with dashes), attribute values carry raw
/// ampersands ("Bang &amp; Olufsen" written as "Bang &amp;"), and attribute spacing is
/// nonstandard (<c>tracks ="..."</c>, which XML itself tolerates). The game reads these
/// files anyway, so must we: <see cref="Clean"/> repairs what a real XML parse can survive,
/// and <see cref="ExtractAttributeValues"/> is the last-resort scrape for files whose markup
/// is broken beyond parsing (mismatched tags, multiple roots, misplaced declarations).
/// </summary>
public static class LenientXml
{
    /// <summary>Comment-strips then ampersand-repairs <paramref name="text"/>, the standard
    /// cleaning pass to run before handing community XML to a real parser.</summary>
    public static string Clean(string text) => RepairBareAmpersands(StripComments(text));

    /// <summary>Removes every <c>&lt;!-- ... --&gt;</c> block by literal scan. Community
    /// headers draw tables with '-' runs, which is illegal inside XML comments, the whole
    /// comment goes, so the parser never sees it. An unterminated comment swallows the rest
    /// of the file (matching how browsers treat it).</summary>
    public static string StripComments(string text)
    {
        var result = new StringBuilder(text.Length);
        int position = 0;
        while (true)
        {
            int start = text.IndexOf("<!--", position, StringComparison.Ordinal);
            if (start < 0)
            {
                result.Append(text, position, text.Length - position);
                break;
            }

            result.Append(text, position, start - position);
            int end = text.IndexOf("-->", start + 4, StringComparison.Ordinal);
            if (end < 0)
                break;
            position = end + 3;
        }
        return result.ToString();
    }

    /// <summary>Escapes '&amp;' characters that do not start a character/entity reference —
    /// community names and texture paths like "Bang &amp; Olufsen" or "AMG&amp;co\skin.dds"
    /// are written with a raw ampersand.</summary>
    public static string RepairBareAmpersands(string text) =>
        BareAmpersand.Replace(text, "&amp;");

    /// <summary>The <c>&lt;!-- --&gt;</c> spans in <paramref name="text"/> (start inclusive, end
    /// exclusive), by the same literal scan as <see cref="StripComments"/>, for callers that must
    /// KEEP the text intact (in-place editors) but still know which regions AMS2 never parses
    /// (e.g. the 1985 pack keeps ~20 alternate LIVERY_OVERRIDE blocks inside one giant comment).
    /// An unterminated comment runs to end-of-text.</summary>
    public static IReadOnlyList<(int Start, int End)> CommentSpans(string text)
    {
        var spans = new List<(int, int)>();
        int i = 0;
        while (true)
        {
            int start = text.IndexOf("<!--", i, StringComparison.Ordinal);
            if (start < 0)
                break;
            int end = text.IndexOf("-->", start + 4, StringComparison.Ordinal);
            if (end < 0) { spans.Add((start, text.Length)); break; }
            spans.Add((start, end + 3));
            i = end + 3;
        }
        return spans;
    }

    /// <summary>Whether <paramref name="index"/> falls inside any of <paramref name="spans"/>
    /// (as returned by <see cref="CommentSpans"/>).</summary>
    public static bool IsInComment(int index, IReadOnlyList<(int Start, int End)> spans)
    {
        foreach (var (start, end) in spans)
            if (index >= start && index < end)
                return true;
        return false;
    }

    private static readonly Regex BareAmpersand =
        new(@"&(?!(?:[A-Za-z][A-Za-z0-9]*|#[0-9]+|#x[0-9A-Fa-f]+);)", RegexOptions.Compiled);

    /// <summary>
    /// Last-resort extraction for markup no XML parser survives: scrapes the values of
    /// <paramref name="attributeName"/> from every <paramref name="elementName"/> start tag
    /// by regex, element and attribute names case-insensitive, in document order. Empty
    /// values are skipped. Returns an empty list when nothing matches, the caller decides
    /// whether that makes the file a warning.
    /// </summary>
    public static IReadOnlyList<string> ExtractAttributeValues(
        string text, string elementName, string attributeName)
    {
        var pattern = new Regex(
            $@"<\s*{Regex.Escape(elementName)}\b[^>]*?\b{Regex.Escape(attributeName)}\s*=\s*""([^""]*)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return pattern.Matches(text)
            .Select(m => m.Groups[1].Value)
            .Where(v => v.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Last-resort extraction of a PAIR of attributes from every <paramref name="elementName"/>
    /// start tag (regex scrape, for markup no XML parser survives). For each element the value
    /// of <paramref name="requiredAttribute"/> must be present and non-empty (else the element is
    /// skipped); <paramref name="optionalAttribute"/> is captured when present, else "". Attribute
    /// order within the tag does not matter. In document order. Used to read
    /// <c>LIVERY_OVERRIDE</c>'s NAME (required) + LIVERY slot (optional, a "##" placeholder or
    /// missing slot is not active) together, so a livery's active/inactive state survives even a
    /// broken skin-pack file.
    /// </summary>
    public static IReadOnlyList<(string Optional, string Required)> ExtractElementAttributePairs(
        string text, string elementName, string optionalAttribute, string requiredAttribute)
    {
        var tag = new Regex(
            $@"<\s*{Regex.Escape(elementName)}\b([^>]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var optionalRx = AttributeRegex(optionalAttribute);
        var requiredRx = AttributeRegex(requiredAttribute);

        var pairs = new List<(string, string)>();
        foreach (Match m in tag.Matches(text))
        {
            string inner = m.Groups[1].Value;
            var required = requiredRx.Match(inner);
            if (!required.Success || required.Groups[1].Value.Length == 0)
                continue;
            var optional = optionalRx.Match(inner);
            pairs.Add((optional.Success ? optional.Groups[1].Value : "", required.Groups[1].Value));
        }
        return pairs;
    }

    private static Regex AttributeRegex(string attributeName) => new(
        $@"\b{Regex.Escape(attributeName)}\s*=\s*""([^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
