using System.Security.Cryptography;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native2D;

public readonly record struct NativeFrameClearColor(float Red, float Green, float Blue, float Alpha)
{
    public static NativeFrameClearColor Transparent { get; } = new(0, 0, 0, 0);
}

public sealed record VulkanNativeFrameResult(
    IReadOnlyList<Native2DPassResult> Passes,
    byte[]? Pixels,
    string? PixelSha256,
    double ReadbackMilliseconds)
{
    public int RenderPassCount => Passes.Count;
    public int DrawCalls => Passes.Sum(static pass => pass.Metrics.DrawCalls);
    public int QuadCount => Passes.Sum(static pass => pass.Metrics.QuadCount);
}

public sealed unsafe class VulkanNativeFrameTarget : IDisposable
{
    private const ulong FenceWaitTimeoutNanoseconds = 5_000_000_000;
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commandPool;
    private readonly VulkanCommandSubmitter submitter;
    private bool frameActive;
    private bool disposed;

    public VulkanNativeFrameTarget(
        AurelianVulkanPlant plant,
        uint width,
        uint height,
        VulkanTextureFormat format = VulkanTextureFormat.Rgba8Unorm)
    {
        ArgumentNullException.ThrowIfNull(plant);
        if (width == 0 || height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The native frame target extent must be positive.");
        }

        this.plant = plant;
        Width = width;
        Height = height;
        allocator = new RawVulkanMemoryAllocator(plant);
        fences = VulkanFenceBundle.Create(plant);
        commandPool = VulkanCommandBufferPool.Create(plant);
        submitter = new VulkanCommandSubmitter(plant, commandPool, fences);
        VulkanTextureCreateResult textureResult = VulkanTextureFactory.Create(
            plant,
            allocator,
            new VulkanTextureCreatePlan(
                plant.Context.Id,
                width,
                height,
                format,
                VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.TransferSource,
                VulkanMemoryUsage.GpuOnly,
                VulkanResourceLayout.Undefined,
                DebugName: "native-frame.target"));
        Require(textureResult.Success, "Native frame target texture creation failed.");
        Texture = textureResult.Texture!;
    }

    public uint Width { get; }

    public uint Height { get; }

    public string Format => Texture.Format switch
    {
        VulkanTextureFormat.Rgba8Unorm => "R8G8B8A8Unorm",
        VulkanTextureFormat.Bgra8Unorm => "B8G8R8A8Unorm",
        VulkanTextureFormat.Rgba8Srgb => "R8G8B8A8Srgb",
        VulkanTextureFormat.Bgra8Srgb => "B8G8R8A8Srgb",
        _ => throw new InvalidOperationException($"Unsupported native frame format {Texture.Format}."),
    };

    public VulkanTextureFormat TextureFormat => Texture.Format;

    public int SampleCount => 1;

    public bool IsDisposed => disposed;

    internal AurelianVulkanTexture Texture { get; }

    public VulkanNativeFrameSession BeginFrame(NativeFrameClearColor clearColor)
    {
        ThrowIfDisposed();
        ValidateClearColor(clearColor);
        if (frameActive)
        {
            throw new InvalidOperationException("A native frame is already active for this target.");
        }
        frameActive = true;
        return new VulkanNativeFrameSession(this, clearColor);
    }

    internal void ValidateCompatibility(AurelianVulkanPlant candidatePlant, uint width, uint height)
    {
        ThrowIfDisposed();
        if (!ReferenceEquals(plant, candidatePlant))
        {
            throw new ArgumentException("Direct composition requires the renderer and target to use the same Vulkan plant.");
        }
        if (width != Width || height != Height)
        {
            throw new ArgumentException(
                $"Direct composition extent {width}x{height} is incompatible with target extent {Width}x{Height}.");
        }
    }

