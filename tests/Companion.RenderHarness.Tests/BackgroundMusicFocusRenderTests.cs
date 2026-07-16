using System.IO;
using Companion.App.Audio;

namespace Companion.RenderHarness.Tests;

public sealed class BackgroundMusicFocusRenderTests
{
    [Fact]
    public void WpfAudioEngine_MusicContinuesWhileInactiveAndManualPauseStillWins()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            string appRoot = Path.Combine(FindRepositoryRoot(), "src", "Companion.App");
            using var engine = new WpfAudioEngine(appRoot);
            SoundscapeTrack track = SoundscapeCatalog.MusicTrack(SoundscapeCatalog.Playlist[0]);

            engine.ApplyMix(new AudioMixSettings(true, 1, 1, 1, true));
            Assert.True(engine.SelectMusic(track, play: true));
            Assert.True(engine.ShouldRunMusicTransport);

            engine.SetApplicationActive(false);
            Assert.True(engine.ShouldRunMusicTransport);

            engine.PauseMusic();
            Assert.False(engine.ShouldRunMusicTransport);
            engine.SetApplicationActive(true);
            Assert.False(engine.ShouldRunMusicTransport);

            Assert.True(engine.PlayMusic());
            engine.ApplyMix(new AudioMixSettings(false, 1, 1, 1, true));
            Assert.False(engine.ShouldRunMusicTransport);

            engine.SetApplicationActive(false);
            engine.ApplyMix(new AudioMixSettings(true, 1, 1, 1, true));
            Assert.True(engine.ShouldRunMusicTransport);
        });
    }

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
}
