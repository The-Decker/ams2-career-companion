using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Ams2.Skins;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.ViewModels.Services;
using Companion.ViewModels.Wizard;

namespace Companion.ViewModels.Hub;

/// <summary>The colour a skin-status chip reads as: a clean/expected skin (green), a default
/// but correct look (neutral), or a real problem that needs a fix (amber). Kept WPF-free — the
/// view maps the tone to a brush.</summary>
public enum SkinTone
{
    Good,
    Neutral,
    Warn,
}

/// <summary>One car's row on the Skins lens: who drives it, the exact livery NAME (the string
/// that binds the skin AND the one the player types/selects in-game), and a plain-language
/// status + tone. A pure display record projected from a <see cref="SkinAssignment"/>.</summary>
public sealed record SkinRow
{
    public string DriverId { get; init; } = "";
    public required string DriverName { get; init; }
    public string TeamId { get; init; } = "";
    public required string TeamName { get; init; }
    public string? Number { get; init; }
    public string SkinSlot { get; init; } = "";
    public required string LiveryName { get; init; }
    public required bool IsPlayer { get; init; }
    public required string StatusLabel { get; init; }
    public required SkinTone Tone { get; init; }

    /// <summary>An extra line of context (where the skin is installed, or the skin-doctor
    /// near-miss hint); empty when the label says it all.</summary>
    public string Detail { get; init; } = "";
}

/// <summary>A skin pack the season expects, surfaced so the player can install missing skins.</summary>
public sealed record SkinPackRef
{
    public required string Name { get; init; }
    public string? Url { get; init; }
    public string? OverridesFolder { get; init; }
}

/// <summary>A bind-ready skin/car preview. Art keys are stable driver/team ids consumed by the
/// App's existing asset converter; null keys and an empty slot are the intentional fallback for
/// an installed active livery that has no authored pack entry.</summary>
public sealed record SkinPreview
{
    public required string LiveryName { get; init; }
    public string DriverId { get; init; } = "";
    public string DriverName { get; init; } = "";
    public string TeamId { get; init; } = "";
    public string TeamName { get; init; } = "";
    public string VehicleModel { get; init; } = "";
    public string? CarNumber { get; init; }
    public string SkinSlot { get; init; } = "";
    public string? PortraitKey { get; init; }
    public string? CarKey { get; init; }
    public string? TopCarKey { get; init; }

    public bool HasPortrait => PortraitKey is { Length: > 0 };
    public bool HasCarPreview => CarKey is { Length: > 0 } || TopCarKey is { Length: > 0 };
    public bool HasSkinSlot => SkinSlot.Length > 0;

    public static SkinPreview Unknown(string liveryName) => new() { LiveryName = liveryName };
}

/// <summary>One editable grid seat (the grid editor): rename the driver and/or rebind the seat to a
/// different ACTIVE livery. Edits persist through the supplied callback (a cosmetic staging override,
/// keyed by the seat's ORIGINAL livery) and land in the staged custom-AI file at the next stage —
/// never the sim. Auto-saves when a field commits (text on focus-loss, livery on selection).</summary>
public sealed partial class SeatEditor : ObservableObject
{
    private readonly Action<string, SeatStagingOverride> _save;
    private readonly bool _loading;
    private readonly IReadOnlyDictionary<string, SkinPreview> _previewsByLivery;
    private SkinPreview _selectedPreview;

    public SeatEditor(
        string liveryKey,
        string originalDriverName,
        string currentDriverName,
        string currentLivery,
        IReadOnlyList<string> liveryOptions,
        Action<string, SeatStagingOverride> save)
        : this(
            liveryKey,
            originalDriverName,
            currentDriverName,
            currentLivery,
            liveryOptions,
            new SkinPreview
            {
                LiveryName = liveryKey,
                DriverName = originalDriverName,
            },
            new Dictionary<string, SkinPreview>(StringComparer.Ordinal),
            save)
    {
    }

