using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Three overlapping, texel-stable, spherical frustum fits in world metres.</summary>
internal sealed record DirectionalShadowCascades3D(Matrix4x4[] Cameras, Vector3 Forward, Vector3 Ends, Vector3 Biases)
{
    public static DirectionalShadowCascades3D Create(Graphics3DSettings settings, Matrix4x4 camera, Vector3 eye)
    {
        if (!Matrix4x4.Invert(camera, out var inverse))
        {
            throw new ArgumentException("Directional shadows require an invertible camera.");
        }
        Vector4 endpoint = Vector4.Transform(new Vector4(0, 0, 1, 1), inverse);
        bool finiteFar = Math.Abs(endpoint.W) > .00000001f;
        Vector3 centerFar = finiteFar
            ? new Vector3(endpoint.X, endpoint.Y, endpoint.Z) / endpoint.W
            : Unproject(new(0, 0, .9999f), inverse);
        Vector3 forward = Vector3.Normalize(centerFar - eye);
        // A depth just below one gives a usable ray for infinite projection,
        // but must not silently impose its arbitrary distance as a coverage cap.
        float distance = finiteFar ? Math.Min(settings.ShadowDistance, Vector3.Distance(centerFar, eye)) : settings.ShadowDistance;
        Vector3 ends = new(distance * .16f, distance * .45f, distance);
        float[] splits = [0, ends.X, ends.Y, ends.Z];
        Matrix4x4[] cameras = new Matrix4x4[3];
        float[] biases = new float[3];
        // Perspective cameras share an eye. Orthographic cameras need their
        // separate parallel corner origins rather than rays from a single eye.
        bool perspective = Math.Abs(camera.M44) < .00001f || Math.Abs(camera.M14) + Math.Abs(camera.M24) + Math.Abs(camera.M34) > .00001f;
        for (int cascade = 0; cascade < 3; cascade++)
        {
            List<Vector3> corners = [];
            float start = splits[cascade] * .9f;
            float end = splits[cascade + 1];
            for (int y = -1; y <= 1; y += 2)
            {
                for (int x = -1; x <= 1; x += 2)
                {
                    Vector3 near = Unproject(new(x, y, 0), inverse);
                    Vector3 far = Unproject(new(x, y, .9999f), inverse);
                    Vector3 origin = perspective ? eye : near;
                    Vector3 ray = Vector3.Normalize(far - origin);
                    float alignment = Math.Max(Vector3.Dot(ray, forward), .001f);
                    float offset = Vector3.Dot(origin - eye, forward);
                    corners.Add(origin + ray * ((start - offset) / alignment));
                    corners.Add(origin + ray * ((end - offset) / alignment));
                }
            }
            Vector3 center = corners.Aggregate(Vector3.Zero, (sum, point) => sum + point) / corners.Count;
            float radius = MathF.Ceiling(corners.Max(point => Vector3.Distance(point, center)) * 16) / 16 + .1f;
            float depthSpan = (radius + settings.ShadowCasterPadding) * 2 + 1;
            Vector3 light = Vector3.Normalize(settings.SunDirection);
            Vector3 up = Math.Abs(Vector3.Dot(light, Vector3.UnitY)) > .95f ? Vector3.UnitZ : Vector3.UnitY;
            Matrix4x4 view = Matrix4x4.CreateLookAt(center + light * (depthSpan * .5f), center, up);
            float texel = radius * 2 / Lighting3DUniforms.ShadowSize;
            view.M41 = MathF.Round(view.M41 / texel) * texel;
            view.M42 = MathF.Round(view.M42 / texel) * texel;
            Matrix4x4 projection = Matrix4x4.CreateOrthographic(radius * 2, radius * 2, .1f, depthSpan);
            projection.M22 *= -1;
            cameras[cascade] = view * projection;
            // Receiver-plane PCF handles the slope. Keep only a small footprint
            // margin for quantization, rather than detaching grazing shadows.
            biases[cascade] = (settings.ShadowWorldBias + texel * .1f) / depthSpan;
        }
        return new(cameras, forward, ends, new(biases[0], biases[1], biases[2]));
    }

    private static Vector3 Unproject(Vector3 point, Matrix4x4 inverse)
    {
        Vector4 world = Vector4.Transform(new Vector4(point, 1), inverse);
        return new Vector3(world.X, world.Y, world.Z) / world.W;
    }
}
