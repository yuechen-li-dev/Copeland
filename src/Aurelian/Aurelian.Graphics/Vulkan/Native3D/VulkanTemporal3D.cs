using System.Numerics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>GPU-only color/depth history. Every resolve reads the other image, never its own output.</summary>
internal sealed class VulkanTemporal3D : IDisposable
{
    private readonly AurelianVulkanTexture[] history = new AurelianVulkanTexture[2];
    private readonly VulkanPostProcess3D[] passes = new VulkanPostProcess3D[2];
    private readonly uint width;
    private readonly uint height;
    private int writeIndex;
    private uint frame;
    private bool valid;
    private Vector2 jitter;
    private Vector2 previousJitter;

    public VulkanTemporal3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool commands, VulkanFenceBundle fences, CompiledGraphicsProgram program, uint width, uint height)
    {
        this.width = width;
        this.height = height;
        try
        {
            using var uploader = new VulkanTextureUploader(plant, allocator, commands, fences);
            for (int index = 0; index < 2; index++)
            {
                history[index] = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
                    VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination,
                    VulkanMemoryUsage.GpuOnly, "temporal.history", VulkanTextureFormat.Rgba32Float);
                // Even a rejected first-frame sample must reference a defined image layout.
                var upload = uploader.Upload(new(history[index], new byte[checked((int)(width * height * 16))], "temporal.initialize"));
                if (!upload.Success) throw new InvalidOperationException(string.Join("; ", upload.Diagnostics.Select(item => item.Message)));
                passes[index] = new(plant, allocator, program, history[index], 3, Filter.Nearest);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Reset()
    {
        valid = false;
        frame = 0;
    }

    public Matrix4x4 Begin(Matrix4x4 logicalCamera)
    {
        previousJitter = jitter;
        uint sample = frame % 8 + 1;
        jitter = new(Halton(sample, 2) - .5f, Halton(sample, 3) - .5f);
        return Jitter(logicalCamera, jitter, width, height);
    }

    public AurelianVulkanTexture Resolve(VulkanCommandBufferLease command, AurelianVulkanBuffer triangle,
        AurelianVulkanTexture current, AurelianVulkanTexture motion, float historyWeight)
    {
        var pass = passes[writeIndex];
        Vector2 delta = previousJitter - jitter;
        pass.Configure([1f / width, 1f / height, valid ? 1 : 0, Math.Min(frame / (frame + 1f), historyWeight),
            delta.X / width, delta.Y / height, 0, 0, jitter.X / width, jitter.Y / height, 0, 0], current, motion, history[1 - writeIndex]);
        pass.Record(command, triangle);
        var result = history[writeIndex];
        writeIndex = 1 - writeIndex;
        valid = true;
        frame++;
        return result;
    }

    internal static Matrix4x4 Jitter(Matrix4x4 camera, Vector2 pixels, uint width, uint height)
    {
        // Row-vector System.Numerics: add jitter * clip.w to clip.x and clip.y.
        float x = 2 * pixels.X / width;
        float y = 2 * pixels.Y / height;
        camera.M11 += x * camera.M14;
        camera.M21 += x * camera.M24;
        camera.M31 += x * camera.M34;
        camera.M41 += x * camera.M44;
        camera.M12 += y * camera.M14;
        camera.M22 += y * camera.M24;
        camera.M32 += y * camera.M34;
        camera.M42 += y * camera.M44;
        return camera;
    }

    private static float Halton(uint index, uint radix)
    {
        float value = 0;
        float fraction = 1;
        for (uint remaining = index; remaining > 0; remaining /= radix)
        {
            fraction /= radix;
            value += fraction * (remaining % radix);
        }
        return value;
    }

    public void Dispose()
    {
        foreach (var pass in passes) pass?.Dispose();
        foreach (var texture in history) texture?.Dispose();
    }
}
