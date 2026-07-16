using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Newsroom;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Data;

namespace Companion.ViewModels.Services;

/// <summary>
/// The newsroom half of the session: shapes the mode-agnostic per-season/per-round facts the
/// <see cref="CareerNewsEvents"/> detector reads (the same shape-in-the-session, detect-in-Core
/// split as <c>BuildSmgpNarrativeSeasons</c>). A pure display-only projection over the immutable
/// stored results, journal, and folded states — never a fold input, so replay stays
/// byte-identical by construction (docs/dev/newsroom-history-overhaul.md D1/D4).
/// </summary>
public sealed partial class CareerSessionService
{
    /// <summary>Every detected newsroom event for the whole career, chronological — the spine
    /// triggers plus, for historical-style careers with a documented reference year, the
    /// real-history divergence events. Deterministic per career.</summary>
    public IReadOnlyList<NewsEvent> NewsroomEvents()
    {
        var seasons = BuildNewsroomSeasons();
        var events = new List<NewsEvent>(CareerNewsEvents.Detect(seasons));

        if (!IsSmgpPack) // the SMGP universe is fiction; it never compares against real history
        {
            foreach (var season in seasons)
            {
                if (HistoricalSeason(season.Year) is { } historical)
                {
                    events.AddRange(CareerDivergence.ToNewsEvents(
                        CareerDivergence.Compare(season, historical)));
                }
            }
        }

        return events.OrderBy(e => e.SeasonOrdinal).ThenBy(e => e.Round).ToList();
    }

    /// <summary>The real-history-vs-career-universe comparison for one season: what really
    /// happened, what this universe produced, and where they part. Null for SMGP careers
    /// (fictional canon) and for years without a documented reference season.</summary>
    public SeasonDivergenceReport? SeasonDivergence(int seasonOrdinal)
    {
        if (IsSmgpPack)
        {
            return null;
        }
        var season = BuildNewsroomSeasons().FirstOrDefault(s => s.Ordinal == seasonOrdinal);
        if (season is null)
        {
            return null;
        }
        return HistoricalSeason(season.Year) is { } historical
            ? CareerDivergence.Compare(season, historical)
            : null;
    }

    /// <summary>The rendered newsroom: every career event voiced through the template library,
    /// newest first. Bodies re-render deterministically from the master seed on every call —
    /// never stored, never a fold input. Empty when the newsroom packs are absent.</summary>
    public IReadOnlyList<NewsroomArticle> NewsroomFeed()
    {
        var rules = _environment.Rules;
        if (rules is null || rules.NewsroomCorpus.IsEmpty)
        {
            return [];
        }

        var identity = new NewsroomIdentity
        {
            PlayerName = PlayerDisplayName() ?? "",
            PreferredEra = SmgpNewsEra,
        };

        var articles = new List<NewsroomArticle>();
        foreach (var e in NewsroomEvents())
        {
            var article = NewsroomComposer.Compose(e, rules.NewsroomCorpus, rules.NewsDesks, identity, MasterSeedU);
            if (article is not null)
            {
                articles.Add(article);
            }
        }

        articles.Reverse(); // detection is chronological; the newsroom reads newest first
        return articles;
    }

    /// <summary>The developing narrative threads (title fight, reliability crisis, rivalry,
    /// injury recovery, driver market...) derived from the event spine. Display-only.</summary>
    public IReadOnlyList<StoryThread> StoryThreads() =>
        Companion.Core.Newsroom.StoryThreads.Build(NewsroomEvents());

    /// <summary>The rumour ledger: fact-backed whispers with honest resolution links. The
    /// original rumour story is never rewritten; resolution links the settling story.</summary>
    public IReadOnlyList<RumorRecord> RumorBoard() =>
        RumorBook.Build(NewsroomEvents());

    /// <summary>The controlled editorial package for one round: importance-selected stories
    /// (quiet 5 / busy 8 / big 12, capped 14) rather than everything the spine detected.</summary>
    public IReadOnlyList<EditorialSelection> WeekendPackage(int seasonOrdinal, int round) =>
        EditorialSelector.SelectRound(
            NewsroomEvents().Where(e => e.SeasonOrdinal == seasonOrdinal && e.Round == round).ToList());

    /// <summary>Read/bookmark state for every story key the user has touched. USER PREFERENCE
    /// (schema v6): never journaled, never a fold input, survives re-simulation.</summary>
    public IReadOnlyDictionary<string, NewsReadingState> ReadingState() =>
        NewsReadingStateStore.ReadAll(_database);

