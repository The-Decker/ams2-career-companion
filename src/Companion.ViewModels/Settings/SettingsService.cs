namespace Companion.ViewModels.Settings;

/// <summary>
/// The live settings seam every consumer reads through: one current snapshot, updated
/// atomically and persisted on every change, with a change event so settings apply live
/// (no restart), the accent/font resources, open screens, and future consumers all
/// subscribe to <see cref="Changed"/>.
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>Raised after every persisted change, with the new snapshot.</summary>
    event EventHandler<AppSettings>? Changed;

    /// <summary>Applies a mutation to the current snapshot, normalizes, persists, and
    /// raises <see cref="Changed"/>.</summary>
    void Update(Func<AppSettings, AppSettings> mutate);

    /// <summary>Back to defaults (persisted; raises <see cref="Changed"/>).</summary>
    void Reset();
}

public sealed class SettingsService : ISettingsService
{
    private readonly ISettingsStore _store;

    public SettingsService(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Current = store.Load();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        var next = mutate(Current).Normalized();
        Current = next;
        _store.Save(next);
        Changed?.Invoke(this, next);
    }

    public void Reset() => Update(static _ => new AppSettings());
}
