using System.Text.Json;
using Companion.ViewModels.Settings;
using Companion.ViewModels.Shell;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Discoverability polish (ux-round contract section 4): the three first-run coach marks
/// (briefing checklist, result-entry "type OR drag", standings rules chip) show until
/// dismissed and the dismissal persists in settings; the remembered window placement
/// round-trips, normalizes away garbage, and clamps back onto the current virtual screen.
/// </summary>
public sealed class CoachMarksAndPlacementTests
{
    private static SettingsService Service(AppSettings? initial = null) =>
        new(new InMemorySettingsStore(initial));

    // ---------- coach marks ----------

    [Fact]
    public void CoachMarks_AllThreeShow_OnFirstRun()
    {
        var marks = new CoachMarksViewModel(Service());

        Assert.True(marks.ShowBriefingChecklist);
        Assert.True(marks.ShowResultEntryTypeOrDrag);
        Assert.True(marks.ShowStandingsRulesChip);
    }

    [Fact]
    public void Dismiss_HidesTheCallout_AndPersistsTheFlag()
    {
        var settings = Service();
        var marks = new CoachMarksViewModel(settings);

        marks.DismissBriefingChecklistCommand.Execute(null);

        Assert.False(marks.ShowBriefingChecklist);
        Assert.True(marks.ShowResultEntryTypeOrDrag); // the others are untouched
        Assert.True(marks.ShowStandingsRulesChip);
        Assert.Contains(CoachMarksViewModel.BriefingChecklistId,
            settings.Current.DismissedCoachMarks);
    }

    [Fact]
    public void Dismissals_NeverShowAgain_AcrossReconstruction()
    {
        var settings = Service();
        var first = new CoachMarksViewModel(settings);
        first.DismissResultEntryTypeOrDragCommand.Execute(null);
        first.DismissStandingsRulesChipCommand.Execute(null);

        // a fresh viewmodel over the same settings = the next career open / app run
        var second = new CoachMarksViewModel(settings);

        Assert.True(second.ShowBriefingChecklist);
        Assert.False(second.ShowResultEntryTypeOrDrag);
        Assert.False(second.ShowStandingsRulesChip);
    }

    [Fact]
    public void DoubleDismiss_DoesNotDuplicateTheStoredId()
    {
        var settings = Service();
        var marks = new CoachMarksViewModel(settings);

        marks.DismissBriefingChecklistCommand.Execute(null);
        marks.DismissBriefingChecklistCommand.Execute(null);

        Assert.Single(settings.Current.DismissedCoachMarks);
    }

    [Fact]
    public void DismissedIds_MatchCaseInsensitively()
    {
        var settings = Service(new AppSettings
        {
            DismissedCoachMarks = ["BRIEFINGCHECKLIST"],
        });

        var marks = new CoachMarksViewModel(settings);

        Assert.False(marks.ShowBriefingChecklist);
    }

    [Fact]
    public void UnknownIds_SurviveDismissal_ForForwardCompatibility()
    {
        var settings = Service(new AppSettings { DismissedCoachMarks = ["futureTip"] });
        var marks = new CoachMarksViewModel(settings);

        marks.DismissStandingsRulesChipCommand.Execute(null);

        Assert.Contains("futureTip", settings.Current.DismissedCoachMarks);
        Assert.Contains(CoachMarksViewModel.StandingsRulesChipId,
            settings.Current.DismissedCoachMarks);
    }

    [Fact]
    public void NullSettings_StillDismissesLocally_WithoutThrowing()
    {
        var marks = new CoachMarksViewModel(settings: null);

        Assert.True(marks.ShowBriefingChecklist);
        marks.DismissBriefingChecklistCommand.Execute(null);
        Assert.False(marks.ShowBriefingChecklist);
    }

    [Fact]
    public void ResetToDefaults_BringsTheCoachMarksBack()
    {
        var settings = Service();
        new CoachMarksViewModel(settings).DismissBriefingChecklistCommand.Execute(null);

        settings.Reset();

        Assert.True(new CoachMarksViewModel(settings).ShowBriefingChecklist);
    }

    [Fact]
    public void HomeViewModel_WiresTheCoachMarks_ToTheCareerSettings()
    {
        var settings = Service();
        using (var home = new HomeViewModel(new FakeCareerSession(), settings: settings))
        {
            Assert.True(home.CoachMarks.ShowResultEntryTypeOrDrag);
            home.CoachMarks.DismissResultEntryTypeOrDragCommand.Execute(null);
        }

        // the next career opened over the same settings no longer shows it
        using var next = new HomeViewModel(new FakeCareerSession(), settings: settings);
        Assert.False(next.CoachMarks.ShowResultEntryTypeOrDrag);
    }

