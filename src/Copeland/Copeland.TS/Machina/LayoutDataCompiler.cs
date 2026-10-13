using Copeland.TS.Diagnostics;
using Copeland.TS.Mir.Machina;
using Copeland.TS.Syntax;

namespace Copeland.TS.MachinaSource;

/// <summary>Compiler-owned immutable spatial data, before backend projection.</summary>
public enum LayoutNodeKind { Row, Column, Grid, Anchor, Overlay, Slot }
public enum LayoutDimensionKind { Fixed, Fill, Fit }
public enum LayoutCoordinateUnit { Px, Ui }
public enum NormalizedLayoutOriginRelation { DeclaredRoot, FlowDerived, AnchorDerived, OverlayDerived }
public enum LayoutRelativeTransform { CenterIn, CenterXIn, CenterYIn, AlignLeft, AlignRight, AlignTop, AlignBottom, PlaceLeftOf, PlaceRightOf, PlaceAbove, PlaceBelow, InsetFrom, ExpandFrom }
public enum LayoutDerivationStatus { Resolved, HostUnresolved }
public enum LayoutOverflowPolicy { Visible, Clip, Auto, Scroll, ScrollX, ScrollY }
public enum LayoutOverflowAxis { Visible, Clip, Auto, Scroll }
public enum TextWrapMode { Wrap, NoWrap }
public enum TextFitMode { None, ScaleDown }
public enum TextOverflowFallback { Clip, Ellipsis, Overflow }

public sealed record BoundBoxOverflowPolicy(LayoutOverflowPolicy Policy, LayoutOverflowAxis X, LayoutOverflowAxis Y)
{
    public static BoundBoxOverflowPolicy Visible { get; } = new(LayoutOverflowPolicy.Visible, LayoutOverflowAxis.Visible, LayoutOverflowAxis.Visible);
}

/// <summary>Stable-region text policy; final font metrics remain host-resolved.</summary>
public sealed record BoundTextFitPolicy(
    MachinaLength PreferredFontSize,
    MachinaLength MinimumFontSize,
    int MaximumLines,
    TextWrapMode Wrap,
    TextFitMode Fit,
    TextOverflowFallback Fallback,
    MachinaSourceSpan Source);

/// <summary>
/// Compiler-generic description of a bounded immutable row derivation. M0 has
/// one public profile: rows in <c>layout::Boxes</c>.
/// </summary>
public sealed record BoundRowDerivation(
    string DerivationId,
    string Relation,
    string TargetBox,
    LayoutRelativeTransform Transform,
    string SourceBox,
    IReadOnlyList<string> FieldsRead,
    IReadOnlyList<string> FieldsWritten,
    int AuthoredOrder,
    LayoutDerivationStatus Status,
    MachinaSourceSpan Source,
    MachinaLength? GapOrPadding = null);

public sealed record BoundRelativeDerivationSpec(
    string TargetBox,
    LayoutRelativeTransform Transform,
    string SourceBox,
    MachinaLength? GapOrPadding,
    int AuthoredOrder,
    MachinaSourceSpan Source);

/// <summary>A typed, authored coordinate. It intentionally is not a node position property.</summary>
public sealed record BoundLayoutCoordinate(double Value, LayoutCoordinateUnit Unit);
public sealed record BoundLayoutOrigin(BoundLayoutCoordinate X, BoundLayoutCoordinate Y);
/// <summary>
/// The host-relative value is deliberately absent until a backend supplies the
/// containing coordinate space. Normalization never invents world coordinates.
/// </summary>
public sealed record NormalizedLayoutOrigin(BoundLayoutOrigin Local, BoundLayoutOrigin? ResolvedHostRelative = null);
public sealed record BoundLayoutDimension(LayoutDimensionKind Kind, MachinaLength? Length = null);
/// <summary>One finite semantic layer set. Declaration order is its total rank order.</summary>
public sealed record BoundLayerSet(string Name, string StableIdentity, IReadOnlyList<string> Layers)
{
    public static BoundLayerSet Default { get; } = new("DefaultLayers", "copeland::layers::default", ["default"]);
    public int RankOf(string layer)
    {
        for (int index = 0; index < Layers.Count; index++)
        {
            if (string.Equals(Layers[index], layer, StringComparison.Ordinal)) return index;
        }
        return -1;
    }
}

/// <summary>Authored paint properties. The normalized graph adds the final node ordinal.</summary>
public sealed record BoundPaintProperties(string Layer, int LocalZ)
{
    public static BoundPaintProperties Default { get; } = new("default", 0);
}
public sealed record BoundLayoutNode(
    string Name,
    LayoutNodeKind Kind,
    IReadOnlyDictionary<string, BoundLayoutDimension> Dimensions,
    IReadOnlyDictionary<string, MachinaLength> Positions,
    MachinaLength Gap,
    int? Columns,
    MachinaInsets Padding,
    MachinaStyle Style,
    IReadOnlyList<BoundLayoutNode> Children,
    MachinaSourceSpan Source,
    BoundPaintProperties? Paint = null,
    IReadOnlyList<BoundRelativeDerivationSpec>? RelativeDerivationSpecs = null,
    BoundBoxOverflowPolicy? Overflow = null,
    BoundTextFitPolicy? TextFit = null)
{
    public BoundPaintProperties ResolvedPaint => Paint ?? BoundPaintProperties.Default;
    public BoundBoxOverflowPolicy ResolvedOverflow => Overflow ?? BoundBoxOverflowPolicy.Visible;
}
/// <summary>Closed compile-time topology node. Geometry is intentionally absent.</summary>
public sealed record BoundLayoutTypeNode(
    string Name,
    LayoutNodeKind Kind,
    int? Columns,
    IReadOnlyList<BoundLayoutTypeNode> Children,
    MachinaSourceSpan Source);
public sealed record BoundLayoutTypeDeclaration(string Name, BoundLayoutTypeNode Root);
public sealed record InferredLayoutShape(string Name, LayoutNodeKind Kind, int? Columns, IReadOnlyList<InferredLayoutShape> Children);
public sealed record BoundLayoutSatisfaction(string ContractName, bool IsSatisfied, InferredLayoutShape InferredShape,
    BoundLayoutTypeDeclaration? BoundContract = null);
public sealed record BoundLayoutDeclaration(
    string Name,
    string? Profile,
    BoundLayoutOrigin Origin,
    BoundLayoutNode Root,
    IReadOnlyDictionary<string, BoundLayoutNode> Slots,
    BoundLayoutSatisfaction? Satisfaction = null,
    BoundLayerSet? LayerSet = null,
    IReadOnlyList<BoundRowDerivation>? Derivations = null)
{
    public BoundLayerSet ResolvedLayerSet => LayerSet ?? BoundLayerSet.Default;
}
public sealed record NormalizedPaintOrder(int LayerRank, int LocalZ, int AuthoredNodeOrder) : IComparable<NormalizedPaintOrder>
{
    public int CompareTo(NormalizedPaintOrder? other)
    {
        if (other is null) return 1;
        int layer = LayerRank.CompareTo(other.LayerRank);
        if (layer != 0) return layer;
        int z = LocalZ.CompareTo(other.LocalZ);
        return z != 0 ? z : AuthoredNodeOrder.CompareTo(other.AuthoredNodeOrder);
    }
}
public sealed record NormalizedLayoutNode(
    string Name,
    LayoutNodeKind Kind,
    string StableIdentity,
    NormalizedLayoutOrigin? Origin,
    NormalizedLayoutOriginRelation OriginRelation,
    IReadOnlyList<NormalizedLayoutNode> Children,
    string LayerSetIdentity = "copeland::layers::default",
    string LayerIdentity = "default",
    int LayerRank = 0,
    int LocalZ = 0,
    int AuthoredNodeOrder = 0,
    NormalizedPaintOrder? ResolvedPaintOrder = null,
    IReadOnlyDictionary<string, BoundLayoutDimension>? Dimensions = null,
    IReadOnlyDictionary<string, MachinaLength>? Positions = null,
    MachinaLength? Gap = null,
    int? Columns = null,
    MachinaSourceSpan? Source = null,
    BoundBoxOverflowPolicy? Overflow = null,
    BoundTextFitPolicy? TextFit = null)
{
    public NormalizedPaintOrder PaintOrder => ResolvedPaintOrder ?? new NormalizedPaintOrder(LayerRank, LocalZ, AuthoredNodeOrder);
}
public sealed record NormalizedLayoutGraph(string LayoutName, NormalizedLayoutNode Root, IReadOnlyDictionary<string, string> SlotIdentities);
public sealed record LayoutReactArtifact(string Css, IReadOnlyDictionary<string, string> ClassesBySlot);
/// <summary>Deterministic typed TypeScript surface for semantic React attachment.</summary>
public sealed record LayoutReactProjection(string Css, string TypeScript, IReadOnlyDictionary<string, string> ClassesBySlot);
public sealed class LayoutDataCompilation(
    SyntaxTree syntaxTree,
    IReadOnlyList<Diagnostic> diagnostics,
    IReadOnlyDictionary<string, BoundLayoutDeclaration> layouts,
    IReadOnlyDictionary<string, BoundLayoutTypeDeclaration> layoutTypes,
    IReadOnlyDictionary<string, BoundLayerSet> layerSets)
{
    public SyntaxTree SyntaxTree { get; } = syntaxTree;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    public IReadOnlyDictionary<string, BoundLayoutDeclaration> Layouts { get; } = layouts;
    public IReadOnlyDictionary<string, BoundLayoutTypeDeclaration> LayoutTypes { get; } = layoutTypes;
    public IReadOnlyDictionary<string, BoundLayerSet> LayerSets { get; } = layerSets;
    public bool Success => Diagnostics.Count == 0;
}

/// <summary>
/// Binds <c>layout</c> declarations directly to constrained data. No user
/// authored function is evaluated while obtaining the layout graph.
/// </summary>
public static class LayoutDataCompiler
{
    public static LayoutDataCompilation Compile(string sourceText, string sourcePath)
    {
        SyntaxTree tree = SyntaxTree.Parse(sourceText, sourcePath);
        return Bind(tree, sourcePath);
    }

    /// <summary>
    /// Binds layouts from the parser tree owned by the ordinary compiler. Imported
    /// layouts arrive as already-bound immutable declarations from module binding.
    /// </summary>
    public static LayoutDataCompilation Bind(
        SyntaxTree tree,
        string sourcePath,
        IReadOnlyDictionary<string, BoundLayoutDeclaration>? importedLayouts = null,
        IReadOnlyDictionary<string, BoundLayoutTypeDeclaration>? importedLayoutTypes = null,
        IReadOnlyDictionary<string, BoundLayerSet>? importedLayerSets = null,
        Func<LayoutDeclarationSyntax, BoundLayoutDeclaration?>? constructLayout = null)
    {
        var binder = new Binder(
            tree,
            sourcePath,
            importedLayouts ?? new Dictionary<string, BoundLayoutDeclaration>(StringComparer.Ordinal),
            importedLayoutTypes ?? new Dictionary<string, BoundLayoutTypeDeclaration>(StringComparer.Ordinal),
            importedLayerSets ?? new Dictionary<string, BoundLayerSet>(StringComparer.Ordinal), constructLayout: constructLayout);
        IReadOnlyDictionary<string, BoundLayoutDeclaration> layouts = binder.Bind();
        return new LayoutDataCompilation(tree, tree.Diagnostics.Concat(binder.Diagnostics).ToArray(), layouts, binder.LayoutTypes, binder.LayerSets);
    }

    /// <summary>
    /// Binds a small lexical presentation domain while retaining the owning
    /// compilation tree for diagnostics and source provenance.
    /// </summary>
    public static LayoutDataCompilation BindDeclarations(
        SyntaxTree tree,
        string sourcePath,
        IReadOnlyList<MemberSyntax> declarations,
        IReadOnlyDictionary<string, BoundLayoutDeclaration>? importedLayouts = null,
        IReadOnlyDictionary<string, BoundLayoutTypeDeclaration>? importedLayoutTypes = null,
        IReadOnlyDictionary<string, BoundLayerSet>? importedLayerSets = null)
    {
        var binder = new Binder(
            tree,
            sourcePath,
            importedLayouts ?? new Dictionary<string, BoundLayoutDeclaration>(StringComparer.Ordinal),
            importedLayoutTypes ?? new Dictionary<string, BoundLayoutTypeDeclaration>(StringComparer.Ordinal),
            importedLayerSets ?? new Dictionary<string, BoundLayerSet>(StringComparer.Ordinal),
            declarations);
        IReadOnlyDictionary<string, BoundLayoutDeclaration> layouts = binder.Bind();
        return new LayoutDataCompilation(tree, binder.Diagnostics.ToArray(), layouts, binder.LayoutTypes, binder.LayerSets);
    }

    public static BoundLayoutDeclaration? BindConstruction(LayoutConstruction construction, string sourcePath,
        out IReadOnlyList<Diagnostic> diagnostics, BoundLayoutTypeDeclaration? contract = null)
    {
        var types = new Dictionary<string, BoundLayoutTypeDeclaration>(StringComparer.Ordinal);
        if (contract is not null) types.Add(contract.Name, contract);
        var binder = new Binder(null, sourcePath, new Dictionary<string, BoundLayoutDeclaration>(), types,
            new Dictionary<string, BoundLayerSet>());
        binder.Bind();
        BoundLayoutDeclaration? layout = binder.BindConstruction(construction);
        if (layout is not null)
        {
            layout = binder.ApplyRelativeDerivations(layout);
            if (construction.Contract is not null) layout = binder.CheckSatisfaction(construction.Identifier.Text, construction.Contract, layout);
        }
        diagnostics = binder.Diagnostics;
        return layout;
    }

