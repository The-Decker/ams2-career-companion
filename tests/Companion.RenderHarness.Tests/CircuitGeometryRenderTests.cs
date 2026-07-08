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
