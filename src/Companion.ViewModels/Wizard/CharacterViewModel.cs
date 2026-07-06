using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Character;

namespace Companion.ViewModels.Wizard;

/// <summary>
/// The new-career wizard's character-creation step (docs/dev/character-system.md §5). Three tiers
/// over the same state: pick an ARCHETYPE preset (the one-click default — a pre-spent, in-budget
/// template), then optionally FREE-CUSTOMIZE the seven stat sliders and the perk shelf with a live
/// remaining-CP meter, with the raw numbers available for tinkerers. Produces a
/// <see cref="CharacterProfile"/> at confirm. Pure viewmodel — no I/O, fully unit-testable.
/// </summary>
public sealed partial class CharacterViewModel : ObservableObject
{
    private readonly CharacterRules _rules;

    private static readonly IReadOnlyDictionary<string, string> StatLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pace"] = "Pace",
            ["oneLap"] = "One-lap pace",
            ["craft"] = "Craft",
            ["racecraft"] = "Racecraft",
            ["adaptability"] = "Adaptability",
            ["marketability"] = "Marketability",
            ["durability"] = "Durability",
        };

    public CharacterViewModel(CharacterRules rules)
    {
        _rules = rules;

        Stats = _rules.Stats.TalentStats.Select(s => new StatSlider(s.Id, Label(s.Id), Recompute)).ToList();
        MetaStats = _rules.Stats.MetaStats.Select(m => new StatSlider(m.Id, Label(m.Id), Recompute, m.Default)).ToList();

        Perks = _rules.Perks
            .Select(p => new PerkOption(p.Id, p.Name, p.Category, p.Cost, p.Description))
            .ToList();
        PerkCategories = Perks
            .GroupBy(p => p.Category, StringComparer.Ordinal)
            .Select(g => new PerkCategory(g.Key, g.ToList()))
            .ToList();

        Archetypes = _rules.Creation.Archetypes.ToList();
        // The first archetype is the default one-click character — a complete, valid build.
        SelectedArchetype = Archetypes.FirstOrDefault();
    }

    /// <summary>The five talent-stat sliders (clamped to each stat's creation range).</summary>
    public IReadOnlyList<StatSlider> Stats { get; }

    /// <summary>The two career meta-stat sliders (marketability, durability).</summary>
    public IReadOnlyList<StatSlider> MetaStats { get; }

    public IReadOnlyList<PerkOption> Perks { get; }

    /// <summary>Perks grouped by category for the shelf (pace, racecraft, physical, …).</summary>
    public IReadOnlyList<PerkCategory> PerkCategories { get; }

    public IReadOnlyList<Archetype> Archetypes { get; }

    [ObservableProperty]
    private Archetype? _selectedArchetype;

    partial void OnSelectedArchetypeChanged(Archetype? value)
    {
        if (value is null)
            return;
        ApplyArchetype(value);
    }

    /// <summary>The creation CP budget (10 by default).</summary>
    public int Budget => _rules.CharacterPoints.CreationBudget;

    /// <summary>Net CP the selected perks cost (refund perks are negative).</summary>
    public int NetCpSpend => Perks.Where(p => p.IsSelected).Sum(p => p.Cost);

    /// <summary>CP left over from the budget for stat growth (never negative for display).</summary>
    public int RemainingCp => Math.Max(0, Budget - NetCpSpend);

    /// <summary>A build is valid when its net perk spend lands in the audited window
    /// [minBudgetAfterSpend, budget + maxRefundHeadroom] (0..16 by default).</summary>
    public bool IsValid =>
        NetCpSpend >= _rules.CharacterPoints.MinBudgetAfterSpend
        && NetCpSpend <= _rules.CharacterPoints.MaxNetSpend;

    /// <summary>The one-line reason the current build is invalid, for the wizard footer; null when valid.</summary>
    public string? Invalidity => IsValid
        ? null
        : NetCpSpend < _rules.CharacterPoints.MinBudgetAfterSpend
            ? $"This build refunds more CP than allowed — spend at least {_rules.CharacterPoints.MinBudgetAfterSpend}."
            : $"This build costs {NetCpSpend} CP, over the {_rules.CharacterPoints.MaxNetSpend} maximum.";

    [RelayCommand]
    private void TogglePerk(PerkOption? perk)
    {
        if (perk is null)
            return;
        perk.IsSelected = !perk.IsSelected;
        Recompute();
    }

    /// <summary>Applies an archetype preset: its stat spread and its exact perk set. Selecting a
    /// preset is a complete, valid character; the player may then customize from there.</summary>
    private void ApplyArchetype(Archetype archetype)
    {
        foreach (var slider in Stats)
            if (archetype.StartStats.TryGetValue(slider.Id, out double value))
                slider.SetSilently(value);
        foreach (var slider in MetaStats)
            if (archetype.StartMeta.TryGetValue(slider.Id, out double value))
                slider.SetSilently(value);

        var chosen = new HashSet<string>(archetype.PerkIds, StringComparer.Ordinal);
        foreach (var perk in Perks)
            perk.IsSelected = chosen.Contains(perk.Id);

        Recompute();
    }

    private void Recompute()
    {
        OnPropertyChanged(nameof(NetCpSpend));
        OnPropertyChanged(nameof(RemainingCp));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Invalidity));
    }

    /// <summary>The authored character this step produces at confirm.</summary>
    public CharacterProfile BuildProfile()
    {
        var stats = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var slider in Stats.Concat(MetaStats))
            stats[slider.Id] = slider.Value;

        return new CharacterProfile
        {
            Stats = stats,
            PerkIds = Perks.Where(p => p.IsSelected).Select(p => p.Id).ToList(),
            CpUnspent = RemainingCp,
        };
    }

    private static string Label(string id) => StatLabels.GetValueOrDefault(id, id);
}

/// <summary>One stat slider, clamped to the character-creation band (0.15–0.85 for talent stats,
/// 0–1 for meta). Raises a recompute callback on user edits (not on preset application).</summary>
public sealed partial class StatSlider : ObservableObject
{
    private readonly Action _onChanged;
    private bool _suppress;

    public StatSlider(string id, string label, Action onChanged, double initial = 0.5)
    {
        Id = id;
        Label = label;
        _onChanged = onChanged;
        _value = Math.Clamp(initial, Min, Max);
    }

    public string Id { get; }
    public string Label { get; }
    public double Min => 0.15;
    public double Max => 0.85;

    [ObservableProperty]
    private double _value;

    partial void OnValueChanged(double value)
    {
        double clamped = Math.Clamp(value, Min, Max);
        if (clamped != value)
        {
            _value = clamped; // re-clamp without re-entrancy; the setter already ran
        }
        if (!_suppress)
            _onChanged();
    }

    /// <summary>Sets the value from a preset without firing the recompute (the caller recomputes once).</summary>
    public void SetSilently(double value)
    {
        _suppress = true;
        Value = Math.Clamp(value, Min, Max);
        _suppress = false;
    }
}

/// <summary>One selectable perk on the shelf.</summary>
public sealed partial class PerkOption(string id, string name, string category, int cost, string description)
    : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Category { get; } = category;
    public int Cost { get; } = cost;
    public string Description { get; } = description;

    /// <summary>The perk cost shown with a sign ("+2", "0", "−1" for a refund).</summary>
    public string CostLabel => Cost > 0 ? $"+{Cost}" : Cost.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>A category header plus its perks, for the grouped shelf.</summary>
public sealed record PerkCategory(string Name, IReadOnlyList<PerkOption> Perks);
