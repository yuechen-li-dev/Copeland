using System.Security.Cryptography;
using System.Text;
namespace Copeland.Profile;

public readonly record struct ProfileSourceSpan(string Path, int Start, int Length)
{
    public static ProfileSourceSpan Generated(string path = "<generated>") => new(path, 0, 1);
}

public sealed record ProfileDiagnostic(string Id, string Message, ProfileSourceSpan Span);

public enum ProfileEdge
{
    Top,
    Right,
    Bottom,
    Left,
}

public abstract record ProfileShapeSpec(string Kind, ProfileSourceSpan Span);

public sealed record RectangleProfileShape(
    double Width,
    double Height,
    ProfileSourceSpan SourceSpan) : ProfileShapeSpec("Rectangle", SourceSpan);

public sealed record RoundedRectangleProfileShape(
    double Width,
    double Height,
    double Radius,
    ProfileSourceSpan SourceSpan) : ProfileShapeSpec("RoundedRectangle", SourceSpan);

public sealed record CircleProfileShape(
    double Radius,
    double CenterX,
    double CenterY,
    ProfileSourceSpan SourceSpan) : ProfileShapeSpec("Circle", SourceSpan);

public sealed record EllipseProfileShape(
    double RadiusX,
    double RadiusY,
    double CenterX,
    double CenterY,
    ProfileSourceSpan SourceSpan) : ProfileShapeSpec("Ellipse", SourceSpan);

public sealed record SlotProfileShape(
    double Length,
    double Width,
    double AngleDegrees,
    double CenterX,
    double CenterY,
    ProfileSourceSpan SourceSpan) : ProfileShapeSpec("Slot", SourceSpan);

public sealed record CapsuleProfileShape(
    VectorPoint From,
    VectorPoint To,
    double Width,
    ProfileSourceSpan SourceSpan) : ProfileShapeSpec("Capsule", SourceSpan);

public sealed record RegularPolygonProfileShape(
    int Sides,
    double Radius,
    double RotationDegrees,
    ProfileSourceSpan SourceSpan) : ProfileShapeSpec("RegularPolygon", SourceSpan);

public sealed record PolygonProfileShape(
    IReadOnlyList<VectorPoint> Points,
    ProfileSourceSpan SourceSpan) : ProfileShapeSpec("Polygon", SourceSpan);

public abstract record ProfileOperation(
    string FeatureId,
    string InputState,
    string OutputState,
    string Kind,
    ProfileSourceSpan Span)
{
    public ProfileTemplateProvenance? TemplateProvenance { get; init; }
}

public sealed record ProfileTemplateProvenance(
    string TemplateName,
    IReadOnlyList<string> SpecializationArguments,
    ProfileSourceSpan InstantiationSpan,
    int GeneratedOperationIndex);

public sealed record AddProfileOperation(
    string Id,
    string Input,
    string Output,
    ProfileShapeSpec Shape,
    ProfileSourceSpan SourceSpan) : ProfileOperation(Id, Input, Output, "Add", SourceSpan);

public sealed record SubtractProfileOperation(
    string Id,
    string Input,
    string Output,
    ProfileShapeSpec Shape,
    ProfileSourceSpan SourceSpan) : ProfileOperation(Id, Input, Output, "Subtract", SourceSpan);

public sealed record HoleProfileOperation(
    string Id,
    string Input,
    string Output,
    CircleProfileShape Hole,
    ProfileSourceSpan SourceSpan) : ProfileOperation(Id, Input, Output, "Hole", SourceSpan);

public sealed record TabProfileOperation(
    string Id,
    string Input,
    string Output,
    ProfileEdge Edge,
    double Width,
    double Depth,
    double Position,
    ProfileSourceSpan SourceSpan) : ProfileOperation(Id, Input, Output, "Tab", SourceSpan);

public sealed record NotchProfileOperation(
    string Id,
    string Input,
    string Output,
    ProfileEdge Edge,
    double Width,
    double Depth,
    double Position,
    ProfileSourceSpan SourceSpan) : ProfileOperation(Id, Input, Output, "Notch", SourceSpan);

public sealed record RepeatRadialProfileOperation(
    string Id,
    string Input,
    string Output,
    int Count,
    double ToothDepth,
    double ToothFraction,
    double RotationDegrees,
    ProfileSourceSpan SourceSpan) : ProfileOperation(Id, Input, Output, "RepeatRadial", SourceSpan);

public sealed record ProfileSpanPattern
{
    public ProfileSpanPattern(IReadOnlyList<ProfileReplacementSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        Segments = segments.ToArray();
    }

    public IReadOnlyList<ProfileReplacementSegment> Segments { get; }