    public SeatEditor(
        string liveryKey,
        string originalDriverName,
        string currentDriverName,
        string currentLivery,
        IReadOnlyList<string> liveryOptions,
        SkinPreview originalPreview,
        IReadOnlyDictionary<string, SkinPreview> previewsByLivery,
        Action<string, SeatStagingOverride> save)
    {
        LiveryKey = liveryKey;
        OriginalDriverName = originalDriverName;
        LiveryOptions = liveryOptions;
        OriginalPreview = originalPreview;
        _previewsByLivery = previewsByLivery;
        _save = save;

        _loading = true;
        _driverName = currentDriverName;
        _selectedLivery = currentLivery;
        _replacementSelection = !string.Equals(currentLivery, liveryKey, StringComparison.Ordinal) &&
                                liveryOptions.Contains(currentLivery, StringComparer.Ordinal)
            ? currentLivery
            : null;
        _selectedPreview = ResolvePreview(currentLivery);
        _loading = false;
    }

    /// <summary>The seat's ORIGINAL livery — the stable key the override is stored under (never
    /// changes when the driver is renamed or the skin rebound).</summary>
    public string LiveryKey { get; }

    /// <summary>The driver the pack authored for this seat — the baseline a rename is measured against
    /// (so clearing back to it removes the override).</summary>
    public string OriginalDriverName { get; }

    /// <summary>Active custom skins for this seat's exact vehicle model. Stock/vanilla liveries and
    /// custom skins authored for a different model are deliberately absent.</summary>
    public IReadOnlyList<string> LiveryOptions { get; }

    public bool HasLiveryOptions => LiveryOptions.Count > 0;

    /// <summary>The current seat before any replacement selection.</summary>
    public SkinPreview OriginalPreview { get; }

    /// <summary>The selected replacement's authored identity/art/slot, or a blank preview when the
    /// selected active livery is installed but not part of this season pack.</summary>
    public SkinPreview SelectedPreview => _selectedPreview;

    public bool IsReplacement => !string.Equals(SelectedLivery, LiveryKey, StringComparison.Ordinal);

    [ObservableProperty]
    private string _driverName;

    [ObservableProperty]
    private string _selectedLivery;

    /// <summary>The replacement-only menu selection. Null means keep the seat's current authored
    /// skin; choosing a compatible custom skin updates <see cref="SelectedLivery"/>.</summary>
    [ObservableProperty]
    private string? _replacementSelection;

    partial void OnDriverNameChanged(string value) => Persist();

    partial void OnSelectedLiveryChanged(string value)
    {
        string? replacement = !string.Equals(value, LiveryKey, StringComparison.Ordinal) &&
                              LiveryOptions.Contains(value, StringComparer.Ordinal)
            ? value
            : null;
        if (!string.Equals(_replacementSelection, replacement, StringComparison.Ordinal))
        {
            _replacementSelection = replacement;
            OnPropertyChanged(nameof(ReplacementSelection));
        }
        _selectedPreview = ResolvePreview(value);
        OnPropertyChanged(nameof(SelectedPreview));
        OnPropertyChanged(nameof(IsReplacement));
        Persist();
    }

    partial void OnReplacementSelectionChanged(string? value)
    {
        if (_loading)
            return;

        string next = string.IsNullOrEmpty(value) ? LiveryKey : value;
        if (!string.Equals(SelectedLivery, next, StringComparison.Ordinal))
            SelectedLivery = next;
    }

    private SkinPreview ResolvePreview(string liveryName)
    {
        if (string.Equals(liveryName, LiveryKey, StringComparison.Ordinal))
            return OriginalPreview;
        return _previewsByLivery.TryGetValue(liveryName, out var preview)
            ? preview
            : SkinPreview.Unknown(liveryName);
    }

    private void Persist()
    {
        if (_loading)
            return;

        string? nameOverride =
            string.IsNullOrWhiteSpace(DriverName) || string.Equals(DriverName, OriginalDriverName, StringComparison.Ordinal)
                ? null
                : DriverName;
        string? liveryOverride =
            string.Equals(SelectedLivery, LiveryKey, StringComparison.Ordinal) ? null : SelectedLivery;

        _save(LiveryKey, new SeatStagingOverride { DriverName = nameOverride, LiveryName = liveryOverride });
    }
}

