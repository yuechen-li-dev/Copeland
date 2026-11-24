using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Core.Authoring;
using Machina.Layout.Geometry;
using Machina.Layout.Frames;
using Machina.Layout.Rows;
using Machina.Standard.Theme;
using Machina.Standard.Widgets;

namespace Machina.Pipeline;

/// <summary>
/// Existing preparation plus measured, viewport-local overlays. The first pass
/// resolves trigger geometry; each nested overlay resolves before its children.
/// No stale previous-frame anchor positions or renderer-owned widget semantics.
/// </summary>
public sealed class MachinaWidgetPipeline
{
    private readonly MachinaPresentationPipeline pipeline = new();

    public MachinaPreparedPresentation Prepare(WidgetSession session, Func<WidgetBuilder, UiNode> declaration,
        int width, int height, UiLoweringOptions? options = null, StandardTheme? theme = null, WidgetStyle? style = null)
        => PrepareCore(session, declaration, width, height, options, theme, style, true, false);

    /// <summary>Prepares a transparent widget layer above a host's independently rendered content.</summary>
    public MachinaPreparedPresentation PrepareOverlay(WidgetSession session, Action<WidgetBuilder> declaration,
        int width, int height, UiLoweringOptions? options = null, StandardTheme? theme = null, WidgetStyle? style = null)
        => PrepareCore(session, builder =>
        {
            declaration(builder);
            return UI.Layer();
        }, width, height, options, theme, style, true, true);

    private MachinaPreparedPresentation PrepareCore(WidgetSession session, Func<WidgetBuilder, UiNode> declaration,
        int width, int height, UiLoweringOptions? options, StandardTheme? theme, WidgetStyle? style, bool correctScroll, bool transparent)
    {
        var builder = session.CreateBuilder(theme, style);
        builder.TextMeasurer = options?.TextMeasurer ?? Machina.Core.Measurement.DeterministicTextMeasurer.Instance;
        UiNode content = declaration(builder);
        // The widget pipeline owns the root so overlays can escape content
        // clipping. Accept the ordinary Surface authoring convention while
        // retaining its node ID, style, semantics and child declarations.
        if (content is LayerNode { Frame: RootFrame } surface)
            content = surface with { Frame = null, Width = null, Height = null };
        var overlays = new List<UiNode>();
        var prepared = pipeline.Prepare(builder.Compose(content, overlays, width, height, transparent), width, height, options);
        var unresolved = builder.Popups.ToList();
        while (unresolved.Count > 0)
        {
            var ready = unresolved.FirstOrDefault(popup => popup.PointAnchor is not null || prepared.Resolved.Nodes.ContainsKey(popup.Trigger));
            if (ready is null) break; // Content of a closed parent is not an active popup.
            Rect anchor = ready.PointAnchor ?? prepared.Resolved.Nodes[ready.Trigger].Rect;
            overlays.Add(builder.CreateOverlay(ready.Id, anchor, new Rect(0, 0, width, height)));
            unresolved.Remove(ready);
            prepared = pipeline.Prepare(builder.Compose(content, overlays, width, height, transparent), width, height, options);
        }
        session.Present(builder, prepared.Lowering, prepared.Resolved);
        // Opening on an offscreen selected option needs one bounded layout
        // correction. Subsequent navigation updates offsets before declaration.
        if (correctScroll && session.NeedsReprepare)
            return PrepareCore(session, declaration, width, height, options, theme, style, false, transparent);
        return prepared;
    }
}
