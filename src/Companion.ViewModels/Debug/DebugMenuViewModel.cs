using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Smgp;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.ViewModels.Debug;

/// <summary>One discoverable pack the debug menu can open or preview.</summary>
public sealed record DebugPackEntry
{
    public required string Directory { get; init; }
    public required string Label { get; init; }
    public required int Year { get; init; }
    public required bool IsSmgp { get; init; }
}

/// <summary>Carries the real throwaway career (and its file path) the debug menu just created.</summary>
public sealed class DebugCareerOpenedEventArgs(ICareerSession session, string careerFilePath) : EventArgs
{
    public ICareerSession Session { get; } = session;
    public string CareerFilePath { get; } = careerFilePath;
}

/// <summary>
/// The app-wide developer DEBUG MENU (dynasty-passport-roadmap.md Piece 2). A pure, WPF-free
/// ViewModel over the real creation factory + session seams, split into two fold-safe tiers:
///
/// <b>Tier 1 — real, replay-safe.</b> Throwaway careers created through <see cref="ICareerFactory"/>
/// and advanced only through the provenance-excluded INPUT mutators (<c>Apply</c>,
/// <c>ApplySkillPlan</c>). A career made this way still resimulates byte-identical.
///
/// <b>Tier 2 — non-persistent preview.</b> A fake <see cref="PreviewCareerSession"/> feeds canned
/// projections to any View for states the real fold cannot reach (Racing Passport, an arbitrary
/// level, a death/career-over/sit-out screen). NEVER resimulated, NEVER written to a
/// <c>.ams2career</c>.
///
/// Navigation is event-based (mirroring <see cref="Start.StartViewModel"/>): the shell decides what
/// "open this session" or "show this screen" means, so this ViewModel never touches WPF.
/// </summary>
public sealed partial class DebugMenuViewModel : ObservableObject
{
    private readonly CareerEnvironment _environment;
    private readonly ICareerFactory _factory;
    private readonly string _debugCareersDirectory;
    private readonly Func<ICareerSession?> _currentSession;
    private readonly Func<string?> _currentCareerPath;

    public DebugMenuViewModel(
        CareerEnvironment environment,
        ICareerFactory factory,
        string debugCareersDirectory,
        Func<ICareerSession?>? currentSession = null,
        Func<string?>? currentCareerPath = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(debugCareersDirectory);

        _environment = environment;
        _factory = factory;
        _debugCareersDirectory = debugCareersDirectory;
        _currentSession = currentSession ?? (static () => null);
        _currentCareerPath = currentCareerPath ?? (static () => null);
        _dnaOptions = new Lazy<IReadOnlyList<RacingDnaDefinition>>(LoadDnaOptions);

        Packs = new ObservableCollection<DebugPackEntry>(DiscoverPacks());
    }

    // ---------- navigation events (the shell wires these) ----------

    /// <summary>Close the overlay and return to the screen underneath.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Tier-1: open a REAL throwaway career (recorded in the gallery like any career).</summary>
    public event EventHandler<DebugCareerOpenedEventArgs>? RealCareerRequested;

    /// <summary>Tier-2: host this display-only <see cref="PreviewCareerSession"/> in a preview hub.</summary>
    public event EventHandler<ICareerSession>? PreviewRequested;

    /// <summary>Tier-2: show a single leaf screen (promotion / demotion) directly.</summary>
    public event EventHandler<ObservableObject>? ScreenRequested;

    // ---------- bound state ----------

    public ObservableCollection<DebugPackEntry> Packs { get; }

    /// <summary>The master seed for created throwaway careers (deterministic; editable).</summary>
    [ObservableProperty]
    private long _masterSeed = 20260717;

    /// <summary>The level the Tier-2 "preview a level" command renders the Driver screen at (1–300).</summary>
    [ObservableProperty]
    private int _previewLevel = 250;

    /// <summary>The inspector / dump panel text (SMGP future lore, journal + determinism dump,
    /// economy dump, or the last error). Empty until a dump command runs.</summary>
    [ObservableProperty]
    private string _inspectorText = "";

