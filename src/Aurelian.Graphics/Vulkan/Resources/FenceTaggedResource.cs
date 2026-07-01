namespace Aurelian.Graphics.Vulkan.Resources;

public sealed record FenceTaggedResource<T>(T Resource, ulong RetireFenceValue);
