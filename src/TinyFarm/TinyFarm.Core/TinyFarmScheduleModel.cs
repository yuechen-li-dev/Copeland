using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public readonly record struct TinyFarmScheduleDay
{
    private TinyFarmScheduleDay(int? specificDay)
    {
        SpecificDay = specificDay;
    }

    public int? SpecificDay { get; }

    public bool IsEveryDay => SpecificDay is null;

    public static TinyFarmScheduleDay EveryDay { get; } = new(null);

    public static TinyFarmScheduleDay Day(int day)
    {
        if (day is < 1 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(day), day, "A TinyFarm schedule day must be from 1 through 7.");
        }

        return new TinyFarmScheduleDay(day);
    }

    public bool Matches(int day)
    {
        return SpecificDay is null || SpecificDay == day;
    }

    public override string ToString()
    {
        return SpecificDay is int day ? $"Day({day})" : "Every";
    }
}

public sealed record TinyFarmScheduleWindow(
    string Id,
    ActorId Actor,
    TinyFarmScheduleDay Day,
    int StartMinute,
    int EndMinuteExclusive,
    TinyFarmScheduleRegime Regime,
    SceneAnchorId? RequiredAnchor,
    int Priority,
    string Reason)
{
    public TinyFarmScheduleWindow(
        ActorId actor,
        TinyFarmScheduleDay day,
        int startMinute,
        int endMinuteExclusive,
        SceneAnchorId anchor,
        int priority,
        string reason)
        : this(
            $"{actor.Value}.{day}.{startMinute}.{endMinuteExclusive}.{reason}",
            actor,
            day,
            startMinute,
            endMinuteExclusive,
            TinyFarmScheduleRegime.Required,
            anchor,
            priority,
            reason)
    {
    }

    [JsonIgnore]
    public SceneAnchorId Anchor
    {
        get => RequiredAnchor
            ?? throw new InvalidOperationException($"Open schedule window '{Id}' has no required anchor.");
        init => RequiredAnchor = value;
    }
}

public enum TinyFarmScheduleRegime
{
    Required,
    Open
}

public sealed record TinyFarmUtilityCandidate(
    string WindowId,
    SceneAnchorId Anchor,
    string ConsiderationKind,
    double BaseScore,
    double CurrentLocationBonus);

public sealed record ScheduleContentProvenance(
    string Format,
    string FileName,
    string SourceSha256,
    long ByteLength,
    string AggregateSha256,
    double ReadMilliseconds,
    double ParseMilliseconds,
    double MaterializeMilliseconds,
    double SemanticValidationMilliseconds,
    double IndexBuildMilliseconds);

public sealed class TinyFarmScheduleCatalog
{
    private const int MinutesPerDay = 1440;
    private readonly IReadOnlyDictionary<ActorId, IReadOnlyList<TinyFarmScheduleWindow>> byActor;

