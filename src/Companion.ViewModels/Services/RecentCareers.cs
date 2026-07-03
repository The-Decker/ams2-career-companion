using System.Text.Json;

namespace Companion.ViewModels.Services;

/// <summary>One entry of the start screen's most-recently-used career list.</summary>
public sealed record RecentCareer
{
    public required string Path { get; init; }
    public required string CareerName { get; init; }
    public required DateTimeOffset LastOpenedUtc { get; init; }
}

public interface IRecentCareersStore
{
    /// <summary>Most recent first. Entries whose career file no longer exists are pruned.</summary>
    IReadOnlyList<RecentCareer> Load();

    /// <summary>Insert or move to the front (capped list), then persist.</summary>
    void Touch(string path, string careerName);

    void Remove(string path);
}

/// <summary>
/// A small JSON MRU file (default: %APPDATA%\AMS2CareerCompanion\recent.json). Corrupt or
/// missing files read as empty — the start screen must never crash on a bad MRU file.
/// </summary>
public sealed class RecentCareersStore : IRecentCareersStore
{
    public const int Capacity = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly TimeProvider _clock;
    private readonly Func<string, bool> _careerFileExists;

    public RecentCareersStore(
        string filePath,
        TimeProvider? clock = null,
        Func<string, bool>? careerFileExists = null)
    {
        _filePath = filePath;
        _clock = clock ?? TimeProvider.System;
        _careerFileExists = careerFileExists ?? File.Exists;
    }

    public static RecentCareersStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AMS2CareerCompanion",
        "recent.json"));

    public IReadOnlyList<RecentCareer> Load() =>
        ReadFile().Where(entry => _careerFileExists(entry.Path)).ToList();

    public void Touch(string path, string careerName)
    {
        var entries = ReadFile()
            .Where(entry => !PathsEqual(entry.Path, path))
            .ToList();
        entries.Insert(0, new RecentCareer
        {
            Path = path,
            CareerName = careerName,
            LastOpenedUtc = _clock.GetUtcNow(),
        });
        WriteFile(entries.Take(Capacity).ToList());
    }

    public void Remove(string path) =>
        WriteFile(ReadFile().Where(entry => !PathsEqual(entry.Path, path)).ToList());

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private List<RecentCareer> ReadFile()
    {
        try
        {
            if (!File.Exists(_filePath))
                return [];
            var file = JsonSerializer.Deserialize<MruFile>(File.ReadAllText(_filePath), JsonOptions);
            return file?.Careers?.ToList() ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void WriteFile(List<RecentCareer> entries)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (directory is { Length: > 0 })
            Directory.CreateDirectory(directory);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(new MruFile { Careers = entries }, JsonOptions));
    }

    private sealed record MruFile
    {
        public List<RecentCareer>? Careers { get; init; }
    }
}
