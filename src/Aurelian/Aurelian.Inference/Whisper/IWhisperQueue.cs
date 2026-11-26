using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.Inference.Whisper;

internal interface IWhisperQueue
{
    VulkanComputeMath.QueuedComputeScope? Retained { get; }

    VulkanComputeMath.QueuedComputeScope? OpenRequestScope();
}
