using System.Numerics;

namespace Aurelian.Beacon3D;

public readonly record struct BeaconInput(float Forward, float Strafe, float Turn, float Look, bool Jump = false);

public readonly record struct ArenaPillar(Vector2 Center, Vector2 HalfSize, float Height);

/// <summary>Game-owned state and collision; independent of Vulkan, pixels, and window input.</summary>
public sealed class BeaconGame
{
    public static IReadOnlyList<Vector2> BeaconPositions { get; } = Array.AsReadOnly<Vector2>(
        [new(-7, 3), new(6, 1), new(0, -7)]);

    public static IReadOnlyList<ArenaPillar> Pillars { get; } = Array.AsReadOnly<ArenaPillar>(
        [new(new(-3, 0), new(1, 1), 3), new(new(3, -3), new(1, 1), 4),
         new(new(1, 4), new(1, 1), 2), new(new(-5, -5), new(0.8f, 0.8f), 3.5f)]);

    public static Vector2 Exit { get; } = new(0, -10);

    private readonly bool[] collected = new bool[BeaconPositions.Count];
    private float verticalVelocity;

    public Vector2 Position { get; private set; } = new(0, 9);
    public float Yaw { get; private set; }
    public float Pitch { get; private set; } = -0.08f;
    public float Height { get; private set; }
    public float Time { get; private set; }
    public int CollectedCount => collected.Count(value => value);
    public bool Won { get; private set; }
    public Vector3 Eye => new(Position.X, 1.6f + Height, Position.Y);

    public bool IsCollected(int index) => collected[index];

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
        if (Won)
        {
            return;
        }
        Time += seconds;
        Yaw = MathF.IEEERemainder(Yaw + input.Turn * 2.4f * seconds, MathF.Tau);
        Pitch = Math.Clamp(Pitch + input.Look * 1.4f * seconds, -0.9f, 0.9f);
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
        for (int index = 0; index < collected.Length; index++)
        {
            if (Vector2.Distance(Position, BeaconPositions[index]) < 1.1f)
            {
                collected[index] = true;
            }
        }
        Won = CollectedCount == collected.Length && Vector2.Distance(Position, Exit) < 1.2f;
    }

    public Matrix4x4 Camera(float aspectRatio)
    {
        Vector3 direction = new(
            MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), -MathF.Cos(Yaw) * MathF.Cos(Pitch));
        Matrix4x4 view = Matrix4x4.CreateLookAt(Eye, Eye + direction, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, aspectRatio, 0.1f, 80);
        // System.Numerics is right-handed with depth [0,1]. Vulkan's positive viewport points Y down.
        projection.M22 = -projection.M22;
        return view * projection;
    }

    private static bool CanStand(Vector2 position)
    {
        const float radius = 0.3f;
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
