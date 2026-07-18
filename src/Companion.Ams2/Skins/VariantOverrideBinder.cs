using System.Globalization;
using System.Text.RegularExpressions;
using Companion.Ams2.Scenarios;

namespace Companion.Ams2.Skins;

/// <summary>Outcome of binding one round's per-race variant overrides.</summary>
public sealed record VariantBindResult
{
    public required int Swapped { get; init; }
    public required int Restored { get; init; }
    public IReadOnlyList<string> Backups { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];

    public bool AnyChanged => Swapped + Restored > 0;
}

/// <summary>
/// Race-by-race livery binding for packs WITHOUT a scenario .bat: the big season skin packs ship
/// per-race override variants beside the active pointer (<c>formula_classic_g4m1_03Imola.xml</c>,
/// <c>formula_v10_g1_1997_08FRA.xml</c>, <c>formula_classic_g3m4_01_USA.xml</c>,
/// <c>formula_ultimate_2016_2016_01AUS.xml</c>…) for MANUAL copying, the game only reads
/// <c>&lt;model&gt;.xml</c>. When staging round N this binder finds each model's variant whose
/// file-name token matches the round (round number and/or venue/country) and copies it over the
/// active pointer, backup-first, exactly what the community selector .bats automate for their own
/// packs. Rounds with no variant restore the model's BASE pointer (season library content when the
/// pack declares a skin season, else the base snapshot taken before the first swap). Purely a
/// skin-file operation, never the career DB / sim / oracle.
/// </summary>
public static class VariantOverrideBinder
{
    /// <summary>Round-number + trailing token of a variant suffix: optional separators, 1–2
    /// digits, then an optional tail (e.g. <c>03Imola</c>, <c>08FRA</c>, <c>01_USA</c>,
    /// <c>08Great-Britain</c>).</summary>
    private static readonly Regex RoundToken =
        new(@"^(?<round>\d{1,2})[_ -]*(?<tail>[A-Za-z][A-Za-z -]*)?$", RegexOptions.Compiled);

