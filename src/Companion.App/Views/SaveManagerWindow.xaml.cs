using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Companion.Data;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.App.Views;

/// <summary>
/// App-only save/reload surface over the shipped <see cref="ICareerSession"/> contract. The window
/// may borrow the live Hub session or own a temporary session opened for a Start-gallery card.
/// A restore spends either kind of session, so the window closes and immediately routes the career
/// path through the shell's normal open command.
/// </summary>
public partial class SaveManagerWindow : Window, INotifyPropertyChanged
{
    private readonly ICareerSession _session;
    private readonly string _careerPath;
    private readonly ShellViewModel _shell;
    private readonly bool _ownsSession;
    private bool _sessionSpent;
    private string? _errorMessage;

    public SaveManagerWindow(
        ICareerSession session,
        string careerPath,
        ShellViewModel shell,
        bool ownsSession = false)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentException.ThrowIfNullOrWhiteSpace(careerPath);
        _careerPath = Path.GetFullPath(careerPath);
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _ownsSession = ownsSession;

        InitializeComponent();
        DataContext = this;
        Closed += OnClosed;
        RefreshSlots();
    }

    /// <summary>Current manual and automatic snapshots, newest first.</summary>
    public ObservableCollection<SaveSlotInfo> Slots { get; } = [];

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value)
                return;
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Shows the manager only when the supplied career is in Normal mode. When a Start-gallery caller
    /// supplied an owned temporary session, a disabled career is disposed here as part of the gate.
    /// </summary>
    public static bool ShowIfEnabled(
        Window? owner,
        ICareerSession session,
        string careerPath,
        ShellViewModel shell,
        bool ownsSession = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.SavesEnabled)
        {
            if (ownsSession)
                (session as IDisposable)?.Dispose();
            return false;
        }

        var window = new SaveManagerWindow(session, careerPath, shell, ownsSession)
        {
            Owner = owner,
        };
        window.ShowDialog();
        return true;
    }

    private void RefreshSlots()
    {
        try
        {
            ErrorMessage = null;
            Slots.Clear();
            foreach (var slot in _session.SaveSlots())
                Slots.Add(slot);
        }
        catch (Exception ex) when (IsExpectedFileFailure(ex))
        {
            ErrorMessage = $"Could not read this career's save points, {ex.Message}";
        }
    }

    private void OnCreateSave(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveLabelDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            ErrorMessage = null;
            _session.SaveToSlot(dialog.Label);
            RefreshSlots();
        }
        catch (Exception ex) when (IsExpectedFileFailure(ex))
        {
            ErrorMessage = $"Could not create the save point, {ex.Message}";
        }
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SaveSlotInfo slot)
            return;

        RestoreAndReopen(this, _session, _careerPath, _shell, slot, beforeReopen: () =>
        {
            _sessionSpent = true;
            Close();
        });
    }

    /// <summary>Restores one captured slot and immediately reopens the spent career through the
    /// shell's normal Start.OpenCareer route. Shared by this manager and the terminal death screen.</summary>
    public static bool RestoreAndReopen(
        Window? owner,
        ICareerSession session,
        string careerPath,
        ShellViewModel shell,
        SaveSlotInfo slot,
        Action? beforeReopen = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(careerPath);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(slot);

        string prompt =
            $"Restore '{slot.Label}'?\n\nSeason {slot.SeasonYear} · Round {slot.Round}\n\n" +
            "This replaces the whole current career timeline. Results and progress after this save point will be lost.";
        var choice = owner is null
            ? MessageBox.Show(prompt, "Restore career save", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(owner, prompt, "Restore career save", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes)
            return false;

        string? failure = null;
        try
        {
            session.RestoreSlot(slot.SlotId);
        }
        catch (Exception ex) when (IsExpectedFileFailure(ex))
        {
            // Restore releases the DB before replacing the working file. Even when replacement
            // fails, the session may therefore be spent; always route through a clean reopen.
            failure = ex.Message;
        }

        (session as IDisposable)?.Dispose();

        if (failure is not null)
        {
            string message =
                $"The save point could not be restored:\n\n{failure}\n\n" +
                "The career will be reopened from the working file.";
            if (owner is null)
                MessageBox.Show(message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                MessageBox.Show(owner, message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        beforeReopen?.Invoke();

        // Raises Start.ContinueRequested → Shell.OpenCareer/AttachHome. Never read session again.
        shell.Start.OpenCareerCommand.Execute(Path.GetFullPath(careerPath));
        return true;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SaveSlotInfo slot)
            return;

        var choice = MessageBox.Show(
            $"Delete the save point '{slot.Label}'?\n\nThis removes its career snapshot permanently.",
            "Delete save point", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes)
            return;

        try
        {
            ErrorMessage = null;
            _session.DeleteSlot(slot.SlotId);
            RefreshSlots();
        }
        catch (Exception ex) when (IsExpectedFileFailure(ex))
        {
            ErrorMessage = $"Could not delete the save point, {ex.Message}";
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        if (_ownsSession && !_sessionSpent)
            (_session as IDisposable)?.Dispose();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        e.Handled = true;
        Close();
    }

    private static bool IsExpectedFileFailure(Exception ex) =>
        ex is ArgumentException or InvalidOperationException or NotSupportedException or
            IOException or UnauthorizedAccessException;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
