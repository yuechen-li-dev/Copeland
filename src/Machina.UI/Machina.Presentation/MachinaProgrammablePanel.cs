using Copeland.SpanAllocation;
using Machina.Core.Styling;
using Machina.Layout.Geometry;

namespace Machina.Presentation;

public enum MachinaPanelSampling
{
    Stretch,
    Tile,
    Crop,
}

public enum MachinaPanelCenterPolicy
{
    AnalyticFill,
    StretchRegion,
    TileRegion,
}

public sealed record MachinaPanelEdgeSegment(
    string Id,
    Rect SourceRect,
    SpanAllocationKind AllocationKind,
    int MinimumLength,
    int Weight,
    MachinaPanelSampling Sampling);

public sealed record MachinaPanelEdgeProgram(IReadOnlyList<MachinaPanelEdgeSegment> Segments);

public sealed record MachinaProgrammablePanelPrimitive : MachinaPresentationOperation
{
    public MachinaProgrammablePanelPrimitive(
        string sourceId,
        MachinaTextureAssetId texture,
        Rect destinationRect,
        Rect topLeft,
        Rect topRight,
        Rect bottomRight,
        Rect bottomLeft,
        MachinaPanelEdgeProgram top,
        MachinaPanelEdgeProgram right,
        MachinaPanelEdgeProgram bottom,
        MachinaPanelEdgeProgram left,
        MachinaPanelCenterPolicy centerPolicy,
        Rect? centerSource,
        double borderScale = 1,
        ColorToken? tint = null)
    {
        SourceId = MachinaPresentationValidation.ValidateSourceId(sourceId);
        Texture = texture;
        DestinationRect = MachinaPresentationValidation.ValidateRect(destinationRect, nameof(destinationRect));
        if (DestinationRect.Width <= 0 || DestinationRect.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationRect), "Panel destination dimensions must be positive.");
        }

        TopLeftSource = ValidateSource(topLeft, nameof(topLeft));
        TopRightSource = ValidateSource(topRight, nameof(topRight));
        BottomRightSource = ValidateSource(bottomRight, nameof(bottomRight));
        BottomLeftSource = ValidateSource(bottomLeft, nameof(bottomLeft));
        Top = ValidateEdge(top, nameof(top));
        Right = ValidateEdge(right, nameof(right));
        Bottom = ValidateEdge(bottom, nameof(bottom));
        Left = ValidateEdge(left, nameof(left));
        if (!Enum.IsDefined(centerPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(centerPolicy));
        }
        if (centerPolicy != MachinaPanelCenterPolicy.AnalyticFill && centerSource is null)
        {
            throw new ArgumentException("A textured center policy requires a center source region.", nameof(centerSource));
        }
        if (centerSource is Rect source)
        {
            CenterSource = ValidateSource(source, nameof(centerSource));
        }
        if (!double.IsFinite(borderScale) || borderScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(borderScale), "Panel border scale must be finite and positive.");
        }

        CenterPolicy = centerPolicy;
        BorderScale = borderScale;
        Tint = tint ?? ColorToken.White;
    }

    public string SourceId { get; }
    public MachinaTextureAssetId Texture { get; }
    public Rect DestinationRect { get; }
    public Rect TopLeftSource { get; }
    public Rect TopRightSource { get; }
    public Rect BottomRightSource { get; }
    public Rect BottomLeftSource { get; }
    public MachinaPanelEdgeProgram Top { get; }
    public MachinaPanelEdgeProgram Right { get; }
    public MachinaPanelEdgeProgram Bottom { get; }
    public MachinaPanelEdgeProgram Left { get; }
    public MachinaPanelCenterPolicy CenterPolicy { get; }
    public Rect? CenterSource { get; }
    public double BorderScale { get; }
    public ColorToken Tint { get; }

    private static Rect ValidateSource(Rect source, string name)
    {
        Rect result = MachinaPresentationValidation.ValidateRect(source, name);
        if (result.Width <= 0 || result.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "Panel source dimensions must be positive.");
        }
        return result;
    }

    private static MachinaPanelEdgeProgram ValidateEdge(MachinaPanelEdgeProgram edge, string name)
    {
        ArgumentNullException.ThrowIfNull(edge, name);
        if (edge.Segments.Count == 0)
        {
            throw new ArgumentException("Panel edges must contain at least one segment.", name);
        }
        foreach (MachinaPanelEdgeSegment segment in edge.Segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Id))
            {
                throw new ArgumentException("Panel segment identity must not be empty.", name);
            }
            ValidateSource(segment.SourceRect, name);
        }
        return edge;
    }
}

