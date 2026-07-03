using Companion.Core.Career;

namespace Companion.Data;

/// <summary>
/// The journal rows an era transition appends under the NEW season, in order: the
/// era.transition header (fromYear/toYear/bridgedYears + pack ids + the accepted team),
/// then the plan's own events (era.bridge per gap year, era.departed per lost entity, the
/// era.economy rescale note). ONE builder shared by the live path
/// (<see cref="CareerStore.StartNextSeason"/>) and replay
/// (<see cref="ReplayService.Resimulate(CareerDatabase, ulong, ReplaySimInputs)"/>), so the
/// byte-compare regenerates the stored sequence by construction.
/// </summary>
internal static class EraTransitionJournal
{
    public static IReadOnlyList<JournalEvent> Rows(TransitionPlan plan)
    {
        var rows = new List<JournalEvent>(plan.Events.Count + 1)
        {
            new()
            {
                Phase = DataJournalPhases.EraTransition,
                Entity = "season",
                DeltaJson = DataJson.Serialize(new
                {
                    fromYear = plan.FromYear,
                    toYear = plan.ToYear,
                    bridgedYears = plan.BridgedYears,
                    fromPackId = plan.FromPackId,
                    toPackId = plan.ToPackId,
                    teamId = plan.PlayerTeamId,
                }),
                Cause = "accepted-offer",
            },
        };
        rows.AddRange(plan.Events);
        return rows;
    }
}