    public string SemanticHash => ProfileHash.Utf8(
        "profile-span-pattern-v1|" + string.Join('|', Segments.Select(segment =>
            $"{segment.Kind}:{segment.Start.X:R},{segment.Start.Y:R}:{segment.End.X:R},{segment.End.Y:R}:{segment.Amount:R}:{segment.Control1.X:R},{segment.Control1.Y:R}:{segment.Control2.X:R},{segment.Control2.Y:R}")));
}

public sealed record RepeatRadialPatternProfileOperation(
    string Id,
    string Input,
    string Output,
    int Count,
    ProfileSpanPattern Pattern,
    double TargetFraction,
    double RotationDegrees,
    ProfileSourceSpan SourceSpan) : ProfileOperation(Id, Input, Output, "RepeatRadialPattern", SourceSpan);

public enum ProfileCurveKind
{
    Line,
    Arc,
    Bulge,
    Spline,
}

public sealed record SegmentReplacement(
    ProfileCurveKind Kind,
    double Amount,
    VectorPoint Control1,
    VectorPoint Control2);

public sealed record ReplaceSegmentProfileOperation(
    string Id,
    string Input,
    string Output,
    int SegmentIndex,
    SegmentReplacement Replacement,
    ProfileSourceSpan SourceSpan) : ProfileOperation(Id, Input, Output, "ReplaceSegment", SourceSpan);

public sealed record ProfileSpanSelection(
    string OwnerState,
    int StartSegmentIndex,
    int SegmentCount)
{
    public string SemanticHash => ProfileHash.Utf8($"profile-span-v1|{OwnerState}|{StartSegmentIndex}|{SegmentCount}");
}

public sealed record ProfileReplacementSegment(
    ProfileCurveKind Kind,
    VectorPoint Start,
    VectorPoint End,
    double Amount,
    VectorPoint Control1,
    VectorPoint Control2);

public sealed record ReplaceSpanProfileOperation(
    string Id,
    string Input,
    string Output,
    ProfileSpanSelection Target,
    IReadOnlyList<ProfileReplacementSegment> Replacement,
    ProfileSourceSpan SourceSpan) : ProfileOperation(Id, Input, Output, "ReplaceSpan", SourceSpan);

public sealed record ReplaceSpanPatternProfileOperation(
    string Id,
    string Input,
    string Output,
    ProfileSpanSelection Target,
    ProfileSpanPattern Pattern,
    ProfileSourceSpan SourceSpan) : ProfileOperation(Id, Input, Output, "ReplaceSpanPattern", SourceSpan);

public sealed record ProfileSegmentSummary(
    string Id,
    string GeometryHash,
    string ProvenanceFeatureId)
{
    public int? GeneratedSegmentIndex { get; init; }

    public int? RepetitionIndex { get; init; }
}

public sealed record TransformProfileOperation(
    string Id,
    string Input,
    string Output,
    string TransformKind,
    double A,
    double B,
    ProfileSourceSpan SourceSpan) : ProfileOperation(Id, Input, Output, TransformKind, SourceSpan);

public sealed record ProfileDefinition(
    string Name,
    string BaseState,
    ProfileShapeSpec Base,
    IReadOnlyList<ProfileOperation> Operations,
    string YieldState,
    ProfileSourceSpan Span);

public sealed record ProfileStateSummary(
    int Index,
    string Name,
    string? ProducingFeatureId,
    string OperationKind,
    IReadOnlyList<string> AppliedFeatureIds,
    int ContourCount,
    VectorBounds Bounds,
    string ContourHash)
{
    public IReadOnlyList<ProfileSegmentSummary> Segments { get; init; } = [];

    public IReadOnlyList<ProfileLoweredReplacementSummary> LoweredReplacements { get; init; } = [];

    public ProfileRadialTargetPreparationSummary? RadialTargetPreparation { get; init; }
}

public sealed record ProfileLoweredReplacementSummary(
    int RepetitionIndex,
    string InputState,
    string OutputState,
    int TargetSegmentIndex,
    int GeneratedSegmentCount);

public sealed record ProfileRadialTargetPreparationSummary(
    string InputState,
    int OriginalOuterSegmentCount,
    int RefinedOuterSegmentCount,
    string Law);

public sealed record ProfileCompilationResult(
    ProfileDefinition? Definition,
    VectorShape? Shape,
    IReadOnlyList<ProfileStateSummary> States,
    IReadOnlyList<ProfileDiagnostic> Diagnostics,
    string? ProfileIrHash,
    string? CanonicalContourHash,
    string? Svg)
{
    public bool Success => Definition is not null && Shape is not null && Diagnostics.Count == 0;

    public ProfileStyle Style { get; init; } = ProfileStyle.Default;
}

internal static class ProfileHash
{
    public static string Utf8(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
