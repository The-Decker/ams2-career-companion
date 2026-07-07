using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Ams2.Skins;
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

        OnPropertyChanged(nameof(HasPlayerCar));
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
