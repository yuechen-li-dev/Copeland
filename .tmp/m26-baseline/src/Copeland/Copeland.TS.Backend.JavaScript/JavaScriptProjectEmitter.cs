using Copeland.TS.Mir;

namespace Copeland.TS.Backend.JavaScript;

/// <summary>
/// Emits one native ESM file per resolved Copeland source module. The shared
/// project MIR is used only to preserve symbol/type identity during lowering;
/// imported functions are removed from an importing module's emitted body and
/// restored as ordinary named ESM imports.
/// </summary>
public static class JavaScriptProjectEmitter
{
    public static JavaScriptProjectCompilation Emit(MirProjectGraph graph, JavaScriptEmissionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        JavaScriptEmissionOptions effectiveOptions = options ?? new JavaScriptEmissionOptions();
        IReadOnlySet<string> boundaryFunctions = graph.Modules
            .SelectMany(module => module.Exports)
            .Where(export => export.RuntimeName is not null)
            .Select(export => export.RuntimeName!)
            .ToHashSet(StringComparer.Ordinal);
        JavaScriptCompilation aggregate = JavaScriptBackend.Emit(graph.AggregateProgram, effectiveOptions with
        {
            EmitModuleFactories = true,
            BoundaryFunctionNames = boundaryFunctions,
        });
        if (!aggregate.Success)
        {
            return new JavaScriptProjectCompilation(new Dictionary<string, string>(), aggregate.Diagnostics);
        }

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var runtimeExports = graph.AggregateProgram.Functions.Select(function => function.Name)
            .Concat(graph.AggregateProgram.Flows.Select(flow => flow.Name))
            .ToHashSet(StringComparer.Ordinal);

        foreach (MirProjectModule module in graph.Modules.OrderBy(module => module.Id.Value, StringComparer.Ordinal))
        {
            string outputPath = GetOutputPath(module.Id);
            var lines = new List<string>();
            var importedHelpers = new HashSet<string>(StringComparer.Ordinal);
            AddExternalImports(lines, module.NpmImports, module.JavaScriptHostImports);
            if (module.Functions.Any(function => function.IsRemote))
            {
                string configSpecifier = GetRelativeSpecifier(outputPath, "bridge-config.js");
                lines.Add($"import {{ baseUrl as {effectiveOptions.BridgeBaseUrlBinding} }} from \"{configSpecifier}\";");
            }
            foreach (IGrouping<MirModuleId, MirModuleImport> imports in module.Imports
                .Where(import => import.TargetModule is not null)
                .GroupBy(import => import.TargetModule!))
            {
                string specifier = GetRelativeSpecifier(outputPath, GetOutputPath(imports.Key));
                MirModuleImport[] runtimeImports = imports
                    .Where(import => GetRuntimeName(graph, import.TargetModule!, import.ExportedName) is string runtimeName && runtimeExports.Contains(runtimeName))
                    .ToArray();
                if (runtimeImports.Length > 0)
                {
                    string bindings = string.Join(", ", runtimeImports.OrderBy(import => import.ExportedName, StringComparer.Ordinal).Select(import => FormatImport(import, GetRuntimeName(graph, import.TargetModule!, import.ExportedName)!)));
                    lines.Add($"import {{ {bindings} }} from \"{specifier}\";");
                }
                foreach (MirModuleImport import in runtimeImports)
                {
                    string runtimeName = GetRuntimeName(graph, import.TargetModule!, import.ExportedName)!;
                    if (runtimeName == import.ExportedName && import.LocalName != import.ExportedName)
                    {
                        lines.Add($"const {runtimeName} = {import.LocalName};");
                    }
                }

                string[] helpers = imports
                    .SelectMany(import => GetRuntimeHelpers(graph, imports.Key, import.ExportedName))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                if (helpers.Length > 0)
                {
                    lines.Add($"import {{ {string.Join(", ", helpers)} }} from \"{specifier}\";");
                    foreach (string helper in helpers) importedHelpers.Add(helper);
                }
            }

            MirModuleExport[] exports = module.Exports
                .Where(export => export.RuntimeName is not null && runtimeExports.Contains(export.RuntimeName))
                .OrderBy(export => export.Name, StringComparer.Ordinal)
                .ToArray();
            string source = StripFunctions(aggregate.SourceText!, graph.AggregateProgram.Functions
                .Select(function => function.Name)
                .Except(module.Functions.Select(function => function.Name), StringComparer.Ordinal));
            source = StripFunctions(source, importedHelpers);
            source = StripExternalImports(source);
            lines.Add(source.TrimEnd());
            if (exports.Length > 0)
            {
                lines.Add($"export {{ {string.Join(", ", exports.Select(export => export.RuntimeName == export.Name ? export.Name : export.RuntimeName + " as " + export.Name))} }};");
            }
            string[] exportedHelpers = module.Exports
                .SelectMany(export => GetRuntimeHelpers(graph, module.Id, export.Name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (exportedHelpers.Length > 0)
            {
                lines.Add($"export {{ {string.Join(", ", exportedHelpers)} }};");
            }
            foreach (MirModuleExport export in module.Exports
                .Where(export => export.DeclarationKind == "record" && export.RuntimeName is not null)
                .OrderBy(export => export.Name, StringComparer.Ordinal))
            {
                string helper = JavaScriptBackend.GetRecordFactoryName(new MirRecordTypeId(export.RuntimeName!));
                lines.Add($"export {{ {helper} as {JavaScriptIdentifierEncoder.Encode(export.Name)} }};");
            }

            files.Add(outputPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }

        return new JavaScriptProjectCompilation(files, []);
    }

    private static string? GetRuntimeName(MirProjectGraph graph, MirModuleId target, string exportedName)
        => graph.Modules.Single(module => module.Id == target).Exports
            .FirstOrDefault(export => export.Name == exportedName)?.RuntimeName;

    private static IEnumerable<string> GetRuntimeHelpers(MirProjectGraph graph, MirModuleId target, string exportedName)
    {
        MirProjectModule module = graph.Modules.Single(module => module.Id == target);
        MirModuleExport? export = module.Exports.FirstOrDefault(export => export.Name == exportedName);
        if (export?.RuntimeName is null) return [];
        return export.DeclarationKind switch
        {
            "record" => [JavaScriptBackend.GetRecordFactoryName(new MirRecordTypeId(export.RuntimeName))],
            "enum" => graph.AggregateProgram.Enums
                .Single(@enum => @enum.Name == export.RuntimeName)
                .Cases.Select(@case => JavaScriptBackend.GetEnumFactoryName(export.RuntimeName, @case.Name))
                .ToArray(),
            _ => [],
        };
    }

    private static string FormatImport(MirModuleImport import, string runtimeName)
    {
        string localName = runtimeName == import.ExportedName ? import.LocalName : runtimeName;
        return string.Equals(import.ExportedName, localName, StringComparison.Ordinal)
            ? import.ExportedName
            : import.ExportedName + " as " + localName;
    }

    private static string StripFunctions(string source, IEnumerable<string> functionNames)
    {
        string result = source;
        foreach (string name in functionNames.OrderByDescending(name => name.Length))
        {
            int start = FindFunctionStart(result, name);
            if (start < 0)
            {
                continue;
            }

            int openBrace = result.IndexOf('{', start);
            if (openBrace < 0)
            {
                continue;
            }

            int depth = 0;
            int end = openBrace;
            for (; end < result.Length; end++)
            {
                if (result[end] == '{') depth++;
                else if (result[end] == '}' && --depth == 0)
                {
                    end++;
                    break;
                }
            }

            result = result.Remove(start, end - start);
        }
        return result;
    }

    private static void AddExternalImports(
        List<string> lines,
        IReadOnlyList<MirNpmImport> npmImports,
        IReadOnlyList<MirJavaScriptHostImport> javaScriptHostImports)
    {
        foreach (MirNpmImport npm in npmImports.OrderBy(import => import.PackageName, StringComparer.Ordinal).ThenBy(import => import.ExportName, StringComparer.Ordinal).ThenBy(import => import.LocalBinding, StringComparer.Ordinal))
        {
            AddExternalImport(lines, npm.PackageName, npm.ExportName, npm.LocalBinding);
        }

        foreach (MirJavaScriptHostImport host in javaScriptHostImports.OrderBy(import => import.ModuleSpecifier, StringComparer.Ordinal).ThenBy(import => import.ExportName, StringComparer.Ordinal).ThenBy(import => import.LocalBinding, StringComparer.Ordinal))
        {
            AddExternalImport(lines, host.ModuleSpecifier, host.ExportName, host.LocalBinding);
        }
    }

    private static void AddExternalImport(List<string> lines, string moduleSpecifier, string exportName, string localBinding)
    {
        string encodedExport = JavaScriptIdentifierEncoder.Encode(exportName);
        string encodedLocal = JavaScriptIdentifierEncoder.Encode(localBinding);
        string binding = string.Equals(encodedExport, encodedLocal, StringComparison.Ordinal)
            ? encodedExport
            : encodedExport + " as " + encodedLocal;
        lines.Add($"import {{ {binding} }} from \"{moduleSpecifier}\";");
    }

    private static string StripExternalImports(string source)
        => string.Join(
            Environment.NewLine,
            source.Split(["\r\n", "\n"], StringSplitOptions.None)
                .Where(line => !line.StartsWith("import { ", StringComparison.Ordinal)))
            .TrimEnd();

    private static int FindFunctionStart(string source, string name)
    {
        string normal = "function " + name + "(";
        int normalIndex = source.IndexOf(normal, StringComparison.Ordinal);
        if (normalIndex >= 0)
        {
            return normalIndex;
        }

        string generator = "function* " + name + "(";
        return source.IndexOf(generator, StringComparison.Ordinal);
    }

    public static string GetOutputPath(MirModuleId module)
    {
        string extension = Path.GetExtension(module.Value);
        string pathWithoutExtension = extension.Length == 0 ? module.Value : module.Value[..^extension.Length];
        return pathWithoutExtension + ".js";
    }

    private static string GetRelativeSpecifier(string fromOutputPath, string targetOutputPath)
    {
        string fromDirectory = Path.GetDirectoryName(fromOutputPath)?.Replace('\\', '/') ?? string.Empty;
        string relative = Path.GetRelativePath(string.IsNullOrEmpty(fromDirectory) ? "." : fromDirectory, targetOutputPath)
            .Replace('\\', '/');
        return relative.StartsWith(".", StringComparison.Ordinal) ? relative : "./" + relative;
    }
}

public sealed class JavaScriptProjectCompilation(
    IReadOnlyDictionary<string, string> files,
    IReadOnlyList<JavaScriptDiagnostic> diagnostics)
{
    public IReadOnlyDictionary<string, string> Files { get; } = files;
    public IReadOnlyList<JavaScriptDiagnostic> Diagnostics { get; } = diagnostics;
    public bool Success => Diagnostics.Count == 0;
}
