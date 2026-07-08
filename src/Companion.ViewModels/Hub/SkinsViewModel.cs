using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Ams2.Skins;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.ViewModels.Services;

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
    public required string DriverName { get; init; }
    public required string TeamName { get; init; }
    public string? Number { get; init; }
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

/// <summary>One editable grid seat (the grid editor): rename the driver and/or rebind the seat to a
/// different ACTIVE livery. Edits persist through the supplied callback (a cosmetic staging override,
/// keyed by the seat's ORIGINAL livery) and land in the staged custom-AI file at the next stage —
/// never the sim. Auto-saves when a field commits (text on focus-loss, livery on selection).</summary>
public sealed partial class SeatEditor : ObservableObject
{
    private readonly Action<string, SeatStagingOverride> _save;
    private readonly bool _loading;

    public SeatEditor(
        string liveryKey,
        string originalDriverName,
        string currentDriverName,
        string currentLivery,
        IReadOnlyList<string> liveryOptions,
        Action<string, SeatStagingOverride> save)
    {
        LiveryKey = liveryKey;
        OriginalDriverName = originalDriverName;
        LiveryOptions = liveryOptions;
        _save = save;

        _loading = true;
        _driverName = currentDriverName;
        _selectedLivery = currentLivery;
        _loading = false;
    }

    /// <summary>The seat's ORIGINAL livery — the stable key the override is stored under (never
    /// changes when the driver is renamed or the skin rebound).</summary>
    public string LiveryKey { get; }

    /// <summary>The driver the pack authored for this seat — the baseline a rename is measured against
    /// (so clearing back to it removes the override).</summary>
    public string OriginalDriverName { get; }

    /// <summary>The liveries this seat can wear: its own original plus every ACTIVE livery for the
    /// class. Picking one other than the original rebinds the seat's skin.</summary>
    public IReadOnlyList<string> LiveryOptions { get; }

    [ObservableProperty]
    private string _driverName;

    [ObservableProperty]
    private string _selectedLivery;

    partial void OnDriverNameChanged(string value) => Persist();

    partial void OnSelectedLiveryChanged(string value) => Persist();

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
            PlayerLiveryName = player.LiveryName;
            PlayerDriverName = player.DriverName;
            PlayerStatusNote = player.Status == SkinStatus.CustomSkin
                ? null
                : "This livery has no custom skin installed yet — your car will look like the default until you add the pack's skins.";
        }
        else
        {
            PlayerLiveryName = null;
            PlayerDriverName = null;
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

        // Editable grid: one row per seat, seeded from any saved override. Livery options = the
        // seat's own livery plus every active livery for the class (so "keep original" is selectable
        // and any rebind targets a livery that actually renders in-game).
        var overrides = _session.SeatStagingOverrides();
        Editors.Clear();
        foreach (var a in plan.Assignments)
        {
            overrides.TryGetValue(a.LiveryName, out var ov);
            string currentName = ov?.DriverName is { Length: > 0 } n ? n : a.DriverName;
            string currentLivery = ov?.LiveryName is { Length: > 0 } l ? l : a.LiveryName;

            var options = new List<string> { a.LiveryName };
            if (!string.Equals(currentLivery, a.LiveryName, StringComparison.Ordinal))
                options.Add(currentLivery);
            options.AddRange(plan.ActiveLiveries.Where(x =>
                !string.Equals(x, a.LiveryName, StringComparison.Ordinal) &&
                !string.Equals(x, currentLivery, StringComparison.Ordinal)));

            Editors.Add(new SeatEditor(a.LiveryName, a.DriverName, currentName, currentLivery, options, SaveOverride));
        }

        OnPropertyChanged(nameof(HasPlayerCar));
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
            DriverName = a.DriverName,
            TeamName = a.TeamName,
            Number = a.Number,
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
