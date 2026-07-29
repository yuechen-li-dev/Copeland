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
    MachinaSourceSpan Source);
public sealed record BoundLayoutDeclaration(string Name, string? Profile, BoundLayoutOrigin Origin, BoundLayoutNode Root, IReadOnlyDictionary<string, BoundLayoutNode> Slots);
public sealed record NormalizedLayoutNode(
    string Name,
    LayoutNodeKind Kind,
    string StableIdentity,
    NormalizedLayoutOrigin? Origin,
    NormalizedLayoutOriginRelation OriginRelation,
    IReadOnlyList<NormalizedLayoutNode> Children);
public sealed record NormalizedLayoutGraph(string LayoutName, NormalizedLayoutNode Root, IReadOnlyDictionary<string, string> SlotIdentities);
public sealed record LayoutReactArtifact(string Css, IReadOnlyDictionary<string, string> ClassesBySlot);
/// <summary>Deterministic typed TypeScript surface for semantic React attachment.</summary>
public sealed record LayoutReactProjection(string Css, string TypeScript, IReadOnlyDictionary<string, string> ClassesBySlot);
public sealed class LayoutDataCompilation(SyntaxTree syntaxTree, IReadOnlyList<Diagnostic> diagnostics, IReadOnlyDictionary<string, BoundLayoutDeclaration> layouts)
{
    public SyntaxTree SyntaxTree { get; } = syntaxTree;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    public IReadOnlyDictionary<string, BoundLayoutDeclaration> Layouts { get; } = layouts;
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
    public static LayoutDataCompilation Bind(SyntaxTree tree, string sourcePath, IReadOnlyDictionary<string, BoundLayoutDeclaration>? importedLayouts = null)
    {
        var binder = new Binder(tree, sourcePath, importedLayouts ?? new Dictionary<string, BoundLayoutDeclaration>(StringComparer.Ordinal));
        IReadOnlyDictionary<string, BoundLayoutDeclaration> layouts = binder.Bind();
        return new LayoutDataCompilation(tree, tree.Diagnostics.Concat(binder.Diagnostics).ToArray(), layouts);
    }