    public static NormalizedLayoutGraph Normalize(BoundLayoutDeclaration layout)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        // The normalized root follows the same composed frame law as backend
        // projection: declaration-level dimensions remain true of an explicit
        // single root rather than disappearing from the inspection surface.
        BoundLayoutNode rootNode = ProjectionRoot(layout.Root);
        int authoredNodeOrder = 0;
        NormalizedLayoutNode root = NormalizeNode(
            rootNode,
            layout.Name,
            slots,
            new NormalizedLayoutOrigin(layout.Origin),
            NormalizedLayoutOriginRelation.DeclaredRoot,
            layout.ResolvedLayerSet,
            ref authoredNodeOrder);
        return new NormalizedLayoutGraph(layout.Name, root, slots);
    }

    public static LayoutReactArtifact LowerForReact(BoundLayoutDeclaration layout)
    {
        BoundLayoutNode projectionRoot = ProjectionRoot(layout.Root);
        BoundLayoutDimension width = projectionRoot.Dimensions.GetValueOrDefault("width") ?? new BoundLayoutDimension(LayoutDimensionKind.Fixed, MachinaLength.Pixels(0));
        BoundLayoutDimension height = projectionRoot.Dimensions.GetValueOrDefault("height") ?? new BoundLayoutDimension(LayoutDimensionKind.Fixed, MachinaLength.Pixels(0));
        if (width.Kind != LayoutDimensionKind.Fixed || height.Kind != LayoutDimensionKind.Fixed)
        {
            throw new MachinaLayoutException("COPE-LAYOUT-ROOT-0001", "A projected layout root requires fixed width and height.");
        }

        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        MachinaView rootChild = LowerNode(projectionRoot, null, true, "root/0", paths);
        MachinaResolvedDocument resolved = MachinaLayoutResolver.Resolve(Machina.Root([rootChild]), new MachinaRect(0, 0, width.Length!.Value.Resolve(0), height.Length!.Value.Resolve(0)));
        MachinaReactArtifact artifact = MachinaBrowserLowerer.LowerForReact(resolved, ToNamespace(layout.Name));
        var named = paths.ToDictionary(pair => pair.Key, pair => artifact.ClassesByIdentity[pair.Value], StringComparer.Ordinal);
        string rootClass = artifact.ClassesByIdentity["root/0"].Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
        string css = ApplyRootOrigin(artifact.Css, rootClass, layout.Origin);
        css = AppendPaintOrderCss(css, projectionRoot, "root/0", artifact.ClassesByIdentity, layout.ResolvedLayerSet);
        css = AppendOverflowCss(css, projectionRoot, "root/0", artifact.ClassesByIdentity);
        return new LayoutReactArtifact(css, named);
    }

    public static LayoutReactProjection ProjectReact(BoundLayoutDeclaration layout)
    {
        LayoutReactArtifact artifact = LowerForReact(layout);
        var source = new System.Text.StringBuilder();
        source.Append("export const ").Append(layout.Name).Append(" = Object.freeze({\n");
        foreach ((string slot, string className) in artifact.ClassesBySlot.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            source.Append("  \"")
                .Append(EscapeJavaScript(slot))
                .Append("\": Object.freeze({ className: \"")
                .Append(EscapeJavaScript(className))
                .Append("\" }),\n");
        }
        source.Append("} as const);\n");
        return new LayoutReactProjection(artifact.Css, source.ToString(), artifact.ClassesBySlot);
    }

    private static NormalizedLayoutNode NormalizeNode(
        BoundLayoutNode node,
        string path,
        Dictionary<string, string> slots,
        NormalizedLayoutOrigin? origin,
        NormalizedLayoutOriginRelation originRelation,
        BoundLayerSet layerSet,
        ref int authoredNodeOrder)
    {
        string identity = path + "." + node.Name;
        slots.Add(node.Name, identity);
        int order = authoredNodeOrder++;
        BoundPaintProperties paint = node.ResolvedPaint;
        var children = new List<NormalizedLayoutNode>();
        foreach (BoundLayoutNode child in node.Children)
        {
            children.Add(NormalizeNode(child, identity, slots, null, OriginRelationForChild(node, child), layerSet, ref authoredNodeOrder));
        }
        return new NormalizedLayoutNode(
            node.Name,
            node.Kind,
            identity,
            origin,
            originRelation,
            children,
            layerSet.StableIdentity,
            paint.Layer,
            layerSet.RankOf(paint.Layer),
            paint.LocalZ,
            order,
            new NormalizedPaintOrder(layerSet.RankOf(paint.Layer), paint.LocalZ, order),
            node.Dimensions,
            node.Positions,
            node.Gap,
            node.Columns,
            node.Source,
            node.ResolvedOverflow,
            node.TextFit);
    }

    private static string AppendPaintOrderCss(
        string css,
        BoundLayoutNode root,
        string rootIdentity,
        IReadOnlyDictionary<string, string> classesByIdentity,
        BoundLayerSet layerSet)
    {
        var rules = new System.Text.StringBuilder("/* Copeland deterministic paint order: layer rank, local z, then DOM authored order. */\n");
        Append(root, rootIdentity, isRoot: true);
        return css + "\n" + rules;

        void Append(BoundLayoutNode node, string identity, bool isRoot)
        {
            string classes = classesByIdentity[identity];
            string frameClass = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
            BoundPaintProperties paint = node.ResolvedPaint;
            // Eleven bounded z positions per declared semantic layer. The value
            // is compiler generated from declaration rank; authored code never
            // supplies an unbounded browser z-index.
            int browserZ = (layerSet.RankOf(paint.Layer) * 11) + paint.LocalZ + 5;
            rules.Append('.').Append(frameClass).Append(" {\n")
                .Append("  z-index: ").Append(browserZ).Append(";\n");
            if (isRoot) rules.Append("  isolation: isolate;\n");
            rules.Append("}\n\n");
            for (int index = 0; index < node.Children.Count; index++)
            {
                Append(node.Children[index], identity + "/" + index, false);
            }
        }
    }

    private static string AppendOverflowCss(
        string css,
        BoundLayoutNode root,
        string rootIdentity,
        IReadOnlyDictionary<string, string> classesByIdentity)
    {
        var rules = new System.Text.StringBuilder("/* Copeland explicit overflow policy. */\n");
        Append(root, rootIdentity);
        return css + "\n" + rules;

        void Append(BoundLayoutNode node, string identity)
        {
            string frameClass = classesByIdentity[identity].Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
            BoundBoxOverflowPolicy overflow = node.ResolvedOverflow;
            rules.Append('.').Append(frameClass).Append(" {\n")
                .Append("  overflow-x: ").Append(CssOverflow(overflow.X)).Append(";\n")
                .Append("  overflow-y: ").Append(CssOverflow(overflow.Y)).Append(";\n")
                .Append("}\n\n");
            for (int index = 0; index < node.Children.Count; index++)
            {
                Append(node.Children[index], identity + "/" + index);
            }
        }
    }

    private static string CssOverflow(LayoutOverflowAxis axis) => axis switch
    {
        LayoutOverflowAxis.Visible => "visible",
        LayoutOverflowAxis.Clip => "hidden",
        LayoutOverflowAxis.Auto => "auto",
        LayoutOverflowAxis.Scroll => "scroll",
        _ => throw new InvalidOperationException("Unknown overflow axis."),
    };

    private static NormalizedLayoutOriginRelation OriginRelationForChild(BoundLayoutNode parent, BoundLayoutNode child)
    {
        if (child.Kind == LayoutNodeKind.Anchor)
        {
            return NormalizedLayoutOriginRelation.AnchorDerived;
        }

        return parent.Kind == LayoutNodeKind.Overlay
            ? NormalizedLayoutOriginRelation.OverlayDerived
            : NormalizedLayoutOriginRelation.FlowDerived;
    }

    private static string ApplyRootOrigin(string css, string rootClass, BoundLayoutOrigin origin)
    {
        string rulePrefix = "." + rootClass + " {\n";
        int ruleStart = css.IndexOf(rulePrefix, StringComparison.Ordinal);
        if (ruleStart < 0)
        {
            throw new MachinaLayoutException("COPE-LAYOUT-PROJECTION-0001", "The Machina React projection did not produce a root frame rule.");
        }

        int declarationStart = ruleStart + rulePrefix.Length;
        int ruleEnd = css.IndexOf("}\n", declarationStart, StringComparison.Ordinal);
        if (ruleEnd < 0)
        {
            throw new MachinaLayoutException("COPE-LAYOUT-PROJECTION-0001", "The Machina React projection produced an unterminated root frame rule.");
        }

        string declarations = css[declarationStart..ruleEnd];
        declarations = System.Text.RegularExpressions.Regex.Replace(
            declarations,
            "(?m)^  (position|left|top): .*;\\r?\\n",
            string.Empty);
        string replacement = rulePrefix
            + "  position: absolute;\n"
            + "  left: " + CssCoordinate(origin.X) + ";\n"
            + "  top: " + CssCoordinate(origin.Y) + ";\n"
            + declarations
            + "}\n";
        return css[..ruleStart] + replacement + css[(ruleEnd + 2)..];
    }

    public static string CssCoordinate(BoundLayoutCoordinate coordinate)
    {
        string value = coordinate.Value.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture);
        return coordinate.Unit == LayoutCoordinateUnit.Px
            ? value + "px"
            : "calc(var(--machina-ui, 1px) * " + value + ")";
    }

    public static BoundLayoutNode ProjectionRoot(BoundLayoutNode layoutRoot)
    {
        if (layoutRoot.Children.Count == 0) return layoutRoot;
        if (layoutRoot.Children.Count == 1)
        {
            BoundLayoutNode child = layoutRoot.Children[0];
            var dimensions = layoutRoot.Dimensions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach ((string key, BoundLayoutDimension value) in child.Dimensions) dimensions[key] = value;
            return child with { Dimensions = dimensions };
        }
        return layoutRoot with { Kind = LayoutNodeKind.Column };
    }

    private static MachinaView LowerNode(BoundLayoutNode node, LayoutNodeKind? parentKind, bool topLevel, string identity, Dictionary<string, string> paths)
    {
        paths.Add(node.Name, identity);
        MachinaFrameIntent? frame = topLevel
            ? RootFrame(node)
            : parentKind is LayoutNodeKind.Anchor or LayoutNodeKind.Overlay
                ? node.Kind == LayoutNodeKind.Anchor ? AnchorFrame(node) : AbsoluteFrame(node)
                : null;
        // A row realizes through HStack: width is its horizontal main axis and
        // height is its vertical cross axis. A column realizes through VStack
        // with the opposite relationship. Keep source property names in their
        // ordinary geometric meaning rather than leaking the underlying track
        // vocabulary into stream authoring.
        bool horizontalParent = parentKind is LayoutNodeKind.Row or LayoutNodeKind.Grid;
        MachinaTrack? main = parentKind is null or LayoutNodeKind.Anchor or LayoutNodeKind.Overlay ? null : Track(node, horizontalParent ? "width" : "height");
        MachinaTrack? cross = parentKind is null or LayoutNodeKind.Anchor or LayoutNodeKind.Overlay ? null : Track(node, horizontalParent ? "height" : "width");
        IReadOnlyList<MachinaView> children = node.Children.Select((child, index) => LowerNode(child, node.Kind, false, identity + "/" + index, paths)).ToArray();
        return node.Kind switch
        {
            LayoutNodeKind.Row => Machina.HStack(children, frame, node.Gap, node.Padding, node.Style, mainTrack: main, crossTrack: cross, source: node.Source),
            LayoutNodeKind.Column => Machina.VStack(children, frame, node.Gap, node.Padding, node.Style, mainTrack: main, crossTrack: cross, source: node.Source),
            // M0 grid tracks are a finite horizontal layout realization. Named
            // children remain topology, while the track count stays a distinct
            // contract property; variable collection reconciliation is deferred.
            LayoutNodeKind.Grid => Machina.HStack(children, frame, node.Gap, node.Padding, node.Style, mainTrack: main, crossTrack: cross, source: node.Source),
            _ => new MachinaView(MachinaViewKind.Container, children, frame, MainTrack: main, CrossTrack: cross, Style: node.Style, Source: node.Source),
        };
    }

    private static MachinaFrameIntent RootFrame(BoundLayoutNode node)
        => Machina.Absolute(MachinaLength.Pixels(0), MachinaLength.Pixels(0), Fixed(node, "width"), Fixed(node, "height"));

    private static MachinaFrameIntent AbsoluteFrame(BoundLayoutNode node)
        => Machina.Absolute(Position(node, "x"), Position(node, "y"), Fixed(node, "width"), Fixed(node, "height"));

    private static MachinaFrameIntent AnchorFrame(BoundLayoutNode node)
        => Machina.Anchor(
            left: OptionalPosition(node, "left"),
            right: OptionalPosition(node, "right"),
            top: OptionalPosition(node, "top"),
            bottom: OptionalPosition(node, "bottom"),
            width: OptionalFixed(node, "width"),
            height: OptionalFixed(node, "height"));

    private static MachinaLength Fixed(BoundLayoutNode node, string name)
        => node.Dimensions.TryGetValue(name, out BoundLayoutDimension? value) && value.Kind == LayoutDimensionKind.Fixed
            ? value.Length!.Value
            : throw new MachinaLayoutException("COPE-LAYOUT-FRAME-0001", $"'{node.Name}' requires a fixed {name} outside row/column composition.");

    private static MachinaLength Position(BoundLayoutNode node, string name)
        => node.Positions.TryGetValue(name, out MachinaLength value) ? value : MachinaLength.Pixels(0);

    private static MachinaLength? OptionalPosition(BoundLayoutNode node, string name)
        => node.Positions.TryGetValue(name, out MachinaLength value) ? value : null;

    private static MachinaLength? OptionalFixed(BoundLayoutNode node, string name)
        => node.Dimensions.TryGetValue(name, out BoundLayoutDimension? value) && value.Kind == LayoutDimensionKind.Fixed ? value.Length : null;

    private static MachinaTrack Track(BoundLayoutNode node, string dimension)
    {
        BoundLayoutDimension value = node.Dimensions.GetValueOrDefault(dimension) ?? new BoundLayoutDimension(LayoutDimensionKind.Fill);
        return value.Kind switch
        {
            LayoutDimensionKind.Fixed => Machina.Fixed(value.Length!.Value),
            LayoutDimensionKind.Fill => Machina.Fill(),
            LayoutDimensionKind.Fit => Machina.Content(),
            _ => throw new InvalidOperationException(),
        };
    }

    private static string ToNamespace(string name)
        => new(name.Where(character => char.IsAsciiLetterOrDigit(character) || character == '-').ToArray());

    private static string EscapeJavaScript(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed class Binder
    {
        private readonly Dictionary<string, LayoutDeclarationSyntax> _syntax = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LayoutTypeDeclarationSyntax> _typeSyntax = new(StringComparer.Ordinal);
        private readonly Dictionary<string, StreamDeclarationSyntax> _streamSyntax = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LayerSetDeclarationSyntax> _layerSyntax = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BoundLayoutDeclaration> _layouts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BoundLayoutTypeDeclaration> _layoutTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BoundLayerSet> _layerSets = new(StringComparer.Ordinal);
        private readonly HashSet<string> _binding = new(StringComparer.Ordinal);
        private readonly List<Diagnostic> _diagnostics = [];
        private readonly string _sourcePath;
        private int _relativeDerivationOrder;
        private readonly Func<LayoutDeclarationSyntax, BoundLayoutDeclaration?>? _constructLayout;

        private readonly IReadOnlyDictionary<string, BoundLayoutDeclaration> _importedLayouts;
        private readonly IReadOnlyDictionary<string, BoundLayoutTypeDeclaration> _importedLayoutTypes;
        private readonly IReadOnlyDictionary<string, BoundLayerSet> _importedLayerSets;

        public Binder(
            SyntaxTree? tree,
            string sourcePath,
            IReadOnlyDictionary<string, BoundLayoutDeclaration> importedLayouts,
            IReadOnlyDictionary<string, BoundLayoutTypeDeclaration> importedLayoutTypes,
            IReadOnlyDictionary<string, BoundLayerSet> importedLayerSets,
            IReadOnlyList<MemberSyntax>? declarations = null,
            Func<LayoutDeclarationSyntax, BoundLayoutDeclaration?>? constructLayout = null)
        {
            _sourcePath = sourcePath;
            _constructLayout = constructLayout;
            _importedLayouts = importedLayouts;
            _importedLayoutTypes = importedLayoutTypes;
            _importedLayerSets = importedLayerSets;
            IEnumerable<MemberSyntax> members = declarations ?? tree?.Root.Members ?? [];
            foreach (LayerSetDeclarationSyntax layerSet in members.OfType<LayerSetDeclarationSyntax>())
            {
                if (!_layerSyntax.TryAdd(layerSet.Identifier.Text, layerSet))
                {
                    Report("COPE-LAYOUT-LAYER-0003", $"Semantic layer set '{layerSet.Identifier.Text}' is already declared.", layerSet.Identifier);
                }
            }
            foreach (LayoutDeclarationSyntax layout in members.OfType<LayoutDeclarationSyntax>())
            {
                if (!_syntax.TryAdd(layout.Identifier.Text, layout))
                {
                    Report("COPE-LAYOUT-DECLARATION-0001", $"Layout '{layout.Identifier.Text}' is already declared.", layout.Identifier);
                }
            }
            foreach (LayoutTypeDeclarationSyntax layoutType in members.OfType<LayoutTypeDeclarationSyntax>())
            {
                if (!_typeSyntax.TryAdd(layoutType.Identifier.Text, layoutType))
                {
                    Report("COPE-LAYOUT-TYPE-0003", $"Layout type '{layoutType.Identifier.Text}' is already declared.", layoutType.Identifier);
                }
            }
            foreach (StreamDeclarationSyntax stream in members.OfType<StreamDeclarationSyntax>())
            {
                if (!_streamSyntax.TryAdd(stream.Identifier.Text, stream))
                {
                    Report("COPE-STREAM-0003", $"Stream '{stream.Identifier.Text}' is already declared.", stream.Identifier);
                }
            }
        }

        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
        public IReadOnlyDictionary<string, BoundLayoutTypeDeclaration> LayoutTypes => _layoutTypes;
        public IReadOnlyDictionary<string, BoundLayerSet> LayerSets => _layerSets;

        public IReadOnlyDictionary<string, BoundLayoutDeclaration> Bind()
        {
            BindLayerSets();
            foreach (LayoutTypeDeclarationSyntax declaration in _typeSyntax.Values.OrderBy(item => item.Identifier.Position))
            {
                BindLayoutType(declaration);
            }
            foreach (LayoutDeclarationSyntax declaration in _syntax.Values.OrderBy(item => item.Identifier.Position)) _ = BindDeclaration(declaration);
            foreach (StreamDeclarationSyntax declaration in _streamSyntax.Values.OrderBy(item => item.Identifier.Position)) _ = BindStream(declaration);
            return _layouts;
        }

        private void BindLayerSets()
        {
            foreach (LayerSetDeclarationSyntax declaration in _layerSyntax.Values.OrderBy(item => item.Identifier.Position))
            {
                if (declaration.Layers.Count == 0)
                {
                    Report("COPE-LAYOUT-LAYER-0004", $"Semantic layer set '{declaration.Identifier.Text}' must declare at least one layer.", declaration.Identifier);
                    continue;
                }

                var names = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (SyntaxToken layer in declaration.Layers)
                {
                    if (!seen.Add(layer.Text))
                    {
                        Report("COPE-LAYOUT-LAYER-0005", $"Semantic layer '{layer.Text}' is duplicated in layer set '{declaration.Identifier.Text}'.", layer);
                        continue;
                    }
                    names.Add(layer.Text);
                }
                if (names.Count > 0)
                {
                    _layerSets.Add(declaration.Identifier.Text, new BoundLayerSet(declaration.Identifier.Text, _sourcePath + "::layers::" + declaration.Identifier.Text, names));
                }
            }
        }

        private BoundLayoutDeclaration? BindStream(StreamDeclarationSyntax declaration)
        {
            string name = declaration.Identifier.Text;
            if (_layouts.ContainsKey(name))
            {
                Report("COPE-STREAM-0003", $"Stream '{name}' conflicts with an existing layout declaration.", declaration.Identifier);
                return null;
            }

            BoundLayoutOrigin? origin = BindOrigin(declaration.Origin);
            if (origin is null) return null;

            ValidateProperties(declaration.Properties, null);
            IReadOnlyDictionary<string, BoundLayoutDimension> rootDimensions = BindDimensions(declaration.Properties);
            IReadOnlyDictionary<string, MachinaLength> rootPositions = BindPositions(declaration.Properties);
            MachinaInsets rootPadding = Padding(declaration.Properties);
            MachinaStyle rootStyle = Style(declaration.Properties);
            BoundBoxOverflowPolicy? rootOverflow = Overflow(declaration.Properties);
            BoundLayerSet layerSet = ResolveLayerSet(declaration.Properties, declaration.Identifier, name);
            var slots = new Dictionary<string, BoundLayoutNode>(StringComparer.Ordinal);
            BoundLayoutNode[] children = BindStreamChildren(declaration.Nodes, declaration.Tables, slots, LayoutNodeKind.Column, layerSet, name, layerSet.Layers[0]);
            bool hasExplicitRoot = declaration.Nodes.Count + declaration.Tables.Count == 1
                && (declaration.Nodes.SingleOrDefault() is { KindToken: not null, Identifier.Text: "root" }
                    || declaration.Tables.SingleOrDefault() is { ContainerKindToken.Text: "overlay", Identifier.Text: "root" });
            if (hasExplicitRoot && children.Length == 1)
            {
                children[0] = children[0] with
                {
                    Dimensions = rootDimensions,
                    Positions = rootPositions,
                    Padding = rootPadding,
                    Style = rootStyle,
                    Overflow = rootOverflow,
                };
            }
            BoundLayoutNode[] rootChildren;
            if (hasExplicitRoot)
            {
                rootChildren = children;
            }
            else
            {
                var implicitRoot = new BoundLayoutNode(
                    "root",
                    LayoutNodeKind.Column,
                    rootDimensions,
                    rootPositions,
                    Gap(declaration.Properties),
                    null,
                    rootPadding,
                    rootStyle,
                    children,
                    Span(declaration),
                    BoundPaintProperties.Default,
                    Overflow: rootOverflow);
                rootChildren = [implicitRoot];
            }
            var root = new BoundLayoutNode(
                name,
                LayoutNodeKind.Overlay,
                rootDimensions,
                rootPositions,
                MachinaLength.Pixels(0),
                null,
                MachinaInsets.None,
                MachinaStyle.Empty,
                rootChildren,
                Span(declaration),
                BoundPaintProperties.Default,
                Overflow: rootOverflow);
            var layout = new BoundLayoutDeclaration(name, null, origin, root, slots, LayerSet: layerSet);
            layout = ApplyRelativeDerivations(layout);

            string inferredContractName = name + "Shape";
            BoundLayoutTypeNode inferredRoot = InferTypeNode(root);
            _layoutTypes[inferredContractName] = new BoundLayoutTypeDeclaration(inferredContractName, inferredRoot);
            if (declaration.ContractIdentifier is not null)
            {
                layout = CheckSatisfaction(name, declaration.ContractIdentifier, layout);
            }
            else
            {
                layout = layout with { Satisfaction = new BoundLayoutSatisfaction(inferredContractName, true, InferShape(root)) };
            }

            _layouts.Add(name, layout);
            return layout;
        }

        private BoundLayoutNode? BindStreamNode(StreamNodeSyntax syntax, Dictionary<string, BoundLayoutNode> slots, LayoutNodeKind parentKind, BoundLayerSet layerSet, string layoutName, string inheritedLayer)
        {
            if (syntax.KindToken is null)
            {
                if (syntax.Content is null)
                {
                    Report("COPE-STREAM-0004", $"Stream region '{syntax.Identifier.Text}' requires renderable content.", syntax.Identifier);
                }
                if (syntax.Children.Count > 0)
                {
                    Report("COPE-STREAM-0005", $"Singular stream region '{syntax.Identifier.Text}' cannot contain child regions.", syntax.Identifier);
                }
                ValidateProperties(syntax.Properties, LayoutNodeKind.Slot);
                var slot = new BoundLayoutNode(syntax.Identifier.Text, LayoutNodeKind.Slot, BindDimensions(syntax.Properties), BindPositions(syntax.Properties), MachinaLength.Pixels(0), null, Padding(syntax.Properties), Style(syntax.Properties), [], Span(syntax), BindPaint(syntax.Properties, layerSet, layoutName, inheritedLayer), BindRelativeDerivations(syntax.Identifier.Text, syntax.RelativeDerivations), Overflow(syntax.Properties), TextFit(syntax.Properties, LayoutNodeKind.Slot));
                if (!slots.TryAdd(slot.Name, slot))
                {
                    Report("COPE-STREAM-0006", $"Stream declares region '{slot.Name}' more than once.", syntax.Identifier);
                }
                return slot;
            }

            if (!Enum.TryParse(syntax.KindToken.Text, true, out LayoutNodeKind kind) || kind == LayoutNodeKind.Slot)
            {
                Report("COPE-STREAM-0007", $"'{syntax.KindToken.Text}' is not a supported stream structural node kind.", syntax.KindToken);
                return null;
            }
            if (syntax.Content is not null && syntax.Content is not ArrayLiteralExpressionSyntax)
            {
                Report("COPE-STREAM-0008", $"Structural stream region '{syntax.Identifier.Text}' cannot bind content directly in M0; add a named singular child region.", syntax.Identifier);
            }
            if (syntax.Content is ArrayLiteralExpressionSyntax && kind != LayoutNodeKind.Grid)
            {
                Report("COPE-STREAM-COLLECTION-0001", $"Fixed collection content is supported only by a grid region; '{syntax.Identifier.Text}' is {kind.ToString().ToLowerInvariant()}.", syntax.Identifier);
            }
            ValidateProperties(syntax.Properties, kind);
            if (kind == LayoutNodeKind.Grid && Columns(syntax.Properties) is null)
            {
                Report("COPE-LAYOUT-GRID-0001", "A grid requires a positive integer 'columns' property.", syntax.Identifier);
            }
            BoundPaintProperties paint = BindPaint(syntax.Properties, layerSet, layoutName, inheritedLayer);
            BoundLayoutNode[] children = BindStreamChildren(syntax.Children, syntax.Tables, slots, kind, layerSet, layoutName, paint.Layer);
            var node = new BoundLayoutNode(syntax.Identifier.Text, kind, BindDimensions(syntax.Properties), BindPositions(syntax.Properties), Gap(syntax.Properties), Columns(syntax.Properties), Padding(syntax.Properties), Style(syntax.Properties), children, Span(syntax), paint, BindRelativeDerivations(syntax.Identifier.Text, syntax.RelativeDerivations), Overflow(syntax.Properties), TextFit(syntax.Properties, kind));
            if (parentKind is LayoutNodeKind.Anchor or LayoutNodeKind.Overlay && kind != LayoutNodeKind.Anchor && (!HasFrameWriter(node, "width") || !HasFrameWriter(node, "height")))
            {
                Report("COPE-LAYOUT-FRAME-0002", $"'{node.Name}' requires fixed width and height inside {parentKind.ToString().ToLowerInvariant()} composition.", syntax.Identifier);
            }
            return node;
        }

        private BoundLayoutNode[] BindStreamChildren(
            IReadOnlyList<StreamNodeSyntax> nodes,
            IReadOnlyList<StreamTableSyntax> tables,
            Dictionary<string, BoundLayoutNode> slots,
            LayoutNodeKind parentKind,
            BoundLayerSet layerSet,
            string layoutName,
            string inheritedLayer)
        {
            return nodes.Cast<SyntaxNode>()
                .Concat(tables)
                .OrderBy(node => FirstToken(node).Position)
                .Select(node => node switch
                {
                    StreamNodeSyntax child => BindStreamNode(child, slots, parentKind, layerSet, layoutName, inheritedLayer),
                    StreamTableSyntax table => BindStreamTable(table, slots, parentKind, layerSet, layoutName, inheritedLayer),
                    _ => null,
                })
                .Where(node => node is not null)
                .Cast<BoundLayoutNode>()
                .ToArray();
        }

        private BoundLayoutNode? BindStreamTable(
            StreamTableSyntax table,
            Dictionary<string, BoundLayoutNode> slots,
            LayoutNodeKind parentKind,
            BoundLayerSet layerSet,
            string layoutName,
            string inheritedLayer)
        {
            if (!string.Equals(table.ContainerKindToken.Text, "overlay", StringComparison.Ordinal))
            {
                Report("COPE-LAYOUT-TABLE-0013", $"CSV-shaped table '{table.Identifier.Text}' uses unsupported container '{table.ContainerKindToken.Text}'. M0 supports only 'overlay'.", table.ContainerKindToken);
                return null;
            }

            string[] required = ["name", "content", "width", "height"];
            string[] supported = ["name", "content", "x", "y", "width", "height", "layer", "z", "derivations"];
            var headers = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < table.Headers.Count; index++)
            {
                SyntaxToken header = table.Headers[index];
                if (!supported.Contains(header.Text, StringComparer.Ordinal))
                {
                    Report("COPE-LAYOUT-TABLE-0003", $"Unknown column '{header.Text}' in CSV layout table '{table.Identifier.Text}'.", header);
                    continue;
                }
                if (!headers.TryAdd(header.Text, index))
                {
                    Report("COPE-LAYOUT-TABLE-0002", $"Column '{header.Text}' is declared more than once in CSV layout table '{table.Identifier.Text}'.", header);
                }
            }
            bool hasDerivationsColumn = headers.ContainsKey("derivations");
            foreach (string column in hasDerivationsColumn ? required : required.Concat(["x", "y"]))
            {
                if (!headers.ContainsKey(column))
                {
                    Report("COPE-LAYOUT-TABLE-0004", $"CSV layout table '{table.Identifier.Text}' is missing required column '{column}'.", table.Identifier);
                }
            }

            var children = new List<BoundLayoutNode>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                StreamTableRowSyntax row = table.Rows[rowIndex];
                if (row.Cells.Count < table.Headers.Count)
                {
                    Report("COPE-LAYOUT-TABLE-0005", $"Row {rowIndex + 1} in CSV layout table '{table.Identifier.Text}' has too few cells. Expected {table.Headers.Count}; received {row.Cells.Count}.", FirstTableRowToken(row));
                    continue;
                }
                if (row.Cells.Count > table.Headers.Count)
                {
                    Report("COPE-LAYOUT-TABLE-0006", $"Row {rowIndex + 1} in CSV layout table '{table.Identifier.Text}' has too many cells. Expected {table.Headers.Count}; received {row.Cells.Count}.", FirstTableRowToken(row));
                    continue;
                }
                if (!headers.TryGetValue("name", out int nameIndex) || row.Cells[nameIndex] is not NameExpressionSyntax nameExpression)
                {
                    Report("COPE-LAYOUT-TABLE-0008", $"Row {rowIndex + 1}, column 'name' in CSV layout table '{table.Identifier.Text}' must be a semantic identifier.", headers.TryGetValue("name", out int index) ? FirstToken(row.Cells[index]) : FirstTableRowToken(row));
                    continue;
                }

                SyntaxToken nameToken = nameExpression.IdentifierToken;
                string rowName = nameToken.Text;
                if (!names.Add(rowName) || slots.ContainsKey(rowName))
                {
                    Report("COPE-LAYOUT-TABLE-0007", $"Row '{rowName}' is duplicated in CSV layout table '{table.Identifier.Text}'.", nameToken);
                    continue;
                }

                IReadOnlyList<LayoutPropertySyntax> properties = TableProperties(table, row, headers);
                if (headers.TryGetValue("x", out int xIndex) && Length(row.Cells[xIndex]) is null)
                {
                    Report("COPE-LAYOUT-TABLE-0009", $"Invalid value in row '{rowName}', column 'x'. Expected a px or ui coordinate.", FirstToken(row.Cells[xIndex]));
                }
                if (headers.TryGetValue("y", out int yIndex) && Length(row.Cells[yIndex]) is null)
                {
                    Report("COPE-LAYOUT-TABLE-0009", $"Invalid value in row '{rowName}', column 'y'. Expected a px or ui coordinate.", FirstToken(row.Cells[yIndex]));
                }
                foreach (string dimension in new[] { "width", "height" })
                {
                    if (headers.TryGetValue(dimension, out int dimensionIndex) && !IsTableDimension(row.Cells[dimensionIndex]))
                    {
                        Report("COPE-LAYOUT-TABLE-0010", $"Invalid value in row '{rowName}', column '{dimension}'. Expected a length, 'fill', or 'fit'.", FirstToken(row.Cells[dimensionIndex]));
                    }
                }
                if (headers.TryGetValue("layer", out int layerIndex))
                {
                    if (row.Cells[layerIndex] is not NameExpressionSyntax layerExpression)
                    {
                        Report("COPE-LAYOUT-TABLE-0011", $"Invalid value in row '{rowName}', column 'layer'. Expected a declared semantic layer.", FirstToken(row.Cells[layerIndex]));
                    }
                    else if (layerSet.RankOf(layerExpression.IdentifierToken.Text) < 0)
                    {
                        Report("COPE-LAYOUT-TABLE-0011", $"Unknown layer '{layerExpression.IdentifierToken.Text}' in row '{rowName}', column 'layer'.", layerExpression.IdentifierToken);
                    }
                }
                if (headers.TryGetValue("z", out int zIndex) && (!TryStaticInteger(row.Cells[zIndex], out int z) || z is < -5 or > 5))
                {
                    Report("COPE-LAYOUT-TABLE-0012", $"Invalid value in row '{rowName}', column 'z'. Expected an integral z value from -5 through 5.", FirstToken(row.Cells[zIndex]));
                }

                IReadOnlyList<BoundRelativeDerivationSpec> derivations = headers.TryGetValue("derivations", out int derivationsIndex)
                    ? BindCsvRelativeDerivations(rowName, row.Cells[derivationsIndex])
                    : [];
                BoundLayoutNode slot = new(
                    rowName,
                    LayoutNodeKind.Slot,
                    BindDimensions(properties),
                    BindPositions(properties),
                    MachinaLength.Pixels(0),
                    null,
                    MachinaInsets.None,
                    MachinaStyle.Empty,
                    [],
                    Span(nameToken),
                    BindPaint(properties, layerSet, layoutName, inheritedLayer),
                    derivations,
                    Overflow(properties),
                    TextFit(properties, LayoutNodeKind.Slot));
                slots[rowName] = slot;
                children.Add(slot);
            }

            var node = new BoundLayoutNode(
                table.Identifier.Text,
                LayoutNodeKind.Overlay,
                new Dictionary<string, BoundLayoutDimension>(StringComparer.Ordinal),
                new Dictionary<string, MachinaLength>(StringComparer.Ordinal),
                MachinaLength.Pixels(0),
                null,
                MachinaInsets.None,
                MachinaStyle.Empty,
                children,
                Span(table),
                new BoundPaintProperties(inheritedLayer, 0));
            if (parentKind is LayoutNodeKind.Anchor or LayoutNodeKind.Overlay && (!HasFixedDimension(node, "width") || !HasFixedDimension(node, "height")))
            {
                // The table itself is a structural container. Its parent owns its
                // frame under the same rules as a nested overlay node.
                Report("COPE-LAYOUT-FRAME-0002", $"'{node.Name}' requires fixed width and height inside {parentKind.ToString().ToLowerInvariant()} composition.", table.Identifier);
            }
            return node;
        }

        private static IReadOnlyList<LayoutPropertySyntax> TableProperties(StreamTableSyntax table, StreamTableRowSyntax row, IReadOnlyDictionary<string, int> headers)
        {
            var properties = new List<LayoutPropertySyntax>();
            foreach (string column in new[] { "x", "y", "width", "height", "layer", "z" })
            {
                if (!headers.TryGetValue(column, out int index)) continue;
                if (column is "width" or "height" && row.Cells[index] is NameExpressionSyntax { IdentifierToken.Text: "derived" }) continue;
                SyntaxToken cellToken = FirstToken(row.Cells[index]);
                SyntaxToken identifier = new(SyntaxKind.IdentifierToken, cellToken.Position, column, null);
                properties.Add(new LayoutPropertySyntax(identifier, table.CsvKeyword, row.Cells[index], row.SemicolonToken));
            }
            return properties;
        }

        private bool IsTableDimension(ExpressionSyntax expression)
            => expression is NameExpressionSyntax { IdentifierToken.Text: "fill" or "fit" or "derived" } || Length(expression) is not null;

        private static SyntaxToken FirstTableRowToken(StreamTableRowSyntax row)
            => row.Cells.Count > 0 ? FirstToken(row.Cells[0]) : row.SemicolonToken;

        private static BoundLayoutTypeNode InferTypeNode(BoundLayoutNode node)
            => new(node.Name, node.Kind, node.Columns, node.Children.Select(InferTypeNode).ToArray(), node.Source);

        private void BindLayoutType(LayoutTypeDeclarationSyntax declaration)
        {
            var childNames = new HashSet<string>(StringComparer.Ordinal);
            BoundLayoutTypeNode[] children = declaration.Nodes
                .Select(node => BindLayoutTypeNode(node, childNames))
                .Where(node => node is not null)
                .Cast<BoundLayoutTypeNode>()
                .ToArray();
            if (children.Length != 1)
            {
                Report("COPE-LAYOUT-TYPE-0004", $"Layout type '{declaration.Identifier.Text}' requires exactly one named root node.", declaration.Identifier);
            }
            BoundLayoutTypeNode root = new(
                declaration.Identifier.Text,
                LayoutNodeKind.Overlay,
                null,
                children,
                Span(declaration));
            _layoutTypes[declaration.Identifier.Text] = new BoundLayoutTypeDeclaration(declaration.Identifier.Text, root);
        }

        private BoundLayoutTypeNode? BindLayoutTypeNode(LayoutNodeSyntax syntax, HashSet<string> siblingNames)
        {
            if (!Enum.TryParse(syntax.KindToken.Text, true, out LayoutNodeKind kind))
            {
                Report("COPE-LAYOUT-TYPE-0005", $"'{syntax.KindToken.Text}' is not a supported layout type node kind.", syntax.KindToken);
                return null;
            }
            if (!siblingNames.Add(syntax.Identifier.Text))
            {
                Report("COPE-LAYOUT-TYPE-0006", $"Layout type child '{syntax.Identifier.Text}' is duplicated.", syntax.Identifier);
            }
            foreach (LayoutPropertySyntax property in syntax.Properties)
            {
                if (kind != LayoutNodeKind.Grid || !string.Equals(property.Identifier.Text, "columns", StringComparison.Ordinal))
                {
                    Report("COPE-LAYOUT-TYPE-0007", "Layout types constrain topology only; only 'columns' on a grid is supported in M0.", property.Identifier);
                }
            }
            if (kind == LayoutNodeKind.Slot && syntax.Children.Count > 0)
            {
                Report("COPE-LAYOUT-TYPE-0008", "A layout type slot cannot contain child nodes.", syntax.KindToken);
            }
            var childNames = new HashSet<string>(StringComparer.Ordinal);
            BoundLayoutTypeNode[] children = syntax.Children
                .Select(child => BindLayoutTypeNode(child, childNames))
                .Where(child => child is not null)
                .Cast<BoundLayoutTypeNode>()
                .ToArray();
            return new BoundLayoutTypeNode(syntax.Identifier.Text, kind, Columns(syntax.Properties), children, Span(syntax));
        }

        private BoundLayoutDeclaration? BindDeclaration(LayoutDeclarationSyntax declaration)
        {
            string name = declaration.Identifier.Text;
            if (_layouts.TryGetValue(name, out BoundLayoutDeclaration? cached)) return cached;
            if (!_binding.Add(name))
            {
                Report("COPE-LAYOUT-COMPOSE-0001", $"Layout composition is recursive at '{name}'.", declaration.Identifier);
                return null;
            }
            BoundLayoutDeclaration? result = null;
            try
            {
                if (declaration.Initializer is not null || declaration.ComposedLayout is not null)
                {
                    BoundLayoutDeclaration? basis = null;
                    if (declaration.Initializer is not null)
                    {
                        basis = _constructLayout?.Invoke(declaration);
                        if (_constructLayout is null)
                        {
                            Report("COPE-LAYOUT-TEMPLATE-0001", "Layout templates require the ordinary Copeland project compiler.", declaration.Identifier);
                        }
                    }
                    else if (_syntax.TryGetValue(declaration.ComposedLayout!.Text, out LayoutDeclarationSyntax? local))
                    {
                        basis = BindDeclaration(local);
                    }
                    else if (!_importedLayouts.TryGetValue(declaration.ComposedLayout!.Text, out basis))
                    {
                        Report("COPE-LAYOUT-COMPOSE-0002", $"Composed layout '{declaration.ComposedLayout.Text}' was not found.", declaration.ComposedLayout);
                    }
                    if (basis is null) return null;
                    BoundLayoutOrigin? origin = declaration.Origin is null ? basis.Origin : BindOrigin(declaration);
                    if (origin is null) return null;
                    LayoutPropertyData[] replacements = ReadProperties(declaration.CompositionProperties);
                    if (Has(replacements, "layers"))
                    {
                        BoundLayerSet layers = ResolveLayerSet(replacements, declaration.Identifier, name);
                        if (layers.StableIdentity != basis.ResolvedLayerSet.StableIdentity)
                        {
                            Report("COPE-LAYOUT-LAYER-0006", $"Composed layout '{name}' cannot replace layer set '{basis.ResolvedLayerSet.Name}'.", declaration.Identifier);
                        }
                    }
                    BoundLayoutNode root = ApplyComposition(basis.Root, replacements, basis.ResolvedLayerSet, name);
                    root = root with { Name = name };
                    result = basis with { Name = name, Origin = origin, Root = root, Slots = CollectSlots(root) };
                    ValidateClosedNodes(root, null);
                    if (basis.Satisfaction is not null)
                    {
                        var contract = declaration.Identifier with { Text = basis.Satisfaction.ContractName };
                        result = basis.Satisfaction.BoundContract is { } boundContract
                            ? CheckSatisfaction(result, boundContract)
                            : CheckSatisfaction(name, contract, result);
                    }
                }
                else
                {
                    BoundLayoutOrigin? origin = BindOrigin(declaration);
                    if (origin is null) return null;
                    var construction = new LayoutConstruction(declaration.Identifier, declaration.Profile?.Text,
                        new LayoutValue(declaration.Identifier, origin.X), new LayoutValue(declaration.Identifier, origin.Y),
                        ReadProperties(declaration.Properties), declaration.Nodes.Select(ReadNode).ToArray(), declaration.ContractIdentifier);
                    result = BindConstruction(construction);
                }
                if (result is not null)
                {
                    result = ApplyRelativeDerivations(result);
                    if (declaration.ContractIdentifier is not null)
                    {
                        result = CheckSatisfaction(declaration, result);
                    }
                    _layouts.Add(name, result);
                }
                return result;
            }
            finally
            {
                _binding.Remove(name);
            }
        }

        public BoundLayoutDeclaration? BindConstruction(LayoutConstruction construction)
        {
            if (construction.Profile is not null && construction.Profile != "page")
            {
                Report("COPE-LAYOUT-PROFILE-0001", $"Layout profile '{construction.Profile}' is not supported.", construction.Identifier);
            }
            BoundLayoutCoordinate? x = ClosedCoordinate(construction.X);
            BoundLayoutCoordinate? y = ClosedCoordinate(construction.Y);
            if (x is null || y is null) return null;
            string name = construction.Identifier.Text;
            BoundLayerSet layers = ResolveLayerSet(construction.Properties, construction.Identifier, name);
            ValidateProperties(construction.Properties, null);
            var slots = new Dictionary<string, BoundLayoutNode>(StringComparer.Ordinal);
            BoundLayoutNode[] children = construction.Nodes
                .Select(node => BindNode(node, slots, null, layers, name, layers.Layers[0]))
                .Where(node => node is not null).Cast<BoundLayoutNode>().ToArray();
            var root = new BoundLayoutNode(name, LayoutNodeKind.Overlay, BindDimensions(construction.Properties),
                BindPositions(construction.Properties), Gap(construction.Properties), null, Padding(construction.Properties),
                Style(construction.Properties), children, Span(construction.Identifier), new BoundPaintProperties(layers.Layers[0], 0),
                Overflow: Overflow(construction.Properties));
            if (children.Length != 1) slots[name] = root;
            return new BoundLayoutDeclaration(name, construction.Profile, new BoundLayoutOrigin(x, y), root, slots, LayerSet: layers);
        }

        private BoundLayoutCoordinate? ClosedCoordinate(LayoutValue value)
        {
            if (value.Scalar is BoundLayoutCoordinate coordinate) return coordinate;
            if (value.Scalar is MachinaLength length)
            {
                LayoutCoordinateUnit unit = length.LiteralUnit == MachinaLengthLiteralUnit.Ui ? LayoutCoordinateUnit.Ui : LayoutCoordinateUnit.Px;
                return new BoundLayoutCoordinate(unit == LayoutCoordinateUnit.Px ? length.Px : length.Ui, unit);
            }
            Report("COPE-LAYOUT-ORIGIN-0002", "A layout origin requires a static px or ui coordinate.", value.Anchor);
            return null;
        }

        private void ValidateClosedNodes(BoundLayoutNode node, LayoutNodeKind? parent, bool wrapper = true)
        {
            if (node.Kind == LayoutNodeKind.Anchor) ValidateAnchor(node, new SyntaxToken(SyntaxKind.IdentifierToken, node.Source.Start, node.Name, null));
            if (parent is LayoutNodeKind.Anchor or LayoutNodeKind.Overlay && node.Kind != LayoutNodeKind.Anchor
                && (!HasFrameWriter(node, "width") || !HasFrameWriter(node, "height")))
            {
                Report("COPE-LAYOUT-FRAME-0002", $"'{node.Name}' requires fixed width and height inside {parent.Value.ToString().ToLowerInvariant()} composition.", node.Source);
            }
            foreach (BoundLayoutNode child in node.Children)
            {
                ValidateClosedNodes(child, wrapper ? null : node.Kind, false);
            }
        }

        private BoundLayoutDeclaration CheckSatisfaction(LayoutDeclarationSyntax declaration, BoundLayoutDeclaration layout)
            => CheckSatisfaction(declaration.Identifier.Text, declaration.ContractIdentifier!, layout);

        public BoundLayoutDeclaration CheckSatisfaction(string layoutName, SyntaxToken contractIdentifier, BoundLayoutDeclaration layout)
        {
            string contractName = contractIdentifier.Text;
            if (!_layoutTypes.TryGetValue(contractName, out BoundLayoutTypeDeclaration? contract)
                && !_importedLayoutTypes.TryGetValue(contractName, out contract))
            {
                Report("COPE-LAYOUT-TYPE-0009", $"Layout '{layout.Name}' satisfies '{contractName}', but no layout type with that name is visible.", contractIdentifier);
                return layout with { Satisfaction = new BoundLayoutSatisfaction(contractName, false, InferShape(layout.Root)) };
            }

            return CheckSatisfaction(layout, contract);
        }

        public BoundLayoutDeclaration CheckSatisfaction(BoundLayoutDeclaration layout, BoundLayoutTypeDeclaration contract)
        {
            bool satisfied = CompareNodes(layout.Name, contract.Name, layout.Root, contract.Root, layout.Name);
            return layout with { Satisfaction = new BoundLayoutSatisfaction(contract.Name, satisfied, InferShape(layout.Root), contract) };
        }

        private bool CompareNodes(string layoutName, string contractName, BoundLayoutNode actual, BoundLayoutTypeNode expected, string path)
        {
            bool matches = true;
            if (actual.Kind != expected.Kind && expected.Name != contractName)
            {
                Report("COPE-LAYOUT-TYPE-0010", $"Layout '{layoutName}' does not satisfy '{contractName}' at '{path}': expected {expected.Kind.ToString().ToLowerInvariant()} '{expected.Name}', but found {actual.Kind.ToString().ToLowerInvariant()}.", actual.Source);
                matches = false;
            }
            if (expected.Columns is int expectedColumns && actual.Columns != expectedColumns)
            {
                Report("COPE-LAYOUT-TYPE-0011", $"Layout '{layoutName}' does not satisfy '{contractName}' at '{path}': expected grid columns: {expectedColumns}, but found {(actual.Columns?.ToString() ?? "none")}.", actual.Source);
                matches = false;
            }

            var expectedByName = expected.Children.ToDictionary(child => child.Name, StringComparer.Ordinal);
            var actualByName = actual.Children.GroupBy(child => child.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            foreach (BoundLayoutTypeNode required in expected.Children)
            {
                if (!actualByName.TryGetValue(required.Name, out BoundLayoutNode[]? candidates))
                {
                    Report("COPE-LAYOUT-TYPE-0012", $"Layout '{layoutName}' does not satisfy '{contractName}' at '{path}': missing required {required.Kind.ToString().ToLowerInvariant()} '{required.Name}'.", actual.Source);
                    matches = false;
                    continue;
                }
                if (candidates.Length != 1)
                {
                    Report("COPE-LAYOUT-TYPE-0013", $"Layout '{layoutName}' does not satisfy '{contractName}' at '{path}': child '{required.Name}' is duplicated.", candidates[1].Source);
                    matches = false;
                }
                matches &= CompareNodes(layoutName, contractName, candidates[0], required, path + "." + required.Name);
            }
            foreach (BoundLayoutNode child in actual.Children)
            {
                if (!expectedByName.ContainsKey(child.Name))
                {
                    Report("COPE-LAYOUT-TYPE-0014", $"Layout '{layoutName}' does not satisfy '{contractName}' at '{path}': unexpected {child.Kind.ToString().ToLowerInvariant()} '{child.Name}'.", child.Source);
                    matches = false;
                }
            }
            return matches;
        }

        private static InferredLayoutShape InferShape(BoundLayoutNode node)
            => new(node.Name, node.Kind, node.Columns, node.Children.Select(InferShape).ToArray());

        private BoundLayoutOrigin? BindOrigin(LayoutDeclarationSyntax declaration)
            => BindOrigin(declaration.Origin);

        private BoundLayoutOrigin? BindOrigin(LayoutOriginSyntax? origin)
        {
            if (origin is null)
            {
                return null;
            }

            BoundLayoutCoordinate? x = Coordinate(origin.X);
            BoundLayoutCoordinate? y = Coordinate(origin.Y);
            return x is null || y is null ? null : new BoundLayoutOrigin(x, y);
        }

        private BoundLayoutCoordinate? Coordinate(ExpressionSyntax expression)
        {
            bool negative = false;
            while (expression is UnaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.MinusToken } unary)
            {
                negative = !negative;
                expression = unary.Operand;
            }

            if (expression is LiteralExpressionSyntax { LiteralToken.Value: LengthLiteralTokenValue length })
            {
                LayoutCoordinateUnit unit = length.Unit switch
                {
                    "px" => LayoutCoordinateUnit.Px,
                    "ui" => LayoutCoordinateUnit.Ui,
                    _ => throw new InvalidOperationException("The lexer accepted an unsupported layout coordinate unit."),
                };
                return new BoundLayoutCoordinate(negative ? -length.Value : length.Value, unit);
            }

            Report(
                "COPE-LAYOUT-ORIGIN-0003",
                "A layout origin coordinate must be a signed px or ui literal; runtime expressions, unitless values, and coordinate arithmetic are not permitted.",
                FirstToken(expression));
            return null;
        }

        private BoundLayoutNode? BindNode(LayoutNodeSyntax syntax, Dictionary<string, BoundLayoutNode> slots,
            LayoutNodeKind? parentKind, BoundLayerSet layerSet, string layoutName, string inheritedLayer)
            => BindNode(ReadNode(syntax), slots, parentKind, layerSet, layoutName, inheritedLayer);

        private LayoutNodeData ReadNode(LayoutNodeSyntax syntax)
        {
            if (syntax.Conditional is not null)
            {
                Report("COPE-LAYOUT-TEMPLATE-0001", "Static layout branches belong in a layout template.", syntax.KindToken);
            }
            return new LayoutNodeData(syntax.KindToken, syntax.Identifier, ReadProperties(syntax.Properties),
                syntax.Children.Select(ReadNode).ToArray(), BindRelativeDerivations(syntax.Identifier.Text, syntax.RelativeDerivations));
        }

        private BoundLayoutNode? BindNode(LayoutNodeData syntax, Dictionary<string, BoundLayoutNode> slots, LayoutNodeKind? parentKind, BoundLayerSet layerSet, string layoutName, string inheritedLayer)
        {
            if (!Enum.TryParse(syntax.KindToken.Text, true, out LayoutNodeKind kind))
            {
                Report("COPE-LAYOUT-NODE-0001", $"'{syntax.KindToken.Text}' is not a supported layout node kind.", syntax.KindToken);
                return null;
            }
            string name = syntax.Identifier.Text;
            ValidateProperties(syntax.Properties, kind);
            if (slots.ContainsKey(name)) Report("COPE-LAYOUT-SLOT-0001", $"Layout slot '{name}' is already declared.", syntax.Identifier);
            if (kind == LayoutNodeKind.Slot && syntax.Children.Count > 0) Report("COPE-LAYOUT-NODE-0002", "A slot cannot contain child nodes.", syntax.KindToken);
            if (kind == LayoutNodeKind.Grid && Columns(syntax.Properties) is null) Report("COPE-LAYOUT-GRID-0001", "A grid requires a positive integer 'columns' property.", syntax.Identifier);
            BoundPaintProperties paint = BindPaint(syntax.Properties, layerSet, layoutName, inheritedLayer);
            var children = syntax.Children.Select(child => BindNode(child, slots, kind, layerSet, layoutName, paint.Layer)).Where(child => child is not null).Cast<BoundLayoutNode>().ToArray();
            var node = new BoundLayoutNode(name, kind, BindDimensions(syntax.Properties), BindPositions(syntax.Properties), Gap(syntax.Properties), Columns(syntax.Properties), Padding(syntax.Properties), Style(syntax.Properties), children, Span(syntax.Identifier), paint, syntax.RelativeDerivations, Overflow(syntax.Properties), TextFit(syntax.Properties, kind));
            if (parentKind is LayoutNodeKind.Anchor or LayoutNodeKind.Overlay && kind != LayoutNodeKind.Anchor && (!HasFrameWriter(node, "width") || !HasFrameWriter(node, "height")))
            {
                Report("COPE-LAYOUT-FRAME-0002", $"'{name}' requires fixed width and height inside {parentKind.Value.ToString().ToLowerInvariant()} composition.", syntax.Identifier);
            }
            if (kind == LayoutNodeKind.Anchor) ValidateAnchor(node, syntax.Identifier);
            slots.TryAdd(name, node);
            return node;
        }

        private IReadOnlyList<BoundRelativeDerivationSpec> BindRelativeDerivations(string targetBox, IReadOnlyList<LayoutRelativeDerivationSyntax>? syntax)
        {
            if (syntax is null || syntax.Count == 0) return [];

            var result = new List<BoundRelativeDerivationSpec>();
            foreach (LayoutRelativeDerivationSyntax derivation in syntax)
            {
                BoundRelativeDerivationSpec? bound = BindRelativeDerivation(
                    targetBox,
                    derivation.TransformIdentifier,
                    derivation.SourceIdentifier,
                    derivation.GapOrPadding,
                    Span(derivation));
                if (bound is not null) result.Add(bound);
            }
            return result;
        }

        private IReadOnlyList<BoundRelativeDerivationSpec> BindCsvRelativeDerivations(string targetBox, ExpressionSyntax expression)
        {
            if (expression is not ArrayLiteralExpressionSyntax list)
            {
                Report("COPE-TABLE-DERIVE-0003", "CSV column 'derivations' must be a static list of compiler-known relative layout transforms.", FirstToken(expression));
                return [];
            }

            var result = new List<BoundRelativeDerivationSpec>();
            foreach (ExpressionSyntax element in list.Elements)
            {
                if (element is not CallExpressionSyntax { Target: NameExpressionSyntax transformName } call
                    || call.Arguments.Count is < 1 or > 2
                    || call.Arguments[0] is not NameExpressionSyntax sourceName)
                {
                    Report("COPE-TABLE-DERIVE-0003", "Each CSV derivation must be a closed relative transform call such as centerIn(root) or placeAbove(dialog, 8px).", FirstToken(element));
                    continue;
                }

                ExpressionSyntax? gapOrPadding = call.Arguments.Count == 2 ? call.Arguments[1] : null;
                BoundRelativeDerivationSpec? bound = BindRelativeDerivation(targetBox, transformName.IdentifierToken, sourceName.IdentifierToken, gapOrPadding, Span(call));
                if (bound is not null) result.Add(bound);
            }
            return result;
        }

        private BoundRelativeDerivationSpec? BindRelativeDerivation(
            string targetBox,
            SyntaxToken transformToken,
            SyntaxToken sourceToken,
            ExpressionSyntax? gapOrPadding,
            MachinaSourceSpan source)
        {
            if (!Enum.TryParse(transformToken.Text, ignoreCase: true, out LayoutRelativeTransform transform))
            {
                Report("COPE-TABLE-DERIVE-0003", $"'{transformToken.Text}' is not a compiler-known relative layout transform.", transformToken);
                return null;
            }

            bool requiresLength = transform is LayoutRelativeTransform.PlaceLeftOf
                or LayoutRelativeTransform.PlaceRightOf
                or LayoutRelativeTransform.PlaceAbove
                or LayoutRelativeTransform.PlaceBelow
                or LayoutRelativeTransform.InsetFrom
                or LayoutRelativeTransform.ExpandFrom;
            if (requiresLength != (gapOrPadding is not null))
            {
                string parameter = transform is LayoutRelativeTransform.InsetFrom or LayoutRelativeTransform.ExpandFrom ? "padding" : "gap";
                Report("COPE-TABLE-DERIVE-0007", $"{transformToken.Text} requires one static {parameter} length.", transformToken);
                return null;
            }
            if (!requiresLength && gapOrPadding is not null)
            {
                Report("COPE-TABLE-DERIVE-0007", $"{transformToken.Text} does not accept a gap or padding argument.", FirstToken(gapOrPadding));
                return null;
            }

            MachinaLength? length = null;
            if (gapOrPadding is not null)
            {
                length = Length(gapOrPadding);
                if (length is null)
                {
                    Report("COPE-TABLE-DERIVE-0006", "Relative layout gap and padding arguments must be static px or ui lengths; runtime values and unitless numbers are not supported.", FirstToken(gapOrPadding));
                    return null;
                }
            }

            return new BoundRelativeDerivationSpec(targetBox, transform, sourceToken.Text, length, _relativeDerivationOrder++, source);
        }

        public BoundLayoutDeclaration ApplyRelativeDerivations(BoundLayoutDeclaration layout)
        {
            var nodes = new Dictionary<string, BoundLayoutNode>(StringComparer.Ordinal);
            var parents = new Dictionary<string, LayoutNodeKind?>(StringComparer.Ordinal);
            Collect(layout.Root, parentKind: null);
            List<BoundRelativeDerivationSpec> specs = nodes.Values
                .SelectMany(node => node.RelativeDerivationSpecs ?? [])
                .OrderBy(spec => spec.AuthoredOrder)
                .ToList();
            if (specs.Count == 0) return layout;

            var valid = new List<BoundRelativeDerivationSpec>();
            var writtenBy = new Dictionary<(string Target, string Field), MachinaSourceSpan>();
            foreach (BoundRelativeDerivationSpec spec in specs)
            {
                if (!nodes.ContainsKey(spec.SourceBox))
                {
                    Report("COPE-TABLE-DERIVE-0003", $"Relative transform '{spec.Transform}' for '{layout.Name}.{spec.TargetBox}' references unknown box '{spec.SourceBox}'.", spec.Source);
                    continue;
                }
                if (parents.GetValueOrDefault(spec.TargetBox) != LayoutNodeKind.Overlay)
                {
                    Report("COPE-TABLE-DERIVE-0003", $"Relative transform '{spec.Transform}' is legal only for a box directly contained by an overlay. '{layout.Name}.{spec.TargetBox}' is flow-derived.", spec.Source);
                    continue;
                }

                bool conflict = false;
                BoundLayoutNode target = nodes[spec.TargetBox];
                foreach (string field in FieldsWritten(spec.Transform))
                {
                    bool direct = field is "x" or "y"
                        ? target.Positions.ContainsKey(field)
                        : target.Dimensions.ContainsKey(field);
                    if (direct)
                    {
                        Report("COPE-TABLE-DERIVE-0001", $"Field '{layout.Name}.{spec.TargetBox}.{field}' has more than one writer: a direct authored value and {spec.Transform}({spec.SourceBox}). Copeland does not solve competing geometric constraints.", spec.Source);
                        conflict = true;
                    }
                    else if (writtenBy.ContainsKey((spec.TargetBox, field)))
                    {
                        Report("COPE-TABLE-DERIVE-0004", $"Field '{layout.Name}.{spec.TargetBox}.{field}' is written by more than one relative transform. Copeland relative layout uses directed derivation; it does not solve simultaneous constraints.", spec.Source);
                        conflict = true;
                    }
                    else
                    {
                        writtenBy[(spec.TargetBox, field)] = spec.Source;
                    }
                }
                if (!conflict) valid.Add(spec);
            }

            var byTarget = valid.GroupBy(spec => spec.TargetBox, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderBy(spec => spec.AuthoredOrder).ToArray(), StringComparer.Ordinal);
            var ordered = new List<BoundRelativeDerivationSpec>();
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var path = new List<string>();
            foreach (string target in byTarget.Keys.OrderBy(name => name, StringComparer.Ordinal)) Visit(target);

            var current = new Dictionary<string, BoundLayoutNode>(nodes, StringComparer.Ordinal);
            var derivations = new List<BoundRowDerivation>();
            foreach (BoundRelativeDerivationSpec spec in ordered)
            {
                if (!TryApply(spec, current, layout.Name, out BoundRowDerivation? derivation)) continue;
                derivations.Add(derivation!);
            }

            BoundLayoutNode rewrittenRoot = Rewrite(layout.Root);
            return layout with { Root = rewrittenRoot, Slots = CollectSlots(rewrittenRoot), Derivations = derivations };

            void Collect(BoundLayoutNode node, LayoutNodeKind? parentKind)
            {
                if (!nodes.TryAdd(node.Name, node))
                {
                    Report("COPE-TABLE-DERIVE-0003", $"Relative derivation requires unique box names; '{node.Name}' is declared more than once.", node.Source);
                    return;
                }
                parents[node.Name] = parentKind;
                foreach (BoundLayoutNode child in node.Children) Collect(child, node.Kind);
            }

            void Visit(string target)
            {
                if (visited.Contains(target)) return;
                if (!visiting.Add(target))
                {
                    int start = path.IndexOf(target);
                    string cycle = string.Join(" → ", path.Skip(start).Append(target).Select(name => layout.Name + "." + name));
                    BoundRelativeDerivationSpec source = byTarget[target][0];
                    Report("COPE-TABLE-DERIVE-0002", $"Relative derivation cycle: {cycle}. Copeland performs directed immutable derivation; it does not solve simultaneous constraints.", source.Source);
                    return;
                }
                path.Add(target);
                foreach (BoundRelativeDerivationSpec dependency in byTarget[target])
                {
                    if (byTarget.ContainsKey(dependency.SourceBox)) Visit(dependency.SourceBox);
                }
                path.RemoveAt(path.Count - 1);
                visiting.Remove(target);
                visited.Add(target);
                ordered.AddRange(byTarget[target]);
            }

            BoundLayoutNode Rewrite(BoundLayoutNode node)
            {
                BoundLayoutNode rewritten = current[node.Name];
                return rewritten with { Children = node.Children.Select(Rewrite).ToArray() };
            }
        }

        private bool TryApply(BoundRelativeDerivationSpec spec, Dictionary<string, BoundLayoutNode> nodes, string layoutName, out BoundRowDerivation? derivation)
        {
            derivation = null;
            BoundLayoutNode source = nodes[spec.SourceBox];
            BoundLayoutNode target = nodes[spec.TargetBox];
            string[] reads = FieldsRead(spec.Transform).Select(field => field switch
            {
                _ when field.StartsWith("self.", StringComparison.Ordinal) => spec.TargetBox + field[4..],
                _ when field.StartsWith("source.", StringComparison.Ordinal) => spec.SourceBox + field[6..],
                _ => field,
            }).ToArray();
            string[] writes = FieldsWritten(spec.Transform).ToArray();
            var required = new List<MachinaLength>();

            bool Need(BoundLayoutNode node, string field, out MachinaLength value)
            {
                if (field is "x" or "y")
                {
                    if (node.Positions.TryGetValue(field, out value))
                    {
                        required.Add(value);
                        return true;
                    }
                    value = ZeroFor(node);
                    required.Add(value);
                    return true;
                }
                if (node.Dimensions.TryGetValue(field, out BoundLayoutDimension? dimension) && dimension.Kind == LayoutDimensionKind.Fixed && dimension.Length is MachinaLength length)
                {
                    value = length;
                    required.Add(value);
                    return true;
                }
                value = default;
                Report("COPE-TABLE-DERIVE-0005", $"Cannot evaluate {spec.Transform}({spec.SourceBox}) for '{layoutName}.{spec.TargetBox}'. Required field '{node.Name}.{field}' is not statically or host-resolvably available. Intrinsic component measurement is not available to layout derivation.", spec.Source);
                return false;
            }

            MachinaLength sx = default, sy = default, sw = default, sh = default, tw = default, th = default;
            bool inputs = spec.Transform switch
            {
                LayoutRelativeTransform.CenterIn => Need(source, "x", out sx) && Need(source, "y", out sy) && Need(source, "width", out sw) && Need(source, "height", out sh) && Need(target, "width", out tw) && Need(target, "height", out th),
                LayoutRelativeTransform.CenterXIn or LayoutRelativeTransform.AlignRight => Need(source, "x", out sx) && Need(source, "width", out sw) && Need(target, "width", out tw),
                LayoutRelativeTransform.CenterYIn or LayoutRelativeTransform.AlignBottom => Need(source, "y", out sy) && Need(source, "height", out sh) && Need(target, "height", out th),
                LayoutRelativeTransform.AlignLeft => Need(source, "x", out sx),
                LayoutRelativeTransform.AlignTop => Need(source, "y", out sy),
                LayoutRelativeTransform.PlaceLeftOf => Need(source, "x", out sx) && Need(target, "width", out tw),
                LayoutRelativeTransform.PlaceRightOf => Need(source, "x", out sx) && Need(source, "width", out sw),
                LayoutRelativeTransform.PlaceAbove => Need(source, "y", out sy) && Need(target, "height", out th),
                LayoutRelativeTransform.PlaceBelow => Need(source, "y", out sy) && Need(source, "height", out sh),
                LayoutRelativeTransform.InsetFrom or LayoutRelativeTransform.ExpandFrom => Need(source, "x", out sx) && Need(source, "y", out sy) && Need(source, "width", out sw) && Need(source, "height", out sh),
                _ => false,
            };
            if (!inputs) return false;
            if (spec.GapOrPadding is MachinaLength gap) required.Add(gap);
            if (!TryCommonUnit(required, spec.Source, out _)) return false;

            var positions = target.Positions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var dimensions = target.Dimensions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            MachinaLength gapOrPadding = spec.GapOrPadding ?? MachinaLength.Pixels(0);
            switch (spec.Transform)
            {
                case LayoutRelativeTransform.CenterIn:
                    positions["x"] = sx + (sw - tw).Scale(0.5);
                    positions["y"] = sy + (sh - th).Scale(0.5);
                    break;
                case LayoutRelativeTransform.CenterXIn: positions["x"] = sx + (sw - tw).Scale(0.5); break;
                case LayoutRelativeTransform.CenterYIn: positions["y"] = sy + (sh - th).Scale(0.5); break;
                case LayoutRelativeTransform.AlignLeft: positions["x"] = sx; break;
                case LayoutRelativeTransform.AlignRight: positions["x"] = sx + sw - tw; break;
                case LayoutRelativeTransform.AlignTop: positions["y"] = sy; break;
                case LayoutRelativeTransform.AlignBottom: positions["y"] = sy + sh - th; break;
                case LayoutRelativeTransform.PlaceLeftOf: positions["x"] = sx - tw - gapOrPadding; break;
                case LayoutRelativeTransform.PlaceRightOf: positions["x"] = sx + sw + gapOrPadding; break;
                case LayoutRelativeTransform.PlaceAbove: positions["y"] = sy - th - gapOrPadding; break;
                case LayoutRelativeTransform.PlaceBelow: positions["y"] = sy + sh + gapOrPadding; break;
                case LayoutRelativeTransform.InsetFrom:
                    positions["x"] = sx + gapOrPadding; positions["y"] = sy + gapOrPadding;
                    dimensions["width"] = new BoundLayoutDimension(LayoutDimensionKind.Fixed, sw - gapOrPadding.Scale(2));
                    dimensions["height"] = new BoundLayoutDimension(LayoutDimensionKind.Fixed, sh - gapOrPadding.Scale(2));
                    break;
                case LayoutRelativeTransform.ExpandFrom:
                    positions["x"] = sx - gapOrPadding; positions["y"] = sy - gapOrPadding;
                    dimensions["width"] = new BoundLayoutDimension(LayoutDimensionKind.Fixed, sw + gapOrPadding.Scale(2));
                    dimensions["height"] = new BoundLayoutDimension(LayoutDimensionKind.Fixed, sh + gapOrPadding.Scale(2));
                    break;
            }
            if (dimensions.TryGetValue("width", out BoundLayoutDimension? width) && width.Length is MachinaLength resolvedWidth && IsNegative(resolvedWidth)
                || dimensions.TryGetValue("height", out BoundLayoutDimension? height) && height.Length is MachinaLength resolvedHeight && IsNegative(resolvedHeight))
            {
                Report("COPE-TABLE-DERIVE-0008", $"{spec.Transform}({spec.SourceBox}) produces a negative extent for '{layoutName}.{spec.TargetBox}'.", spec.Source);
                return false;
            }

            nodes[spec.TargetBox] = target with { Positions = positions, Dimensions = dimensions };
            derivation = new BoundRowDerivation(
                layoutName + "::" + spec.TargetBox + "::" + spec.AuthoredOrder,
                "layout::Boxes",
                spec.TargetBox,
                spec.Transform,
                spec.SourceBox,
                reads,
                writes,
                spec.AuthoredOrder,
                LayoutDerivationStatus.Resolved,
                spec.Source,
                spec.GapOrPadding);
            return true;
        }

        private bool TryCommonUnit(IEnumerable<MachinaLength> values, MachinaSourceSpan source, out LayoutCoordinateUnit unit)
        {
            LayoutCoordinateUnit? expected = null;
            foreach (MachinaLength value in values)
            {
                LayoutCoordinateUnit? actual = UnitOf(value);
                if (actual is null)
                {
                    Report("COPE-TABLE-DERIVE-0006", "Relative layout inputs must use one compatible px or ui coordinate space; affine px/ui mixtures are not supported.", source);
                    unit = LayoutCoordinateUnit.Px;
                    return false;
                }
                if (expected is not null && expected != actual)
                {
                    Report("COPE-TABLE-DERIVE-0006", "Relative layout gap, padding, coordinates, and extents must use compatible units. Copeland does not implicitly mix px and ui.", source);
                    unit = LayoutCoordinateUnit.Px;
                    return false;
                }
                expected = actual;
            }
            unit = expected ?? LayoutCoordinateUnit.Px;
            return true;
        }

        private static LayoutCoordinateUnit? UnitOf(MachinaLength value)
        {
            if (value.Ui != 0 && value.Px != 0) return null;
            if (value.Ui != 0 || value.LiteralUnit == MachinaLengthLiteralUnit.Ui) return LayoutCoordinateUnit.Ui;
            return LayoutCoordinateUnit.Px;
        }

        private static MachinaLength ZeroFor(BoundLayoutNode node)
        {
            LayoutCoordinateUnit unit = node.Dimensions.Values.Select(dimension => dimension.Length).OfType<MachinaLength>()
                .Concat(node.Positions.Values)
                .Select(UnitOf)
                .FirstOrDefault(value => value is not null) ?? LayoutCoordinateUnit.Px;
            return unit == LayoutCoordinateUnit.Ui ? MachinaLength.Normalized(0) : MachinaLength.Pixels(0);
        }

        private static bool IsNegative(MachinaLength value) => value.Px < 0 || value.Ui < 0;

        private static IReadOnlyList<string> FieldsWritten(LayoutRelativeTransform transform) => transform switch
        {
            LayoutRelativeTransform.CenterIn => ["x", "y"],
            LayoutRelativeTransform.CenterXIn or LayoutRelativeTransform.AlignLeft or LayoutRelativeTransform.AlignRight or LayoutRelativeTransform.PlaceLeftOf or LayoutRelativeTransform.PlaceRightOf => ["x"],
            LayoutRelativeTransform.CenterYIn or LayoutRelativeTransform.AlignTop or LayoutRelativeTransform.AlignBottom or LayoutRelativeTransform.PlaceAbove or LayoutRelativeTransform.PlaceBelow => ["y"],
            LayoutRelativeTransform.InsetFrom or LayoutRelativeTransform.ExpandFrom => ["x", "y", "width", "height"],
            _ => [],
        };

        private static IReadOnlyList<string> FieldsRead(LayoutRelativeTransform transform) => transform switch
        {
            LayoutRelativeTransform.CenterIn => ["source.x", "source.y", "source.width", "source.height", "self.width", "self.height"],
            LayoutRelativeTransform.CenterXIn => ["source.x", "source.width", "self.width"],
            LayoutRelativeTransform.CenterYIn => ["source.y", "source.height", "self.height"],
            LayoutRelativeTransform.AlignLeft => ["source.x"],
            LayoutRelativeTransform.AlignRight => ["source.x", "source.width", "self.width"],
            LayoutRelativeTransform.AlignTop => ["source.y"],
            LayoutRelativeTransform.AlignBottom => ["source.y", "source.height", "self.height"],
            LayoutRelativeTransform.PlaceLeftOf => ["source.x", "self.width", "gap"],
            LayoutRelativeTransform.PlaceRightOf => ["source.x", "source.width", "gap"],
            LayoutRelativeTransform.PlaceAbove => ["source.y", "self.height", "gap"],
            LayoutRelativeTransform.PlaceBelow => ["source.y", "source.height", "gap"],
            LayoutRelativeTransform.InsetFrom or LayoutRelativeTransform.ExpandFrom => ["source.x", "source.y", "source.width", "source.height", "padding"],
            _ => [],
        };

        private LayoutPropertyData[] ReadProperties(IReadOnlyList<LayoutPropertySyntax> properties)
            => properties.Select(property => new LayoutPropertyData(property.Identifier, ReadValue(property.Value))).ToArray();

        private LayoutValue ReadValue(ExpressionSyntax expression)
        {
            SyntaxToken anchor = FirstToken(expression);
            if (expression is NameExpressionSyntax name)
            {
                return new LayoutValue(anchor, Keyword: name.IdentifierToken.Text);
            }
            if (expression is ObjectLiteralExpressionSyntax record)
            {
                var fields = new Dictionary<string, LayoutValue>(StringComparer.Ordinal);
                foreach (ObjectPropertySyntax property in record.Properties)
                {
                    if (!fields.TryAdd(property.NameToken.Text, ReadValue(property.ValueExpression)))
                    {
                        Report("COPE-LAYOUT-PROPERTY-0001", $"Field '{property.NameToken.Text}' is declared more than once.", property.NameToken);
                    }
                }
                return new LayoutValue(anchor, Fields: fields);
            }
            if (expression is LiteralExpressionSyntax literal)
            {
                object? value = literal.LiteralToken.Value;
                if (value is LengthLiteralTokenValue length)
                {
                    try
                    {
                        value = length.Unit == "px" ? MachinaLength.Pixels(length.Value) : MachinaLength.Normalized(length.Value);
                    }
                    catch (MachinaLayoutException exception)
                    {
                        Report(exception.Code, exception.Message, anchor);
                        value = null;
                    }
                }
                return new LayoutValue(anchor, value);
            }
            if (expression is UnaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.MinusToken } unary)
            {
                LayoutValue operand = ReadValue(unary.Operand);
                return operand.Scalar switch
                {
                    MachinaLength length => new LayoutValue(anchor, -length),
                    int integer => new LayoutValue(anchor, -integer),
                    _ => new LayoutValue(anchor)
                };
            }
            if (expression is BinaryExpressionSyntax binary && binary.OperatorToken.Kind is SyntaxKind.PlusToken or SyntaxKind.MinusToken)
            {
                LayoutValue left = ReadValue(binary.Left);
                LayoutValue right = ReadValue(binary.Right);
                if (left.Scalar is MachinaLength leftLength && right.Scalar is MachinaLength rightLength)
                {
                    try
                    {
                        return new LayoutValue(anchor, binary.OperatorToken.Kind == SyntaxKind.PlusToken
                            ? leftLength + rightLength : leftLength - rightLength);
                    }
                    catch (MachinaLayoutException exception)
                    {
                        Report(exception.Code, exception.Message, anchor);
                    }
                }
            }
            return new LayoutValue(anchor);
        }

        private MachinaLength? Length(ExpressionSyntax value) => Length(ReadValue(value));
        private static bool TryStaticInteger(ExpressionSyntax value, out int integer)
        {
            if (value is LiteralExpressionSyntax { LiteralToken.Value: int number })
            {
                integer = number;
                return true;
            }
            if (value is UnaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.MinusToken,
                Operand: LiteralExpressionSyntax { LiteralToken.Value: int negative } })
            {
                integer = -negative;
                return true;
            }
            integer = 0;
            return false;
        }

