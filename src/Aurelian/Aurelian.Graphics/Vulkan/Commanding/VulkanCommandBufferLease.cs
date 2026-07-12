using System.Threading;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Commanding;

public sealed class VulkanCommandBufferLease
{
    private static long nextLeaseId;
    private static long nextRenderPassScopeId;

    private readonly object gate = new();
    private readonly Vk vk;
    private VulkanCommandBufferLifecycle lifecycle = VulkanCommandBufferLifecycle.Ready;
    private VulkanRenderPassScope? activeRenderPassScope;

    internal VulkanCommandBufferLease(
        PlantId plantId,
        Vk vk,
        CommandBuffer commandBuffer)
    {
        PlantId = plantId;
        LeaseId = unchecked((ulong)Interlocked.Increment(ref nextLeaseId));
        this.vk = vk;
        CommandBuffer = commandBuffer;
    }

    public PlantId PlantId { get; }

    /// <summary>Process-local command buffer lease identity used for scope validation diagnostics, not global device state.</summary>
    public ulong LeaseId { get; }

    public CommandBuffer CommandBuffer { get; }

    public bool IsReady
    {
        get
        {
            lock (gate)
            {
                return lifecycle == VulkanCommandBufferLifecycle.Ready;
            }
        }
    }

    public bool IsRecording
    {
        get
        {
            lock (gate)
            {
                return lifecycle == VulkanCommandBufferLifecycle.Recording;
            }
        }
    }

    public bool IsExecutable
    {
        get
        {
            lock (gate)
            {
                return lifecycle == VulkanCommandBufferLifecycle.Executable;
            }
        }
    }


    public bool HasActiveRenderPass
    {
        get
        {
            lock (gate)
            {
                return activeRenderPassScope is not null;
            }
        }
    }

    internal VulkanRenderPassScope? ActiveRenderPassScope
    {
        get
        {
            lock (gate)
            {
                return activeRenderPassScope;
            }
        }
    }

    public bool IsRetired
    {
        get
        {
            lock (gate)
            {
                return lifecycle == VulkanCommandBufferLifecycle.Retired;
            }
        }
    }

    public bool IsDisposed
    {
        get
        {
            lock (gate)
            {
                return lifecycle == VulkanCommandBufferLifecycle.Disposed;
            }
        }
    }

    public VulkanCommandBufferOperationResult Reset()
    {
        lock (gate)
        {
            if (lifecycle == VulkanCommandBufferLifecycle.Disposed)
            {
                return Failed(
                    VulkanCommandBufferDiagnosticCodes.CommandBufferDisposed,
                    "Cannot reset a disposed Vulkan command buffer lease.");
            }
        }

        Result result = vk.ResetCommandBuffer(CommandBuffer, CommandBufferResetFlags.None);
        if (result != Result.Success)
        {
            return Failed(
                VulkanCommandBufferDiagnosticCodes.CommandBufferResetFailed,
                $"Vulkan command buffer reset failed with result {result}.");
        }

        lock (gate)
        {
            if (lifecycle != VulkanCommandBufferLifecycle.Disposed)
            {
                lifecycle = VulkanCommandBufferLifecycle.Ready;
                activeRenderPassScope = null;
            }
        }

        return VulkanCommandBufferOperationResult.Succeeded();
    }

    public unsafe VulkanCommandBufferOperationResult Begin()
    {
        lock (gate)
        {
            if (lifecycle == VulkanCommandBufferLifecycle.Disposed)
            {
                return Failed(
                    VulkanCommandBufferDiagnosticCodes.CommandBufferDisposed,
                    "Cannot begin recording a disposed Vulkan command buffer lease.");
            }

            if (lifecycle != VulkanCommandBufferLifecycle.Ready)
            {
                return Failed(
                    VulkanCommandBufferDiagnosticCodes.InvalidCommandBufferState,
                    $"Cannot begin a Vulkan command buffer while it is {lifecycle}; expected Ready.");
            }
        }

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };

        Result result = vk.BeginCommandBuffer(CommandBuffer, &beginInfo);
        if (result != Result.Success)
        {
            return Failed(
                VulkanCommandBufferDiagnosticCodes.CommandBufferBeginFailed,
                $"Vulkan command buffer begin failed with result {result}.");
        }

        lock (gate)
        {
            if (lifecycle != VulkanCommandBufferLifecycle.Disposed)
            {
                lifecycle = VulkanCommandBufferLifecycle.Recording;
                activeRenderPassScope = null;
            }
        }

        return VulkanCommandBufferOperationResult.Succeeded();
    }

    public VulkanCommandBufferOperationResult End()
    {
        lock (gate)
        {
            if (lifecycle == VulkanCommandBufferLifecycle.Disposed)
            {
                return Failed(
                    VulkanCommandBufferDiagnosticCodes.CommandBufferDisposed,
                    "Cannot end recording a disposed Vulkan command buffer lease.");
            }

            if (lifecycle != VulkanCommandBufferLifecycle.Recording)
            {
                return Failed(
                    VulkanCommandBufferDiagnosticCodes.InvalidCommandBufferState,
                    $"Cannot end a Vulkan command buffer while it is {lifecycle}; expected Recording.");
            }
        }

        Result result = vk.EndCommandBuffer(CommandBuffer);
        if (result != Result.Success)
        {
            return Failed(
                VulkanCommandBufferDiagnosticCodes.CommandBufferEndFailed,
                $"Vulkan command buffer end failed with result {result}.");
        }

        lock (gate)
        {
            if (lifecycle != VulkanCommandBufferLifecycle.Disposed)
            {
                lifecycle = VulkanCommandBufferLifecycle.Executable;
                activeRenderPassScope = null;
            }
        }

        return VulkanCommandBufferOperationResult.Succeeded();
    }

    internal void MarkRetired()
    {
        lock (gate)
        {
            if (lifecycle != VulkanCommandBufferLifecycle.Disposed)
            {
                lifecycle = VulkanCommandBufferLifecycle.Retired;
                activeRenderPassScope = null;
            }
        }
    }

    internal void MarkDisposed()
    {
        lock (gate)
        {
            lifecycle = VulkanCommandBufferLifecycle.Disposed;
            activeRenderPassScope = null;
        }
    }


    internal VulkanRenderPassScope MarkRenderPassActive(PlantId plantId)
    {
        lock (gate)
        {
            if (lifecycle != VulkanCommandBufferLifecycle.Recording)
            {
                throw new InvalidOperationException("Render pass scope can only be marked active while the command buffer is recording.");
            }

            if (activeRenderPassScope is not null)
            {
                throw new InvalidOperationException("A render pass is already active on this command buffer lease.");
            }

            VulkanRenderPassScope scope = new(
                plantId,
                LeaseId,
                unchecked((ulong)Interlocked.Increment(ref nextRenderPassScopeId)));
            activeRenderPassScope = scope;
            return scope;
        }
    }

    internal bool TryClearRenderPass(VulkanRenderPassScope scope)
    {
        lock (gate)
        {
            if (activeRenderPassScope != scope)
            {
                return false;
            }

            activeRenderPassScope = null;
            return true;
        }
    }

    internal bool IsActiveScope(VulkanRenderPassScope scope)
    {
        lock (gate)
        {
            return lifecycle == VulkanCommandBufferLifecycle.Recording
                && activeRenderPassScope == scope;
        }
    }

    private VulkanCommandBufferOperationResult Failed(string code, string message)
        => VulkanCommandBufferOperationResult.Failed(new VulkanCommandBufferDiagnostic(
            code,
            VulkanCommandBufferDiagnosticSeverity.Error,
            message,
            PlantId));
}