    // ---------- settings normalization for the new fields ----------

    [Fact]
    public void Normalized_TrimsDedupesAndDropsEmpty_CoachMarkIds()
    {
        var settings = new AppSettings
        {
            DismissedCoachMarks = ["  briefingChecklist  ", "", "   ", "BriefingChecklist", "other"],
        }.Normalized();

        Assert.Equal(2, settings.DismissedCoachMarks.Count);
        Assert.Contains("briefingChecklist", settings.DismissedCoachMarks);
        Assert.Contains("other", settings.DismissedCoachMarks);
    }

    [Theory]
    [InlineData(double.NaN, 100, 800, 600)]
    [InlineData(0, 0, 0, 600)]
    [InlineData(0, 0, 800, -50)]
    [InlineData(0, double.PositiveInfinity, 800, 600)]
    public void Normalized_DropsUnusableWindowPlacement(
        double left, double top, double width, double height)
    {
        var settings = new AppSettings
        {
            WindowPlacement = new WindowPlacementSettings
            {
                Left = left, Top = top, Width = width, Height = height,
            },
        }.Normalized();

        Assert.Null(settings.WindowPlacement);
    }

    [Fact]
    public void Normalized_KeepsAValidWindowPlacement()
    {
        var placement = new WindowPlacementSettings
        {
            Left = -8, Top = 12, Width = 1120, Height = 740, IsMaximized = true,
        };

        Assert.Equal(placement, new AppSettings { WindowPlacement = placement }
            .Normalized().WindowPlacement);
    }

    // ---------- window placement clamping (monitors change between runs) ----------

    [Fact]
    public void ClampTo_LeavesAnInBoundsPlacementAlone()
    {
        var placement = new WindowPlacementSettings { Left = 100, Top = 50, Width = 1000, Height = 700 };

        Assert.Equal(placement, placement.ClampTo(0, 0, 1920, 1080));
    }

    [Fact]
    public void ClampTo_PullsAnOffscreenWindowBackIn()
    {
        var placement = new WindowPlacementSettings { Left = 5000, Top = -900, Width = 1000, Height = 700 };

        var clamped = placement.ClampTo(0, 0, 1920, 1080);

        Assert.Equal(920, clamped.Left);   // right edge lands on the screen edge
        Assert.Equal(0, clamped.Top);
        Assert.Equal(1000, clamped.Width); // size untouched — it fits
        Assert.Equal(700, clamped.Height);
    }

    [Fact]
    public void ClampTo_ShrinksAWindowLargerThanTheScreen()
    {
        var placement = new WindowPlacementSettings { Left = 0, Top = 0, Width = 4000, Height = 2500 };

        var clamped = placement.ClampTo(0, 0, 1920, 1080);

        Assert.Equal(1920, clamped.Width);
        Assert.Equal(1080, clamped.Height);
    }

    [Fact]
    public void ClampTo_HandlesANegativeOriginVirtualScreen()
    {
        // a second monitor left of the primary: the virtual screen starts at -1920
        var placement = new WindowPlacementSettings { Left = -1800, Top = 100, Width = 1000, Height = 700 };

        Assert.Equal(placement, placement.ClampTo(-1920, 0, 3840, 1080));
    }

    // ---------- persistence round-trip of the new fields ----------

    [Fact]
    public void NewFields_RoundTripThroughTheJsonStore()
    {
        string path = Path.Combine(
            Directory.CreateTempSubdirectory("companion-coach-").FullName, "settings.json");
        try
        {
            var settings = new AppSettings
            {
                DismissedCoachMarks = ["briefingChecklist", "standingsRulesChip"],
                WindowPlacement = new WindowPlacementSettings
                {
                    Left = 40, Top = 60, Width = 1280, Height = 800, IsMaximized = true,
                },
            };
            new JsonSettingsStore(path).Save(settings);

            var reloaded = new JsonSettingsStore(path).Load();

            Assert.Equal(settings.DismissedCoachMarks, reloaded.DismissedCoachMarks);
            Assert.Equal(settings.WindowPlacement, reloaded.WindowPlacement);

            // camelCase on disk like every other key (contract section 3)
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(document.RootElement.TryGetProperty("dismissedCoachMarks", out _));
            Assert.True(document.RootElement.TryGetProperty("windowPlacement", out _));
        }
        finally
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
