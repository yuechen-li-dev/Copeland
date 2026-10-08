using System.Numerics;
using Aurelian.Spatial3D;
using Aurelian.NativeComposition;
using Aurelian.World.Scenes;

namespace Aurelian.Beacon3D;

public readonly record struct BeaconInput(float Forward, float Strafe, float Turn, float Look, bool Jump = false,
    float MouseYaw = 0, float MousePitch = 0, bool Fire = false, bool Reload = false);

public readonly record struct ArenaPillar(Vector2 Center, Vector2 HalfSize, float Height);

/// <summary>Game-owned state and collision; independent of Vulkan, pixels, and window input.</summary>
public sealed partial class BeaconGame : IDisposable
{
    public static IReadOnlyList<Vector2> BeaconPositions { get; } = Array.AsReadOnly<Vector2>(
        [new(-7, 3), new(6, 1), new(0, -7)]);

    public static IReadOnlyList<ArenaPillar> Pillars { get; } = Array.AsReadOnly<ArenaPillar>(
        [new(new(-3, 0), new(1, 1), 3), new(new(3, -3), new(1, 1), 4),
         new(new(1, 4), new(1, 1), 2), new(new(-5, -5), new(0.8f, 0.8f), 3.5f)]);

    public static Vector2 Exit { get; } = new(0, -10);

    private float verticalVelocity;
    private readonly CharacterMotor3D motor = new();
    private static readonly Lazy<SpatialWorld3D> ArenaQueries = new(() =>
        SceneSpatial3D.Build(SceneCompiler.Compile(BeaconScene.Arena())));
    private IRayQueryWorld3D? rayQueries;
    public SpatialWorld3D SpatialWorld => ArenaQueries.Value;
    public void UseRayQueries(IRayQueryWorld3D backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        rayQueries = backend;
    }
    public CharacterMove3D? LastMovement { get; private set; }

    public Vector2 Position
    {
        get
        {
            Vector3 point = agents["runner"].State.Position;
            return new Vector2(point.X, point.Z);
        }
        private set => SetPlayerPosition(value);
    }
    public float Yaw { get; private set; }
    public float Pitch { get; private set; } = -0.08f;
    public float Height => agents["runner"].State.Position.Y;
    public float Time { get; private set; }
    public int CollectedCount => BeaconPositions.Select((_, index) => IsCollected(index)).Count(value => value);
    public bool Won { get; private set; }
    public Vector3 Eye => new(Position.X, 1.6f + Height, Position.Y);

    public bool IsCollected(int index) => agents[$"beacon-{index}"].State.Collected;
    public Vector2 BeaconPosition(int index)
    {
        Vector3 point = agents[$"beacon-{index}"].State.Position;
        return new Vector2(point.X, point.Z);
    }
    public Vector3 Direction => new(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), -MathF.Cos(Yaw) * MathF.Cos(Pitch));

    public void Step(BeaconInput input, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0 || seconds > 0.05f)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "Game steps must be positive and at most 50 ms.");
        }
        float[] axes = [input.Forward, input.Strafe, input.Turn, input.Look];
        if (axes.Any(value => !float.IsFinite(value) || MathF.Abs(value) > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }
        if (!float.IsFinite(input.MouseYaw) || !float.IsFinite(input.MousePitch))
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }
        if (Won || Dead)
        {
            return;
        }
        Time += seconds;
        Yaw = MathF.IEEERemainder(Yaw + input.Turn * 2.4f * seconds + input.MouseYaw, MathF.Tau);
        Pitch = Math.Clamp(Pitch + input.Look * 1.4f * seconds + input.MousePitch, -1.3f, 1.3f);
        Vector2 forward = new(MathF.Sin(Yaw), -MathF.Cos(Yaw));
        Vector2 right = new(MathF.Cos(Yaw), MathF.Sin(Yaw));
        Vector2 movement = forward * input.Forward + right * input.Strafe;
        if (movement.LengthSquared() > 1)
        {
            movement = Vector2.Normalize(movement);
        }
        LastMovement = motor.Step(SpatialWorld, new(agents["runner"].State.Position, verticalVelocity),
            new Vector3(movement.X, 0, movement.Y) * 4, input.Jump, seconds);
        agents["runner"].State = agents["runner"].State with { Position = LastMovement.State.Feet };
        verticalVelocity = LastMovement.State.VerticalVelocity;
        for (int index = 0; index < BeaconPositions.Count; index++)
        {
            if (Vector2.Distance(Position, BeaconPosition(index)) < 1.1f)
            {
                Collect(index);
            }
        }
        if (combat)
        {
            StepCombat(input, seconds);
        }
        Won = !Dead && GateOpen && Vector2.Distance(Position, Exit) < 1.2f;
    }

    public Matrix4x4 Camera(float aspectRatio)
    {
        return Aurelian.Runtime.Camera3D.Matrix(Eye, Direction, aspectRatio);
    }

    public static bool CanStand(Vector2 position, float radius = 0.3f)
    {
        return ArenaQueries.Value.Overlap(Capsule3D.AtFeet(new(position.X, 0, position.Y), radius, 1.4f)).IsEmpty;
    }

    public void Dispose()
    {
        scene.Dispose();
    }
}

