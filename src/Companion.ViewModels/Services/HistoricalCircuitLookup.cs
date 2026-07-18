using Companion.Core.Packs;

namespace Companion.ViewModels.Services;

/// <summary>
/// THE one rule for resolving a pack round's ORIGINAL circuit (Calendar expander, briefing
/// circuit panel, result-entry map strip): the round's authored <see cref="PackRound.History"/>
/// pointer when present, packs whose calendars run in a non-historical order, like the SMGP
/// replica's game-order season over 1989-modelled circuits, else the pack year's same-numbered
/// round (every historical pack; carryover-stable because it keys the PACK year, never the
/// drifting career year). Display-only reference data; the sim never reads it.
/// </summary>
public static class HistoricalCircuitLookup
{
    public static HistoricalCircuit? ForRound(
        SeasonPack pack, int roundNumber, Func<int, HistoricalSeason?> seasonForYear)
    {
        var packRound = pack.Season.Rounds.FirstOrDefault(r => r.Round == roundNumber);
        int year = packRound?.History?.Year ?? pack.Season.Year;
        int historyRound = packRound?.History?.Round ?? roundNumber;
        return seasonForYear(year)?.Rounds
            .FirstOrDefault(r => r.Round == historyRound)?.Circuit;
    }
}
