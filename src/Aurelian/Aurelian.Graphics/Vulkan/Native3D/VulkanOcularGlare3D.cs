using System.Numerics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Photopic forward scatter on an angularly weighted, zero-extended pyramid.</summary>
internal sealed class VulkanOcularGlare3D : IDisposable
{
    private const int MaximumLevels = 9;
    private readonly AurelianVulkanPlant plant;
    private readonly Stack<IDisposable> owned = new();
    private readonly List<Level> levels = [];

    public VulkanOcularGlare3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        CompiledGraphicsProgram program, uint width, uint height, VulkanFrameInFlightSet frames)
    {
        this.plant = plant;
        try
        {
            for (int index = 0; index < MaximumLevels; index++)
            {
                AurelianVulkanTexture? down = null;
                VulkanPostProcess3D? reduce = null;
                if (index > 0)
                {
                    width = (width + 1) / 2;
                    height = (height + 1) / 2;
                    down = Texture("ocular-glare.reduce");
                    reduce = Pass(down);
                }
                var up = Texture("ocular-glare.reconstruct");
                levels.Add(new(down, reduce, up, Pass(up)));
                if (width == 1 && height == 1)
                {
                    break;
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }

        AurelianVulkanTexture Texture(string label) => Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
            VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferSource,
            VulkanMemoryUsage.GpuOnly, label, VulkanTextureFormat.Rgba16Float));

        VulkanPostProcess3D Pass(AurelianVulkanTexture color) => Own(new VulkanPostProcess3D(plant, allocator, program, color, 2,
            frames: frames, address: SamplerAddressMode.ClampToBorder));
    }

    public AurelianVulkanTexture Record(VulkanCommandBufferLease command, AurelianVulkanBuffer triangle,
        AurelianVulkanTexture source, Matrix4x4 camera, OcularGlare3D settings)
    {
        if (source.Width != levels[0].Up.Width || source.Height != levels[0].Up.Height)
        {
            throw new InvalidOperationException("Ocular glare requires the reconstructed display extent.");
        }
        Vulkan3DPass.SampleAfterRendering(plant, command, source);
        var current = source;
        // A zero-extended 2x2 average conserves mass after accounting for the
        // coarser texel area. The matched bilinear expansion can only lose mass at a clipped edge.
        for (int index = 1; index < levels.Count; index++)
        {
            var level = levels[index];
            var down = level.Down!;
            level.Reduce!.Configure(
                [2f * down.Width / current.Width, 2f * down.Height / current.Height, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0],
                current, current);
            level.Reduce.Record(command, triangle);
            current = down;
        }

        Span<float> weights = stackalloc float[MaximumLevels];
        double focalLength = SpencerPhotopicGlare.FocalLength(source.Height, camera, settings.OrthographicFieldOfViewDegrees);
        float direct = SpencerPhotopicGlare.Weights(weights[..levels.Count], focalLength, settings.Strength);
        AurelianVulkanTexture? broad = null;
        for (int index = levels.Count - 1; index >= 0; index--)
        {
            var level = levels[index];
            current = level.Down ?? source;
            var lower = broad ?? current;
            level.Reconstruct.Configure(
                [1, 1, 1f / current.Width, 1f / current.Height,
                 .5f * current.Width / lower.Width, .5f * current.Height / lower.Height, broad is null ? 0 : 1, 0,
                 index == 0 ? direct : 0, weights[index], 0, 0],
                current, lower);
            level.Reconstruct.Record(command, triangle);
            broad = level.Up;
        }
        return levels[0].Up;
    }

    private T Own<T>(T value) where T : IDisposable
    {
        owned.Push(value);
        return value;
    }

    public void Dispose()
    {
        while (owned.TryPop(out var item)) item.Dispose();
    }

    private sealed record Level(AurelianVulkanTexture? Down, VulkanPostProcess3D? Reduce, AurelianVulkanTexture Up,
        VulkanPostProcess3D Reconstruct);
}

internal static class SpencerPhotopicGlare
{
    private const int CdfIntervals = 1024;
    private const double RadiusOffset = .02;
    private const double HemisphereDegrees = 90;
    private const double CoreMass = .384;
    private static readonly double CdfExtent = Math.Log(1 + HemisphereDegrees / RadiusOffset);
    private static readonly double[] Cdf = BuildCdf();

    public static double FocalLength(uint height, Matrix4x4 camera, float fallbackDegrees)
    {
        double w = new Vector3(camera.M14, camera.M24, camera.M34).Length();
        double y = new Vector3(camera.M12, camera.M22, camera.M32).Length();
        if (double.IsFinite(w) && double.IsFinite(y) && w > 0 && y > 0)
        {
            return .5 * height * y / w;
        }
        return .5 * height / Math.Tan(fallbackDegrees * Math.PI / 360);
    }

    public static float Weights(Span<float> weights, double focalLength, float strength)
    {
        double previous = Encircled(.5, focalLength);
        float direct = (float)(1 - strength + strength * previous);
        for (int level = 0; level < weights.Length; level++)
        {
            // A tent at this lattice spacing has a radial median close to one texel.
            // Partition the PSF at geometric midpoints between consecutive scales.
            double upper = Encircled(Math.Sqrt(2) * (1 << level), focalLength);
            weights[level] = (float)((upper - previous) * strength);
            previous = upper;
        }
        return direct;
    }

    private static double Encircled(double pixels, double focalLength)
    {
        double angle = Math.Atan(pixels / focalLength) * 180 / Math.PI;
        double offset = CdfIntervals * Math.Log(1 + angle / RadiusOffset) / CdfExtent;
        int low = Math.Min((int)offset, CdfIntervals - 1);
        return Cdf[low] + (offset - low) * (Cdf[low + 1] - Cdf[low]);
    }

    private static double[] BuildCdf()
    {
        // Spencer et al., SIGGRAPH 1995, Eq. 5. Normalize f1 and f2 over solid angle
        // separately. The narrow f0 Gaussian occupies the direct pixel.
        var near = new double[CdfIntervals + 1];
        var far = new double[CdfIntervals + 1];
        double previousAngle = 0;
        double previousNear = 0;
        double previousFar = 0;
        for (int index = 1; index <= CdfIntervals; index++)
        {
            double angle = RadiusOffset * (Math.Exp(index * CdfExtent / CdfIntervals) - 1);
            double denominator = angle + RadiusOffset;
            double farDensity = Math.Sin(angle * Math.PI / 180) / (denominator * denominator);
            double nearDensity = farDensity / denominator;
            double width = .5 * (angle - previousAngle);
            near[index] = near[index - 1] + width * (previousNear + nearDensity);
            far[index] = far[index - 1] + width * (previousFar + farDensity);
            previousAngle = angle;
            previousNear = nearDensity;
            previousFar = farDensity;
        }
        var result = new double[near.Length];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = CoreMass + .478 * near[index] / near[^1] + .138 * far[index] / far[^1];
        }
        return result;
    }
}
