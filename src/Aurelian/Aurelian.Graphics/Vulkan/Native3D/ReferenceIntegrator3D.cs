using System.Numerics;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Untextured opaque material evaluated by the real-time lighting module.
/// Capture currently admits emissive triangles and a black environment.</summary>
public sealed record ReferenceMaterial3D(Vector3 BaseColor, float Roughness = .5f,
    float Metallic = 0, float Dielectric = .04f, DirectDiffuseModel3D DiffuseModel = DirectDiffuseModel3D.Lambert,
    Vector3 Emission = default, bool TwoSidedEmission = false);

public sealed record ReferenceTriangle3D(Vector3 A, Vector3 B, Vector3 C, ReferenceMaterial3D Material);
public sealed record ReferenceSphere3D(Vector3 Center, float Radius, ReferenceMaterial3D Material);
public sealed record ReferenceAnalytic3D(AnalyticPrimitiveKind3D Kind, Matrix4x4 Transform, ReferenceMaterial3D Material);
public sealed record ReferenceRectLight3D(Vector3 Corner, Vector3 EdgeU, Vector3 EdgeV, Vector3 Radiance);
public sealed record ReferenceCamera3D(Vector3 Eye, Vector3 Target, Vector3 Up, float VerticalFieldOfViewRadians,
    float OrthographicHeight = 0);
public sealed record ReferenceCapture3D(int Width, int Height, int MaximumBounces = 8, uint Seed = 1,
    bool NextEventEstimation = true, bool PixelJitter = true);
public sealed record ReferenceImage3D(int Width, int Height, int Samples, float[] LinearRgb);

/// <summary>Explicit offline Capture session. Keeps geometry, ray packets, path state and sums
/// resident on the GPU. Nothing registers this work with the interactive frame renderer.</summary>
public sealed class ReferenceIntegrator3D : IDisposable
{
    private readonly VulkanRayQueryScene queries;
    private readonly ReferenceCapture3D capture;
    private int samples;
    private bool disposed;

    public int Samples => samples;
    public int DispatchCount => queries.DispatchCount;
    public double LastBatchGpuMilliseconds => queries.LastGpuMilliseconds;