public sealed record MachinaPanelResolvedSegment(
    string Edge,
    string SegmentId,
    int Offset,
    int Length,
    SpanAllocationKind AllocationKind);

public sealed record MachinaPanelDiagnostic(
    string Code,
    string Edge,
    string Message);

public sealed record MachinaPanelEdgeAllocation(
    string Edge,
    int Extent,
    int MinimumDemand,
    int UsedLength,
    int UnusedLength,
    int DeficitLength,
    SpanAllocationStatus Status);

public readonly record struct MachinaPanelQuad(
    Rect DestinationRect,
    Rect SourceRect,
    string SemanticId);

public sealed record MachinaPanelLoweringResult(
    IReadOnlyList<MachinaPanelQuad> Quads,
    IReadOnlyList<MachinaPanelResolvedSegment> Segments,
    IReadOnlyList<MachinaPanelEdgeAllocation> EdgeAllocations,
    IReadOnlyList<MachinaPanelDiagnostic> Diagnostics);

public static class MachinaProgrammablePanelLowerer
{
    public static MachinaPanelLoweringResult Lower(MachinaProgrammablePanelPrimitive primitive)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        Rect destination = primitive.DestinationRect;
        (double left, double right) = FitMargins(
            Math.Max(primitive.TopLeftSource.Width, primitive.BottomLeftSource.Width) * primitive.BorderScale,
            Math.Max(primitive.TopRightSource.Width, primitive.BottomRightSource.Width) * primitive.BorderScale,
            destination.Width);
        (double top, double bottom) = FitMargins(
            Math.Max(primitive.TopLeftSource.Height, primitive.TopRightSource.Height) * primitive.BorderScale,
            Math.Max(primitive.BottomLeftSource.Height, primitive.BottomRightSource.Height) * primitive.BorderScale,
            destination.Height);

        var quads = new List<MachinaPanelQuad>();
        var resolved = new List<MachinaPanelResolvedSegment>();
        var edgeAllocations = new List<MachinaPanelEdgeAllocation>();
        var diagnostics = new List<MachinaPanelDiagnostic>();
        AddQuad(quads, new Rect(destination.X, destination.Y, left, top), primitive.TopLeftSource, "corner.top-left");
        AddQuad(quads, new Rect(destination.X + destination.Width - right, destination.Y, right, top), primitive.TopRightSource, "corner.top-right");
        AddQuad(quads, new Rect(destination.X + destination.Width - right, destination.Y + destination.Height - bottom, right, bottom), primitive.BottomRightSource, "corner.bottom-right");
        AddQuad(quads, new Rect(destination.X, destination.Y + destination.Height - bottom, left, bottom), primitive.BottomLeftSource, "corner.bottom-left");

        LowerEdge(primitive.Top, "top", horizontal: true,
            new Rect(destination.X + left, destination.Y, destination.Width - left - right, top), primitive.BorderScale, quads, resolved, edgeAllocations, diagnostics);
        LowerEdge(primitive.Right, "right", horizontal: false,
            new Rect(destination.X + destination.Width - right, destination.Y + top, right, destination.Height - top - bottom), primitive.BorderScale, quads, resolved, edgeAllocations, diagnostics);
        LowerEdge(primitive.Bottom, "bottom", horizontal: true,
            new Rect(destination.X + left, destination.Y + destination.Height - bottom, destination.Width - left - right, bottom), primitive.BorderScale, quads, resolved, edgeAllocations, diagnostics);
        LowerEdge(primitive.Left, "left", horizontal: false,
            new Rect(destination.X, destination.Y + top, left, destination.Height - top - bottom), primitive.BorderScale, quads, resolved, edgeAllocations, diagnostics);

        Rect centerDestination = new(
            destination.X + left,
            destination.Y + top,
            Math.Max(0, destination.Width - left - right),
            Math.Max(0, destination.Height - top - bottom));
        if (primitive.CenterSource is Rect centerSource && centerDestination.Width > 0 && centerDestination.Height > 0)
        {
            if (primitive.CenterPolicy == MachinaPanelCenterPolicy.TileRegion)
            {
                AddTiledCenter(quads, centerDestination, centerSource);
            }
            else if (primitive.CenterPolicy == MachinaPanelCenterPolicy.StretchRegion)
            {
                AddQuad(quads, centerDestination, centerSource, "center");
            }
        }

