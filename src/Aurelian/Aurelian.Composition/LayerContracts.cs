namespace Aurelian.Composition;

public readonly record struct LayerId
{
    public LayerId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value.Trim().ToLowerInvariant();
        if (Value.Length == 0)
        {
            throw new ArgumentException("Layer identity must not be empty.", nameof(value));
        }
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct LayerViewport
{
    public LayerViewport(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)
            || width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Viewport values must be finite and its extent must be positive.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public bool Contains(double x, double y)
    {
        return x >= X && x < X + Width && y >= Y && y < Y + Height;
    }

    public LayerPoint ToLocal(LayerPoint hostPoint, double scale)
    {
        return new LayerPoint((hostPoint.X - X) / scale, (hostPoint.Y - Y) / scale);
    }
}

public readonly record struct LayerPoint(double X, double Y);

public enum LayerPresentationMode
{
    DirectHostPass,
    OffscreenSurface
}

public enum LayerInputPolicy
{
    None,
    HitTest,
    Opaque
}

public enum LayerSurfaceKind
{
    HostBackBuffer,
    Offscreen
}

public sealed record LayerSurfaceDescriptor
{
    public LayerSurfaceDescriptor(int width, int height, double scale = 1, LayerSurfaceKind kind = LayerSurfaceKind.HostBackBuffer)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Surface extent must be positive.");
        }
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "Surface scale must be finite and positive.");
        }

        Width = width;
        Height = height;
        Scale = scale;
        Kind = kind;
    }

    public int Width { get; }
    public int Height { get; }
    public double Scale { get; }
    public LayerSurfaceKind Kind { get; }
    public LayerViewport FullViewport => new(0, 0, Width, Height);
}

public sealed record LayerDescriptor(
    LayerId Id,
    int ZOrder,
    bool Enabled,
    LayerViewport Viewport,
    LayerPresentationMode PresentationMode,
    LayerInputPolicy InputPolicy);

public sealed record LayerPresentationDto(
    LayerId Layer,
    LayerViewport Viewport,
    bool FullRedraw,
    LayerSurfaceKind SurfaceKind,
    string? OutputIdentity = null);

public sealed record LayerUpdateContext(ulong FrameId, TimeSpan Elapsed);

public sealed record LayerPresentationContext(ulong FrameId, LayerSurfaceDescriptor Surface);

public interface IAurelianLayer
{
    LayerDescriptor Describe();
    void Attach(LayerSurfaceDescriptor surface);
    void Resize(LayerSurfaceDescriptor surface);
    void Update(LayerUpdateContext context);
    LayerPresentationDto Present(LayerPresentationContext context);
    LayerInputResult HandleInput(LayerInputEvent input);
    void Detach();
}

public interface IAurelianLayerMessageReceiver<TPayload>
{
    void Receive(LayerMessage<TPayload> message);
}

public sealed record LayerMessage<TPayload>(LayerId Source, LayerId? Target, TPayload Payload);

public interface ILayerApplicationMessageSink
{
    void Publish<TPayload>(LayerMessage<TPayload> message);
}
