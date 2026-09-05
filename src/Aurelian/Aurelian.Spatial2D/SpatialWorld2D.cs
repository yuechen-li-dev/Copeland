namespace Aurelian.Spatial2D;

public sealed class SpatialWorld2D
{
    private readonly SpatialCollider2D[] staticColliders;
    private readonly SpatialTrigger2D[] triggers;

    public SpatialWorld2D(
        IEnumerable<SpatialCollider2D>? staticColliders = null,
        IEnumerable<SpatialTrigger2D>? triggers = null)
    {
        this.staticColliders = (staticColliders ?? [])
            .OrderBy(collider => collider.Id)
            .ToArray();
        this.triggers = (triggers ?? [])
            .OrderBy(trigger => trigger.Id)
            .ToArray();
        Validate();
    }

    public IReadOnlyList<SpatialCollider2D> StaticColliders => staticColliders;

    public IReadOnlyList<SpatialTrigger2D> Triggers => triggers;

    public IReadOnlyList<SpatialOverlap2D> Overlap(
        SpatialShape2D shape,
        SpatialQueryFilter? filter = null,
        IEnumerable<SpatialCollider2D>? transientColliders = null)
    {
        shape.Validate();
        SpatialQueryFilter actualFilter = filter ?? SpatialQueryFilter.All;
        return EnumerateColliders(transientColliders)
            .Where(collider => Matches(collider.Layer, collider.Mask, actualFilter)
                && SpatialGeometry2D.Overlaps(shape, collider.Shape))
            .Select(collider => new SpatialOverlap2D(
                collider.Id,
                (shape.Center - collider.Shape.Center).LengthSquared,
                collider.SemanticOwnerId))
            .OrderBy(overlap => overlap.SquaredCenterDistance)
            .ThenBy(overlap => overlap.ColliderId)
            .ToArray();
    }

    public IReadOnlyList<SpatialOverlap2D> PointQuery(
        SpatialPoint2D point,
        SpatialQueryFilter? filter = null,
        IEnumerable<SpatialCollider2D>? transientColliders = null)
    {
        point.Validate();
        SpatialQueryFilter actualFilter = filter ?? SpatialQueryFilter.All;
        return EnumerateColliders(transientColliders)
            .Where(collider => Matches(collider.Layer, collider.Mask, actualFilter)
                && SpatialGeometry2D.Contains(collider.Shape, point))
            .Select(collider => new SpatialOverlap2D(
                collider.Id,
                (point - collider.Shape.Center).LengthSquared,
                collider.SemanticOwnerId))
            .OrderBy(overlap => overlap.SquaredCenterDistance)
            .ThenBy(overlap => overlap.ColliderId)
            .ToArray();
    }

    public IReadOnlyList<SpatialTriggerOverlap2D> OverlapTriggers(
        SpatialShape2D shape,
        SpatialQueryFilter? filter = null)
    {
        shape.Validate();
        SpatialQueryFilter actualFilter = filter ?? SpatialQueryFilter.All;
        return triggers
            .Where(trigger => Matches(trigger.Layer, trigger.Mask, actualFilter)
                && SpatialGeometry2D.Overlaps(shape, trigger.Shape))
            .Select(trigger => new SpatialTriggerOverlap2D(
                trigger.Id,
                (shape.Center - trigger.Shape.Center).LengthSquared,
                trigger.SemanticOwnerId))
            .OrderBy(overlap => overlap.SquaredCenterDistance)
            .ThenBy(overlap => overlap.TriggerId)
            .ToArray();
    }

    public SpatialHit2D? Sweep(
        SpatialShape2D shape,
        SpatialVector2D displacement,
        SpatialQueryFilter? filter = null,
        IEnumerable<SpatialCollider2D>? transientColliders = null)
    {
        shape.Validate();
        displacement.Validate();
        if (displacement.LengthSquared <= SpatialMath2D.Epsilon * SpatialMath2D.Epsilon)
        {
            return null;
        }

        SpatialQueryFilter actualFilter = filter ?? SpatialQueryFilter.All;
        SpatialHit2D? first = null;
        foreach (SpatialCollider2D collider in EnumerateColliders(transientColliders))
        {
            if (!Matches(collider.Layer, collider.Mask, actualFilter))
            {
                continue;
            }
            SpatialHit2D? candidate = SpatialGeometry2D.Sweep(shape, displacement, collider);
            if (candidate is null
                || first is not null
                    && CompareHits(candidate.Value, first.Value) >= 0)
            {
                continue;
            }
            first = candidate.Value;
        }
        return first;
    }

