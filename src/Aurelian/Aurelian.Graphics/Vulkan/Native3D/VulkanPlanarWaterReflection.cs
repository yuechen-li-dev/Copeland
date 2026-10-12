using System.Numerics;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Shaders;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>One explicitly budgeted reflected opaque view on the same plant.
/// An oblique near plane clips submerged geometry on the GPU. Water, glass and
/// alpha surfaces are excluded, avoiding recursive transport.</summary>
internal sealed class VulkanPlanarWaterReflection : IDisposable
{
    private readonly VulkanNativeFrameTarget target;
    private readonly VulkanSolid3DRenderer renderer;
    public PlanarWaterReflection3D Configuration { get; }
    public Matrix4x4 Clip { get; private set; }
    public double GpuMilliseconds { get; private set; }
    public AurelianVulkanTexture Color => renderer.UnfoggedReflectionOutput;

    public VulkanPlanarWaterReflection(AurelianVulkanPlant plant, uint width, uint height,
        PlanarWaterReflection3D configuration, CompiledGraphicsProgram solid, CompiledGraphicsProgram model,
        CompiledGraphicsProgram? shadow, CompiledGraphicsProgram? output, SurfaceLightingPrograms programs)
    {
        Configuration = configuration;
        target = new(plant, Math.Max(1, width / (uint)configuration.ResolutionDivisor),
            Math.Max(1, height / (uint)configuration.ResolutionDivisor));
        try
        {
            renderer = new(plant, solid, target, modelProgram: model, shadowProgram: shadow,
                outputProgram: output, surfacePrograms: programs with
                {
                    RefractiveModel = null,
                    RefractionResolve = null,
                    WaterTemporal = null,
                    TransparentModel = null,
                    TransparencyResolve = null,
                });
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    public void Render(Native3DScene scene, Matrix4x4 camera, Vector3 eye, NativeFrameClearColor clear,
        Graphics3DSettings settings, EnvironmentLighting? environment, ReflectionProbe3D? probe,
        IReadOnlyList<LocalLight3D> lights)
    {
        var mirror = Matrix4x4.CreateReflection(new Plane(Vector3.UnitY, -Configuration.Height));
        var clip = mirror * camera;
        var plane = new Vector4(0, 1, 0, -Configuration.Height - .002f);
        if (!Matrix4x4.Invert(clip, out var inverse))
        {
            throw new ArgumentException("Planar reflection requires an invertible perspective camera.");
        }
        Vector4 clipPlane = Vector4.Transform(plane, Matrix4x4.Transpose(inverse));
        Vector4 corner = Vector4.Transform(new Vector4(Math.Sign(clipPlane.X), Math.Sign(clipPlane.Y), 1, 1), inverse);
        float divisor = Vector4.Dot(plane, corner);
        if (!float.IsFinite(divisor) || divisor <= .00001f)
        {
            throw new NotSupportedException("AUR-WATER-007: Reflection clip plane does not intersect this perspective frustum.");
        }
        Vector4 near = plane / divisor;
        clip.M13 = near.X;
        clip.M23 = near.Y;
        clip.M33 = near.Z;
        clip.M43 = near.W;
        Clip = clip;
        renderer.Settings = settings with
        {
            AntiAliasing = AntiAliasing3D.None,
            ToneMapping = false,
            Exposure = 1,
            BloomIntensity = 0,
            Fog = new(),
            Volumetrics = new(),
            SurfaceDebugView = SurfaceDebugView3D.Shaded,
        };
        renderer.Environment = environment;
        renderer.ReflectionProbe = probe;
        renderer.LocalLights = lights;
        var reflected = new Native3DScene(Reverse(scene.Geometry), scene.Models
            .Where(batch => !batch.Material.AlphaBlend && batch.Material.Transmission == 0)
            .Select(batch => batch with { Vertices = Reverse(batch.Vertices) }).ToArray())
        {
            LightingRevision = scene.LightingRevision,
            TemporalRevision = scene.TemporalRevision,
        };
        // Alpha marks actual reflected geometry. The environment remains the
        // source for sky pixels instead of replacing it with the clear color.
        var reflectionClear = new NativeFrameClearColor(clear.Red, clear.Green, clear.Blue, 0);
        var result = renderer.Render(reflected, clip, Vector3.Transform(eye, mirror), reflectionClear);
        GpuMilliseconds = result.GpuPassTimes.Sum(time => time.Milliseconds);
    }

    private static T[] Reverse<T>(IReadOnlyList<T> source)
    {
        var result = new T[source.Count];
        for (int first = 0; first < source.Count; first += 3)
        {
            result[first] = source[first];
            result[first + 1] = source[first + 2];
            result[first + 2] = source[first + 1];
        }
        return result;
    }

    public void Dispose()
    {
        renderer.Dispose();
        target.Dispose();
    }
}
