using System.Numerics;
using Aurelian.Spatial3D;
using Xunit;

namespace Aurelian.Spatial3D.Tests;

public sealed class SpatialWorld3DTests
{
    private static Collider3D Box(string id, Vector3 center, Vector3 half, uint layer = 1, uint mask = uint.MaxValue)
        => new(id, CollisionMesh3D.Box(half), Matrix4x4.CreateTranslation(center), layer, mask, id);

    [Fact]
    public void RayFindsNearestFilteredOwnerAndStableTriangle()
    {
        Assert.Equal(QueryFilter3D.All, new QueryFilter3D());
        Collider3D a = Box("a", new(0, 0, -3), Vector3.One, layer: 1, mask: 2);
        Collider3D b = Box("b", new(0, 0, -6), Vector3.One, layer: 4);
        var world = new SpatialWorld3D([b, a]);
        var ray = new Ray3D(Vector3.Zero, -Vector3.UnitZ, 20);
        Assert.Equal("a", world.Raycast(ray, new(1, 2))!.Value.ColliderId);
        Assert.Equal("b", world.Raycast(ray, new(5, 8))!.Value.ColliderId);
        Assert.Null(world.Raycast(ray, new(2, 2)));
        var reversed = new SpatialWorld3D([a, b]);
        Assert.Equal(world.Raycast(ray), reversed.Raycast(ray));
        Assert.Equal(2, world.Raycast(ray)!.Value.Distance, 4);
    }

    [Fact]
    public void ClosedCollisionRejectsMissingFacesAndInconsistentWinding()
    {
        CollisionMesh3D box = CollisionMesh3D.Box(Vector3.One);
        Assert.Throws<ArgumentException>(() => new CollisionMesh3D(box.Positions, box.Indices.Skip(3), closed: true));
        int[] reversed = box.Indices.ToArray();
        (reversed[0], reversed[1]) = (reversed[1], reversed[0]);
        Assert.Throws<ArgumentException>(() => new CollisionMesh3D(box.Positions, reversed, closed: true));
    }

    [Fact]
    public void CapsuleCannotTunnelThroughThinWallAndSlidesAlongIt()
    {
        var world = new SpatialWorld3D([Box("wall", new(2, 1, 0), new(0.01f, 3, 10))]);
        var capsule = Capsule3D.AtFeet(Vector3.Zero);
        var hit = world.Sweep(capsule, new(20, 0, 0))!.Value;
        Assert.InRange(hit.TimeOfImpact, 0.084f, 0.085f);
        var movement = world.SweepAndSlide(capsule, new(20, 0, 2));
        Assert.InRange(movement.AcceptedDisplacement.X, 1.68f, 1.70f);
        Assert.InRange(movement.AcceptedDisplacement.Z, 1.999f, 2.001f);
        Assert.Empty(world.Overlap(capsule.Translated(movement.AcceptedDisplacement)));
    }

    [Fact]
    public void EmbeddedCapsuleStopsWithExplicitOverlap()
    {
        var world = new SpatialWorld3D([Box("solid", Vector3.Zero, new(5, 5, 5))]);
        var result = world.SweepAndSlide(Capsule3D.AtFeet(Vector3.Zero), Vector3.UnitX);
        Assert.True(result.InitiallyOverlapping);
        Assert.Equal(Vector3.Zero, result.AcceptedDisplacement);
    }

    [Fact]
    public void JumpLandsOnAuthoredFloorAndFallingOffItsEdgeHasGravity()
    {
        var world = new SpatialWorld3D([Box("floor", new(0, -0.1f, 0), new(3, 0.1f, 3))]);
        var motor = new CharacterMotor3D();
        CharacterState3D state = new(Vector3.Zero, 0);
        var jump = motor.Step(world, state, Vector3.Zero, true, 1f / 60);
        Assert.False(jump.Grounded);
        Assert.True(jump.State.Feet.Y > 0);
        state = jump.State;
        CharacterMove3D current = jump;
        for (int index = 0; index < 90; index++)
        {
            current = motor.Step(world, state, Vector3.Zero, false, 1f / 60);
            state = current.State;
        }
        Assert.True(current.Grounded);
        Assert.InRange(state.Feet.Y, -0.0001f, 0.0001f);
        for (int index = 0; index < 150; index++)
        {
            current = motor.Step(world, state, new(4, 0, 0), false, 1f / 60);
            state = current.State;
        }
        Assert.False(current.Grounded);
        Assert.True(state.Feet.Y < -1);
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(65, false)]
    public void MotorClimbsWalkableSlopesAndBlocksSteepOnes(float angle, bool walkable)
    {
        float radians = angle * MathF.PI / 180;
        Matrix4x4 rotation = Matrix4x4.CreateRotationZ(radians);
        var ramp = new Collider3D("ramp", CollisionMesh3D.Box(new(8, 0.1f, 3)), rotation);
        var world = new SpatialWorld3D([ramp]);
        var motor = new CharacterMotor3D();
        float y = (0.1f + 0.3f) / MathF.Cos(radians) - 0.3f;
        CharacterState3D state = new(new(0, y, 0), 0);
        float initial = y;
        CharacterMove3D result = motor.Step(world, state, Vector3.Zero, false, 1f / 60);
        for (int index = 0; index < 120; index++)
        {
            result = motor.Step(world, state, new(2, 0, 0), false, 1f / 60);
            state = result.State;
        }
        if (walkable)
        {
            Assert.True(result.Grounded);
            Assert.True(state.Feet.X > 3);
            Assert.True(state.Feet.Y > initial + 1);
        }
        else
        {
            Assert.False(result.Grounded);
            Assert.True(state.Feet.X < 0.1f);
        }
    }

    [Fact]
    public void CeilingStopsUpwardMotion()
    {
        var world = new SpatialWorld3D([
            Box("floor", new(0, -0.1f, 0), new(5, 0.1f, 5)),
            Box("ceiling", new(0, 2.2f, 0), new(5, 0.1f, 5)),
        ]);
        var motor = new CharacterMotor3D();
        CharacterState3D state = new(Vector3.Zero, 0);
        bool ceilingHit = false;
        for (int index = 0; index < 45; index++)
        {
            var result = motor.Step(world, state, Vector3.Zero, index == 0, 1f / 60);
            state = result.State;
            ceilingHit |= result.Movement.Contacts.Any(hit => hit.ColliderId == "ceiling");
            Assert.True(state.Feet.Y < 0.301f);
        }
        Assert.True(ceilingHit);
    }
}
