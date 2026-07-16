using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Companion.ViewModels.Shell;

/// <summary>The two cinematic, click-through session gates in the Upcoming Race loop.</summary>
public enum SessionIntroKind
{
    Qualifying,
    Race,
}

/// <summary>
/// Display-only introduction shown immediately before a qualifying or race result editor. The
/// callback only advances the in-memory shell state; this projection never writes a result, journal
/// row, save field, or RNG draw.
/// </summary>
public sealed partial class SessionIntroViewModel : ObservableObject
{
    private readonly Action _onContinue;
    private bool _continued;

    public SessionIntroViewModel(SessionIntroKind kind, string subtitle, Action onContinue)
    {
        ArgumentNullException.ThrowIfNull(subtitle);
        ArgumentNullException.ThrowIfNull(onContinue);

        Kind = kind;
        Subtitle = subtitle;
        _onContinue = onContinue;

        (Eyebrow, Title, ActionLabel, ArtworkKey) = kind switch
        {
            SessionIntroKind.Qualifying =>
                ("RACE WEEKEND", "QUALIFYING", "Begin qualifying", "qualifying"),
            SessionIntroKind.Race =>
                ("RACE DAY", "RACE", "Start the race", "race"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    public SessionIntroKind Kind { get; }

    public string Eyebrow { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string ActionLabel { get; }

    /// <summary>Stable key for the App lane's artwork resolver: <c>qualifying</c> or <c>race</c>.</summary>
    public string ArtworkKey { get; }

    private bool CanContinue => !_continued;

    /// <summary>Advance exactly once even if a rapid double-click reaches the command twice.</summary>
    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        if (_continued)
            return;

        _continued = true;
        ContinueCommand.NotifyCanExecuteChanged();
        _onContinue();
    }
}
