using Copeland.TS.Compiler;
using Copeland.TS.MachinaSource;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Mir.Machina;
using System.Globalization;
using System.Text;

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
        var css = new StringBuilder();
        var exportedNames = new HashSet<string>(StringComparer.Ordinal);
        var projectedPresentations = new HashSet<string>(StringComparer.Ordinal);

        foreach (CopelandProjectModuleCompilation module in modules.OrderBy(module => module.LogicalPath, StringComparer.Ordinal))
        {
            IEnumerable<BoundLayoutBinding> privatePresentations = module.BoundCompilation is null
                ? Enumerable.Empty<BoundLayoutBinding>()
                : module.BoundCompilation.Program.LayoutBindings
                    .Where(binding => binding.IsContributionBinding
                        ? !HasFixedRoot(binding.Layout.BoundLayout!)
                        : binding.IsPrivate)
                    .OrderBy(binding => binding.Layout.StableIdentity, StringComparer.Ordinal);
            foreach (BoundLayoutBinding privatePresentation in privatePresentations)
            {
                if (!projectedPresentations.Add(privatePresentation.Layout.StableIdentity)) continue;
                var presentationCss = new StringBuilder();
                try
                {
                    AppendPrivatePresentationCss(presentationCss, privatePresentation);
                    css.Append(presentationCss);
                }
                catch (MachinaLayoutException exception)
                {
                    diagnostics.Add(new JavaScriptDiagnostic(exception.Code, exception.Message));
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(new JavaScriptDiagnostic("COPE-LAYOUT-EMIT-0002",
                        $"Could not project private layout '{privatePresentation.Layout.Name}': {exception.Message}"));
                }
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
        bool offsetOrigin = binding.IsContributionBinding
            && (binding.Layout.BoundLayout!.Origin.X.Value != 0 || binding.Layout.BoundLayout.Origin.Y.Value != 0);
        if (IsTransparentSingleContentHost(binding.Realization.Root) && !offsetOrigin)
        {
            AppendTransparentHost(binding.Realization.Root);
            return;
        }

        AppendNode(binding.Realization.Root, parentKind: null);

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

        void AppendNode(BoundLayoutNode node, LayoutNodeKind? parentKind)
        {
            if (!binding.Realization.ClassesByNode.TryGetValue(node.Name, out string? className))
            {
                return;
            }

            css.Append('.').Append(className).Append(" {\n")
                .Append("  box-sizing: border-box;\n");
            if (parentKind is null && offsetOrigin)
            {
                BoundLayoutOrigin origin = binding.Layout.BoundLayout!.Origin;
                css.Append("  position: relative;\n")
                    .Append("  left: ").Append(LayoutDataCompiler.CssCoordinate(origin.X)).Append(";\n")
                    .Append("  top: ").Append(LayoutDataCompiler.CssCoordinate(origin.Y)).Append(";\n");
            }
            string? mainAxis = parentKind switch
            {
                LayoutNodeKind.Row => "width",
                LayoutNodeKind.Column => "height",
                _ => null
            };
            AppendDimension("width", mainAxis);
            AppendDimension("height", mainAxis);
            css.Append("  min-width: 0;\n  min-height: 0;\n");
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
            }
            if (node.Kind is LayoutNodeKind.Row or LayoutNodeKind.Column or LayoutNodeKind.Grid)
            {
                css.Append("  gap: ").Append(LayoutLengthCss(node.Gap)).Append(";\n");
            }
            MachinaBrowserLowerer.AppendStyleDeclarations(node.Style, css);
            if (node.Padding != MachinaInsets.None)
            {
                css.Append("  padding: ").Append(Format(node.Padding.Top)).Append("px ")
                    .Append(Format(node.Padding.Right)).Append("px ")
                    .Append(Format(node.Padding.Bottom)).Append("px ")
                    .Append(Format(node.Padding.Left)).Append("px;\n");
            }
            css.Append("  overflow-x: ").Append(OverflowCss(node.ResolvedOverflow.X)).Append(";\n")
                .Append("  overflow-y: ").Append(OverflowCss(node.ResolvedOverflow.Y)).Append(";\n");
            css.Append("}\n\n");

            foreach (BoundLayoutNode child in node.Children)
            {
                AppendNode(child, node.Kind);
            }

            void AppendDimension(string name, string? mainAxis)
            {
                BoundLayoutDimension dimension = node.Dimensions.GetValueOrDefault(name)
                    ?? new BoundLayoutDimension(LayoutDimensionKind.Fill);
                if (name == mainAxis)
                {
                    string flex = dimension.Kind switch
                    {
                        LayoutDimensionKind.Fill => "1 1 0px",
                        LayoutDimensionKind.Fit => "0 0 auto",
                        LayoutDimensionKind.Fixed => "0 0 " + LayoutLengthCss(dimension.Length!.Value),
                        _ => throw new InvalidOperationException("Unknown layout dimension.")
                    };
                    css.Append("  flex: ").Append(flex).Append(";\n");
                }
                string value = dimension.Kind switch
                {
                    LayoutDimensionKind.Fill when name == mainAxis => "auto",
                    LayoutDimensionKind.Fill => "100%",
                    LayoutDimensionKind.Fit => "auto",
                    LayoutDimensionKind.Fixed => LayoutLengthCss(dimension.Length!.Value),
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
           && node.Children[0].Dimensions.Values.All(dimension => dimension.Kind == LayoutDimensionKind.Fill)
           && IsTransparentBox(node)
           && IsTransparentBox(node.Children[0]);

    private static bool IsTransparentBox(BoundLayoutNode node)
    {
        return node.Padding == MachinaInsets.None
            && node.Gap == MachinaLength.Pixels(0)
            && node.Style == MachinaStyle.Empty
            && node.ResolvedOverflow == BoundBoxOverflowPolicy.Visible
            && node.Positions.Count == 0
            && node.TextFit is null
            && node.ResolvedPaint == BoundPaintProperties.Default;
    }

    private static string Format(double value)
    {
        return value.ToString("0.################", CultureInfo.InvariantCulture);
    }

    private static string LayoutLengthCss(MachinaLength length)
    {
        if (length.Ui == 0)
        {
            return Format(length.Px) + "px";
        }
        string percent = Format(length.Ui * 100) + "%";
        if (length.Px == 0)
        {
            return percent;
        }
        string sign = length.Px < 0 ? " - " : " + ";
        return "calc(" + percent + sign + Format(Math.Abs(length.Px)) + "px)";
    }

    private static string OverflowCss(LayoutOverflowAxis axis)
    {
        return axis switch
        {
            LayoutOverflowAxis.Visible => "visible",
            LayoutOverflowAxis.Clip => "hidden",
            LayoutOverflowAxis.Auto => "auto",
            LayoutOverflowAxis.Scroll => "scroll",
            _ => throw new InvalidOperationException("Unknown overflow axis.")
        };
    }

    private static string ToJavaScript(string typeScript)
        => typeScript.Replace(" as const", string.Empty, StringComparison.Ordinal);

    private static string Sanitize(string value)
        => string.Concat(value.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_'));
}
