using Machina.Layout.Geometry;

namespace Machina.Presentation;

/// <summary>
/// Aspect-preserving mapping between authored logical coordinates and a physical framebuffer.
/// </summary>
public readonly record struct MachinaViewportTransform
{
    private MachinaViewportTransform(
        int referenceWidth,
        int referenceHeight,
        int framebufferWidth,
        int framebufferHeight,
        double scale,
        Rect physicalViewport)
    {
        ReferenceWidth = referenceWidth;
        ReferenceHeight = referenceHeight;
        FramebufferWidth = framebufferWidth;
        FramebufferHeight = framebufferHeight;
        Scale = scale;
        PhysicalViewport = physicalViewport;
    }

    public int ReferenceWidth { get; }
    public int ReferenceHeight { get; }
    public int FramebufferWidth { get; }
    public int FramebufferHeight { get; }
    public double Scale { get; }
    public Rect PhysicalViewport { get; }

    public static MachinaViewportTransform Create(
        int referenceWidth,
        int referenceHeight,
        int framebufferWidth,
        int framebufferHeight)
    {
        if (referenceWidth <= 0 || referenceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(referenceWidth), "Reference dimensions must be positive.");
        }

        if (framebufferWidth <= 0 || framebufferHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framebufferWidth), "Framebuffer dimensions must be positive.");
        }

        double scale = Math.Min(
            (double)framebufferWidth / referenceWidth,
            (double)framebufferHeight / referenceHeight);
        double viewportWidth = referenceWidth * scale;
        double viewportHeight = referenceHeight * scale;
        var viewport = new Rect(
            (framebufferWidth - viewportWidth) / 2,
            (framebufferHeight - viewportHeight) / 2,
            viewportWidth,
            viewportHeight);
        return new MachinaViewportTransform(
            referenceWidth,
            referenceHeight,
            framebufferWidth,
            framebufferHeight,
            scale,
            viewport);
    }

    public Rect ToPhysical(Rect logical)
    {
        return new Rect(
            PhysicalViewport.X + (logical.X * Scale),
            PhysicalViewport.Y + (logical.Y * Scale),
            logical.Width * Scale,
            logical.Height * Scale);
    }

    public (double X, double Y) ToPhysical(double logicalX, double logicalY)
    {
        return (
            PhysicalViewport.X + (logicalX * Scale),
            PhysicalViewport.Y + (logicalY * Scale));
    }

    public (double X, double Y) ToLogical(double physicalX, double physicalY)
    {
        return (
            (physicalX - PhysicalViewport.X) / Scale,
            (physicalY - PhysicalViewport.Y) / Scale);
    }

    public bool ContainsPhysical(double x, double y)
    {
        return x >= PhysicalViewport.X
            && y >= PhysicalViewport.Y
            && x < PhysicalViewport.X + PhysicalViewport.Width
            && y < PhysicalViewport.Y + PhysicalViewport.Height;
    }
}