        return new MachinaPanelLoweringResult(quads, resolved, edgeAllocations, diagnostics);
    }

    public static MachinaProgrammablePanelPrimitive FromNineSlice(MachinaNineSlicePrimitive primitive)
    {
        return MachinaPanelPrebuilt.NineSlice(primitive);
    }

    private static void LowerEdge(
        MachinaPanelEdgeProgram edge,
        string edgeName,
        bool horizontal,
        Rect destination,
        double borderScale,
        ICollection<MachinaPanelQuad> quads,
        ICollection<MachinaPanelResolvedSegment> resolved,
        ICollection<MachinaPanelEdgeAllocation> edgeAllocations,
        ICollection<MachinaPanelDiagnostic> diagnostics)
    {
        int extent = Math.Max(0, (int)Math.Round(horizontal ? destination.Width : destination.Height));
        SpanAllocationRequest<MachinaPanelEdgeSegment>[] requests = edge.Segments
            .Select(segment => segment.AllocationKind == SpanAllocationKind.Fixed
                ? SpanAllocationRequest<MachinaPanelEdgeSegment>.Fixed(segment, segment.MinimumLength)
                : SpanAllocationRequest<MachinaPanelEdgeSegment>.Flex(segment, segment.MinimumLength, segment.Weight))
            .ToArray();
        SpanAllocationResult<MachinaPanelEdgeSegment> allocation = SpanAllocator.Resolve(extent, requests);
        edgeAllocations.Add(new MachinaPanelEdgeAllocation(
            edgeName,
            allocation.Extent,
            allocation.MinimumDemand,
            allocation.UsedLength,
            allocation.UnusedLength,
            allocation.DeficitLength,
            allocation.Status));
        foreach (SpanAllocationDiagnostic diagnostic in allocation.Diagnostics)
        {
            diagnostics.Add(new MachinaPanelDiagnostic(diagnostic.Code, edgeName, diagnostic.Message));
        }

        foreach (SpanPlacement<MachinaPanelEdgeSegment> placement in allocation.Placements)
        {
            MachinaPanelEdgeSegment segment = placement.Payload;
            resolved.Add(new MachinaPanelResolvedSegment(edgeName, segment.Id, placement.Offset, placement.Length, placement.Kind));
            if (placement.Length == 0)
            {
                continue;
            }

            Rect segmentDestination = horizontal
                ? new Rect(destination.X + placement.Offset, destination.Y, placement.Length, destination.Height)
                : new Rect(destination.X, destination.Y + placement.Offset, destination.Width, placement.Length);
            AddSampled(
                quads,
                segmentDestination,
                segment.SourceRect,
                horizontal,
                segment.Sampling,
                borderScale,
                edgeName + "." + segment.Id);
        }
    }

    private static void AddSampled(
        ICollection<MachinaPanelQuad> quads,
        Rect destination,
        Rect source,
        bool horizontal,
        MachinaPanelSampling sampling,
        double borderScale,
        string id)
    {
        if (sampling == MachinaPanelSampling.Stretch)
        {
            AddQuad(quads, destination, source, id);
            return;
        }

        if (sampling == MachinaPanelSampling.Crop)
        {
            double sourceLength = horizontal ? source.Width : source.Height;
            double visibleSourceLength = Math.Min(sourceLength, (horizontal ? destination.Width : destination.Height) / borderScale);
            Rect croppedSource = horizontal
                ? new Rect(source.X, source.Y, visibleSourceLength, source.Height)
                : new Rect(source.X, source.Y, source.Width, visibleSourceLength);
            AddQuad(quads, destination, croppedSource, id);
            return;
        }

        double tileSourceLength = horizontal ? source.Width : source.Height;
        double tileDestinationLength = tileSourceLength * borderScale;
        double available = horizontal ? destination.Width : destination.Height;
        for (double offset = 0; offset < available - 0.000001; offset += tileDestinationLength)
        {
            double length = Math.Min(tileDestinationLength, available - offset);
            double sourceFraction = length / tileDestinationLength;
            Rect tileDestination = horizontal
                ? new Rect(destination.X + offset, destination.Y, length, destination.Height)
                : new Rect(destination.X, destination.Y + offset, destination.Width, length);
            Rect tileSource = horizontal
                ? new Rect(source.X, source.Y, source.Width * sourceFraction, source.Height)
                : new Rect(source.X, source.Y, source.Width, source.Height * sourceFraction);
            AddQuad(quads, tileDestination, tileSource, id);
        }
    }

    private static void AddTiledCenter(ICollection<MachinaPanelQuad> quads, Rect destination, Rect source)
    {
        for (double y = 0; y < destination.Height - 0.000001; y += source.Height)
        {
            double height = Math.Min(source.Height, destination.Height - y);
            for (double x = 0; x < destination.Width - 0.000001; x += source.Width)
            {
                double width = Math.Min(source.Width, destination.Width - x);
                AddQuad(
                    quads,
                    new Rect(destination.X + x, destination.Y + y, width, height),
                    new Rect(source.X, source.Y, width, height),
                    "center");
            }
        }
    }

    private static void AddQuad(ICollection<MachinaPanelQuad> quads, Rect destination, Rect source, string id)
    {
        if (destination.Width > 0 && destination.Height > 0 && source.Width > 0 && source.Height > 0)
        {
            quads.Add(new MachinaPanelQuad(destination, source, id));
        }
    }

    private static (double First, double Second) FitMargins(double first, double second, double available)
    {
        double total = first + second;
        if (total <= available || total == 0)
        {
            return (first, second);
        }

        double scale = available / total;
        return (first * scale, second * scale);
    }
}

