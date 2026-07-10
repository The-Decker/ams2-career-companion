using Companion.Ams2.Skins;
using static Companion.Ams2.Skins.VariantOverrideBinder;

namespace Companion.Tests.Ams2;

/// <summary>
/// Race-by-race variant binding for packs without a scenario .bat. Community packs ship
/// CHANGE-POINT sets — <c>02Canada</c> is "the grid from the Canadian GP on" (1986 numbers files
/// by change-set index, not round!), 1990's <c>15_JPN</c> carries Herbert-replaces-Donnelly to
/// season end — so each file is ANCHORED to the round it names (venue-primary, number-fallback,
/// year-guarded) and round N binds the largest anchor ≤ N. What-if / special / other-season
/// files never anchor.
/// </summary>
public sealed class VariantOverrideBinderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-variant-bind-").FullName;
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ---------- anchoring, every observed community shape ----------

    /// <summary>The real 1986 calendar — the pack's files are numbered by CHANGE-SET, not round.</summary>
    private static readonly IReadOnlyList<CalendarRound> Season1986 =
    [
        new(1, "Brazilian Grand Prix", "Autódromo do Jacarepaguá"),
        new(2, "Spanish Grand Prix", "Circuito de Jerez"),
        new(3, "San Marino Grand Prix", "Autodromo Internazionale Enzo e Dino Ferrari"),
        new(4, "Monaco Grand Prix", "Circuit de Monaco"),
        new(5, "Belgian Grand Prix", "Circuit de Spa-Francorchamps"),
        new(6, "Canadian Grand Prix", "Circuit Gilles Villeneuve"),
        new(7, "Detroit Grand Prix", "Detroit street circuit"),
        new(8, "French Grand Prix", "Circuit Paul Ricard"),
        new(9, "British Grand Prix", "Brands Hatch"),
        new(10, "German Grand Prix", "Hockenheimring"),
        new(11, "Hungarian Grand Prix", "Hungaroring"),
        new(12, "Austrian Grand Prix", "Österreichring"),
        new(13, "Italian Grand Prix", "Autodromo Nazionale Monza"),
        new(14, "Portuguese Grand Prix", "Autódromo do Estoril"),
        new(15, "Mexican Grand Prix", "Autódromo Hermanos Rodríguez"),
        new(16, "Australian Grand Prix", "Adelaide Street Circuit"),
    ];

    [Theory]
    // 1986 change-set numbering: the tail names the GP; the file number is a set index.
    [InlineData("01Brazil", 1)]
    [InlineData("02Canada", 6)]
    [InlineData("03USA", 7)]      // Detroit was the US race
    [InlineData("04France", 8)]
    [InlineData("05Great-Britain", 9)]
    [InlineData("06Germany", 10)]
    [InlineData("07Hungary", 11)]
    [InlineData("08Italy", 13)]
    [InlineData("09Portugal", 14)]
    // Nicknames the formal venue text never contains.
    [InlineData("03Imola", 3)]
    [InlineData("05Spa", 5)]
    [InlineData("13Monza", 13)]
    [InlineData("14Estoril", 14)]
    [InlineData("16Adelaide", 16)]
    // Country codes, with and without separators; SanMarino compound.
    [InlineData("04MON", 4)]
    [InlineData("04MCO", 4)]
    [InlineData("15_MEX", 15)]
    [InlineData("03SMR", 3)]
    [InlineData("02SanMarino", 3)] // the tail wins over the set index
    // Bare venue tokens (no round number) — the 1992-style files. The nickname→GP mapping is
    // venue-blind on purpose: "Interlagos" means the Brazilian GP whatever circuit hosted it.
    [InlineData("Interlagos", 1)]
    [InlineData("SIL", 9)]
    [InlineData("ADE", 16)]
    // Number-only and unknown tails: trust the number.
    [InlineData("07", 7)]
    [InlineData("12Xyzzy", 12)]
    // Specials never anchor.
    [InlineData("WhatIf_Andretti", null)]
    [InlineData("Sen", null)]
    [InlineData("Tobacco", null)]
    [InlineData("DEFAULT", null)]
    [InlineData("00Alesi", null)]
    [InlineData("54WIF", null)]
    [InlineData("late", null)]
    public void AnchorRound_CoversTheCommunityNamingShapes(string suffix, int? expected) =>
        Assert.Equal(expected, AnchorRound(suffix, Season1986, 1986));

    [Fact]
    public void AnchorRound_InterlagosNicknameStillAnchorsTheBrazilianRound()
    {
        // 1991-style: the pack venue is the formal "Autódromo José Carlos Pace"; the file tail is
        // the community nickname.
        IReadOnlyList<CalendarRound> rounds =
        [
            new(1, "United States Grand Prix", "Phoenix Street Circuit"),
            new(2, "Brazilian Grand Prix", "Autódromo José Carlos Pace"),
        ];
        Assert.Equal(2, AnchorRound("02Interlagos", rounds, 1991));
        Assert.Equal(2, AnchorRound("Interlagos", rounds, 1991));
    }

    [Fact]
    public void AnchorRound_YearGuard_RejectsAnotherSeasonsFiles()
    {
        // The 1998 skinpack shares the F-V10_Gen2 class folder with our 2000 pack — its files
        // must never bind to 2000 rounds, however well their numbers/tails fit.
        IReadOnlyList<CalendarRound> rounds2000 =
        [
            new(1, "Australian Grand Prix", "Melbourne"),
            new(6, "Monaco Grand Prix", "Circuit de Monaco"),
        ];
        Assert.Null(AnchorRound("1998_01AUS", rounds2000, 2000));
        Assert.Null(AnchorRound("1998_06Monaco", rounds2000, 2000));
        Assert.Equal(1, AnchorRound("2000_01AUS", rounds2000, 2000));
    }

    [Fact]
    public void AnchorRound_AmbiguousVenue_PrefersTheFileNumber()
    {
        // 2020 raced Silverstone twice (British + 70th Anniversary): a tail matching both rounds
        // resolves by the file's own number when it is one of them.
        IReadOnlyList<CalendarRound> rounds =
        [
            new(4, "British Grand Prix", "Silverstone Circuit"),
            new(5, "70th Anniversary Grand Prix", "Silverstone Circuit"),
        ];
        Assert.Equal(5, AnchorRound("05Silverstone", rounds, 2020));
        Assert.Equal(4, AnchorRound("02Silverstone", rounds, 2020)); // number not a candidate → first match
    }

    // ---------- binding on disk (progressive change-points) ----------

    private static readonly IReadOnlyList<CalendarRound> Calendar =
    [
        new(1, "United States Grand Prix", "Phoenix Street Circuit"),
        new(2, "Brazilian Grand Prix", "Autódromo José Carlos Pace"),
        new(3, "San Marino Grand Prix", "Autodromo Internazionale Enzo e Dino Ferrari"),
        new(4, "Monaco Grand Prix", "Circuit de Monaco"),
    ];

    private const string BaseXml = "<USER_OVERRIDES><LIVERY_OVERRIDE LIVERY=\"51\" NAME=\"Base set\" /></USER_OVERRIDES>";
    private const string Round2Xml = "<USER_OVERRIDES><LIVERY_OVERRIDE LIVERY=\"51\" NAME=\"Round 2 set\" /></USER_OVERRIDES>";
    private const string Round4Xml = "<USER_OVERRIDES><LIVERY_OVERRIDE LIVERY=\"51\" NAME=\"Round 4 set\" /></USER_OVERRIDES>";

    private string Model(string name = "formula_classic_g4m1")
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".xml"), BaseXml);
        File.WriteAllText(Path.Combine(folder, name + "_02Interlagos.xml"), Round2Xml);
        File.WriteAllText(Path.Combine(folder, name + "_04Monaco.xml"), Round4Xml);
        return folder;
    }

    private string Active(string folder) =>
        File.ReadAllText(Path.Combine(folder, Path.GetFileName(folder) + ".xml"));

    private VariantBindResult Bind(int round, IReadOnlyDictionary<string, string>? seasonBase = null) =>
        BindRound(_root, ["formula_classic_g4m1"], round, Calendar, 1991, seasonBase, Now);

    [Fact]
    public void BindRound_SwapsTheAnchoredVariant_BackupFirst_AndSnapshotsTheBase()
    {
        string folder = Model();

        var result = Bind(2);

        Assert.Equal(1, result.Swapped);
        Assert.Contains("Round 2 set", Active(folder));
        Assert.Contains("Base set", File.ReadAllText(Assert.Single(result.Backups)));
        Assert.Contains("Base set",
            File.ReadAllText(Path.Combine(folder, "_companion-backups", "formula_classic_g4m1.base.xml")));

        // Same round again → already active → no-op.
        var again = Bind(2);
        Assert.False(again.AnyChanged);
        Assert.Empty(again.Backups);
    }

    [Fact]
    public void BindRound_ChangePointCarriesForward_UntilTheNextAnchor()
    {
        string folder = Model();

        // Round 3 has no file of its own — the round-2 change-point is still in force.
        var round3 = Bind(3);
        Assert.Equal(1, round3.Swapped);
        Assert.Contains("Round 2 set", Active(folder));

        // Round 4 has its own change-point.
        var round4 = Bind(4);
        Assert.Equal(1, round4.Swapped);
        Assert.Contains("Round 4 set", Active(folder));
        // The base snapshot still holds the BASE, not an intermediate variant.
        Assert.Contains("Base set",
            File.ReadAllText(Path.Combine(folder, "_companion-backups", "formula_classic_g4m1.base.xml")));
    }

    [Fact]
    public void BindRound_BeforeTheFirstAnchor_RestoresTheBase()
    {
        string folder = Model();
        Bind(4); // late-season set active

        // Restaging round 1 (before any change-point) restores the base snapshot.
        var round1 = Bind(1);
        Assert.Equal(0, round1.Swapped);
        Assert.Equal(1, round1.Restored);
        Assert.Contains("Base set", Active(folder));
    }

    [Fact]
    public void BindRound_BeforeTheFirstAnchor_PrefersTheSeasonLibraryBase()
    {
        string folder = Model();
        Bind(4);

        const string libraryBase = "<USER_OVERRIDES><LIVERY_OVERRIDE LIVERY=\"51\" NAME=\"Library base\" /></USER_OVERRIDES>";
        var round1 = Bind(1, new Dictionary<string, string> { ["formula_classic_g4m1"] = libraryBase });

        Assert.Equal(1, round1.Restored);
        Assert.Contains("Library base", Active(folder));
    }

    [Fact]
    public void BindRound_LeavesModelsAlone_WhenTheyShipNoVariants_OrTheBaseIsAlreadyRight()
    {
        // No variants at all → never managed.
        string plain = Path.Combine(_root, "mclaren_mp45b");
        Directory.CreateDirectory(plain);
        File.WriteAllText(Path.Combine(plain, "mclaren_mp45b.xml"), BaseXml);
        var result = BindRound(_root, ["mclaren_mp45b"], 1, Calendar, 1991, null, Now);
        Assert.False(result.AnyChanged);

        // Variants exist but round 1 precedes every anchor and the base is active → untouched.
        Model();
        var idle = Bind(1);
        Assert.False(idle.AnyChanged);
    }

    [Fact]
    public void BindRound_NeverBindsWhatIfOrOtherSeasonFiles()
    {
        string folder = Model();
        File.WriteAllText(Path.Combine(folder, "formula_classic_g4m1_WhatIf_Alesi.xml"),
            "<USER_OVERRIDES><LIVERY_OVERRIDE LIVERY=\"51\" NAME=\"What if\" /></USER_OVERRIDES>");
        File.WriteAllText(Path.Combine(folder, "formula_classic_g4m1_1990_01USA.xml"),
            "<USER_OVERRIDES><LIVERY_OVERRIDE LIVERY=\"51\" NAME=\"1990 set\" /></USER_OVERRIDES>");
        File.WriteAllText(Path.Combine(folder, "formula_classic_g4m1_dist.xml"), "<template />");

        var round1 = Bind(1); // only the what-if/1990/dist files could claim round 1 — none may

        Assert.False(round1.AnyChanged);
        Assert.Contains("Base set", Active(folder));
    }
}
