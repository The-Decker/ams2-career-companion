using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;
using Companion.ViewModels.Standings;

namespace Companion.ViewModels.Review;

/// <summary>One offer letter row of the review screen.</summary>
public sealed partial class OfferLetterViewModel(SeasonOfferModel offer) : ObservableObject
{
    public string TeamId { get; } = offer.TeamId;

    public string TeamName { get; } = offer.TeamName;

    public string TierText { get; } = $"Tier {offer.Tier}";

    public string SalaryText { get; } = $"{offer.SalaryBu:0.##} BU / season";

    public string ScoreText { get; } = $"score {offer.Score:0.####}";

    [ObservableProperty]
    private bool _isAccepted = offer.Accepted;
}

/// <summary>
/// The season review + offers screen (docs/dev/m5-fix-integration.md, "App wiring"): final
/// standings, the journal's headline digest, the offer letters with accept-one (journaled),
/// the one-click NAMeS restore of the pre-season AI file (locked decision #7c), and the era
/// transition placeholder (M6). Shown by Home once every round has an applied result.
/// </summary>
public sealed partial class SeasonReviewViewModel : ObservableObject
{
    private readonly ICareerSession _session;

    public SeasonReviewViewModel(ICareerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;

        FinalStandings = new StandingsViewModel(session.AllSnapshots(), session.Pack);
        Review = session.SeasonReview();
        Headlines = Review?.Headlines ?? [];
        Offers = new ObservableCollection<OfferLetterViewModel>(
            (Review?.Offers ?? []).Select(o => new OfferLetterViewModel(o)));
        _acceptedTeamId = Review?.AcceptedTeamId;
        CanRestoreAiFile = session is IAiFileRestore;
    }

    /// <summary>The final-standings block (drivers/constructors tabs + round matrix).</summary>
    public StandingsViewModel FinalStandings { get; }

    /// <summary>Null when the season is somehow not complete (defensive; Home only builds
    /// this screen after the final Apply).</summary>
    public SeasonReviewModel? Review { get; }

    public string Title => Review is null
        ? "Season review"
        : $"{Review.SeasonYear} season review";

    public string PlayerSummaryText
    {
        get
        {
            if (Review is null)
                return "";
            string position = Review.PlayerPosition is { } p ? $"P{p} in the championship" : "unclassified";
            return $"You finished {position} — reputation {Review.FinalReputation:0.#}, " +
                   $"OPI {Review.FinalOpi:+0.00;-0.00;0.00}.";
        }
    }

    // ---------- journal digest ----------

    /// <summary>The season's journaled headlines, in story order.</summary>
    public IReadOnlyList<string> Headlines { get; }

    public bool HasHeadlines => Headlines.Count > 0;

    // ---------- offers ----------

    public ObservableCollection<OfferLetterViewModel> Offers { get; }

    public bool HasOffers => Offers.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OfferAccepted))]
    private string? _acceptedTeamId;

    public bool OfferAccepted => AcceptedTeamId is not null;

    [ObservableProperty]
    private string? _offerError;

    /// <summary>Accept-one: the choice is journaled by the session; re-accepting a different
    /// letter replaces the previous acceptance (at most one per season).</summary>
    [RelayCommand]
    private void AcceptOffer(OfferLetterViewModel? offer)
    {
        if (offer is null)
            return;
        try
        {
            _session.AcceptOffer(offer.TeamId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            OfferError = ex.Message;
            return;
        }

        OfferError = null;
        foreach (var letter in Offers)
            letter.IsAccepted = string.Equals(letter.TeamId, offer.TeamId, StringComparison.Ordinal);
        AcceptedTeamId = offer.TeamId;
    }

    /// <summary>Era-transition placeholder (v1 single-season careers end here; M6 carries the
    /// accepted seat into the next era pack).</summary>
    public string EraTransitionText =>
        "This career's season is complete. Era transition — carrying your accepted seat into " +
        "the next season's pack — arrives in M6; your choice above is recorded in the journal.";

    // ---------- NAMeS restore (locked decision #7c) ----------

    /// <summary>True when the session supports restoring the pre-season AI file.</summary>
    public bool CanRestoreAiFile { get; }

    [ObservableProperty]
    private string? _restoreBanner;

    [ObservableProperty]
    private bool _restoreSucceeded;

    /// <summary>One-click restore of the pre-season backup; the current file is re-backed-up
    /// first, so restore never destroys state.</summary>
    [RelayCommand]
    private void RestoreAiFile()
    {
        if (_session is not IAiFileRestore restore)
            return;

        var outcome = restore.RestoreOriginalAiFile();
        RestoreSucceeded = outcome.Success;
        RestoreBanner = outcome.Messages.Count > 0
            ? string.Join(" ", outcome.Messages)
            : outcome.Success ? "Restored." : "Restore failed.";
    }
}