    internal (byte[] Pixels, string Hash, double Milliseconds) Capture()
    {
        ThrowIfDisposed();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using AurelianVulkanBuffer readback = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(
            plant,
            allocator,
            checked((ulong)Width * Height * 4),
            VulkanBufferUsage.TransferDestination,
            VulkanMemoryUsage.GpuToCpu,
            "native-frame.readback");
        VulkanCommandBufferLease commandBuffer = commandPool.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(commandBuffer.Begin().Success, "Native frame readback command buffer begin failed.");

        BufferImageCopy region = new()
        {
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new Extent3D(Width, Height, 1),
        };
        plant.Vk.CmdCopyImageToBuffer(
            commandBuffer.CommandBuffer,
            Texture.NativeImage,
            ImageLayout.TransferSrcOptimal,
            readback.NativeBuffer,
            1,
            &region);
        BufferMemoryBarrier barrier = new()
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.HostReadBit,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = readback.NativeBuffer,
            Size = readback.SizeBytes,
        };
        plant.Vk.CmdPipelineBarrier(
            commandBuffer.CommandBuffer,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.HostBit,
            0,
            0,
            null,
            1,
            &barrier,
            0,
            null);
        Require(commandBuffer.End().Success, "Native frame readback command buffer end failed.");
        VulkanCommandSubmitResult submit = submitter.Submit(new VulkanCommandSubmitRequest(
            commandBuffer,
            WaitForCompletion: true,
            TimeoutNanoseconds: FenceWaitTimeoutNanoseconds,
            DebugName: "native-frame.readback"));
        Require(submit.Success, "Native frame readback submission failed.");

        byte[] pixels = readback.ReadBytes(checked((int)(Width * Height * 4)));
        if (Texture.Format is VulkanTextureFormat.Bgra8Unorm or VulkanTextureFormat.Bgra8Srgb)
        {
            for (int index = 0; index < pixels.Length; index += 4)
            {
                (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
            }
        }
        string hash = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
        stopwatch.Stop();
        return (pixels, hash, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3));
    }

    internal void CompleteFrame()
    {
        frameActive = false;
    }

    internal void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        if (frameActive)
        {
            throw new InvalidOperationException("A native frame target cannot be disposed while a frame is active.");
        }
        disposed = true;
        _ = plant.Vk.DeviceWaitIdle(plant.Device);
        Texture.Dispose();
        submitter.Dispose();
        commandPool.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }

    private static void ValidateClearColor(NativeFrameClearColor color)
    {
        float[] channels = [color.Red, color.Green, color.Blue, color.Alpha];
        if (channels.Any(static value => !float.IsFinite(value) || value < 0 || value > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(color), "Clear color channels must be finite and in [0,1].");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed class VulkanNativeFrameSession : IDisposable
{
    private readonly VulkanNativeFrameTarget target;
    private readonly VulkanColorClearValue clearColor;
    private readonly List<Native2DPassResult> passes = [];
    private bool completed;

    internal VulkanNativeFrameSession(VulkanNativeFrameTarget target, NativeFrameClearColor clearColor)
    {
        this.target = target;
        this.clearColor = new VulkanColorClearValue(clearColor.Red, clearColor.Green, clearColor.Blue, clearColor.Alpha);
    }

    public uint Width => target.Width;

    public uint Height => target.Height;

    public int PresentedPassCount => passes.Count;

    public Native2DPassResult Present(
        VulkanOrderedQuadRenderer renderer,
        Action<VulkanOrderedQuadRenderer> submit)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(submit);
        EnsureActive();
        if (!renderer.Targets(target))
        {
            throw new InvalidOperationException("A native layer renderer cannot replace the compositor frame target.");
        }

        renderer.Begin2D();
        try
        {
            submit(renderer);
            Native2DPassResult result = renderer.EndShared2D(clear: passes.Count == 0, clearColor);
            passes.Add(result);
            return result;
        }
        catch
        {
            renderer.Cancel2D();
            throw;
        }
    }

    public VulkanNativeFrameResult EndFrame(bool captureReadback = true)
    {
        EnsureActive();
        if (passes.Count == 0)
        {
            throw new InvalidOperationException("A native frame requires at least one presented pass so clear has one authority.");
        }
        byte[]? pixels = null;
        string? hash = null;
        double readbackMilliseconds = 0;
        if (captureReadback)
        {
            (pixels, hash, readbackMilliseconds) = target.Capture();
        }
        completed = true;
        target.CompleteFrame();
        return new VulkanNativeFrameResult(passes.ToArray(), pixels, hash, readbackMilliseconds);
    }

    public void Dispose()
    {
        if (!completed)
        {
            completed = true;
            target.CompleteFrame();
        }
    }

    private void EnsureActive()
    {
        target.ThrowIfDisposed();
        if (completed)
        {
            throw new InvalidOperationException("The native frame is already complete.");
        }
    }
}