    public void MarkStoryRead(string storyKey) =>
        NewsReadingStateStore.MarkRead(_database, storyKey, NowUtc());

    public void SetStoryBookmark(string storyKey, bool bookmarked) =>
        NewsReadingStateStore.SetBookmark(_database, storyKey, bookmarked, NowUtc());

    private HistoryArchiveIndex? _historyArchive;

    /// <summary>The computed history archive: driver/team/circuit profiles aggregated from the
    /// shipped verified season files, the authored eras/subjects/team-identity reference data,
    /// and the verified-history timeline. Static app data — built once per session and cached.
    /// Real history only; career-universe records never enter this index.</summary>
    public HistoryArchiveIndex HistoryArchive()
    {
        if (_historyArchive is null)
        {
            var directory = _environment.HistoryDirectory;
            var store = new HistoricalSeasonStore(directory);
            _historyArchive = HistoryEntityIndex.Build(
                store.ForYear,
                Companion.Core.HistoryArchive.HistoryArchiveData.Load(directory));
        }
        return _historyArchive;
    }

    private IReadOnlyList<NewsroomSeason> BuildNewsroomSeasons()
    {
        var seasonsInput = new List<NewsroomSeason>();
        int ordinal = 0;
        foreach (var season in CareerStore.ReadSeasons(_database))
        {
            ordinal++;
            var seasonPack = SeasonPackFor(season);
            string pid = PlayerDriverIdFor(season, seasonPack);
            var venueByRound = seasonPack.Season.Rounds.ToDictionary(r => r.Round, VenueLabel);
            var driverNames = seasonPack.Drivers.ToDictionary(d => d.Id, d => d.Name, StringComparer.Ordinal);
            var teamsById = seasonPack.Teams.ToDictionary(t => t.Id, StringComparer.Ordinal);
            var championshipRounds = seasonPack.Season.Rounds.Where(r => r.Championship).Select(r => r.Round).ToList();

            // The player's season-start seat → team identity (SMGP seats ride CurrentSeatLivery).
            var startState = StateStore.ReadPlayerState(_database, season.Id, StateStore.StageStart);
            string? startLivery = startState?.Smgp?.CurrentSeatLivery ?? startState?.LiveryName;
            var (startTeamName, _) = TeamOfLiveryInPack(seasonPack, startLivery);
            string startTeamId = startLivery is { Length: > 0 }
                ? seasonPack.Entries.FirstOrDefault(e =>
                    string.Equals(e.Ams2LiveryName, startLivery, StringComparison.Ordinal))?.TeamId ?? ""
                : "";

            // One journal read feeds result causes, accidents, and the season notes below.
            var journalRows = JournalStore.ReadSeason(_database, season.Id);
            var resultRowByRound = new Dictionary<int, (string Cause, int? Expected, int? Actual, bool Dnf)>();
            var accidentByRound = new Dictionary<int, (string Outcome, int MissRaces)>();
            foreach (var row in journalRows)
            {
                if (row.Round is not { } jr)
                {
                    continue;
                }

                if (string.Equals(row.Phase, JournalPhases.RaceResult, StringComparison.Ordinal)
                    && string.Equals(row.Entity, "player", StringComparison.Ordinal))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(row.DeltaJson);
                        var root = doc.RootElement;
                        int? expected = root.TryGetProperty("expectedFinish", out var ev) && ev.ValueKind == JsonValueKind.Number
                            ? ev.GetInt32() : null;
                        int? actual = root.TryGetProperty("actualFinish", out var av) && av.ValueKind == JsonValueKind.Number
                            ? av.GetInt32() : null;
                        bool dnf = root.TryGetProperty("dnf", out var dv)
                            && (dv.ValueKind == JsonValueKind.True || dv.ValueKind == JsonValueKind.String);
                        resultRowByRound[jr] = (row.Cause, expected, actual, dnf);
                    }
                    catch (JsonException)
                    {
                        // A malformed delta cell never breaks the display feed.
                    }
                }
                else if (string.Equals(row.Phase, JournalPhases.PlayerAccident, StringComparison.Ordinal))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(row.DeltaJson);
                        var root = doc.RootElement;
                        string? outcome = root.TryGetProperty("outcome", out var ov) ? ov.GetString() : null;
                        if (outcome is "minorInjury" or "seasonEnding" or "death")
                        {
                            int miss = root.TryGetProperty("missRaces", out var mv) && mv.ValueKind == JsonValueKind.Number
                                ? mv.GetInt32() : 0;
                            accidentByRound[jr] = (outcome, miss);
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }
            }

