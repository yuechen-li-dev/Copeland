using System.Numerics;

namespace Aurelian.Beacon3D;

public readonly record struct BeaconInput(float Forward, float Strafe, float Turn, float Look, bool Jump = false,
    float MouseYaw = 0, float MousePitch = 0, bool Fire = false, bool Reload = false);

public readonly record struct ArenaPillar(Vector2 Center, Vector2 HalfSize, float Height);

/// <summary>Game-owned state and collision; independent of Vulkan, pixels, and window input.</summary>
public sealed partial class BeaconGame
{
    public static IReadOnlyList<Vector2> BeaconPositions { get; } = Array.AsReadOnly<Vector2>(
        [new(-7, 3), new(6, 1), new(0, -7)]);

    public static IReadOnlyList<ArenaPillar> Pillars { get; } = Array.AsReadOnly<ArenaPillar>(
        [new(new(-3, 0), new(1, 1), 3), new(new(3, -3), new(1, 1), 4),
         new(new(1, 4), new(1, 1), 2), new(new(-5, -5), new(0.8f, 0.8f), 3.5f)]);

    public static Vector2 Exit { get; } = new(0, -10);

    private float verticalVelocity;

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
    public float Height { get; private set; }
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
        Vector2 next = Position + movement * (4 * seconds);
        // Axis separation gives sliding without allowing diagonal penetration.
        Vector2 xStep = new(next.X, Position.Y);
        if (CanStand(xStep))
        {
            Position = xStep;
        }
        Vector2 zStep = new(Position.X, next.Y);
        if (CanStand(zStep))
        {
            Position = zStep;
        }
        if (input.Jump && Height == 0)
        {
            verticalVelocity = 5;
        }
        verticalVelocity -= 12 * seconds;
        Height = MathF.Max(0, Height + verticalVelocity * seconds);
        if (Height == 0)
        {
            verticalVelocity = 0;
        }
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
        if (MathF.Abs(position.X) > 10.7f || MathF.Abs(position.Y) > 10.7f)
        {
            return false;
        }
        foreach (ArenaPillar pillar in Pillars)
        {
            Vector2 closest = Vector2.Clamp(position, pillar.Center - pillar.HalfSize, pillar.Center + pillar.HalfSize);
            if (Vector2.DistanceSquared(position, closest) < radius * radius)
            {
                return false;
            }
        }
        return true;
    }
}

