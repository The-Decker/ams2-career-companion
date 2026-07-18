using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.ViewModels.Services;
using Companion.ViewModels.Standings;

namespace Companion.ViewModels.Review;

/// <summary>One offer letter of the review screen, the offer's facts plus a period-document
/// rendering of it (telegram / fax / email by era), so signing feels like answering the paddock.</summary>
public sealed partial class OfferLetterViewModel : ObservableObject
{
    public OfferLetterViewModel(SeasonOfferModel offer, OfferDocument document)
    {
        TeamId = offer.TeamId;
        TeamName = offer.TeamName;
        TierText = $"Tier {offer.Tier}";
        SalaryText = $"{offer.SalaryBu:0.##} BU / season";
        ScoreText = $"score {offer.Score:0.####}";
        _isAccepted = offer.Accepted;
        Document = document;
    }

    public string TeamId { get; }

    public string TeamName { get; }

    public string TierText { get; }

    public string SalaryText { get; }

    public string ScoreText { get; }

    /// <summary>The period-document form of this offer (era medium, letterhead, dateline, body).</summary>
    public OfferDocument Document { get; }

    /// <summary>The era skin this letter renders with, the shared bind contract
    /// (era-theming-assets-brief.md Slice 0), surfaced first-class so the App's reusable era
    /// DataTemplate keys off the letter, not the screen.</summary>
    public IEraSkin EraSkin => Document.Era;

    /// <summary>The era medium flattened to a top-level bindable for DataTrigger keying.</summary>
    public EraMedium EraMedium => Document.Era.Medium;

    public string MediumLabel => Document.Era.Label;

    public string AccentHex => Document.Era.AccentHex;

    public string DocumentFontStack => Document.Era.DocumentFontStack;

    public string Letterhead => Document.Letterhead;

    public string Dateline => Document.Dateline;

    public string BodyText => Document.Body;

    [ObservableProperty]
    private bool _isAccepted;
}

/// <summary>One raisable stat row of the review's development block: the stat's id (command
/// parameter), its display label, and its current value.</summary>
public sealed record DevStatViewModel(string Id, string Label, double Value)
{
    public string ValueText => $"{Value:0.00}";
}

/// <summary>One buyable perk row of the review's development block: what it is, what it costs, and —
/// in plain language, what it does, for a Buy button that spends banked points.</summary>
public sealed record PurchasablePerkViewModel(PurchasablePerk Perk)
{
    public string Id => Perk.Id;

    public string Name => Perk.Name;

    public string CategoryText => Perk.Category;

    public string CostText => Perk.Cost == 1 ? "1 pt" : $"{Perk.Cost} pts";

    public string BenefitText => string.Join("   ·   ", Perk.Benefits);

    public string DrawbackText => string.Join("   ·   ", Perk.Drawbacks);

    public bool HasDrawback => Perk.Drawbacks.Count > 0;
}

/// <summary>
/// The season review + offers screen (docs/dev/m5-fix-integration.md, "App wiring"): final
/// standings, the journal's headline digest, the offer letters with accept-one (journaled),
/// the one-click NAMeS restore of the pre-season AI file (locked decision #7c), and the era
/// transition sign-and-continue block (M6): after an offer is accepted and a next-era pack is
/// discovered, "Sign &amp; start &lt;year&gt;" executes the transition and raises
/// <see cref="SeasonSigned"/> so the shell reopens the career into the new season.
/// Shown by Home once every round has an applied result.
/// </summary>
public sealed partial class SeasonReviewViewModel : ObservableObject
{
    private readonly ICareerSession _session;

    public SeasonReviewViewModel(ICareerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;

        FinalStandings = new StandingsViewModel(session.AllSnapshots(), session.Pack, session: session);
        Review = session.SeasonReview();
        Headlines = Review?.Headlines ?? [];

        // Render each offer as a period document (telegram / fax / email) for the season's era,
        // addressed to the driver by their character name. A data/rules/era-themes.json decade
        // override restyles the document skin; the built-in table is the fallback.
        int seasonYear = Review?.SeasonYear ?? session.Pack.Season.Year;
        string driverName = session.PlayerIdentity()?.DisplayName ?? "";
        Offers = new ObservableCollection<OfferLetterViewModel>(
            (Review?.Offers ?? []).Select(o => new OfferLetterViewModel(
                o, OfferDocument.Compose(seasonYear, o.TeamName, o.Tier, o.SalaryBu, driverName,
                    session.EraThemeOverrides()))));
        _acceptedTeamId = Review?.AcceptedTeamId;
        CanRestoreAiFile = session is IAiFileRestore;
        NextSeason = session.NextSeason();

        RefreshDevelopment();
    }

    // ---------- character development (depth 4): spend banked points between seasons ----------

    /// <summary>True when this career has a character to develop (the review shows the block).</summary>
    public bool HasCharacter { get; private set; }

    /// <summary>Character points available to spend now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCp))]
    private int _availableCp;

    /// <summary>True while the driver still has a point to spend (gates the raise buttons).</summary>
    public bool HasCp => AvailableCp > 0;

    /// <summary>The driver's seven stats, each raisable one step for a point while any remain.</summary>
    public ObservableCollection<DevStatViewModel> DevelopmentStats { get; } = [];

    /// <summary>Perks the driver can afford to buy right now (cheapest first); empty when the pool
    /// can't afford any unowned perk.</summary>
    public ObservableCollection<PurchasablePerkViewModel> DevelopmentPerks { get; } = [];

