using System.Globalization;
using Companion.Ams2.ContentLibrary;
using Companion.Core.Packs;

namespace Companion.ViewModels.Services;

/// <summary>
/// Pure composition of the Race Day briefing (app-shell contract): every in-game setting as
/// an exact copy-ready string, in the fixed contract order — Track, Class, Laps, Date,
/// Start time, Weather slots, Opponents, Time progression, Mandatory pit stop. Placeholder
/// rounds keep the REAL venue as the display name and are titled
/// "&lt;GP name&gt; — placeholder: &lt;track&gt;"; the distance note travels in
/// <see cref="Companion.ViewModels.Services.BriefingModel.SetupNotes"/> (authored in
/// setupGuide.notes).
/// </summary>
public static class BriefingComposer
{
    public const string TrackLabel = "Track";

    public static BriefingModel Compose(SeasonPack pack, PackRound round, Ams2ContentLibrary library)
    {
        string trackName = InGameTrackName(round, library);

        var settings = new List<CopyableSetting>
        {
            new(TrackLabel, trackName),
            new("Class", pack.Season.Ams2Class),
            new("Laps", round.Laps.ToString(CultureInfo.InvariantCulture)),
        };

        var session = round.SetupGuide?.Session;
        settings.Add(new CopyableSetting("Date", session?.Date ?? round.Date));

        if (session is not null)
        {
            if (session.StartTime is { Length: > 0 } startTime)
                settings.Add(new CopyableSetting("Start time", startTime));

            for (int i = 0; i < session.WeatherSlots.Count; i++)
                settings.Add(new CopyableSetting($"Weather slot {i + 1}", session.WeatherSlots[i]));

            settings.Add(new CopyableSetting(
                "Opponents", session.Opponents.ToString(CultureInfo.InvariantCulture)));

            if (session.TimeProgression is { Length: > 0 } timeProgression)
                settings.Add(new CopyableSetting("Time progression", timeProgression));

            settings.Add(new CopyableSetting("Mandatory pit stop", session.MandatoryPitStop ? "Yes" : "No"));
        }

        return new BriefingModel
        {
            Round = round,
            VenueDisplayName = round.Track.RealVenue is { Length: > 0 } venue ? venue : trackName,
            IsPlaceholder = round.Track.IsPlaceholder,
            Settings = settings,
            SetupNotes = string.IsNullOrWhiteSpace(round.SetupGuide?.Notes) ? null : round.SetupGuide!.Notes,
        };
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
