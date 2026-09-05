using Xunit;

namespace Aurelian.Spatial2D.Tests;

public sealed class SpatialWorld2DTests
{
    [Fact]
    public void AabbOverlapReturnsStableDistanceThenIdOrder()
    {
        SpatialWorld2D world = World(
            BoxCollider("far", 3, 0, 2, 2),
            BoxCollider("z-near", 1, 0, 2, 2),
            BoxCollider("a-near", -1, 0, 2, 2));

        IReadOnlyList<SpatialOverlap2D> hits = world.Overlap(Box(0, 0, 2, 2));

        Assert.Equal(["a-near", "z-near"], hits.Select(hit => hit.ColliderId.Value));
    }

    [Fact]
    public void CircleCircleOverlapQualifiesTouching()
    {
        SpatialWorld2D world = World(CircleCollider("circle", 2, 0, 1));

        SpatialOverlap2D hit = Assert.Single(world.Overlap(new Circle2(Point(0, 0), 1)));

        Assert.Equal("circle", hit.ColliderId.Value);
    }

    [Fact]
    public void CircleAabbOverlapHandlesCornerDistance()
    {
        SpatialWorld2D world = World(BoxCollider("box", 2, 2, 2, 2));

        Assert.Empty(world.Overlap(new Circle2(Point(0, 0), 1.4)));
        Assert.Single(world.Overlap(new Circle2(Point(0, 0), Math.Sqrt(2))));
    }

    [Fact]
    public void PointQueryUsesStableOrdering()
    {
        SpatialWorld2D world = World(
            BoxCollider("b", 0, 0, 4, 4),
            CircleCollider("a", 0, 0, 3));

        Assert.Equal(
            ["a", "b"],
            world.PointQuery(Point(0, 0)).Select(hit => hit.ColliderId.Value));
    }

    [Fact]
    public void AabbSweepStopsAtFirstContact()
    {
        SpatialWorld2D world = World(BoxCollider("wall", 5, 0, 2, 10));

        SpatialHit2D hit = Assert.IsType<SpatialHit2D>(world.Sweep(Box(0, 0, 2, 2), Vector(10, 0)));

        Assert.Equal(0.3, hit.TimeOfImpact, 9);
        Assert.Equal(Vector(-1, 0), hit.Normal);
        Assert.Equal(Point(4, 0), hit.ContactPoint);
    }

    [Fact]
    public void CircleSweepStopsAtRoundedAabbCorner()
    {
        SpatialWorld2D world = World(BoxCollider("wall", 5, 5, 2, 2));

        SpatialHit2D hit = Assert.IsType<SpatialHit2D>(
            world.Sweep(new Circle2(Point(0, 0), 1), Vector(10, 10)));

        Assert.InRange(hit.TimeOfImpact, 0.3292, 0.3294);
        Assert.InRange(hit.Normal.X, -0.708, -0.706);
        Assert.InRange(hit.Normal.Y, -0.708, -0.706);
    }

    [Fact]
    public void CircleSweepAgainstCircleIsContinuous()
    {
        SpatialWorld2D world = World(CircleCollider("target", 5, 0, 1));

        SpatialHit2D hit = Assert.IsType<SpatialHit2D>(
            world.Sweep(new Circle2(Point(0, 0), 1), Vector(10, 0)));

        Assert.Equal(0.3, hit.TimeOfImpact, 9);
        Assert.Equal(Vector(-1, 0), hit.Normal);
    }

    [Fact]
    public void LargeDisplacementCannotTunnelThroughThinWall()
    {
        SpatialWorld2D world = World(BoxCollider("thin", 50, 0, 0.1, 20));

        SpatialMoveResult result = world.SweepAndSlide(Box(0, 0, 2, 2), Vector(100, 0));

        Assert.InRange(result.AcceptedDisplacement.X, 48.949999, 48.950001);
        Assert.Equal(0, result.AcceptedDisplacement.Y);
        Assert.Equal("thin", Assert.Single(result.Contacts).ColliderId.Value);
    }

