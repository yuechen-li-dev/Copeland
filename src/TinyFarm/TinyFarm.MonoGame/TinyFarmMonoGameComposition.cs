using Aurelian.Composition;
using Machina.Core.Styling;
using Machina.Presentation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TinyFarm.Presentation;
using System.Diagnostics;

internal sealed class TinyFarmApplicationMessageSink : ILayerApplicationMessageSink
{
    private readonly Queue<TinyFarmUiCommandDto> commands = [];

    public void Publish<TPayload>(LayerMessage<TPayload> message)
    {
        if (message.Payload is not TinyFarmUiCommandDto command)
        {
            throw new InvalidOperationException($"Unsupported TinyFarm application message '{typeof(TPayload).FullName}'.");
        }
        commands.Enqueue(command);
    }

    public bool TryDequeue(out TinyFarmUiCommandDto? command)
    {
        return commands.TryDequeue(out command);
    }
}

internal sealed class TinyFarmMonoGameWorldLayer : IAurelianLayer
{
    public static readonly LayerId Id = new("tiny-farm-world");

    private readonly Action drawWorld;
    private readonly TinyFarmCompositionMetrics metrics;
    private LayerSurfaceDescriptor surface;

    public TinyFarmMonoGameWorldLayer(
        Action drawWorld,
        LayerSurfaceDescriptor surface,
        TinyFarmCompositionMetrics metrics)
    {
        this.drawWorld = drawWorld ?? throw new ArgumentNullException(nameof(drawWorld));
        this.surface = surface;
        this.metrics = metrics;
    }

    public LayerDescriptor Describe() => new(
        Id,
        0,
        true,
        surface.FullViewport,
        LayerPresentationMode.DirectHostPass,
        LayerInputPolicy.HitTest);

    public void Attach(LayerSurfaceDescriptor attachedSurface) => surface = attachedSurface;

    public void Resize(LayerSurfaceDescriptor resizedSurface) => surface = resizedSurface;

    public void Update(LayerUpdateContext context)
    {
    }

    public LayerPresentationDto Present(LayerPresentationContext context)
    {
        long started = Stopwatch.GetTimestamp();
        drawWorld();
        metrics.RecordWorld(Stopwatch.GetElapsedTime(started).TotalMicroseconds);
        return new LayerPresentationDto(Id, surface.FullViewport, true, surface.Kind, "monogame-backbuffer");
    }

    public LayerInputResult HandleInput(LayerInputEvent input)
    {
        return input is LayerPointerButtonChanged or LayerPointerMoved or LayerKeyChanged
            ? LayerInputResult.ConsumedOnly
            : LayerInputResult.Unconsumed;
    }

    public void Detach()
    {
    }
}

internal sealed class TinyFarmMachinaMonoGameLayer : IAurelianLayer, IAurelianLayerMessageReceiver<TinyFarmPresentationSnapshot>
{
    private readonly TinyFarmMachinaUiLayer uiLayer;
    private readonly TinyFarmMonoGamePresentationRenderer renderer;
    private readonly TinyFarmCompositionMetrics metrics;

    public TinyFarmMachinaMonoGameLayer(
        TinyFarmMachinaUiLayer uiLayer,
        TinyFarmMonoGamePresentationRenderer renderer,
        TinyFarmCompositionMetrics metrics)
    {
        this.uiLayer = uiLayer;
        this.renderer = renderer;
        this.metrics = metrics;
    }

    public LayerDescriptor Describe() => uiLayer.Describe();

    public void Attach(LayerSurfaceDescriptor surface) => uiLayer.Attach(surface);

    public void Resize(LayerSurfaceDescriptor surface) => uiLayer.Resize(surface);

    public void Update(LayerUpdateContext context) => uiLayer.Update(context);

    public LayerPresentationDto Present(LayerPresentationContext context)
    {
        LayerPresentationDto result = uiLayer.Present(context);
        long started = Stopwatch.GetTimestamp();
        renderer.Render(uiLayer.Prepared.PresentationFrame);
        metrics.RecordUi(
            uiLayer.LastRecompositionMicroseconds,
            Stopwatch.GetElapsedTime(started).TotalMicroseconds);
        return result;
    }

    public LayerInputResult HandleInput(LayerInputEvent input) => uiLayer.HandleInput(input);

    public void Detach() => uiLayer.Detach();

    public void Receive(LayerMessage<TinyFarmPresentationSnapshot> message) => uiLayer.Receive(message);
}