    public static NormalizedLayoutGraph Normalize(BoundLayoutDeclaration layout)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        BoundLayoutNode rootNode = layout.Root.Children.Count == 1 ? layout.Root.Children[0] : layout.Root;
        NormalizedLayoutNode root = NormalizeNode(
            rootNode,
            layout.Name,
            slots,
            new NormalizedLayoutOrigin(layout.Origin),
            NormalizedLayoutOriginRelation.DeclaredRoot);
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
        NormalizedLayoutOriginRelation originRelation)
    {
        string identity = path + "." + node.Name;
        slots.Add(node.Name, identity);
        return new NormalizedLayoutNode(
            node.Name,
            node.Kind,
            identity,
            origin,
            originRelation,
            node.Children.Select(child => NormalizeNode(child, identity, slots, null, OriginRelationForChild(node, child))).ToArray());
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
        private readonly Dictionary<string, BoundLayoutDeclaration> _layouts = new(StringComparer.Ordinal);
        private readonly HashSet<string> _binding = new(StringComparer.Ordinal);
        private readonly List<Diagnostic> _diagnostics = [];
        private readonly string _sourcePath;

        private readonly IReadOnlyDictionary<string, BoundLayoutDeclaration> _importedLayouts;

        public Binder(SyntaxTree tree, string sourcePath, IReadOnlyDictionary<string, BoundLayoutDeclaration> importedLayouts)
        {
            _sourcePath = sourcePath;
            _importedLayouts = importedLayouts;
            foreach (LayoutDeclarationSyntax layout in tree.Root.Members.OfType<LayoutDeclarationSyntax>())
            {
                if (!_syntax.TryAdd(layout.Identifier.Text, layout))
                {
                    Report("COPE-LAYOUT-DECLARATION-0001", $"Layout '{layout.Identifier.Text}' is already declared.", layout.Identifier);
                }
            }
        }

        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

        public IReadOnlyDictionary<string, BoundLayoutDeclaration> Bind()
        {
            foreach (LayoutDeclarationSyntax declaration in _syntax.Values.OrderBy(item => item.Identifier.Position)) _ = BindDeclaration(declaration);
            return _layouts;
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

            BoundLayoutDeclaration? result;
            if (declaration.ComposedLayout is not null)
            {
                if (_syntax.TryGetValue(declaration.ComposedLayout.Text, out LayoutDeclarationSyntax? baseSyntax))
                {
                    if (BindDeclaration(baseSyntax) is BoundLayoutDeclaration basis)
                    {
                        BoundLayoutNode root = ApplyOverrides(basis.Root, declaration.CompositionProperties);
                        BoundLayoutNode composedRoot = root with { Name = name };
                        result = new BoundLayoutDeclaration(name, declaration.Profile?.Text, origin, composedRoot, CollectSlots(composedRoot));
                    }
                    else result = null;
                }
                else if (_importedLayouts.TryGetValue(declaration.ComposedLayout.Text, out BoundLayoutDeclaration? imported))
                {
                    BoundLayoutNode root = ApplyOverrides(imported.Root, declaration.CompositionProperties);
                    BoundLayoutNode composedRoot = root with { Name = name };
                    result = new BoundLayoutDeclaration(name, declaration.Profile?.Text, origin, composedRoot, CollectSlots(composedRoot));
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
                BoundLayoutNode root = new(name, LayoutNodeKind.Overlay, BindDimensions(declaration.Properties), BindPositions(declaration.Properties), Gap(declaration.Properties), null, Padding(declaration.Properties), Style(declaration.Properties), declaration.Nodes.Select(node => BindNode(node, slots, null)).Where(node => node is not null).Cast<BoundLayoutNode>().ToArray(), Span(declaration));
                if (root.Children.Count != 1) slots[name] = root;
                result = new BoundLayoutDeclaration(name, declaration.Profile?.Text, origin, root, slots);
            }

            _binding.Remove(name);
            if (result is not null) _layouts.Add(name, result);
            return result;
        }

        private BoundLayoutOrigin? BindOrigin(LayoutDeclarationSyntax declaration)
        {
            if (declaration.Origin is null)
            {
                return null;
            }

            BoundLayoutCoordinate? x = Coordinate(declaration.Origin.X);
            BoundLayoutCoordinate? y = Coordinate(declaration.Origin.Y);
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

        private BoundLayoutNode? BindNode(LayoutNodeSyntax syntax, Dictionary<string, BoundLayoutNode> slots, LayoutNodeKind? parentKind)
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
            var children = syntax.Children.Select(child => BindNode(child, slots, kind)).Where(child => child is not null).Cast<BoundLayoutNode>().ToArray();
            var node = new BoundLayoutNode(name, kind, BindDimensions(syntax.Properties), BindPositions(syntax.Properties), Gap(syntax.Properties), Columns(syntax.Properties), Padding(syntax.Properties), Style(syntax.Properties), children, Span(syntax));
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
                    null => property.Identifier.Text is "width" or "height" or "frame" or "gap" or "padding" or "style",
                    LayoutNodeKind.Row or LayoutNodeKind.Column => property.Identifier.Text is "width" or "height" or "frame" or "gap" or "padding" or "style",
                    LayoutNodeKind.Grid => property.Identifier.Text is "x" or "y" or "position" or "width" or "height" or "frame" or "gap" or "padding" or "style" or "columns",
                    LayoutNodeKind.Anchor => property.Identifier.Text is "left" or "right" or "top" or "bottom" or "width" or "height" or "frame" or "gap" or "padding" or "style",
                    LayoutNodeKind.Overlay => property.Identifier.Text is "x" or "y" or "position" or "width" or "height" or "frame" or "gap" or "padding" or "style",
                    LayoutNodeKind.Slot => property.Identifier.Text is "x" or "y" or "position" or "width" or "height" or "frame" or "style",
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
        private MachinaSourceSpan Span(SyntaxNode node) { SyntaxToken token = FirstToken(node); return new MachinaSourceSpan(_sourcePath, token.Position, Math.Max(1, token.Text.Length)); }
        private static SyntaxToken FirstToken(SyntaxNode node) => node.GetChildren().OfType<SyntaxToken>().FirstOrDefault() ?? new SyntaxToken(SyntaxKind.BadToken, 0, string.Empty, null);
        private void Report(string id, string message, SyntaxToken token) => _diagnostics.Add(new Diagnostic(id, message, token.Position, Math.Max(1, token.Text.Length), _sourcePath));
    }
}
