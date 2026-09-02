using System.Collections.ObjectModel;

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
    ActorId Actor,
    TinyFarmScheduleDay Day,
    int StartMinute,
    int EndMinuteExclusive,
    SceneAnchorId Anchor,
    int Priority,
    string Reason);

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

    internal TinyFarmScheduleCatalog(IEnumerable<TinyFarmScheduleWindow> windows)
    {
        Windows = windows
            .OrderBy(window => window.Actor.Value, StringComparer.Ordinal)
            .ThenBy(window => window.Day.IsEveryDay ? 0 : 1)
            .ThenBy(window => window.Day.SpecificDay ?? 0)
            .ThenBy(window => window.StartMinute)
            .ThenBy(window => window.EndMinuteExclusive)
            .ThenBy(window => window.Priority)
            .ThenBy(window => window.Anchor.Value, StringComparer.Ordinal)
            .ThenBy(window => window.Reason, StringComparer.Ordinal)
            .ToArray();

        byActor = new ReadOnlyDictionary<ActorId, IReadOnlyList<TinyFarmScheduleWindow>>(
            Windows
                .GroupBy(window => window.Actor)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<TinyFarmScheduleWindow>)group.ToArray()));
    }

    public IReadOnlyList<TinyFarmScheduleWindow> Windows { get; }

    public IReadOnlyList<TinyFarmScheduleWindow> ForActor(ActorId actor)
    {
        if (!byActor.TryGetValue(actor, out IReadOnlyList<TinyFarmScheduleWindow>? windows))
        {
            throw new KeyNotFoundException($"No TinyFarm NPC schedule is registered for actor '{actor}'.");
        }

        return windows;
    }

    internal static void Validate(
        IReadOnlyList<TinyFarmScheduleWindow> windows,
        IReadOnlySet<ActorId> knownActors,
        TinyFarmSceneCatalog scenes)
    {
        if (windows.Count == 0)
        {
            throw new InvalidDataException("TinyFarm schedule content must contain at least one row.");
        }

        foreach (TinyFarmScheduleWindow window in windows)
        {
            if (!knownActors.Contains(window.Actor))
            {
                throw new InvalidDataException($"Schedule row references unknown actor '{window.Actor}'.");
            }

            try
            {
                _ = scenes.GetAnchor(window.Anchor);
            }
            catch (KeyNotFoundException exception)
            {
                throw new InvalidDataException($"Schedule row references unknown anchor '{window.Anchor}'.", exception);
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
                    SceneAnchorId[] highestAnchors = active
                        .Where(window => window.Priority == highestPriority)
                        .Select(window => window.Anchor)
                        .Distinct()
                        .ToArray();
                    if (highestAnchors.Length != 1)
                    {
                        throw new InvalidDataException(
                            $"Schedule has a conflicting priority tie for actor '{actor}' on day {day} at minute {minute} and priority {highestPriority}.");
                    }
                }
            }
        }
    }
}