    /// <summary>The SMGP season ordinal (1–17) <see cref="OpenSmgpAtSeasonCommand"/> fast-forwards
    /// a throwaway SMGP career to — through the real INPUT seams, so it resimulates honestly.</summary>
    [ObservableProperty]
    private int _targetSmgpSeason = 5;

    /// <summary>The Racing DNA identity the level preview renders (an id from the shipped catalog;
    /// null = the default circuit specialist). Preview-only — never validated for real creation.</summary>
    [ObservableProperty]
    private string? _selectedDnaId;

    /// <summary>The completed-seasons position on the mastery track the level preview carries
    /// (clamped to the synthesized plan's mastery season — 499-SP pool gate).</summary>
    [ObservableProperty]
    private int _previewCompletedSeasons = 16;

    /// <summary>Every shipped Racing DNA identity for the level-preview picker. Loaded lazily and
    /// failure-tolerant: a dev box without the rules catalog gets an empty picker, not a crash.</summary>
    public IReadOnlyList<RacingDnaDefinition> DnaOptions => _dnaOptions.Value;

    private readonly Lazy<IReadOnlyList<RacingDnaDefinition>> _dnaOptions;

    public bool HasPacks => Packs.Count > 0;

    // ---------- Tier 1: real, replay-safe careers ----------

    [RelayCommand]
    private void OpenDynasty()
    {
        var pack = Packs.FirstOrDefault(p => !p.IsSmgp);
        if (pack is null)
        {
            InspectorText = "No historical pack found on disk to start a Grand Prix Dynasty.";
            return;
        }
        CreateThrowaway(pack.Directory, CareerExperienceModes.GrandPrixDynasty, advance: false);
    }

    [RelayCommand]
    private void OpenSmgp()
    {
        var pack = Packs.FirstOrDefault(p => p.IsSmgp);
        if (pack is null)
        {
            InspectorText = "No SMGP pack found on disk to start Super Monaco GP.";
            return;
        }
        CreateThrowaway(pack.Directory, CareerExperienceModes.Smgp, advance: false);
    }

    [RelayCommand]
    private void OpenPack(DebugPackEntry? pack)
    {
        if (pack is null)
            return;
        string mode = pack.IsSmgp ? CareerExperienceModes.Smgp : CareerExperienceModes.GrandPrixDynasty;
        CreateThrowaway(pack.Directory, mode, advance: false);
    }

    /// <summary>Create a throwaway SMGP career and FAST-FORWARD it to the start of season
    /// <see cref="TargetSmgpSeason"/> — the only way to preview the 17-ordinal campaign without
    /// grinding. Every season advances through the real INPUT seams (Apply player-wins,
    /// ResolveSmgpOffer, AcceptOffer, StartNextSeason + reopen), so the result resimulates
    /// byte-identical; <see cref="DebugCareerFactory.AdvanceSmgpToSeason"/> reports early stops
    /// (knock-out, finale) in the inspector.</summary>
    [RelayCommand]
    private void OpenSmgpAtSeason()
    {
        var pack = Packs.FirstOrDefault(p => p.IsSmgp);
        if (pack is null)
        {
            InspectorText = "No SMGP pack found on disk.";
            return;
        }
        try
        {
            Directory.CreateDirectory(_debugCareersDirectory);
            string path = Path.Combine(
                _debugCareersDirectory,
                $"debug-smgp-s{TargetSmgpSeason}-{Guid.NewGuid():N}.ams2career");
            var request = DebugCareerFactory.BuildRequest(
                pack.Directory, path, CareerExperienceModes.Smgp, MasterSeed,
                careerName: $"[DEBUG] smgp season {TargetSmgpSeason}",
                playerLivery: DebugCareerFactory.ResolvePlayerLivery(pack.Directory));

            var session = _factory.Create(request);
            session = DebugCareerFactory.AdvanceSmgpToSeason(
                session,
                TargetSmgpSeason,
                reopen: old =>
                {
                    (old as IDisposable)?.Dispose();
                    return _factory.Open(path);
                },
                out string note);
            if (note.Length > 0)
                InspectorText = note;

            RealCareerRequested?.Invoke(this, new DebugCareerOpenedEventArgs(session, path));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            InspectorText = $"SMGP season jump failed: {ex.Message}";
        }
    }