    /// <summary>True while there is at least one affordable perk to buy (shows the perk subsection).</summary>
    public bool HasPurchasablePerks { get; private set; }

    /// <summary>Raise one stat a step, spending a point (no-op when unaffordable or at the cap).</summary>
    [RelayCommand]
    private void RaiseStat(string? statId)
    {
        if (statId is null)
            return;
        try
        {
            _session.SpendCharacterPoint(CharacterSpend.Stat(statId, 1));
        }
        catch (InvalidOperationException)
        {
            return; // unaffordable or at the cap, the button just does nothing
        }
        RefreshDevelopment();
    }

    /// <summary>Buy a perk, spending its points (no-op when unaffordable or already owned).</summary>
    [RelayCommand]
    private void BuyPerk(string? perkId)
    {
        if (perkId is null)
            return;
        var row = DevelopmentPerks.FirstOrDefault(p => p.Id == perkId);
        if (row is null)
            return;
        try
        {
            _session.SpendCharacterPoint(CharacterSpend.Perk(row.Id, row.Perk.Cost));
        }
        catch (InvalidOperationException)
        {
            return; // unaffordable / already owned, the button just does nothing
        }
        RefreshDevelopment();
    }

    private void RefreshDevelopment()
    {
        var dossier = _session.CharacterDossier();
        HasCharacter = dossier is not null;
        AvailableCp = _session.AvailableCharacterCp();
        DevelopmentStats.Clear();
        foreach (var stat in dossier?.Stats ?? [])
            DevelopmentStats.Add(new DevStatViewModel(stat.Id, stat.Label, stat.Value));
        DevelopmentPerks.Clear();
        foreach (var perk in _session.PurchasablePerks())
            DevelopmentPerks.Add(new PurchasablePerkViewModel(perk));
        HasPurchasablePerks = DevelopmentPerks.Count > 0;
        OnPropertyChanged(nameof(HasCharacter));
        OnPropertyChanged(nameof(HasPurchasablePerks));
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
            return $"You finished {position}, reputation {Review.FinalReputation:0.#}, " +
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
    [NotifyPropertyChangedFor(nameof(OfferAccepted), nameof(CanSign))]
    [NotifyCanExecuteChangedFor(nameof(SignAndContinueCommand))]
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

    // ---------- era transition: sign & continue (M6) ----------

    /// <summary>Where the career goes next: a real era CHANGEOVER when an eligible dedicated
    /// next-year pack is installed, otherwise a CARRYOVER on the same car. Null at a bounded
    /// campaign summit (SMGP season 17).</summary>
    public NextSeasonInfo? NextSeason { get; }

    public bool HasNextSeason => NextSeason is not null;

    /// <summary>What the sign-and-continue block says: same-car carryover guidance when no
    /// dedicated next-year pack exists, otherwise the era-changeover guidance for the next pack.</summary>
    public string EraTransitionText => NextSeason switch
    {
        { IsCarryover: true } next =>
            $"No {next.SeasonYear} season pack is installed, so your career carries on in the same car " +
            $"and liveries, the grid ages, retires and refills around you. Accept an offer above, then " +
            $"sign to take your age, reputation and form into {next.SeasonYear}. Drop a later-year pack " +
            "in the packs folder and the car switches over when that season arrives.",
        { } next =>
            $"Your career continues: {next.PackName} is installed. Accept an offer above, then sign to " +
            $"carry your age, reputation, form and every team lineage into {next.SeasonYear}.",
        _ => "This season is complete.",
    };

    /// <summary>The bridge note when the next pack skips years, e.g. "1968 has no pack, your
    /// career bridges through it." Null for consecutive seasons (or no next pack).</summary>
    public string? BridgeNote
    {
        get
        {
            if (NextSeason is not { BridgedYears.Count: > 0 } next)
                return null;
            var years = next.BridgedYears;
            return years.Count == 1
                ? $"{years[0]} has no pack, your career bridges through it."
                : $"{years[0]}–{years[^1]} have no packs, your career bridges through them.";
        }
    }

    public bool HasBridgeNote => BridgeNote is not null;

    public string SignButtonText => NextSeason is { } next
        ? $"Sign & start {next.SeasonYear}"
        : "Sign & start";

    /// <summary>Signing requires both player inputs: an accepted offer and a next pack.</summary>
    public bool CanSign => OfferAccepted && HasNextSeason;

    /// <summary>Transition problems the user must see (the plan's validation errors, e.g.
    /// the accepted team does not exist in the next pack).</summary>
    [ObservableProperty]
    private string? _transitionError;

    /// <summary>Raised after the next season has been started and persisted, the shell
    /// reopens the career file, which now lands in the new season's round 1 briefing.</summary>
    public event EventHandler? SeasonSigned;

    /// <summary>Executes EraTransition + CareerStore.StartNextSeason through the session seam,
    /// surfacing validation errors instead of navigating. Raising <see cref="SeasonSigned"/>
    /// is the LAST thing this command does: the shell's handler disposes this screen's
    /// session, so nothing may touch it afterwards.</summary>
    [RelayCommand(CanExecute = nameof(CanSign))]
    private void SignAndContinue()
    {
        if (AcceptedTeamId is not { } teamId)
            return;
        try
        {
            _session.StartNextSeason(teamId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException)
        {
            TransitionError = ex.Message;
            return;
        }

        TransitionError = null;
        SeasonSigned?.Invoke(this, EventArgs.Empty);
    }

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