    public SpatialMoveResult SweepAndSlide(
        SpatialShape2D shape,
        SpatialVector2D displacement,
        int maximumIterations = 4,
        SpatialQueryFilter? filter = null,
        IEnumerable<SpatialCollider2D>? transientColliders = null)
    {
        shape.Validate();
        displacement.Validate();
        if (maximumIterations < 1 || maximumIterations > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumIterations), "Iteration count must be between 1 and 16.");
        }
        if (displacement.LengthSquared <= SpatialMath2D.Epsilon * SpatialMath2D.Epsilon)
        {
            return new SpatialMoveResult(displacement, SpatialVector2D.Zero, [], false, 0);
        }

        SpatialShape2D current = shape;
        SpatialVector2D remaining = displacement;
        SpatialVector2D accepted = SpatialVector2D.Zero;
        var contacts = new List<SpatialHit2D>();
        for (int iteration = 0; iteration < maximumIterations; iteration++)
        {
            IReadOnlyList<SpatialHit2D> hits = SweepContacts(current, remaining, filter, transientColliders);
            if (hits.Count == 0)
            {
                accepted += remaining;
                return new SpatialMoveResult(displacement, accepted, contacts, false, iteration + 1);
            }
            if (hits[0].InitiallyOverlapping)
            {
                contacts.AddRange(hits.Where(hit => hit.InitiallyOverlapping));
                return new SpatialMoveResult(displacement, accepted, contacts, true, iteration + 1);
            }

            double firstTime = hits[0].TimeOfImpact;
            SpatialHit2D[] simultaneous = hits
                .Where(hit => Math.Abs(hit.TimeOfImpact - firstTime) <= SpatialMath2D.Epsilon)
                .OrderBy(hit => hit.ColliderId)
                .ToArray();
            contacts.AddRange(simultaneous);
            SpatialVector2D advance = remaining * firstTime;
            accepted += advance;
            current = current.Translate(advance);
            remaining *= 1 - firstTime;
            foreach (SpatialHit2D hit in simultaneous)
            {
                double blocked = SpatialVector2D.Dot(remaining, hit.Normal);
                if (blocked < 0)
                {
                    remaining -= hit.Normal * blocked;
                }
            }
            if (remaining.LengthSquared <= SpatialMath2D.Epsilon * SpatialMath2D.Epsilon)
            {
                return new SpatialMoveResult(displacement, accepted, contacts, false, iteration + 1);
            }
        }
        return new SpatialMoveResult(displacement, accepted, contacts, false, maximumIterations);
    }

    public IReadOnlyList<SpatialDebugFact2D> DebugFacts()
    {
        return staticColliders
            .Select(collider => new SpatialDebugFact2D(
                SpatialDebugFactKind.Solid,
                collider.Id.Value,
                collider.Shape))
            .Concat(triggers.Select(trigger => new SpatialDebugFact2D(
                SpatialDebugFactKind.Trigger,
                trigger.Id.Value,
                trigger.Shape)))
            .ToArray();
    }

    public static TriggerTransition2D DiffTriggers(
        IEnumerable<SpatialTriggerId> previous,
        IEnumerable<SpatialTriggerId> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        SpatialTriggerId[] oldIds = previous.Distinct().Order().ToArray();
        SpatialTriggerId[] newIds = current.Distinct().Order().ToArray();
        return new TriggerTransition2D(
            newIds.Except(oldIds).Order().ToArray(),
            newIds.Intersect(oldIds).Order().ToArray(),
            oldIds.Except(newIds).Order().ToArray());
    }

    private IReadOnlyList<SpatialHit2D> SweepContacts(
        SpatialShape2D shape,
        SpatialVector2D displacement,
        SpatialQueryFilter? filter,
        IEnumerable<SpatialCollider2D>? transientColliders)
    {
        shape.Validate();
        displacement.Validate();
        if (displacement.LengthSquared <= SpatialMath2D.Epsilon * SpatialMath2D.Epsilon)
        {
            return [];
        }
        SpatialQueryFilter actualFilter = filter ?? SpatialQueryFilter.All;
        return EnumerateColliders(transientColliders)
            .Where(collider => Matches(collider.Layer, collider.Mask, actualFilter))
            .Select(collider => SpatialGeometry2D.Sweep(shape, displacement, collider))
            .Where(hit => hit is not null)
            .Select(hit => hit!.Value)
            .OrderBy(hit => hit.TimeOfImpact)
            .ThenBy(hit => hit.ColliderId)
            .ToArray();
    }

    private IEnumerable<SpatialCollider2D> EnumerateColliders(
        IEnumerable<SpatialCollider2D>? transientColliders)
    {
        if (transientColliders is null)
        {
            return staticColliders;
        }
        return staticColliders
            .Concat(transientColliders)
            .OrderBy(collider => collider.Id);
    }

    private static bool Matches(
        SpatialLayerMask layer,
        SpatialLayerMask colliderMask,
        SpatialQueryFilter filter)
    {
        return (layer & filter.IncludedLayers) != 0
            && (colliderMask & filter.QueryLayer) != 0;
    }

    private static int CompareHits(SpatialHit2D left, SpatialHit2D right)
    {
        double delta = left.TimeOfImpact - right.TimeOfImpact;
        if (Math.Abs(delta) > SpatialMath2D.Epsilon)
        {
            return delta < 0 ? -1 : 1;
        }
        return left.ColliderId.CompareTo(right.ColliderId);
    }

    private void Validate()
    {
        var colliderIds = new HashSet<SpatialColliderId>();
        foreach (SpatialCollider2D collider in staticColliders)
        {
            if (string.IsNullOrWhiteSpace(collider.Id.Value) || !colliderIds.Add(collider.Id))
            {
                throw new ArgumentException("Static collider IDs must be non-empty and unique.");
            }
            if (collider.Layer == SpatialLayerMask.None)
            {
                throw new ArgumentException($"Static collider '{collider.Id}' must have a layer.");
            }
            collider.Shape.Validate();
        }

        var triggerIds = new HashSet<SpatialTriggerId>();
        foreach (SpatialTrigger2D trigger in triggers)
        {
            if (string.IsNullOrWhiteSpace(trigger.Id.Value) || !triggerIds.Add(trigger.Id))
            {
                throw new ArgumentException("Trigger IDs must be non-empty and unique.");
            }
            if (trigger.Layer == SpatialLayerMask.None)
            {
                throw new ArgumentException($"Trigger '{trigger.Id}' must have a layer.");
            }
            trigger.Shape.Validate();
        }
    }
}

