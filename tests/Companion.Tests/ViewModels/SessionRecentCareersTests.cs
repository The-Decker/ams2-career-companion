using Companion.ViewModels.Services;
using Companion.ViewModels.Start;

namespace Companion.Tests.ViewModels;

/// <summary>MRU behavior of the start screen: the JSON store (front-insert, dedupe,
/// capacity, pruning, corrupt-file tolerance) and the StartViewModel commands over it.</summary>
public sealed class SessionRecentCareersTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-mru-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string StoreFile => Path.Combine(_root, "recent.json");

    private RecentCareersStore Store(Func<string, bool>? exists = null) =>
        new(StoreFile, careerFileExists: exists ?? (_ => true));

    // ---------- the store ----------

    [Fact]
    public void Touch_InsertsAtTheFront()
    {
        var store = Store();
        store.Touch(@"C:\careers\a.ams2career", "A");
        store.Touch(@"C:\careers\b.ams2career", "B");

        var loaded = store.Load();
        Assert.Equal([@"C:\careers\b.ams2career", @"C:\careers\a.ams2career"], loaded.Select(c => c.Path));
        Assert.Equal("B", loaded[0].CareerName);
    }

    [Fact]
    public void Touch_ExistingPath_MovesToFrontWithoutDuplicating()
    {
        var store = Store();
        store.Touch(@"C:\a.ams2career", "A");
        store.Touch(@"C:\b.ams2career", "B");
        store.Touch(@"C:\A.AMS2CAREER", "A renamed"); // case-insensitive same path

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal(@"C:\A.AMS2CAREER", loaded[0].Path);
        Assert.Equal("A renamed", loaded[0].CareerName);
        Assert.Equal(@"C:\b.ams2career", loaded[1].Path);
    }

    [Fact]
    public void Touch_CapsTheListAtCapacity()
    {
        var store = Store();
        for (int i = 1; i <= RecentCareersStore.Capacity + 2; i++)
            store.Touch($@"C:\career-{i}.ams2career", $"Career {i}");

        var loaded = store.Load();
        Assert.Equal(RecentCareersStore.Capacity, loaded.Count);
        Assert.Equal($@"C:\career-{RecentCareersStore.Capacity + 2}.ams2career", loaded[0].Path);
        Assert.DoesNotContain(loaded, c => c.Path == @"C:\career-1.ams2career");
        Assert.DoesNotContain(loaded, c => c.Path == @"C:\career-2.ams2career");
    }

    [Fact]
    public void Load_PrunesEntriesWhoseCareerFileIsGone()
    {
        var store = Store(exists: path => path.Contains("keep"));
        store.Touch(@"C:\keep-1.ams2career", "K1");
        store.Touch(@"C:\gone.ams2career", "G");
        store.Touch(@"C:\keep-2.ams2career", "K2");

        Assert.Equal([@"C:\keep-2.ams2career", @"C:\keep-1.ams2career"], store.Load().Select(c => c.Path));
    }

    [Fact]
    public void Load_CorruptFile_ReadsAsEmptyAndRecovers()
    {
        File.WriteAllText(StoreFile, "{ this is not json");
        var store = Store();

        Assert.Empty(store.Load());

        store.Touch(@"C:\a.ams2career", "A");
        Assert.Single(store.Load());
    }

    [Fact]
    public void Remove_DropsTheEntry()
    {
        var store = Store();
        store.Touch(@"C:\a.ams2career", "A");
        store.Touch(@"C:\b.ams2career", "B");

        store.Remove(@"C:\a.ams2career");

        Assert.Equal([@"C:\b.ams2career"], store.Load().Select(c => c.Path));
    }

    // ---------- the start viewmodel ----------

    [Fact]
    public void Continue_TouchesTheMruAndRaisesContinueRequested()
    {
        var store = Store();
        store.Touch(@"C:\old.ams2career", "Old");
        store.Touch(@"C:\new.ams2career", "New");

        var vm = new StartViewModel(store);
        Assert.Equal(2, vm.RecentCareers.Count);

        string? requested = null;
        vm.ContinueRequested += (_, path) => requested = path;

        var older = vm.RecentCareers[1];
        vm.ContinueCommand.Execute(older);

        Assert.Equal(@"C:\old.ams2career", requested);
        // Continuing moved it to the front of the MRU.
        Assert.Equal(@"C:\old.ams2career", vm.RecentCareers[0].Path);
    }

    [Fact]
    public void NewCareer_RaisesNewCareerRequested()
    {
        var vm = new StartViewModel(Store());
        bool raised = false;
        vm.NewCareerRequested += (_, _) => raised = true;

        vm.NewCareerCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void RemoveRecent_RemovesFromStoreAndList()
    {
        var store = Store();
        store.Touch(@"C:\a.ams2career", "A");
        var vm = new StartViewModel(store);

        vm.RemoveRecentCommand.Execute(vm.RecentCareers[0]);

        Assert.Empty(vm.RecentCareers);
        Assert.False(vm.HasRecentCareers);
        Assert.Empty(store.Load());
    }
}