            // Standings snapshots over the championship rounds stored so far.
            var stored = ResultStore.ReadSeasonResults(_database, season.Id).OrderBy(r => r.Round).ToList();
            var champStored = stored
                .Where(r => seasonPack.Season.Rounds.FirstOrDefault(rd => rd.Round == r.Round)?.Championship ?? false)
                .ToList();
            var scoring = ChampionshipCalendar.ResolveScoring(seasonPack);
            IReadOnlyList<StandingsSnapshot> snapshots = champStored.Count > 0
                ? StandingsEngine.ComputeSeason(scoring, champStored.Select(r => r.ToRoundResult()).ToList()).Snapshots
                : [];
            var snapshotByRound = new Dictionary<int, StandingsSnapshot>();
            for (int i = 0; i < snapshots.Count && i < champStored.Count; i++)
            {
                snapshotByRound[champStored[i].Round] = snapshots[i];
            }

            // Gross points are monotonic — the honest "scored this round" signal even under
            // dropped-scores rules (counted points can shrink late-season).
            var grossAfterRound = new Dictionary<int, double>();
            for (int i = 0; i < snapshots.Count && i < champStored.Count; i++)
            {
                var ps = snapshots[i].Drivers.FirstOrDefault(d => string.Equals(d.DriverId, pid, StringComparison.Ordinal));
                grossAfterRound[champStored[i].Round] = ps?.GrossPoints.ToDouble() ?? 0.0;
            }

            double maxPerRound = MaxPointsPerRound(scoring.PointsSystem);
            bool complete = string.Equals(season.Status, SeasonStatus.Complete, StringComparison.Ordinal);
            var finalSnapshot = snapshots.Count > 0 ? snapshots[^1] : null;
            string championId = "";
            string championName = "";
            bool playerChampion = false;
            if (complete && finalSnapshot?.Drivers.FirstOrDefault(d => d.Position == 1) is { } champRow)
            {
                championId = champRow.DriverId;
                championName = DisplayName(champRow.DriverId);
                playerChampion = string.Equals(champRow.DriverId, pid, StringComparison.Ordinal);
            }

