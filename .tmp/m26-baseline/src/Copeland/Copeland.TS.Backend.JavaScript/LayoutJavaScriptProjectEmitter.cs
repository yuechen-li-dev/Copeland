using Copeland.TS.Compiler;
using Copeland.TS.MachinaSource;
using Copeland.TS.Semantics.Bound;

namespace Copeland.TS.Backend.JavaScript;

/// <summary>
/// Adds compiler-bound layout projection files to the ordinary JavaScript
/// project artifact set. Layouts are data: this emits CSS and constants only.
/// </summary>
public static class LayoutJavaScriptProjectEmitter
{
    public static JavaScriptProjectCompilation AddLayouts(
        JavaScriptProjectCompilation baseEmission,
        IReadOnlyList<CopelandProjectModuleCompilation> modules)
    {
        var files = new Dictionary<string, string>(baseEmission.Files, StringComparer.Ordinal);
        var diagnostics = new List<JavaScriptDiagnostic>(baseEmission.Diagnostics);
        var css = new System.Text.StringBuilder();
        var exportedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (CopelandProjectModuleCompilation module in modules.OrderBy(module => module.LogicalPath, StringComparer.Ordinal))
        {
            IEnumerable<BoundLayoutBinding> privatePresentations = module.BoundCompilation is null
                ? Enumerable.Empty<BoundLayoutBinding>()
                : module.BoundCompilation.Program.LayoutBindings
                    .Where(binding => binding.IsPrivate)
                    .OrderBy(binding => binding.Layout.StableIdentity, StringComparer.Ordinal);
            foreach (BoundLayoutBinding privatePresentation in privatePresentations)
            {
                AppendPrivatePresentationCss(css, privatePresentation);
            }

            IEnumerable<BoundLayoutDeclaration> layouts = module.BoundCompilation is null
                ? Enumerable.Empty<BoundLayoutDeclaration>()
                : module.BoundCompilation.Program.Layouts.OrderBy(layout => layout.Name, StringComparer.Ordinal);
            foreach (BoundLayoutDeclaration layout in layouts)
            {
                if (!HasFixedRoot(layout)) continue;
                string moduleName = "generated/layouts/" + Sanitize(module.LogicalPath) + "-" + Sanitize(layout.Name) + ".js";
                if (!exportedNames.Add(module.LogicalPath + "::" + layout.Name))
                {
                    diagnostics.Add(new JavaScriptDiagnostic("COPE-LAYOUT-EMIT-0001", $"Layout projection module collision for '{layout.Name}'."));
                    continue;
                }
                try
                {
                    LayoutReactProjection projection = LayoutDataCompiler.ProjectReact(layout);
                    files.Add(moduleName, ToJavaScript(projection.TypeScript));
                    css.Append("/* ").Append(module.LogicalPath).Append(" :: ").Append(layout.Name).Append(" */\n")
                        .Append(projection.Css).Append('\n');
                    foreach (BoundLayoutBinding binding in module.BoundCompilation!.Program.LayoutBindings.Where(binding => binding.Layout.Name == layout.Name))
                    {
                        foreach (BoundStreamCollection collection in binding.Collections)
                        {
                            int columns = collection.Region.Columns ?? 1;
                            css.Append(".m-stream-collection-").Append(Sanitize(layout.Name)).Append('-').Append(Sanitize(collection.Region.Name)).Append(" {\n")
                                .Append("  display: grid;\n")
                                .Append("  grid-template-columns: repeat(").Append(columns).Append(", minmax(0, 1fr));\n")
                                .Append("  gap: ").Append(collection.Region.Gap.Px.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("px;\n")
                                .Append("}\n\n");
                        }
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(new JavaScriptDiagnostic("COPE-LAYOUT-EMIT-0002", $"Could not project layout '{layout.Name}': {exception.Message}"));
                }
            }
        }

        if (css.Length > 0) files.Add("generated/layouts.css", css.ToString());
        return new JavaScriptProjectCompilation(files, diagnostics);
    }

    private static bool HasFixedRoot(BoundLayoutDeclaration layout)
        => layout.Root.Dimensions.TryGetValue("width", out BoundLayoutDimension? width)
           && layout.Root.Dimensions.TryGetValue("height", out BoundLayoutDimension? height)
           && width.Kind == LayoutDimensionKind.Fixed
           && height.Kind == LayoutDimensionKind.Fixed;

    private static void AppendPrivatePresentationCss(System.Text.StringBuilder css, BoundLayoutBinding binding)
    {
        if (IsTransparentSingleContentHost(binding.Realization.Root))
        {
            AppendTransparentHost(binding.Realization.Root);
            return;
        }

        AppendNode(binding.Realization.Root, isRoot: true);

        void AppendTransparentHost(BoundLayoutNode node)
        {
            if (binding.Realization.ClassesByNode.TryGetValue(node.Name, out string? className))
            {
                css.Append('.').Append(className).Append(" {\n")
                    .Append("  display: contents;\n")
                    .Append("}\n\n");
            }

            foreach (BoundLayoutNode child in node.Children)
            {
                AppendTransparentHost(child);
            }
        }

        void AppendNode(BoundLayoutNode node, bool isRoot)
        {
            if (!binding.Realization.ClassesByNode.TryGetValue(node.Name, out string? className))
            {
                return;
            }

            css.Append('.').Append(className).Append(" {\n")
                .Append("  box-sizing: border-box;\n");
            AppendDimension("width");
            AppendDimension("height");
            switch (node.Kind)
            {
                case LayoutNodeKind.Row:
                    css.Append("  display: flex;\n  flex-direction: row;\n");
                    break;
                case LayoutNodeKind.Column:
                    css.Append("  display: flex;\n  flex-direction: column;\n");
                    break;
                case LayoutNodeKind.Grid:
                    css.Append("  display: grid;\n  grid-template-columns: repeat(")
                        .Append(node.Columns ?? 1)
                        .Append(", minmax(0, 1fr));\n");
                    break;
                case LayoutNodeKind.Anchor:
                case LayoutNodeKind.Overlay:
                    css.Append("  position: relative;\n");
                    break;
                case LayoutNodeKind.Slot:
                    if (!isRoot)
                    {
                        css.Append("  min-width: 0;\n  min-height: 0;\n");
                    }
                    break;
            }
            css.Append("}\n\n");

            foreach (BoundLayoutNode child in node.Children)
            {
                AppendNode(child, isRoot: false);
            }

            void AppendDimension(string name)
            {
                BoundLayoutDimension dimension = node.Dimensions.GetValueOrDefault(name)
                    ?? new BoundLayoutDimension(LayoutDimensionKind.Fill);
                string value = dimension.Kind switch
                {
                    LayoutDimensionKind.Fill => "100%",
                    LayoutDimensionKind.Fit => "auto",
                    LayoutDimensionKind.Fixed => dimension.Length!.Value.Px.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px",
                    _ => throw new InvalidOperationException("Unknown layout dimension."),
                };
                css.Append("  ").Append(name).Append(": ").Append(value).Append(";\n");
            }
        }
    }

    private static bool IsTransparentSingleContentHost(BoundLayoutNode node)
        => node.Kind is LayoutNodeKind.Column or LayoutNodeKind.Row
           && node.Children.Count == 1
           && node.Children[0].Kind == LayoutNodeKind.Slot
           && node.Dimensions.Values.All(dimension => dimension.Kind == LayoutDimensionKind.Fill)
           && node.Children[0].Dimensions.Values.All(dimension => dimension.Kind == LayoutDimensionKind.Fill);

    private static string ToJavaScript(string typeScript)
        => typeScript.Replace(" as const", string.Empty, StringComparison.Ordinal);

    private static string Sanitize(string value)
        => string.Concat(value.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_'));
}