/// <summary>
/// The hub's Skins lens: what livery/skin every car on the current round's grid will actually
/// show in AMS2, and — the headline — the exact livery the player must pick for their OWN car on
/// the in-game vehicle-selection screen (the player's car is not driven by the custom-AI file, so
/// its livery is a manual pick the app can only crib, never force). Pure read-only projection over
/// <see cref="ICareerSession.CurrentSkinAssignments"/>; writes nothing and never touches the user's
/// community skin files. Re-projects after every applied round like the other lenses.
/// </summary>
public sealed partial class SkinsViewModel : ObservableObject
{
    private readonly ICareerSession _session;

    public SkinsViewModel(ICareerSession session)
    {
        _session = session;
        Refresh();
    }

    /// <summary>Raised with the exact livery NAME to copy — the view owns the real clipboard so
    /// this viewmodel stays WPF-free (same seam as the briefing's copy action).</summary>
    public event EventHandler<string>? CopyRequested;

    /// <summary>Every car on this round's grid, grid order.</summary>
    public ObservableCollection<SkinRow> Cars { get; } = [];

    /// <summary>The skin packs this season's liveries expect — the reference the player installs
    /// missing skins from.</summary>
    public ObservableCollection<SkinPackRef> RequiredSkinPacks { get; } = [];

    /// <summary>Liveries installed on disk as "##" placeholders but NOT switched on in-game — the
    /// activator's candidates. Turning one on writes the community override file (backup-first).</summary>
    public ObservableCollection<string> ActivatableLiveries { get; } = [];

    /// <summary>The editable grid: one row per seat for renaming the driver + rebinding the livery.
    /// Edits persist as cosmetic staging overrides applied to the custom-AI file at the next stage.</summary>
    public ObservableCollection<SeatEditor> Editors { get; } = [];

    /// <summary>The editor shown in the detail pane. The player's seat is selected first when one
    /// exists; an all-AI grid starts on its first seat.</summary>
    [ObservableProperty]
    private SeatEditor? _selectedEditor;

    /// <summary>True when there is at least one inactive livery to activate — shows the activator.</summary>
    [ObservableProperty]
    private bool _hasActivatable;

    /// <summary>The outcome banner of the last activation (null before any).</summary>
    [ObservableProperty]
    private string? _activationBanner;

    /// <summary>True when the last activation succeeded (green banner) vs failed (amber).</summary>
    [ObservableProperty]
    private bool _activationSucceeded;

    [ObservableProperty]
    private string _summary = "";

    /// <summary>The livery budget for this class, e.g. "Livery slots: 22 of 24 active" — how many of
    /// the class's fixed livery cap are switched on. Empty when the cap is unknown and none active.</summary>
    [ObservableProperty]
    private string _liveryBudget = "";

    /// <summary>True when this round's grid asks for more distinct liveries than the class can show —
    /// the historical field is bigger than the livery cap.</summary>
    [ObservableProperty]
    private bool _exceedsCap;

    /// <summary>The over-cap explanation (how many cars can't have their own livery), or null.</summary>
    [ObservableProperty]
    private string? _capWarning;

    /// <summary>The exact livery NAME the player selects for their own car in-game. Null when the
    /// player has no seat this round (an all-AI round).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlayerCar))]
    private string? _playerLiveryName;

    [ObservableProperty]
    private string? _playerDriverName;

    /// <summary>The team-coloured player portrait key (<c>player.&lt;team&gt;</c>).</summary>
    [ObservableProperty]
    private string? _playerPortraitKey;

    /// <summary>The authored side-view car-art key for the livery the player occupies.</summary>
    [ObservableProperty]
    private string? _playerCarKey;

    /// <summary>The authored top-down car-art key for the livery the player occupies.</summary>
    [ObservableProperty]
    private string? _playerTopCarKey;

