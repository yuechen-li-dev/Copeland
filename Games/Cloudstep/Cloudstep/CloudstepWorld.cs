using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Serialization;
using Aurelian.Spatial3D;

namespace Cloudstep;

public enum CrumblePhase
{
    Solid,
    Shaking,
    Falling,
}

public sealed record CrumbleSnapshot(string Id, CrumblePhase Phase, float Timer);

/// <summary>The whole course state. Platform poses are derived from <see cref="Time"/> and crumble phases.</summary>
public sealed record CloudstepSnapshot(
    PlayerSnapshot Player,
    double Time,
    IReadOnlyList<bool> Gems,
    int Checkpoint,
    int Deaths,
    float RespawnTimer,
    bool Finished,
    double FinishTime,
    IReadOnlyList<CrumbleSnapshot> Crumbles,
    float CameraYaw,
    float CameraPitch);

[JsonSerializable(typeof(CloudstepSnapshot))]
public partial class CloudstepJsonContext : JsonSerializerContext;

public enum CourseEvent
{
    Jump,
    Land,
    Step,
    Gem,
    Checkpoint,
    Fall,
    Crushed,
    Hazard,
    Respawn,
    Crumble,
    Goal,
}

/// <summary>
/// Course rules: platform motion, the hero, pickups, checkpoints, deaths and the goal. Deterministic for a given
/// sequence of inputs; presentation and audio react to the returned events.
/// </summary>
public sealed class CloudstepWorld
{
    public const float RespawnDelay = 0.9f;
    private const float CrumbleShakeSeconds = 0.55f;
    private const float CrumbleReturnSeconds = 3.5f;
    private readonly bool[] gems;
    private readonly Dictionary<string, (CrumblePhase Phase, float Timer)> crumbles = new(StringComparer.Ordinal);
    private readonly SpatialWorld3D hazards;

    public CloudstepWorld(CloudstepCourse? course = null, PlayerTuning? tuning = null)
    {
        Course = course ?? CloudstepCourse.Default();
        gems = new bool[Course.Gems.Length];
        hazards = Course.BuildHazards();
        var bodies = Course.Platforms.Select(platform => new KinematicBody3D(platform.Id, CollisionMesh3D.Box(platform.HalfSize),
            SemanticOwnerId: platform.Id));
        Physics = new KinematicSpatialWorld3D(Course.BuildStatics(), bodies,
            Course.Platforms.ToDictionary(platform => platform.Id, platform => Pose(platform, 0, CrumblePhase.Solid, 0)));
        Player = new CloudstepPlayer(tuning);
        Reset();
    }

    public CloudstepCourse Course { get; }
    public KinematicSpatialWorld3D Physics { get; }
    public CloudstepPlayer Player { get; }
    public double Time { get; private set; }
    public int Checkpoint { get; private set; }
    public int Deaths { get; private set; }
    public float RespawnTimer { get; private set; }
    public bool Finished { get; private set; }
    public double FinishTime { get; private set; }
    public float CameraYaw { get; set; }
    public float CameraPitch { get; set; } = 0.35f;
    public int GemsCollected => gems.Count(collected => collected);
    public bool Collected(int gem) => gems[gem];
    public bool Dead => RespawnTimer > 0;
    public CrumblePhase CrumbleState(string id) => crumbles.TryGetValue(id, out var state) ? state.Phase : CrumblePhase.Solid;

    public void Reset()
    {
        Time = 0;
        Array.Clear(gems);
        Checkpoint = 0;
        Deaths = 0;
        RespawnTimer = 0;
        Finished = false;
        FinishTime = 0;
        crumbles.Clear();
        foreach (CoursePlatform platform in Course.Platforms.Where(platform => platform.Motion == PlatformMotion.Crumble))
        {
            crumbles[platform.Id] = (CrumblePhase.Solid, 0);
        }
        CourseCheckpoint start = Course.Checkpoints[0];
        Player.Place(start.Position, start.Yaw);
        CameraYaw = start.Yaw;
        CameraPitch = 0.35f;
        RestorePoses(previousTime: 0);
    }

