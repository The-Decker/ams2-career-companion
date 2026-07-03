using Microsoft.Data.Sqlite;

namespace Companion.Tests.Data;

/// <summary>A temp-file SQLite database path in an isolated directory, cleaned up (including
/// WAL side files) on dispose. Pools are cleared first so Windows releases the file handles.</summary>
internal sealed class TempDb : IDisposable
{
    private readonly string _directory;

    public string Path { get; }

    public TempDb()
    {
        _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "companion-data-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Path = System.IO.Path.Combine(_directory, "career.ams2career");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort — the OS temp cleaner owns leftovers.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
