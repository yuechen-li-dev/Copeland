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

    public VulkanNativeFrameResult Render(GameMenuView view, GameMenuPage page, int selectedIndex, bool capture = false)
    {
        var prepared = view.Prepare(page, selectedIndex, (int)target.Width, (int)target.Height);
        return RenderPrepared(prepared.PresentationFrame, capture);
    }

    /// <summary>Also supports basic HUDs composed of the same native shapes and text.</summary>
    public VulkanNativeFrameResult RenderPrepared(MachinaPresentationFrame presentation, bool capture = false)
    {
        if (!ReferenceEquals(previousFrame, presentation))
        {
            Realize(presentation);
            previousFrame = presentation;
        }
        using var frame = target.BeginFrame(NativeFrameClearColor.Transparent, preserveContents: true);
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
                    shapeSubmissions.Add(submission.Value);
                }
            }
            else if (operation is PositionedTextOperation source)
            {
                var qualified = font.Qualify(source);
                AurelianMsdfTextPresentationAdapter.AdaptInto(qualified, font.ResourceFor(qualified), fontCache, textSubmissions);
            }
            else
            {
                throw new NotSupportedException($"The basic menu template does not support operation {operation.GetType().Name}.");
            }
        }
    }

    public void Dispose()
    {
        fontCache.Dispose();
        text.Dispose();
        shapes.Dispose();
    }
}
