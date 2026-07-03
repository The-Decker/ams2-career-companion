using System.Text.Json;

namespace Companion.ViewModels.Settings;

public interface ISettingsStore
{
    /// <summary>Never throws: a missing or corrupt file loads as defaults (normalized).</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>
/// The settings.json store (default: %APPDATA%\AMS2CareerCompanion\settings.json —
/// camelCase, versioned, human-editable). Corrupt or missing files read as defaults; every
/// loaded value is normalized so hand edits can never wedge the UI.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public string FilePath { get; }

    public JsonSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    public static JsonSettingsStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AMS2CareerCompanion",
        "settings.json"));

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings().Normalized();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
            return (settings ?? new AppSettings()).Normalized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings().Normalized();
        }
    }

    public void Save(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(FilePath);
        if (directory is { Length: > 0 })
            Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings.Normalized(), JsonOptions));
    }
}

/// <summary>Non-persisting store: the default for tests and for shells constructed without
/// a real store (settings still live-apply within the session, nothing touches disk).</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private AppSettings _settings;

    public InMemorySettingsStore(AppSettings? initial = null) =>
        _settings = (initial ?? new AppSettings()).Normalized();

    public int SaveCount { get; private set; }

    public AppSettings Load() => _settings;

    public void Save(AppSettings settings)
    {
        _settings = settings.Normalized();
        SaveCount++;
    }
}
