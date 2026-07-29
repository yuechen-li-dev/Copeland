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

    private static string ToJavaScript(string typeScript)
        => typeScript.Replace(" as const", string.Empty, StringComparison.Ordinal);

    private static string Sanitize(string value)
        => string.Concat(value.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_'));
}
