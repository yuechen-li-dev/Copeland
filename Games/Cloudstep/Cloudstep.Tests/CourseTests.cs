using System.Numerics;
using System.Text.Json;
using Cloudstep;
using Xunit;
using Xunit.Abstractions;

namespace Cloudstep.Tests;

public sealed class CourseTests(ITestOutputHelper output)
{
    private const float Tick = 1f / 60;

    private static void Run(CloudstepWorld world, CloudstepAutopilot pilot, int ticks, List<CourseEvent>? events = null)
    {
        for (int tick = 0; tick < ticks && !world.Finished; tick++)
        {
            var (move, pressed, held) = pilot.Drive(world);
            IReadOnlyList<CourseEvent> happened = world.Tick(move, pressed, held, Tick);
            events?.AddRange(happened);
        }
    }

    [Fact]
    public void AutopilotCompletesTheCourseWithoutFalling()
    {
        var world = new CloudstepWorld();
        var pilot = new CloudstepAutopilot();
        var events = new List<CourseEvent>();
        string lastLeg = "";
        for (int tick = 0; tick < 60 * 240 && !world.Finished; tick++)
        {
            if (pilot.Leg != lastLeg)
            {
                output.WriteLine($"{world.Time,7:0.00}s {pilot.Leg,-16} at {world.Player.Feet}");
                lastLeg = pilot.Leg;
            }
            var (move, pressed, held) = pilot.Drive(world);
            events.AddRange(world.Tick(move, pressed, held, Tick));
        }
        output.WriteLine($"finished={world.Finished} time={world.FinishTime:0.00} gems={world.GemsCollected} falls={world.Deaths} leg={pilot.Leg}");
        Assert.True(world.Finished, $"stuck on leg '{pilot.Leg}' at {world.Player.Feet}");
        Assert.Equal(0, world.Deaths);
        Assert.Equal(2, world.Checkpoint);
        Assert.Contains(CourseEvent.Step, events);
        Assert.Contains(CourseEvent.Crumble, events);
        Assert.True(world.GemsCollected >= 8, $"collected {world.GemsCollected}");
    }

    [Fact]
    public void FallingOffRespawnsAtTheLastCheckpointAndCountsAFall()
    {
        var world = new CloudstepWorld();
        var events = new List<CourseEvent>();
        for (int tick = 0; tick < 400 && world.Deaths == 0; tick++)
        {
            events.AddRange(world.Tick(new Vector2(1, 0), false, false, Tick));
        }
        Assert.Equal(1, world.Deaths);
        Assert.Contains(CourseEvent.Fall, events);
        Assert.True(world.Dead);
        for (int tick = 0; tick < 70; tick++)
        {
            events.AddRange(world.Tick(Vector2.Zero, false, false, Tick));
        }
        Assert.False(world.Dead);
        Assert.Contains(CourseEvent.Respawn, events);
        Assert.True(Vector3.Distance(world.Course.Checkpoints[0].Position, world.Player.Feet) < 0.001f);
    }

    [Fact]
    public void ThornsAndCrushingCostALife()
    {
        var world = new CloudstepWorld();
        world.Player.Place(new(12.5f, 7, -85.5f), 0);
        var events = new List<CourseEvent>();
        for (int tick = 0; tick < 120 && world.Deaths == 0; tick++)
        {
            events.AddRange(world.Tick(new Vector2(0, 1), false, false, Tick));
        }
        Assert.Contains(CourseEvent.Hazard, events);

        // A press that comes down onto the floor squashes whoever stands under it.
        var press = new CloudstepCourse(
            [CourseBlock.Span("floor", new(-5, -1, -5), new(5, 0, 5), SurfaceKind.Stone)],
            [new CoursePlatform("press", PlatformMotion.Shuttle, new(0, 4, 0), new(1, 0.5f, 1), SurfaceKind.Stone)
            {
                To = new(0, 0.6f, 0),
                Period = 4,
                Dwell = 0,
            }],
            [], [new CourseCheckpoint(new(0, 0, 0), 0)], [], new(40, 0, 40), -12);
        var crusher = new CloudstepWorld(press);
        var crushed = new List<CourseEvent>();
        for (int tick = 0; tick < 240 && crusher.Deaths == 0; tick++)
        {
            crushed.AddRange(crusher.Tick(Vector2.Zero, false, false, Tick));
        }
        Assert.Contains(CourseEvent.Crushed, crushed);
    }

    [Fact]
    public void ShortTapsJumpLowerThanHeldJumpsAndCoyoteTimeForgivesLateJumps()
    {
        float Apex(bool hold)
        {
            var world = new CloudstepWorld();
            for (int tick = 0; tick < 10; tick++)
            {
                world.Tick(Vector2.Zero, false, false, Tick);
            }
            float top = 0;
            for (int tick = 0; tick < 60; tick++)
            {
                world.Tick(Vector2.Zero, tick == 0, hold, Tick);
                top = MathF.Max(top, world.Player.Feet.Y);
            }
            return top;
        }
        float held = Apex(true);
        float tapped = Apex(false);
        Assert.InRange(held, 1.3f, 1.6f);
        Assert.True(tapped < held * 0.6f, $"tap {tapped} vs hold {held}");

        // Run off the start island's east edge and press jump a few ticks after leaving it.
        var late = new CloudstepWorld();
        int airborne = -1;
        bool jumped = false;
        for (int tick = 0; tick < 200 && !jumped; tick++)
        {
            bool press = airborne >= 0 && tick - airborne == 3;
            IReadOnlyList<CourseEvent> events = late.Tick(new Vector2(1, 0), press, true, Tick);
            jumped = events.Contains(CourseEvent.Jump);
            if (airborne < 0 && !late.Player.Grounded && late.Player.Feet.X > 4)
            {
                airborne = tick;
            }
        }
        Assert.True(jumped, "a jump three ticks after leaving the ledge is within coyote time");
    }

    [Fact]
    public void SnapshotsRestoreMidRideExactly()
    {
        var first = new CloudstepWorld();
        var pilot = new CloudstepAutopilot();
        while (pilot.Leg != "ride shuttle")
        {
            var (move, pressed, held) = pilot.Drive(first);
            first.Tick(move, pressed, held, Tick);
        }
        for (int tick = 0; tick < 30; tick++)
        {
            var (move, pressed, held) = pilot.Drive(first);
            first.Tick(move, pressed, held, Tick);
        }
        Assert.Equal("shuttle", first.Player.Support);
        CloudstepSnapshot saved = first.Capture();
        string json = JsonSerializer.Serialize(saved, CloudstepJsonContext.Default.CloudstepSnapshot);
        var second = new CloudstepWorld();
        second.Restore(JsonSerializer.Deserialize(json, CloudstepJsonContext.Default.CloudstepSnapshot)!);
        for (int tick = 0; tick < 90; tick++)
        {
            first.Tick(new Vector2(0.3f, 0), false, false, Tick);
            second.Tick(new Vector2(0.3f, 0), false, false, Tick);
        }
        Assert.Equal(first.Player.Feet, second.Player.Feet);
        Assert.Equal(JsonSerializer.Serialize(first.Capture(), CloudstepJsonContext.Default.CloudstepSnapshot),
            JsonSerializer.Serialize(second.Capture(), CloudstepJsonContext.Default.CloudstepSnapshot));
        Assert.Throws<InvalidDataException>(() => second.Restore(saved with { Gems = [true] }));
        Assert.Throws<InvalidDataException>(() => second.Restore(saved with { Player = saved.Player with { Support = "nowhere" } }));
    }
}