    [Fact]
    public void ZeroDisplacementIsNoOpWithoutFakeHit()
    {
        SpatialWorld2D world = World(BoxCollider("wall", 5, 0, 2, 2));

        Assert.Null(world.Sweep(Box(0, 0, 2, 2), Vector(0, 0)));
        SpatialMoveResult move = world.SweepAndSlide(Box(0, 0, 2, 2), Vector(0, 0));
        Assert.Equal(Vector(0, 0), move.AcceptedDisplacement);
        Assert.Empty(move.Contacts);
        Assert.Equal(0, move.Iterations);
    }

    [Fact]
    public void DiagonalMoveSlidesAlongWall()
    {
        SpatialWorld2D world = World(BoxCollider("wall", 5, 0, 2, 100));

        SpatialMoveResult result = world.SweepAndSlide(Box(0, 0, 2, 2), Vector(10, 7));

        Assert.Equal(3, result.AcceptedDisplacement.X, 9);
        Assert.Equal(7, result.AcceptedDisplacement.Y, 9);
    }

    [Fact]
    public void InnerCornerReportsBothEqualTimeContactsAndStops()
    {
        SpatialWorld2D world = World(
            BoxCollider("vertical", 5, 0, 2, 100),
            BoxCollider("horizontal", 0, 5, 100, 2));

        SpatialMoveResult result = world.SweepAndSlide(Box(0, 0, 2, 2), Vector(10, 10));

        Assert.Equal(Vector(3, 3), result.AcceptedDisplacement);
        Assert.Equal(["horizontal", "vertical"], result.Contacts.Select(hit => hit.ColliderId.Value));
    }

    [Fact]
    public void OuterCornerPreservesTangentMotion()
    {
        SpatialWorld2D world = World(BoxCollider("short-wall", 5, 0, 2, 6));

        SpatialMoveResult result = world.SweepAndSlide(Box(0, 0, 2, 2), Vector(10, 10));

        Assert.Equal(3, result.AcceptedDisplacement.X, 9);
        Assert.Equal(10, result.AcceptedDisplacement.Y, 9);
    }

    [Fact]
    public void NarrowPassageAcceptsCenteredActor()
    {
        SpatialWorld2D world = World(
            BoxCollider("left", -2, 5, 2, 20),
            BoxCollider("right", 2, 5, 2, 20));

        SpatialMoveResult result = world.SweepAndSlide(Box(0, 0, 2, 2), Vector(0, 10));

        Assert.Equal(Vector(0, 10), result.AcceptedDisplacement);
        Assert.Empty(result.Contacts);
    }

    [Fact]
    public void EqualTimeOfImpactUsesStableColliderId()
    {
        SpatialWorld2D world = World(
            BoxCollider("z-wall", 5, 0, 2, 10),
            BoxCollider("a-wall", 5, 0, 2, 10));

        SpatialHit2D hit = Assert.IsType<SpatialHit2D>(world.Sweep(Box(0, 0, 2, 2), Vector(10, 0)));

        Assert.Equal("a-wall", hit.ColliderId.Value);
    }

    [Fact]
    public void InitialPenetrationIsReportedWithoutDepenetration()
    {
        SpatialWorld2D world = World(BoxCollider("wall", 0, 0, 4, 4));

        SpatialMoveResult result = world.SweepAndSlide(Box(0, 0, 2, 2), Vector(1, 0));

        Assert.True(result.StartedOverlapping);
        Assert.Equal(Vector(0, 0), result.AcceptedDisplacement);
        Assert.True(Assert.Single(result.Contacts).InitiallyOverlapping);
    }

    [Fact]
    public void TriggerDiffProducesEnteredStayedExitedInStableOrder()
    {
        TriggerTransition2D result = SpatialWorld2D.DiffTriggers(
            [Trigger("stay"), Trigger("exit-z"), Trigger("exit-a")],
            [Trigger("enter-z"), Trigger("stay"), Trigger("enter-a")]);

        Assert.Equal(["enter-a", "enter-z"], result.Entered.Select(id => id.Value));
        Assert.Equal(["stay"], result.Stayed.Select(id => id.Value));
        Assert.Equal(["exit-a", "exit-z"], result.Exited.Select(id => id.Value));
    }

