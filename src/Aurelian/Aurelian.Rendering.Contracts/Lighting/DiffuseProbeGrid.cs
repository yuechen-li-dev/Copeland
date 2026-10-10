using System.Numerics;

namespace Aurelian.Rendering.Contracts.Lighting;

/// <summary>A bounded experimental diffuse domain in game metres. Irradiance is stored divided by pi.</summary>
public sealed record DiffuseProbeGrid(Vector3 Minimum, Vector3 Maximum, int X = 6, int Y = 4, int Z = 6,
    int RaysPerProbe = 64, int SurfaceResolution = 8)
{
    public int Count => checked(X * Y * Z);

    public void Validate()
    {
        if (!Finite(Minimum) || !Finite(Maximum) || Maximum.X <= Minimum.X || Maximum.Y <= Minimum.Y
            || Maximum.Z <= Minimum.Z || X is < 2 or > 8 || Y is < 2 or > 8 || Z is < 2 or > 8
            || RaysPerProbe is < 16 or > 128 || SurfaceResolution is < 2 or > 32)
        {
            throw new ArgumentException("AUR-PROBE-001: Invalid bounded probe grid or sampling budget.");
        }
    }

    public Vector3 Position(int index)
    {
        if (index < 0 || index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        Vector3 fraction = new((float)(index % X) / (X - 1), (float)(index / X % Y) / (Y - 1),
            (float)(index / (X * Y)) / (Z - 1));
        return Minimum + fraction * (Maximum - Minimum);
    }

    public int[] Corners(Vector3 point, Vector3 normal)
    {
        Vector3 spacing = (Maximum - Minimum) / new Vector3(X - 1, Y - 1, Z - 1);
        Vector3 coordinate = (point + normal * .03f - Minimum) / spacing;
        int x = (int)MathF.Floor(Math.Clamp(coordinate.X, 0, X - 1.0001f));
        int y = (int)MathF.Floor(Math.Clamp(coordinate.Y, 0, Y - 1.0001f));
        int z = (int)MathF.Floor(Math.Clamp(coordinate.Z, 0, Z - 1.0001f));
        var result = new int[8];
        for (int corner = 0; corner < 8; corner++)
        {
            result[corner] = x + corner % 2 + (y + corner / 2 % 2) * X + (z + corner / 4) * X * Y;
        }
        return result;
    }

    public static bool Finite(Vector3 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}

public sealed record DiffuseProbeTriangle(Vector3 A, Vector3 B, Vector3 C, Vector3 Albedo, Vector3 Emission)
{
    public Vector3 Normal => Vector3.Normalize(Vector3.Cross(B - A, C - A));

    public void Validate()
    {
        if (!DiffuseProbeGrid.Finite(A) || !DiffuseProbeGrid.Finite(B) || !DiffuseProbeGrid.Finite(C)
            || Vector3.Cross(B - A, C - A).LengthSquared() < 1e-12f
            || !DiffuseProbeGrid.Finite(Albedo) || Albedo.X < 0 || Albedo.Y < 0 || Albedo.Z < 0
            || Albedo.X > 1 || Albedo.Y > 1 || Albedo.Z > 1
            || !DiffuseProbeGrid.Finite(Emission) || Emission.X < 0 || Emission.Y < 0 || Emission.Z < 0
            || Emission.X > 1000 || Emission.Y > 1000 || Emission.Z > 1000)
        {
            throw new ArgumentException("AUR-PROBE-002: Only finite, opaque, constant Lambertian triangles are admitted.");
        }
    }
}

public sealed record DiffuseProbeLight(Vector3 Position, Vector3 Color, float Intensity)
{
    public void Validate()
    {
        if (!DiffuseProbeGrid.Finite(Position) || !DiffuseProbeGrid.Finite(Color)
            || Color.X < 0 || Color.Y < 0 || Color.Z < 0 || Color.X > 100 || Color.Y > 100 || Color.Z > 100
            || !float.IsFinite(Intensity) || Intensity < 0 || Intensity > 1000)
        {
            throw new ArgumentException("AUR-PROBE-003: Invalid point light.");
        }
    }
}