    /// <summary>Advance one fixed tick. <paramref name="move"/> is camera-relative stick input (X right, Y forward).</summary>
    public IReadOnlyList<CourseEvent> Tick(Vector2 move, bool jumpPressed, bool jumpHeld, float seconds)
    {
        var events = new List<CourseEvent>();
        Time += seconds;
        Physics.BeginStep();
        AdvanceCrumbles(seconds, events);
        PosePlatforms();
        if (RespawnTimer > 0)
        {
            RespawnTimer = MathF.Max(0, RespawnTimer - seconds);
            if (RespawnTimer == 0)
            {
                CourseCheckpoint point = Course.Checkpoints[Checkpoint];
                Player.Place(point.Position, point.Yaw);
                CameraYaw = point.Yaw;
                events.Add(CourseEvent.Respawn);
            }
            return events;
        }
        if (Finished)
        {
            move = Vector2.Zero;
            jumpPressed = false;
        }
        // Rotate stick input by the camera yaw: forward is the direction the camera looks along the ground.
        var forward = new Vector2(MathF.Sin(CameraYaw), -MathF.Cos(CameraYaw));
        var right = new Vector2(MathF.Cos(CameraYaw), MathF.Sin(CameraYaw));
        Vector2 world = right * move.X + forward * move.Y;
        PlayerStepResult step = Player.Step(Physics, new PlayerInput(world, jumpPressed, jumpHeld), seconds);
        if (step.Jumped)
        {
            events.Add(CourseEvent.Jump);
        }
        if (step.Landed)
        {
            events.Add(CourseEvent.Land);
        }
        if (step.SteppedUp > 0)
        {
            events.Add(CourseEvent.Step);
        }

        if (step.Crushed)
        {
            Die(CourseEvent.Crushed, events);
            return events;
        }
        if (Player.Feet.Y < Course.KillHeight)
        {
            Die(CourseEvent.Fall, events);
            return events;
        }
        if (!hazards.Overlap(Player.Capsule).IsEmpty)
        {
            Die(CourseEvent.Hazard, events);
            return events;
        }

        Vector3 centre = Player.Centre;
        for (int index = 0; index < gems.Length; index++)
        {
            if (!gems[index] && Vector3.Distance(centre, Course.Gems[index]) < 0.95f)
            {
                gems[index] = true;
                events.Add(CourseEvent.Gem);
            }
        }
        for (int index = Checkpoint + 1; index < Course.Checkpoints.Length; index++)
        {
            Vector3 offset = Player.Feet - Course.Checkpoints[index].Position;
            if (new Vector2(offset.X, offset.Z).Length() < 1.8f && MathF.Abs(offset.Y) < 1.5f)
            {
                Checkpoint = index;
                events.Add(CourseEvent.Checkpoint);
            }
        }
        if (!Finished)
        {
            Vector3 toGoal = Player.Feet - Course.Goal;
            if (new Vector2(toGoal.X, toGoal.Z).Length() < 1.6f && toGoal.Y > -0.5f && toGoal.Y < 3)
            {
                Finished = true;
                FinishTime = Time;
                events.Add(CourseEvent.Goal);
            }
        }
        return events;
    }

