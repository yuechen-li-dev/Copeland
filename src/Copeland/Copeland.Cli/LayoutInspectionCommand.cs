using System.Text.Json;
using Copeland.TS.Compiler;
using Copeland.TS.Diagnostics;
using Copeland.TS.MachinaSource;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.Cli;

/// <summary>CLI projection of compiler-normalized layout facts. No backend is invoked.</summary>
internal static class LayoutInspectionCommand
{
    private const int Success = 0;
    private const int CompileFailure = 1;
    private const int UsageFailure = 2;
    private const int FileFailure = 3;

    public static int Run(string[] args)
    {
        if (args.Length < 3 || args[1] != "inspect")
        {
            return Usage("COPE-LAYOUT-INSPECT-0001", "Usage: tscl layout inspect <layout|module::layout> --source <entry.ts> [--json].");
        }

        string target = args[2];
        string? sourcePath = null;
        bool json = false;
        for (int index = 3; index < args.Length; index += 1)
        {
            switch (args[index])
            {
                case "--source" when index + 1 < args.Length:
                    sourcePath = args[++index];
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return Usage("COPE-LAYOUT-INSPECT-0002", $"Unsupported layout inspect argument '{args[index]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return Usage("COPE-LAYOUT-INSPECT-0003", "Layout inspection requires '--source <entry.ts>' to establish the normal project snapshot.");
        }

        try
        {
            string fullSourcePath = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullSourcePath))
            {
                return Failure("COPE-LAYOUT-INSPECT-0004", $"Source '{sourcePath}' does not exist.", json, FileFailure);
            }

            CopelandProjectCompilation compilation = CompileProject(fullSourcePath, out string projectRoot);
            if (!compilation.Success)
            {
                return CompilationFailure(compilation.Diagnostics, json);
            }

            if (!TryResolve(compilation, target, out LayoutTarget? resolved, out string code, out string message))
            {
                return Failure(code, message, json, CompileFailure);
            }

            ProjectedTableSet projectedTables = LayoutProjectedTableProvider.Create(compilation, projectRoot);
            LayoutInspectionDocument inspection = projectedTables.LayoutViews.Single(document =>
                document.Layout.Name == resolved!.Layout.Name &&
                document.Layout.Module == resolved.Module.LogicalPath);
            if (json)
            {
                WriteProjectedJson(projectedTables, inspection);
            }
            else
            {
                WriteTable(inspection);
            }

            return Success;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("COPE-LAYOUT-INSPECT-0005", exception.Message, json, FileFailure);
        }
    }

    internal static CopelandProjectCompilation CompileProject(string sourcePath, out string projectRoot)
    {
        projectRoot = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        return CopelandProjectCompiler.CompileToMir(
            ReadProjectSources(projectRoot),
            new CopelandCompilationOptions { ProjectRoot = projectRoot });
    }

    private static IReadOnlyList<CopelandProjectSource> ReadProjectSources(string root)
    {
        return Directory.EnumerateFiles(root, "*.ts", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "node_modules" or "bin" or "obj" or ".git"))
            .Where(path => !path.EndsWith(".generated.ts", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new CopelandProjectSource(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                path,
                File.ReadAllText(path)))
            .ToArray();
    }

    private static bool TryResolve(CopelandProjectCompilation compilation, string target, out LayoutTarget? result, out string code, out string message)
    {
        string[] parts = target.Split("::", StringSplitOptions.None);
        string? requestedModule = parts.Length == 2 ? parts[0].TrimStart('.', '/') : null;
        string name = parts.Length == 2 ? parts[1] : target;
        if (parts.Length > 2 || string.IsNullOrWhiteSpace(name))
        {
            result = null;
            code = "COPE-LAYOUT-INSPECT-0006";
            message = $"Layout target '{target}' is invalid. Use <layout> or <module::layout>.";
            return false;
        }

        LayoutTarget[] candidates = compilation.Modules
            .Where(module => requestedModule is null || string.Equals(module.LogicalPath, requestedModule, StringComparison.OrdinalIgnoreCase))
            .SelectMany(module => module.BoundCompilation?.Program.Layouts.Select(layout => new LayoutTarget(module, layout)) ?? [])
            .Where(candidate => string.Equals(candidate.Layout.Name, name, StringComparison.Ordinal))
            .OrderBy(candidate => candidate.Module.LogicalPath, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 1)
        {
            result = candidates[0];
            code = string.Empty;
            message = string.Empty;
            return true;
        }

        result = null;
        if (candidates.Length > 1)
        {
            code = "COPE-LAYOUT-INSPECT-0007";
            message = $"Layout target '{name}' is ambiguous. Use module::layout: {string.Join(", ", candidates.Select(candidate => candidate.Module.LogicalPath + "::" + name))}.";
            return false;
        }

        bool layoutType = compilation.Modules.Any(module => module.BoundCompilation?.SyntaxTree.Root.Members
            .OfType<LayoutTypeDeclarationSyntax>().Any(declaration => declaration.Identifier.Text == name) == true);
        bool ordinarySymbol = compilation.Modules.Any(module => module.BoundCompilation?.ModuleScope?.Declarations.ContainsKey(name) == true);
        code = layoutType ? "COPE-LAYOUT-INSPECT-0008" : ordinarySymbol ? "COPE-LAYOUT-INSPECT-0010" : "COPE-LAYOUT-INSPECT-0009";
        message = layoutType
            ? $"'{name}' is a layout type, not a concrete layout or stream realization."
            : ordinarySymbol
                ? $"'{name}' is an ordinary symbol, not a concrete layout or stream realization."
                : $"Layout or stream target '{target}' was not found in the project snapshot.";
        return false;
    }

