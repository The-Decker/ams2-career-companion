using Microsoft.Data.Sqlite;

namespace Companion.Data;

/// <summary>
/// File-level operations on career files for the Start gallery (rename / duplicate). The display
/// name lives INSIDE the .ams2career (career.name, id = 1) as well as in the MRU, so both
/// operations rewrite it in the affected file, otherwise the next open would resurrect the old
/// name. Connections are short-lived and their pools cleared on the way out so the touched files
/// stay immediately renamable/deletable (same contract as <see cref="CareerDatabase.Dispose"/>).
/// </summary>
public static class CareerFileStore
{
    /// <summary>Renames the career: moves the file (skipped when only the display name changes),
    /// then rewrites the stored name. A failed name rewrite moves the file back so the gallery
    /// entry stays truthful. Throws IOException/UnauthorizedAccessException for locked or
    /// permission-blocked files; SQLite-level failures surface as InvalidOperationException.</summary>
    public static void Rename(string sourcePath, string destinationPath, string newName)
    {
        bool moved = !string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase);
        if (moved)
            File.Move(sourcePath, destinationPath);
        try
        {
            UpdateStoredName(destinationPath, newName);
        }
        catch when (moved)
        {
            File.Move(destinationPath, sourcePath);
            throw;
        }
    }

    /// <summary>Copies the career via SQLite's backup API, a consistent snapshot even while the
    /// source is open somewhere (a raw File.Copy of a WAL-mode database mid-write can tear) —
    /// then rewrites the copy's stored name.</summary>
    public static void Duplicate(string sourcePath, string destinationPath, string copyName)
    {
        var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        try
        {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
            SetName(destination, copyName);
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException($"Could not copy the career file, {ex.Message}", ex);
        }
        finally
        {
            source.Dispose();
            destination.Dispose();
            SqliteConnection.ClearPool(source);
            SqliteConnection.ClearPool(destination);
        }
    }

    private static void UpdateStoredName(string path, string newName)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString());
        try
        {
            connection.Open();
            SetName(connection, newName);
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException($"Could not update the stored career name, {ex.Message}", ex);
        }
        finally
        {
            connection.Dispose();
            SqliteConnection.ClearPool(connection);
        }
    }

    private static void SetName(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE career SET name = @name WHERE id = 1;";
        command.Parameters.AddWithValue("@name", name);
        command.ExecuteNonQuery();
    }
}