    public CloudstepSnapshot Capture() => new(Player.Capture(), Time, gems.ToArray(), Checkpoint, Deaths, RespawnTimer, Finished,
        FinishTime, crumbles.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new CrumbleSnapshot(pair.Key, pair.Value.Phase, pair.Value.Timer)).ToArray(),
        CameraYaw, CameraPitch);

    public void Validate(CloudstepSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Player is null || snapshot.Gems is null || snapshot.Crumbles is null
            || snapshot.Gems.Count != gems.Length || !double.IsFinite(snapshot.Time) || snapshot.Time < 0
            || snapshot.Checkpoint < 0 || snapshot.Checkpoint >= Course.Checkpoints.Length || snapshot.Deaths < 0
            || !float.IsFinite(snapshot.RespawnTimer) || snapshot.RespawnTimer < 0 || snapshot.RespawnTimer > RespawnDelay
            || !double.IsFinite(snapshot.FinishTime) || !float.IsFinite(snapshot.CameraYaw) || !float.IsFinite(snapshot.CameraPitch))
        {
            throw new InvalidDataException("Invalid Cloudstep state; the live game has not been changed.");
        }
        PlayerSnapshot player = snapshot.Player;
        CloudstepPlayer.Validate(player);
        if (player.Support is not null && !Physics.IsKinematic(player.Support)
            && !Course.Blocks.Any(block => block.Id == player.Support))
        {
            throw new InvalidDataException("The saved player stands on an unknown surface.");
        }
        var expected = crumbles.Keys.Order(StringComparer.Ordinal).ToArray();
        if (!snapshot.Crumbles.Select(item => item?.Id).SequenceEqual(expected)
            || snapshot.Crumbles.Any(item => !Enum.IsDefined(item.Phase) || !float.IsFinite(item.Timer) || item.Timer < 0))
        {
            throw new InvalidDataException("The saved crumbling tiles do not match this course.");
        }
    }

    public void Restore(CloudstepSnapshot snapshot)
    {
        Validate(snapshot);
        Time = snapshot.Time;
        for (int index = 0; index < gems.Length; index++)
        {
            gems[index] = snapshot.Gems[index];
        }
        Checkpoint = snapshot.Checkpoint;
        Deaths = snapshot.Deaths;
        RespawnTimer = snapshot.RespawnTimer;
        Finished = snapshot.Finished;
        FinishTime = snapshot.FinishTime;
        foreach (CrumbleSnapshot crumble in snapshot.Crumbles)
        {
            crumbles[crumble.Id] = (crumble.Phase, crumble.Timer);
        }
        CameraYaw = snapshot.CameraYaw;
        CameraPitch = snapshot.CameraPitch;
        Player.Restore(snapshot.Player);
        RestorePoses(previousTime: Math.Max(0, Time - 1.0 / 60));
    }

    /// <summary>Pose of a platform at a time; crumbling tiles also depend on their phase.</summary>
    public static Matrix4x4 Pose(CoursePlatform platform, double time, CrumblePhase phase, float timer)
    {
        switch (platform.Motion)
        {
            case PlatformMotion.Turntable:
                return Matrix4x4.CreateRotationY((float)(platform.AngularSpeed * time)) * Matrix4x4.CreateTranslation(platform.From);
            case PlatformMotion.Crumble:
                Vector3 offset = phase switch
                {
                    CrumblePhase.Shaking => new(MathF.Sin(timer * 61) * 0.035f, 0, MathF.Cos(timer * 47) * 0.035f),
                    CrumblePhase.Falling => new(0, -0.5f * 40 * timer * timer, 0),
                    _ => Vector3.Zero,
                };
                return Matrix4x4.CreateTranslation(platform.From + offset);
            default:
                double cycle = (time % platform.Period + platform.Period) % platform.Period / platform.Period * 2;
                bool returning = cycle >= 1;
                double leg = returning ? cycle - 1 : cycle;
                double t = Math.Clamp((leg - platform.Dwell) / (1 - 2 * platform.Dwell), 0, 1);
                float eased = (float)(t * t * (3 - 2 * t));
                Vector3 position = returning
                    ? Vector3.Lerp(platform.To, platform.From, eased)
                    : Vector3.Lerp(platform.From, platform.To, eased);
                return Matrix4x4.CreateTranslation(position);
        }
    }

    /// <summary>Move every platform to its pose at <see cref="Time"/> for this step.</summary>
    private void PosePlatforms()
    {
        foreach (CoursePlatform platform in Course.Platforms)
        {
            (CrumblePhase phase, float timer) = crumbles.TryGetValue(platform.Id, out var state) ? state : (CrumblePhase.Solid, 0);
            Physics.SetTransform(platform.Id, Pose(platform, Time, phase, timer));
        }
    }

    /// <summary>Set both poses exactly (after reset or restore) so the next step's carry is correct.</summary>
    private void RestorePoses(double previousTime)
    {
        foreach (CoursePlatform platform in Course.Platforms)
        {
            (CrumblePhase phase, float timer) = crumbles.TryGetValue(platform.Id, out var state) ? state : (CrumblePhase.Solid, 0);
            Physics.Restore(platform.Id, Pose(platform, previousTime, phase, timer), Pose(platform, Time, phase, timer));
        }
    }

    private void AdvanceCrumbles(float seconds, List<CourseEvent> events)
    {
        foreach (string id in crumbles.Keys.Order(StringComparer.Ordinal).ToArray())
        {
            (CrumblePhase phase, float timer) = crumbles[id];
            switch (phase)
            {
                case CrumblePhase.Solid:
                    if (Player.Support == id && RespawnTimer == 0)
                    {
                        crumbles[id] = (CrumblePhase.Shaking, 0);
                        events.Add(CourseEvent.Crumble);
                    }
                    break;
                case CrumblePhase.Shaking:
                    timer += seconds;
                    crumbles[id] = timer >= CrumbleShakeSeconds ? (CrumblePhase.Falling, 0) : (CrumblePhase.Shaking, timer);
                    break;
                default:
                    timer += seconds;
                    crumbles[id] = timer >= CrumbleReturnSeconds ? (CrumblePhase.Solid, 0) : (CrumblePhase.Falling, timer);
                    break;
            }
        }
    }

    private void Die(CourseEvent cause, List<CourseEvent> events)
    {
        Deaths++;
        RespawnTimer = RespawnDelay;
        events.Add(cause);
    }
}
