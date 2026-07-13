using Machina.Core.Flat;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Presentation;
using Machina.Runtime.Input;

namespace Machina.Pipeline;

/// <summary>
/// Prepares backend-neutral Machina presentation intent from UI authoring input.
/// </summary>
public sealed class MachinaPresentationPipeline
{
    public MachinaPreparedPresentation Prepare(UiDocument document, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(document);
        return PrepareDocument(document, width, height);
    }

    public MachinaPreparedPresentation Prepare(UiNode ui, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ValidateDimensions(width, height);

        UiLoweringResult lowering = UiLowerer.Lower(ui);
        LayoutDocument document = LayoutCompiler.CompileLayoutRows(lowering.Rows);

        var rootRect = new Rect(0, 0, width, height);
        ResolvedLayoutDocument resolved = LayoutDocumentResolver.ResolveLayoutDocument(document, rootRect);

        UiHitTestIndex hitTest = UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics);

        var viewport = new MachinaPresentationViewport(width, height);
        MachinaPresentationFrame presentationFrame = MachinaPresentationFrameBuilder.Build(
            lowering,
            resolved,
            viewport);

        return new MachinaPreparedPresentation(lowering, document, resolved, hitTest, presentationFrame);
    }

    private static MachinaPreparedPresentation PrepareDocument(UiDocument document, int width, int height)
    {
        ValidateDimensions(width, height);
        UiLoweringResult lowering = UiDocumentLowerer.Lower(document);
        LayoutDocument layoutDocument = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        var rootRect = new Rect(0, 0, width, height);
        ResolvedLayoutDocument resolved = LayoutDocumentResolver.ResolveLayoutDocument(layoutDocument, rootRect);
        UiHitTestIndex hitTest = UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics);
        MachinaPresentationFrame presentationFrame = MachinaPresentationFrameBuilder.Build(
            lowering,
            resolved,
            new MachinaPresentationViewport(width, height));

        return new MachinaPreparedPresentation(lowering, layoutDocument, resolved, hitTest, presentationFrame);
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");
        }
    }
}