    internal static IReadOnlyList<LayoutInspectionBox> AttachContent(IReadOnlyList<LayoutInspectionBox> boxes, CopelandProjectCompilation compilation, BoundLayoutDeclaration layout)
    {
        var content = new Dictionary<string, LayoutInspectionContent>(StringComparer.Ordinal);
        foreach (BoundLayoutBinding binding in compilation.Modules.SelectMany(module => module.BoundCompilation?.Program.LayoutBindings ?? []))
        {
            if (!ReferenceEquals(binding.Layout.BoundLayout, layout)) continue;
            foreach (BoundLayoutBindingEntry entry in binding.Entries)
            {
                content[entry.Slot.SemanticPath] = new LayoutInspectionContent("single", Display(entry.Component), Symbol(entry.Component));
            }
            foreach (BoundStreamCollection collection in binding.Collections)
            {
                string path = FindPath(layout.Root, collection.Region, layout.Name);
                content[path] = new LayoutInspectionContent("boundedCollection", $"{collection.Items.Count} items", null, collection.Items.Count);
            }
        }
        return boxes.Select(box => content.TryGetValue(box.SemanticPath, out LayoutInspectionContent? value) ? box with { Content = value } : box).ToArray();
    }

    internal static string FindPath(BoundLayoutNode node, BoundLayoutNode target, string path)
    {
        if (ReferenceEquals(node, target)) return path;
        foreach (BoundLayoutNode child in node.Children)
        {
            string found = FindPath(child, target, path + "." + child.Name);
            if (found != string.Empty) return found;
        }
        return string.Empty;
    }

    internal static string Display(BoundExpression expression) => expression switch
    {
        BoundCallExpression call => call.Function.Name + "()",
        BoundNpmComponentMemberExpression component => component.Component.Name,
        BoundVariableExpression variable => variable.Variable.Name,
        _ => expression.Type.Name,
    };

    internal static string? Symbol(BoundExpression expression) => expression switch
    {
        BoundCallExpression call => call.Function.Name,
        BoundNpmComponentMemberExpression component => component.Component.Name,
        BoundVariableExpression variable => variable.Variable.Name,
        _ => null,
    };

    private static void WriteTable(LayoutInspectionDocument document)
    {
        Console.Out.WriteLine($"Layout: {document.Layout.Name}");
        Console.Out.WriteLine($"Origin: ({Value(document.Layout.OriginX)}, {Value(document.Layout.OriginY)})");
        Console.Out.WriteLine($"Extent: {LayoutInspection.FormatLength(document.Layout.Width)} × {LayoutInspection.FormatLength(document.Layout.Height)}");
        Console.Out.WriteLine($"Layer set: {document.Layout.LayerSet}");
        if (document.Layout.Contract is not null) Console.Out.WriteLine($"Contract: {document.Layout.Contract} ({(document.Layout.Conformance == true ? "valid" : "invalid")})");
        string[] headers = ["Name", "Parent", "Kind", "X", "Y", "Width", "Height", "Layer", "Rank", "Z", "Order", "Paint", "Content"];
        string[][] rows = document.Boxes.Select(box => new[]
        {
            box.Name, box.Parent ?? "—", box.Kind, Value(box.OriginX), Value(box.OriginY), LayoutInspection.FormatLength(box.Width), LayoutInspection.FormatLength(box.Height), box.Layer,
            box.LayerRank.ToString(), box.Z.ToString(), box.AuthoredOrder.ToString(), box.PaintOrder.ToString(), box.Content?.Display ?? "—",
        }).ToArray();
        int[] widths = headers.Select((header, index) => Math.Max(header.Length, rows.Length == 0 ? 0 : rows.Max(row => row[index].Length))).ToArray();
        Console.Out.WriteLine();
        Console.Out.WriteLine(string.Join("  ", headers.Select((header, index) => header.PadRight(widths[index]))));
        foreach (string[] row in rows) Console.Out.WriteLine(string.Join("  ", row.Select((value, index) => value.PadRight(widths[index]))));
    }

