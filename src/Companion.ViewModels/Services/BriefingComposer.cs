using System.Globalization;
using Companion.Ams2.ContentLibrary;
using Companion.Core.Packs;

namespace Companion.ViewModels.Services;

/// <summary>
/// Pure composition of the Race Day briefing (app-shell contract): every in-game setting as an
/// exact copy-ready string, grouped into the AMS2 custom-race screen's sections — Event
/// (track/class/opponents), then each SESSION (Practice, Qualifying, Race) with its own timed
/// length + up to four independent weather slots, then Rules (mandatory pit stop, refuelling).
/// Per-session weather comes from <see cref="PackWeekend"/>, falling back to the round-level
/// <see cref="PackSessionSettings.WeatherSlots"/> for un-migrated packs. Placeholder rounds keep the
/// REAL venue as the display name and are titled "&lt;GP name&gt; — placeholder: &lt;track&gt;"; the
/// distance note travels in <see cref="BriefingModel.SetupNotes"/> (authored in setupGuide.notes),
/// and the fuel advisory in <see cref="BriefingModel.FuelNote"/>. All display-only — no fold input.
/// </summary>
public static class BriefingComposer
{
    public const string TrackLabel = "Track";

    // Race-Day sections, in AMS2 custom-race screen order (also the checklist render + copy order).
    private const string Event = "Event";
    private const string Practice = "Practice";
    private const string Qualifying = "Qualifying";
    private const string Race = "Race";
    private const string Rules = "Rules";

    public static BriefingModel Compose(SeasonPack pack, PackRound round, Ams2ContentLibrary library)
    {
        string trackName = InGameTrackName(round, library);
        var session = round.SetupGuide?.Session;
        var weekend = round.Weekend;

        var settings = new List<CopyableSetting>
        {
            new(TrackLabel, trackName) { Section = Event },
            new("Class", pack.Season.Ams2Class) { Section = Event },
        };
        if (session is not null)
            settings.Add(new("Opponents", session.Opponents.ToString(CultureInfo.InvariantCulture)) { Section = Event });

        // Practice / Qualifying: timed length + this session's weather. AMS2's practice and qualifying
        // are always time-limited (qualifying can never be lap-based). Only rendered when the pack
        // authors at least one detail, so an un-migrated pack shows no empty session block.
        AddSession(settings, Practice, weekend?.Practice);
        AddSession(settings, Qualifying, weekend?.Qualifying);

        // Race: laps (the only lap-based session), its weather (per-session, else round-level), then
        // the shared date / start time / time progression.
        settings.Add(new("Laps", round.Laps.ToString(CultureInfo.InvariantCulture)) { Section = Race });
        AddWeather(settings, Race, weekend?.Races.FirstOrDefault()?.WeatherSlots ?? session?.WeatherSlots);
        settings.Add(new("Date", session?.Date ?? round.Date) { Section = Race });
        if (session?.StartTime is { Length: > 0 } startTime)
            settings.Add(new("Start time", startTime) { Section = Race });
        if (session?.TimeProgression is { Length: > 0 } timeProgression)
            settings.Add(new("Time progression", timeProgression) { Section = Race });

        // Rules: mandatory pit stop (when a setup guide exists) + refuelling (only when the season
        // declares it — hidden on packs that don't yet author the flag).
        if (session is not null)
            settings.Add(new("Mandatory pit stop", session.MandatoryPitStop ? "Yes" : "No") { Section = Rules });
        if (pack.Season.RefuellingAllowed is { } refuellingAllowed)
            settings.Add(new("Refuelling", refuellingAllowed ? "Yes" : "No") { Section = Rules });

        return new BriefingModel
        {
            Round = round,
            VenueDisplayName = round.Track.RealVenue is { Length: > 0 } venue ? venue : trackName,
            IsPlaceholder = round.Track.IsPlaceholder,
            Settings = settings,
            SetupNotes = string.IsNullOrWhiteSpace(round.SetupGuide?.Notes) ? null : round.SetupGuide!.Notes,
            FuelNote = FuelGuidance.Note(pack.Season.Ams2Class, round.Laps, pack.Season.RefuellingAllowed),
        };
    }

    /// <summary>Adds a practice/qualifying section: its timed length (when authored) then each of its
    /// weather slots. Adds nothing (so the section never appears) when the session is absent, not
    /// present, or authors neither a duration nor weather.</summary>
    private static void AddSession(List<CopyableSetting> settings, string section, PackWeekendSession? session)
    {
        if (session is not { Present: true })
            return;

        if (session.DurationMinutes is { } minutes)
            settings.Add(new("Duration", $"{minutes.ToString(CultureInfo.InvariantCulture)} min") { Section = section });

        AddWeather(settings, section, session.WeatherSlots);
    }

    /// <summary>Adds one "Weather slot N" row per authored slot to <paramref name="section"/>; a null
    /// or empty list adds nothing.</summary>
    private static void AddWeather(List<CopyableSetting> settings, string section, IReadOnlyList<string>? slots)
    {
        if (slots is null)
            return;

        for (int i = 0; i < slots.Count; i++)
            settings.Add(new($"Weather slot {i + 1}", slots[i]) { Section = section });
    }

    /// <summary>Screen title per the contract: placeholder rounds carry the explicit
    /// "&lt;GP name&gt; — placeholder: &lt;track&gt;" labeling.</summary>
    public static string ComposeTitle(BriefingModel briefing)
    {
        if (!briefing.IsPlaceholder)
            return briefing.Round.Name;

        string track = briefing.Settings.FirstOrDefault(s => s.Label == TrackLabel)?.Value
            ?? briefing.Round.Track.Id;
        return $"{briefing.Round.Name} — placeholder: {track}";
    }

    private static string InGameTrackName(PackRound round, Ams2ContentLibrary library) =>
        library.Tracks.TryGetValue(round.Track.Id, out var track)
            ? track.TrackName ?? track.Id
            : round.Track.Id;
}
