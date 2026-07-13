using Aurelian.Machina;
using Aurelian.Rendering.Raster;
using AurelianRasterFrame = Aurelian.Rendering.Raster.RasterFrame;
using Machina.Core.Flat;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Layout.Documents;
using Machina.Pipeline;
using Machina.Presentation;
using Machina.Runtime.Input;

namespace Machina.Presenter.Sample;

/// <summary>
/// Sample-owned composition root for the Machina presentation to Aurelian CPU raster path.
/// </summary>
public static class MachinaAurelianCpuRasterComposition
{
    public static MachinaComposedFrame Render(UiNode document, int width, int height)
    {
        MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(document, width, height);
        return Complete(prepared);
    }

    public static MachinaComposedFrame Render(UiDocument document, int width, int height)
    {
        MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(document, width, height);
        return Complete(prepared);
    }

    private static MachinaComposedFrame Complete(MachinaPreparedPresentation prepared)
    {
        AurelianRasterFrame rasterFrame = new AurelianCpuRasterRenderer().Render(
            MachinaPresentationTranslator.Translate(prepared.PresentationFrame));
        return new MachinaComposedFrame(prepared, SampleRasterFrame.From(rasterFrame));
    }
}

public sealed record MachinaComposedFrame(MachinaPreparedPresentation Prepared, SampleRasterFrame RasterFrame)
{
    public UiLoweringResult Lowering => Prepared.Lowering;

    public LayoutDocument Document => Prepared.Document;

    public ResolvedLayoutDocument Resolved => Prepared.Resolved;

    public UiHitTestIndex HitTest => Prepared.HitTest;

    public MachinaPresentationFrame PresentationFrame => Prepared.PresentationFrame;
}