    private static string Value(LayoutInspectionConstraint value) => value.Value is null ? value.Kind : LayoutInspection.FormatLength(value.Value);

    private static void WriteProjectedJson(ProjectedTableSet tableSet, LayoutInspectionDocument inspection)
    {
        string layoutId = inspection.Layout.Module + "::" + inspection.Layout.Name;
        ProjectedTable layouts = tableSet.Require(LayoutProjectedTableProvider.Layouts);
        ProjectedTable boxes = tableSet.Require(LayoutProjectedTableProvider.Boxes);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> layoutRows = layouts.Rows.Where(row => (string)row["layoutId"]! == layoutId).ToArray();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> boxRows = boxes.Rows.Where(row => (string)row["layoutId"]! == layoutId).ToArray();
        HashSet<string> boxIds = boxRows.Select(row => (string)row["boxId"]!).ToHashSet(StringComparer.Ordinal);
        ProjectedTable bindings = tableSet.Require(LayoutProjectedTableProvider.Bindings);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> bindingRows = bindings.Rows.Where(row => boxIds.Contains((string)row["boxId"]!)).ToArray();
        HashSet<string> bindingIds = bindingRows.Select(row => (string)row["bindingId"]!).ToHashSet(StringComparer.Ordinal);
        ProjectedTable collectionItems = tableSet.Require(LayoutProjectedTableProvider.CollectionItems);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> collectionItemRows = collectionItems.Rows.Where(row => bindingIds.Contains((string)row["bindingId"]!)).ToArray();
        HashSet<string> sourceIds = layoutRows.Concat(boxRows).Select(row => row["sourceId"] as string).Where(id => id is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        ProjectedTable sources = tableSet.Require(LayoutProjectedTableProvider.Sources);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> sourceRows = sources.Rows.Where(row => sourceIds.Contains((string)row["sourceId"]!)).ToArray();
        object[] tables =
        [
            TableEnvelope(layouts, layoutRows),
            TableEnvelope(boxes, boxRows),
            TableEnvelope(bindings, bindingRows),
            TableEnvelope(collectionItems, collectionItemRows),
            TableEnvelope(sources, sourceRows),
        ];
        Console.Out.WriteLine(JsonSerializer.Serialize(new { schemaVersion = LayoutInspection.SchemaVersion, success = true, command = "layout.inspect", sourceKind = "projected", readOnly = true, tables }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static object TableEnvelope(ProjectedTable table, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        => new { name = table.Name, columns = table.Columns.Select(column => new { name = column.Name, type = column.Type }), rows };

    private static int CompilationFailure(IReadOnlyList<Diagnostic> diagnostics, bool json)
    {
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new { schemaVersion = LayoutInspection.SchemaVersion, success = false, diagnostics = diagnostics.Select(diagnostic => new { code = diagnostic.Id, severity = "error", message = diagnostic.Message }) }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else foreach (Diagnostic diagnostic in diagnostics) Console.Error.WriteLine($"{diagnostic.Id} error: {diagnostic.Message}");
        return CompileFailure;
    }

    private static int Failure(string code, string message, bool json, int exitCode)
    {
        if (json) Console.Out.WriteLine(JsonSerializer.Serialize(new { schemaVersion = LayoutInspection.SchemaVersion, success = false, diagnostics = new[] { new { code, severity = "error", message } } }, new JsonSerializerOptions { WriteIndented = true }));
        else Console.Error.WriteLine($"{code} error: {message}");
        return exitCode;
    }

    private static int Usage(string code, string message)
    {
        Console.Error.WriteLine("Usage: tscl layout inspect <layout|module::layout> --source <entry.ts> [--json]");
        Console.Error.WriteLine($"{code} error: {message}");
        return UsageFailure;
    }

    private sealed record LayoutTarget(CopelandProjectModuleCompilation Module, BoundLayoutDeclaration Layout);
}