private void ValidateProperties(IReadOnlyList<LayoutPropertySyntax> properties, LayoutNodeKind? kind)
            => ValidateProperties(ReadProperties(properties), kind);

private IReadOnlyDictionary<string, BoundLayoutDimension> BindDimensions(IReadOnlyList<LayoutPropertySyntax> properties)
            => BindDimensions(ReadProperties(properties));

private BoundLayerSet ResolveLayerSet(IReadOnlyList<LayoutPropertySyntax> properties, SyntaxToken token, string layoutName)
            => ResolveLayerSet(ReadProperties(properties), token, layoutName);

private BoundPaintProperties BindPaint(IReadOnlyList<LayoutPropertySyntax> properties, BoundLayerSet layerSet, string layoutName, string inheritedLayer)
            => BindPaint(ReadProperties(properties), layerSet, layoutName, inheritedLayer);

private IReadOnlyDictionary<string, MachinaLength> BindPositions(IReadOnlyList<LayoutPropertySyntax> properties)
            => BindPositions(ReadProperties(properties));

private BoundBoxOverflowPolicy? Overflow(IReadOnlyList<LayoutPropertySyntax> properties)
            => Overflow(ReadProperties(properties));

private BoundTextFitPolicy? TextFit(IReadOnlyList<LayoutPropertySyntax> properties, LayoutNodeKind kind)
            => TextFit(ReadProperties(properties), kind);

