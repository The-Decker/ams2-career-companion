using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Hub;

/// <summary>
/// The Calendar lens: the whole season's TRACK schedule, visible up front so the circuits you'll race
/// are never a mystery — only the results reveal as you go (that's the History lens). Each round shows
/// the real venue, the ACTUAL AMS2 track driven, and a badge (real venue / stand-in / mod alternate),
/// plus a note when an alternate exists that wasn't enabled. Pure read-only projection of
/// <see cref="ICareerSession.SeasonSchedule"/>; refreshed in place after every Apply like the other
/// lenses (so a season changeover re-projects).
/// </summary>
public sealed partial class CalendarViewModel : ObservableObject
{
    private readonly ICareerSession _session;

    public CalendarViewModel(ICareerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        Refresh();
    }

    public ObservableCollection<CalendarRoundViewModel> Rounds { get; } = [];

    public bool IsEmpty => Rounds.Count == 0;

    /// <summary>A one-line season summary: rounds, how many drive a mod alternate, how many are base
    /// stand-ins for a venue AMS2 doesn't have.</summary>
    public string HeaderNote
    {
        get
        {
            if (Rounds.Count == 0)
                return "";
            int alternates = Rounds.Count(r => r.IsAlternate);
            int standIns = Rounds.Count(r => r.IsStandIn);
            string alt = alternates > 0 ? $" · {alternates} on mod alternate{(alternates == 1 ? "" : "s")}" : "";
            string stand = standIns > 0 ? $" · {standIns} stand-in{(standIns == 1 ? "" : "s")}" : "";
            return $"{Rounds.Count} rounds{alt}{stand}";
        }
    }

    /// <summary>True when any round has an alternate that COULD be enabled but isn't — the calendar
    /// shows a hint that alternate tracks are available (turn them on at career creation).</summary>
    public bool HasUnusedAlternates => Rounds.Any(r => r.HasUnusedAlternate);

    public void Refresh()
    {
        Rounds.Clear();
        foreach (var entry in _session.SeasonSchedule())
            Rounds.Add(new CalendarRoundViewModel(entry));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HeaderNote));
        OnPropertyChanged(nameof(HasUnusedAlternates));
    }
}

/// <summary>One round of the season calendar: the driven AMS2 track, its real venue, and how they
/// relate (real venue / stand-in / applied mod alternate). Expands to the ORIGINAL circuit's map,
/// facts and history (the historical venue, not the stand-in) + an optional venue photo.</summary>
public sealed partial class CalendarRoundViewModel : ObservableObject
{
    public CalendarRoundViewModel(SeasonScheduleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        RoundLabel = $"R{entry.Round}";
        Name = entry.Name;
        ChipLabel = ShortName(entry.Name);
        DateText = entry.Date;
        RealVenue = entry.RealVenue;
        Ams2TrackName = entry.Ams2TrackName;
        LapsText = $"{entry.Laps.ToString(CultureInfo.InvariantCulture)} laps";
        Kind = entry.Kind;
        UnusedAlternateName = entry.UnusedAlternateName;
        CircuitLayoutId = entry.CircuitLayoutId;
        CircuitCaption = entry.CircuitCaption;
        CircuitHistory = entry.CircuitHistory;
        CircuitFacts = entry.CircuitFacts;

        (BadgeText, TrackLine) = entry.Kind switch
        {
            SeasonTrackKind.RealVenue => ("Real venue", Ams2TrackName),
            SeasonTrackKind.Alternate => ("Alternate", $"{Ams2TrackName} — mod alternate for {RealVenue}"),
            _ => ("Stand-in", $"{Ams2TrackName} — stand-in for {RealVenue}"),
        };

        UnusedAlternateNote = UnusedAlternateName is { Length: > 0 } alt
            ? $"Alternate available: {alt} — enable “Use alternate tracks” at career creation to race it."
            : "";
    }

    public string RoundLabel { get; }
    public string Name { get; }

    /// <summary>The short name for the at-a-glance overview chip ("Belgian Grand Prix" →
    /// "Belgian") — the full name still heads the round card.</summary>
    public string ChipLabel { get; }

    public string DateText { get; }
    public string RealVenue { get; }
    /// <summary>The AMS2 track you will actually drive — the calendar's headline value.</summary>
    public string Ams2TrackName { get; }
    public string LapsText { get; }
    public SeasonTrackKind Kind { get; }

    public bool IsRealVenue => Kind == SeasonTrackKind.RealVenue;
    public bool IsStandIn => Kind == SeasonTrackKind.StandIn;
    public bool IsAlternate => Kind == SeasonTrackKind.Alternate;

    /// <summary>Short badge label ("Real venue" / "Stand-in" / "Alternate").</summary>
    public string BadgeText { get; }

    /// <summary>The one-line description of what's driven and why (venue vs stand-in vs alternate).</summary>
    public string TrackLine { get; }

    public string? UnusedAlternateName { get; }
    public bool HasUnusedAlternate => UnusedAlternateName is { Length: > 0 };

    /// <summary>The "alternate available — enable at creation" hint (empty when none).</summary>
    public string UnusedAlternateNote { get; }

    // ---------- expandable detail: the ORIGINAL circuit (not the stand-in) ----------

    /// <summary>Whether the card is expanded to show the original circuit's map + facts + history.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    /// <summary>The ORIGINAL (historical) circuit's map layout id — keys the SVG map + the venue photo.
    /// Empty when no circuit data is shipped for the round.</summary>
    public string CircuitLayoutId { get; }
    public bool HasCircuit => CircuitLayoutId.Length > 0;

    /// <summary>The original circuit's caption (name · place · km · turns · direction).</summary>
    public string CircuitCaption { get; }
    public bool HasCircuitCaption => CircuitCaption.Length > 0;

    /// <summary>A brief, data-grounded history of the original circuit.</summary>
    public string CircuitHistory { get; }
    public bool HasCircuitHistory => CircuitHistory.Length > 0;

    /// <summary>Era-capped fun facts about the original circuit — spoiler-free by construction.</summary>
    public IReadOnlyList<string> CircuitFacts { get; }
    public bool HasCircuitFacts => CircuitFacts.Count > 0;

    private static string ShortName(string name)
    {
        foreach (string suffix in (string[])[" Grand Prix", " GP"])
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
                return name[..^suffix.Length];
        }
        return name;
    }
}
