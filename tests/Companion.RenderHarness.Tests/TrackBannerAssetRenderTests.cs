using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Companion.App.Converters;

namespace Companion.RenderHarness.Tests;

/// <summary>Guards the embedded panoramic Calendar art for every id in the AMS2 track library.</summary>
public sealed class TrackBannerAssetRenderTests
{
    [Fact]
    public void TrackBannerManifest_CoversEveryAms2Track_WithExactPanoramicMasters()
    {
        string root = FindRepositoryRoot();
        string assets = Path.Combine(root, "src", "Companion.App", "Assets", "TrackBanners");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "manifest.json")));
        using var library = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "ams2", "tracks.json")));

        Assert.Equal(1920, manifest.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(440, manifest.RootElement.GetProperty("height").GetInt32());
        var mapped = manifest.RootElement.GetProperty("tracks").EnumerateObject()
            .ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!, StringComparer.OrdinalIgnoreCase);
        var expected = library.RootElement.GetProperty("tracks").EnumerateArray()
            .Select(track => track.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expected.Count, mapped.Count);
        Assert.Empty(expected.Except(mapped.Keys, StringComparer.OrdinalIgnoreCase));
        foreach (string relativePath in mapped.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.Combine(assets, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Manifest references a missing master: {relativePath}");
            using var stream = File.OpenRead(path);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(1920, decoder.Frames[0].PixelWidth);
            Assert.Equal(440, decoder.Frames[0].PixelHeight);
        }
    }

    [Fact]
    public void TrackBannerConverter_LoadsEmbeddedMaster_AndFailsSafeForUnknownId()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var converter = new TrackBannerImageConverter();
            var source = Assert.IsAssignableFrom<BitmapSource>(converter.Convert(
                "imola_88", typeof(ImageSource), null, CultureInfo.InvariantCulture));
            Assert.True(source.IsFrozen);
            Assert.Equal(1920, source.PixelWidth);
            Assert.Equal(440, source.PixelHeight);
            Assert.Null(converter.Convert(
                "not-a-real-track-id", typeof(ImageSource), null, CultureInfo.InvariantCulture));
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
        throw new DirectoryNotFoundException($"Could not find Companion.slnx above '{AppContext.BaseDirectory}'.");
    }
}
