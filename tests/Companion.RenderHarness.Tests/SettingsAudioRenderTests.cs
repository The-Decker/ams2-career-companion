using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Companion.App.Audio;
using Companion.App.Views;
using Companion.ViewModels.Settings;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Pins the manual, app-lifetime music transport and the deliberately sparse interaction-SFX
/// contract. Music is never driven by shell navigation: only the player's commands, direct track
/// selection, and a natural media-end event may change the selected song or playback state.
/// </summary>
public sealed class SettingsAudioRenderTests
{
    [Fact]
    public void SettingsView_AudioPanel_LaysOutAndTwoWayBindsMasterControls()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = new AudioSettingsHost();
            var view = new SettingsView { DataContext = host };
            view.Measure(new Size(1050, 820));
            view.Arrange(new Rect(0, 0, 1050, 820));
            view.UpdateLayout();

            var panel = (FrameworkElement)view.FindName("AudioSettingsPanel");
            var uiSize = (Slider)view.FindName("UiSizeSlider");
            var enabled = (CheckBox)view.FindName("SoundEnabledCheckBox");
            var master = (Slider)view.FindName("MasterVolumeSlider");
            var focusMute = (CheckBox)view.FindName("MuteWhenUnfocusedCheckBox");
            Button preview = FindByAutomationName<Button>(view, "Preview menu effects");

            Assert.True(panel.ActualWidth > 0);
            Assert.True(panel.ActualHeight > 0);
            Assert.Equal(90, uiSize.Minimum);
            Assert.Equal(160, uiSize.Maximum);
            Assert.Equal(100, uiSize.Value);
            Assert.True(enabled.IsChecked);
            Assert.Equal(80, master.Value);
            Assert.True(focusMute.IsChecked);
            Assert.Equal(
                "Mute menu effects when another application has focus",
                focusMute.Content);
            Assert.Equal(
                "Music keeps playing in the background; interface cues stop while this app is inactive",
                focusMute.ToolTip);
            Assert.Equal(SoundEffectCue.Confirm, SoundAssist.GetCue(preview));
            Assert.Throws<InvalidOperationException>(() =>
                FindByAutomationName<Slider>(view, "Music volume"));

            master.Value = 55;
            uiSize.Value = 160;
            enabled.IsChecked = false;
            focusMute.IsChecked = false;
            WpfRenderHarness.Pump();