    /// <summary>Create a throwaway career and FAST-FORWARD it to season end (auto-winning every round
    /// through the real <c>Apply</c> INPUT), then spend one Skill Point through the real
    /// <c>ApplySkillPlan</c> INPUT — an honest, replay-safe way to preview an advanced career.</summary>
    [RelayCommand]
    private void OpenPackAtSeasonEnd(DebugPackEntry? pack)
    {
        if (pack is null)
            return;
        string mode = pack.IsSmgp ? CareerExperienceModes.Smgp : CareerExperienceModes.GrandPrixDynasty;
        CreateThrowaway(pack.Directory, mode, advance: true);
    }

    /// <summary>Continue the CURRENTLY OPEN real career exactly one season end forward — the
    /// season-by-season navigation the fresh-throwaway buttons can't give. Plays out an unfinished
    /// season; from a season end, signs the first offer and plays the new season out too. The SAME
    /// career file advances (every step a real INPUT, so it still resimulates byte-identical), then
    /// the hub reopens on the continued career. The monitors (journal / economy dump) read it live
    /// between clicks.</summary>
    [RelayCommand]
    private void AdvanceSeason()
    {
        var session = _currentSession();
        if (session is null)
        {
            InspectorText = "No live career is open — open a Tier-1 career first.";
            return;
        }
        if (session is PreviewCareerSession)
        {
            InspectorText = "A Tier-2 preview is display-only — there is no career to advance. " +
                            "Open a Tier-1 career first.";
            return;
        }
        if (_currentCareerPath() is not { } path)
        {
            InspectorText = "The open career has no file on disk to continue through.";
            return;
        }

        try
        {
            DebugCareerFactory.AdvanceToNextSeasonEnd(session, out string note);

            // Hand the shell a FRESH session on the continued file: AttachHome disposes the old hub
            // — and its session — so the hub must never show the instance this menu advanced.
            var fresh = _factory.Open(path);
            DebugCareerFactory.FinishSeason(fresh); // play the just-signed season out; no-op at an end
            if (note.Length > 0)
                InspectorText = note;

            RealCareerRequested?.Invoke(this, new DebugCareerOpenedEventArgs(fresh, path));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            InspectorText = $"Advance failed: {ex.Message}";
        }
    }

    private void CreateThrowaway(string packDirectory, string mode, bool advance)
    {
        try
        {
            Directory.CreateDirectory(_debugCareersDirectory);
            string path = Path.Combine(
                _debugCareersDirectory, $"debug-{mode}-{Guid.NewGuid():N}.ams2career");
            var request = DebugCareerFactory.BuildRequest(
                packDirectory, path, mode, MasterSeed, careerName: $"[DEBUG] {mode}",
                playerLivery: DebugCareerFactory.ResolvePlayerLivery(packDirectory));

            var session = _factory.Create(request);
            if (advance)
            {
                DebugCareerFactory.FinishSeason(session);
                DebugCareerFactory.TrySpendOneSkillPoint(session);
            }

            RealCareerRequested?.Invoke(this, new DebugCareerOpenedEventArgs(session, path));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            InspectorText = $"Create failed ({mode}): {ex.Message}";
        }
    }

    // ---------- Tier 2: non-persistent previews (never write a .ams2career) ----------

    /// <summary>Racing Passport is unbuildable (IsAvailable=false AND throws at creation), so it is
    /// reachable ONLY as a Tier-2 preview.</summary>
    [RelayCommand]
    private void PreviewRacingPassport() =>
        Preview(DebugPreviews.RacingPassport(DebugPreviewPack.Build(1967)));

