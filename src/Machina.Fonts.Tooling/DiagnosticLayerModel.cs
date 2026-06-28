using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.Tooling;

public sealed record DiagnosticLayerComposition(
    int Width,
    int Height,
    Rgba32 Background,
    IReadOnlyList<DiagnosticLayer> Layers)
{
    public DiagnosticLayerComposition Validate()
    {
        if (Width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width));
        }

        if (Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Height));
        }

        ArgumentNullException.ThrowIfNull(Layers);

        HashSet<string> layerIds = new(StringComparer.Ordinal);
        foreach (DiagnosticLayer layer in Layers)
        {
            ArgumentNullException.ThrowIfNull(layer);
            layer.Validate();

            if (!layerIds.Add(layer.Id))
            {
                throw new InvalidOperationException($"Duplicate diagnostic layer id '{layer.Id}'.");
            }
        }

        return this;
    }

    public IReadOnlyList<DiagnosticLayer> GetOrderedLayers()
    {
        return Validate()
            .Layers
            .OrderBy(static layer => layer.ZIndex)
            .ThenBy(static layer => layer.Id, StringComparer.Ordinal)
            .ToArray();
    }
}

public abstract record DiagnosticLayer(
    string Id,
    string Label,
    bool Visible,
    double Opacity,
    int ZIndex)
{
    public virtual DiagnosticLayer Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Label);

        if (!double.IsFinite(Opacity) || Opacity < 0d || Opacity > 1d)
        {
            throw new InvalidOperationException($"Layer '{Id}' has invalid opacity '{Opacity}'.");
        }

        return this;
    }
}

public sealed record DiagnosticImageLayer(
    string Id,
    string Label,
    bool Visible,
    double Opacity,
    int ZIndex,
    RgbaImage? Image,
    Rgba32? TintColor = null,
    string? SourcePath = null,
    string? MissingReason = null)
    : DiagnosticLayer(Id, Label, Visible, Opacity, ZIndex)
{
    public override DiagnosticLayer Validate()
    {
        base.Validate();
        return this;
    }
}

public sealed record DiagnosticMaskLayer(
    string Id,
    string Label,
    bool Visible,
    double Opacity,
    int ZIndex,
    InkMask? Mask,
    Rgba32 Color,
    string? SourcePath = null,
    string? MissingReason = null)
    : DiagnosticLayer(Id, Label, Visible, Opacity, ZIndex)
{
    public override DiagnosticLayer Validate()
    {
        base.Validate();
        return this;
    }
}

public sealed record DiagnosticBoundsLayer(
    string Id,
    string Label,
    bool Visible,
    double Opacity,
    int ZIndex,
    IReadOnlyList<DiagnosticBoundsItem> Items)
    : DiagnosticLayer(Id, Label, Visible, Opacity, ZIndex)
{
    public override DiagnosticLayer Validate()
    {
        base.Validate();
        ArgumentNullException.ThrowIfNull(Items);
        return this;
    }
}

public sealed record DiagnosticGridLayer(
    string Id,
    string Label,
    bool Visible,
    double Opacity,
    int ZIndex,
    int GridStep,
    int MajorStep,
    bool ShowUnitLabels,
    Rgba32 GridColor,
    Rgba32 MajorGridColor,
    Rgba32 LabelColor)
    : DiagnosticLayer(Id, Label, Visible, Opacity, ZIndex)
{
    public override DiagnosticLayer Validate()
    {
        base.Validate();
        if (GridStep <= 0)
        {
            throw new InvalidOperationException($"Layer '{Id}' has invalid grid step '{GridStep}'.");
        }

        if (MajorStep <= 0)
        {
            throw new InvalidOperationException($"Layer '{Id}' has invalid major step '{MajorStep}'.");
        }

        return this;
    }
}

public sealed record DiagnosticAxisLayer(
    string Id,
    string Label,
    bool Visible,
    double Opacity,
    int ZIndex,
    bool ShowXAxis,
    bool ShowYAxis,
    bool ShowOriginMarker,
    int TickStep,
    Rgba32 AxisColor)
    : DiagnosticLayer(Id, Label, Visible, Opacity, ZIndex)
{
    public override DiagnosticLayer Validate()
    {
        base.Validate();
        if (TickStep <= 0)
        {
            throw new InvalidOperationException($"Layer '{Id}' has invalid tick step '{TickStep}'.");
        }

        return this;
    }
}

public sealed record DiagnosticBaselineLayer(
    string Id,
    string Label,
    bool Visible,
    double Opacity,
    int ZIndex,
    int BaselineY,
    Rgba32 BaselineColor)
    : DiagnosticLayer(Id, Label, Visible, Opacity, ZIndex);

public sealed record DiagnosticTextLabelLayer(
    string Id,
    string Label,
    bool Visible,
    double Opacity,
    int ZIndex,
    IReadOnlyList<DiagnosticTextLabel> Labels)
    : DiagnosticLayer(Id, Label, Visible, Opacity, ZIndex)
{
    public override DiagnosticLayer Validate()
    {
        base.Validate();
        ArgumentNullException.ThrowIfNull(Labels);
        return this;
    }
}

public enum DiagnosticDifferenceMode
{
    PairwiseMaskOverlay,
    ThreeWayMaskOverlay,
}

public sealed record DiagnosticDifferenceLayer(
    string Id,
    string Label,
    bool Visible,
    double Opacity,
    int ZIndex,
    DiagnosticDifferenceMode Mode,
    InkMask? LeftMask,
    InkMask? RightMask,
    InkMask? ThirdMask,
    Rgba32 BackgroundColor,
    Rgba32 LeftColor,
    Rgba32 RightColor,
    Rgba32 OverlapColor,
    Rgba32? ThirdColor = null,
    Rgba32? BaselineColor = null,
    double BaselineY = 0d,
    string? MissingReason = null)
    : DiagnosticLayer(Id, Label, Visible, Opacity, ZIndex);

public sealed record DiagnosticGlyphWireframeLayer(
    string Id,
    string Label,
    bool Visible,
    double Opacity,
    int ZIndex,
    IReadOnlyList<FontDiagnosticBounds> Bounds,
    Rgba32 StrokeColor,
    bool ShowLabels = false)
    : DiagnosticLayer(Id, Label, Visible, Opacity, ZIndex)
{
    public override DiagnosticLayer Validate()
    {
        base.Validate();
        ArgumentNullException.ThrowIfNull(Bounds);
        return this;
    }
}

public sealed record DiagnosticBoundsItem(
    string Id,
    string Label,
    FontDiagnosticBounds? Bounds,
    Rgba32 StrokeColor,
    bool ShowLabel = false);

public sealed record DiagnosticTextLabel(
    string Text,
    int X,
    int Y,
    Rgba32 Color);
