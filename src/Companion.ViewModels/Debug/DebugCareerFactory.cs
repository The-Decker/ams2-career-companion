using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Smgp;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Debug;

/// <summary>
/// TIER-1 helpers for the developer debug menu (dynasty-passport-roadmap.md Piece 2, §4 of the build
/// brief): build REAL throwaway careers and drive them through the SAME provenance-excluded INPUT
/// seams the normal app uses, so a debug-created career still resimulates byte-identical. Nothing
/// here pokes derived state (level/XP/standings/injury/death) or injects an un-provenanced journal
/// row — it only routes through <see cref="CareerCreationRequest"/> + the input mutators on
/// <see cref="ICareerSession"/> (<c>Apply</c>, <c>ApplySkillPlan</c>). The result is an honest save.
/// </summary>
public static class DebugCareerFactory
{
    /// <summary>A valid progression-v2 character used for throwaway careers when the caller supplies
    /// none. Uses a NON-contextual Racing DNA (circuit specialist) so it is accepted against any pack
    /// or mode — a nationality/rival DNA would require pack-specific validation. Mirrors the shape the
    /// creation boundary requires (see CampaignProgressionCreationTests.VersionTwoCharacter).</summary>
    public static CharacterProfile DefaultCharacter(string name = "Debug Driver")
    {
        var talent = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.70,
            ["oneLap"] = 0.65,
            ["craft"] = 0.60,
            ["racecraft"] = 0.62,
            ["adaptability"] = 0.58,
        };
        var meta = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["marketability"] = 0.50,
            ["durability"] = 0.55,
        };
        var all = talent.Concat(meta).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        return new CharacterProfile
        {
            Name = name,
            CountryCode = "GBR",
            Age = 22,
            Stats = all,
            PerkIds = ["engineers_favorite"],
            CreationPerkIds = ["engineers_favorite"],
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            ExpectationModelVersion = CharacterProfile.CurrentExpectationModelVersion,
            RacingDnaId = "dna_circuit_specialist",
            RacingDnaVersion = 1,
            RacingDnaChoice = "technical",
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = talent,
                Meta = meta,
                TraitIds = ["engineers_favorite"],
            },
        };
    }

    /// <summary>Builds a creation request for a throwaway career in <paramref name="mode"/>. Sets the
    /// SMGP gate for the SMGP mode and turns on the form-aware fold (as the real wizard does). Legacy
    /// mode (<paramref name="mode"/> null) omits the character so the compact v1 path is exercised.</summary>
    public static CareerCreationRequest BuildRequest(
        string packDirectory,
        string careerFilePath,
        string? mode,
        long masterSeed,
        CharacterProfile? character = null,
        string? careerName = null,
        string? playerLivery = null)
    {
        bool smgp = mode == CareerExperienceModes.Smgp;
        return new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = careerFilePath,
            CareerName = careerName ?? "Debug career",
            MasterSeed = masterSeed,
            ExperienceMode = mode,
            // A REAL career must take a REAL seat on the pack's ladder — a real pack never contains
            // the in-memory preview livery, so the caller resolves an actual entry (see
            // ResolvePlayerLivery). The preview-pack livery is only a last-resort fallback.
            PlayerLiveryName = playerLivery ?? DebugPreviewPack.PlayerLivery,
            Character = mode is null ? character : character ?? DefaultCharacter(),
            FormAware = true,
            SmgpMode = smgp,
        };
    }

    /// <summary>Resolves a REAL player seat livery from a pack on disk — the LAST grid entry (a
    /// backmarker, so an SMGP debug career can climb the ladder and show promotions). Returns null
    /// when the pack cannot be read; the caller then falls back to the preview livery.</summary>
    public static string? ResolvePlayerLivery(string packDirectory)
    {
        try
        {
            var pack = SeasonPackFiles.Read(packDirectory).Parse();
            return pack.Entries.Count > 0 ? pack.Entries[^1].Ams2LiveryName : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Text.Json.JsonException or InvalidOperationException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Auto-applies a player-win result for every remaining round until the season is
    /// complete — the honest way to "advance a season" (each round is a real <c>Apply</c> INPUT the
    /// fold derives from, and the run resimulates byte-identical). The player is placed first; every
    /// other seat keeps grid order. Off-mortality throwaways never sit a round out. Stops early when
    /// an SMGP two-wins promotion offer is pending: season end HOLDS until <c>ResolveSmgpOffer</c>
    /// answers it (a deferred offer can arrive mid-season or after the final round), so the caller
    /// must resolve and then resume — applying into a held season would diverge from the screen
    /// flow replay reproduces. Null for every non-SMGP session, so other modes never break here.</summary>
    public static int FastForwardToSeasonEnd(ICareerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        int applied = 0;
        while (!session.Summary.SeasonComplete)
        {
            if (session.CurrentSmgpPromotion() is not null)
                break;
            var grid = session.CurrentGrid();
            if (grid.Count == 0)
                break;

            string playerId = grid.FirstOrDefault(s => s.IsPlayer)?.DriverId ?? grid[0].DriverId;
            var classified = grid.Select(s => s.DriverId).Where(id => id != playerId).ToList();
            classified.Insert(0, playerId);

            session.Apply(new ResultDraft
            {
                Classified = classified,
                DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal),
                Disqualified = [],
            });
            applied++;

            // A hard safety stop: a pathological pack should never loop forever in a dev tool.
            if (applied > session.Summary.RoundCount + 4)
                break;
        }
        return applied;
    }

    /// <summary>Spends one Skill Point on the first affordable unlockable tree node, through the real
    /// atomic <c>ApplySkillPlan</c> INPUT seam (the ONLY legitimate v2 development injection point).
    /// Returns the acquired node id, or null when nothing is affordable/available. Try/catch guards a
    /// node that fails the boundary's validation — the plan is all-or-nothing, so a rejection applies
    /// nothing.</summary>
    public static string? TrySpendOneSkillPoint(ICareerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        int available = session.AvailableCharacterCp();
        if (available <= 0)
            return null;

        var snapshot = session.SkillTree();
        if (snapshot is null)
            return null;

        foreach (var node in snapshot.Branches
                     .SelectMany(b => b.Nodes)
                     .Where(n => n.State == SkillNodeState.Unlockable && n.Cost > 0 && n.Cost <= available)
                     .OrderBy(n => n.Cost))
        {
            try
            {
                session.ApplySkillPlan([node.Id]);
                return node.Id;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                // This node was not a valid single-node plan right now; try the next candidate.
            }
        }
        return null;
    }

    /// <summary>Advances a REAL SMGP career to the START of season ordinal
    /// <paramref name="targetOrdinal"/> (clamped to 1–<see cref="SmgpRules.CampaignSeasons"/>) —
    /// the "jump to season N" the live game only reaches by playing the campaign. Every step goes
    /// through the honest provenance-excluded INPUT seams: each round is a real <c>Apply</c>
    /// player-win, deferred two-wins promotion offers are ACCEPTED via <c>ResolveSmgpOffer</c> (the
    /// ladder climb is the point of the replica), and each rollover signs the first offer letter
    /// then REOPENS the file (the session keeps pointing at the finished season otherwise — the
    /// exact flow the shell drives). The run resimulates byte-identical.
    ///
    /// Returns the session positioned at the reached ordinal — possibly a REOPENED instance, so the
    /// caller must use the return value and must not dispose sessions it passed in (the
    /// <paramref name="reopen"/> callback owns disposing the superseded one). Stops early with
    /// <paramref name="note"/> saying why: a Zeroforce knock-out ends the campaign, a season with no
    /// offer letter, or an advance that fails to move the ordinal.</summary>
    public static ICareerSession AdvanceSmgpToSeason(
        ICareerSession session,
        int targetOrdinal,
        Func<ICareerSession, ICareerSession> reopen,
        out string note)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reopen);
        int target = Math.Clamp(targetOrdinal, 1, SmgpRules.CampaignSeasons);
        note = "";

        int ordinal = session.CurrentSmgpBriefing()?.SeasonOrdinal ?? 1;
        while (ordinal < target)
        {
            FastForwardToSeasonEnd(session);
            // Resolve every deferred promotion (fast-forward stops at each one). Bounded: a
            // well-behaved session clears one offer per resolve; the guard only fences a fake.
            for (int guard = 0; guard < 8 && session.CurrentSmgpPromotion() is not null; guard++)
            {
                session.ResolveSmgpOffer(accept: true);
                FastForwardToSeasonEnd(session); // resume when the offer interrupted mid-season
            }

            var review = session.SeasonReview();
            var offer = review?.Offers.FirstOrDefault();
            if (offer is null)
            {
                note = $"Stopped at season {ordinal}: no offer letter to sign for the next season " +
                       "(career over, or the campaign finale).";
                return session;
            }
            session.AcceptOffer(offer.TeamId);
            session.StartNextSeason(offer.TeamId);

            // The old session still points at the FINISHED season — reopen to land in the new one.
            session = reopen(session);
            int reached = session.CurrentSmgpBriefing()?.SeasonOrdinal ?? (ordinal + 1);
            if (reached <= ordinal)
            {
                note = $"Stopped: the season advance did not move the ordinal ({ordinal} → {reached}).";
                return session;
            }
            ordinal = reached;

            if (session.CurrentSmgpBriefing()?.CareerOver == true)
            {
                note = $"SMGP career over (the floor gave way) before season {target} — " +
                       $"opened at season {ordinal}.";
                return session;
            }
        }
        return session;
    }
}
