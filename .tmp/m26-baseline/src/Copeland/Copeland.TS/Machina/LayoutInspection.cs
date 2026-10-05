using System.Globalization;
using Copeland.TS.Mir.Machina;

namespace Copeland.TS.MachinaSource;

/// <summary>
/// Stable, backend-neutral view of the compiler's normalized layout graph.
/// It deliberately contains constraints, not browser or DOM measurements.
/// </summary>
public static class LayoutInspection
{
    public const int SchemaVersion = 1;

    public static LayoutInspectionDocument Create(BoundLayoutDeclaration layout, string modulePath, string projectRoot)
    {
        NormalizedLayoutGraph graph = LayoutDataCompiler.Normalize(layout);
        var boxes = new List<LayoutInspectionBox>();
        var derivedFields = (layout.Derivations ?? [])
            .SelectMany(derivation => derivation.FieldsWritten.Select(field => (derivation.TargetBox, Field: field)))
            .ToHashSet();
        Add(graph.Root, parent: null);
        IReadOnlyDictionary<string, int> paintOrders = boxes
            .OrderBy(box => box.PaintKey)
            .Select((box, index) => new { box.SemanticPath, Index = index })
            .ToDictionary(item => item.SemanticPath, item => item.Index, StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> pathByName = boxes
            .GroupBy(box => box.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().SemanticPath, StringComparer.Ordinal);
        IReadOnlyList<LayoutInspectionDerivation> derivations = (layout.Derivations ?? [])
            .OrderBy(derivation => derivation.AuthoredOrder)
            .Select(derivation => new LayoutInspectionDerivation(
                derivation.DerivationId,
                pathByName.GetValueOrDefault(derivation.TargetBox, derivation.TargetBox),
                derivation.Transform.ToString(),
                pathByName.GetValueOrDefault(derivation.SourceBox, derivation.SourceBox),
                derivation.FieldsRead,
                derivation.FieldsWritten,
                derivation.AuthoredOrder,
                derivation.Status.ToString(),
                derivation.GapOrPadding is MachinaLength length ? Length(length) : null,
                Source(derivation.Source, projectRoot)))
            .ToArray();
        return new LayoutInspectionDocument(
            SchemaVersion,
            new LayoutInspectionLayout(
                layout.Name,
                ProjectRelative(modulePath, projectRoot),
                layout.Profile,
                Coordinate(layout.Origin.X),
                Coordinate(layout.Origin.Y),
                Dimension(graph.Root.Dimensions, "width", relativeDerived: false),
                Dimension(graph.Root.Dimensions, "height", relativeDerived: false),
                layout.ResolvedLayerSet.Name,
                layout.Satisfaction?.ContractName,
                layout.Satisfaction is null ? null : layout.Satisfaction.IsSatisfied),
            boxes.Select(box => box with { PaintOrder = paintOrders[box.SemanticPath] }).ToArray(),
            derivations);

        void Add(NormalizedLayoutNode node, string? parent)
        {
            BoundBoxOverflowPolicy overflow = node.Overflow ?? BoundBoxOverflowPolicy.Visible;
            boxes.Add(new LayoutInspectionBox(
                node.Name,
                node.StableIdentity,
                parent,
                node.Kind.ToString().ToLowerInvariant(),
                Origin(node, "x", derivedFields.Contains((node.Name, "x"))),
                Origin(node, "y", derivedFields.Contains((node.Name, "y"))),
                Dimension(node.Dimensions, "width", derivedFields.Contains((node.Name, "width"))),
                Dimension(node.Dimensions, "height", derivedFields.Contains((node.Name, "height"))),
                node.LayerSetIdentity,
                node.LayerIdentity,
                node.LayerRank,
                node.LocalZ,
                node.AuthoredNodeOrder,
                node.PaintOrder,
                node.OriginRelation.ToString(),
                node.Columns,
                node.Gap is MachinaLength gap ? Length(gap) : null,
                node.Source is null ? null : Source(node.Source, projectRoot),
                OverflowPolicy: overflow.Policy.ToString().ToLowerInvariant(),
                OverflowX: overflow.X.ToString().ToLowerInvariant(),
                OverflowY: overflow.Y.ToString().ToLowerInvariant(),
                TextPolicy: node.TextFit is null ? null : new LayoutInspectionTextPolicy(
                    node.TextFit.PreferredFontSize.Px,
                    node.TextFit.MinimumFontSize.Px,
                    node.TextFit.MaximumLines,
                    node.TextFit.Wrap.ToString().ToLowerInvariant(),
                    node.TextFit.Fit.ToString().ToLowerInvariant(),
                    node.TextFit.Fallback.ToString().ToLowerInvariant(),
                    Source(node.TextFit.Source, projectRoot))));
            foreach (NormalizedLayoutNode child in node.Children)
            {
                Add(child, node.StableIdentity);
            }
        }
    }

    public static string FormatLength(LayoutInspectionLength? value)
        => value is null ? "—" : value.Value is not null && value.Unit is not null ? FormatNumber(value.Value.Value) + value.Unit : value.Kind;

    private static LayoutInspectionConstraint Origin(NormalizedLayoutNode node, string name, bool relativeDerived)
    {
        if (node.Origin is not null && node.OriginRelation == NormalizedLayoutOriginRelation.DeclaredRoot)
        {
            return Coordinate(name == "x" ? node.Origin.Local.X : node.Origin.Local.Y);
        }
        return node.Positions is not null && node.Positions.TryGetValue(name, out MachinaLength position)
            ? new LayoutInspectionConstraint(relativeDerived ? "relative-derived" : "declared", Length(position))
            : new LayoutInspectionConstraint(node.OriginRelation == NormalizedLayoutOriginRelation.FlowDerived ? "derived" : "host-unresolved", null);
    }

    private static LayoutInspectionConstraint Coordinate(BoundLayoutCoordinate value)
        => new("declared", new LayoutInspectionLength("fixed", value.Value, value.Unit == LayoutCoordinateUnit.Px ? "px" : "ui"));

    private static LayoutInspectionLength? Dimension(IReadOnlyDictionary<string, BoundLayoutDimension>? dimensions, string name, bool relativeDerived)
    {
        if (dimensions is null || !dimensions.TryGetValue(name, out BoundLayoutDimension? value)) return new LayoutInspectionLength("host-unresolved", null, null);
        return value.Kind switch
        {
            LayoutDimensionKind.Fixed when relativeDerived => Length(value.Length!.Value) with { Kind = "relative-derived" },
            LayoutDimensionKind.Fixed => Length(value.Length!.Value),
            LayoutDimensionKind.Fill => new LayoutInspectionLength("fill", null, null),
            LayoutDimensionKind.Fit => new LayoutInspectionLength("fit", null, null),
            _ => new LayoutInspectionLength("host-unresolved", null, null),
        };
    }

    private static LayoutInspectionLength Length(MachinaLength value)
    {
        bool zeroPx = value.Px == 0 && value.Ui == 0 && value.LiteralUnit == MachinaLengthLiteralUnit.Px;
        if (value.Px != 0 && value.Ui == 0 || zeroPx)
        {
            return new LayoutInspectionLength("fixed", value.Px, "px");
        }

        bool zeroUi = value.Px == 0 && value.Ui == 0 && value.LiteralUnit == MachinaLengthLiteralUnit.Ui;
        if (value.Ui != 0 && value.Px == 0 || zeroUi)
        {
            return new LayoutInspectionLength("fixed", value.Ui, "ui");
        }
        return new LayoutInspectionLength("affine", null, null);
    }

    private static LayoutInspectionSource Source(MachinaSourceSpan source, string root)
        => new(ProjectRelative(source.SourcePath, root), source.Start, source.Start + source.Length);

    private static string ProjectRelative(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "<memory>") return path;
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static string FormatNumber(double value) => value.ToString("0.################", CultureInfo.InvariantCulture);
}

public sealed record LayoutInspectionDocument(int SchemaVersion, LayoutInspectionLayout Layout, IReadOnlyList<LayoutInspectionBox> Boxes, IReadOnlyList<LayoutInspectionDerivation>? Derivations = null);
public sealed record LayoutInspectionLayout(string Name, string Module, string? Profile, LayoutInspectionConstraint OriginX, LayoutInspectionConstraint OriginY, LayoutInspectionLength? Width, LayoutInspectionLength? Height, string LayerSet, string? Contract, bool? Conformance);
public sealed record LayoutInspectionBox(string Name, string SemanticPath, string? Parent, string Kind, LayoutInspectionConstraint OriginX, LayoutInspectionConstraint OriginY, LayoutInspectionLength? Width, LayoutInspectionLength? Height, string LayerSetIdentity, string Layer, int LayerRank, int Z, int AuthoredOrder, NormalizedPaintOrder PaintKey, string OriginRelation, int? Columns, LayoutInspectionLength? Gap, LayoutInspectionSource? Source, int PaintOrder = 0, LayoutInspectionContent? Content = null, string OverflowPolicy = "visible", string OverflowX = "visible", string OverflowY = "visible", LayoutInspectionTextPolicy? TextPolicy = null);
public sealed record LayoutInspectionConstraint(string Kind, LayoutInspectionLength? Value);
public sealed record LayoutInspectionLength(string Kind, double? Value, string? Unit);
public sealed record LayoutInspectionSource(string Path, int Start, int End);
public sealed record LayoutInspectionContent(string Kind, string Display, string? Symbol, int? ItemCount = null);
public sealed record LayoutInspectionTextPolicy(double PreferredFontSize, double MinimumFontSize, int MaximumLines, string WrapMode, string FitMode, string FallbackMode, LayoutInspectionSource Source);
public sealed record LayoutInspectionDerivation(string DerivationId, string TargetBoxId, string Transform, string SourceBoxId, IReadOnlyList<string> FieldsRead, IReadOnlyList<string> FieldsWritten, int AuthoredOrder, string Status, LayoutInspectionLength? GapOrPadding, LayoutInspectionSource Source);
