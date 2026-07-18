using Companion.Data;

namespace Companion.Tests.Data;

/// <summary>Closing a career must actually release the .ams2career file handle. Microsoft.Data.Sqlite
/// pools connections, and a pooled (returned) connection keeps the native handle open, on Windows
/// that blocks File.Delete on the career file until the pool prunes. CareerDatabase.Dispose clears
/// its connection's pool so "close career, then delete it from the gallery" works within one app run
/// (the Start gallery's "Delete career file…" depends on this).</summary>
public sealed class CareerDatabaseDisposeTests
{
    [Fact]
    public void Dispose_ReleasesTheFileHandle_SoTheCareerFileCanBeDeleted()
    {
        using var temp = new TempDb();
        var database = CareerDatabase.Open(temp.Path);
        database.Dispose();

        // Without the pool clear in Dispose this throws IOException (sharing violation) on
        // Windows, the handle would sit in the pool until the process exits or the pool prunes.
        File.Delete(temp.Path);

        Assert.False(File.Exists(temp.Path));
        // A clean final close also checkpoints away the WAL sidecars.
        Assert.False(File.Exists(temp.Path + "-wal"));
        Assert.False(File.Exists(temp.Path + "-shm"));
    }
}
