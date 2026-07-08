using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>The shared user-image primitive behind the gallery era art, track thumbnails and future
/// story art: a most-specific-first candidate probe with a clean null fallback. Pure ordering + a
/// File.Exists probe, so it unit-tests with plain temp files (no WPF).</summary>
public sealed class UserImageResolverTests
{
    [Fact]
    public void CandidatesForKey_orders_jpg_jpeg_png()
    {
        Assert.Equal(
            ["silverstone.jpg", "silverstone.jpeg", "silverstone.png"],
            UserImageResolver.CandidatesForKey("silverstone"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CandidatesForKey_is_empty_for_a_blank_key(string? key) =>
        Assert.Empty(UserImageResolver.CandidatesForKey(key));

    [Fact]
    public void FirstExisting_returns_null_when_the_directory_is_blank_or_missing()
    {
        Assert.Null(UserImageResolver.FirstExisting(null, ["a.jpg"]));
        Assert.Null(UserImageResolver.FirstExisting("", ["a.jpg"]));
        string missing = Path.Combine(Path.GetTempPath(), "uir-" + Guid.NewGuid().ToString("N"));
        Assert.Null(UserImageResolver.FirstExisting(missing, ["a.jpg"]));
    }

    [Fact]
    public void FirstExisting_returns_the_first_candidate_that_exists_in_order()
    {
        using var dir = new TempDir();
        // Only the second candidate exists → it wins even though the first was tried first.
        string png = Path.Combine(dir.Path, "monza.png");
        File.WriteAllBytes(png, [0x89, 0x50]);

        Assert.Equal(png, UserImageResolver.FirstExisting(dir.Path, ["monza.jpg", "monza.png"]));
    }

    [Fact]
    public void FirstExisting_prefers_the_earlier_candidate_when_several_exist()
    {
        using var dir = new TempDir();
        string jpg = Path.Combine(dir.Path, "spa.jpg");
        File.WriteAllBytes(jpg, [0xFF, 0xD8]);
        File.WriteAllBytes(Path.Combine(dir.Path, "spa.png"), [0x89, 0x50]);

        Assert.Equal(jpg, UserImageResolver.FirstExisting(dir.Path, ["spa.jpg", "spa.png"]));
    }

    [Fact]
    public void ResolveByKey_finds_a_keyed_asset_and_is_null_when_absent()
    {
        using var dir = new TempDir();
        string jpg = Path.Combine(dir.Path, "interlagos.jpg");
        File.WriteAllBytes(jpg, [0xFF, 0xD8]);

        Assert.Equal(jpg, UserImageResolver.ResolveByKey(dir.Path, "interlagos"));
        Assert.Null(UserImageResolver.ResolveByKey(dir.Path, "nurburgring"));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uir-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