    internal TinyFarmScheduleCatalog(
        IEnumerable<TinyFarmScheduleWindow> windows,
        IEnumerable<TinyFarmUtilityCandidate>? candidates = null)
    {
        Windows = windows
            .OrderBy(window => window.Actor.Value, StringComparer.Ordinal)
            .ThenBy(window => window.Day.IsEveryDay ? 0 : 1)
            .ThenBy(window => window.Day.SpecificDay ?? 0)
            .ThenBy(window => window.StartMinute)
            .ThenBy(window => window.EndMinuteExclusive)
            .ThenBy(window => window.Priority)
            .ThenBy(window => window.Regime)
            .ThenBy(window => window.RequiredAnchor?.Value, StringComparer.Ordinal)
            .ThenBy(window => window.Id, StringComparer.Ordinal)
            .ThenBy(window => window.Reason, StringComparer.Ordinal)
            .ToArray();

        Candidates = (candidates ?? [])
            .OrderBy(candidate => candidate.WindowId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Anchor.Value, StringComparer.Ordinal)
            .ToArray();

        byActor = new ReadOnlyDictionary<ActorId, IReadOnlyList<TinyFarmScheduleWindow>>(
            Windows
                .GroupBy(window => window.Actor)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<TinyFarmScheduleWindow>)group.ToArray()));
    }

    public IReadOnlyList<TinyFarmScheduleWindow> Windows { get; }

    public IReadOnlyList<TinyFarmUtilityCandidate> Candidates { get; }

    public IReadOnlyList<TinyFarmScheduleWindow> ForActor(ActorId actor)
    {
        if (!byActor.TryGetValue(actor, out IReadOnlyList<TinyFarmScheduleWindow>? windows))
        {
            throw new KeyNotFoundException($"No TinyFarm NPC schedule is registered for actor '{actor}'.");
        }

        return windows;
    }

    public IReadOnlyList<TinyFarmUtilityCandidate> CandidatesFor(TinyFarmScheduleWindow window)
    {
        return Candidates.Where(candidate => candidate.WindowId == window.Id).ToArray();
    }

    internal static void Validate(
        IReadOnlyList<TinyFarmScheduleWindow> windows,
        IReadOnlyList<TinyFarmUtilityCandidate> candidates,
        IReadOnlySet<ActorId> knownActors,
        TinyFarmSceneCatalog scenes)
    {
        if (windows.Count == 0)
        {
            throw new InvalidDataException("TinyFarm schedule content must contain at least one row.");
        }

        foreach (TinyFarmScheduleWindow window in windows)
        {
            if (string.IsNullOrWhiteSpace(window.Id))
            {
                throw new InvalidDataException("Schedule rows require a stable window ID.");
            }

            if (!knownActors.Contains(window.Actor))
            {
                throw new InvalidDataException($"Schedule row references unknown actor '{window.Actor}'.");
            }

            if (window.Regime == TinyFarmScheduleRegime.Required && window.RequiredAnchor is null)
            {
                throw new InvalidDataException($"Required schedule window '{window.Id}' requires an anchor.");
            }
            if (window.Regime == TinyFarmScheduleRegime.Open && window.RequiredAnchor is not null)
            {
                throw new InvalidDataException($"Open schedule window '{window.Id}' cannot specify a required anchor.");
            }
            if (window.RequiredAnchor is SceneAnchorId requiredAnchor)
            {
                RequireKnownAnchor(scenes, requiredAnchor, $"Schedule window '{window.Id}'");
            }

            if (window.StartMinute is < 0 or >= MinutesPerDay)
            {
                throw new InvalidDataException(
                    $"Schedule row for '{window.Actor}' has invalid start minute {window.StartMinute}.");
            }

            if (window.EndMinuteExclusive is <= 0 or > MinutesPerDay
                || window.StartMinute >= window.EndMinuteExclusive)
            {
                throw new InvalidDataException(
                    $"Schedule row for '{window.Actor}' has invalid half-open interval [{window.StartMinute}, {window.EndMinuteExclusive}).");
            }

            if (window.Priority < 0)
            {
                throw new InvalidDataException(
                    $"Schedule row for '{window.Actor}' has invalid negative priority {window.Priority}.");
            }

            if (string.IsNullOrWhiteSpace(window.Reason))
            {
                throw new InvalidDataException($"Schedule row for '{window.Actor}' requires a reason.");
            }
        }

        if (windows.Select(window => window.Id).Distinct(StringComparer.Ordinal).Count() != windows.Count)
        {
            throw new InvalidDataException("Schedule window IDs must be unique.");
        }

        foreach (TinyFarmUtilityCandidate candidate in candidates)
        {
            TinyFarmScheduleWindow? window = windows.SingleOrDefault(item => item.Id == candidate.WindowId);
            if (window is null || window.Regime != TinyFarmScheduleRegime.Open)
            {
                throw new InvalidDataException($"Utility candidate references unknown or non-Open window '{candidate.WindowId}'.");
            }
            RequireKnownAnchor(scenes, candidate.Anchor, $"Utility candidate for '{candidate.WindowId}'");
            if (candidate.Anchor != TinyFarmAnchorIds.FarmHome
                && candidate.Anchor != TinyFarmAnchorIds.FarmWorkArea
                && candidate.Anchor != TinyFarmAnchorIds.TownSquare
                && candidate.Anchor != TinyFarmAnchorIds.StoreCounter
                && candidate.Anchor != TinyFarmAnchorIds.RiversideMeetingPoint)
            {
                throw new InvalidDataException(
                    $"Utility candidate anchor '{candidate.Anchor}' has no Dominatus schedule option.");
            }
            if (candidate.ConsiderationKind != "current-location-stickiness")
            {
                throw new InvalidDataException($"Utility candidate for '{candidate.WindowId}' has unknown consideration kind '{candidate.ConsiderationKind}'.");
            }
            if (!double.IsFinite(candidate.BaseScore)
                || !double.IsFinite(candidate.CurrentLocationBonus)
                || candidate.BaseScore <= 0d
                || candidate.CurrentLocationBonus < 0d
                || candidate.BaseScore + candidate.CurrentLocationBonus > 1d)
            {
                throw new InvalidDataException(
                    $"Utility candidate for '{candidate.WindowId}' requires a positive base score and a total score in (0, 1].");
            }
        }

        foreach (TinyFarmScheduleWindow open in windows.Where(window => window.Regime == TinyFarmScheduleRegime.Open))
        {
            TinyFarmUtilityCandidate[] openCandidates = candidates.Where(candidate => candidate.WindowId == open.Id).ToArray();
            if (openCandidates.Length == 0)
            {
                throw new InvalidDataException($"Open schedule window '{open.Id}' requires at least one utility candidate.");
            }
            if (openCandidates.Select(candidate => candidate.Anchor).Distinct().Count() != openCandidates.Length)
            {
                throw new InvalidDataException($"Open schedule window '{open.Id}' contains a duplicate semantic candidate.");
            }
        }

        TinyFarmScheduleWindow? duplicate = windows
            .GroupBy(window => window)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Schedule content contains a duplicate semantic row for actor '{duplicate.Actor}'.");
        }

        foreach (ActorId actor in knownActors.OrderBy(value => value.Value, StringComparer.Ordinal))
        {
            TinyFarmScheduleWindow[] actorWindows = windows.Where(window => window.Actor == actor).ToArray();
            if (actorWindows.Length == 0)
            {
                throw new InvalidDataException($"Schedule content has no rows for actor '{actor}'.");
            }

            for (int day = 1; day <= 7; day++)
            {
                for (int minute = 0; minute < MinutesPerDay; minute++)
                {
                    TinyFarmScheduleWindow[] active = actorWindows
                        .Where(window => window.Day.Matches(day)
                            && minute >= window.StartMinute
                            && minute < window.EndMinuteExclusive)
                        .ToArray();
                    if (active.Length == 0)
                    {
                        throw new InvalidDataException(
                            $"Schedule coverage hole for actor '{actor}' on day {day} at minute {minute}.");
                    }

                    int highestPriority = active.Max(window => window.Priority);
                    TinyFarmScheduleWindow[] highest = active
                        .Where(window => window.Priority == highestPriority)
                        .ToArray();
                    if (highest.Select(window => window.Regime).Distinct().Count() != 1
                        || highest.Select(window => window.Id).Distinct(StringComparer.Ordinal).Count() != 1)
                    {
                        throw new InvalidDataException(
                            $"Schedule has a conflicting priority tie for actor '{actor}' on day {day} at minute {minute} and priority {highestPriority}.");
                    }
                }
            }
        }
    }

    private static void RequireKnownAnchor(
        TinyFarmSceneCatalog scenes,
        SceneAnchorId anchor,
        string owner)
    {
        try
        {
            _ = scenes.GetAnchor(anchor);
        }
        catch (KeyNotFoundException exception)
        {
            throw new InvalidDataException($"{owner} references unknown anchor '{anchor}'.", exception);
        }
    }
}