    [Fact]
    public void TriggerVolumesNeverBlockSweep()
    {
        SpatialWorld2D world = new(
            triggers:
            [
                new SpatialTrigger2D(Trigger("pickup"), Box(5, 0, 2, 2))
            ]);

        SpatialMoveResult result = world.SweepAndSlide(Box(0, 0, 2, 2), Vector(10, 0));

        Assert.Equal(Vector(10, 0), result.AcceptedDisplacement);
        Assert.Single(world.OverlapTriggers(Box(5, 0, 2, 2)));
    }

    [Fact]
    public void CrossingTriggerBoundaryProducesEnteredStayedExitedSequence()
    {
        SpatialWorld2D world = new(
            triggers: [new SpatialTrigger2D(Trigger("door"), Box(5, 0, 2, 2))]);
        IReadOnlyList<SpatialTriggerId> enteredIds = world
            .OverlapTriggers(Box(5, 0, 1, 1))
            .Select(hit => hit.TriggerId)
            .ToArray();

        TriggerTransition2D entered = SpatialWorld2D.DiffTriggers([], enteredIds);
        TriggerTransition2D stayed = SpatialWorld2D.DiffTriggers(enteredIds, enteredIds);
        TriggerTransition2D exited = SpatialWorld2D.DiffTriggers(enteredIds, []);

        Assert.Equal("door", Assert.Single(entered.Entered).Value);
        Assert.Equal("door", Assert.Single(stayed.Stayed).Value);
        Assert.Equal("door", Assert.Single(exited.Exited).Value);
    }

    [Fact]
    public void LayerFilterIsTypedAndEngineNeutral()
    {
        SpatialWorld2D world = World(
            BoxCollider("layer-zero", 0, 0, 2, 2),
            BoxCollider("layer-one", 0, 0, 2, 2, SpatialLayerMask.Layer1));

        IReadOnlyList<SpatialOverlap2D> hits = world.Overlap(
            Box(0, 0, 1, 1),
            new SpatialQueryFilter(SpatialLayerMask.Layer1));

        Assert.Equal("layer-one", Assert.Single(hits).ColliderId.Value);
    }

    [Fact]
    public void ColliderMaskCanRejectAQueryLayer()
    {
        var collider = new SpatialCollider2D(
            new SpatialColliderId("selective"),
            Box(0, 0, 2, 2),
            SpatialLayerMask.Layer0,
            SpatialLayerMask.Layer1);
        SpatialWorld2D world = World(collider);

        Assert.Empty(world.Overlap(
            Box(0, 0, 1, 1),
            new SpatialQueryFilter(SpatialLayerMask.All, SpatialLayerMask.Layer2)));
        Assert.Single(world.Overlap(
            Box(0, 0, 1, 1),
            new SpatialQueryFilter(SpatialLayerMask.All, SpatialLayerMask.Layer1)));
    }

    [Fact]
    public void TransientActorVolumeCanBeQueriedWithoutWorldMutation()
    {
        SpatialWorld2D world = World();
        SpatialCollider2D actor = CircleCollider("actor", 1, 0, 1);

        IReadOnlyList<SpatialOverlap2D> hits = world.Overlap(
            new Circle2(Point(0, 0), 1),
            transientColliders: [actor]);

        Assert.Equal("actor", Assert.Single(hits).ColliderId.Value);
        Assert.Empty(world.StaticColliders);
    }

    [Fact]
    public void AttackAndPickupProofsReturnCandidatesNotGameplayCallbacks()
    {
        SpatialWorld2D world = new(
            [CircleCollider("enemy-volume", 2, 0, 1)],
            [new SpatialTrigger2D(Trigger("pickup-volume"), new Circle2(Point(0, 0), 1), SemanticOwnerId: "pickup")]);

        Assert.Equal("enemy", Assert.Single(world.Overlap(new Circle2(Point(1, 0), 1))).SemanticOwnerId);
        Assert.Equal("pickup", Assert.Single(world.OverlapTriggers(new Circle2(Point(0, 0), 0.5))).SemanticOwnerId);
    }

    [Fact]
    public void KnockbackUsesTheSameCollisionSafeDisplacementLaw()
    {
        SpatialWorld2D world = World(BoxCollider("wall", 5, 0, 2, 10));

        SpatialMoveResult knockback = world.SweepAndSlide(new Circle2(Point(0, 0), 1), Vector(8, 0));

        Assert.Equal(3, knockback.AcceptedDisplacement.X, 9);
        Assert.Equal("wall", Assert.Single(knockback.Contacts).ColliderId.Value);
    }