    [RelayCommand]
    private void PreviewLevelScreen()
    {
        try
        {
            var rules = _environment.Rules;
            Preview(DebugPreviews.Level(
                DebugPreviewPack.Build(1967),
                rules.Character,
                rules.RacingDna,
                rules.MasterySkills,
                PreviewLevel,
                racingDnaId: SelectedDnaId,
                completedSeasons: PreviewCompletedSeasons));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            InspectorText = $"Level preview failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void PreviewDeathHardcore() =>
        Preview(DebugPreviews.Death(DebugPreviewPack.Build(1990, smgp: true), MortalityMode.Hardcore));

    [RelayCommand]
    private void PreviewDeathNormal() =>
        Preview(DebugPreviews.Death(DebugPreviewPack.Build(1990, smgp: true), MortalityMode.Normal));

    [RelayCommand]
    private void PreviewSmgpCareerOver() =>
        Preview(DebugPreviews.SmgpCareerOver(DebugPreviewPack.Build(1990, smgp: true)));

    [RelayCommand]
    private void PreviewInjury() =>
        Preview(DebugPreviews.SitOut(DebugPreviewPack.Build(1990, smgp: true), seasonEnding: false));

    [RelayCommand]
    private void PreviewSeasonEndingInjury() =>
        Preview(DebugPreviews.SitOut(DebugPreviewPack.Build(1990, smgp: true), seasonEnding: true));

    [RelayCommand]
    private void PreviewFinale() =>
        Preview(DebugPreviews.Finale(DebugPreviewPack.Build(1990, smgp: true), flawless: false));

    [RelayCommand]
    private void PreviewFlawlessFinale() =>
        Preview(DebugPreviews.Finale(DebugPreviewPack.Build(1990, smgp: true), flawless: true));

    [RelayCommand]
    private void PreviewPromotion() => ShowPromotion(demotion: false);

    [RelayCommand]
    private void PreviewDemotion() => ShowPromotion(demotion: true);

    private void ShowPromotion(bool demotion)
    {
        var model = DebugPreviews.Promotion(demotion);
        // The promotion/demotion screens only appear post-Apply in the real loop, so they cannot be
        // routed to through a hub constructor — host the leaf ViewModel directly. Accept/decline just
        // return to the debug menu (a preview commits nothing).
        var vm = new PromotionViewModel(
            model,
            onAccept: () => ReopenDebug(),
            onDecline: () => ReopenDebug());
        ScreenRequested?.Invoke(this, vm);
    }

    private void Preview(PreviewCareerSession session) =>
        PreviewRequested?.Invoke(this, session);

    private void ReopenDebug() => ScreenRequested?.Invoke(this, this);

    // ---------- inspect / dump ----------

    /// <summary>Reveal the spoiler-hidden SMGP future lore — every authored campaign season's identity
    /// (title/era/overview), which the live game keeps sealed until reached.</summary>
    [RelayCommand]
    private void RevealSmgpLore()
    {
        try
        {
            var lore = _environment.RulesDirectory is { } dir
                ? SmgpSeasonLore.Load(dir)
                : SmgpSeasonLore.Empty;
            if (lore.IsEmpty)
            {
                InspectorText = "No SMGP season lore is shipped (data/rules/smgp/seasons.json absent).";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"SMGP FUTURE LORE — {lore.Seasons.Count} of {SmgpRules.CampaignSeasons} seasons authored")
                .AppendLine();
            foreach (var season in lore.Seasons)
            {
                sb.AppendLine($"SEASON {season.Ordinal} / {SmgpRules.CampaignSeasons} — {season.Title}");
                if (season.Era.Length > 0)
                    sb.AppendLine($"  Era: {season.Era}");
                if (season.Subtitle.Length > 0)
                    sb.AppendLine($"  {season.Subtitle}");
                if (season.Overview.Length > 0)
                    sb.AppendLine($"  {Truncate(season.Overview, 220)}");
                sb.AppendLine();
            }
            InspectorText = sb.ToString().TrimEnd();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            InspectorText = $"Lore reveal failed: {ex.Message}";
        }
    }

    /// <summary>Dump the current live career's journal + determinism-relevant state for debugging.
    /// Reads only public session projections (the journal chain, the folded summary, the campaign
    /// timeline) — no DB poke. Reports plainly when no live career is open.</summary>
    [RelayCommand]
    private void DumpJournal()
    {
        var session = _currentSession();
        if (session is null)
        {
            InspectorText = "No live career is open — open a Tier-1 career first, then dump.";
            return;
        }

        try
        {
            var sb = new StringBuilder();
            var s = session.Summary;
            sb.AppendLine("=== DETERMINISM / STATE DUMP ===");
            sb.AppendLine($"Career:  {s.CareerName}");
            sb.AppendLine($"Season:  {s.SeasonYear} · round {s.CurrentRound}/{s.RoundCount}" +
                          (s.SeasonComplete ? " (complete)" : ""));
            sb.AppendLine($"Standing: {(s.PlayerPosition is { } p ? $"P{p}" : "—")}" +
                          $"   Rep {s.Reputation?.ToString("0.#") ?? "—"}   OPI {s.Opi?.ToString("+0.00;-0.00") ?? "—"}");

            var mortality = session.PlayerMortality();
            sb.AppendLine($"Mortality: {mortality.Mode}  fit={mortality.IsFit}  deceased={mortality.Deceased}");

            var timeline = session.CampaignTimeline();
            if (timeline.Count > 0)
            {
                sb.AppendLine().AppendLine("Campaign timeline:");
                foreach (var e in timeline)
                    sb.AppendLine($"  #{e.Ordinal} {e.State}" +
                                  (e.Year is { } y ? $" ({y})" : "") +
                                  (e.Title.Length > 0 ? $" — {e.Title}" : ""));
            }

            var chain = session.JournalFor("player");
            sb.AppendLine().AppendLine($"Journal chain for 'player' — {chain.Contributions.Count} rows");
            foreach (var row in chain.Contributions.Take(40))
                sb.AppendLine($"  [{row.SourceSeq}] {row.Label}: {row.Value ?? row.Detail}");

            sb.AppendLine().AppendLine(
                "(Note: the master seed lives inside the career DB and is not exposed on the session " +
                "projection; ResimulateCore is the authoritative determinism check.)");
            InspectorText = sb.ToString().TrimEnd();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            InspectorText = $"Journal dump failed: {ex.Message}";
        }
    }

    /// <summary>Dump the live career's Dynasty owner-economy dashboard (balance, sponsors, statement,
    /// pending decisions) as inspector text. READ-ONLY — and the declared SEAM for the future tycoon
    /// debug levers (build brief §2): force-money commands, when that workstream wants them, go through
    /// <c>DeclareEconomyDecision</c> (the validated, journaled INPUT seam) so the economy still
    /// resimulates byte-identical. Never a direct state poke.</summary>
    [RelayCommand]
    private void DumpEconomy()
    {
        var session = _currentSession();
        if (session is null)
        {
            InspectorText = "No live career is open — open a Tier-1 career first, then dump the economy.";
            return;
        }

        try
        {
            var dashboard = session.EconomyDashboard();
            InspectorText = dashboard is null
                ? "The live career is not a Dynasty owner-economy career (EconomyDashboard is null)."
                : DebugEconomyDump.Format(dashboard);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            InspectorText = $"Economy dump failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    // ---------- helpers ----------

    private IReadOnlyList<DebugPackEntry> DiscoverPacks()
    {
        try
        {
            return PackDiscovery.Discover(_environment.ResolvePackSearchRoots())
                .Where(p => p is { Manifest: not null, LoadError: null, SeasonYear: not null })
                .Select(p => new DebugPackEntry
                {
                    Directory = p.Directory,
                    Year = p.SeasonYear!.Value,
                    IsSmgp = string.Equals(
                        p.Manifest!.CareerStyle, SmgpRules.CareerStyle, StringComparison.Ordinal),
                    Label = $"{p.Title} ({p.SeasonYear})",
                })
                .OrderBy(p => p.IsSmgp)
                .ThenBy(p => p.Year)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private IReadOnlyList<RacingDnaDefinition> LoadDnaOptions()
    {
        try
        {
            return _environment.Rules.RacingDna?.Definitions ?? (IReadOnlyList<RacingDnaDefinition>)[];
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            return [];
        }
    }

    private static string Truncate(string text, int max)
    {
        string oneLine = text.ReplaceLineEndings(" ").Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..max].TrimEnd() + "…";
    }
}