/// <summary>
/// High-level 3-slice and 9-slice constructors. These are authoring conveniences;
/// explicit edge allocation remains the single lowering mechanism.
/// </summary>
public static class MachinaPanelPrebuilt
{
    public static MachinaPanelEdgeProgram ThreeSliceEdge(
        string id,
        Rect source,
        MachinaPanelSampling sampling)
    {
        return new MachinaPanelEdgeProgram(
            [new MachinaPanelEdgeSegment(id, source, SpanAllocationKind.Flex, 0, 1, sampling)]);
    }

    public static MachinaProgrammablePanelPrimitive NineSlice(MachinaNineSlicePrimitive primitive)
    {
        Rect source = primitive.SourceRect;
        MachinaSliceMargins margins = primitive.Margins;
        Rect topLeft = new(source.X, source.Y, margins.Left, margins.Top);
        Rect topRight = new(source.X + source.Width - margins.Right, source.Y, margins.Right, margins.Top);
        Rect bottomRight = new(source.X + source.Width - margins.Right, source.Y + source.Height - margins.Bottom, margins.Right, margins.Bottom);
        Rect bottomLeft = new(source.X, source.Y + source.Height - margins.Bottom, margins.Left, margins.Bottom);
        Rect top = new(source.X + margins.Left, source.Y, source.Width - margins.Left - margins.Right, margins.Top);
        Rect right = new(source.X + source.Width - margins.Right, source.Y + margins.Top, margins.Right, source.Height - margins.Top - margins.Bottom);
        Rect bottom = new(source.X + margins.Left, source.Y + source.Height - margins.Bottom, source.Width - margins.Left - margins.Right, margins.Bottom);
        Rect left = new(source.X, source.Y + margins.Top, margins.Left, source.Height - margins.Top - margins.Bottom);
        Rect center = new(source.X + margins.Left, source.Y + margins.Top, source.Width - margins.Left - margins.Right, source.Height - margins.Top - margins.Bottom);
        MachinaPanelSampling edgeSampling = primitive.EdgeMode == MachinaNineSliceMode.Tile
            ? MachinaPanelSampling.Tile
            : MachinaPanelSampling.Stretch;
        return new MachinaProgrammablePanelPrimitive(
            primitive.SourceId,
            primitive.Texture,
            primitive.DestinationRect,
            topLeft,
            topRight,
            bottomRight,
            bottomLeft,
            ThreeSliceEdge("top", top, edgeSampling),
            ThreeSliceEdge("right", right, edgeSampling),
            ThreeSliceEdge("bottom", bottom, edgeSampling),
            ThreeSliceEdge("left", left, edgeSampling),
            primitive.CenterMode == MachinaNineSliceMode.Tile
                ? MachinaPanelCenterPolicy.TileRegion
                : MachinaPanelCenterPolicy.StretchRegion,
            center,
            primitive.BorderScale,
            primitive.Tint);
    }

}