    /// <summary>Token vocabulary observed across the installed community packs, ISO-ish country
    /// codes, country names AND circuit nicknames (the formal pack venue "Autódromo José Carlos
    /// Pace" never contains the pack author's "Interlagos") → the Grand-Prix-name keywords the
    /// round must contain. A token OUTSIDE this vocabulary that also fails the venue-word match
    /// neither confirms nor vetoes, the round number decides.</summary>
    private static readonly Dictionary<string, string[]> KnownTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // country/GP codes + names
        ["aus"] = ["australian"], ["australia"] = ["australian"],
        ["bra"] = ["brazilian"], ["brazil"] = ["brazilian"],
        ["smr"] = ["san marino"], ["rsm"] = ["san marino"], ["sanmarino"] = ["san marino"],
        ["mon"] = ["monaco"], ["mco"] = ["monaco"], ["monaco"] = ["monaco"],
        ["mex"] = ["mexic"], ["mexico"] = ["mexic"],
        ["can"] = ["canadian"], ["canada"] = ["canadian"],
        ["fra"] = ["french"], ["france"] = ["french"],
        ["gbr"] = ["british"], ["uk"] = ["british"], ["greatbritain"] = ["british"],
        ["ger"] = ["german"], ["deu"] = ["german"], ["germany"] = ["german"],
        ["hun"] = ["hungarian"], ["hungary"] = ["hungarian"],
        ["bel"] = ["belgian"], ["belgium"] = ["belgian"],
        ["ita"] = ["italian"], ["italy"] = ["italian"],
        ["por"] = ["portuguese"], ["prt"] = ["portuguese"], ["portugal"] = ["portuguese"],
        ["esp"] = ["spanish"], ["spain"] = ["spanish"],
        ["jpn"] = ["japanese"], ["japan"] = ["japanese"],
        ["usa"] = ["united states", "detroit", "caesars", "dallas", "indianapolis"],
        ["eur"] = ["european"],
        ["rsa"] = ["south african"], ["zaf"] = ["south african"], ["saf"] = ["south african"],
        ["uae"] = ["abu dhabi"], ["abu"] = ["abu dhabi"],
        ["bhr"] = ["bahrain", "sakhir"], ["brn"] = ["bahrain"], ["skh"] = ["sakhir"],
        ["chn"] = ["chinese"], ["tur"] = ["turkish"],
        ["sgp"] = ["singapore"], ["sin"] = ["singapore"],
        ["kor"] = ["korean"], ["ind"] = ["indian"], ["rus"] = ["russian"], ["aze"] = ["azerbaijan"],
        ["nld"] = ["dutch"], ["ned"] = ["dutch"],
        ["aut"] = ["austrian"], ["austria"] = ["austrian"], ["sty"] = ["styrian"],
        ["arg"] = ["argentine"], ["swe"] = ["swedish"], ["lux"] = ["luxembourg"],
        ["pac"] = ["pacific"], ["mys"] = ["malaysian"], ["mal"] = ["malaysian"],
        ["emi"] = ["emilia"], ["tus"] = ["tuscan"], ["eif"] = ["eifel"],
        ["detroit"] = ["detroit", "united states"],
        // circuit nicknames (community tails), the GP they imply
        ["interlagos"] = ["brazilian"], ["int"] = ["brazilian"],
        ["imola"] = ["san marino", "emilia"], ["imo"] = ["san marino", "emilia"],
        ["spa"] = ["belgian"],
        ["monza"] = ["italian"],
        ["suzuka"] = ["japanese"], ["suz"] = ["japanese"],
        ["adelaide"] = ["australian"], ["ade"] = ["australian"],
        ["estoril"] = ["portuguese"],
        ["kyalami"] = ["south african"],
        ["silverstone"] = ["british"], ["sil"] = ["british"],
        ["donington"] = ["european"],
        ["hockenheim"] = ["german"],
        ["hungaroring"] = ["hungarian"],
        ["montreal"] = ["canadian"],
        ["phoenix"] = ["united states"],
        ["jerez"] = ["spanish", "european"],
        ["barcelona"] = ["spanish"], ["bar"] = ["spanish"],
        ["magnycours"] = ["french"], ["paulricard"] = ["french"],
        ["zandvoort"] = ["dutch"],
        ["mugello"] = ["tuscan"],
        ["nurburgring"] = ["european", "german", "luxembourg", "eifel"],
        ["brandshatch"] = ["british", "european"],
        ["zolder"] = ["belgian"],
        ["watkinsglen"] = ["united states"],
        ["mosport"] = ["canadian"],
        ["fuji"] = ["japanese"],
        ["istanbul"] = ["turkish"],
        ["sepang"] = ["malaysian"],
        ["sochi"] = ["russian"],
        ["baku"] = ["azerbaijan", "european"],
        ["melbourne"] = ["australian"],
        ["yasmarina"] = ["abu dhabi"],
    };

    /// <summary>One round of the pack calendar, for anchoring variants.</summary>
    public readonly record struct CalendarRound(int Number, string Name, string? RealVenue);

    /// <summary>
    /// The round a variant file is ANCHORED to, the round it names, from which it applies
    /// ONWARD (community packs ship change-point sets: 1986's <c>02Canada</c> is "the grid from
    /// the Canadian GP on", 1990's <c>15_JPN</c> carries Herbert-replaces-Donnelly through the
    /// end of the season). Anchor resolution, in order:
    /// venue/country tail when the vocabulary can place it on a round (the file NUMBER is often a
    /// change-set index, not the round, 1986's Canada is set 02 but round 6); else the file
    /// number when it is a plausible round. A year-prefixed file from ANOTHER season (the 1998
    /// skinpack shares its class folder with our 2000 pack) never anchors, nor do what-if /
    /// special files (<c>WhatIf_*</c>, <c>54WIF</c>, <c>Sen</c>, <c>00Alesi</c>, <c>_dist</c>).
    /// Null = the file targets no round of this season.
    /// </summary>
    public static int? AnchorRound(string suffix, IReadOnlyList<CalendarRound> rounds, int seasonYear)
    {
        string s = suffix.Trim('_', ' ', '-');
        if (s.Length == 0 || s.Contains("whatif", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("wif", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith("_dist", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "dist", StringComparison.OrdinalIgnoreCase))
            return null;

        // A leading 4-digit year must be THIS season's ("1998_06Monaco" on the shared F-V10_Gen2
        // folder belongs to the 1998 pack, not our 2000 season).
        var year = Regex.Match(s, @"^(19|20)\d{2}(?<sep>[_ -]+)");
        if (year.Success)
        {
            if (int.Parse(s[..4], CultureInfo.InvariantCulture) != seasonYear)
                return null;
            s = s[year.Length..];
        }

        var m = RoundToken.Match(s);
        int? fileNumber = null;
        string tail = s;
        if (m.Success)
        {
            fileNumber = int.Parse(m.Groups["round"].Value, CultureInfo.InvariantCulture);
            tail = m.Groups["tail"].Value;
        }
        else if (!Regex.IsMatch(s, @"^[A-Za-z][A-Za-z -]*$"))
            return null;

        // Venue-primary: the tail names the GP (nickname, country code or country name).
        if (tail.Length > 0)
        {
            var agreed = rounds.Where(r => TailAgrees(tail, r.Name, r.RealVenue) == true)
                .Select(r => r.Number)
                .ToList();
            if (agreed.Count == 1)
                return agreed[0];
            if (agreed.Count > 1)
                return fileNumber is { } n && agreed.Contains(n) ? n : agreed[0];
            // A tail we can positively interpret that fits NO round of this season → not ours.
            string token = Regex.Replace(RemoveDiacritics(tail), "[^A-Za-z]", "");
            if (KnownTokens.ContainsKey(token))
                return null;
        }

        // Number-fallback: an unknown (or absent) tail with a plausible round number.
        int last = rounds.Count > 0 ? rounds.Max(r => r.Number) : 0;
        return fileNumber is { } number && number >= 1 && number <= last ? number : null;
    }

    /// <summary>True/false when the tail positively confirms/contradicts the round; null when the
    /// token is outside the vocabulary AND fails the venue-word match (unknown). A known token
    /// still confirms via the venue words (2020's "05Silverstone" names the 70th Anniversary
    /// round, whose NAME has no "british", but its venue is Silverstone).</summary>
    private static bool? TailAgrees(string tail, string roundName, string? realVenue)
    {
        string token = Regex.Replace(RemoveDiacritics(tail), "[^A-Za-z]", "");
        if (token.Length == 0)
            return null;
        bool known = KnownTokens.TryGetValue(token, out var keywords);
        if (known && keywords!.Any(k => roundName.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (token.Length >= 4 && VenueTokenMatch(token, roundName, realVenue))
            return true;
        return known ? false : null;
    }

    /// <summary>Venue-name agreement à la import_jusk_ai: the tail and any venue/name word share
    /// a ≥4-char prefix (so <c>Imola</c> matches "Imola", <c>Suzuka</c> matches "Suzuka Circuit",
    /// <c>Interlagos</c> matches "Autódromo José Carlos Pace (Interlagos)").</summary>
    private static bool VenueTokenMatch(string tail, string roundName, string? realVenue)
    {
        if (tail.Length < 4)
            return false;
        var words = Regex.Matches($"{realVenue} {roundName}", @"[A-Za-zÀ-ÿ]{4,}")
            .Select(w => RemoveDiacritics(w.Value));
        string t = RemoveDiacritics(tail);
        foreach (var w in words)
        {
            int shared = SharedPrefixLength(t, w);
            if (shared >= 4 && (shared == t.Length || shared == w.Length))
                return true;
        }
        return false;
    }

    private static int SharedPrefixLength(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length), i = 0;
        while (i < n && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i]))
            i++;
        return i;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized.Where(c =>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
            System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray());
    }

    /// <summary>Whether <paramref name="variantXml"/> shares at least one active
    /// <c>LIVERY_OVERRIDE NAME</c> with <paramref name="seasonBaseXml"/>, the test for a variant
    /// BELONGING to the active season (a change-point off its base) versus a foreign season's file
    /// squatting in a shared car-model folder. Comment-stripped so placeholder examples never
    /// count; an unreadable variant (empty text) shares nothing → treated as not-ours.</summary>
    internal static bool SharesAnyLiveryName(string seasonBaseXml, string variantXml)
    {
        var baseNames = LiveryNames(seasonBaseXml);
        return baseNames.Count > 0 && LiveryNames(variantXml).Overlaps(baseNames);
    }

    private static HashSet<string> LiveryNames(string xml)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, name) in Companion.Ams2.CustomAi.LenientXml.ExtractElementAttributePairs(
            Companion.Ams2.CustomAi.LenientXml.StripComments(xml), "LIVERY_OVERRIDE", "LIVERY", "NAME"))
            names.Add(name);
        return names;
    }

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>Stable-named base snapshot (distinct from the timestamped backups) taken before
    /// the FIRST variant swap so a variant-less round can restore the pack's base pointer even
    /// without a skin-season library entry.</summary>
    private static string BaseSnapshotPath(string modelFolder, string model) =>
        Path.Combine(modelFolder, "_companion-backups", model + ".base.xml");

    /// <summary>
    /// Binds round <paramref name="roundNumber"/>'s livery state for every model in
    /// <paramref name="modelDirs"/> under <paramref name="overridesRoot"/>. Variants are
    /// change-point sets: the active pointer for round N is the variant with the LARGEST anchor
    /// ≤ N; before the first anchor, the BASE pointer
    /// (<paramref name="seasonBaseByModel"/> when the pack declares a skin season, else the base
    /// snapshot taken before our first swap). A model whose active pointer already matches the
    /// target is untouched; every real write is backup-first.
    /// </summary>
    public static VariantBindResult BindRound(
        string overridesRoot,
        IEnumerable<string> modelDirs,
        int roundNumber,
        IReadOnlyList<CalendarRound> rounds,
        int seasonYear,
        IReadOnlyDictionary<string, string>? seasonBaseByModel,
        DateTimeOffset now)
    {
        int swapped = 0, restored = 0;
        var backups = new List<string>();
        var errors = new List<string>();

        foreach (string model in modelDirs)
        {
            string folder = Path.Combine(overridesRoot, model);
            string activePath = Path.Combine(folder, model + ".xml");
            if (!Directory.Exists(folder))
                continue;

            try
            {
                var variants = Directory.EnumerateFiles(folder, model + "_*.xml")
                    .Where(f => !Path.GetFileNameWithoutExtension(f)
                        .EndsWith("_dist", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (variants.Count == 0)
                    continue; // this model ships no per-race variants, nothing to manage

                // Ownership guard: when the ACTIVE season's base for this model is known, a variant
                // that shares NONE of the base's livery NAMEs belongs to a DIFFERENT season that
                // happens to share the same car-model folder. smgp and F1-1990 both skin
                // formula_classic_g3m*, and the smgp round names (San Marino, Japan, Australia…)
                // alias 1990's own calendar tokens, so 1990's change-point variants would otherwise
                // anchor onto, and hijack, the smgp grid. A legitimate change-point only renames a
                // handful of the ~26 cars, so it always shares the unchanged names; a foreign
                // season's file shares none. Only the forward MATCH is filtered, the restore
                // recognition below still sees every variant, so a stray foreign file that somehow
                // went active is still recognized and restored off.
                var ownedVariants = seasonBaseByModel is not null &&
                    seasonBaseByModel.TryGetValue(model, out var seasonBaseXml)
                    ? variants.Where(f => SharesAnyLiveryName(seasonBaseXml, SafeRead(f))).ToList()
                    : variants;

                // The change-point in force at this round: the largest anchor ≤ round.
                string? match = ownedVariants
                    .Select(f => (Path: f, Anchor: AnchorRound(
                        Path.GetFileNameWithoutExtension(f)[(model.Length + 1)..], rounds, seasonYear)))
                    .Where(v => v.Anchor is { } a && a <= roundNumber)
                    .OrderByDescending(v => v.Anchor)
                    .Select(v => v.Path)
                    .FirstOrDefault();

                string? current = File.Exists(activePath) ? File.ReadAllText(activePath) : null;

                if (match is not null)
                {
                    string target = File.ReadAllText(match);
                    if (current is not null && SkinSeasonManager.SameContent(current, target))
                        continue; // the round's set is already active

                    // First swap for this model? Snapshot the BASE pointer so an earlier-round
                    // restage can restore it (only when current is not itself a variant, never
                    // snapshot one round's variant as "the base").
                    if (current is not null &&
                        !variants.Any(v => SkinSeasonManager.SameContent(current, File.ReadAllText(v))))
                    {
                        string snapshot = BaseSnapshotPath(folder, model);
                        Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
                        File.WriteAllText(snapshot, current, new System.Text.UTF8Encoding(false));
                    }

                    if (current is not null)
                        backups.Add(ScenarioApplier.BackUp(activePath, now));
                    File.Copy(match, activePath, overwrite: true);
                    swapped++;
                    continue;
                }

                // Before the first change-point: if a variant is active (restaging an earlier
                // round after racing a later one), restore the base pointer.
                if (current is null ||
                    !variants.Any(v => SkinSeasonManager.SameContent(current, File.ReadAllText(v))))
                    continue; // base (or something else) is active, leave it alone

                string? baseContent = null;
                if (seasonBaseByModel is not null && seasonBaseByModel.TryGetValue(model, out var lib))
                    baseContent = lib;
                else if (File.Exists(BaseSnapshotPath(folder, model)))
                    baseContent = File.ReadAllText(BaseSnapshotPath(folder, model));
                if (baseContent is null || SkinSeasonManager.SameContent(current, baseContent))
                    continue;

                backups.Add(ScenarioApplier.BackUp(activePath, now));
                File.WriteAllText(activePath, baseContent, new System.Text.UTF8Encoding(false));
                restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{model}: {ex.Message}");
            }
        }

        return new VariantBindResult
        {
            Swapped = swapped,
            Restored = restored,
            Backups = backups,
            Errors = errors,
        };
    }
}
