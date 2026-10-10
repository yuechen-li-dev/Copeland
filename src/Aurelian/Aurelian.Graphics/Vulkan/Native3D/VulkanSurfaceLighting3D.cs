using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Retained surface lighting, half-resolution horizon AO and GPU conservative light tiles.</summary>
internal sealed class VulkanSurfaceLighting3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly Stack<IDisposable> owned = new();
    private readonly VulkanTextureUploader uploader;
    private readonly AurelianVulkanTexture lightData;
    private readonly AurelianVulkanTexture tiles;
    private readonly AurelianVulkanTexture rawAo;
    private readonly AurelianVulkanTexture filteredAo;
    private readonly AurelianVulkanTexture fallback;
    private readonly VulkanPostProcess3D tilePass;
    private readonly VulkanPostProcess3D aoPass;
    private readonly VulkanPostProcess3D denoisePass;
    private readonly VulkanPostProcess3D resolvePass;
    private AurelianVulkanTexture? environment;
    private string? environmentKey;
    private LocalLight3D[] priorLights = [];
    private readonly float[] packedLights = new float[32 * 16];
    private readonly AurelianVulkanTexture[] spotMaps = new AurelianVulkanTexture[2];
    private readonly Vulkan3DPass[] spotPasses = new Vulkan3DPass[2];
    private readonly Matrix4x4[] spotCameras = [Matrix4x4.Identity, Matrix4x4.Identity];
    private ReflectionProbe3D? probe;
    public int ShadowCount { get; private set; }

    public VulkanSurfaceLighting3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool commands, VulkanFenceBundle fences, SurfaceLightingPrograms programs,
        CompiledGraphicsProgram shadowProgram, uint width, uint height)
    {
        this.plant = plant;
        this.allocator = allocator;
        uploader = Own(new VulkanTextureUploader(plant, allocator, commands, fences));
        try
        {
            Normal = Image(width, height, "surface.normal", VulkanTextureFormat.Rgba16Float);
            Emission = Image(width, height, "surface.emission", VulkanTextureFormat.Rgba16Float);
            Output = Image(width, height, "surface.lighting", VulkanTextureFormat.Rgba16Float);
            rawAo = Image((width + 1) / 2, (height + 1) / 2, "surface.ao", VulkanTextureFormat.Rgba32Float);
            filteredAo = Image(rawAo.Width, rawAo.Height, "surface.filtered-ao", VulkanTextureFormat.Rgba32Float);
            tiles = Image((width + 15) / 16, (height + 15) / 16, "surface.light-tiles", VulkanTextureFormat.Rgba32Float);
            lightData = Data(4, 32, "surface.lights");
            fallback = Data(1, 1, "surface.no-data");
            Upload(lightData, packedLights);
            Upload(fallback, [0, 0, 0, 1]);
            tilePass = Own(new VulkanPostProcess3D(plant, allocator, programs.LightTiles, tiles, 1, Filter.Nearest));
            aoPass = Own(new VulkanPostProcess3D(plant, allocator, programs.AmbientOcclusion, rawAo, 2, Filter.Nearest));
            denoisePass = Own(new VulkanPostProcess3D(plant, allocator, programs.AmbientDenoise, filteredAo, 2, Filter.Nearest));
            resolvePass = Own(new VulkanPostProcess3D(plant, allocator, programs.Resolve, Output, 12, Filter.Nearest));
            float[] emptyShadow = new float[512 * 512];
            Array.Fill(emptyShadow, 1);
            for (int index = 0; index < 2; index++)
            {
                spotMaps[index] = Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, 512, 512,
                    VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination,
                    VulkanMemoryUsage.GpuOnly, "surface.spot-shadow", VulkanTextureFormat.R32Float));
                Upload(spotMaps[index], emptyShadow);
                spotPasses[index] = Own(new Vulkan3DPass(plant, allocator, shadowProgram, spotMaps[index], 512, 512, true));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public AurelianVulkanTexture Normal { get; }
    public AurelianVulkanTexture Emission { get; }
    public AurelianVulkanTexture Output { get; }

    public bool Prepare(EnvironmentLighting? asset, ReflectionProbe3D? reflectionProbe, IReadOnlyList<LocalLight3D> lights, int shadowBudget)
    {
        if (lights.Count > 32)
        {
            throw new ArgumentException("AUR-LIGHT-LOCAL-001: This light tile contract admits at most 32 local lights.");
        }
        foreach (var light in lights)
        {
            light.Validate();
        }
        reflectionProbe?.Validate();
        if (reflectionProbe is not null && asset is null)
        {
            throw new ArgumentException("AUR-ENV-003: A reflection probe requires a compiled environment captured at its origin.");
        }
        if (lights.Count(light => light.CastShadows) > shadowBudget)
        {
            throw new ArgumentException("AUR-LIGHT-LOCAL-003: Requested spot shadows exceed the declared local shadow budget.");
        }
        bool environmentChanged = environmentKey != asset?.ContentKey;
        bool changed = environmentChanged || probe != reflectionProbe;
        probe = reflectionProbe;
        if (environmentChanged)
        {
            AurelianVulkanTexture? replacement = null;
            try
            {
                if (asset is not null)
                {
                    replacement = Data(EnvironmentLighting.Size, EnvironmentLighting.Size * EnvironmentLighting.Bands, "surface.environment");
                    Upload(replacement, asset.Pixels.AsSpan());
                }
                environment?.Dispose();
                environment = replacement;
                environmentKey = asset?.ContentKey;
            }
            catch
            {
                replacement?.Dispose();
                throw;
            }
        }
        if (!lights.SequenceEqual(priorLights))
        {
            Array.Clear(packedLights);
            ShadowCount = 0;
            for (int index = 0; index < lights.Count; index++)
            {
                var light = lights[index];
                Vector3 direction = Vector3.Normalize(light.Direction);
                int offset = index * 16;
                packedLights[offset] = light.Position.X;
                packedLights[offset + 1] = light.Position.Y;
                packedLights[offset + 2] = light.Position.Z;
                packedLights[offset + 3] = light.Range;
                packedLights[offset + 4] = light.Color.X;
                packedLights[offset + 5] = light.Color.Y;
                packedLights[offset + 6] = light.Color.Z;
                packedLights[offset + 7] = light.Intensity;
                packedLights[offset + 8] = direction.X;
                packedLights[offset + 9] = direction.Y;
                packedLights[offset + 10] = direction.Z;
                packedLights[offset + 11] = MathF.Cos(light.OuterAngle);
                packedLights[offset + 12] = MathF.Cos(light.InnerAngle);
                packedLights[offset + 13] = light.Kind == LocalLightKind.Spot ? 1 : 0;
                if (light.CastShadows)
                {
                    packedLights[offset + 14] = ShadowCount + 1;
                    Vector3 up = Math.Abs(Vector3.Dot(direction, Vector3.UnitY)) > .95f ? Vector3.UnitZ : Vector3.UnitY;
                    Matrix4x4 view = Matrix4x4.CreateLookAt(light.Position, light.Position + direction, up);
                    Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(light.OuterAngle * 2, 1, .05f, light.Range);
                    projection.M22 *= -1;
                    spotCameras[ShadowCount++] = view * projection;
                }
            }
            Upload(lightData, packedLights);
            priorLights = lights.ToArray();
        }
        return changed;
    }

    public Vulkan3DPass ShadowPass(int index)
    {
        return spotPasses[index];
    }

    public Matrix4x4 ShadowCamera(int index)
    {
        return spotCameras[index];
    }

    public void FinishShadow(VulkanCommandBufferLease command, int index)
    {
        Vulkan3DPass.SampleAfterRendering(plant, command, spotMaps[index]);
    }

    public AurelianVulkanTexture Record(VulkanCommandBufferLease command, AurelianVulkanBuffer triangle,
        AurelianVulkanTexture surface, AurelianVulkanTexture motion, AurelianVulkanTexture shadow,
        AurelianVulkanTexture? diffuse, Matrix4x4 clip, Matrix4x4 inverse, Vector3 eye,
        Graphics3DSettings settings, float[] lighting, bool useDiffuse, Vulkan3DGpuTimings? timings)
    {
        float[] inverseRows = Lighting3DUniforms.Rows(inverse);
        timings?.Mark(command, 10);
        aoPass.Configure([.. inverseRows, 1f / surface.Width, 1f / surface.Height,
            settings.AmbientOcclusionRadius, settings.AmbientOcclusionStrength], motion, Normal);
        aoPass.Record(command, triangle);
        denoisePass.Configure([.. inverseRows, 1f / rawAo.Width, 1f / rawAo.Height,
            settings.AmbientOcclusionRadius, settings.AmbientOcclusionStrength,
            1f / surface.Width, 1f / surface.Height, 0, 0], rawAo, Normal);
        denoisePass.Record(command, triangle);
        timings?.Mark(command, 11);
        timings?.Mark(command, 12);
        tilePass.Configure([.. Lighting3DUniforms.Rows(clip), priorLights.Length, 1f / tiles.Width, 1f / tiles.Height, 0], lightData);
        tilePass.Record(command, triangle);
        timings?.Mark(command, 13);
        timings?.Mark(command, 14);
        resolvePass.Configure([.. inverseRows, eye.X, eye.Y, eye.Z, 1, .. lighting,
            priorLights.Length, environment is null ? 0 : 1, settings.EnvironmentIntensity, useDiffuse ? 1 : 0,
            clear.X, clear.Y, clear.Z, clear.W,
            1f / rawAo.Width, 1f / rawAo.Height, settings.AmbientOcclusionStrength > 0 ? settings.AmbientOcclusionRadius : 0,
            settings.LocalLightCulling ? 0 : 1,
            1f / surface.Width, 1f / surface.Height, 0, 0,
            probe?.Position.X ?? 0, probe?.Position.Y ?? 0, probe?.Position.Z ?? 0, 0,
            probe?.HalfSize.X ?? 1, probe?.HalfSize.Y ?? 1, probe?.HalfSize.Z ?? 1, probe is null ? 0 : 1,
            .. Lighting3DUniforms.Rows(spotCameras[0]), .. Lighting3DUniforms.Rows(spotCameras[1])],
            surface, motion, Normal, Emission, environment ?? fallback, lightData, tiles, shadow, diffuse ?? fallback, filteredAo,
            spotMaps[0], spotMaps[1]);
        resolvePass.SetLinearInput(4, environment ?? fallback);
        resolvePass.Record(command, triangle);
        timings?.Mark(command, 15);
        return Output;
    }

    private Vector4 clear;
    public void SetClear(Vector4 value)
    {
        clear = value;
    }

    private AurelianVulkanTexture Image(uint width, uint height, string name, VulkanTextureFormat format)
    {
        return Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
            VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource, VulkanMemoryUsage.GpuOnly, name, format));
    }

    private AurelianVulkanTexture Data(uint width, uint height, string name)
    {
        return VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
            VulkanTextureUsage.TransferDestination | VulkanTextureUsage.ShaderResource, VulkanMemoryUsage.GpuOnly,
            name, VulkanTextureFormat.Rgba32Float);
    }

    private void Upload(AurelianVulkanTexture texture, ReadOnlySpan<float> values)
    {
        var result = uploader.Upload(new(texture, MemoryMarshal.AsBytes(values).ToArray(), "surface.data"));
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        }
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        owned.Push(resource);
        return resource;
    }

    public void Dispose()
    {
        environment?.Dispose();
        while (owned.TryPop(out var resource))
        {
            resource.Dispose();
        }
        lightData?.Dispose();
        fallback?.Dispose();
    }
}