public static class SpatialWorldAuthoring2D
{
    public static Aabb2 TileRectangle(
        int x,
        int y,
        int width,
        int height,
        double worldUnitsPerTile)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Tile rectangle dimensions must be positive.");
        }
        SpatialMath2D.RequireFinite(worldUnitsPerTile, nameof(worldUnitsPerTile));
        if (worldUnitsPerTile <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldUnitsPerTile), "World units per tile must be positive.");
        }
        double worldWidth = width * worldUnitsPerTile;
        double worldHeight = height * worldUnitsPerTile;
        return new Aabb2(
            new SpatialPoint2D(
                (x * worldUnitsPerTile) + (worldWidth / 2),
                (y * worldUnitsPerTile) + (worldHeight / 2)),
            new SpatialVector2D(worldWidth / 2, worldHeight / 2));
    }
}

public static class SpatialDebugFacts2D
{
    public static IReadOnlyList<SpatialDebugFact2D> ForSweep(
        string sweepId,
        SpatialShape2D shape,
        SpatialVector2D displacement,
        SpatialMoveResult result)
    {
        if (string.IsNullOrWhiteSpace(sweepId))
        {
            throw new ArgumentException("Sweep debug identity must not be empty.", nameof(sweepId));
        }
        shape.Validate();
        displacement.Validate();
        ArgumentNullException.ThrowIfNull(result);
        var facts = new List<SpatialDebugFact2D>(result.Contacts.Count + 1)
        {
            new(
                SpatialDebugFactKind.Sweep,
                sweepId,
                Shape: shape,
                Start: shape.Center,
                End: shape.Center + displacement)
        };
        facts.AddRange(result.Contacts.Select(contact => new SpatialDebugFact2D(
            SpatialDebugFactKind.Contact,
            contact.ColliderId.Value,
            ContactPoint: contact.ContactPoint,
            Normal: contact.Normal)));
        return facts;
    }
}
