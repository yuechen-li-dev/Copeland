using System.Numerics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>GPU-resident froxel injection and front-to-back single-scattering integration.</summary>
internal sealed class VulkanVolumeLighting3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly SurfaceLightingPrograms programs;
    private readonly uint width;
    private readonly uint height;
    private readonly VulkanPostProcess3D resolve;
    private readonly Stack<IDisposable> gridResources = new();
    private AurelianVulkanTexture? source;
    private AurelianVulkanTexture? integrated;
    private VulkanPostProcess3D? inject;
    private VulkanPostProcess3D? integrate;
    private int pixelSize;
    private int slices;

    public VulkanVolumeLighting3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        SurfaceLightingPrograms programs, AurelianVulkanTexture output)
    {
        this.plant = plant;
        this.allocator = allocator;
        this.programs = programs;
        width = output.Width;
        height = output.Height;
        resolve = new(plant, allocator, programs.VolumeResolve!, output, 3, Filter.Nearest);
    }

    public AurelianVulkanTexture? Integrated => integrated;

    public float[] Parameters(Graphics3DSettings settings)
    {
        VolumetricLighting3D volume = settings.Volumetrics;
        return [integrated?.Width ?? 1, integrated is null ? 1 : integrated.Height / (uint)slices,
            volume.DepthSlices, volume.Enabled && settings.Fog.Density > 0 ? 1 : 0,
            .1f, volume.MaximumDistance, volume.DepthSlices, settings.Fog.MaximumDistance];
    }

    public void Prepare(Graphics3DSettings settings)
    {
        VolumetricLighting3D volume = settings.Volumetrics;
        if (!volume.Enabled || settings.Fog.Density == 0)
            return;
        if (pixelSize == volume.PixelSize && slices == volume.DepthSlices)
            return;
        ReleaseGrid();
        try
        {
            uint x = (width + (uint)volume.PixelSize - 1) / (uint)volume.PixelSize;
            uint y = (height + (uint)volume.PixelSize - 1) / (uint)volume.PixelSize;
            source = Image(x, y * (uint)volume.DepthSlices, "volume.source");
            integrated = Image(x, y * (uint)volume.DepthSlices, "volume.integrated");
            inject = Own(new VulkanPostProcess3D(plant, allocator, programs.VolumeInject!, source, 6, Filter.Nearest));
            integrate = Own(new VulkanPostProcess3D(plant, allocator, programs.VolumeIntegrate!, integrated, 1, Filter.Nearest));
            pixelSize = volume.PixelSize;
            slices = volume.DepthSlices;
        }
        catch
        {
            ReleaseGrid();
            throw;
        }
    }

    public void Record(VulkanCommandBufferLease command, AurelianVulkanBuffer triangle,
        AurelianVulkanTexture scene, AurelianVulkanTexture motion, Matrix4x4 inverse, Vector3 eye,
        Graphics3DSettings settings, float[] lighting, float[] frame, AurelianVulkanTexture[] lightingInputs)
    {
        VolumetricLighting3D volume = settings.Volumetrics;
        FogRegion3D? region = volume.Region;
        float[] parameters = Parameters(settings);
        float[] inverseRows = Lighting3DUniforms.Rows(inverse);
        inject!.Configure([.. inverseRows, eye.X, eye.Y, eye.Z, 1, .. parameters,
            settings.Fog.Density, settings.Fog.HeightFalloff, settings.Fog.BaseHeight, settings.Fog.StartDistance,
            volume.ScatteringAlbedo.X, volume.ScatteringAlbedo.Y, volume.ScatteringAlbedo.Z, volume.Anisotropy,
            volume.AmbientRadiance.X, volume.AmbientRadiance.Y, volume.AmbientRadiance.Z, 0,
            region?.Minimum.X ?? 0, region?.Minimum.Y ?? 0, region?.Minimum.Z ?? 0, region is null ? 0 : 1,
            region?.Maximum.X ?? 0, region?.Maximum.Y ?? 0, region?.Maximum.Z ?? 0, 0,
            .. lighting[..8], .. lighting[16..36], .. frame[..44], .. frame[68..100], frame[44], 0, 0, 0],
            lightingInputs[0], lightingInputs[4], lightingInputs[5], lightingInputs[6], lightingInputs[7], lightingInputs[3]);
        inject.Record(command, triangle);
        integrate!.Configure(parameters, source!);
        integrate.Record(command, triangle);
        resolve.Configure([.. inverseRows, eye.X, eye.Y, eye.Z, 1, .. parameters], scene, motion, integrated!);
        resolve.SetLinearInput(2, integrated!);
        resolve.Record(command, triangle);
    }

    private AurelianVulkanTexture Image(uint x, uint y, string name)
    {
        return Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, x, y,
            VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource,
            VulkanMemoryUsage.GpuOnly, name, VulkanTextureFormat.Rgba16Float));
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        gridResources.Push(resource);
        return resource;
    }

    private void ReleaseGrid()
    {
        while (gridResources.TryPop(out IDisposable? resource))
            resource.Dispose();
        pixelSize = 0;
        slices = 0;
        source = null;
        integrated = null;
        inject = null;
        integrate = null;
    }

    public void Dispose()
    {
        resolve.Dispose();
        ReleaseGrid();
    }
}