    [Fact]
    public void InvalidShapesAndNonFiniteInputsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            World().Overlap(new Circle2(Point(0, 0), -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            World().Sweep(Box(0, 0, 2, 2), Vector(double.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            World().Overlap(new Aabb2(Point(0, 0), Vector(-1, 1))));
    }

    [Fact]
    public void ThousandStepReplayHasExactContactAndPositionParity()
    {
        SpatialWorld2D world = World(
            BoxCollider("left", -5, 0, 1, 30),
            BoxCollider("right", 5, 0, 1, 30),
            BoxCollider("top", 0, -5, 30, 1),
            BoxCollider("bottom", 0, 5, 30, 1));

        string first = RunReplay(world);
        string second = RunReplay(world);

        Assert.Equal(first, second);
        Assert.Equal("0.000000000|2.750000000|bb50b9b9", first);
    }

    [Fact]
    public void TileAuthoringLowersOnceToWorldSpaceAabb()
    {
        Aabb2 shape = SpatialWorldAuthoring2D.TileRectangle(2, 3, 2, 1, 1024);

        Assert.Equal(Point(3072, 3584), shape.Center);
        Assert.Equal(Vector(1024, 512), shape.HalfExtents);
    }

    [Fact]
    public void DebugFactsCarrySweepContactNormalAndStableColliderId()
    {
        SpatialWorld2D world = World(BoxCollider("wall", 5, 0, 2, 10));
        Aabb2 actor = Box(0, 0, 2, 2);
        SpatialVector2D desired = Vector(10, 0);
        SpatialMoveResult result = world.SweepAndSlide(actor, desired);

        IReadOnlyList<SpatialDebugFact2D> facts = SpatialDebugFacts2D.ForSweep(
            "player-move-1",
            actor,
            desired,
            result);

        Assert.Equal(SpatialDebugFactKind.Sweep, facts[0].Kind);
        Assert.Equal("wall", facts[1].StableId);
        Assert.Equal(Point(4, 0), facts[1].ContactPoint);
        Assert.Equal(Vector(-1, 0), facts[1].Normal);
    }

    private static string RunReplay(SpatialWorld2D world)
    {
        SpatialPoint2D position = Point(0, 0);
        uint contactHash = 2166136261;
        for (int step = 0; step < 1000; step++)
        {
            SpatialVector2D desired = (step % 4) switch
            {
                0 => Vector(0.75, 0.25),
                1 => Vector(0.25, 0.75),
                2 => Vector(-0.75, 0.25),
                _ => Vector(-0.25, -0.75)
            };
            SpatialMoveResult move = world.SweepAndSlide(new Circle2(position, 1), desired);
            position += move.AcceptedDisplacement;
            foreach (SpatialHit2D contact in move.Contacts)
            {
                foreach (char value in contact.ColliderId.Value)
                {
                    contactHash = (contactHash ^ value) * 16777619;
                }
            }
        }
        return FormattableString.Invariant($"{position.X:F9}|{position.Y:F9}|{contactHash:x8}");
    }

    private static SpatialWorld2D World(params SpatialCollider2D[] colliders)
    {
        return new SpatialWorld2D(colliders);
    }

    private static SpatialCollider2D BoxCollider(
        string id,
        double x,
        double y,
        double width,
        double height,
        SpatialLayerMask layer = SpatialLayerMask.Layer0)
    {
        return new SpatialCollider2D(new SpatialColliderId(id), Box(x, y, width, height), layer, SemanticOwnerId: id.Replace("-volume", ""));
    }

    private static SpatialCollider2D CircleCollider(
        string id,
        double x,
        double y,
        double radius)
    {
        return new SpatialCollider2D(new SpatialColliderId(id), new Circle2(Point(x, y), radius), SemanticOwnerId: id.Replace("-volume", ""));
    }

    private static Aabb2 Box(double x, double y, double width, double height)
    {
        return new Aabb2(Point(x, y), Vector(width / 2, height / 2));
    }

    private static SpatialPoint2D Point(double x, double y) => new(x, y);

    private static SpatialVector2D Vector(double x, double y) => new(x, y);

    private static SpatialTriggerId Trigger(string id) => new(id);
}
