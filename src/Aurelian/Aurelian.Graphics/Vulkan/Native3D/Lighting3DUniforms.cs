using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

internal static class Lighting3DUniforms
{
    public const uint ShadowSize = 1024;

    public static float[] Rows(Matrix4x4 matrix) =>
    [
        matrix.M11, matrix.M21, matrix.M31, matrix.M41,
        matrix.M12, matrix.M22, matrix.M32, matrix.M42,
        matrix.M13, matrix.M23, matrix.M33, matrix.M43,
        matrix.M14, matrix.M24, matrix.M34, matrix.M44,
    ];

    public static Matrix4x4 ShadowCamera(Graphics3DSettings settings, Vector3 eye)
    {
        Vector3 light = Vector3.Normalize(settings.SunDirection);
        Vector3 up = Math.Abs(Vector3.Dot(light, Vector3.UnitY)) > .95f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 center = new(eye.X, 0, eye.Z);
        float radius = settings.ShadowRadius;
        Matrix4x4 view = Matrix4x4.CreateLookAt(center + light * radius * 3, center, up);
        // Snap the center in light space to texels so camera translation does not crawl across the map.
        Vector3 origin = Vector3.Transform(Vector3.Zero, view);
        float texel = radius * 2 / ShadowSize;
        view.M41 += MathF.Round(origin.X / texel) * texel - origin.X;
        view.M42 += MathF.Round(origin.Y / texel) * texel - origin.Y;
        Matrix4x4 projection = Matrix4x4.CreateOrthographic(radius * 2, radius * 2, .1f, radius * 6);
        projection.M22 *= -1;
        return view * projection;
    }

    public static float[] Scene(Graphics3DSettings settings, Matrix4x4 shadow, bool shadowsAvailable)
    {
        Vector3 light = Vector3.Normalize(settings.SunDirection);
        return
        [
            light.X, light.Y, light.Z, .28f,
            settings.SunColor.X, settings.SunColor.Y, settings.SunColor.Z, settings.SunIntensity,
            settings.SkyAmbient.X, settings.SkyAmbient.Y, settings.SkyAmbient.Z, 0,
            settings.GroundAmbient.X, settings.GroundAmbient.Y, settings.GroundAmbient.Z, 0,
            .. Rows(shadow),
            settings.Shadows && shadowsAvailable ? 1 : 0, settings.ShadowBias, 1f / ShadowSize, 0,
        ];
    }
}