    public ReferenceIntegrator3D(AurelianVulkanPlant plant, byte[] program,
        ReferenceCapture3D capture, ReferenceCamera3D camera, IReadOnlyList<ReferenceTriangle3D> triangles,
        IReadOnlyList<ReferenceSphere3D> spheres, ReferenceRectLight3D? light = null,
        IReadOnlyList<ReferenceAnalytic3D>? analytic = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(camera);
        analytic ??= [];
        if (capture.Width < 1 || capture.Height < 1 || (long)capture.Width * capture.Height > 1_048_576
            || capture.MaximumBounces is < 1 or > 16)
        {
            throw new ArgumentException("AUR-REF-001: Capture needs 1..1048576 pixels and 1..16 bounces.");
        }
        this.capture = capture;
        RequireFinite(camera.Eye);
        RequireFinite(camera.Target);
        RequireFinite(camera.Up);
        float viewLengthSquared = Vector3.DistanceSquared(camera.Eye, camera.Target);
        if (!float.IsFinite(camera.VerticalFieldOfViewRadians) || camera.VerticalFieldOfViewRadians <= 0
            || camera.VerticalFieldOfViewRadians >= MathF.PI
            || !float.IsFinite(viewLengthSquared) || viewLengthSquared < 1e-10f)
        {
            throw new ArgumentException("AUR-REF-002: Invalid capture camera.");
        }
        Vector3 forward = Vector3.Normalize(camera.Target - camera.Eye);
        Vector3 right = Vector3.Cross(forward, camera.Up);
        float rightLengthSquared = right.LengthSquared();
        if (!float.IsFinite(rightLengthSquared) || rightLengthSquared < 1e-10f)
            throw new ArgumentException("AUR-REF-002: Camera up cannot define a finite view basis.");
        right = Vector3.Normalize(right);
        Vector3 up = Vector3.Cross(right, forward);
        var geometry = triangles.Select(item => new RayQueryTriangle(item.A, item.B, item.C)).ToList();
        var materials = triangles.Select(item => item.Material).ToList();
        if (light is not null)
        {
            RequireFinite(light.Corner);
            RequireFinite(light.EdgeU);
            RequireFinite(light.EdgeV);
            RequireNonnegative(light.Radiance);
            float area = Vector3.Cross(light.EdgeU, light.EdgeV).Length();
            if (!float.IsFinite(area) || area < 1e-8f) throw new ArgumentException("AUR-REF-003: Degenerate emitter.");
            geometry.Add(new(light.Corner, light.Corner + light.EdgeU, light.Corner + light.EdgeV));
            geometry.Add(new(light.Corner + light.EdgeU, light.Corner + light.EdgeU + light.EdgeV, light.Corner + light.EdgeV));
            var emitter = new ReferenceMaterial3D(Vector3.Zero, Dielectric: 0, Emission: light.Radiance);
            materials.Add(emitter);
            materials.Add(emitter);
        }
        int emitterTable = 64 + (geometry.Count + spheres.Count + analytic.Count) * 16;
        float[] parameters = new float[emitterTable + geometry.Count];
        parameters[0] = capture.Width;
        parameters[1] = capture.Height;
        parameters[2] = capture.MaximumBounces;
        parameters[3] = BitConverter.UInt32BitsToSingle(capture.Seed);
        Write3(parameters, 4, camera.Eye);
        Write3(parameters, 8, forward);
        if (!float.IsFinite(camera.OrthographicHeight) || camera.OrthographicHeight < 0)
            throw new ArgumentException("AUR-REF-002: Invalid orthographic height.");
        float scale = camera.OrthographicHeight > 0 ? camera.OrthographicHeight * .5f : MathF.Tan(camera.VerticalFieldOfViewRadians * .5f);
        Write3(parameters, 12, right * (scale * capture.Width / capture.Height));
        Write3(parameters, 16, up * scale);
        parameters[35] = geometry.Count;
        parameters[36] = capture.NextEventEstimation ? 1 : 0;
        parameters[37] = camera.OrthographicHeight > 0 ? 1 : 0;
        parameters[38] = capture.PixelJitter ? 1 : 0;
        parameters[41] = emitterTable;
        float totalArea = 0;
        int emitterCount = 0;
        for (int index = 0; index < geometry.Count; index++)
        {
            WriteMaterial(parameters, index, materials[index]);
            if (materials[index].Emission != Vector3.Zero)
            {
                RayQueryTriangle triangle = geometry[index];
                float area = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).Length() * .5f;
                if (!float.IsFinite(area) || area < 1e-10f) throw new ArgumentException("AUR-REF-003: Degenerate emitter.");
                float nextArea = totalArea + area;
                if (!float.IsFinite(nextArea) || nextArea <= totalArea)
                    throw new ArgumentException("AUR-REF-009: Emission areas exceed float32 CDF precision.");
                totalArea = nextArea;
                parameters[64 + index * 16 + 10] = totalArea;
                parameters[emitterTable + emitterCount++] = index;
            }
        }
        if (emitterCount is < 1 or > 256 || !float.IsFinite(totalArea))
            throw new ArgumentException("AUR-REF-009: Capture admits 1..256 emissive triangles.");
        parameters[39] = totalArea;
        parameters[40] = emitterCount;
        var procedurals = new List<RayQueryAnalytic3D>(spheres.Count + analytic.Count);
        for (int index = 0; index < spheres.Count; index++)
        {
            ReferenceSphere3D sphere = spheres[index];
            if (!float.IsFinite(sphere.Radius) || sphere.Radius <= 0)
                throw new ArgumentException("AUR-REF-011: Sphere radius must be positive and finite.");
            if (spheres[index].Material.Emission != Vector3.Zero)
                throw new ArgumentException("AUR-REF-010: Emissive spheres need a matching light sampler.");
            WriteMaterial(parameters, geometry.Count + index, spheres[index].Material);
            procedurals.Add(new(AnalyticPrimitiveKind3D.UnitSphere,
                Matrix4x4.CreateScale(sphere.Radius) * Matrix4x4.CreateTranslation(sphere.Center)));
        }
        for (int index = 0; index < analytic.Count; index++)
        {
            ReferenceAnalytic3D primitive = analytic[index];
            if (primitive.Material.Emission != Vector3.Zero)
                throw new ArgumentException("AUR-REF-010: Emissive analytic surfaces need a matching light sampler.");
            WriteMaterial(parameters, geometry.Count + spheres.Count + index, primitive.Material);
            procedurals.Add(new(primitive.Kind, primitive.Transform));
        }
        queries = new(plant, program, geometry, null, capture.Width * capture.Height, parameters,
            capture.Width * capture.Height * 4, procedurals);
    }

    /// <summary>Submit a batch without reading back pixels. Different batch partitions
    /// preserve the same sample sequence and floating-point accumulation order.</summary>
    public void Accumulate(int additionalSamples)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (additionalSamples is < 1 or > 4096 || (long)samples + additionalSamples > 16_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(additionalSamples));
        }
        queries.AccumulateResident(additionalSamples);
        samples += additionalSamples;
    }

    public ReferenceImage3D Capture()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (samples == 0) throw new InvalidOperationException("AUR-REF-004: Accumulate samples before capture.");
        float[] sums = queries.ReadResidentAccumulation();
        float[] rgb = new float[capture.Width * capture.Height * 3];
        for (int pixel = 0; pixel < rgb.Length / 3; pixel++)
        {
            if (sums[pixel * 4 + 3] != samples) throw new InvalidOperationException("AUR-REF-005: GPU sample count mismatch.");
            for (int channel = 0; channel < 3; channel++)
            {
                float value = sums[pixel * 4 + channel] / samples;
                if (!float.IsFinite(value) || value < 0) throw new InvalidOperationException("AUR-REF-006: Invalid HDR radiance.");
                rgb[pixel * 3 + channel] = value;
            }
        }
        return new(capture.Width, capture.Height, samples, rgb);
    }

    private static void WriteMaterial(float[] data, int index, ReferenceMaterial3D material)
    {
        RequireNonnegative(material.BaseColor);
        RequireNonnegative(material.Emission);
        if (material.BaseColor.X > 1 || material.BaseColor.Y > 1 || material.BaseColor.Z > 1
            || !float.IsFinite(material.Roughness) || material.Roughness < .045f || material.Roughness > 1
            || !float.IsFinite(material.Metallic) || material.Metallic < 0 || material.Metallic > 1
            || !float.IsFinite(material.Dielectric) || material.Dielectric < 0 || material.Dielectric > 1
            || !Enum.IsDefined(material.DiffuseModel))
        {
            throw new ArgumentException("AUR-REF-007: Unsupported material parameters.");
        }
        int offset = 64 + index * 16;
        Write3(data, offset, material.BaseColor);
        data[offset + 3] = material.Roughness;
        Write3(data, offset + 4, material.Emission);
        data[offset + 7] = material.Metallic;
        data[offset + 8] = material.Dielectric;
        data[offset + 9] = (float)material.DiffuseModel;
        data[offset + 12] = material.TwoSidedEmission ? 1 : 0;
    }

    private static void Write3(float[] data, int offset, Vector3 value)
    {
        RequireFinite(value);
        data[offset] = value.X;
        data[offset + 1] = value.Y;
        data[offset + 2] = value.Z;
    }

    private static void RequireFinite(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentException("AUR-REF-008: Values must be finite.");
    }

    private static void RequireNonnegative(Vector3 value)
    {
        RequireFinite(value);
        if (value.X < 0 || value.Y < 0 || value.Z < 0) throw new ArgumentException("AUR-REF-008: Negative radiance or reflectance.");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        queries.Dispose();
    }
}
