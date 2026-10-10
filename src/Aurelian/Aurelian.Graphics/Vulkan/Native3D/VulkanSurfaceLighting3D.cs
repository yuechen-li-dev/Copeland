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

/// <summary>Retained surface lighting, cascades, horizon AO, light tiles, height fog and diffuse diffusion.</summary>
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
    private readonly AurelianVulkanTexture diffuseOutput;
    private readonly AurelianVulkanTexture[] cascadeMaps = new AurelianVulkanTexture[2];
    private readonly Vulkan3DPass[] cascadePasses = new Vulkan3DPass[2];
    private readonly VulkanPostProcess3D? fogPass;
    private readonly VulkanVolumeLighting3D? volumeLighting;
    public AurelianVulkanTexture? UnfoggedOutput { get; private set; }
    private readonly VulkanPostProcess3D? diffusionHorizontal;
    private readonly VulkanPostProcess3D? diffusionVertical;
    private readonly VulkanPostProcess3D? diffusionMerge;
    private readonly AurelianVulkanTexture? fogOutput;
    private readonly AurelianVulkanTexture? diffuseHorizontal;
    private readonly AurelianVulkanTexture? diffuseVertical;
    private readonly AurelianVulkanTexture? subsurfaceOutput;
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
            Subsurface = Image(width, height, "surface.subsurface", VulkanTextureFormat.Rgba16Float);
            diffuseOutput = Image(width, height, "surface.diffuse", VulkanTextureFormat.Rgba16Float);
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
            resolvePass = Own(new VulkanPostProcess3D(plant, allocator, programs.Resolve, Output, 15, Filter.Nearest, diffuseOutput));
            for (int index = 0; index < 2; index++)
            {
                cascadeMaps[index] = Image(Lighting3DUniforms.ShadowSize, Lighting3DUniforms.ShadowSize,
                    "surface.directional-cascade", VulkanTextureFormat.R32Float);
                cascadePasses[index] = Own(new Vulkan3DPass(plant, allocator, shadowProgram, cascadeMaps[index],
                    Lighting3DUniforms.ShadowSize, Lighting3DUniforms.ShadowSize, true));
            }
            if (programs.HeightFog is not null || programs.VolumeResolve is not null)
            {
                fogOutput = Image(width, height, "surface.fog", VulkanTextureFormat.Rgba16Float);
                if (programs.HeightFog is not null)
                    fogPass = Own(new VulkanPostProcess3D(plant, allocator, programs.HeightFog, fogOutput, 2, Filter.Nearest));
                if (programs.VolumeInject is not null && programs.VolumeIntegrate is not null && programs.VolumeResolve is not null)
                    volumeLighting = Own(new VulkanVolumeLighting3D(plant, allocator, programs, fogOutput));
            }
            if (programs.SubsurfaceDiffuse is not null && programs.SubsurfaceMerge is not null)
            {
                diffuseHorizontal = Image(width, height, "surface.diffuse-horizontal", VulkanTextureFormat.Rgba16Float);
                diffuseVertical = Image(width, height, "surface.diffuse-vertical", VulkanTextureFormat.Rgba16Float);
                subsurfaceOutput = Image(width, height, "surface.scattered", VulkanTextureFormat.Rgba16Float);
                diffusionHorizontal = Own(new VulkanPostProcess3D(plant, allocator, programs.SubsurfaceDiffuse, diffuseHorizontal, 4, Filter.Nearest));
                diffusionVertical = Own(new VulkanPostProcess3D(plant, allocator, programs.SubsurfaceDiffuse, diffuseVertical, 4, Filter.Nearest));
                diffusionMerge = Own(new VulkanPostProcess3D(plant, allocator, programs.SubsurfaceMerge, subsurfaceOutput, 4, Filter.Nearest));
            }
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

    public AurelianVulkanTexture Subsurface { get; }
    public AurelianVulkanTexture Normal { get; }
    public Vulkan3DPass CascadePass(int index) => cascadePasses[index];
    public void FinishCascade(VulkanCommandBufferLease command, int index)
    {
        Vulkan3DPass.SampleAfterRendering(plant, command, cascadeMaps[index]);
    }

    public AurelianVulkanTexture[] TransparentInputs(AurelianVulkanTexture shadow, AurelianVulkanTexture motion)
    {
        return [shadow, environment ?? fallback, motion, lightData, cascadeMaps[0], cascadeMaps[1], spotMaps[0], spotMaps[1],
            VolumeTexture];
    }

    public AurelianVulkanTexture VolumeTexture => volumeLighting?.Integrated ?? fallback;

    public float[] VolumeParameters(Graphics3DSettings settings)
    {
        return volumeLighting?.Parameters(settings) ?? [1, 1, 1, 0, .1f, 1, 1, 1];
    }

    public float[] TransparentUniforms(DirectionalShadowCascades3D cascades, Graphics3DSettings settings, uint width, uint height)
    {
        return [.. CascadeUniforms(cascades), priorLights.Length, environment is null ? 0 : 1, settings.EnvironmentIntensity, 0,
            probe?.Position.X ?? 0, probe?.Position.Y ?? 0, probe?.Position.Z ?? 0, 0,
            probe?.HalfSize.X ?? 1, probe?.HalfSize.Y ?? 1, probe?.HalfSize.Z ?? 1, probe is null ? 0 : 1,
            .. FogUniforms(settings.Fog), 1f / width, 1f / height, settings.RefractionTraceDistance, 0,
            .. Lighting3DUniforms.Rows(spotCameras[0]), .. Lighting3DUniforms.Rows(spotCameras[1]), .. VolumeParameters(settings)];
    }

    private static float[] CascadeUniforms(DirectionalShadowCascades3D cascades)
    {
        return [.. Lighting3DUniforms.Rows(cascades.Cameras[1]), .. Lighting3DUniforms.Rows(cascades.Cameras[2]),
            cascades.Ends.X, cascades.Ends.Y, cascades.Ends.Z, 0,
            cascades.Forward.X, cascades.Forward.Y, cascades.Forward.Z, 0,
            cascades.Biases.X, cascades.Biases.Y, cascades.Biases.Z, 0];
    }

    private static float[] FogUniforms(HeightFog3D fog)
    {
        return [fog.Density, fog.HeightFalloff, fog.BaseHeight, fog.StartDistance,
            fog.Color.X, fog.Color.Y, fog.Color.Z, fog.MaximumDistance];
    }
    public AurelianVulkanTexture Emission { get; }
    public AurelianVulkanTexture Output { get; }

    public void ValidatePresentation(Graphics3DSettings settings, bool subsurface)
    {
        if (settings.SurfaceDebugView != SurfaceDebugView3D.Shaded)
            return;
        if (settings.Volumetrics.Enabled && volumeLighting is null)
            throw new InvalidOperationException("Volumetric lighting requires injection, integration and resolve programs.");
        volumeLighting?.Prepare(settings);
        if (settings.Fog.Density > 0 && !settings.Volumetrics.Enabled && fogPass is null)
            throw new InvalidOperationException("Fog requires the HeightFog3D program.");
        if (subsurface && diffusionMerge is null)
            throw new InvalidOperationException("Subsurface materials require the diffuse diffusion and merge programs.");
    }

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
            // The lighting change has no matching surface motion vector.
            // Invalidate retained shading, including atmospheric in-scattering.
            changed = true;
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
        Graphics3DSettings settings, float[] lighting, bool useDiffuse, Vulkan3DGpuTimings? timings,
        DirectionalShadowCascades3D cascades, bool subsurface)
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
            1f / surface.Width, 1f / surface.Height, (float)settings.SurfaceDebugView, 0,
            probe?.Position.X ?? 0, probe?.Position.Y ?? 0, probe?.Position.Z ?? 0, 0,
            probe?.HalfSize.X ?? 1, probe?.HalfSize.Y ?? 1, probe?.HalfSize.Z ?? 1, probe is null ? 0 : 1,
            .. Lighting3DUniforms.Rows(spotCameras[0]), .. Lighting3DUniforms.Rows(spotCameras[1]), .. CascadeUniforms(cascades)],
            surface, motion, Normal, Emission, environment ?? fallback, lightData, tiles, shadow, diffuse ?? fallback, filteredAo,
            spotMaps[0], spotMaps[1], cascadeMaps[0], cascadeMaps[1], Subsurface);
        resolvePass.SetLinearInput(4, environment ?? fallback);
        resolvePass.Record(command, triangle);
        timings?.Mark(command, 15);
        AurelianVulkanTexture resolved = Output;
        timings?.Mark(command, 18);
        if (settings.SurfaceDebugView == SurfaceDebugView3D.Shaded && subsurface)
        {
            if (diffusionMerge is null)
                throw new InvalidOperationException("Subsurface materials require the diffuse diffusion and merge programs.");

            diffusionHorizontal!.Configure([.. inverseRows, 1f / surface.Width, 0, 0, 0], diffuseOutput, motion, Normal, Subsurface);
            diffusionHorizontal.Record(command, triangle);
            diffusionVertical!.Configure([.. inverseRows, 0, 1f / surface.Height, 0, 0], diffuseHorizontal!, motion, Normal, Subsurface);
            diffusionVertical.Record(command, triangle);
            diffusionMerge.Configure([0, 0, 0, 0], Output, diffuseOutput, diffuseVertical!, Subsurface);
            diffusionMerge.Record(command, triangle);
            resolved = subsurfaceOutput!;
        }
        timings?.Mark(command, 19);
        UnfoggedOutput = resolved;
        timings?.Mark(command, 20);
        if (settings.SurfaceDebugView == SurfaceDebugView3D.Shaded && settings.Fog.Density > 0)
        {
            if (settings.Volumetrics.Enabled)
            {
                volumeLighting!.Record(command, triangle, resolved, motion, inverse, eye, settings, lighting,
                    TransparentUniforms(cascades, settings, surface.Width, surface.Height), TransparentInputs(shadow, motion));
            }
            else
            {
                fogPass!.Configure([.. inverseRows, eye.X, eye.Y, eye.Z, 1, .. FogUniforms(settings.Fog)], resolved, motion);
                fogPass.Record(command, triangle);
            }
            resolved = fogOutput!;
        }
        timings?.Mark(command, 21);
        return resolved;
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