internal sealed class TinyFarmCompositionMetrics
{
    private double worldMicroseconds;
    private double uiRecompositionMicroseconds;
    private double uiRealizationMicroseconds;

    public int Frames { get; private set; }

    public void RecordWorld(double microseconds)
    {
        worldMicroseconds += microseconds;
    }

    public void RecordUi(double recompositionMicroseconds, double realizationMicroseconds)
    {
        uiRecompositionMicroseconds += recompositionMicroseconds;
        uiRealizationMicroseconds += realizationMicroseconds;
        Frames++;
    }

    public object Snapshot()
    {
        int denominator = Math.Max(1, Frames);
        return new
        {
            frames = Frames,
            worldCpuMicrosecondsPerFrame = worldMicroseconds / denominator,
            machinaRecompositionMicrosecondsPerFrame = uiRecompositionMicroseconds / denominator,
            adapterRealizationCpuMicrosecondsPerFrame = uiRealizationMicroseconds / denominator
        };
    }
}

internal sealed class TinyFarmMonoGamePresentationRenderer
{
    private readonly SpriteBatch spriteBatch;
    private readonly Texture2D pixel;

    public TinyFarmMonoGamePresentationRenderer(SpriteBatch spriteBatch, Texture2D pixel)
    {
        this.spriteBatch = spriteBatch;
        this.pixel = pixel;
    }

    public void Render(MachinaPresentationFrame frame)
    {
        var clips = new Stack<Rectangle>();
        foreach (MachinaPresentationOperation operation in frame.Operations)
        {
            switch (operation)
            {
                case FillRectangleOperation fill:
                    DrawFill(ToRectangle(fill.Rect), ToColor(fill.Color), CurrentClip(clips));
                    break;
                case StrokeRectangleOperation stroke:
                    DrawBorder(
                        ToRectangle(stroke.Rect),
                        ToColor(stroke.Color),
                        Math.Max(1, (int)Math.Round(stroke.Thickness)),
                        CurrentClip(clips));
                    break;
                case PositionedTextOperation text:
                    BitmapText.Draw(
                        spriteBatch,
                        pixel,
                        text.Text,
                        new Vector2((float)text.Rect.X, (float)text.Rect.Y),
                        ToColor(text.Color),
                        text.Style.Size == TextSize.H1 ? 2 : 1,
                        CurrentClip(clips));
                    break;
                case PushRectangularClipOperation push:
                    Rectangle requested = ToRectangle(push.Rect);
                    clips.Push(clips.Count == 0 ? requested : Rectangle.Intersect(clips.Peek(), requested));
                    break;
                case PopClipOperation:
                    clips.Pop();
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported Machina operation '{operation.GetType().FullName}'.");
            }
        }
    }

    private void DrawFill(Rectangle rectangle, Color color, Rectangle? clip)
    {
        Rectangle visible = clip is Rectangle clipRectangle
            ? Rectangle.Intersect(rectangle, clipRectangle)
            : rectangle;
        if (!visible.IsEmpty)
        {
            spriteBatch.Draw(pixel, visible, color);
        }
    }

    private void DrawBorder(Rectangle rectangle, Color color, int width, Rectangle? clip)
    {
        DrawFill(new Rectangle(rectangle.Left, rectangle.Top, rectangle.Width, width), color, clip);
        DrawFill(new Rectangle(rectangle.Left, rectangle.Bottom - width, rectangle.Width, width), color, clip);
        DrawFill(new Rectangle(rectangle.Left, rectangle.Top, width, rectangle.Height), color, clip);
        DrawFill(new Rectangle(rectangle.Right - width, rectangle.Top, width, rectangle.Height), color, clip);
    }

    private static Rectangle? CurrentClip(Stack<Rectangle> clips) => clips.Count == 0 ? null : clips.Peek();

    private static Rectangle ToRectangle(Machina.Layout.Geometry.Rect rectangle)
    {
        return new Rectangle(
            (int)Math.Round(rectangle.X),
            (int)Math.Round(rectangle.Y),
            Math.Max(0, (int)Math.Round(rectangle.Width)),
            Math.Max(0, (int)Math.Round(rectangle.Height)));
    }

    private static Color ToColor(ColorToken color)
    {
        return new Color(
            (byte)(color.Rgba >> 24),
            (byte)(color.Rgba >> 16),
            (byte)(color.Rgba >> 8),
            (byte)color.Rgba);
    }
}
