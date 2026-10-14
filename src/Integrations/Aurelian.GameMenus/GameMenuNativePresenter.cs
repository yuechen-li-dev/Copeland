using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Machina;
using Aurelian.Rendering.Contracts.Shaders;
using Machina.Core.Styling;
using Machina.Presentation;

namespace Aurelian.GameMenus;

/// <summary>Realizes the bounded menu template with retained native analytic shapes and MSDF text.</summary>
public sealed class GameMenuNativePresenter : IDisposable
{
    private readonly VulkanNativeFrameTarget target;
    private readonly VulkanOrderedQuadRenderer shapes;
    private readonly VulkanOrderedQuadRenderer text;
    private readonly AurelianMsdfAtlasCache fontCache;
    private readonly AurelianNativeUiFont font;
    private MachinaPresentationFrame? previousFrame;
    private double previousScale = 1;
    private double scale = 1;
    private readonly List<NativeAnalyticShapeSubmission> shapeSubmissions = [];
    private readonly List<NativeMsdfQuadSubmission> textSubmissions = [];

    public GameMenuNativePresenter(
        AurelianVulkanPlant plant,
        VulkanNativeFrameTarget target,
        CompiledGraphicsProgram shapeProgram,
        CompiledGraphicsProgram textProgram,
        AurelianNativeUiFont font)
    {
        this.target = target;
        this.font = font;
        shapes = new VulkanOrderedQuadRenderer(plant, shapeProgram, target, Native2DPipelineOptions.AnalyticShape2D);
        text = new VulkanOrderedQuadRenderer(plant, textProgram, target, Native2DPipelineOptions.MsdfText);
        fontCache = new AurelianMsdfAtlasCache(text);
        foreach (var atlas in font.Resources)
        {
            fontCache.Resolve(atlas);
        }
    }

    public int GeometryRebuilds { get; private set; }
    public int FontUploads => fontCache.UploadCount;

    /// <summary>
    /// Physical pixels per logical UI unit. Layout happens on the logical surface
    /// (<see cref="LogicalWidth"/> x <see cref="LogicalHeight"/>); realization scales shapes and MSDF glyphs.
    /// </summary>
    public double Scale
    {
        get => scale;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "UI scale must be finite and positive.");
            }
            scale = value;
        }
    }

    public int LogicalWidth => (int)Math.Floor(target.Width / scale);
    public int LogicalHeight => (int)Math.Floor(target.Height / scale);

    public VulkanNativeFrameResult Render(GameMenuView view, GameMenuPage page, int selectedIndex, bool capture = false,
        NativeFrameClearColor? clear = null)
    {
        var prepared = view.Prepare(page, selectedIndex, LogicalWidth, LogicalHeight);
        return RenderPrepared(prepared.PresentationFrame, capture, clear);
    }

    /// <summary>
    /// Also supports basic HUDs composed of the same native shapes and text. By default the overlay is composited
    /// over the target's existing contents; pass <paramref name="clear"/> when nothing was drawn underneath.
    /// </summary>
    public VulkanNativeFrameResult RenderPrepared(MachinaPresentationFrame presentation, bool capture = false,
        NativeFrameClearColor? clear = null)
    {
        if (!ReferenceEquals(previousFrame, presentation) || previousScale != scale)
        {
            Realize(presentation);
            previousFrame = presentation;
            previousScale = scale;
        }
        using var frame = clear is { } color
            ? target.BeginFrame(color, preserveContents: false)
            : target.BeginFrame(NativeFrameClearColor.Transparent, preserveContents: true);
        frame.Present(shapes, renderer =>
        {
            foreach (var shape in shapeSubmissions)
            {
                renderer.SubmitAnalyticShape(shape);
            }
        });
        frame.Present(text, renderer =>
        {
            foreach (var glyph in textSubmissions)
            {
                renderer.SubmitMsdfQuad(glyph);
            }
        });
        return frame.EndFrame(capture);
    }

    private void Realize(MachinaPresentationFrame frame)
    {
        shapeSubmissions.Clear();
        textSubmissions.Clear();
        GeometryRebuilds++;
        foreach (var operation in frame.Operations)
        {
            MachinaAnalyticShapePrimitive? shape = operation switch
            {
                MachinaAnalyticShapePrimitive analytic => analytic,
                FillRectangleOperation fill => new(fill.SourceId, MachinaAnalyticShapeKind.RoundedRect, fill.Rect, fill.Color),
                StrokeRectangleOperation stroke => new(stroke.SourceId, MachinaAnalyticShapeKind.RoundedRect,
                    stroke.Rect, ColorToken.Hex(0), borderColor: stroke.Color, borderWidth: stroke.Thickness),
                _ => null,
            };
            if (shape is not null)
            {
                var submission = AurelianAnalyticShapePresentationAdapter.Adapt(shape);
                if (submission.HasValue)
                {
                    shapeSubmissions.Add(ScaleShape(submission.Value));
                }
            }
            else if (operation is PositionedTextOperation source)
            {
                var qualified = font.Qualify(source);
                int first = textSubmissions.Count;
                AurelianMsdfTextPresentationAdapter.AdaptInto(qualified, font.ResourceFor(qualified), fontCache, textSubmissions);
                for (int index = first; index < textSubmissions.Count; index++)
                {
                    textSubmissions[index] = ScaleGlyph(textSubmissions[index]);
                }
            }
            else
            {
                throw new NotSupportedException($"The basic menu template does not support operation {operation.GetType().Name}.");
            }
        }
    }

    private NativeAnalyticShapeSubmission ScaleShape(NativeAnalyticShapeSubmission shape)
    {
        if (scale == 1)
        {
            return shape;
        }
        float factor = (float)scale;
        return shape with
        {
            Destination = ScaleRect(shape.Destination, factor),
            ShapeSize = new Native2DSize(shape.ShapeSize.Width * factor, shape.ShapeSize.Height * factor),
            Radius = shape.Radius * factor,
            BorderWidth = shape.BorderWidth * factor,
        };
    }

    private NativeMsdfQuadSubmission ScaleGlyph(NativeMsdfQuadSubmission glyph)
    {
        if (scale == 1)
        {
            return glyph;
        }
        float factor = (float)scale;
        return glyph with
        {
            Destination = ScaleRect(glyph.Destination, factor),
            Msdf = NativeMsdfParameters.Create(glyph.Msdf.PixelRange, glyph.Msdf.FieldScale * factor),
        };
    }

    private static Native2DRect ScaleRect(Native2DRect rect, float factor)
    {
        return new Native2DRect(rect.X * factor, rect.Y * factor, rect.Width * factor, rect.Height * factor);
    }

    public void Dispose()
    {
        fontCache.Dispose();
        text.Dispose();
        shapes.Dispose();
    }
}
