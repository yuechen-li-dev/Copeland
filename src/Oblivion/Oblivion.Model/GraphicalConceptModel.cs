namespace Oblivion.Model;

public readonly record struct GraphicalConceptPath
{
    public GraphicalConceptPath(string value)
    {
        if (!TryNormalize(value, out string? normalized))
        {
            throw new ArgumentException(
                "A graphical concept path must contain dot-separated readable identifiers.",
                nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public GraphicalConceptPath Child(string localName)
    {
        return new GraphicalConceptPath(Value + "." + localName);
    }

    public bool IsDescendantOf(GraphicalConceptPath parent)
    {
        return Value.StartsWith(parent.Value + ".", StringComparison.Ordinal);
    }

    public override string ToString()
    {
        return Value;
    }

    public static bool TryCreate(string value, out GraphicalConceptPath path)
    {
        if (TryNormalize(value, out string? normalized))
        {
            path = new GraphicalConceptPath(normalized);
            return true;
        }

        path = default;
        return false;
    }

    private static bool TryNormalize(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 240)
        {
            return false;
        }

        string[] segments = normalized.Split('.');
        return segments.Length <= 24 && segments.All(IsValidSegment);
    }

    private static bool IsValidSegment(string segment)
    {
        if (segment.Length == 0 || segment.Length > 64 || !char.IsAsciiLetter(segment[0]))
        {
            return false;
        }

        return segment.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }
}

public enum GraphicalConceptKind
{
    Panel,
    Region,
    EdgeSegment,
    Guide,
    Datum,
    Blockout,
}

public enum GraphicalConceptStage
{
    Authored,
    Resolved,
    Runtime,
}

public enum SpriteCardRelationshipKind
{
    Parent,
    SourceOf,
    ResolvesTo,
    AttachedTo,
    ConstrainedBy,
}

public enum SpriteCardDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public readonly record struct GraphicalSourceLocation(
    string Path,
    int Start,
    int Length,
    int Line,
    int Column);

public readonly record struct GraphicalRect(int X, int Y, int Width, int Height);

public sealed record SpriteCardDiagnostic(
    string Code,
    SpriteCardDiagnosticSeverity Severity,
    string Message,
    GraphicalConceptPath? ConceptPath = null);

public sealed record SpriteCardRelationship(
    SpriteCardRelationshipKind Kind,
    GraphicalConceptPath Target);

public sealed record SpriteCardAuthoredState(
    string Policy,
    int? MinimumLength = null,
    int? Weight = null,
    string? Sampling = null,
    string? RegionId = null);

public sealed record SpriteCardResolvedState(
    int? Offset,
    int? Length,
    GraphicalRect? Bounds,
    string Status);

public sealed record SpriteCardRuntimeState(
    bool SurvivesLowering,
    string Projection);

public sealed record SpriteCard(
    GraphicalConceptPath ConceptPath,
    GraphicalConceptKind Kind,
    string Role,
    GraphicalSourceLocation Source,
    GraphicalRect? SourceRect,
    SpriteCardAuthoredState Authored,
    SpriteCardResolvedState? Resolved,
    SpriteCardRuntimeState Runtime,
    IReadOnlyList<SpriteCardRelationship> Relationships,
    IReadOnlyList<SpriteCardDiagnostic> Diagnostics,
    IReadOnlyList<SpriteCardEditProperty> EditCapabilities);

public enum SpriteCardEditProperty
{
    FlexWeight,
    MinimumLength,
    Sampling,
    SourceRegion,
    GuideVisibility,
}

public sealed record SpriteCardEdgeSummary(
    string Edge,
    int Extent,
    int MinimumDemand,
    int UsedLength,
    int UnusedLength,
    int DeficitLength,
    string Status);

public sealed record SpriteCardProjection(
    string AssetId,
    string PanelId,
    string SourcePath,
    string AtlasImagePath,
    int AtlasWidth,
    int AtlasHeight,
    string SourceSha256,
    long CompileVersion,
    int Width,
    int Height,
    IReadOnlyList<SpriteCard> Cards,
    IReadOnlyList<SpriteCardEdgeSummary> EdgeSummaries,
    IReadOnlyList<SpriteCardDiagnostic> Diagnostics,
    TimeSpan BuildDuration)
{
    public IReadOnlyList<SpriteCard> Filter(
        GraphicalConceptKind? kind = null,
        GraphicalConceptPath? selected = null,
        bool diagnosticsOnly = false)
    {
        IEnumerable<SpriteCard> result = Cards;
        if (kind is not null)
        {
            result = result.Where(card => card.Kind == kind);
        }

        if (selected is not null)
        {
            result = result.Where(card =>
                card.ConceptPath == selected ||
                card.ConceptPath.IsDescendantOf(selected.Value) ||
                selected.Value.IsDescendantOf(card.ConceptPath));
        }

        if (diagnosticsOnly)
        {
            result = result.Where(card => card.Diagnostics.Count > 0);
        }

        return result.ToArray();
    }
}

public sealed record SpriteCardEditIntent(
    GraphicalConceptPath ConceptPath,
    SpriteCardEditProperty Property,
    string Before,
    string After,
    GraphicalSourceLocation Source,
    string ExpectedSourceSha256);

public sealed record SpriteCardEditTrace(
    SpriteCardEditIntent Intent,
    bool Applied,
    string CompileResult,
    string SourceSha256Before,
    string SourceSha256After,
    TimeSpan RecompileDuration,
    IReadOnlyList<SpriteCardDiagnostic> Diagnostics);
