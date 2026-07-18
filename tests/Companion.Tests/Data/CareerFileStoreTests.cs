using Companion.Data;

namespace Companion.Tests.Data;

/// <summary>The Start gallery's rename/duplicate file operations against REAL career databases:
/// the display name lives inside the file (career.name), so both operations must rewrite it —
/// and both must leave every touched file immediately deletable (pools cleared).</summary>
public sealed class CareerFileStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-filestore-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string PathFor(string fileName) => System.IO.Path.Combine(_root, fileName);

    private static void CreateCareerFile(string path, string name)
    {
        using var database = CareerDatabase.Open(path);
        using var command = database.Connection.CreateCommand();
        command.CommandText =
            "INSERT INTO career (id, name, created_utc, master_seed, app_version) " +
            "VALUES (1, @name, '2026-01-01T00:00:00Z', 42, 'test');";
        command.Parameters.AddWithValue("@name", name);
        command.ExecuteNonQuery();
    }

    private static string StoredName(string path)
    {
        using var database = CareerDatabase.Open(path);
        using var command = database.Connection.CreateCommand();
        command.CommandText = "SELECT name FROM career WHERE id = 1;";
        return (string)command.ExecuteScalar()!;
    }

    [Fact]
    public void Rename_MovesTheFileAndRewritesTheStoredName()
    {
        string source = PathFor("Monaco.ams2career");
        string destination = PathFor("Glory Years.ams2career");
        CreateCareerFile(source, "Monaco");

        CareerFileStore.Rename(source, destination, "Glory Years");

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(destination));
        Assert.Equal("Glory Years", StoredName(destination));
        // The renamed file is immediately deletable, no pooled handle survives.
        File.Delete(destination);
    }

    [Fact]
    public void Rename_DisplayNameOnly_SamePath_RewritesTheStoredNameInPlace()
    {
        string path = PathFor("a.ams2career");
        CreateCareerFile(path, "Old name");

        CareerFileStore.Rename(path, path, "New name");

        Assert.True(File.Exists(path));
        Assert.Equal("New name", StoredName(path));
    }

    [Fact]
    public void Duplicate_CopiesTheCareerAndRenamesOnlyTheCopy()
    {
        string source = PathFor("Monaco.ams2career");
        string copy = PathFor("Monaco (copy).ams2career");
        CreateCareerFile(source, "Monaco");

        CareerFileStore.Duplicate(source, copy, "Monaco (copy)");

        Assert.Equal("Monaco", StoredName(source));
        Assert.Equal("Monaco (copy)", StoredName(copy));
        // Both files immediately deletable afterwards.
        File.Delete(source);
        File.Delete(copy);
    }

    [Fact]
    public void Duplicate_WhileTheSourceIsOpen_StillProducesAConsistentCopy()
    {
        // The backup API reads a consistent snapshot even while a session holds the source open
        // (the reason Duplicate is not a raw File.Copy, a WAL-mode copy mid-write can tear).
        string source = PathFor("open.ams2career");
        string copy = PathFor("open (copy).ams2career");
        CreateCareerFile(source, "Open career");

        using var held = CareerDatabase.Open(source);
        CareerFileStore.Duplicate(source, copy, "Open career (copy)");

        Assert.Equal("Open career (copy)", StoredName(copy));
    }
}
