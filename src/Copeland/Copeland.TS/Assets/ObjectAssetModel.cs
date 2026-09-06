using Copeland.SpanAllocation;
using Copeland.TS.Diagnostics;

namespace Copeland.TS.Assets;

public sealed record ObjectAssetTexture(
    string Id,
    string Source,
    int Width,
    int Height);

public sealed record ObjectAssetRegion(
    string Id,
    int X,
    int Y,
    int Width,
    int Height);

public enum ObjectAssetSampling
{
    Stretch,
    Tile,
    Crop,
}

public sealed record ObjectAssetEdgeSegment(
    string Id,
    string RegionId,
    SpanAllocationKind AllocationKind,
    int MinimumLength,
    int Weight,
    ObjectAssetSampling Sampling);

public sealed record ObjectAssetEdge(IReadOnlyList<ObjectAssetEdgeSegment> Segments)
{
    public int MinimumLength => Segments.Sum(segment => segment.MinimumLength);
}

public enum ObjectAssetCenterPolicy
{
    AnalyticFill,
    StretchRegion,
    TileRegion,
}

public sealed record ObjectAssetPadding(
    int Left,
    int Top,
    int Right,
    int Bottom);

public sealed record ObjectAssetPanel(
    string Id,
    string TopLeftRegionId,
    string TopRightRegionId,
    string BottomRightRegionId,
    string BottomLeftRegionId,
    ObjectAssetEdge Top,
    ObjectAssetEdge Right,
    ObjectAssetEdge Bottom,
    ObjectAssetEdge Left,
    ObjectAssetCenterPolicy CenterPolicy,
    string CenterRegionId,
    double BorderScale,
    ObjectAssetPadding ContentPadding,
    int MinimumWidth,
    int MinimumHeight);

public sealed record ObjectAssetDocument(
    int SchemaVersion,
    string Id,
    ObjectAssetTexture Texture,
    IReadOnlyList<ObjectAssetRegion> Regions,
    IReadOnlyList<ObjectAssetPanel> Panels);

public sealed record ObjectAssetCompilationResult(
    ObjectAssetDocument? Document,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Document is not null && Diagnostics.Count == 0;
}

public sealed record ObjectAssetBuildOutputs(
    string Toml,
    string RuntimeToml,
    string Json,
    string AuditJson);