private MachinaLength Gap(IReadOnlyList<LayoutPropertySyntax> properties)
            => Gap(ReadProperties(properties));

private int? Columns(IReadOnlyList<LayoutPropertySyntax> properties)
            => Columns(ReadProperties(properties));

private MachinaInsets Padding(IReadOnlyList<LayoutPropertySyntax> properties)
            => Padding(ReadProperties(properties));

private MachinaStyle Style(IReadOnlyList<LayoutPropertySyntax> properties)
            => Style(ReadProperties(properties));

        private BoundLayoutNode ApplyComposition(BoundLayoutNode root, IReadOnlyList<LayoutPropertyData> replacements,
            BoundLayerSet layerSet, string layoutName)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (LayoutPropertyData replacement in replacements)
            {
                string path = replacement.Identifier.Text;
                if (!paths.Add(path) || paths.Any(other => other != path
                    && (other.StartsWith(path + ".", StringComparison.Ordinal)
                        || path.StartsWith(other + ".", StringComparison.Ordinal))))
                {
                    Report("COPE-LAYOUT-WITH-0001", $"Layout replacement '{path}' overlaps another replacement.", replacement.Identifier);
                    return root;
                }
            }
            foreach (IGrouping<string, LayoutPropertyData> group in replacements.GroupBy(property =>
                property.Identifier.Text.Contains('.') ? property.Identifier.Text[..property.Identifier.Text.LastIndexOf('.')] : string.Empty))
            {
                string[] members = group.Key.Length == 0 ? [] : group.Key.Split('.');
                LayoutPropertyData[] properties = group.Select(property => property with
                {
                    Identifier = property.Identifier with { Text = property.Identifier.Text.Split('.').Last() }
                }).ToArray();
                bool found = false;
                root = Update(root, 0);
                if (!found)
                {
                    Report("COPE-LAYOUT-WITH-0002", $"Layout '{layoutName}' has no node at '{group.Key}'.", group.First().Identifier);
                }

                BoundLayoutNode Update(BoundLayoutNode node, int index)
                {
                    if (index == members.Length)
                    {
                        found = true;
                        ValidateProperties(properties, members.Length == 0 ? null : node.Kind);
                        return ApplyOverrides(node, properties);
                    }
                    return node with
                    {
                        Children = node.Children.Select(child => child.Name == members[index]
                            ? Update(child, index + 1) : child).ToArray()
                    };
                }
            }
            return root;
        }

        private BoundLayoutNode ApplyOverrides(BoundLayoutNode root, IReadOnlyList<LayoutPropertyData> properties)
        {
            var dimensions = root.Dimensions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach ((string key, BoundLayoutDimension value) in BindDimensions(properties)) dimensions[key] = value;
            var positions = root.Positions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach ((string key, MachinaLength value) in BindPositions(properties)) positions[key] = value;
            int? columns = Has(properties, "columns") ? Columns(properties) : root.Columns;
            if (root.Kind == LayoutNodeKind.Grid && columns is null)
            {
                Report("COPE-LAYOUT-GRID-0001", "A grid requires a positive integer 'columns' property.", properties[0].Identifier);
            }
            if (properties.Any(property => property.Identifier.Text is "layer" or "z"
                or "fontSize" or "minFontSize" or "lines" or "wrap" or "textFit" or "textFallback"))
            {
                Report("COPE-LAYOUT-WITH-0003", "Paint and text policy replacement is not supported by this layout variant slice. Configure these in the template.", properties[0].Identifier);
            }
            return root with
            {
                Dimensions = dimensions,
                Positions = positions,
                Columns = columns,
                Gap = Has(properties, "gap") ? Gap(properties) : root.Gap,
                Padding = Has(properties, "padding") ? Padding(properties) : root.Padding,
                Style = Has(properties, "style") ? Style(properties) : root.Style,
                Overflow = Has(properties, "overflow") ? Overflow(properties) : root.Overflow,
            };
        }

        private void ValidateAnchor(BoundLayoutNode node, SyntaxToken token)
        {
            int horizontal = ConstraintCount(node, "left", "right", "width");
            int vertical = ConstraintCount(node, "top", "bottom", "height");
            if (horizontal != 2 || vertical != 2)
            {
                Report("COPE-LAYOUT-ANCHOR-0001", "An anchor requires exactly two horizontal and two vertical constraints.", token);
            }
        }

        private static int ConstraintCount(BoundLayoutNode node, string first, string second, string dimension)
            => (node.Positions.ContainsKey(first) ? 1 : 0)
             + (node.Positions.ContainsKey(second) ? 1 : 0)
             + (HasFixedDimension(node, dimension) ? 1 : 0);

        private static bool HasFixedDimension(BoundLayoutNode node, string name)
            => node.Dimensions.TryGetValue(name, out BoundLayoutDimension? value) && value.Kind == LayoutDimensionKind.Fixed;

        private static bool HasFrameWriter(BoundLayoutNode node, string field)
            => HasFixedDimension(node, field)
                || (node.RelativeDerivationSpecs ?? []).Any(spec => FieldsWritten(spec.Transform).Contains(field, StringComparer.Ordinal));

        private void ValidateProperties(IReadOnlyList<LayoutPropertyData> properties, LayoutNodeKind? kind)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (LayoutPropertyData property in properties)
            {
                if (!names.Add(property.Identifier.Text))
                {
                    Report("COPE-LAYOUT-PROPERTY-0001", $"Layout property '{property.Identifier.Text}' is declared more than once.", property.Identifier);
                    continue;
                }
                bool allowed = kind switch
                {
                    null => property.Identifier.Text is "width" or "height" or "frame" or "gap" or "padding" or "style" or "layers" or "overflow",
                    LayoutNodeKind.Row or LayoutNodeKind.Column => property.Identifier.Text is "x" or "y" or "position" or "width" or "height" or "frame" or "gap" or "padding" or "style" or "layer" or "z" or "overflow",
                    LayoutNodeKind.Grid => property.Identifier.Text is "x" or "y" or "position" or "width" or "height" or "frame" or "gap" or "padding" or "style" or "columns" or "layer" or "z" or "overflow",
                    LayoutNodeKind.Anchor => property.Identifier.Text is "left" or "right" or "top" or "bottom" or "width" or "height" or "frame" or "gap" or "padding" or "style" or "layer" or "z" or "overflow",
                    LayoutNodeKind.Overlay => property.Identifier.Text is "x" or "y" or "position" or "width" or "height" or "frame" or "gap" or "padding" or "style" or "layer" or "z" or "overflow",
                    LayoutNodeKind.Slot => property.Identifier.Text is "x" or "y" or "position" or "width" or "height" or "frame" or "style" or "layer" or "z" or "overflow" or "fontSize" or "minFontSize" or "lines" or "wrap" or "textFit" or "textFallback",
                    _ => false,
                };
                if (!allowed) Report("COPE-LAYOUT-PROPERTY-0002", $"Property '{property.Identifier.Text}' is not valid for this layout node.", property.Identifier);
            }
        }

        private static IReadOnlyDictionary<string, BoundLayoutNode> CollectSlots(BoundLayoutNode root)
        {
            var slots = new Dictionary<string, BoundLayoutNode>(StringComparer.Ordinal);
            Add(root);
            return slots;
            void Add(BoundLayoutNode node)
            {
                slots.TryAdd(node.Name, node);
                foreach (BoundLayoutNode child in node.Children) Add(child);
            }
        }

        private IReadOnlyDictionary<string, BoundLayoutDimension> BindDimensions(IReadOnlyList<LayoutPropertyData> properties)
        {
            var result = new Dictionary<string, BoundLayoutDimension>(StringComparer.Ordinal);
            foreach (LayoutPropertyData property in properties.Where(property => property.Identifier.Text is "width" or "height"))
            {
                BoundLayoutDimension? dimension = Dimension(property.Value);
                if (dimension is not null) result[property.Identifier.Text] = dimension;
            }
            if (Property(properties, "frame")?.Value is { Fields: not null } frame)
            {
                foreach (string name in new[] { "width", "height" })
                {
                    if (ObjectProperty(frame, name) is LayoutValue value && Dimension(value) is BoundLayoutDimension dimension)
                    {
                        result[name] = dimension;
                    }
                }
            }
            return result;
        }

        private BoundLayerSet ResolveLayerSet(IReadOnlyList<LayoutPropertyData> properties, SyntaxToken token, string layoutName)
        {
            LayoutPropertyData? property = Property(properties, "layers");
            if (property is null) return BoundLayerSet.Default;
            if (property.Value is not { Keyword: not null } name)
            {
                Report("COPE-LAYOUT-LAYER-0007", $"Layout '{layoutName}' must name a declared semantic layer set in 'layers:'.", property.Identifier);
                return BoundLayerSet.Default;
            }
            if (_layerSets.TryGetValue(name.Keyword, out BoundLayerSet? local)) return local;
            if (_importedLayerSets.TryGetValue(name.Keyword, out BoundLayerSet? imported)) return imported;
            Report("COPE-LAYOUT-LAYER-0008", $"Layout '{layoutName}' uses unknown semantic layer set '{name.Keyword}'.", name.Anchor);
            return BoundLayerSet.Default;
        }

        private BoundPaintProperties BindPaint(IReadOnlyList<LayoutPropertyData> properties, BoundLayerSet layerSet, string layoutName, string inheritedLayer)
        {
            string layer = inheritedLayer;
            if (Property(properties, "layer") is { } layerProperty)
            {
                if (layerProperty.Value is { Keyword: not null } name)
                {
                    layer = name.Keyword;
                    if (layerSet.RankOf(layer) < 0)
                    {
                        Report("COPE-LAYOUT-LAYER-0001", $"Layout '{layoutName}' names unknown semantic layer '{layer}' in layer set '{layerSet.Name}'.", name.Anchor);
                        layer = layerSet.Layers[0];
                    }
                }
                else
                {
                    Report("COPE-LAYOUT-LAYER-0009", $"Layout '{layoutName}' requires a declared semantic layer name for 'layer:'.", layerProperty.Identifier);
                    layer = layerSet.Layers[0];
                }
            }
            int z = 0;
            if (Property(properties, "z") is { } zProperty)
            {
                if (!TryStaticInteger(zProperty.Value, out int value))
                {
                    Report("COPE-LAYOUT-Z-0002", "Numeric z order must be a statically known integral value.", zProperty.Identifier);
                }
                else if (value is < -5 or > 5)
                {
                    Report("COPE-LAYOUT-Z-0001", $"Numeric z order must be between -5 and 5.\n\nReceived:\n  {value}\n\nUse a semantic layer when broader ordering is required.", zProperty.Identifier);
                }
                else
                {
                    z = value;
                }
            }
            return new BoundPaintProperties(layer, z);
        }

        private static bool TryStaticInteger(LayoutValue expression, out int value)
        {
            if (expression is { Scalar: int integer })
            {
                value = integer;
                return true;
            }
            value = 0;
            return false;
        }

        private IReadOnlyDictionary<string, MachinaLength> BindPositions(IReadOnlyList<LayoutPropertyData> properties)
        {
            var result = new Dictionary<string, MachinaLength>(StringComparer.Ordinal);
            foreach (LayoutPropertyData property in properties.Where(property => property.Identifier.Text is "x" or "y" or "left" or "right" or "top" or "bottom"))
            {
                MachinaLength? value = Length(property.Value);
                if (value is not null) result[property.Identifier.Text] = value.Value;
            }
            foreach (string recordName in new[] { "position", "frame" })
            {
                if (Property(properties, recordName)?.Value is not { Fields: not null } record) continue;
                foreach (string name in new[] { "x", "y", "left", "right", "top", "bottom" })
                {
                    if (ObjectProperty(record, name) is LayoutValue value && Length(value) is MachinaLength length)
                    {
                        result[name] = length;
                    }
                }
            }
            return result;
        }

        private BoundLayoutDimension? Dimension(LayoutValue expression)
        {
            if (expression is { Keyword: not null } name && name.Keyword == "fill") return new BoundLayoutDimension(LayoutDimensionKind.Fill);
            if (expression is { Keyword: not null } fit && fit.Keyword == "fit") return new BoundLayoutDimension(LayoutDimensionKind.Fit);
            MachinaLength? length = Length(expression);
            if (length is not null && !IsNegative(length.Value)) return new BoundLayoutDimension(LayoutDimensionKind.Fixed, length);
            Report("COPE-LAYOUT-DIMENSION-0001", "A layout dimension must be a length, 'fill', or 'fit'.", expression.Anchor);
            return null;
        }

        private BoundBoxOverflowPolicy? Overflow(IReadOnlyList<LayoutPropertyData> properties)
        {
            LayoutPropertyData? property = Property(properties, "overflow");
            if (property is null) return null;
            if (property.Value is not { Keyword: not null } name || !Enum.TryParse(name.Keyword, true, out LayoutOverflowPolicy policy))
            {
                Report("COPE-LAYOUT-OVERFLOW-0001", "Overflow must be one of: visible, clip, auto, scroll, scrollX, scrollY.", property.Identifier);
                return BoundBoxOverflowPolicy.Visible;
            }

            return policy switch
            {
                LayoutOverflowPolicy.Visible => BoundBoxOverflowPolicy.Visible,
                LayoutOverflowPolicy.Clip => new BoundBoxOverflowPolicy(policy, LayoutOverflowAxis.Clip, LayoutOverflowAxis.Clip),
                LayoutOverflowPolicy.Auto => new BoundBoxOverflowPolicy(policy, LayoutOverflowAxis.Auto, LayoutOverflowAxis.Auto),
                LayoutOverflowPolicy.Scroll => new BoundBoxOverflowPolicy(policy, LayoutOverflowAxis.Scroll, LayoutOverflowAxis.Scroll),
                LayoutOverflowPolicy.ScrollX => new BoundBoxOverflowPolicy(policy, LayoutOverflowAxis.Auto, LayoutOverflowAxis.Clip),
                LayoutOverflowPolicy.ScrollY => new BoundBoxOverflowPolicy(policy, LayoutOverflowAxis.Clip, LayoutOverflowAxis.Auto),
                _ => throw new InvalidOperationException("Unknown overflow policy."),
            };
        }

        private BoundTextFitPolicy? TextFit(IReadOnlyList<LayoutPropertyData> properties, LayoutNodeKind kind)
        {
            string[] names = ["fontSize", "minFontSize", "lines", "wrap", "textFit", "textFallback"];
            if (!properties.Any(property => names.Contains(property.Identifier.Text, StringComparer.Ordinal))) return null;
            if (kind != LayoutNodeKind.Slot)
            {
                LayoutPropertyData property = properties.First(property => names.Contains(property.Identifier.Text, StringComparer.Ordinal));
                Report("COPE-TEXT-FIT-0001", "Text fitting is valid only on a declared text slot, never a structural or mixed-content box.", property.Identifier);
                return null;
            }

            LayoutPropertyData? fontSize = Property(properties, "fontSize");
            LayoutPropertyData? minimum = Property(properties, "minFontSize");
            LayoutPropertyData? lines = Property(properties, "lines");
            LayoutPropertyData? fit = Property(properties, "textFit");
            if (fontSize is null || minimum is null || lines is null || fit is null)
            {
                LayoutPropertyData source = fontSize ?? minimum ?? lines ?? fit ?? properties[0];
                Report("COPE-TEXT-FIT-0002", "A text-fit slot requires fontSize, minFontSize, lines, and textFit.", source.Identifier);
                return null;
            }

            MachinaLength? preferred = Length(fontSize.Value);
            MachinaLength? min = Length(minimum.Value);
            if (preferred is null || min is null || preferred.Value.Px <= 0 || preferred.Value.Ui != 0 || min.Value.Px <= 0 || min.Value.Ui != 0)
            {
                Report("COPE-TEXT-FIT-0003", "fontSize and minFontSize must be positive px lengths.", fontSize.Identifier);
                return null;
            }
            if (min.Value.Px > preferred.Value.Px)
            {
                Report("COPE-TEXT-FIT-0004", "minFontSize cannot exceed fontSize.", minimum.Identifier);
                return null;
            }
            if (!TryStaticInteger(lines.Value, out int maximumLines) || maximumLines <= 0)
            {
                Report("COPE-TEXT-FIT-0005", "lines must be a positive static integer.", lines.Identifier);
                return null;
            }
            if (fit.Value is not { Keyword: not null } fitName || !Enum.TryParse(fitName.Keyword, true, out TextFitMode fitMode))
            {
                Report("COPE-TEXT-FIT-0006", "textFit must be one of: none, scaleDown.", fit.Identifier);
                return null;
            }

            TextWrapMode wrapMode = TextWrapMode.Wrap;
            if (Property(properties, "wrap") is { } wrap && (wrap.Value is not { Keyword: not null } wrapName || !Enum.TryParse(wrapName.Keyword, true, out wrapMode)))
            {
                Report("COPE-TEXT-FIT-0007", "wrap must be one of: wrap, noWrap.", wrap.Identifier);
                return null;
            }
            TextOverflowFallback fallback = TextOverflowFallback.Clip;
            if (Property(properties, "textFallback") is { } fallbackProperty && (fallbackProperty.Value is not { Keyword: not null } fallbackName || !Enum.TryParse(fallbackName.Keyword, true, out fallback)))
            {
                Report("COPE-TEXT-FIT-0008", "textFallback must be one of: clip, ellipsis, overflow.", fallbackProperty.Identifier);
                return null;
            }
            if (fitMode == TextFitMode.None && min.Value.Px != preferred.Value.Px)
            {
                Report("COPE-TEXT-FIT-0009", "textFit: none requires minFontSize to equal fontSize.", fit.Identifier);
                return null;
            }
            return new BoundTextFitPolicy(preferred.Value, min.Value, maximumLines, wrapMode, fitMode, fallback, Span(fit.Identifier));
        }

        private MachinaLength Gap(IReadOnlyList<LayoutPropertyData> properties)
            => NonnegativeLength(properties, "gap") ?? MachinaLength.Pixels(0);

        private MachinaLength? NonnegativeLength(IReadOnlyList<LayoutPropertyData> properties, string name)
        {
            LayoutPropertyData? property = Property(properties, name);
            if (property is null) return null;
            MachinaLength? length = Length(property.Value);
            if (length is null || IsNegative(length.Value))
            {
                Report("COPE-LAYOUT-DIMENSION-0001", $"'{name}' requires a nonnegative unit-bearing length.", property.Identifier);
                return null;
            }
            return length;
        }

        private int? Columns(IReadOnlyList<LayoutPropertyData> properties)
        {
            LayoutPropertyData? property = Property(properties, "columns");
            if (property?.Value is { Scalar: int value } && value > 0) return value;
            return null;
        }

        private MachinaInsets Padding(IReadOnlyList<LayoutPropertyData> properties)
        {
            MachinaLength? value = NonnegativeLength(properties, "padding");
            if (value is null) return MachinaInsets.None;
            if (value.Value.Ui != 0 || value.Value.LiteralUnit == MachinaLengthLiteralUnit.Ui)
            {
                Report("COPE-LAYOUT-DIMENSION-0001", "Padding currently requires a px length. Use a px configuration value.", Property(properties, "padding")!.Identifier);
                return MachinaInsets.None;
            }
            return MachinaInsets.All(value.Value.Px);
        }

        private MachinaStyle Style(IReadOnlyList<LayoutPropertyData> properties)
        {
            LayoutPropertyData? property = Property(properties, "style");
            if (property is null) return MachinaStyle.Empty;
            if (property.Value.Fields is not { } values)
            {
                Report("COPE-LAYOUT-STYLE-0001", "Layout style must be an immutable record.", property.Identifier);
                return MachinaStyle.Empty;
            }
            foreach (string field in values.Keys)
            {
                if (field is not ("fill" or "border" or "radius" or "text"))
                {
                    Report("COPE-LAYOUT-STYLE-0001", $"Layout style has no supported field '{field}'. Use fill, radius, border, or text.", values[field].Anchor);
                }
            }
            string? fill = ReadString(values, "fill");
            MachinaLength? radius = ReadPixelLength(values, "radius", allowZero: true);
            MachinaSurfaceStyle? surface = fill is null && radius is null ? null : new MachinaSurfaceStyle(Fill: fill, Radius: radius);
            MachinaTextStyle? textStyle = null;
            if (values.TryGetValue("text", out LayoutValue? textValue))
            {
                if (textValue.Fields is not { } textFields)
                {
                    Report("COPE-LAYOUT-STYLE-0001", "Text style requires an immutable record.", textValue.Anchor);
                }
                else
                {
                    foreach (string field in textFields.Keys)
                    {
                        if (field is not ("color" or "size" or "weight" or "lineHeight" or "wrap"))
                        {
                            Report("COPE-LAYOUT-STYLE-0001", $"Text style has no field '{field}'.", textFields[field].Anchor);
                        }
                    }
                    int? weight = null;
                    if (textFields.TryGetValue("weight", out LayoutValue? weightValue))
                    {
                        if (weightValue.Scalar is int integer && integer is >= 1 and <= 1000) weight = integer;
                        else Report("COPE-LAYOUT-STYLE-0001", "Text weight requires an integer in [1, 1000].", weightValue.Anchor);
                    }
                    double? lineHeight = null;
                    if (textFields.TryGetValue("lineHeight", out LayoutValue? heightValue))
                    {
                        if (heightValue.Scalar is int or double)
                        {
                            double number = Convert.ToDouble(heightValue.Scalar, System.Globalization.CultureInfo.InvariantCulture);
                            if (double.IsFinite(number) && number > 0) lineHeight = number;
                        }
                        if (lineHeight is null) Report("COPE-LAYOUT-STYLE-0001", "Text lineHeight requires a positive finite ratio.", heightValue.Anchor);
                    }
                    MachinaTextWrap? wrap = null;
                    if (textFields.TryGetValue("wrap", out LayoutValue? wrapValue))
                    {
                        wrap = wrapValue.Scalar switch
                        {
                            "normal" => MachinaTextWrap.Normal,
                            "anywhere" => MachinaTextWrap.Anywhere,
                            _ => null,
                        };
                        if (wrap is null) Report("COPE-LAYOUT-STYLE-0001", "Text wrap requires \"normal\" or \"anywhere\".", wrapValue.Anchor);
                    }
                    textStyle = new MachinaTextStyle(ReadString(textFields, "color"),
                        ReadPixelLength(textFields, "size", allowZero: false), weight, lineHeight, wrap);
                }
            }
            MachinaBorderStyle? border = null;
            if (values.TryGetValue("border", out LayoutValue? borderValue))
            {
                if (borderValue.Fields is not { } fields)
                {
                    Report("COPE-LAYOUT-STYLE-0001", "Border style requires an immutable record with width, color, or style.", borderValue.Anchor);
                }
                else
                {
                    foreach (string field in fields.Keys)
                    {
                        if (field is not ("width" or "color" or "style"))
                        {
                            Report("COPE-LAYOUT-STYLE-0001", $"Border style has no field '{field}'.", fields[field].Anchor);
                        }
                    }
                    MachinaLength? width = null;
                    if (fields.TryGetValue("width", out LayoutValue? widthValue))
                    {
                        width = Length(widthValue);
                        if (width is null || IsNegative(width.Value))
                        {
                            Report("COPE-LAYOUT-STYLE-0001", "Border width requires a nonnegative unit-bearing length.", widthValue.Anchor);
                        }
                    }
                    border = new MachinaBorderStyle(width, ReadString(fields, "color"), ReadString(fields, "style"));
                }
            }
            return surface is null && border is null && textStyle is null
                ? MachinaStyle.Empty : new MachinaStyle(Surface: surface, Border: border, Text: textStyle);

            MachinaLength? ReadPixelLength(IReadOnlyDictionary<string, LayoutValue> fields, string name, bool allowZero)
            {
                if (!fields.TryGetValue(name, out LayoutValue? value)) return null;
                MachinaLength? length = Length(value);
                if (length is not null && length.Value.Ui == 0
                    && length.Value.LiteralUnit != MachinaLengthLiteralUnit.Ui
                    && (allowZero ? length.Value.Px >= 0 : length.Value.Px > 0)) return length;
                Report("COPE-LAYOUT-STYLE-0001", $"Style '{name}' requires a {(allowZero ? "nonnegative" : "positive")} px length.", value.Anchor);
                return null;
            }

            string? ReadString(IReadOnlyDictionary<string, LayoutValue> fields, string name)
            {
                if (!fields.TryGetValue(name, out LayoutValue? value)) return null;
                if (value.Scalar is string text) return text;
                Report("COPE-LAYOUT-STYLE-0001", $"Style field '{name}' requires a string.", value.Anchor);
                return null;
            }
        }

        private MachinaLength? Length(LayoutValue value)
        {
            return value.Scalar as MachinaLength?;
        }

        private static LayoutPropertyData? Property(IReadOnlyList<LayoutPropertyData> properties, string name)
            => properties.LastOrDefault(property => property.Identifier.Text == name);
        private static bool Has(IReadOnlyList<LayoutPropertyData> properties, string name) => Property(properties, name) is not null;
        private static LayoutValue? ObjectProperty(LayoutValue expression, string name) => expression.Fields?.GetValueOrDefault(name);
        private static string? String(LayoutValue? expression) => expression is { Scalar: string value } ? value : null;
        private static ExpressionSyntax? ObjectProperty(ObjectLiteralExpressionSyntax expression, string name)
            => expression.Properties.FirstOrDefault(property => property.NameToken.Text == name)?.ValueExpression;
        private MachinaSourceSpan Span(SyntaxNode node) { SyntaxToken token = FirstToken(node); return Span(token); }
        private MachinaSourceSpan Span(SyntaxToken token) => new(_sourcePath, token.Position, Math.Max(1, token.Text.Length));
        private static SyntaxToken FirstToken(SyntaxNode node) => node.GetChildren().OfType<SyntaxToken>().FirstOrDefault() ?? new SyntaxToken(SyntaxKind.BadToken, 0, string.Empty, null);
        private void Report(string id, string message, SyntaxToken token) => _diagnostics.Add(new Diagnostic(id, message, token.Position, Math.Max(1, token.Text.Length), _sourcePath));
        private void Report(string id, string message, MachinaSourceSpan source) => _diagnostics.Add(new Diagnostic(id, message, source.Start, source.Length, source.SourcePath));
    }
}
