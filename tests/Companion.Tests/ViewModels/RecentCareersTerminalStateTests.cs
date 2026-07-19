using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The MRU store's terminal-state badge contract (commit 8a0427c): the badge-aware Touch overload
/// records a finished career's terminal state ("deceased" → IN MEMORIAM, "careerOver" → CAREER
/// OVER), a later plain Touch carries the stored state forward (a Continue must never un-badge a
/// memorial card), and a live career reads no badge at all.
/// </summary>
public sealed class RecentCareersTerminalStateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-terminal-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private RecentCareersStore Store() =>
        new(Path.Combine(_root, "recent.json"), careerFileExists: _ => true);

    [Fact]
    public void Touch_WithDeceased_StoresTheStateAndBadgesInMemoriam()
    {
        var store = Store();

        store.Touch(@"C:\careers\ayrton.ams2career", "Ayrton", 1990, "smgp", "deceased");

        var entry = Assert.Single(store.Load());
        Assert.Equal("deceased", entry.TerminalState);
        Assert.True(entry.IsTerminal);
        Assert.Equal("IN MEMORIAM", entry.TerminalBadge);
    }

    [Fact]
    public void PlainTouch_CarriesTheStoredTerminalStateForward()
    {
        var store = Store();
        store.Touch(@"C:\careers\ayrton.ams2career", "Ayrton", 1990, "smgp", "deceased");

        // Re-opening the archive is a plain 4-arg Touch, it must not wipe the memorial badge.
        store.Touch(@"C:\careers\ayrton.ams2career", "Ayrton", 1990, "smgp");

        var entry = Assert.Single(store.Load());
        Assert.Equal("deceased", entry.TerminalState);
        Assert.True(entry.IsTerminal);
        Assert.Equal("IN MEMORIAM", entry.TerminalBadge);
    }

    [Fact]
    public void Touch_WithCareerOver_BadgesCareerOver()
    {
        var store = Store();

        store.Touch(@"C:\careers\floor.ams2career", "Knocked Out", 1990, "smgp", "careerOver");

        var entry = Assert.Single(store.Load());
        Assert.Equal("careerOver", entry.TerminalState);
        Assert.True(entry.IsTerminal);
        Assert.Equal("CAREER OVER", entry.TerminalBadge);
    }

    [Fact]
    public void Touch_NullStateOnAFreshEntry_ReadsAsALiveCareer()
    {
        var store = Store();

        store.Touch(@"C:\careers\alive.ams2career", "Alive", 1967, null, null);

        var entry = Assert.Single(store.Load());
        Assert.Null(entry.TerminalState);
        Assert.False(entry.IsTerminal);
        Assert.Equal("", entry.TerminalBadge);
    }
}
