using System.IO;
using System.Windows.Media;
using Companion.App.Converters;

namespace Companion.RenderHarness.Tests;

/// <summary>Proves the circuit-map pipeline end-to-end: every shipped data/ams2/circuits/*.json (path
/// data emitted by tools/derive_circuits.cs, normalized from f1db SVGs) parses with WPF's
/// <see cref="Geometry.Parse"/> into a non-empty geometry. This is the load-bearing check that the
/// SVG→WPF number-normalization is correct — if WPF rejected the path syntax, the maps would silently
/// fail to render. Runs on an STA thread (WPF). Self-skips off Windows / when the data dir is absent.</summary>
public sealed class CircuitGeometryRenderTests
{
    [Fact]
    public void EveryShippedCircuit_ParsesIntoANonEmptyGeometry()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        string? circuitsDir = FindCircuitsDirectory();
        if (circuitsDir is null)
            return; // data dir not locatable from the test bin — skip rather than false-fail

        var files = Directory.GetFiles(circuitsDir, "*.json");
        Assert.True(files.Length >= 100, $"expected the full shipped circuit set, found {files.Length}");

        WpfRenderHarness.RunSta(() =>
        {
            int rendered = 0;
            foreach (var file in files)
            {
                string layoutId = Path.GetFileNameWithoutExtension(file);
                var geometry = CircuitGeometryConverter.LoadFrom(circuitsDir, layoutId);
                Assert.True(geometry is not null, $"{layoutId}: LoadFrom returned null (parse failed)");
                Assert.False(geometry!.Bounds.IsEmpty, $"{layoutId}: geometry has empty bounds");
                rendered++;
            }
            Assert.Equal(files.Length, rendered);
        });
    }

    [Fact]
    public void LoadFrom_PreservesTheAuthoredCircuitOrientation()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        string directory = Path.Combine(Path.GetTempPath(), $"ams2-circuit-orientation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "authored-vertical.json"),
                """{"paths":["M 0,0 L 60,0 L 60,200 L 0,200 Z"]}""");

            WpfRenderHarness.RunSta(() =>
            {
                var geometry = CircuitGeometryConverter.LoadFrom(directory, "authored-vertical");
                Assert.NotNull(geometry);
                Assert.Equal(60, geometry!.Bounds.Width, precision: 6);
                Assert.Equal(200, geometry.Bounds.Height, precision: 6);
                Assert.True(geometry.Bounds.Height > geometry.Bounds.Width * 3,
                    $"expected the supplied vertical orientation, got {geometry.Bounds.Width:0}x{geometry.Bounds.Height:0}");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadFrom_IsNullForAMissingLayout()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        string? circuitsDir = FindCircuitsDirectory();
        if (circuitsDir is null)
            return;

        WpfRenderHarness.RunSta(() =>
            Assert.Null(CircuitGeometryConverter.LoadFrom(circuitsDir, "no-such-circuit-999")));
    }

    /// <summary>Walks up from the test bin to the repo root (the folder holding Companion.slnx) and
    /// returns its data/ams2/circuits, or null when it can't be found.</summary>
    private static string? FindCircuitsDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Companion.slnx")))
            {
                string circuits = Path.Combine(dir.FullName, "data", "ams2", "circuits");
                return Directory.Exists(circuits) ? circuits : null;
            }
        }
        return null;
    }
}