            double previousGross = 0.0;
            var rounds = new List<NewsroomRound>();
            foreach (var s in stored)
            {
                var envelope = s.ToEnvelope();
                var race = envelope.Result.Sessions.FirstOrDefault(x => x.Kind == SessionKind.Race)
                    ?? envelope.Result.Sessions.FirstOrDefault();
                var entries = race?.Entries ?? [];
                int? finish = race is not null ? FinishPosition(entries, pid) : null;
                bool playerEntered = entries.Any(e => string.Equals(e.DriverId, pid, StringComparison.Ordinal));

                var resultRow = resultRowByRound.TryGetValue(s.Round, out var rr) ? rr : default;
                string dnfCause = playerEntered && finish is null && !envelope.PlayerDidNotStart
                    ? resultRow.Cause switch
                    {
                        "dnf-mechanical" => "mechanical",
                        "dnf-driver-error" => "driverError",
                        _ => envelope.PlayerDnfCause == DnfCause.Mechanical ? "mechanical" : "driverError",
                    }
                    : "";

                bool isChampionshipRound = seasonPack.Season.Rounds
                    .FirstOrDefault(rd => rd.Round == s.Round)?.Championship ?? false;

                double grossNow = grossAfterRound.TryGetValue(s.Round, out var g) ? g : previousGross;
                bool scoredThisRound = grossNow > previousGross;
                previousGross = grossNow;

                // Winner identity + team (entry covering this calendar round wins attribution).
                var winnerRow = entries.FirstOrDefault(e => e.Status == FinishStatus.Classified && e.Position == 1);
                string winnerId = winnerRow?.DriverId ?? "";
                string winnerTeamId = "";
                if (winnerId.Length > 0)
                {
                    winnerTeamId = seasonPack.Entries.FirstOrDefault(e =>
                            string.Equals(e.DriverId, winnerId, StringComparison.Ordinal)
                            && RoundsRange.TryParse(e.Rounds, out var range) && range.Contains(s.Round))?.TeamId
                        ?? seasonPack.Entries.FirstOrDefault(e =>
                            string.Equals(e.DriverId, winnerId, StringComparison.Ordinal))?.TeamId
                        ?? "";
                }
                var winnerTeam = winnerTeamId.Length > 0 ? teamsById.GetValueOrDefault(winnerTeamId) : null;

                // Qualifying facts (present only when the weekend ran qualifying).
                int? playerQuali = null;
                string poleDriverId = "";
                if (envelope.QualifyingOrder is { Count: > 0 } order)
                {
                    poleDriverId = MapPlayer(order[0], pid);
                    for (int qi = 0; qi < order.Count; qi++)
                    {
                        if (string.Equals(order[qi], pid, StringComparison.Ordinal))
                        {
                            playerQuali = qi + 1;
                            break;
                        }
                    }
                }

                // Championship picture after this round.
                string leaderId = "", leaderName = "";
                double leaderPoints = 0, secondPoints = 0, playerPoints = 0;
                int? playerPosition = null;
                double maxRemaining = 0;
                bool isFinal = false;
                if (isChampionshipRound && snapshotByRound.TryGetValue(s.Round, out var snap))
                {
                    var leader = snap.Drivers.FirstOrDefault(d => d.Position == 1);
                    if (leader is not null)
                    {
                        leaderId = MapPlayer(leader.DriverId, pid);
                        leaderName = DisplayName(leader.DriverId);
                        leaderPoints = leader.CountedPoints.ToDouble();
                    }
                    var second = snap.Drivers.Where(d => d.Position is > 1).OrderBy(d => d.Position).FirstOrDefault();
                    secondPoints = second?.CountedPoints.ToDouble() ?? 0;
                    var playerRow = snap.Drivers.FirstOrDefault(d => string.Equals(d.DriverId, pid, StringComparison.Ordinal));
                    playerPosition = playerRow?.Position;
                    playerPoints = playerRow?.CountedPoints.ToDouble() ?? 0;
                    int remainingRounds = championshipRounds.Count(r => r > s.Round);
                    maxRemaining = remainingRounds * maxPerRound;
                    isFinal = remainingRounds == 0;
                }

                var accident = accidentByRound.TryGetValue(s.Round, out var acc) ? acc : default;

                rounds.Add(new NewsroomRound
                {
                    Round = s.Round,
                    Championship = isChampionshipRound,
                    Venue = venueByRound.GetValueOrDefault(s.Round) ?? $"Round {s.Round}",
                    IsFinalChampionshipRound = isFinal,
                    PlayerFinish = finish,
                    ExpectedFinish = resultRow.Expected,
                    PlayerDnfCause = dnfCause,
                    PlayerDidNotStart = envelope.PlayerDidNotStart,
                    PlayerScoredPoints = scoredThisRound,
                    IsWet = envelope.IsWet,
                    PlayerQualifyingPosition = playerQuali,
                    PoleDriverId = poleDriverId,
                    WinnerId = MapPlayer(winnerId, pid),
                    WinnerName = winnerId.Length > 0 ? DisplayName(winnerId) : "",
                    WinnerTeamId = winnerTeamId,
                    WinnerTeamName = winnerTeam?.Name ?? "",
                    WinnerTeamTier = winnerTeam is null ? 0 : Math.Clamp(winnerTeam.Prestige, 1, 5),
                    LeaderId = leaderId,
                    LeaderName = leaderName,
                    LeaderPoints = leaderPoints,
                    SecondPoints = secondPoints,
                    PlayerPosition = playerPosition,
                    PlayerPoints = playerPoints,
                    MaxRemainingPoints = maxRemaining,
                    MaxPointsPerRound = maxPerRound,
                    AccidentOutcome = accident.Outcome ?? "",
                    AccidentMissRaces = accident.MissRaces,
                    RivalName = envelope.SmgpRival is { } rival ? DisplayName(rival.RivalDriverId) : "",
                });
            }

            seasonsInput.Add(new NewsroomSeason
            {
                Ordinal = ordinal,
                Year = season.Year,
                ChampionshipRoundCount = championshipRounds.Count,
                Complete = complete,
                ChampionId = championId,
                ChampionName = championName,
                PlayerChampion = playerChampion,
                PlayerTeamId = startTeamId,
                PlayerTeamName = startTeamName,
                Rounds = rounds,
                SeasonNotes = BuildSeasonNotes(journalRows, driverNames, teamsById),
            });