            Assert.Equal(55, host.MasterVolumePercent);
            Assert.Equal(160, host.FontScalePercent);
            Assert.False(host.SoundEnabled);
            Assert.False(host.MuteWhenUnfocused);
        });
    }

    [Fact]
    public void SharedButton_CommonStatesReturnToNeutralWithoutStickyHoverOrPress()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var button = new Button { Content = "TEST", Width = 160, Height = 48 };
            var window = new Window
            {
                Content = button,
                Width = 220,
                Height = 100,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                WpfRenderHarness.Pump(DispatcherPriority.Loaded);
                button.ApplyTemplate();

                var visualRoot = Assert.IsType<Grid>(button.Template.FindName("VisualRoot", button));
                var hover = Assert.IsType<Border>(button.Template.FindName("HoverOverlay", button));
                var inset = Assert.IsType<Border>(button.Template.FindName("PressedInset", button));
                var scale = Assert.IsType<ScaleTransform>(button.Template.FindName("PressScale", button));
                var translate = Assert.IsType<TranslateTransform>(
                    button.Template.FindName("PressTranslate", button));

                Assert.True(VisualStateManager.GoToElementState(visualRoot, "MouseOver", useTransitions: false));
                WpfRenderHarness.Pump(DispatcherPriority.Render);
                Assert.Equal(.08, hover.Opacity, precision: 12);
                Assert.Equal(1.01, scale.ScaleX, precision: 12);
                Assert.Equal(-1, translate.Y, precision: 12);

                Assert.True(VisualStateManager.GoToElementState(visualRoot, "Pressed", useTransitions: false));
                WpfRenderHarness.Pump(DispatcherPriority.Render);
                Assert.Equal(.17, hover.Opacity, precision: 12);
                Assert.Equal(.48, inset.Opacity, precision: 12);
                Assert.Equal(.99, scale.ScaleX, precision: 12);
                Assert.Equal(1, translate.Y, precision: 12);

                Assert.True(VisualStateManager.GoToElementState(visualRoot, "Normal", useTransitions: false));
                WpfRenderHarness.Pump(DispatcherPriority.Render);
                Assert.Equal(0, hover.Opacity, precision: 12);
                Assert.Equal(0, inset.Opacity, precision: 12);
                Assert.Equal(1, scale.ScaleX, precision: 12);
                Assert.Equal(0, translate.Y, precision: 12);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void SoundscapeCatalog_DeclaresOrderedPlaylistAndNineTypedInteractionWavs()
    {
        (string Title, string Path, double GainDecibels)[] expectedPlaylist =
        [
            ("The Long Climb", "Assets/Audio/Music/the-long-climb.mp3", -1.49),
            ("Pitwall at Dusk", "Assets/Audio/Music/pitwall-at-dusk.mp3", -0.99),
            ("Amber Pitlane", "Assets/Audio/Music/amber-pitlane.mp3", -1.62),
            ("Telemetry at Twilight", "Assets/Audio/Music/telemetry-at-twilight.mp3", -1.34),
            ("Night Shift", "Assets/Audio/Music/night-shift.mp3", -2.13),
            ("Race Control", "Assets/Audio/Music/race-control.mp3", -1.37),
            ("Grid Locked", "Assets/Audio/Music/grid-locked.mp3", -0.26),
            ("Formation Hold", "Assets/Audio/Music/formation-hold.mp3", -2.01),
            ("After the Flag", "Assets/Audio/Music/after-the-flag.mp3", -0.60),
            ("Cooling Lap", "Assets/Audio/Music/cooling-lap.mp3", -0.90),
            ("Empty Grandstands", "Assets/Audio/Music/empty-grandstands.mp3", -1.30),
            ("Super Monaco Grand Prix Intro", "Assets/Audio/Music/intro-smgp.mp3", -1.70),
            ("First Light Briefing", "Assets/Audio/Music/first-light-briefing.mp3", -0.20),
            ("Morning Question", "Assets/Audio/Music/morning-question.mp3", -1.60),
            ("Lights in the Distance", "Assets/Audio/Music/lights-in-the-distance.mp3", -1.60),
            ("Open Table", "Assets/Audio/Music/open-table.mp3", -2.10),
            ("Strategy Room", "Assets/Audio/Music/strategy-room.mp3", -1.50),
            ("Open Ledger", "Assets/Audio/Music/open-ledger.mp3", -1.10),
            ("Rain Before Rhythm", "Assets/Audio/Music/rain-before-rhythm.mp3", -1.50),
            ("Wet Line Reverie", "Assets/Audio/Music/wet-line-reverie.mp3", -0.70),
            ("Golden Lap", "Assets/Audio/Music/golden-lap.mp3", -1.60),
            ("Injury", "Assets/Audio/Music/injury.mp3", -0.50),
            ("Injury / Death", "Assets/Audio/Music/injury-death.mp3", -2.10),
        ];
        string[] expectedSfx =
        [
            "Assets/Audio/Sfx/navigate.wav",
            "Assets/Audio/Sfx/commit.wav",
            "Assets/Audio/Sfx/seat-confirm.wav",
            "Assets/Audio/Sfx/back.wav",
            "Assets/Audio/Sfx/bucket-pickup.wav",
            "Assets/Audio/Sfx/bucket-place.wav",
            "Assets/Audio/Sfx/warning.wav",
            "Assets/Audio/Sfx/destructive.wav",
            "Assets/Audio/Sfx/skill-unlock.wav",
        ];

        Assert.Equal(expectedPlaylist.Length, SoundscapeCatalog.Playlist.Count);
        for (int i = 0; i < expectedPlaylist.Length; i++)
        {
            Assert.Equal(expectedPlaylist[i].Title, SoundscapeCatalog.Playlist[i].Title);
            Assert.Equal(expectedPlaylist[i].Path, SoundscapeCatalog.Playlist[i].RelativePath);
            Assert.Equal(expectedPlaylist[i].GainDecibels, SoundscapeCatalog.Playlist[i].GainDecibels);
            double expectedLinearGain = Math.Pow(10, expectedPlaylist[i].GainDecibels / 20.0);
            Assert.Equal(expectedLinearGain, SoundscapeCatalog.Playlist[i].PlaybackGain, precision: 12);
            Assert.Equal(
                expectedLinearGain,
                SoundscapeCatalog.MusicTrack(SoundscapeCatalog.Playlist[i]).Gain,
                precision: 12);
        }

        Assert.Equal(32, SoundscapeCatalog.DeclaredRelativePaths.Count);
        Assert.Equal(23, SoundscapeCatalog.DeclaredRelativePaths.Count(path =>
            path.StartsWith("Assets/Audio/Music/", StringComparison.Ordinal) &&
            path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(
            Enum.GetValues<SoundEffectCue>().Length - 1,
            SoundscapeCatalog.DeclaredRelativePaths.Count(path =>
                path.StartsWith("Assets/Audio/Sfx/", StringComparison.Ordinal) &&
                path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(
            expectedSfx.Order(StringComparer.OrdinalIgnoreCase),
            SoundscapeCatalog.DeclaredRelativePaths
                .Where(path => path.StartsWith("Assets/Audio/Sfx/", StringComparison.Ordinal))
                .Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(SoundscapeCatalog.DeclaredRelativePaths, path =>
        {
            Assert.DoesNotContain('\\', path);
            Assert.DoesNotContain("..", path, StringComparison.Ordinal);
        });
        Assert.Equal(
            SoundscapeCatalog.DeclaredRelativePaths.Count,
            SoundscapeCatalog.DeclaredRelativePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void GeneratedSfx_AreExactClickSafePcmMasters()
    {
        var expected = new Dictionary<string, (int Frames, double PeakDbfs)>(StringComparer.OrdinalIgnoreCase)
        {
            ["navigate.wav"] = (4_320, -13.0),
            ["commit.wav"] = (15_360, -10.5),
            ["seat-confirm.wav"] = (23_040, -10.0),
            ["back.wav"] = (13_440, -11.0),
            ["warning.wav"] = (24_960, -10.0),
            ["destructive.wav"] = (34_560, -9.5),
            ["skill-unlock.wav"] = (43_200, -9.5),
            ["bucket-pickup.wav"] = (6_720, -12.5),
            ["bucket-place.wav"] = (10_560, -11.5),
        };
        string sfxRoot = Path.Combine(
            FindRepositoryRoot(), "src", "Companion.App", "Assets", "Audio", "Sfx");

        Assert.Equal(
            expected.Keys.Order(StringComparer.OrdinalIgnoreCase),
            Directory.EnumerateFiles(sfxRoot, "*.wav")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase));

        foreach ((string fileName, (int frames, double targetPeakDbfs)) in expected)
        {
            using var stream = File.OpenRead(Path.Combine(sfxRoot, fileName));
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

            Assert.Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
            uint riffSize = reader.ReadUInt32();
            Assert.Equal("WAVE", Encoding.ASCII.GetString(reader.ReadBytes(4)));
            Assert.Equal("fmt ", Encoding.ASCII.GetString(reader.ReadBytes(4)));
            Assert.Equal(16u, reader.ReadUInt32());
            Assert.Equal((ushort)1, reader.ReadUInt16());
            Assert.Equal((ushort)1, reader.ReadUInt16());
            Assert.Equal(48_000u, reader.ReadUInt32());
            Assert.Equal(96_000u, reader.ReadUInt32());
            Assert.Equal((ushort)2, reader.ReadUInt16());
            Assert.Equal((ushort)16, reader.ReadUInt16());
            Assert.Equal("data", Encoding.ASCII.GetString(reader.ReadBytes(4)));
            uint dataLength = reader.ReadUInt32();
            Assert.Equal(frames * 2, (int)dataLength);
            Assert.Equal(stream.Length - 8, riffSize);
            Assert.Equal(44 + dataLength, (ulong)stream.Length);

            short first = reader.ReadInt16();
            int peak = Math.Abs((int)first);
            short last = first;
            for (int frame = 1; frame < frames; frame++)
            {
                last = reader.ReadInt16();
                peak = Math.Max(peak, Math.Abs((int)last));
            }

            Assert.Equal((short)0, first);
            Assert.Equal((short)0, last);
            double peakDbfs = 20 * Math.Log10(peak / 32768.0);
            Assert.InRange(peakDbfs, targetPeakDbfs - .02, targetPeakDbfs + .02);
        }
    }

    [Fact]
    public void SoundscapeCatalog_PinsTheInteractionMixAndAntiChatterPolicy()
    {
        (SoundEffectCue Cue, string File, double Gain, int CooldownMs, string Group,
            int DedupeMs, double Duck, int HoldMs)[] expected =
        [
            (SoundEffectCue.Navigate, "navigate.wav", .50, 24, "navigation", 12, 1, 0),
            (SoundEffectCue.Confirm, "commit.wav", .60, 90, "action", 45, 1, 0),
            (SoundEffectCue.SeatConfirm, "seat-confirm.wav", .62, 120, "seat", 60, 1, 0),
            (SoundEffectCue.Back, "back.wav", .50, 90, "navigation", 45, 1, 0),
            (SoundEffectCue.BucketPickup, "bucket-pickup.wav", .40, 45, "bucket", 25, 1, 0),
            (SoundEffectCue.BucketPlace, "bucket-place.wav", .46, 55, "bucket", 30, 1, 0),
            (SoundEffectCue.Warning, "warning.wav", .70, 420, "outcome", 140, .80, 500),
            (SoundEffectCue.Destructive, "destructive.wav", .72, 650, "critical", 240, .68, 700),
            (SoundEffectCue.SkillUnlock, "skill-unlock.wav", .72, 750, "progression", 300, .70, 800),
        ];
        var catalog = new SoundscapeCatalog();

        foreach (var item in expected)
        {
            Assert.True(catalog.TryGetEffect(item.Cue, out SoundEffectDefinition definition));
            SoundscapeTrack track = catalog.NextEffect(item.Cue, definition);
            Assert.Equal($"Assets/Audio/Sfx/{item.File}", track.RelativePath);
            Assert.Equal(item.Gain, track.Gain, precision: 12);
            Assert.Equal(TimeSpan.FromMilliseconds(item.CooldownMs), definition.Cooldown);
            Assert.Equal(item.Group, definition.DedupeGroup);
            Assert.Equal(TimeSpan.FromMilliseconds(item.DedupeMs), definition.DedupeWindow);
            Assert.Equal(item.Duck, definition.MusicDuck, precision: 12);
            Assert.Equal(TimeSpan.FromMilliseconds(item.HoldMs), definition.DuckDuration);
        }
    }

    [Fact]
    public void ManualPlayer_StartsOnFirstTrackPaused_AndToggleControlsTransport()
    {
        var engine = new FakeAudioEngine { PlayMusicResult = false };
        using var controller = CreateController(engine);

        Assert.Same(SoundscapeCatalog.Playlist[0], controller.Player.SelectedTrack);
        Assert.Equal("The Long Climb", controller.Player.TrackTitle);
        Assert.False(controller.Player.IsPlaying);
        Assert.Empty(engine.Selections);
        Assert.Equal(0, engine.SelectMusicCalls);

        controller.Player.TogglePlayCommand.Execute(null);

        Assert.True(controller.Player.IsPlaying);
        Assert.Equal(1, engine.PlayMusicCalls);
        Assert.Equal(1, engine.SelectMusicCalls);
        Assert.Collection(engine.Selections, selection =>
        {
            Assert.Equal("Assets/Audio/Music/the-long-climb.mp3", selection.Track.RelativePath);
            Assert.True(selection.Play);
        });

        controller.Player.TogglePlayCommand.Execute(null);

        Assert.False(controller.Player.IsPlaying);
        Assert.Equal(1, engine.PauseMusicCalls);
        Assert.Single(engine.Selections);
    }

    [Fact]
    public void ManualPlayer_ExplicitPauseSurvivesFocusDisableAndEnable()
    {
        var engine = new FakeAudioEngine();
        using var controller = CreateController(engine);
        controller.Player.TogglePlayCommand.Execute(null);
        controller.Player.TogglePlayCommand.Execute(null);

        controller.SetApplicationActive(false);
        controller.SetApplicationActive(true);

        Assert.Equal([false, true], engine.ApplicationActiveChanges);
        Assert.False(controller.Player.IsPlaying);
        Assert.Equal(1, engine.PlayMusicCalls);
        Assert.Equal(1, engine.PauseMusicCalls);
    }

    [Fact]
    public void ManualPlayer_DirectSelectionWhilePausedLoadsSelectionWithoutStartingIt()
    {
        var engine = new FakeAudioEngine();
        using var controller = CreateController(engine);

        controller.Player.SelectedTrack = controller.Player.Tracks[5];

        Assert.Same(controller.Player.Tracks[5], controller.Player.SelectedTrack);
        Assert.Equal("Race Control", controller.Player.TrackTitle);
        Assert.False(controller.Player.IsPlaying);
        Assert.Equal("Assets/Audio/Music/race-control.mp3", engine.Selections[^1].Track.RelativePath);
        Assert.False(engine.Selections[^1].Play);
        Assert.Equal(0, engine.PlayMusicCalls);
    }

    [Fact]
    public void ManualPlayer_PreviousAndNextWrapAndPreservePausedOrPlayingState()
    {
        var engine = new FakeAudioEngine();
        using var controller = CreateController(engine);
        MusicPlayerViewModel player = controller.Player;

        player.PreviousCommand.Execute(null);
        Assert.Same(player.Tracks[^1], player.SelectedTrack);
        Assert.False(player.IsPlaying);
        Assert.False(engine.Selections[^1].Play);

        player.NextCommand.Execute(null);
        Assert.Same(player.Tracks[0], player.SelectedTrack);
        Assert.False(player.IsPlaying);
        Assert.False(engine.Selections[^1].Play);

        player.TogglePlayCommand.Execute(null);
        player.NextCommand.Execute(null);
        Assert.Same(player.Tracks[1], player.SelectedTrack);
        Assert.True(player.IsPlaying);
        Assert.True(engine.Selections[^1].Play);

        player.PreviousCommand.Execute(null);
        Assert.Same(player.Tracks[0], player.SelectedTrack);
        Assert.True(player.IsPlaying);
        Assert.True(engine.Selections[^1].Play);
    }

    [Fact]
    public void ManualPlayer_NaturalMusicEndAdvancesAndStartsFollowingTrack()
    {
        var engine = new FakeAudioEngine();
        using var controller = CreateController(engine);
        controller.Player.TogglePlayCommand.Execute(null);

        engine.RaiseMusicEnded();

        Assert.Same(controller.Player.Tracks[1], controller.Player.SelectedTrack);
        Assert.True(controller.Player.IsPlaying);
        Assert.Equal("Assets/Audio/Music/pitwall-at-dusk.mp3", engine.Selections[^1].Track.RelativePath);
        Assert.True(engine.Selections[^1].Play);
    }

    [Fact]
    public async Task ManualPlayer_AsynchronousMusicFailureResetsPlayingState()
    {
        var engine = new FakeAudioEngine();
        using var controller = CreateController(engine);
        controller.Player.TogglePlayCommand.Execute(null);
        Assert.True(controller.Player.IsPlaying);

        await Task.Run(engine.RaiseMusicFailed);

        Assert.False(controller.Player.IsPlaying);
        Assert.Same(controller.Player.Tracks[0], controller.Player.SelectedTrack);
    }

    [Fact]
    public void ManualPlayer_PlayAfterFailureReloadsTheVisibleSelection()
    {
        var engine = new FakeAudioEngine();
        using var controller = CreateController(engine);
        controller.Player.TogglePlayCommand.Execute(null);
        engine.RaiseMusicFailed();
        engine.PlayMusicResult = false;

        controller.Player.TogglePlayCommand.Execute(null);

        Assert.True(controller.Player.IsPlaying);
        Assert.Same(controller.Player.Tracks[0], controller.Player.SelectedTrack);
        Assert.Equal("Assets/Audio/Music/the-long-climb.mp3", engine.Selections[^1].Track.RelativePath);
        Assert.True(engine.Selections[^1].Play);
    }

    [Fact]
    public void ManualPlayer_PlayAfterFailedStartupSkipsAnUnavailableVisibleSelection()
    {
        var engine = new FakeAudioEngine { PlayMusicResult = false };
        engine.UnloadablePaths.Add("Assets/Audio/Music/the-long-climb.mp3");
        using var controller = CreateController(engine);

        controller.Player.TogglePlayCommand.Execute(null);

        Assert.True(controller.Player.IsPlaying);
        Assert.Same(controller.Player.Tracks[1], controller.Player.SelectedTrack);
        Assert.Equal(
            [
                "Assets/Audio/Music/the-long-climb.mp3",
                "Assets/Audio/Music/pitwall-at-dusk.mp3",
            ],
            engine.Selections.Select(selection => selection.Track.RelativePath));
        Assert.True(engine.Selections[^1].Play);
    }

    [Fact]
    public void ManualPlayer_PlayRecoveryStopsAfterOnePlaylistCycle()
    {
        var engine = new FakeAudioEngine { PlayMusicResult = false };
        foreach (MusicPlaylistTrack track in SoundscapeCatalog.Playlist)
            engine.UnloadablePaths.Add(track.RelativePath);
        using var controller = CreateController(engine);
        engine.Selections.Clear();

        controller.Player.TogglePlayCommand.Execute(null);

        Assert.False(controller.Player.IsPlaying);
        Assert.Same(controller.Player.Tracks[0], controller.Player.SelectedTrack);
        Assert.Equal(
            SoundscapeCatalog.Playlist.Select(track => track.RelativePath),
            engine.Selections.Select(selection => selection.Track.RelativePath));
    }

    [Fact]
    public void ManualPlayer_PreviousNextAndNaturalEndSkipUnloadableTracks()
    {
        var previousEngine = new FakeAudioEngine();
        previousEngine.UnloadablePaths.Add("Assets/Audio/Music/injury-death.mp3");
        using (var previousController = CreateController(previousEngine))
        {
            previousController.Player.PreviousCommand.Execute(null);

            Assert.Same(previousController.Player.Tracks[^2], previousController.Player.SelectedTrack);
            Assert.Equal(
                [
                    "Assets/Audio/Music/injury-death.mp3",
                    "Assets/Audio/Music/injury.mp3",
                ],
                previousEngine.Selections.TakeLast(2).Select(selection => selection.Track.RelativePath));
            Assert.False(previousController.Player.IsPlaying);
        }

        var nextEngine = new FakeAudioEngine();
        nextEngine.UnloadablePaths.Add("Assets/Audio/Music/pitwall-at-dusk.mp3");
        using (var nextController = CreateController(nextEngine))
        {
            nextController.Player.NextCommand.Execute(null);

            Assert.Same(nextController.Player.Tracks[2], nextController.Player.SelectedTrack);
            Assert.Equal(
                [
                    "Assets/Audio/Music/pitwall-at-dusk.mp3",
                    "Assets/Audio/Music/amber-pitlane.mp3",
                ],
                nextEngine.Selections.TakeLast(2).Select(selection => selection.Track.RelativePath));
            Assert.False(nextController.Player.IsPlaying);
        }

        var endedEngine = new FakeAudioEngine();
        endedEngine.UnloadablePaths.Add("Assets/Audio/Music/pitwall-at-dusk.mp3");
        using var endedController = CreateController(endedEngine);
        endedController.Player.TogglePlayCommand.Execute(null);

        endedEngine.RaiseMusicEnded();

        Assert.Same(endedController.Player.Tracks[2], endedController.Player.SelectedTrack);
        Assert.True(endedController.Player.IsPlaying);
        Assert.All(endedEngine.Selections.TakeLast(2), selection => Assert.True(selection.Play));
    }

    [Fact]
    public void ManualPlayer_VolumePercentPersistsThroughSettingsService()
    {
        var engine = new FakeAudioEngine();
        var settings = new FakeSettingsService(new AppSettings { MusicVolumePercent = 37 });
        using var controller = new AppAudioController(settings, engine);

        Assert.Equal(37, controller.Player.VolumePercent);

        controller.Player.VolumePercent = 63;

        Assert.Equal(63, settings.Current.MusicVolumePercent);
        Assert.Equal(63, controller.Player.VolumePercent);
        Assert.Equal(1, settings.UpdateCalls);
        Assert.Equal(.63, engine.Mixes[^1].Music, precision: 6);
    }

    [Fact]
    public void AppAudioTypes_HaveNoShellOrNavigationDependency()
    {
        Type[] ownedTypes = [typeof(AppAudioController), typeof(MusicPlayerViewModel)];
        foreach (Type type in ownedTypes)
        {
            IEnumerable<string> dependencies = type
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => field.FieldType.FullName ?? field.FieldType.Name)
                .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .SelectMany(ctor => ctor.GetParameters())
                    .Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name));

            Assert.DoesNotContain(dependencies, name =>
                name.Contains("Companion.ViewModels.Shell", StringComparison.Ordinal) ||
                name.Contains("Navigation", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void MusicPlayerControl_LaysOutTransportTrackAndVolumeWithTwoWayBindings()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = new MusicPlayerHost();
            var view = new MusicPlayerControl { DataContext = host };
            view.Measure(new Size(800, 56));
            view.Arrange(new Rect(0, 0, 800, 56));
            view.UpdateLayout();

            ComboBox tracks = FindByAutomationName<ComboBox>(view, "Music track");
            Button play = FindByAutomationName<Button>(view, "Play music");
            Slider volume = FindByAutomationName<Slider>(view, "Music player volume");

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
            Assert.True(tracks.ActualWidth > 0);
            Assert.True(play.ActualWidth > 0);
            Assert.True(volume.ActualWidth > 0);
            Assert.Equal(2, tracks.Items.Count);
            Assert.Same(host.Tracks[0], tracks.SelectedItem);
            Assert.Equal(40, volume.Value);
            Assert.Equal("PLAY", play.Content);
            Assert.Equal("Play music", AutomationProperties.GetName(play));

            tracks.SelectedItem = host.Tracks[1];
            volume.Value = 67;
            Assert.NotNull(play.Command);
            play.Command.Execute(play.CommandParameter);
            WpfRenderHarness.Pump();

            Assert.Same(host.Tracks[1], host.SelectedTrack);
            Assert.Equal(67, host.VolumePercent);
            Assert.True(host.IsPlaying);
            Assert.Equal("PAUSE", play.Content);
            Assert.Equal("Pause music", AutomationProperties.GetName(play));
        });
    }

    [Fact]
    public void SoundAssist_OptedInButton_RoutesTypedCueAndCanDetach()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var played = new List<SoundEffectCue>();
            SoundAssist.Connect(played.Add);
            try
            {
                var button = new Button();
                SoundAssist.SetCue(button, SoundEffectCue.Confirm);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                SoundAssist.SetCue(button, SoundEffectCue.Back);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                SoundAssist.SetCue(button, SoundEffectCue.None);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                SoundAssist.Play(SoundEffectCue.BucketPickup);

                Assert.Equal(
                    [SoundEffectCue.Confirm, SoundEffectCue.Back, SoundEffectCue.BucketPickup],
                    played);
            }
            finally
            {
                SoundAssist.Disconnect();
            }
        });
    }

    [Fact]
    public void SoundAssist_SuppressesNoOpButton_AndCarriesTheOriginatingControl()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var played = new List<(SoundEffectCue Cue, object? Source)>();
            SoundAssist.Connect((cue, source) => played.Add((cue, source)));
            try
            {
                var button = new Button();
                SoundAssist.SetCue(button, SoundEffectCue.Navigate);
                SoundAssist.SetSuppressWhen(button, true);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Empty(played);

                SoundAssist.SetSuppressWhen(button, false);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Single(played);
                Assert.Equal(SoundEffectCue.Navigate, played[0].Cue);
                Assert.Same(button, played[0].Source);
            }
            finally
            {
                SoundAssist.Disconnect();
            }
        });
    }

    [Fact]
    public void ResultEntryDragDrop_RequestsPickupAndSuccessfulPlaceCues()
    {
        string behaviorPath = Path.Combine(
            FindRepositoryRoot(), "src", "Companion.App", "Behaviors", "ListDragDropBehavior.cs");
        string source = File.ReadAllText(behaviorPath);

        Assert.Contains("SoundAssist.Play(SoundEffectCue.BucketPickup);", source, StringComparison.Ordinal);
        Assert.Contains("SoundAssist.Play(SoundEffectCue.BucketPlace);", source, StringComparison.Ordinal);
        Assert.Contains("if (moved)", source, StringComparison.Ordinal);
        Assert.Contains("bool moved = targetRole switch", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractionSfx_RejectedPlaybackDoesNotConsumeTheNextAudibleClick()
    {
        var engine = new FakeAudioEngine { PlayEffectResult = false };
        using var controller = CreateController(engine);

        controller.PlayEffect(SoundEffectCue.Navigate);
        controller.PlayEffect(SoundEffectCue.Navigate);

        Assert.Equal(2, engine.Effects.Count);

        engine.PlayEffectResult = true;
        controller.PlayEffect(SoundEffectCue.Navigate);
        controller.PlayEffect(SoundEffectCue.Navigate);

        Assert.Equal(3, engine.Effects.Count);
        Assert.Equal("Assets/Audio/Sfx/navigate.wav", engine.Effects[^1].Track.RelativePath);
    }

    [Fact]
    public void InteractionSfx_EnforcesSameCueCooldownAndNavigationCrossDedupe()
    {
        var engine = new FakeAudioEngine();
        var clock = new ManualTimeProvider();
        using var controller = new AppAudioController(
            new FakeSettingsService(new AppSettings()),
            engine,
            clock: clock);

        controller.PlayEffect(SoundEffectCue.Navigate);
        controller.PlayEffect(SoundEffectCue.Navigate);
        clock.Advance(TimeSpan.FromMilliseconds(23));
        controller.PlayEffect(SoundEffectCue.Navigate);
        Assert.Single(engine.Effects);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        controller.PlayEffect(SoundEffectCue.Navigate);
        Assert.Equal(2, engine.Effects.Count);

        controller.PlayEffect(SoundEffectCue.Confirm);
        Assert.Equal(3, engine.Effects.Count);

        clock.Advance(TimeSpan.FromMilliseconds(40));
        controller.PlayEffect(SoundEffectCue.Back);
        Assert.Equal(3, engine.Effects.Count);

        clock.Advance(TimeSpan.FromMilliseconds(5));
        controller.PlayEffect(SoundEffectCue.Back);
        Assert.Equal(4, engine.Effects.Count);
        Assert.Equal("Assets/Audio/Sfx/back.wav", engine.Effects[^1].Track.RelativePath);
    }

    [Fact]
    public void InteractionSfx_RapidDistinctButtonsDoNotSuppressEachOther()
    {
        var engine = new FakeAudioEngine();
        var clock = new ManualTimeProvider();
        using var controller = new AppAudioController(
            new FakeSettingsService(new AppSettings()),
            engine,
            clock: clock);
        var firstButton = new object();
        var secondButton = new object();

        controller.PlayEffect(SoundEffectCue.Navigate, firstButton);
        controller.PlayEffect(SoundEffectCue.Navigate, secondButton);
        controller.PlayEffect(SoundEffectCue.Navigate, firstButton);

        Assert.Equal(2, engine.Effects.Count);
    }

    [Fact]
    public void WpfAudioEngine_RejectsEffectsWhenTheEffectsBusCannotSound()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            using var engine = new WpfAudioEngine(FindRepositoryRoot());
            var warning = new SoundscapeTrack(
                "src/Companion.App/Assets/Audio/Sfx/warning.wav",
                AudioBus.Effects);

            engine.ApplyMix(new AudioMixSettings(true, 0, 1, 1, false));
            Assert.False(engine.PlayEffect(warning, .8, TimeSpan.FromMilliseconds(500)));

            engine.ApplyMix(new AudioMixSettings(true, 1, 0, 1, false));
            Assert.False(engine.PlayEffect(warning, .8, TimeSpan.FromMilliseconds(500)));

            engine.ApplyMix(new AudioMixSettings(false, 1, 1, 1, false));
            Assert.False(engine.PlayEffect(warning, .8, TimeSpan.FromMilliseconds(500)));

            engine.ApplyMix(new AudioMixSettings(true, 1, 1, 1, true));
            engine.SetApplicationActive(false);
            Assert.False(engine.PlayEffect(warning, .8, TimeSpan.FromMilliseconds(500)));
        });
    }

    [Fact]
    public void WpfAudioEngine_DefersDuckUntilMediaOpensAndFocusMuteClearsThePendingVoice()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            using var engine = new WpfAudioEngine(FindRepositoryRoot());
            engine.ApplyMix(new AudioMixSettings(true, 1, 1, 1, true));
            var warning = new SoundscapeTrack(
                "src/Companion.App/Assets/Audio/Sfx/warning.wav",
                AudioBus.Effects);

            Assert.True(engine.PlayEffect(warning, .8, TimeSpan.FromMilliseconds(500)));
            Assert.Equal(1, engine.ActiveEffectCount);
            Assert.Equal(1, engine.CurrentMusicDuck);
            Assert.False(engine.IsDuckActive);

            engine.SetApplicationActive(false);

            Assert.Equal(0, engine.ActiveEffectCount);
            Assert.Equal(1, engine.CurrentMusicDuck);
            Assert.False(engine.IsDuckActive);
        });
    }

    [Fact]
    public void WpfAudioEngine_DuckMathPreservesHoldsAndUsesTheFullReleaseDuration()
    {
        TimeSpan destructiveHold = TimeSpan.FromMilliseconds(700);

        Assert.Equal(
            destructiveHold,
            WpfAudioEngine.ExtendDuckHold(
                destructiveHold,
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(500)));
        Assert.Equal(
            TimeSpan.FromMilliseconds(800),
            WpfAudioEngine.ExtendDuckHold(
                destructiveHold,
                TimeSpan.FromMilliseconds(300),
                TimeSpan.FromMilliseconds(500)));

        Assert.Equal(.68, WpfAudioEngine.InterpolateDuckRelease(.68, TimeSpan.Zero), precision: 12);
        Assert.Equal(.84, WpfAudioEngine.InterpolateDuckRelease(
            .68, TimeSpan.FromMilliseconds(130)), precision: 12);
        Assert.Equal(1, WpfAudioEngine.InterpolateDuckRelease(
            .68, TimeSpan.FromMilliseconds(260)), precision: 12);
        Assert.Equal(1, WpfAudioEngine.InterpolateDuckRelease(
            .68, TimeSpan.FromMilliseconds(400)), precision: 12);
    }

    [Fact]
    public void XamlMenuSfx_MatchesTheAuditedOptInCueMap()
    {
        var expected = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["MainWindow.xaml"] = ["Navigate"],
            ["BriefingCompactWindow.xaml"] = ["Back"],
            ["BriefingView.xaml"] =
                ["Navigate", "Warning", "Confirm", "Confirm", "Back", "Destructive", "Back"],
            ["ConfirmView.xaml"] = ["Back", "Confirm"],
            ["DossierView.xaml"] =
                ["Confirm", "Back", "SkillUnlock", "SkillUnlock", "Destructive", "Back", "Destructive", "SkillUnlock", "Destructive"],
            ["HistoryView.xaml"] = ["Navigate", "Navigate", "Back"],
            ["HubView.xaml"] =
                ["Navigate", "Navigate", "Navigate", "Back", "Navigate", "Navigate", "Back"],
            ["NewsView.xaml"] = ["Navigate", "Navigate", "Navigate", "Navigate", "Back", "Navigate"],
            ["NewsWindow.xaml"] = ["Back"],
            ["PaddockView.xaml"] = ["Navigate", "Navigate", "Navigate", "Navigate"],
            ["PhotoWindow.xaml"] = ["Back"],
            ["PromotionView.xaml"] = ["Back", "Confirm"],
            ["RenameCareerDialog.xaml"] = ["Back", "Confirm", "Back"],
            ["ResultEntryView.xaml"] = ["Confirm"],
            ["RivalScreenView.xaml"] = ["Back", "Confirm", "Confirm"],
            ["SaveLabelDialog.xaml"] = ["Back", "Confirm", "Back"],
            ["SaveManagerWindow.xaml"] = ["Back", "Warning", "Destructive", "Navigate"],
            ["SeasonReviewView.xaml"] =
                ["Confirm", "Confirm", "Confirm", "Confirm", "Warning"],
            ["SessionIntroView.xaml"] = ["Confirm"],
            ["SettingsView.xaml"] =
                ["Warning", "Back", "Confirm", "Navigate", "Navigate", "Navigate", "Navigate", "Navigate"],
            ["SkinsView.xaml"] = ["Confirm", "Destructive", "Confirm"],
            ["SmgpCareerOverView.xaml"] = ["Back"],
            ["SmgpFinaleView.xaml"] = ["Confirm"],
            ["StandingsView.xaml"] = ["Back"],
            ["StartingGridView.xaml"] = ["Confirm"],
            ["StartView.xaml"] =
                [
                    "Navigate", "Navigate", "Navigate", "Navigate", "Navigate", "Navigate",
                    "Back", "Back", "Navigate", "Navigate", "Navigate", "Navigate",
                ],
            ["TabWindow.xaml"] = ["Back"],
            ["WizardView.xaml"] = ["Back", "Back", "Navigate", "Navigate", "Navigate"],
        };

        string appRoot = Path.Combine(FindRepositoryRoot(), "src", "Companion.App");
        var actual = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                         StringComparison.OrdinalIgnoreCase) &&
                                    !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                        StringComparison.OrdinalIgnoreCase)))
        {
            XDocument document = XDocument.Load(path);
            XAttribute[] attachedCues = document
                .Descendants()
                .SelectMany(static element => element.Attributes())
                .Where(static attribute => attribute.Name.LocalName == "SoundAssist.Cue")
                .ToArray();

            Assert.All(attachedCues, attribute =>
                Assert.Equal("Button", attribute.Parent?.Name.LocalName));

            string[] cues = document
                .Descendants()
                .Where(static element => element.Name.LocalName == "Button")
                .SelectMany(static button => button.Attributes())
                .Where(static attribute => attribute.Name.LocalName == "SoundAssist.Cue")
                .Select(static attribute => attribute.Value)
                .ToArray();

            if (cues.Length > 0)
                actual.Add(Path.GetFileName(path), cues);
        }

        Assert.Equal(
            expected.Keys.Order(StringComparer.OrdinalIgnoreCase),
            actual.Keys.Order(StringComparer.OrdinalIgnoreCase));
        foreach ((string fileName, string[] cues) in expected)
            Assert.Equal(cues, actual[fileName]);

        // The wizard's primary button deliberately changes Navigate -> Confirm only when its
        // visual state changes from Next to Create; these style setters are not direct attributes.
        XDocument wizard = XDocument.Load(Path.Combine(appRoot, "Views", "WizardView.xaml"));
        string[] wizardDynamicCues = wizard
            .Descendants()
            .Where(static element => element.Name.LocalName == "Setter" &&
                element.Attribute("Property")?.Value == "audio:SoundAssist.Cue")
            .Select(static element => element.Attribute("Value")?.Value)
            .OfType<string>()
            .ToArray();
        Assert.Equal(["Navigate", "Confirm"], wizardDynamicCues);

        Assert.DoesNotContain("MusicPlayerControl.xaml", actual.Keys);
        Assert.DoesNotContain("SitOutView.xaml", actual.Keys);
        Assert.DoesNotContain("DeathScreenView.xaml", actual.Keys);
    }

    private static AppAudioController CreateController(FakeAudioEngine engine) =>
        new(new FakeSettingsService(new AppSettings()), engine);

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Companion.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not find Companion.slnx above '{AppContext.BaseDirectory}'.");
    }

    private static T FindByAutomationName<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        if (root is T match && AutomationProperties.GetName(match) == name)
            return match;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            try
            {
                return FindByAutomationName<T>(child, name);
            }
            catch (InvalidOperationException)
            {
                // Keep walking sibling branches until the named control is found.
            }
        }

        throw new InvalidOperationException($"Could not find {typeof(T).Name} named '{name}'.");
    }

    private sealed class FakeAudioEngine : ISoundscapeAudioEngine
    {
        public event EventHandler? MusicEnded;
        public event EventHandler? MusicFailed;

        internal List<AudioMixSettings> Mixes { get; } = [];
        internal List<bool> ApplicationActiveChanges { get; } = [];
        internal List<(SoundscapeTrack Track, bool Play)> Selections { get; } = [];
        internal int SelectMusicCalls { get; private set; }
        internal int PlayMusicCalls { get; private set; }
        internal int PauseMusicCalls { get; private set; }
        internal bool PlayMusicResult { get; set; } = true;
        internal bool SelectMusicResult { get; set; } = true;
        internal bool PlayEffectResult { get; set; } = true;
        internal HashSet<string> UnloadablePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal List<(SoundscapeTrack Track, double MusicDuck, TimeSpan? DuckDuration)> Effects { get; } = [];

        public void ApplyMix(AudioMixSettings settings) => Mixes.Add(settings);

        public void SetApplicationActive(bool active) => ApplicationActiveChanges.Add(active);

        public bool SelectMusic(SoundscapeTrack track, bool play)
        {
            SelectMusicCalls++;
            Selections.Add((track, play));
            return SelectMusicResult && !UnloadablePaths.Contains(track.RelativePath);
        }

        public bool PlayMusic()
        {
            PlayMusicCalls++;
            return PlayMusicResult;
        }

        public void PauseMusic() => PauseMusicCalls++;

        public bool PlayEffect(
            SoundscapeTrack track,
            double musicDuck = 1.0,
            TimeSpan? duckDuration = null)
        {
            Effects.Add((track, musicDuck, duckDuration));
            return PlayEffectResult;
        }

        internal void RaiseMusicEnded() => MusicEnded?.Invoke(this, EventArgs.Empty);

        internal void RaiseMusicFailed() => MusicFailed?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
        }
    }

    private sealed class FakeSettingsService(AppSettings initial) : ISettingsService
    {
        public AppSettings Current { get; private set; } = initial;

        public event EventHandler<AppSettings>? Changed;

        internal int UpdateCalls { get; private set; }

        public void Update(Func<AppSettings, AppSettings> mutate)
        {
            Current = mutate(Current).Normalized();
            UpdateCalls++;
            Changed?.Invoke(this, Current);
        }

        public void Reset() => Update(static _ => new AppSettings());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }

    private sealed class MusicPlayerHost : INotifyPropertyChanged
    {
        private MusicTrackHost _selectedTrack;
        private bool _isPlaying;
        private int _volumePercent = 40;

        internal MusicPlayerHost()
        {
            Tracks = [new("Track One"), new("Track Two")];
            _selectedTrack = Tracks[0];
            TogglePlayCommand = new TestCommand(() => IsPlaying = !IsPlaying);
            PreviousCommand = new TestCommand(() => { });
            NextCommand = new TestCommand(() => { });
        }

        public IReadOnlyList<MusicTrackHost> Tracks { get; }

        public MusicTrackHost SelectedTrack
        {
            get => _selectedTrack;
            set
            {
                if (ReferenceEquals(_selectedTrack, value))
                    return;
                _selectedTrack = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTrack)));
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (_isPlaying == value)
                    return;
                _isPlaying = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
            }
        }

        public int VolumePercent
        {
            get => _volumePercent;
            set
            {
                if (_volumePercent == value)
                    return;
                _volumePercent = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VolumePercent)));
            }
        }

        public ICommand TogglePlayCommand { get; }

        public ICommand PreviousCommand { get; }

        public ICommand NextCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed record MusicTrackHost(string Title);

    private sealed class TestCommand(Action execute) : ICommand
    {
#pragma warning disable CS0067 // Required by ICommand; these fixed test commands never disable.
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }

    private sealed class AudioSettingsHost
    {
        public int MinFontScalePercent { get; } = 90;
        public int MaxFontScalePercent { get; } = 160;
        public int FontScalePercent { get; set; } = 100;
        public bool SoundEnabled { get; set; } = true;
        public int MasterVolumePercent { get; set; } = 80;
        public int EffectsVolumePercent { get; set; } = 70;
        public int AmbienceVolumePercent { get; set; } = 35;
        public int MusicVolumePercent { get; set; } = 40;
        public bool MuteWhenUnfocused { get; set; } = true;
    }
}
