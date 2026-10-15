using System.Numerics;
using Aurelian.Spatial3D;
using Xunit;

namespace Aurelian.Spatial3D.Tests;

/// <summary>Regression tests for characters getting stuck on edges, seams and stairs.</summary>
public sealed class EdgeHandlingTests
{
    private const float Tick = 1f / 60;

    private static Collider3D Box(string id, Vector3 center, Vector3 half) => new(id, CollisionMesh3D.Box(half), Matrix4x4.CreateTranslation(center));

    private static CharacterMotor3D Platformer(float ledgeAssist = 0) => new(new(Radius: 0.35f, Height: 1.4f, Gravity: 28,
        MaximumSlopeDegrees: 50, GroundSnap: 0.12f, StepHeight: 0.4f, StepDownHeight: 0.35f, LedgeAssistHeight: ledgeAssist));

    [Theory]
    [InlineData(-0.1f, true)]
    [InlineData(0.05f, false)]
    [InlineData(0.15f, false)]
    [InlineData(0.25f, false)]
    [InlineData(0.32f, false)]
    [InlineData(0.345f, false)]
    public void DroppingOntoAnEdgeEitherStandsOrSlidesOffButNeverHovers(float overhang, bool centreOverTile)
    {
        var world = new SpatialWorld3D([Box("tile", new(0, -0.2f, 0), new(1, 0.2f, 1))]);
        CharacterMotor3D motor = Platformer();
        CharacterState3D state = new(new(1 + overhang, 1, 0), 0);
        CharacterMove3D move = motor.Step(world, state, Vector3.Zero, false, Tick);
        for (int tick = 0; tick < 120; tick++)
        {
            move = motor.Step(world, state, Vector3.Zero, false, Tick);
            state = move.State;
        }
        Assert.False(move.Embedded);
        Assert.True(move.Grounded || state.Feet.Y < -1, $"hovering at {state.Feet}");
        if (move.Grounded)
        {
            Assert.Equal(centreOverTile, move.CentreSupported);
        }
    }

    [Theory]
    [InlineData(0f, 2f)]
    [InlineData(30f, 3f)]
    [InlineData(55f, 4f)]
    [InlineData(70f, 7f)]
    [InlineData(80f, 7f)]
    public void StairsClimbFromAnyAngleAndSpeed(float degrees, float speed)
    {
        var steps = Enumerable.Range(0, 6).Select(i => Box($"s{i}", new(0, 0.125f + i * 0.125f, -2 - i * 0.6f),
            new(30, 0.125f + i * 0.125f, 0.3f))).ToList();
        var world = new SpatialWorld3D([Box("floor", new(0, -0.5f, 0), new(40, 0.5f, 40)), .. steps,
            Box("top", new(0, 0.75f, -7), new(30, 0.75f, 1.8f))]);
        CharacterMotor3D motor = Platformer();
        float radians = degrees * MathF.PI / 180;
        var velocity = new Vector3(MathF.Sin(radians), 0, -MathF.Cos(radians)) * speed;
        CharacterState3D state = new(new(-1.5f, 0, 0), 0);
        for (int tick = 0; tick < 900 && state.Feet.Y < 1.49f; tick++)
        {
            CharacterMove3D move = motor.Step(world, state, velocity, false, Tick);
            Assert.False(move.Embedded, $"embedded at {move.State.Feet}");
            state = move.State;
        }
        Assert.True(state.Feet.Y > 1.49f, $"stuck at {state.Feet}");
    }

    [Fact]
    public void WalkingDownStairsStaysGroundedInsteadOfHopping()
    {
        var steps = Enumerable.Range(0, 6).Select(i => Box($"s{i}", new(0, 0.125f + i * 0.125f, -2 - i * 0.6f),
            new(4, 0.125f + i * 0.125f, 0.3f))).ToList();
        var world = new SpatialWorld3D([Box("floor", new(0, -0.5f, 0), new(10, 0.5f, 10)), .. steps,
            Box("top", new(0, 0.75f, -7), new(4, 0.75f, 1.8f))]);
        CharacterMotor3D motor = Platformer();
        CharacterState3D state = new(new(0, 1.5f + 0.0001f, -6.5f), 0);
        int airborne = 0;
        for (int tick = 0; tick < 150 && state.Feet.Z < -0.5f; tick++)
        {
            CharacterMove3D move = motor.Step(world, state, new(0, 0, 4), false, Tick);
            airborne += move.Grounded ? 0 : 1;
            state = move.State;
        }
        Assert.True(state.Feet.Z >= -0.5f);
        Assert.InRange(state.Feet.Y, -0.001f, 0.001f);
        Assert.Equal(0, airborne);
    }

    [Fact]
    public void LedgeAssistLiftsAShortJumpOntoTheLedgeOnlyWhenEnabled()
    {
        // An upward speed of 6 under gravity 28 peaks 0.64 m up; the ledge top is 0.94 m, 0.3 m above the apex.
        // Forty ticks at 3 m/s ends about a metre onto the 2 m deep ledge.
        var world = new SpatialWorld3D([Box("floor", new(0, -0.5f, 0), new(10, 0.5f, 10)), Box("ledge", new(0, 0.47f, -3), new(4, 0.47f, 1))]);
        float Jump(CharacterMotor3D motor, out bool assisted)
        {
            CharacterState3D state = new(new(0, 0, -1.1f), 6);
            assisted = false;
            for (int tick = 0; tick < 40; tick++)
            {
                CharacterMove3D move = motor.Step(world, state, new(0, 0, -3), false, Tick);
                assisted |= move.LedgeAssisted;
                state = move.State;
            }
            return state.Feet.Y;
        }
        Assert.InRange(Jump(Platformer(), out bool plain), -0.001f, 0.001f);
        Assert.False(plain);
        Assert.InRange(Jump(Platformer(ledgeAssist: 0.45f), out bool helped), 0.939f, 0.941f);
        Assert.True(helped);
        Assert.InRange(Jump(Platformer(ledgeAssist: 0.2f), out _), -0.001f, 0.001f);
    }

    [Fact]
    public void ShallowOverlapsAreResolvedButDeepOnesStayEmbedded()
    {
        var world = new SpatialWorld3D([Box("floor", new(0, -0.5f, 0), new(10, 0.5f, 10))]);
        CharacterMotor3D motor = Platformer();
        CharacterMove3D shallow = motor.Step(world, new(new(0, -0.01f, 0), 0), new(1, 0, 0), false, Tick);
        Assert.False(shallow.Embedded);
        Assert.True(shallow.Grounded);
        Assert.True(shallow.State.Feet.X > 0);
        Assert.True(shallow.State.Feet.Y >= 0);
        CharacterMove3D deep = motor.Step(world, new(new(0, -0.3f, 0), 0), new(1, 0, 0), false, Tick);
        Assert.True(deep.Embedded);
    }
}
