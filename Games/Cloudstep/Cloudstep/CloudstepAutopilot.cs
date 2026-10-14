using System.Numerics;

namespace Cloudstep;

/// <summary>
/// A scripted runner that plays the default course from the start to the beacon using only the same stick and jump
/// inputs a player has. Tests use it to prove the course stays completable; the "autopilot" command uses it to tour
/// the course in a native playtest.
/// </summary>
internal sealed class CloudstepAutopilot
{
    private sealed record RouteLeg(
        string Name,
        Func<CloudstepWorld, Vector3> Target,
        Func<CloudstepWorld, bool>? Ready = null,
        Func<CloudstepWorld, bool>? JumpWhen = null,
        Func<CloudstepWorld, bool>? Until = null,
        float Arrive = 0.35f);

    private readonly List<RouteLeg> legs;
    private int index;
    private bool started;
    private bool jumpedThisLeg;

    public CloudstepAutopilot()
    {
        legs = BuildRoute();
    }

    public int LegIndex => index;
    public string Leg => index < legs.Count ? legs[index].Name : "done";
    public bool Done => index >= legs.Count;

    public void Reset()
    {
        index = 0;
        started = false;
        jumpedThisLeg = false;
    }

    /// <summary>Choose this tick's input. The world's camera yaw is held at zero so stick +Y means world -Z.</summary>
    public (Vector2 Move, bool JumpPressed, bool JumpHeld) Drive(CloudstepWorld world)
    {
        world.CameraYaw = 0;
        if (world.Dead)
        {
            // A fall restarts the route from the leg containing the respawn checkpoint.
            index = world.Checkpoint switch { 0 => 0, 1 => legs.FindIndex(leg => leg.Name == "lift edge"), _ => legs.FindIndex(leg => leg.Name == "e south edge") };
            started = false;
            jumpedThisLeg = false;
            return (Vector2.Zero, false, false);
        }
        while (index < legs.Count)
        {
            RouteLeg leg = legs[index];
            CloudstepPlayer player = world.Player;
            if (!started)
            {
                if (leg.Ready is not null && !leg.Ready(world))
                {
                    return (Vector2.Zero, false, false);
                }
                started = true;
                jumpedThisLeg = false;
            }
            Vector3 target = leg.Target(world);
            Vector2 offset = new(target.X - player.Feet.X, target.Z - player.Feet.Z);
            bool arrived = offset.Length() < leg.Arrive && player.Grounded && (leg.Until?.Invoke(world) ?? true);
            if (arrived)
            {
                index++;
                started = false;
                continue;
            }
            bool jump = !jumpedThisLeg && player.Grounded && leg.JumpWhen is not null && leg.JumpWhen(world);
            if (jump)
            {
                jumpedThisLeg = true;
            }
            Vector2 move = offset.Length() > 0.05f ? Vector2.Normalize(offset) : Vector2.Zero;
            // Slow down when close so the runner does not orbit its target.
            if (offset.Length() < 0.6f)
            {
                move *= offset.Length() / 0.6f;
            }
            return (new Vector2(move.X, -move.Y), jump, !player.Grounded || jump);
        }
        return (Vector2.Zero, false, false);
    }

    private static Vector3 Centre(CloudstepWorld world, string platform) => world.Physics.Transform(platform).Translation;

    /// <summary>Turntable angle relative to lying along world Z, folded into (-pi/2, pi/2].</summary>
    private static float BarAngle(CloudstepWorld world)
    {
        Vector3 along = Vector3.TransformNormal(Vector3.UnitZ, world.Physics.Transform("turntable"));
        float angle = MathF.Atan2(along.X, along.Z);
        if (angle > MathF.PI / 2)
        {
            angle -= MathF.PI;
        }
        else if (angle <= -MathF.PI / 2)
        {
            angle += MathF.PI;
        }
        return angle;
    }

    /// <summary>The end of the turntable bar nearer the far island (smaller Z), pulled in slightly from the tip.</summary>
    private static Vector3 BarTip(CloudstepWorld world)
    {
        Matrix4x4 pose = world.Physics.Transform("turntable");
        Vector3 along = Vector3.TransformNormal(Vector3.UnitZ, pose);
        if (along.Z > 0)
        {
            along = -along;
        }
        return pose.Translation + along * 4.4f;
    }

    private static float Z(CloudstepWorld world) => world.Player.Feet.Z;

    private static List<RouteLeg> BuildRoute()
    {
        var route = new List<RouteLeg>
        {
            new("stairs", _ => new(0, 0, -14.2f)),
            new("gap", _ => new(0, 0, -20.5f), JumpWhen: world => Z(world) < -14.5f),
            new("shuttle edge", _ => new(0, 0, -23.4f)),
            new("board shuttle", world => Centre(world, "shuttle"), Ready: world => Centre(world, "shuttle").Z > -26.7f,
                JumpWhen: world => world.Player.Support == "isle-b", Arrive: 0.6f),
            new("ride shuttle", world => Centre(world, "shuttle"), Until: world => Centre(world, "shuttle").Z < -34.3f, Arrive: 0.8f),
            new("isle c", _ => new(0, 0, -39.4f), JumpWhen: world => world.Player.Support == "shuttle"),
            new("lift edge", _ => new(3.5f, 0, -41)),
            new("board lift", world => Centre(world, "lift"), Ready: world => Centre(world, "lift").Y < 1.35f,
                JumpWhen: world => world.Player.Support == "isle-c", Arrive: 0.6f),
            new("ride lift", world => Centre(world, "lift"), Until: world => Centre(world, "lift").Y > 6.75f, Arrive: 0.8f),
            new("isle d", _ => new(11.6f, 0, -41), JumpWhen: world => world.Player.Support == "lift"),
            new("d south edge", _ => new(12.75f, 0, -45.9f)),
        };
        float[] tiles = [-48.5f, -51.5f, -54.5f, -57.5f, -60.5f];
        for (int tile = 0; tile < tiles.Length; tile++)
        {
            float from = tile == 0 ? -45.6f : tiles[tile - 1] - 0.55f;
            float target = tiles[tile];
            route.Add(new RouteLeg($"crumble {tile}", _ => new(12.75f, 0, target), JumpWhen: world => Z(world) < from, Arrive: 0.5f));
        }
        route.AddRange(
        [
            new("isle e", _ => new(12.5f, 0, -64.6f), JumpWhen: world => Z(world) < -61.1f),
            new("e south edge", _ => new(12.5f, 0, -70.6f)),
            new("board turntable", _ => new(12.5f, 0, -75.6f),
                Ready: world => MathF.Abs(BarAngle(world)) < 0.06f, JumpWhen: world => world.Player.Support == "isle-e", Arrive: 0.7f),
            new("turntable tip", BarTip, Ready: world => BarAngle(world) > -0.25f && BarAngle(world) < -0.12f, Arrive: 0.45f),
            new("isle f", _ => new(12.5f, 0, -85.4f), Ready: world => MathF.Abs(BarAngle(world)) < 0.1f,
                JumpWhen: world => world.Player.Support == "turntable", Arrive: 0.6f),
            new("thorns", _ => new(12.5f, 0, -89.6f), JumpWhen: world => Z(world) < -85.9f),
            new("goal", world => world.Course.Goal),
        ]);
        return route;
    }
}