    /// <summary>The player's real livery slot, blank when the installed/stock slot is unknown.</summary>
    [ObservableProperty]
    private string _playerSkinSlot = "";

    /// <summary>The skin/entry car number (not a team ordinal), blank when unavailable.</summary>
    [ObservableProperty]
    private string? _playerCarNumber;

    /// <summary>The skin status of the player's own car — so the crib can warn when the livery the
    /// player must pick has no installed skin (they'll look default).</summary>
    [ObservableProperty]
    private string? _playerStatusNote;

    public bool HasPlayerCar => PlayerLiveryName is { Length: > 0 };

    /// <summary>True when at least one car's livery will not bind — the panel raises the amber note
    /// telling the player which names need fixing.</summary>
    [ObservableProperty]
    private bool _hasUnbound;

    /// <summary>True when at least one car will show a default skin (no installed override) — the
    /// panel offers the required-skin-packs list so the player can install them.</summary>
    [ObservableProperty]
    private bool _hasMissingSkins;

    public void Refresh()
    {
        var plan = _session.CurrentSkinAssignments();
        var previewsByLivery = BuildReplacementPreviews(plan);

        Cars.Clear();
        foreach (var a in plan.Assignments)
            Cars.Add(ToRow(a));

        Summary = plan.Summary;
        HasUnbound = plan.UnboundCount > 0 || plan.InactiveCount > 0;
        HasMissingSkins = plan.DefaultSkinCount > 0 || plan.InactiveCount > 0 || plan.UnboundCount > 0;

        LiveryBudget = plan.LiveryCap is { } cap
            ? $"Livery slots: {plan.ActiveLiveryCount} of {cap} active for {plan.Ams2Class}."
            : plan.ActiveLiveryCount > 0
                ? $"{plan.ActiveLiveryCount} liveries active for {plan.Ams2Class}."
                : "";
        ExceedsCap = plan.ExceedsCap;
        CapWarning = plan.ExceedsCap
            ? $"This grid asks for {plan.DistinctLiveriesOnGrid} distinct liveries but {plan.Ams2Class} shows at " +
              $"most {plan.LiveryCap}. {plan.DistinctLiveriesOnGrid - plan.LiveryCap!.Value} car(s) can't get their " +
              "own livery — they'll duplicate another or use a default skin. For an accurate field, race a grid of " +
              $"{plan.LiveryCap} or fewer."
            : null;

        if (plan.PlayerCar is { } player)
        {
            var playerPreview = BuildOriginalPreview(player, previewsByLivery);
            PlayerLiveryName = player.LiveryName;
            PlayerDriverName = player.DriverName;
            PlayerPortraitKey = GridSeatChoice.PlayerImageKey(player.TeamId);
            PlayerCarKey = playerPreview.CarKey;
            PlayerTopCarKey = playerPreview.TopCarKey;
            PlayerSkinSlot = player.SkinSlot;
            PlayerCarNumber = player.Number;
            PlayerStatusNote = player.Status == SkinStatus.CustomSkin
                ? null
                : "This livery has no custom skin installed yet — your car will look like the default until you add the pack's skins.";
        }
        else
        {
            PlayerLiveryName = null;
            PlayerDriverName = null;
            PlayerPortraitKey = null;
            PlayerCarKey = null;
            PlayerTopCarKey = null;
            PlayerSkinSlot = "";
            PlayerCarNumber = null;
            PlayerStatusNote = null;
        }

        RequiredSkinPacks.Clear();
        foreach (var pack in _session.Pack.Manifest.Requires.SkinPacks)
            RequiredSkinPacks.Add(new SkinPackRef
            {
                Name = pack.Name,
                Url = pack.Url,
                OverridesFolder = pack.OverridesFolder,
            });

        ActivatableLiveries.Clear();
        foreach (var name in plan.InactiveLiveries)
            ActivatableLiveries.Add(name);
        HasActivatable = ActivatableLiveries.Count > 0;

        // Editable grid: one row per seat, seeded from any saved override. The replacement menu is
        // CUSTOM-only and exact-model-only. A formula_classic_g3m2 seat can never receive a stock
        // livery or a custom skin authored for g3m1/g3m3/etc.
        var overrides = _session.SeatStagingOverrides();
        Editors.Clear();
        SeatEditor? playerEditor = null;
        foreach (var a in plan.Assignments)
        {
            overrides.TryGetValue(a.LiveryName, out var ov);
            string currentName = ov?.DriverName is { Length: > 0 } n ? n : a.DriverName;
            string currentLivery = ov?.LiveryName is { Length: > 0 } l ? l : a.LiveryName;

            var originalPreview = BuildOriginalPreview(a, previewsByLivery);
            var options = plan.ActiveCustomLiveryModels
                .Where(pair => string.Equals(
                    pair.Value,
                    originalPreview.VehicleModel,
                    StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Old builds allowed cross-model and stock overrides. Retire those saved invalid
            // selections immediately so they cannot still reach staging after the menu is fixed.
            if (!string.Equals(currentLivery, a.LiveryName, StringComparison.Ordinal) &&
                !options.Contains(currentLivery, StringComparer.Ordinal))
            {
                currentLivery = a.LiveryName;
                SaveOverride(a.LiveryName, new SeatStagingOverride
                {
                    DriverName = ov?.DriverName,
                    LiveryName = null,
                });
            }

            var editor = new SeatEditor(
                a.LiveryName,
                a.DriverName,
                currentName,
                currentLivery,
                options,
                originalPreview,
                previewsByLivery,
                SaveOverride);
            Editors.Add(editor);
            if (a.IsPlayer)
                playerEditor = editor;
        }
        SelectedEditor = playerEditor ?? Editors.FirstOrDefault();

        OnPropertyChanged(nameof(HasPlayerCar));
    }

    /// <summary>Build the replacement selector's authored livery-to-car lookup. Pack identity owns
    /// the art keys; the resolver's active-slot map owns the installed/official slot. A livery that
    /// is active but absent from the pack deliberately yields a blank preview.</summary>
    private IReadOnlyDictionary<string, SkinPreview> BuildReplacementPreviews(SkinAssignmentPlan plan)
    {
        int round = _session.Summary.CurrentRound;
        var entriesByLivery = _session.Pack.Entries
            .GroupBy(entry => entry.Ams2LiveryName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.FirstOrDefault(entry =>
                    RoundsRange.TryParse(entry.Rounds, out var range) && range.Contains(round))
                    ?? group.First(),
                StringComparer.Ordinal);
        var driversById = _session.Pack.Drivers.ToDictionary(driver => driver.Id, StringComparer.Ordinal);
        var teamsById = _session.Pack.Teams.ToDictionary(team => team.Id, StringComparer.Ordinal);
        var assignmentSlots = plan.Assignments
            .GroupBy(assignment => assignment.LiveryName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().SkinSlot, StringComparer.Ordinal);

        var previews = new Dictionary<string, SkinPreview>(StringComparer.Ordinal);
        foreach (string liveryName in plan.ActiveLiveries
                     .Concat(plan.Assignments.Select(assignment => assignment.LiveryName))
                     .Distinct(StringComparer.Ordinal))
        {
            if (!entriesByLivery.TryGetValue(liveryName, out var entry))
            {
                previews[liveryName] = SkinPreview.Unknown(liveryName);
                continue;
            }

            driversById.TryGetValue(entry.DriverId, out var driver);
            teamsById.TryGetValue(entry.TeamId, out var team);
            string slot = plan.ActiveLiverySlots.TryGetValue(liveryName, out string? activeSlot)
                ? activeSlot
                : assignmentSlots.GetValueOrDefault(liveryName, "");
            previews[liveryName] = new SkinPreview
            {
                LiveryName = liveryName,
                DriverId = entry.DriverId,
                DriverName = driver?.Name ?? entry.DriverId,
                TeamId = entry.TeamId,
                TeamName = team?.Name ?? entry.TeamId,
                VehicleModel = plan.ActiveCustomLiveryModels.GetValueOrDefault(
                    liveryName,
                    team?.CarVehicleIds.Count == 1 ? team.CarVehicleIds[0] : ""),
                CarNumber = entry.Number,
                SkinSlot = slot,
                PortraitKey = entry.DriverId,
                CarKey = entry.DriverId,
                TopCarKey = entry.DriverId,
            };
        }
        return previews;
    }

    private static SkinPreview BuildOriginalPreview(
        SkinAssignment assignment,
        IReadOnlyDictionary<string, SkinPreview> previewsByLivery)
    {
        previewsByLivery.TryGetValue(assignment.LiveryName, out var authored);
        string? carKey = authored?.CarKey;
        if (carKey is null &&
            !string.IsNullOrEmpty(assignment.DriverId) &&
            !string.Equals(
                assignment.DriverId,
                RoundGridResolver.SyntheticPlayerDriverId,
                StringComparison.Ordinal))
        {
            carKey = assignment.DriverId;
        }

        return new SkinPreview
        {
            LiveryName = assignment.LiveryName,
            DriverId = assignment.DriverId,
            DriverName = assignment.DriverName,
            TeamId = assignment.TeamId,
            TeamName = assignment.TeamName,
            VehicleModel = assignment.VehicleFolder is { Length: > 0 } model
                ? model
                : authored?.VehicleModel ?? "",
            CarNumber = assignment.Number,
            SkinSlot = assignment.SkinSlot,
            PortraitKey = assignment.IsPlayer
                ? GridSeatChoice.PlayerImageKey(assignment.TeamId)
                : string.IsNullOrEmpty(assignment.DriverId) ? null : assignment.DriverId,
            CarKey = carKey,
            TopCarKey = carKey,
        };
    }

    /// <summary>Persists one seat's grid-editor override through the session (rename / rebind). The
    /// lens re-projects to the new picture on the next natural refresh (Apply / reopen); we don't
    /// rebuild on every keystroke so editing keeps focus.</summary>
    private void SaveOverride(string liveryKey, SeatStagingOverride seatOverride) =>
        _session.SetSeatStagingOverride(liveryKey, seatOverride);

    // ---------- push the grid (with edits) into AMS2 ----------

    /// <summary>The outcome banner of the last stage (null before any).</summary>
    [ObservableProperty]
    private string? _stageBanner;

    [ObservableProperty]
    private bool _stageSucceeded;

    /// <summary>True when staging paused on the community-file gate — the view shows "Stage anyway".</summary>
    [ObservableProperty]
    private bool _stageBlocked;

    /// <summary>True when the session supports the force-stage escape hatch (community NAMeS file).</summary>
    public bool CanForceStage => _session is IForceStaging;

    /// <summary>Writes this round's grid — INCLUDING your renames/rebinds — into the AMS2 custom-AI
    /// file (backup-first). Prefers the explicit "apply" path that ALWAYS writes an app-marked file
    /// (so the write is verifiable on disk — the AMS2 diagnosis found the ordinary flow often wrote
    /// nothing); falls back to normal staging for sessions without that seam.</summary>
    [RelayCommand]
    private void StageGrid() =>
        ApplyStage(_session is IExplicitGridApply apply ? apply.ApplyGridToAms2() : _session.StageCurrentGrid());

    [RelayCommand]
    private void ForceStageGrid()
    {
        if (_session is IForceStaging forceStaging)
            ApplyStage(forceStaging.StageCurrentGrid(force: true));
    }

    private void ApplyStage(StageOutcome outcome)
    {
        StageSucceeded = outcome.Success;
        StageBlocked = outcome.BlockedByForceGate;
        StageBanner = ComposeStageBanner(outcome);
    }

    private string ComposeStageBanner(StageOutcome outcome)
    {
        if (outcome.BlockedByForceGate)
            return "Your installed AI file is a community file, so the app only overwrites it when you " +
                   "confirm — click “Overwrite anyway (backup first)” to set up this race. A timestamped " +
                   "backup is taken first, and season-end offers one-click restore.";
        if (!outcome.Success)
            return outcome.Messages.Count > 0 ? $"Couldn't set up the race — {outcome.Messages[^1]}" : "Couldn't set up the race.";
        if (outcome.NoOpAlreadyMatches)
            return "✔ AMS2 is already set up for this race — your installed drivers + skins match, nothing to change.";

        // Surface WHAT happened (the drivers written, this race's skins activated, any bubble-car swap,
        // the base-game fallback) so the reason for each is visible — then the one thing to do in-game.
        var lines = new List<string> { "✔ AMS2 is set up for this race." };
        lines.AddRange(outcome.Messages);
        if (PlayerLiveryName is { Length: > 0 } pick)
            lines.Add($"► Your car: pick “{pick}” on the AMS2 car-select screen.");
        if (outcome.BackupPath is { Length: > 0 } backup)
            lines.Add($"Your previous file was backed up ({System.IO.Path.GetFileName(backup)}); season-end can restore it.");
        lines.Add("Close AMS2 first if it's open, then launch and race.");
        return string.Join("\n\n", lines);
    }

    /// <summary>Turn an installed-but-inactive livery ON in-game (assign it a real slot in the
    /// community override file, backup-first) — the fix for "installed but AMS2 doesn't show it".
    /// Re-projects afterwards so the newly-active livery moves out of the activatable list and any
    /// car bound to it flips to "Custom skin".</summary>
    [RelayCommand]
    private void ActivateLivery(string? liveryName)
    {
        if (string.IsNullOrEmpty(liveryName))
            return;

        var result = _session.ActivateLivery(liveryName);
        ActivationSucceeded = result.Success;
        ActivationBanner = result.Message;
        Refresh();
    }

    private static SkinRow ToRow(SkinAssignment a)
    {
        var (label, tone, detail) = a.Status switch
        {
            SkinStatus.CustomSkin => (
                "Custom skin",
                SkinTone.Good,
                a.VehicleFolder is { Length: > 0 } folder ? $"installed under {folder}" : ""),
            SkinStatus.InstalledInactive => (
                "Installed — not active",
                SkinTone.Warn,
                "the skin is on disk but not switched on in-game (a “##” placeholder) — activate it or pick an active livery"),
            SkinStatus.StockDefault => (
                "Default livery",
                SkinTone.Neutral,
                "the game's built-in livery — no custom skin needed"),
            SkinStatus.NameOnly => (
                "Default skin",
                SkinTone.Neutral,
                "the driver name binds, but no matching skin is installed — add the pack's skins to see it"),
            SkinStatus.Unbound => (
                "Won't bind",
                SkinTone.Warn,
                a.NearMiss is { Length: > 0 } near
                    ? $"no exact match — did you mean “{near}”? (case or spacing differs)"
                    : "no matching installed livery, NAMeS name or stock name"),
            _ => ("", SkinTone.Neutral, ""),
        };

        return new SkinRow
        {
            DriverId = a.DriverId,
            DriverName = a.DriverName,
            TeamId = a.TeamId,
            TeamName = a.TeamName,
            Number = a.Number,
            SkinSlot = a.SkinSlot,
            LiveryName = a.LiveryName,
            IsPlayer = a.IsPlayer,
            StatusLabel = label,
            Tone = tone,
            Detail = detail,
        };
    }

    /// <summary>Copy the player's own-car livery NAME — the crib's one-tap action (pick it exactly,
    /// case-sensitive, on the in-game vehicle-selection screen).</summary>
    [RelayCommand]
    private void CopyPlayerLivery()
    {
        if (PlayerLiveryName is { Length: > 0 } name)
            CopyRequested?.Invoke(this, name);
    }

    /// <summary>Copy any car's livery NAME (for looking one up in-game or fixing a pack).</summary>
    [RelayCommand]
    private void CopyLivery(SkinRow? row)
    {
        if (row is { LiveryName.Length: > 0 })
            CopyRequested?.Invoke(this, row.LiveryName);
    }
}
