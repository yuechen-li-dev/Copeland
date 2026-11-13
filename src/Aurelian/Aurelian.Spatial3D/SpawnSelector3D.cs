using System.Numerics;

namespace Aurelian.Spatial3D;

public sealed record SpawnPoint3D(string Id, Vector3 Feet);
public sealed record SpawnUse3D(string Id, double Time);
public sealed record SpawnChoice3D(SpawnPoint3D Point, float Score, float PlayerDistance, bool Visible, float RecentUse);

public sealed record SpawnSelectionSettings3D
{
    public float MinimumPlayerDistance { get; init; } = 6;
    public float PreferredPlayerDistance { get; init; } = 10;
    public float MinimumSeparation { get; init; } = 3;
    public float ReuseSeconds { get; init; } = 20;
    public float VisiblePenalty { get; init; } = .75f;
    public float ReusePenalty { get; init; } = .35f;
    public float Radius { get; init; } = .45f;
    public float Height { get; init; } = 1.6f;
    public float ViewHalfAngleDegrees { get; init; } = 55;

    internal void Validate()
    {
        float[] values = [MinimumPlayerDistance, PreferredPlayerDistance, MinimumSeparation, ReuseSeconds,
            VisiblePenalty, ReusePenalty, Radius, Height, ViewHalfAngleDegrees];
        if (values.Any(value => !float.IsFinite(value)) || MinimumPlayerDistance < 0
            || PreferredPlayerDistance <= MinimumPlayerDistance || MinimumSeparation < 0 || ReuseSeconds < 0
            || VisiblePenalty is < 0 or > 1 || ReusePenalty is < 0 or > 1 || ViewHalfAngleDegrees is <= 0 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(SpawnSelectionSettings3D));
        }
        Capsule3D.AtFeet(Vector3.Zero, Radius, Height).Validate();
    }
}

/// <summary>
/// Deterministic placement proposals over authored candidates. Only RecordUse commits history, after the game accepts a spawn.
/// Collision queries, visibility and bounded recent-use memory remain independent of agents and rendering.
/// </summary>
public sealed class SpawnSelector3D
{
    private readonly Dictionary<string, double> recent = new(StringComparer.Ordinal);
    private readonly SpawnSelectionSettings3D settings;

    public SpawnSelector3D(SpawnSelectionSettings3D? settings = null)
    {
        this.settings = settings ?? new();
        this.settings.Validate();
    }

    public SpawnChoice3D? Select(ISpatialQueryWorld3D world, IReadOnlyList<SpawnPoint3D> candidates,
        Vector3 eye, Vector3 forward, double time, IReadOnlyList<Vector3>? occupied = null, QueryFilter3D? filter = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(candidates);
        new Ray3D(eye, forward, 1).Validate();
        ValidateTime(time);
        if (candidates.Count is < 1 or > 256 || candidates.Any(point => point is null || string.IsNullOrWhiteSpace(point.Id))
            || candidates.Select(point => point.Id).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
        {
            throw new ArgumentException("Supply 1..256 uniquely named spawn points.", nameof(candidates));
        }
        foreach (var point in candidates)
        {
            SpatialMath3D.RequireFinite(point.Feet);
        }
        foreach (var point in occupied ?? [])
        {
            SpatialMath3D.RequireFinite(point);
        }
        SpawnChoice3D? best = null;
        float cone = MathF.Cos(settings.ViewHalfAngleDegrees * MathF.PI / 180);
        foreach (var point in candidates.OrderBy(point => point.Id, StringComparer.Ordinal))
        {
            float distance = new Vector2(point.Feet.X - eye.X, point.Feet.Z - eye.Z).Length();
            if (!float.IsFinite(distance))
            {
                throw new ArgumentException("Spawn distances must be finite.", nameof(candidates));
            }
            if (distance < settings.MinimumPlayerDistance
                || (occupied?.Any(other => Vector3.Distance(other, point.Feet) < settings.MinimumSeparation) ?? false)
                || !world.Overlap(Capsule3D.AtFeet(point.Feet, settings.Radius, settings.Height), filter).IsEmpty)
            {
                continue;
            }
            var ground = world.Raycast(new(point.Feet + Vector3.UnitY * .1f, -Vector3.UnitY, .4f), filter);
            if (ground is null || ground.Value.Normal.Y < .5f)
            {
                continue;
            }
            Vector3 sight = point.Feet + Vector3.UnitY * (settings.Height * .7f) - eye;
            float length = sight.Length();
            bool visible = length > .001f && Vector3.Dot(sight / length, forward) >= cone;
            if (visible)
            {
                var hit = world.Raycast(new(eye, sight / length, length), filter);
                visible = hit is null || hit.Value.Distance >= length - .01f;
            }
            float reuse = recent.TryGetValue(point.Id, out double used) && settings.ReuseSeconds > 0
                ? (float)Math.Clamp(1 - (time - used) / settings.ReuseSeconds, 0, 1) : 0;
            float score = Math.Min(1, distance / settings.PreferredPlayerDistance)
                - (visible ? settings.VisiblePenalty : 0) - reuse * settings.ReusePenalty;
            var choice = new SpawnChoice3D(point, score, distance, visible, reuse);
            if (best is null || choice.Score > best.Score)
            {
                best = choice;
            }
        }
        return best;
    }

    public void RecordUse(string id, double time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ValidateTime(time);
        recent[id] = time;
        if (recent.Count > 256)
        {
            string oldest = recent.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).First().Key;
            recent.Remove(oldest);
        }
    }

    public IReadOnlyList<SpawnUse3D> Capture() => recent.OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => new SpawnUse3D(pair.Key, pair.Value)).ToArray();

    public void Restore(IReadOnlyList<SpawnUse3D> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Count > 256 || snapshot.Any(use => use is null || string.IsNullOrWhiteSpace(use.Id)
            || !double.IsFinite(use.Time) || use.Time < 0)
            || snapshot.Select(use => use.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Count)
        {
            throw new ArgumentException("Invalid spawn history; live history was not changed.", nameof(snapshot));
        }
        recent.Clear();
        foreach (var use in snapshot)
        {
            recent.Add(use.Id, use.Time);
        }
    }

    private void ValidateTime(double time)
    {
        if (!double.IsFinite(time) || time < 0 || recent.Values.Any(previous => previous > time))
        {
            throw new ArgumentOutOfRangeException(nameof(time), "Use simulation time; restore history when rewinding.");
        }
    }
}
