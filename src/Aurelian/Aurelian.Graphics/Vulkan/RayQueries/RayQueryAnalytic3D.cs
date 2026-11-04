using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.RayQueries;

/// <summary>An immutable canonical analytic surface under an invertible affine transform.
/// The AABB is traversal coverage; the shared typed shader owns the actual surface.</summary>
public sealed class RayQueryAnalytic3D
{
    public AnalyticPrimitiveKind3D Kind { get; }
    public Matrix4x4 Transform { get; }
    public Matrix4x4 InverseTransform { get; }
    public Vector3 Minimum { get; }
    public Vector3 Maximum { get; }
    public uint Layer { get; }
    public uint Mask { get; }

    public RayQueryAnalytic3D(AnalyticPrimitiveKind3D kind, Matrix4x4 transform,
        uint layer = 1, uint mask = uint.MaxValue)
    {
        if (kind is < AnalyticPrimitiveKind3D.UnitSphere or > AnalyticPrimitiveKind3D.Capsule
            || !Finite(transform) || transform.M14 != 0 || transform.M24 != 0 || transform.M34 != 0 || transform.M44 != 1
            || !Matrix4x4.Invert(transform, out Matrix4x4 inverse) || !Finite(inverse))
        {
            throw new ArgumentException("AUR-RAY-ANALYTIC-001: Expected a canonical kind and finite invertible affine transform.");
        }
        Kind = kind;
        Transform = transform;
        InverseTransform = inverse;
        Layer = layer;
        Mask = mask;
        double extentX = kind == AnalyticPrimitiveKind3D.Capsule ? .5 : 1;
        double extentZ = extentX;
        double x = Math.Abs((double)transform.M11) * extentX + Math.Abs((double)transform.M21) + Math.Abs((double)transform.M31) * extentZ;
        double y = Math.Abs((double)transform.M12) * extentX + Math.Abs((double)transform.M22) + Math.Abs((double)transform.M32) * extentZ;
        double z = Math.Abs((double)transform.M13) * extentX + Math.Abs((double)transform.M23) + Math.Abs((double)transform.M33) * extentZ;
        Minimum = new(Outward(transform.M41 - x, false), Outward(transform.M42 - y, false), Outward(transform.M43 - z, false));
        Maximum = new(Outward(transform.M41 + x, true), Outward(transform.M42 + y, true), Outward(transform.M43 + z, true));
    }

    internal void Pack(Span<float> words)
    {
        Matrix4x4 inverse = InverseTransform;
        words[0] = inverse.M11;
        words[1] = inverse.M21;
        words[2] = inverse.M31;
        words[3] = inverse.M41;
        words[4] = inverse.M12;
        words[5] = inverse.M22;
        words[6] = inverse.M32;
        words[7] = inverse.M42;
        words[8] = inverse.M13;
        words[9] = inverse.M23;
        words[10] = inverse.M33;
        words[11] = inverse.M43;
        words[12] = BitConverter.UInt32BitsToSingle((uint)Kind);
        words[13] = BitConverter.UInt32BitsToSingle(Layer);
        words[14] = BitConverter.UInt32BitsToSingle(Mask);
        words[15] = 0;
    }

    private static float Outward(double value, bool upper)
    {
        float bound = (float)value;
        // Cover transform/intersection rounding as well as float conversion.
        for (int step = 0; step < 8; step++)
        {
            bound = upper ? float.BitIncrement(bound) : float.BitDecrement(bound);
        }
        if (!float.IsFinite(bound)) throw new ArgumentException("AUR-RAY-ANALYTIC-001: Analytic bounds exceed float32.");
        return bound;
    }

    private static bool Finite(Matrix4x4 matrix)
    {
        float[] values = [matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44];
        return values.All(float.IsFinite);
    }
}