            string DisplayName(string driverId) =>
                string.Equals(driverId, pid, StringComparison.Ordinal)
                    ? PlayerDisplayName() ?? driverNames.GetValueOrDefault(driverId, driverId)
                    : driverNames.GetValueOrDefault(driverId, driverId);
        }

        return seasonsInput;
    }

    private static string MapPlayer(string driverId, string pid) =>
        string.Equals(driverId, pid, StringComparison.Ordinal) ? "player" : driverId;

    /// <summary>A generous single-round ceiling (win + fastest lap + sprint + best alternate
    /// table) — generous keeps the clinch bound conservative: a title is only called when the
    /// gap exceeds even this ceiling times the remaining rounds.</summary>
    private static double MaxPointsPerRound(PointsSystem system)
    {
        double best = system.RacePoints.Count > 0 ? system.RacePoints[0].ToDouble() : 0;
        if (system.AlternateRaceTables is { } tables)
        {
            foreach (var table in tables.Values)
            {
                if (table.Count > 0)
                {
                    best = Math.Max(best, table[0].ToDouble());
                }
            }
        }
        if (system.SprintPoints is { Count: > 0 } sprint)
        {
            best += sprint[0].ToDouble();
        }
        if (system.FastestLap is { } fl)
        {
            best += fl.Points.ToDouble();
        }
        return best;
    }

    private static IReadOnlyList<NewsroomSeasonNote> BuildSeasonNotes(
        IReadOnlyList<JournalRow> journalRows,
        IReadOnlyDictionary<string, string> driverNames,
        IReadOnlyDictionary<string, PackTeam> teamsById)
    {
        var notes = new List<NewsroomSeasonNote>();
        foreach (var row in journalRows)
        {
            if (row.Round is not null)
            {
                continue;
            }

            switch (row.Phase)
            {
                case JournalPhases.TeamTier when row.Cause is "promoted" or "relegated":
                    notes.Add(new NewsroomSeasonNote
                    {
                        Kind = row.Cause == "promoted"
                            ? NewsroomSeasonNoteKind.TeamPromoted
                            : NewsroomSeasonNoteKind.TeamRelegated,
                        SubjectId = row.Entity,
                        SubjectName = teamsById.TryGetValue(row.Entity, out var team) ? team.Name : row.Entity,
                    });
                    break;

                case JournalPhases.Retirement:
                    notes.Add(new NewsroomSeasonNote
                    {
                        Kind = NewsroomSeasonNoteKind.DriverRetired,
                        SubjectId = row.Entity,
                        SubjectName = driverNames.GetValueOrDefault(row.Entity, row.Entity),
                        Detail = row.Cause,
                        Value = ReadIntProperty(row.DeltaJson, "age"),
                    });
                    break;

                case JournalPhases.RetirementForeshadow:
                    notes.Add(new NewsroomSeasonNote
                    {
                        Kind = NewsroomSeasonNoteKind.RetirementConsidered,
                        SubjectId = row.Entity,
                        SubjectName = driverNames.GetValueOrDefault(row.Entity, row.Entity),
                        Value = ReadIntProperty(row.DeltaJson, "age"),
                    });
                    break;

                case JournalPhases.SeatMarket when row.Cause is "vacancy-filled" or "vacancy-unfilled":
                {
                    string hired = ReadStringProperty(row.DeltaJson, "hired");
                    string vacated = ReadStringProperty(row.DeltaJson, "vacatedBy");
                    bool filled = row.Cause == "vacancy-filled" && hired.Length > 0;
                    string subject = filled ? hired : vacated;
                    notes.Add(new NewsroomSeasonNote
                    {
                        Kind = filled ? NewsroomSeasonNoteKind.SeatFilled : NewsroomSeasonNoteKind.SeatVacancy,
                        SubjectId = subject,
                        SubjectName = driverNames.GetValueOrDefault(subject, subject),
                        Detail = teamsById.TryGetValue(row.Entity, out var seatTeam) ? seatTeam.Name : row.Entity,
                    });
                    break;
                }

                case JournalPhases.OfferExtended:
                    notes.Add(new NewsroomSeasonNote
                    {
                        Kind = NewsroomSeasonNoteKind.OfferReceived,
                        SubjectName = teamsById.TryGetValue(row.Entity, out var offerTeam) ? offerTeam.Name : row.Entity,
                        Value = ReadIntProperty(row.DeltaJson, "tier"),
                    });
                    break;
            }
        }

        return notes;
    }

    private static int ReadIntProperty(string deltaJson, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(deltaJson);
            return doc.RootElement.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt32()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static string ReadStringProperty(string deltaJson, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(deltaJson);
            return doc.RootElement.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? ""
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
