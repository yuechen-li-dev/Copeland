namespace Machina.Fonts.Tooling;

public sealed record FontShapeDiff(
    double IntersectionOverUnion,
    double MeanEdgeDistance,
    double P95EdgeDistance,
    double MaxEdgeDistance,
    int LeftOnlyArea,
    int RightOnlyArea,
    int? DeltaLeft,
    int? DeltaTop,
    int? DeltaRight,
    int? DeltaBottom,
    int? DeltaWidth,
    int? DeltaHeight);

public sealed record FontShapeDiffFixtureReport(
    string Id,
    string Text,
    string DirectOutlinePngPath,
    string MsdfPngPath,
    string WireframePngPath,
    FontDiagnosticBounds? DirectOutlineBounds,
    FontDiagnosticBounds? MsdfBounds,
    IReadOnlyList<FontDiagnosticBounds> WireframeBounds,
    FontShapeDiff DirectVsMsdf);

public sealed record FontShapeDiffSizeReport(
    int SizePx,
    string OutputDirectory,
    int CanvasWidth,
    int CanvasHeight,
    double OriginX,
    double BaselineY,
    IReadOnlyList<FontShapeDiffFixtureReport> Fixtures);

public sealed record FontShapeDiffReport(
    string FontPath,
    string FontFace,
    IReadOnlyList<int> FontSizes,
    IReadOnlyList<string> Texts,
    string GeometryReferencePolicy,
    string BrowserKerningPolicy,
    string DiagnosticGridPolicy,
    IReadOnlyList<FontShapeDiffSizeReport> Sizes);

public sealed record LayerCompositionArtifactReport(
    string PresetName,
    int SizePx,
    string TextId,
    string Text,
    string ArtifactPath,
    IReadOnlyList<LayerCompositionLayerReport> Layers,
    IReadOnlyDictionary<string, string?> SourceImagePaths,
    LayerCompositionGridReport? Grid,
    LayerCompositionBoundsReport Bounds,
    string? Notes);

public sealed record LayerCompositionLayerReport(
    string Id,
    string Label,
    bool Visible,
    double Opacity,
    int ZIndex,
    string LayerType);

public sealed record LayerCompositionGridReport(
    bool ShowGrid,
    int GridStep,
    int MajorStep,
    bool ShowUnitLabels);

public sealed record LayerCompositionBoundsReport(
    bool ShowBounds,
    bool ShowWireframes);

public sealed record LayerCompositionReport(
    string OutputDirectory,
    IReadOnlyList<string> PresetsGenerated,
    IReadOnlyList<LayerCompositionArtifactReport> Artifacts);

public sealed record FontDiagnosticExportResult(
    string OutputDirectory,
    string ShapeDiffReportJsonPath,
    string ShapeDiffReportTextPath,
    FontShapeDiffReport ShapeDiffReport,
    string LayerCompositionReportJsonPath,
    string LayerCompositionReportTextPath,
    LayerCompositionReport LayerCompositionReport);
