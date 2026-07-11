using Companion.Core.Career;

namespace Companion.ViewModels;

/// <summary>Display projection of a <see cref="CarSpec"/> for the reusable CarSpecCard control: the
/// machine name, a combined engine·power subheader, and the five labelled bars as {label, value,
/// max} rows. <see cref="From"/> returns null when the car has no spec, so the host collapses the
/// card. Pure and display-only.</summary>
public sealed record CarSpecCardViewModel
{
    public required string MachineName { get; init; }

    /// <summary>Engine and peak power joined for the subheader (e.g. "Honda V10 · 690 hp"); either
    /// half is dropped when absent, and the whole line is empty when neither is authored.</summary>
    public required string SubHeader { get; init; }

    public required IReadOnlyList<CarSpecBarViewModel> Bars { get; init; }

    /// <summary>Builds the card VM from a spec (null spec → null card), scaling every bar to the
    /// catalog's shared 0..barMax.</summary>
    public static CarSpecCardViewModel? From(CarSpec? spec, int barMax)
    {
        if (spec is null)
            return null;

        string power = spec.MaxPowerHp > 0 ? $"{spec.MaxPowerHp} hp" : "";
        string subHeader = (spec.Engine, power) switch
        {
            ({ Length: > 0 } e, { Length: > 0 } p) => $"{e}  ·  {p}",
            ({ Length: > 0 } e, _) => e,
            (_, { Length: > 0 } p) => p,
            _ => "",
        };

        return new CarSpecCardViewModel
        {
            MachineName = spec.MachineName,
            SubHeader = subHeader,
            Bars =
            [
                new CarSpecBarViewModel("ENG", spec.Bars.Engine, barMax),
                new CarSpecBarViewModel("TM", spec.Bars.Transmission, barMax),
                new CarSpecBarViewModel("SUS", spec.Bars.Suspension, barMax),
                new CarSpecBarViewModel("TIRE", spec.Bars.Tyre, barMax),
                new CarSpecBarViewModel("BRA", spec.Bars.Brake, barMax),
            ],
        };
    }
}

/// <summary>One labelled spec bar: <see cref="Label"/> (ENG/TM/SUS/TIRE/BRA), its 0..<see cref="Max"/>
/// <see cref="Value"/>.</summary>
public sealed record CarSpecBarViewModel(string Label, int Value, int Max);
