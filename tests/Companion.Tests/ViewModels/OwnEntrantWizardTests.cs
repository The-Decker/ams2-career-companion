using Companion.ViewModels.Wizard;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The wizard's "race as your own entrant" escape hatch (custom AMS2 livery): typing a livery on the
/// seat-pick step lets the player advance WITHOUT selecting a pack seat, adds a locked own-entrant row
/// to the grid, and creates the career on that livery — which the session resolves to the stable
/// synthetic player-entrant (a non-pack livery). The ordinary seat-pick flow stays byte-identical.
/// </summary>
public sealed class OwnEntrantWizardTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-own-entrant-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private NewCareerWizardViewModel WizardOnSeatPick(out FakeCareerFactory factory)
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(_root, "packs", "pack"));
        factory = new FakeCareerFactory();
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());
        var wizard = new NewCareerWizardViewModel(
            environment, factory,
            packSearchRoots: [Path.Combine(_root, "packs")],
            careersDirectory: Path.Combine(_root, "careers"),
            seedSource: new Random(9));

        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);                 // -> Verification
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);                 // -> Character
        Assert.Equal(WizardStep.Character, wizard.Step);
        wizard.Character!.Name = "Privateer Driver";
        wizard.NextCommand.Execute(null);                 // -> SeatPick
        Assert.Equal(WizardStep.SeatPick, wizard.Step);
        return wizard;
    }

    [Fact]
    public void CustomLivery_UnlocksNext_WithoutASeat_AndCreatesOnThatLivery()
    {
        var wizard = WizardOnSeatPick(out var factory);

        // No seat, no custom livery → the seat step is not satisfied.
        Assert.False(wizard.NextCommand.CanExecute(null));

        // Typing a custom livery is the own-entrant escape hatch — Next unlocks with no seat picked.
        wizard.CustomLiveryName = "My Privateer Skin";
        Assert.True(wizard.IsOwnEntrant);
        Assert.True(wizard.NextCommand.CanExecute(null));

        wizard.NextCommand.Execute(null);                 // -> Grid (built on entry)

        // The grid shows exactly one locked row — the player's own entrant, on the typed livery.
        var locked = Assert.Single(wizard.GridChoices, c => c.IsLocked);
        Assert.Equal("My Privateer Skin", locked.LiveryName);
        Assert.Contains("own entrant", locked.DriverName);

        wizard.NextCommand.Execute(null);                 // -> Confirm
        wizard.NextCommand.Execute(null);                 // Create

        Assert.Equal("My Privateer Skin", factory.LastRequest!.PlayerLiveryName);
    }

    [Fact]
    public void PickingASeat_WithNoCustomLivery_UsesTheSeatLivery_AndNoOwnEntrantRow()
    {
        var wizard = WizardOnSeatPick(out var factory);

        var seat = wizard.Seats.First();
        wizard.SelectedSeat = seat;
        Assert.False(wizard.IsOwnEntrant);
        wizard.NextCommand.Execute(null);                 // -> Grid (built on entry)

        // The locked row is the picked seat, and no synthetic own-entrant row appears.
        Assert.DoesNotContain(wizard.GridChoices, c => c.DriverName.Contains("own entrant"));

        wizard.NextCommand.Execute(null);                 // -> Confirm
        wizard.NextCommand.Execute(null);                 // Create

        Assert.Equal(seat.LiveryName, factory.LastRequest!.PlayerLiveryName);
    }

    [Fact]
    public void CustomLivery_TakesPrecedence_OverAnAlsoSelectedSeat()
    {
        var wizard = WizardOnSeatPick(out var factory);

        // Even if a seat is also selected, a typed custom livery wins (the own-entrant path).
        wizard.SelectedSeat = wizard.Seats.First();
        wizard.CustomLiveryName = "  Override Skin  "; // trimmed on use
        Assert.True(wizard.IsOwnEntrant);

        wizard.NextCommand.Execute(null);                 // -> Grid
        wizard.NextCommand.Execute(null);                 // -> Confirm
        wizard.NextCommand.Execute(null);                 // Create

        Assert.Equal("Override Skin", factory.LastRequest!.PlayerLiveryName);
    }
}
