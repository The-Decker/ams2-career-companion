using Companion.Data;
using Microsoft.Data.Sqlite;

namespace Companion.Tests.Data;

/// <summary>
/// The FILE-level save &amp; reload store (character-death plan §4). Snapshots are whole career-DB copies
/// living in a sibling <c>Saves/&lt;stem&gt;/</c> folder, entirely outside the fold/replay contract, so
/// these tests exercise pure file mechanics: snapshot → mutate → restore reverts; list newest-first;
/// delete; and the Hardcore-death destructive helper.
/// </summary>
public sealed class SaveSlotStoreTests
{
    private const string Utc = "2026-07-12T00:00:00Z";

    private static void SeedCareer(CareerDatabase db, string name) =>
        CareerStore.CreateCareer(db, name, masterSeed: 42UL, appVersion: "test", createdUtc: Utc);

    [Fact]
    public void Save_ThenMutate_ThenRestore_RevertsTheCareerWholesale()
    {
        using var tmp = new TempDb();
        using (var db = CareerDatabase.Open(tmp.Path))
        {
            SeedCareer(db, "Original");
            // Snapshot the career at its original state.
            SaveSlotStore.Save(
                db.Connection, tmp.Path, "manual-001", "checkpoint",
                seasonYear: 1967, round: 3, createdUtc: Utc, isAutosave: false);

            // Mutate AFTER the snapshot, the change must be undone by a restore.
            using var mutate = db.Connection.CreateCommand();
            mutate.CommandText = "UPDATE career SET name = 'Mutated' WHERE id = 1;";
            mutate.ExecuteNonQuery();
            Assert.Equal("Mutated", CareerStore.ReadCareer(db).Name);
        }

        SaveSlotStore.Restore(tmp.Path, "manual-001");

        using (var db = CareerDatabase.Open(tmp.Path))
            Assert.Equal("Original", CareerStore.ReadCareer(db).Name);
    }

    [Fact]
    public void List_ReturnsSlotsNewestFirst_WithMetadata_AndDeleteRemovesOne()
    {
        using var tmp = new TempDb();
        using (var db = CareerDatabase.Open(tmp.Path))
        {
            SeedCareer(db, "Career");
            SaveSlotStore.Save(db.Connection, tmp.Path, "manual-001", "first", 1967, 1, "2026-07-12T00:00:01Z", false);
            SaveSlotStore.Save(db.Connection, tmp.Path, "manual-002", "second", 1967, 2, "2026-07-12T00:00:02Z", false);
        }

        var slots = SaveSlotStore.List(tmp.Path);
        Assert.Equal(2, slots.Count);
        // Newest first (the later CreatedUtc).
        Assert.Equal("manual-002", slots[0].SlotId);
        Assert.Equal("second", slots[0].Label);
        Assert.Equal(2, slots[0].Round);
        Assert.False(slots[0].IsAutosave);

        SaveSlotStore.Delete(tmp.Path, "manual-002");
        Assert.Equal("manual-001", Assert.Single(SaveSlotStore.List(tmp.Path)).SlotId);
    }

    [Fact]
    public void Restore_UnknownSlot_Throws()
    {
        using var tmp = new TempDb();
        using (var db = CareerDatabase.Open(tmp.Path))
            SeedCareer(db, "Career");

        Assert.Throws<InvalidOperationException>(() => SaveSlotStore.Restore(tmp.Path, "does-not-exist"));
    }

    [Fact]
    public void Save_OverwritesAnExistingSlotOfTheSameId()
    {
        using var tmp = new TempDb();
        using (var db = CareerDatabase.Open(tmp.Path))
        {
            SeedCareer(db, "Career");
            SaveSlotStore.Save(db.Connection, tmp.Path, "autosave-season-1", "start", 1967, 1, Utc, isAutosave: true);
            SaveSlotStore.Save(db.Connection, tmp.Path, "autosave-season-1", "restart", 1968, 5, Utc, isAutosave: true);
        }

        var slot = Assert.Single(SaveSlotStore.List(tmp.Path));
        Assert.Equal(1968, slot.SeasonYear);
        Assert.Equal(5, slot.Round);
    }

    [Fact]
    public void DeleteCareerAndAllSaves_RemovesTheCareerFileAndEverySnapshot()
    {
        using var tmp = new TempDb();
        using (var db = CareerDatabase.Open(tmp.Path))
        {
            SeedCareer(db, "Career");
            SaveSlotStore.Save(db.Connection, tmp.Path, "manual-001", "x", 1967, 1, Utc, isAutosave: false);
        }
        Assert.True(File.Exists(tmp.Path));
        Assert.True(Directory.Exists(SaveSlotStore.SavesDirectoryFor(tmp.Path)));

        SaveSlotStore.DeleteCareerAndAllSaves(tmp.Path);

        Assert.False(File.Exists(tmp.Path));
        Assert.False(Directory.Exists(SaveSlotStore.SavesDirectoryFor(tmp.Path)));
    }

    [Fact]
    public void List_IsEmpty_WhenNoSavesFolderExists()
    {
        using var tmp = new TempDb();
        using (var db = CareerDatabase.Open(tmp.Path))
            SeedCareer(db, "Career");

        Assert.Empty(SaveSlotStore.List(tmp.Path));
    }
}
