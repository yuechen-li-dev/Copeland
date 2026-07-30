using Copeland.TS.Diagnostics;
using Copeland.TS.Mir.Machina;
using Copeland.TS.Syntax;

namespace Copeland.TS.MachinaSource;

/// <summary>Compiler-owned immutable spatial data, before backend projection.</summary>
public enum LayoutNodeKind { Row, Column, Grid, Anchor, Overlay, Slot }
public enum LayoutDimensionKind { Fixed, Fill, Fit }
public enum LayoutCoordinateUnit { Px, Ui }
public enum NormalizedLayoutOriginRelation { DeclaredRoot, FlowDerived, AnchorDerived, OverlayDerived }

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
    BoundPaintProperties? Paint = null)
{
    public BoundPaintProperties ResolvedPaint => Paint ?? BoundPaintProperties.Default;
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
public sealed record BoundLayoutSatisfaction(string ContractName, bool IsSatisfied, InferredLayoutShape InferredShape);
public sealed record BoundLayoutDeclaration(
    string Name,
    string? Profile,
    BoundLayoutOrigin Origin,
    BoundLayoutNode Root,
    IReadOnlyDictionary<string, BoundLayoutNode> Slots,
    BoundLayoutSatisfaction? Satisfaction = null,
    BoundLayerSet? LayerSet = null)
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
    NormalizedPaintOrder? ResolvedPaintOrder = null)
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
        IReadOnlyDictionary<string, BoundLayerSet>? importedLayerSets = null)
    {
        var binder = new Binder(
            tree,
            sourcePath,
            importedLayouts ?? new Dictionary<string, BoundLayoutDeclaration>(StringComparer.Ordinal),
            importedLayoutTypes ?? new Dictionary<string, BoundLayoutTypeDeclaration>(StringComparer.Ordinal),
            importedLayerSets ?? new Dictionary<string, BoundLayerSet>(StringComparer.Ordinal));
        IReadOnlyDictionary<string, BoundLayoutDeclaration> layouts = binder.Bind();
        return new LayoutDataCompilation(tree, tree.Diagnostics.Concat(binder.Diagnostics).ToArray(), layouts, binder.LayoutTypes, binder.LayerSets);
    }

    public static NormalizedLayoutGraph Normalize(BoundLayoutDeclaration layout)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        BoundLayoutNode rootNode = layout.Root.Children.Count == 1 ? layout.Root.Children[0] : layout.Root;
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
            new NormalizedPaintOrder(layerSet.RankOf(paint.Layer), paint.LocalZ, order));
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

    private static string CssCoordinate(BoundLayoutCoordinate coordinate)
    {
        string value = coordinate.Value.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture);
        return coordinate.Unit == LayoutCoordinateUnit.Px
            ? value + "px"
            : "calc(var(--machina-ui, 1px) * " + value + ")";
    }

    private static BoundLayoutNode ProjectionRoot(BoundLayoutNode layoutRoot)
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
        MachinaTrack? main = parentKind is null or LayoutNodeKind.Anchor or LayoutNodeKind.Overlay ? null : Track(node, parentKind == LayoutNodeKind.Row ? "height" : "width");
        MachinaTrack? cross = parentKind is null or LayoutNodeKind.Anchor or LayoutNodeKind.Overlay ? null : Track(node, parentKind == LayoutNodeKind.Row ? "width" : "height");
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

        private readonly IReadOnlyDictionary<string, BoundLayoutDeclaration> _importedLayouts;
        private readonly IReadOnlyDictionary<string, BoundLayoutTypeDeclaration> _importedLayoutTypes;
        private readonly IReadOnlyDictionary<string, BoundLayerSet> _importedLayerSets;

        public Binder(
            SyntaxTree tree,
            string sourcePath,
            IReadOnlyDictionary<string, BoundLayoutDeclaration> importedLayouts,
            IReadOnlyDictionary<string, BoundLayoutTypeDeclaration> importedLayoutTypes,
            IReadOnlyDictionary<string, BoundLayerSet> importedLayerSets)
        {
            _sourcePath = sourcePath;
            _importedLayouts = importedLayouts;
            _importedLayoutTypes = importedLayoutTypes;
            _importedLayerSets = importedLayerSets;
            foreach (LayerSetDeclarationSyntax layerSet in tree.Root.Members.OfType<LayerSetDeclarationSyntax>())
            {
                if (!_layerSyntax.TryAdd(layerSet.Identifier.Text, layerSet))
                {
                    Report("COPE-LAYOUT-LAYER-0003", $"Semantic layer set '{layerSet.Identifier.Text}' is already declared.", layerSet.Identifier);
                }
            }
            foreach (LayoutDeclarationSyntax layout in tree.Root.Members.OfType<LayoutDeclarationSyntax>())
            {
                if (!_syntax.TryAdd(layout.Identifier.Text, layout))
                {
                    Report("COPE-LAYOUT-DECLARATION-0001", $"Layout '{layout.Identifier.Text}' is already declared.", layout.Identifier);
                }
            }
            foreach (LayoutTypeDeclarationSyntax layoutType in tree.Root.Members.OfType<LayoutTypeDeclarationSyntax>())
            {
                if (!_typeSyntax.TryAdd(layoutType.Identifier.Text, layoutType))
                {
                    Report("COPE-LAYOUT-TYPE-0003", $"Layout type '{layoutType.Identifier.Text}' is already declared.", layoutType.Identifier);
                }
            }
            foreach (StreamDeclarationSyntax stream in tree.Root.Members.OfType<StreamDeclarationSyntax>())
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
            BoundLayerSet layerSet = ResolveLayerSet(declaration.Properties, declaration.Identifier, name);
            var slots = new Dictionary<string, BoundLayoutNode>(StringComparer.Ordinal);
            BoundLayoutNode[] children = BindStreamChildren(declaration.Nodes, declaration.Tables, slots, LayoutNodeKind.Column, layerSet, name, layerSet.Layers[0]);
            bool hasExplicitRoot = declaration.Nodes.Count + declaration.Tables.Count == 1
                && (declaration.Nodes.SingleOrDefault() is { KindToken: not null, Identifier.Text: "root" }
                    || declaration.Tables.SingleOrDefault() is { ContainerKindToken.Text: "overlay", Identifier.Text: "root" });
            if (hasExplicitRoot && declaration.Tables.Count == 1 && children.Length == 1)
            {
                children[0] = children[0] with
                {
                    Dimensions = BindDimensions(declaration.Properties),
                    Positions = BindPositions(declaration.Properties),
                    Padding = Padding(declaration.Properties),
                    Style = Style(declaration.Properties),
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
                    BindDimensions(declaration.Properties),
                    BindPositions(declaration.Properties),
                    Gap(declaration.Properties),
                    null,
                    Padding(declaration.Properties),
                    Style(declaration.Properties),
                    children,
                    Span(declaration),
                    BoundPaintProperties.Default);
                rootChildren = [implicitRoot];
            }
            var root = new BoundLayoutNode(
                name,
                LayoutNodeKind.Overlay,
                BindDimensions(declaration.Properties),
                BindPositions(declaration.Properties),
                MachinaLength.Pixels(0),
                null,
                MachinaInsets.None,
                MachinaStyle.Empty,
                rootChildren,
                Span(declaration),
                BoundPaintProperties.Default);
            var layout = new BoundLayoutDeclaration(name, null, origin, root, slots, LayerSet: layerSet);

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
                var slot = new BoundLayoutNode(syntax.Identifier.Text, LayoutNodeKind.Slot, BindDimensions(syntax.Properties), BindPositions(syntax.Properties), MachinaLength.Pixels(0), null, Padding(syntax.Properties), Style(syntax.Properties), [], Span(syntax), BindPaint(syntax.Properties, layerSet, layoutName, inheritedLayer));
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
            var node = new BoundLayoutNode(syntax.Identifier.Text, kind, BindDimensions(syntax.Properties), BindPositions(syntax.Properties), Gap(syntax.Properties), Columns(syntax.Properties), Padding(syntax.Properties), Style(syntax.Properties), children, Span(syntax), paint);
            if (parentKind is LayoutNodeKind.Anchor or LayoutNodeKind.Overlay && kind != LayoutNodeKind.Anchor && (!HasFixedDimension(node, "width") || !HasFixedDimension(node, "height")))
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

            string[] required = ["name", "content", "x", "y", "width", "height"];
            string[] supported = ["name", "content", "x", "y", "width", "height", "layer", "z"];
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
            foreach (string column in required)
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
                    BindPaint(properties, layerSet, layoutName, inheritedLayer));
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
                SyntaxToken cellToken = FirstToken(row.Cells[index]);
                SyntaxToken identifier = new(SyntaxKind.IdentifierToken, cellToken.Position, column, null);
                properties.Add(new LayoutPropertySyntax(identifier, table.CsvKeyword, row.Cells[index], row.SemicolonToken));
            }
            return properties;
        }

        private bool IsTableDimension(ExpressionSyntax expression)
            => expression is NameExpressionSyntax { IdentifierToken.Text: "fill" or "fit" } || Length(expression) is not null;

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
            if (declaration.Profile is not null && !string.Equals(declaration.Profile.Text, "page", StringComparison.Ordinal))
            {
                Report("COPE-LAYOUT-PROFILE-0001", $"Layout profile '{declaration.Profile.Text}' is not supported in M0.", declaration.Profile);
            }

            BoundLayoutOrigin? origin = BindOrigin(declaration);
            if (origin is null)
            {
                _binding.Remove(name);
                return null;
            }

            BoundLayerSet declaredLayerSet = ResolveLayerSet(declaration.Properties.Count > 0 ? declaration.Properties : declaration.CompositionProperties, declaration.Identifier, name);
            BoundLayoutDeclaration? result;
            if (declaration.ComposedLayout is not null)
            {
                if (_syntax.TryGetValue(declaration.ComposedLayout.Text, out LayoutDeclarationSyntax? baseSyntax))
                {
                    if (BindDeclaration(baseSyntax) is BoundLayoutDeclaration basis)
                    {
                        BoundLayoutNode root = ApplyOverrides(basis.Root, declaration.CompositionProperties);
                        BoundLayoutNode composedRoot = root with { Name = name };
                        if (declaration.CompositionProperties.Any(property => property.Identifier.Text == "layers")
                            && !string.Equals(basis.ResolvedLayerSet.StableIdentity, declaredLayerSet.StableIdentity, StringComparison.Ordinal))
                        {
                            Report("COPE-LAYOUT-LAYER-0006", $"Composed layout '{name}' cannot replace layer set '{basis.ResolvedLayerSet.Name}' with '{declaredLayerSet.Name}'. Nested composition stays in its containing layer space.", declaration.Identifier);
                        }
                        result = new BoundLayoutDeclaration(name, declaration.Profile?.Text, origin, composedRoot, CollectSlots(composedRoot), LayerSet: basis.ResolvedLayerSet);
                    }
                    else result = null;
                }
                else if (_importedLayouts.TryGetValue(declaration.ComposedLayout.Text, out BoundLayoutDeclaration? imported))
                {
                    BoundLayoutNode root = ApplyOverrides(imported.Root, declaration.CompositionProperties);
                    BoundLayoutNode composedRoot = root with { Name = name };
                    if (declaration.CompositionProperties.Any(property => property.Identifier.Text == "layers")
                        && !string.Equals(imported.ResolvedLayerSet.StableIdentity, declaredLayerSet.StableIdentity, StringComparison.Ordinal))
                    {
                        Report("COPE-LAYOUT-LAYER-0006", $"Composed layout '{name}' cannot replace layer set '{imported.ResolvedLayerSet.Name}' with '{declaredLayerSet.Name}'. Nested composition stays in its containing layer space.", declaration.Identifier);
                    }
                    result = new BoundLayoutDeclaration(name, declaration.Profile?.Text, origin, composedRoot, CollectSlots(composedRoot), LayerSet: imported.ResolvedLayerSet);
                }
                else
                {
                    Report("COPE-LAYOUT-COMPOSE-0002", $"Composed layout '{declaration.ComposedLayout.Text}' was not found.", declaration.ComposedLayout);
                    result = null;
                }
            }
            else
            {
                var slots = new Dictionary<string, BoundLayoutNode>(StringComparer.Ordinal);
                ValidateProperties(declaration.Properties, null);
                BoundLayoutNode root = new(name, LayoutNodeKind.Overlay, BindDimensions(declaration.Properties), BindPositions(declaration.Properties), Gap(declaration.Properties), null, Padding(declaration.Properties), Style(declaration.Properties), declaration.Nodes.Select(node => BindNode(node, slots, null, declaredLayerSet, name, declaredLayerSet.Layers[0])).Where(node => node is not null).Cast<BoundLayoutNode>().ToArray(), Span(declaration), new BoundPaintProperties(declaredLayerSet.Layers[0], 0));
                if (root.Children.Count != 1) slots[name] = root;
                result = new BoundLayoutDeclaration(name, declaration.Profile?.Text, origin, root, slots, LayerSet: declaredLayerSet);
            }

            _binding.Remove(name);
            if (result is not null && declaration.ContractIdentifier is not null)
            {
                result = CheckSatisfaction(declaration, result);
            }
            if (result is not null) _layouts.Add(name, result);
            return result;
        }

        private BoundLayoutDeclaration CheckSatisfaction(LayoutDeclarationSyntax declaration, BoundLayoutDeclaration layout)
            => CheckSatisfaction(declaration.Identifier.Text, declaration.ContractIdentifier!, layout);

        private BoundLayoutDeclaration CheckSatisfaction(string layoutName, SyntaxToken contractIdentifier, BoundLayoutDeclaration layout)
        {
            string contractName = contractIdentifier.Text;
            if (!_layoutTypes.TryGetValue(contractName, out BoundLayoutTypeDeclaration? contract)
                && !_importedLayoutTypes.TryGetValue(contractName, out contract))
            {
                Report("COPE-LAYOUT-TYPE-0009", $"Layout '{layout.Name}' satisfies '{contractName}', but no layout type with that name is visible.", contractIdentifier);
                return layout with { Satisfaction = new BoundLayoutSatisfaction(contractName, false, InferShape(layout.Root)) };
            }

            bool satisfied = CompareNodes(layout.Name, contract.Name, layout.Root, contract.Root, layout.Name);
            return layout with { Satisfaction = new BoundLayoutSatisfaction(contract.Name, satisfied, InferShape(layout.Root)) };
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

        private BoundLayoutNode? BindNode(LayoutNodeSyntax syntax, Dictionary<string, BoundLayoutNode> slots, LayoutNodeKind? parentKind, BoundLayerSet layerSet, string layoutName, string inheritedLayer)
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
            var node = new BoundLayoutNode(name, kind, BindDimensions(syntax.Properties), BindPositions(syntax.Properties), Gap(syntax.Properties), Columns(syntax.Properties), Padding(syntax.Properties), Style(syntax.Properties), children, Span(syntax), paint);
            if (parentKind is LayoutNodeKind.Anchor or LayoutNodeKind.Overlay && kind != LayoutNodeKind.Anchor && (!HasFixedDimension(node, "width") || !HasFixedDimension(node, "height")))
            {
                Report("COPE-LAYOUT-FRAME-0002", $"'{name}' requires fixed width and height inside {parentKind.Value.ToString().ToLowerInvariant()} composition.", syntax.Identifier);
            }
            if (kind == LayoutNodeKind.Anchor) ValidateAnchor(node, syntax.Identifier);
            slots.TryAdd(name, node);
            return node;
        }

        private BoundLayoutNode ApplyOverrides(BoundLayoutNode root, IReadOnlyList<LayoutPropertySyntax> properties)
        {
            var dimensions = root.Dimensions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach ((string key, BoundLayoutDimension value) in BindDimensions(properties)) dimensions[key] = value;
            return root with { Dimensions = dimensions, Gap = Has(properties, "gap") ? Gap(properties) : root.Gap, Padding = Has(properties, "padding") ? Padding(properties) : root.Padding, Style = Has(properties, "style") ? Style(properties) : root.Style };
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

        private void ValidateProperties(IReadOnlyList<LayoutPropertySyntax> properties, LayoutNodeKind? kind)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (LayoutPropertySyntax property in properties)
            {
                if (!names.Add(property.Identifier.Text))
                {
                    Report("COPE-LAYOUT-PROPERTY-0001", $"Layout property '{property.Identifier.Text}' is declared more than once.", property.Identifier);
                    continue;
                }
                bool allowed = kind switch
                {
                    null => property.Identifier.Text is "width" or "height" or "frame" or "gap" or "padding" or "style" or "layers",
                    LayoutNodeKind.Row or LayoutNodeKind.Column => property.Identifier.Text is "width" or "height" or "frame" or "gap" or "padding" or "style" or "layer" or "z",
                    LayoutNodeKind.Grid => property.Identifier.Text is "x" or "y" or "position" or "width" or "height" or "frame" or "gap" or "padding" or "style" or "columns" or "layer" or "z",
                    LayoutNodeKind.Anchor => property.Identifier.Text is "left" or "right" or "top" or "bottom" or "width" or "height" or "frame" or "gap" or "padding" or "style" or "layer" or "z",
                    LayoutNodeKind.Overlay => property.Identifier.Text is "x" or "y" or "position" or "width" or "height" or "frame" or "gap" or "padding" or "style" or "layer" or "z",
                    LayoutNodeKind.Slot => property.Identifier.Text is "x" or "y" or "position" or "width" or "height" or "frame" or "style" or "layer" or "z",
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
                slots.Add(node.Name, node);
                foreach (BoundLayoutNode child in node.Children) Add(child);
            }
        }

        private IReadOnlyDictionary<string, BoundLayoutDimension> BindDimensions(IReadOnlyList<LayoutPropertySyntax> properties)
        {
            var result = new Dictionary<string, BoundLayoutDimension>(StringComparer.Ordinal);
            foreach (LayoutPropertySyntax property in properties.Where(property => property.Identifier.Text is "width" or "height"))
            {
                BoundLayoutDimension? dimension = Dimension(property.Value);
                if (dimension is not null) result[property.Identifier.Text] = dimension;
            }
            if (Property(properties, "frame")?.Value is ObjectLiteralExpressionSyntax frame)
            {
                foreach (string name in new[] { "width", "height" })
                {
                    if (ObjectProperty(frame, name) is ExpressionSyntax value && Dimension(value) is BoundLayoutDimension dimension)
                    {
                        result[name] = dimension;
                    }
                }
            }
            return result;
        }

        private BoundLayerSet ResolveLayerSet(IReadOnlyList<LayoutPropertySyntax> properties, SyntaxToken token, string layoutName)
        {
            LayoutPropertySyntax? property = Property(properties, "layers");
            if (property is null) return BoundLayerSet.Default;
            if (property.Value is not NameExpressionSyntax name)
            {
                Report("COPE-LAYOUT-LAYER-0007", $"Layout '{layoutName}' must name a declared semantic layer set in 'layers:'.", property.Identifier);
                return BoundLayerSet.Default;
            }
            if (_layerSets.TryGetValue(name.IdentifierToken.Text, out BoundLayerSet? local)) return local;
            if (_importedLayerSets.TryGetValue(name.IdentifierToken.Text, out BoundLayerSet? imported)) return imported;
            Report("COPE-LAYOUT-LAYER-0008", $"Layout '{layoutName}' uses unknown semantic layer set '{name.IdentifierToken.Text}'.", name.IdentifierToken);
            return BoundLayerSet.Default;
        }

        private BoundPaintProperties BindPaint(IReadOnlyList<LayoutPropertySyntax> properties, BoundLayerSet layerSet, string layoutName, string inheritedLayer)
        {
            string layer = inheritedLayer;
            if (Property(properties, "layer") is { } layerProperty)
            {
                if (layerProperty.Value is NameExpressionSyntax name)
                {
                    layer = name.IdentifierToken.Text;
                    if (layerSet.RankOf(layer) < 0)
                    {
                        Report("COPE-LAYOUT-LAYER-0001", $"Layout '{layoutName}' names unknown semantic layer '{layer}' in layer set '{layerSet.Name}'.", name.IdentifierToken);
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

        private static bool TryStaticInteger(ExpressionSyntax expression, out int value)
        {
            if (expression is LiteralExpressionSyntax { LiteralToken.Value: int integer })
            {
                value = integer;
                return true;
            }
            if (expression is UnaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.MinusToken, Operand: LiteralExpressionSyntax { LiteralToken.Value: int negativeInteger } })
            {
                value = -negativeInteger;
                return true;
            }
            value = 0;
            return false;
        }

        private IReadOnlyDictionary<string, MachinaLength> BindPositions(IReadOnlyList<LayoutPropertySyntax> properties)
        {
            var result = new Dictionary<string, MachinaLength>(StringComparer.Ordinal);
            foreach (LayoutPropertySyntax property in properties.Where(property => property.Identifier.Text is "x" or "y" or "left" or "right" or "top" or "bottom"))
            {
                MachinaLength? value = Length(property.Value);
                if (value is not null) result[property.Identifier.Text] = value.Value;
            }
            foreach (string recordName in new[] { "position", "frame" })
            {
                if (Property(properties, recordName)?.Value is not ObjectLiteralExpressionSyntax record) continue;
                foreach (string name in new[] { "x", "y", "left", "right", "top", "bottom" })
                {
                    if (ObjectProperty(record, name) is ExpressionSyntax value && Length(value) is MachinaLength length)
                    {
                        result[name] = length;
                    }
                }
            }
            return result;
        }

        private BoundLayoutDimension? Dimension(ExpressionSyntax expression)
        {
            if (expression is NameExpressionSyntax name && name.IdentifierToken.Text == "fill") return new BoundLayoutDimension(LayoutDimensionKind.Fill);
            if (expression is NameExpressionSyntax fit && fit.IdentifierToken.Text == "fit") return new BoundLayoutDimension(LayoutDimensionKind.Fit);
            MachinaLength? length = Length(expression);
            if (length is not null) return new BoundLayoutDimension(LayoutDimensionKind.Fixed, length);
            Report("COPE-LAYOUT-DIMENSION-0001", "A layout dimension must be a length, 'fill', or 'fit'.", FirstToken(expression));
            return null;
        }

        private MachinaLength Gap(IReadOnlyList<LayoutPropertySyntax> properties)
            => Property(properties, "gap") is { } property && Length(property.Value) is MachinaLength value ? value : MachinaLength.Pixels(0);

        private int? Columns(IReadOnlyList<LayoutPropertySyntax> properties)
        {
            LayoutPropertySyntax? property = Property(properties, "columns");
            if (property?.Value is LiteralExpressionSyntax { LiteralToken.Value: int value } && value > 0) return value;
            return null;
        }

        private MachinaInsets Padding(IReadOnlyList<LayoutPropertySyntax> properties)
            => Property(properties, "padding") is { } property && Length(property.Value) is MachinaLength value ? MachinaInsets.All(value.Px) : MachinaInsets.None;

        private MachinaStyle Style(IReadOnlyList<LayoutPropertySyntax> properties)
        {
            LayoutPropertySyntax? property = Property(properties, "style");
            if (property is null) return MachinaStyle.Empty;
            if (property.Value is not ObjectLiteralExpressionSyntax style)
            {
                Report("COPE-LAYOUT-STYLE-0001", "Layout style must be an immutable record literal in M0.", property.Identifier);
                return MachinaStyle.Empty;
            }
            Dictionary<string, ExpressionSyntax> values = style.Properties.ToDictionary(property => property.NameToken.Text, property => property.ValueExpression, StringComparer.Ordinal);
            string? fill = String(values.GetValueOrDefault("fill"));
            MachinaSurfaceStyle? surface = fill is null ? null : new MachinaSurfaceStyle(Fill: fill);
            MachinaBorderStyle? border = values.GetValueOrDefault("border") is ObjectLiteralExpressionSyntax borderRecord
                ? new MachinaBorderStyle(ObjectProperty(borderRecord, "width") is ExpressionSyntax width ? Length(width) : null, String(ObjectProperty(borderRecord, "color")), String(ObjectProperty(borderRecord, "style")))
                : null;
            return surface is null && border is null ? MachinaStyle.Empty : new MachinaStyle(Surface: surface, Border: border);
        }

        private MachinaLength? Length(ExpressionSyntax expression)
        {
            try
            {
                return expression switch
                {
                    LiteralExpressionSyntax { LiteralToken.Value: LengthLiteralTokenValue length } => length.Unit == "px" ? MachinaLength.Pixels(length.Value) : MachinaLength.Normalized(length.Value),
                    UnaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.MinusToken } unary when Length(unary.Operand) is MachinaLength value => -value,
                    BinaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.PlusToken } binary when Length(binary.Left) is MachinaLength left && Length(binary.Right) is MachinaLength right => left + right,
                    BinaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.MinusToken } binary when Length(binary.Left) is MachinaLength left && Length(binary.Right) is MachinaLength right => left - right,
                    _ => null,
                };
            }
            catch (MachinaLayoutException exception)
            {
                Report(exception.Code, exception.Message, FirstToken(expression));
                return null;
            }
        }

        private static LayoutPropertySyntax? Property(IReadOnlyList<LayoutPropertySyntax> properties, string name)
            => properties.LastOrDefault(property => property.Identifier.Text == name);
        private static bool Has(IReadOnlyList<LayoutPropertySyntax> properties, string name) => Property(properties, name) is not null;
        private static ExpressionSyntax? ObjectProperty(ObjectLiteralExpressionSyntax expression, string name) => expression.Properties.FirstOrDefault(property => property.NameToken.Text == name)?.ValueExpression;
        private static string? String(ExpressionSyntax? expression) => expression is LiteralExpressionSyntax { LiteralToken.Value: string value } ? value : null;
        private MachinaSourceSpan Span(SyntaxNode node) { SyntaxToken token = FirstToken(node); return Span(token); }
        private MachinaSourceSpan Span(SyntaxToken token) => new(_sourcePath, token.Position, Math.Max(1, token.Text.Length));
        private static SyntaxToken FirstToken(SyntaxNode node) => node.GetChildren().OfType<SyntaxToken>().FirstOrDefault() ?? new SyntaxToken(SyntaxKind.BadToken, 0, string.Empty, null);
        private void Report(string id, string message, SyntaxToken token) => _diagnostics.Add(new Diagnostic(id, message, token.Position, Math.Max(1, token.Text.Length), _sourcePath));
        private void Report(string id, string message, MachinaSourceSpan source) => _diagnostics.Add(new Diagnostic(id, message, source.Start, source.Length, source.SourcePath));
    }
}
