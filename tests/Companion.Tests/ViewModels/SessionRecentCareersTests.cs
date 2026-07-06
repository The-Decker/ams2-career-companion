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

    // ---------- stored season year (Part A) ----------

    [Fact]
    public void Touch_PersistsTheSeasonYear()
    {
        var store = Store();
        store.Touch(@"C:\a.ams2career", "A", seasonYear: 1967);

        var loaded = store.Load();
        Assert.Equal(1967, loaded[0].SeasonYear);
    }

    [Fact]
    public void Touch_DefaultsTheSeasonYearToZeroWhenUnknown()
    {
        var store = Store();
        store.Touch(@"C:\a.ams2career", "A"); // no year supplied

        Assert.Equal(0, store.Load()[0].SeasonYear);
    }

    [Fact]
    public void Load_LegacyEntryWithoutSeasonYear_ReadsAsZero()
    {
        // A recent.json written before SeasonYear existed omits the property entirely — it must
        // still load (read-with-default 0), never throw, so old MRU files keep working.
        File.WriteAllText(StoreFile, """
            {
              "careers": [
                { "path": "C:\\legacy.ams2career", "careerName": "Legacy 1988", "lastOpenedUtc": "2024-01-01T00:00:00+00:00" }
              ]
            }
            """);
        var store = Store();

        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.Equal(@"C:\legacy.ams2career", loaded[0].Path);
        Assert.Equal(0, loaded[0].SeasonYear);
        // The gallery still resolves this legacy card's era by parsing the name.
        Assert.Equal(1988, EraArtResolver.YearForEntry(loaded[0]));
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
    public void Continue_PreservesTheStoredSeasonYearWhenReTouching()
    {
        var store = Store();
        store.Touch(@"C:\a.ams2career", "A", seasonYear: 1974);
        var vm = new StartViewModel(store);

        vm.ContinueCommand.Execute(vm.RecentCareers[0]);

        // Re-touching via Continue keeps the era-art year (the shell re-records it authoritatively
        // once the session opens, but the MRU must not lose it in the meantime).
        Assert.Equal(1974, store.Load()[0].SeasonYear);
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

    // ---------- "Open career…" by path (Part B) ----------

    [Fact]
    public void OpenCareer_ValidPath_RaisesContinueRequestedAndClearsError()
    {
        var vm = new StartViewModel(Store(), careerFileExists: _ => true);
        string? requested = null;
        vm.ContinueRequested += (_, path) => requested = path;

        vm.OpenCareerCommand.Execute(@"C:\somewhere\old.ams2career");

        Assert.Equal(@"C:\somewhere\old.ams2career", requested);
        Assert.Null(vm.OpenError);
    }

    [Fact]
    public void OpenCareer_MissingFile_SetsErrorAndDoesNotRaise()
    {
        var vm = new StartViewModel(Store(), careerFileExists: _ => false);
        bool raised = false;
        vm.ContinueRequested += (_, _) => raised = true;

        vm.OpenCareerCommand.Execute(@"C:\gone\missing.ams2career");

        Assert.False(raised);
        Assert.NotNull(vm.OpenError);
        Assert.Contains("missing.ams2career", vm.OpenError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void OpenCareer_BlankPath_SetsErrorAndDoesNotRaise(string? path)
    {
        // careerFileExists must never be consulted for a blank path — the guard short-circuits first.
        var vm = new StartViewModel(Store(), careerFileExists: _ => throw new Xunit.Sdk.XunitException(
            "existence must not be probed for a blank path"));
        bool raised = false;
        vm.ContinueRequested += (_, _) => raised = true;

        vm.OpenCareerCommand.Execute(path);

        Assert.False(raised);
        Assert.NotNull(vm.OpenError);
    }

    [Fact]
    public void OpenCareer_AfterAnError_ClearsTheErrorOnASuccessfulOpen()
    {
        bool exists = false;
        var vm = new StartViewModel(Store(), careerFileExists: _ => exists);
        vm.OpenCareerCommand.Execute(@"C:\gone.ams2career");
        Assert.NotNull(vm.OpenError);

        exists = true;
        vm.OpenCareerCommand.Execute(@"C:\now-here.ams2career");
        Assert.Null(vm.OpenError);
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

    // ---------- "Delete career file…" (delete from disk, not just the shortcut) ----------

    [Fact]
    public void DeleteRecent_DeletesTheFileAndRemovesTheEntry()
    {
        var store = Store();
        store.Touch(@"C:\a.ams2career", "A");
        string? deleted = null;
        var vm = new StartViewModel(store, deleteCareerFile: path => deleted = path);

        vm.DeleteRecentCommand.Execute(vm.RecentCareers[0]);

        Assert.Equal(@"C:\a.ams2career", deleted);
        Assert.Empty(vm.RecentCareers);
        Assert.Empty(store.Load());
        Assert.Null(vm.GalleryError);
    }

    [Fact]
    public void DeleteRecent_UndeletableFile_ReportsAndKeepsTheEntry()
    {
        // A locked file (the career is open) or a permission failure: the career still exists on
        // disk, so the gallery entry must survive and the banner must say why.
        var store = Store();
        store.Touch(@"C:\a.ams2career", "Monaco 1967");
        var vm = new StartViewModel(store, deleteCareerFile: _ =>
            throw new IOException("locked by another process"));

        vm.DeleteRecentCommand.Execute(vm.RecentCareers[0]);

        Assert.Single(vm.RecentCareers);
        Assert.Single(store.Load());
        Assert.NotNull(vm.GalleryError);
        Assert.Contains("Monaco 1967", vm.GalleryError);
        Assert.Contains("locked by another process", vm.GalleryError);
    }

    [Fact]
    public void DeleteRecent_AfterAnError_ClearsTheBannerOnASuccessfulDelete()
    {
        var store = Store();
        store.Touch(@"C:\a.ams2career", "A");
        store.Touch(@"C:\b.ams2career", "B");
        bool locked = true;
        var vm = new StartViewModel(store, deleteCareerFile: _ =>
        {
            if (locked)
                throw new IOException("in use");
        });

        vm.DeleteRecentCommand.Execute(vm.RecentCareers[0]);
        Assert.NotNull(vm.GalleryError);

        locked = false;
        vm.DeleteRecentCommand.Execute(vm.RecentCareers[0]);
        Assert.Null(vm.GalleryError);
        Assert.Single(vm.RecentCareers);
    }

    [Fact]
    public void DeleteRecent_DefaultDelete_RemovesTheFileAndSqliteSidecars()
    {
        // The real default delegate: the .ams2career goes, and any crash-leftover -wal/-shm
        // sidecars go with it (best-effort).
        string career = Path.Combine(_root, "delete-me.ams2career");
        File.WriteAllText(career, "career-bytes");
        File.WriteAllText(career + "-wal", "wal");
        File.WriteAllText(career + "-shm", "shm");
        var store = Store(exists: File.Exists);
        store.Touch(career, "Delete me");
        var vm = new StartViewModel(store);

        vm.DeleteRecentCommand.Execute(vm.RecentCareers[0]);

        Assert.False(File.Exists(career));
        Assert.False(File.Exists(career + "-wal"));
        Assert.False(File.Exists(career + "-shm"));
        Assert.Empty(vm.RecentCareers);
        Assert.Null(vm.GalleryError);
    }

    [Fact]
    public void GalleryError_ClearsWhenTheUserMovesOn()
    {
        // A failed delete's banner must not outlive the entry it refers to: falling back to
        // "Remove from this list" (or continuing/opening another career) clears it, mirroring
        // OpenError's lifecycle.
        var store = Store();
        store.Touch(@"C:\a.ams2career", "A");
        store.Touch(@"C:\b.ams2career", "B");
        var vm = new StartViewModel(store,
            careerFileExists: _ => true,
            deleteCareerFile: _ => throw new IOException("in use"));

        vm.DeleteRecentCommand.Execute(vm.RecentCareers[0]);
        Assert.NotNull(vm.GalleryError);
        vm.RemoveRecentCommand.Execute(vm.RecentCareers[0]);
        Assert.Null(vm.GalleryError);

        vm.DeleteRecentCommand.Execute(vm.RecentCareers[0]);
        Assert.NotNull(vm.GalleryError);
        vm.ContinueCommand.Execute(vm.RecentCareers[0]);
        Assert.Null(vm.GalleryError);

        vm.DeleteRecentCommand.Execute(vm.RecentCareers[0]);
        Assert.NotNull(vm.GalleryError);
        vm.OpenCareerCommand.Execute(@"C:\elsewhere.ams2career");
        Assert.Null(vm.GalleryError);
    }

    [Fact]
    public void DeleteRecent_FileAlreadyGone_StillDropsTheEntry()
    {
        // Deleted outside the app between MRU load and the command: nothing to delete is not an
        // error — the user asked for it gone, and it is.
        string career = Path.Combine(_root, "already-gone.ams2career");
        var store = Store(exists: _ => true); // keep the entry visible despite the missing file
        store.Touch(career, "Ghost");
        var vm = new StartViewModel(store);

        vm.DeleteRecentCommand.Execute(vm.RecentCareers[0]);

        Assert.Empty(vm.RecentCareers);
        Assert.Empty(store.Load());
        Assert.Null(vm.GalleryError);
    }

    // ---------- "Rename career…" ----------

    [Fact]
    public void RenameRecent_RenamesTheFileAndUpdatesTheMru()
    {
        var store = Store();
        store.Touch(@"C:\careers\Monaco.ams2career", "Monaco", seasonYear: 1967);
        (string From, string To, string Name)? renamed = null;
        var vm = new StartViewModel(store,
            careerFileExists: path => path == @"C:\careers\Monaco.ams2career",
            renameCareerFile: (from, to, name) => renamed = (from, to, name));

        vm.RenameRecent(vm.RecentCareers[0], "Glory Years");

        Assert.Equal(
            (@"C:\careers\Monaco.ams2career", @"C:\careers\Glory Years.ams2career", "Glory Years"),
            renamed);
        var entry = Assert.Single(store.Load());
        Assert.Equal(@"C:\careers\Glory Years.ams2career", entry.Path);
        Assert.Equal("Glory Years", entry.CareerName);
        Assert.Equal(1967, entry.SeasonYear); // era art survives the rename
        Assert.Null(vm.GalleryError);
    }

    [Fact]
    public void RenameRecent_SanitizesTheFileNameButKeepsTheDisplayName()
    {
        var store = Store();
        store.Touch(@"C:\careers\a.ams2career", "A");
        (string To, string Name)? renamed = null;
        var vm = new StartViewModel(store,
            careerFileExists: path => path == @"C:\careers\a.ams2career",
            renameCareerFile: (_, to, name) => renamed = (to, name));

        vm.RenameRecent(vm.RecentCareers[0], "What if: 1967?");

        Assert.Equal((@"C:\careers\What if_ 1967_.ams2career", "What if: 1967?"), renamed);
        Assert.Equal("What if: 1967?", store.Load()[0].CareerName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RenameRecent_BlankName_SetsErrorAndChangesNothing(string? newName)
    {
        var store = Store();
        store.Touch(@"C:\a.ams2career", "A");
        var vm = new StartViewModel(store, renameCareerFile: (_, _, _) =>
            throw new Xunit.Sdk.XunitException("must not rename on a blank name"));

        vm.RenameRecent(vm.RecentCareers[0], newName);

        Assert.NotNull(vm.GalleryError);
        Assert.Equal("A", store.Load()[0].CareerName);
    }

    [Fact]
    public void RenameRecent_TargetFileExists_SetsErrorAndChangesNothing()
    {
        var store = Store();
        store.Touch(@"C:\careers\a.ams2career", "A");
        var vm = new StartViewModel(store,
            careerFileExists: _ => true, // every candidate path "exists" → guaranteed collision
            renameCareerFile: (_, _, _) =>
                throw new Xunit.Sdk.XunitException("must not rename onto an existing file"));

        vm.RenameRecent(vm.RecentCareers[0], "B");

        Assert.NotNull(vm.GalleryError);
        Assert.Contains("already exists", vm.GalleryError);
        Assert.Equal("A", store.Load()[0].CareerName);
    }

    [Fact]
    public void RenameRecent_LockedFile_ReportsAndKeepsTheEntry()
    {
        var store = Store();
        store.Touch(@"C:\careers\a.ams2career", "Monaco 1967");
        var vm = new StartViewModel(store,
            careerFileExists: path => path == @"C:\careers\a.ams2career",
            renameCareerFile: (_, _, _) => throw new IOException("locked"));

        vm.RenameRecent(vm.RecentCareers[0], "B");

        Assert.NotNull(vm.GalleryError);
        Assert.Contains("Monaco 1967", vm.GalleryError);
        var entry = Assert.Single(store.Load());
        Assert.Equal(@"C:\careers\a.ams2career", entry.Path);
    }

    // ---------- "Duplicate career" ----------

    [Fact]
    public void DuplicateRecent_CopiesBesideTheOriginalWithACopySuffix()
    {
        var store = Store();
        store.Touch(@"C:\careers\Monaco.ams2career", "Monaco", seasonYear: 1967);
        (string From, string To, string Name)? copied = null;
        var vm = new StartViewModel(store,
            careerFileExists: path => path == @"C:\careers\Monaco.ams2career",
            duplicateCareerFile: (from, to, name) => copied = (from, to, name));

        vm.DuplicateRecentCommand.Execute(vm.RecentCareers[0]);

        Assert.Equal(
            (@"C:\careers\Monaco.ams2career", @"C:\careers\Monaco (copy).ams2career", "Monaco (copy)"),
            copied);
        var entries = store.Load();
        Assert.Equal(2, entries.Count);
        Assert.Equal("Monaco (copy)", entries[0].CareerName); // the copy lands at the front
        Assert.Equal(1967, entries[0].SeasonYear);
        Assert.Null(vm.GalleryError);
    }

    [Fact]
    public void DuplicateRecent_CollisionPicksTheNextCopyNumber()
    {
        var store = Store();
        store.Touch(@"C:\careers\Monaco.ams2career", "Monaco");
        string? copyPath = null;
        var vm = new StartViewModel(store,
            careerFileExists: path =>
                path is @"C:\careers\Monaco.ams2career" or @"C:\careers\Monaco (copy).ams2career",
            duplicateCareerFile: (_, to, _) => copyPath = to);

        vm.DuplicateRecentCommand.Execute(vm.RecentCareers[0]);

        Assert.Equal(@"C:\careers\Monaco (copy 2).ams2career", copyPath);
        Assert.Equal("Monaco (copy 2)", store.Load()[0].CareerName);
    }

    [Fact]
    public void DuplicateRecent_FailedCopy_ReportsAndAddsNothing()
    {
        var store = Store();
        store.Touch(@"C:\careers\Monaco.ams2career", "Monaco");
        var vm = new StartViewModel(store,
            careerFileExists: path => path == @"C:\careers\Monaco.ams2career",
            duplicateCareerFile: (_, _, _) => throw new IOException("disk full"));

        vm.DuplicateRecentCommand.Execute(vm.RecentCareers[0]);

        Assert.NotNull(vm.GalleryError);
        Assert.Contains("Monaco", vm.GalleryError);
        Assert.Single(store.Load());
    }
}
