using System.Numerics;

namespace Aurelian.World.Scenes;

/// <summary>Local scale, then rotation, then translation. World coordinates are metres, Y up.</summary>
public readonly record struct SceneTransform(Vector3 Position, Quaternion Rotation, Vector3 Scale)
{
    public static SceneTransform Identity => new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    public static SceneTransform At(Vector3 position) => Identity with { Position = position };

    public Matrix4x4 Matrix()
    {
        if (!Finite(Position) || !Finite(Scale) || Scale.X <= 0 || Scale.Y <= 0 || Scale.Z <= 0
            || !float.IsFinite(Rotation.X) || !float.IsFinite(Rotation.Y)
            || !float.IsFinite(Rotation.Z) || !float.IsFinite(Rotation.W)
            || MathF.Abs(Rotation.LengthSquared() - 1) > 0.0001f)
        {
            throw new InvalidDataException("Scene transforms require finite positions, positive scales and unit quaternions.");
        }

        Matrix4x4 matrix = Matrix4x4.CreateScale(Scale)
            * Matrix4x4.CreateFromQuaternion(Rotation)
            * Matrix4x4.CreateTranslation(Position);
        ValidateMatrix(matrix);
        return matrix;
    }

    public static void ValidateMatrix(Matrix4x4 matrix)
    {
        float[] values = [matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44];
        if (values.Any(value => !float.IsFinite(value)) || matrix.M14 != 0 || matrix.M24 != 0
            || matrix.M34 != 0 || matrix.M44 != 1 || !float.IsFinite(matrix.GetDeterminant()) || matrix.GetDeterminant() <= 0
            || !Matrix4x4.Invert(matrix, out Matrix4x4 inverse))
        {
            throw new InvalidDataException("Scene world transforms must be finite, invertible, orientation-preserving affine matrices.");
        }
        float[] inverseValues = [inverse.M11, inverse.M12, inverse.M13, inverse.M14,
            inverse.M21, inverse.M22, inverse.M23, inverse.M24,
            inverse.M31, inverse.M32, inverse.M33, inverse.M34,
            inverse.M41, inverse.M42, inverse.M43, inverse.M44];
        if (inverseValues.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidDataException("Scene world transform inverse is not finite.");
        }
    }

    internal static bool Finite(Vector3 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}

public sealed record ScenePlacement(string Id, string Name, Matrix4x4 WorldTransform)
{
    public Vector3 Position => WorldTransform.Translation;

    /// <summary>Preserves authored scale and orientation while replacing the runtime world position.</summary>
    public Matrix4x4 At(Vector3 position)
    {
        Matrix4x4 result = WorldTransform;
        result.Translation = position;
        SceneTransform.ValidateMatrix(result);
        return result;
    }
}
