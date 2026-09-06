namespace Aurelian.Graphics.Vulkan.Native2D;

public readonly record struct Native2DTextureHandle(ulong Value);

public readonly record struct Native2DRect(float X, float Y, float Width, float Height);

public readonly record struct Native2DSize(float Width, float Height);

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

public readonly record struct NativeMsdfParameters(
    float PixelRange,
    float FieldScale,
    float Threshold)
{
    public static NativeMsdfParameters Create(float pixelRange, float fieldScale)
        => new(pixelRange, fieldScale, 0.5f);
}

public readonly record struct NativeMsdfQuadSubmission(
    Native2DRect Destination,
    Native2DUvRect Uv,
    Native2DTextureHandle AtlasTexture,
    Native2DTint Color,
    NativeMsdfParameters Msdf);

public enum NativeAnalyticShapeKind : uint
{
    RoundedRect = 0,
    Circle = 1,
    Pill = 2,
}

public readonly record struct NativeAnalyticShapeSubmission(
    Native2DRect Destination,
    Native2DSize ShapeSize,
    Native2DUvRect LocalCoordinates,
    NativeAnalyticShapeKind Kind,
    Native2DTint FillColor,
    float Radius,
    Native2DTint BorderColor,
    float BorderWidth);

public readonly record struct NativeSoftShockwaveSubmission(
    Native2DRect Destination,
    Native2DUvRect LocalCoordinates,
    Native2DTint Color,
    float Age,
    float Lifetime,
    float Radius,
    float Thickness,
    float Intensity,
    float Seed);

public enum Native2DPipelineKind
{
    Textured,
    MsdfText,
    AnalyticShape2D,
    SoftShockwave,
}

public sealed record Native2DPipelineOptions(
    Native2DPipelineKind Kind,
    bool TransparentClear = false,
    bool EnableStraightAlphaBlend = false,
    bool EnableLinearFiltering = false,
    bool InputsAreSrgb = true)
{
    public static Native2DPipelineOptions Textured { get; } = new(Native2DPipelineKind.Textured);

    public static Native2DPipelineOptions SpriteNearest { get; } = new(
        Native2DPipelineKind.Textured,
        EnableStraightAlphaBlend: true);

    public static Native2DPipelineOptions SpriteLinear { get; } = new(
        Native2DPipelineKind.Textured,
        EnableStraightAlphaBlend: true,
        EnableLinearFiltering: true);

    public static Native2DPipelineOptions MsdfText { get; } = new(Native2DPipelineKind.MsdfText);

    public static Native2DPipelineOptions AnalyticShape2D { get; } = new(Native2DPipelineKind.AnalyticShape2D);

    public static Native2DPipelineOptions SoftShockwave { get; } = new(Native2DPipelineKind.SoftShockwave);

    public bool LinearFiltering => Kind == Native2DPipelineKind.MsdfText || EnableLinearFiltering;

    public bool StraightAlphaBlend => EnableStraightAlphaBlend
        || Kind is Native2DPipelineKind.MsdfText or Native2DPipelineKind.AnalyticShape2D or Native2DPipelineKind.SoftShockwave;
}

public static class NativeSrgbTransfer
{
    public static float Decode(float encoded)
    {
        if (!float.IsFinite(encoded) || encoded < 0 || encoded > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(encoded), "An sRGB channel must be finite and in [0,1].");
        }

        return encoded <= 0.04045f
            ? encoded / 12.92f
            : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);
    }

    public static Native2DTint Decode(Native2DTint encoded)
    {
        return new Native2DTint(
            Decode(encoded.Red),
            Decode(encoded.Green),
            Decode(encoded.Blue),
            encoded.Alpha);
    }
}

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
