namespace Aurelian.Graphics.Vulkan.Native2D;

public readonly record struct Native2DTextureHandle(ulong Value);

public readonly record struct Native2DRect(float X, float Y, float Width, float Height);

public readonly record struct Native2DUvRect(float U0, float V0, float U1, float V1)
{
    public static Native2DUvRect Full { get; } = new(0, 0, 1, 1);
}

public readonly record struct Native2DTint(float Red, float Green, float Blue, float Alpha)
{
    public static Native2DTint White { get; } = new(1, 1, 1, 1);
}

public readonly record struct NativeQuadSubmission(
    Native2DRect Destination,
    Native2DUvRect Uv,
    Native2DTextureHandle Texture,
    Native2DTint Tint);

public sealed record Native2DPassMetrics(
    int QuadCount,
    int DrawCalls,
    int CommandBuffers,
    int QueueSubmissions,
    int BufferUploads,
    int DescriptorSetAllocations,
    int DescriptorWrites,
    int VertexCapacityQuads,
    double VertexUploadMilliseconds,
    double CommandRecordingMilliseconds,
    double SubmitWaitMilliseconds,
    double ReadbackMilliseconds,
    long CpuAllocatedBytes);

public sealed record Native2DPassResult(
    Native2DPassMetrics Metrics,
    byte[]? Pixels,
    string? PixelSha256);
