using Copeland.TS.Diagnostics;
using Copeland.TS.Lowering;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Compiler;

/// <summary>
/// Resolves the deliberately small Copeland source-module law. A source module
/// is available only when its normalized logical path was supplied by the host
/// project; this resolver never probes the file system.
/// </summary>
public static class CopelandProjectCompiler
{
    public static CopelandProjectCompilation CompileToMir(
        IReadOnlyList<CopelandProjectSource> sources,
        CopelandCompilationOptions? options = null)
    {
        var diagnostics = new List<Diagnostic>();
        IReadOnlyList<ProjectModule> modules = CreateModules(sources, diagnostics);
        var byPath = modules.ToDictionary(module => module.LogicalPath, StringComparer.OrdinalIgnoreCase);

        foreach (ProjectModule module in modules)
        {
            ResolveImports(module, byPath, diagnostics);
        }

        ValidateModulePrivacy(modules, diagnostics);
        DetectCycles(modules, diagnostics);
        if (diagnostics.Count > 0)
        {
            return new CopelandProjectCompilation(null, diagnostics);
        }

        IReadOnlyList<ProjectModule> ordered = OrderModules(modules);
        var npmResolver = new CopelandNpmContractResolver(options?.NpmDependencies ?? new CopelandNpmDependencyGraph(options?.NpmPackages ?? []));
        var hostResolver = new CopelandJavaScriptHostContractResolver(options?.JavaScriptHostModules ?? []);
        var clrResolver = new CopelandClrMetadataResolver(options?.ClrReferences ?? []);
        var packageContracts = new CopelandPackageContractMap(options?.PackageContracts ?? []);
        foreach (ProjectModule module in ordered)
        {
            BoundModuleImports imports = CreateImports(module, diagnostics);
            SyntaxTree tree = SyntaxTree.Parse(RewriteModule(module), module.Source.SourcePath);
            BoundCompilation bound = Binder.Bind(tree, null, npmResolver, hostResolver, clrResolver, packageContracts, options?.PackageBackend ?? CopelandPackageBackend.Clr, options?.TsXmlProfile ?? CopelandTsXmlProfile.None, module.Source.SourcePath, module.LogicalPath, imports);
            module.Bound = bound;
            diagnostics.AddRange(bound.Diagnostics.Select(diagnostic => diagnostic with { SourcePath = module.Source.SourcePath }));
        }

        if (diagnostics.Count > 0) return new CopelandProjectCompilation(null, diagnostics);

        ConfigureDuplicateFunctionEmissionNames(ordered);
        ConfigureDuplicateRecordEmissionNames(ordered);
        ConfigureDuplicateEnumEmissionNames(ordered);
        foreach (ProjectModule module in ordered)
        {
            MirCompilation mir = MirLowerer.Lower(module.Bound!);
            module.Mir = mir.Program;
            diagnostics.AddRange(mir.Diagnostics.Select(diagnostic => diagnostic with { SourcePath = module.Source.SourcePath }));
            if (diagnostics.Count > 0) return new CopelandProjectCompilation(null, diagnostics);
        }

        MirProgram aggregate = CombinePrograms(ordered.Select(module => module.Mir!).ToArray());
        MirProjectGraph graph = BuildMirProjectGraph(aggregate, ordered);
        var compiled = new CopelandCompilation(
            CopelandCompilationStage.Mir,
            [],
            null,
            null,
            new MirCompilation(aggregate, []),
            MirTextWriter.Write(aggregate));
        return new CopelandProjectCompilation(compiled, diagnostics, graph);
    }

    public static bool ContainsRelativeImports(IReadOnlyList<CopelandProjectSource> sources)
        => sources.Any(source => ReadImports(source).Any(import => import.Specifier.StartsWith("./", StringComparison.Ordinal)
            || import.Specifier.StartsWith("../", StringComparison.Ordinal)));

    private static IReadOnlyList<ProjectModule> CreateModules(IReadOnlyList<CopelandProjectSource> sources, List<Diagnostic> diagnostics)
    {
        var result = new List<ProjectModule>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CopelandProjectSource source in sources)
        {
            string logicalPath;
            try
            {
                logicalPath = NormalizeLogicalPath(source.LogicalPath);
            }
            catch (ArgumentException exception)
            {
                diagnostics.Add(new Diagnostic("COPE-MODULE-0003", exception.Message, 0, 1, source.SourcePath));
                continue;
            }

            if (!paths.Add(logicalPath))
            {
                diagnostics.Add(new Diagnostic(
                    "COPE-MODULE-0003",
                    $"Copeland project source '{logicalPath}' is included more than once after path normalization.",
                    0,
                    1,
                    source.SourcePath));
                continue;
            }

            IReadOnlyList<ProjectDeclaration> declarations = ReadDeclarations(source);
            result.Add(new ProjectModule(source, logicalPath, ReadImports(source), ReadExports(source), declarations));
        }

        return result.OrderBy(module => module.LogicalPath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ResolveImports(ProjectModule module, IReadOnlyDictionary<string, ProjectModule> byPath, List<Diagnostic> diagnostics)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProjectImport import in module.Imports)
        {
            if (!IsRelative(import.Specifier))
            {
                continue;
            }

            if (!TryResolve(module.LogicalPath, import.Specifier, byPath, out ProjectModule? target, out string? error))
            {
                diagnostics.Add(new Diagnostic("COPE-MODULE-0002", error!, import.Position, import.Length, module.Source.SourcePath));
                continue;
            }

            ProjectModule resolvedTarget = target!;
            import.Target = resolvedTarget;
            foreach (ProjectImportBinding binding in import.Bindings)
            {
                if (!aliases.Add(binding.LocalName))
                {
                    diagnostics.Add(new Diagnostic(
                        "COPE-MODULE-0005",
                        $"Local module import alias '{binding.LocalName}' is declared more than once in '{module.LogicalPath}'.",
                        binding.Position,
                        binding.Length,
                        module.Source.SourcePath));
                }

                if (!resolvedTarget.Exports.Contains(binding.ExportedName))
                {
                    diagnostics.Add(new Diagnostic(
                        "COPE-MODULE-0004",
                        $"Module '{resolvedTarget.LogicalPath}' does not export '{binding.ExportedName}'. Only named exported declarations may be imported.",
                        binding.Position,
                        binding.Length,
                        module.Source.SourcePath));
                }
            }
        }
    }

    private static bool TryResolve(
        string importingPath,
        string specifier,
        IReadOnlyDictionary<string, ProjectModule> byPath,
        out ProjectModule? target,
        out string? error)
    {
        target = null;
        error = null;
        string extension = Path.GetExtension(specifier);
        if (extension.Length > 0 && !string.Equals(extension, ".ts", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".tsx", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Local Copeland module '{specifier}' has unsupported extension '{extension}'. Relative imports support only project-owned .ts and .tsx modules.";
            return false;
        }

        string basePath = NormalizeLogicalPath(Path.Combine(Path.GetDirectoryName(importingPath) ?? string.Empty, specifier));
        string[] candidates = extension.Length == 0
            ? [basePath + ".ts", basePath + ".tsx"]
            : [basePath];
        ProjectModule[] matches = candidates
            .Where(byPath.ContainsKey)
            .Select(candidate => byPath[candidate])
            .Distinct()
            .ToArray();
        if (matches.Length == 1)
        {
            target = matches[0];
            return true;
        }

        if (matches.Length > 1)
        {
            error = $"Local Copeland module '{specifier}' is ambiguous. Matching project-owned modules: {string.Join(", ", matches.Select(match => match.LogicalPath))}.";
            return false;
        }

        error = $"Cannot resolve local Copeland module '{specifier}'. Relative imports resolve only project-owned .ts or .tsx files included in @(CopelandCompile); they do not fall back to npm or CLR resolution.";
        return false;
    }

    private static void DetectCycles(IReadOnlyList<ProjectModule> modules, List<Diagnostic> diagnostics)
    {
        var visiting = new HashSet<ProjectModule>();
        var visited = new HashSet<ProjectModule>();
        var path = new List<ProjectModule>();

        foreach (ProjectModule module in modules)
        {
            Visit(module);
        }

        void Visit(ProjectModule module)
        {
            if (visited.Contains(module)) return;
            if (!visiting.Add(module))
            {
                int start = path.IndexOf(module);
                string cycle = string.Join(" → ", path.Skip(start).Append(module).Select(item => item.LogicalPath));
                diagnostics.Add(new Diagnostic("COPE-MODULE-0006", $"Local Copeland module cycles are not supported: {cycle}.", 0, 1, module.Source.SourcePath));
                return;
            }

            path.Add(module);
            foreach (ProjectModule dependency in module.Imports.Where(import => import.Target is not null).Select(import => import.Target!).OrderBy(item => item.LogicalPath, StringComparer.OrdinalIgnoreCase))
            {
                Visit(dependency);
            }
            path.RemoveAt(path.Count - 1);
            visiting.Remove(module);
            visited.Add(module);
        }
    }

    private static void ValidateModulePrivacy(IReadOnlyList<ProjectModule> modules, List<Diagnostic> diagnostics)
    {
        var functions = modules
            .SelectMany(module => module.Declarations
                .Where(declaration => declaration.Kind == "function")
                .Select(declaration => (Name: declaration.Name, Module: module)))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (ProjectModule module in modules)
        {
            var allowedImports = module.Imports
                .Where(import => import.Target is not null)
                .SelectMany(import => import.Bindings.Select(binding => binding.LocalName))
                .ToHashSet(StringComparer.Ordinal);
            SyntaxToken[] tokens = SyntaxTree.ParseTokens(module.Source.SourceText).Tokens.ToArray();
            foreach (var pair in functions)
            {
                ProjectModule[] foreignOwners = pair.Value
                    .Select(item => item.Module)
                    .Where(owner => owner != module)
                    .Distinct()
                    .ToArray();
                if (pair.Value.Length != 1 || foreignOwners.Length != 1 || allowedImports.Contains(pair.Key))
                {
                    continue;
                }

                for (int index = 0; index + 1 < tokens.Length; index++)
                {
                    SyntaxToken token = tokens[index];
                    if (token.Kind != SyntaxKind.IdentifierToken
                        || token.Text != pair.Key
                        || tokens[index + 1].Kind != SyntaxKind.OpenParenToken
                        || index > 0 && tokens[index - 1].Kind is SyntaxKind.DotToken or SyntaxKind.FunctionKeyword)
                    {
                        continue;
                    }

                    diagnostics.Add(new Diagnostic(
                        "COPE-MODULE-0007",
                        $"Function '{pair.Key}' belongs to module '{foreignOwners[0].LogicalPath}' and is not imported into '{module.LogicalPath}'. Module-private declarations may not be referenced across Copeland modules.",
                        token.Position,
                        token.Text.Length,
                        module.Source.SourcePath));
                }
            }

            var types = modules
                .SelectMany(owner => owner.Declarations
                    .Where(declaration => declaration.Kind is "record" or "enum" or "type" or "interface" or "class")
                    .Select(declaration => (declaration.Name, Owner: owner)))
                .GroupBy(item => item.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            foreach (var pair in types)
            {
                ProjectModule[] foreignOwners = pair.Value
                    .Select(item => item.Owner)
                    .Where(owner => owner != module)
                    .Distinct()
                    .ToArray();
                if (pair.Value.Length != 1 || foreignOwners.Length != 1 || allowedImports.Contains(pair.Key))
                {
                    continue;
                }

                for (int index = 0; index < tokens.Length; index++)
                {
                    SyntaxToken token = tokens[index];
                    bool appearsInTypePosition = index > 0 && tokens[index - 1].Kind == SyntaxKind.ColonToken;
                    bool appearsAsEnumReceiver = index + 1 < tokens.Length && tokens[index + 1].Kind == SyntaxKind.DotToken;
                    if (token.Kind != SyntaxKind.IdentifierToken
                        || token.Text != pair.Key
                        || !appearsInTypePosition && !appearsAsEnumReceiver)
                    {
                        continue;
                    }

                    diagnostics.Add(new Diagnostic(
                        "COPE-MODULE-0007",
                        $"Declaration '{pair.Key}' belongs to module '{foreignOwners[0].LogicalPath}' and is not imported into '{module.LogicalPath}'. Module-private declarations may not be referenced across Copeland modules.",
                        token.Position,
                        token.Text.Length,
                        module.Source.SourcePath));
                }
            }
        }
    }

    private static IReadOnlyList<ProjectModule> OrderModules(IReadOnlyList<ProjectModule> modules)
    {
        var ordered = new List<ProjectModule>();
        var visited = new HashSet<ProjectModule>();
        foreach (ProjectModule module in modules)
        {
            Add(module);
        }
        return ordered;

        void Add(ProjectModule module)
        {
            if (!visited.Add(module)) return;
            foreach (ProjectModule dependency in module.Imports.Where(import => import.Target is not null).Select(import => import.Target!).OrderBy(item => item.LogicalPath, StringComparer.OrdinalIgnoreCase))
            {
                Add(dependency);
            }
            ordered.Add(module);
        }
    }

    private static string RewriteModule(ProjectModule module)
    {
        SyntaxTree tokenTree = SyntaxTree.ParseTokens(module.Source.SourceText);
        var replacements = new List<TextReplacement>();
        foreach (ProjectImport import in module.Imports)
        {
            if (!IsRelative(import.Specifier))
            {
                continue;
            }

            replacements.Add(new TextReplacement(import.Start, import.End - import.Start, string.Empty));
        }

        foreach (SyntaxToken token in tokenTree.Tokens.Where(token => token.Kind == SyntaxKind.IdentifierToken && token.Text == "export"))
        {
            replacements.Add(new TextReplacement(token.Position, token.Text.Length, string.Empty));
        }

        string text = module.Source.SourceText;
        foreach (TextReplacement replacement in replacements
            .OrderByDescending(item => item.Start)
            .ThenByDescending(item => item.Length)
            .GroupBy(item => item.Start)
            .Select(group => group.First()))
        {
            text = text.Remove(replacement.Start, replacement.Length).Insert(replacement.Start, replacement.Replacement);
        }
        return text;
    }

    private static BoundModuleImports CreateImports(ProjectModule module, List<Diagnostic> diagnostics)
    {
        var declarations = new Dictionary<string, Symbol>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, TypeAliasSymbol>(StringComparer.Ordinal);
        var interfaces = new Dictionary<string, InterfaceSymbol>(StringComparer.Ordinal);
        var genericBodies = new Dictionary<FunctionSymbol, BoundFunctionDeclaration>();
        foreach (ProjectImport import in module.Imports.Where(import => import.Target is not null))
        {
            BoundModuleScope scope = import.Target!.Bound!.ModuleScope!;
            foreach (ProjectImportBinding binding in import.Bindings)
            {
                if (import.Target.Declarations.Any(declaration => declaration.Name == binding.ExportedName && declaration.Kind == "flow"))
                {
                    diagnostics.Add(new Diagnostic(
                        "COPE-MODULE-0008",
                        $"Flow '{binding.ExportedName}' is exported by '{import.Target.LogicalPath}', but local flow imports are deferred: Flow M1 exposes only provisional backend-facing session APIs and has no source-level flow value model.",
                        binding.Position,
                        binding.Length,
                        module.Source.SourcePath));
                    continue;
                }
                if (scope.Declarations.TryGetValue(binding.ExportedName, out Symbol? symbol))
                {
                    declarations[binding.LocalName] = symbol;
                    if (symbol is FunctionSymbol function && scope.GenericBodies.TryGetValue(function, out BoundFunctionDeclaration? body)) genericBodies[function] = body;
                    continue;
                }
                if (scope.Aliases.TryGetValue(binding.ExportedName, out TypeAliasSymbol? alias))
                {
                    aliases[binding.LocalName] = alias;
                    continue;
                }
                if (scope.Interfaces.TryGetValue(binding.ExportedName, out InterfaceSymbol? @interface))
                {
                    interfaces[binding.LocalName] = @interface;
                    continue;
                }
                diagnostics.Add(new Diagnostic("COPE-MODULE-0004", $"Module '{import.Target.LogicalPath}' does not expose semantic declaration '{binding.ExportedName}'.", binding.Position, binding.Length, module.Source.SourcePath));
            }
        }
        return new BoundModuleImports(declarations, aliases, interfaces, genericBodies);
    }

    private static MirProgram CombinePrograms(IReadOnlyList<MirProgram> programs)
        => new(
            programs.SelectMany(program => program.Enums).ToArray(),
            programs.SelectMany(program => program.Records).ToArray(),
            programs.SelectMany(program => program.Tables).ToArray(),
            programs.SelectMany(program => program.TsonEncodingPlans).ToArray(),
            programs.SelectMany(program => program.NpmImports).ToArray(),
            programs.SelectMany(program => program.Functions).ToArray(),
            programs.SelectMany(program => program.CSharpUsings).Distinct(StringComparer.Ordinal).ToArray(),
            null,
            programs.SelectMany(program => program.Flows).ToArray(),
            programs.SelectMany(program => program.JavaScriptHostImports).ToArray(),
            programs.SelectMany(program => program.PackageImports).ToArray());

    private static void ConfigureDuplicateFunctionEmissionNames(IReadOnlyList<ProjectModule> modules)
    {
        var duplicates = modules
            .SelectMany(module => module.Bound!.ModuleScope!.Declarations.Values.OfType<FunctionSymbol>())
            .GroupBy(function => function.Name, StringComparer.Ordinal)
            .Where(group => group.Select(function => function.StableIdentity).Distinct(StringComparer.Ordinal).Skip(1).Any())
            .SelectMany(group => group)
            .ToHashSet();
        foreach (ProjectModule module in modules)
        {
            foreach (FunctionSymbol function in module.Bound!.ModuleScope!.Declarations.Values.OfType<FunctionSymbol>())
            {
                if (!duplicates.Contains(function)) continue;
                function.EmissionName = "__cope_" + Sanitize(module.LogicalPath) + "_" + function.Name;
            }

            foreach (BoundFunctionDeclaration lifted in module.Bound.Program.Functions.Where(function => function.Symbol.Name.StartsWith("__cope_arrow_", StringComparison.Ordinal)))
            {
                lifted.Symbol.EmissionName = "__cope_" + Sanitize(module.LogicalPath) + "_" + lifted.Symbol.Name;
            }
        }
    }

    private static string Sanitize(string logicalPath)
        => string.Concat(logicalPath.Select(character => char.IsLetterOrDigit(character) ? character : '_'));

    private static void ConfigureDuplicateEnumEmissionNames(IReadOnlyList<ProjectModule> modules)
    {
        var duplicates = modules
            .SelectMany(module => module.Bound!.ModuleScope!.Declarations.Values
                .OfType<VariableSymbol>()
                .Select(variable => variable.Type)
                .OfType<EnumTypeSymbol>())
            .GroupBy(@enum => @enum.Name, StringComparer.Ordinal)
            .Where(group => group.Select(@enum => @enum.StableIdentity).Distinct(StringComparer.Ordinal).Skip(1).Any())
            .SelectMany(group => group)
            .ToHashSet();
        foreach (ProjectModule module in modules)
        {
            foreach (EnumTypeSymbol @enum in module.Bound!.ModuleScope!.Declarations.Values
                .OfType<VariableSymbol>()
                .Select(variable => variable.Type)
                .OfType<EnumTypeSymbol>())
            {
                if (!duplicates.Contains(@enum)) continue;
                @enum.EmissionName = "__cope_" + Sanitize(module.LogicalPath) + "_" + @enum.Name;
            }
        }
    }

    private static void ConfigureDuplicateRecordEmissionNames(IReadOnlyList<ProjectModule> modules)
    {
        var duplicates = modules
            .SelectMany(module => module.Bound!.ModuleScope!.Declarations.Values
                .OfType<VariableSymbol>()
                .Select(variable => variable.Type)
                .OfType<RecordTypeSymbol>())
            .GroupBy(record => record.Name, StringComparer.Ordinal)
            .Where(group => group.Select(record => record.StableIdentity).Distinct(StringComparer.Ordinal).Skip(1).Any())
            .SelectMany(group => group)
            .ToHashSet();
        foreach (ProjectModule module in modules)
        {
            foreach (RecordTypeSymbol record in module.Bound!.ModuleScope!.Declarations.Values
                .OfType<VariableSymbol>()
                .Select(variable => variable.Type)
                .OfType<RecordTypeSymbol>())
            {
                if (!duplicates.Contains(record)) continue;
                record.EmissionName = "__cope_" + Sanitize(module.LogicalPath) + "_" + record.Name;
            }
        }
    }

    private static MirProjectGraph BuildMirProjectGraph(MirProgram aggregateProgram, IReadOnlyList<ProjectModule> modules)
    {
        MirProjectModule[] mirModules = modules.Select(module =>
        {
            MirModuleImport[] imports = module.Imports
                .SelectMany(import => import.Bindings.Select(binding => new MirModuleImport(
                    import.Specifier,
                    import.Target is null ? null : new MirModuleId(import.Target.LogicalPath),
                    binding.ExportedName,
                    binding.LocalName)))
                .ToArray();
            MirModuleExport[] exports = module.Exports
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => new MirModuleExport(
                    name,
                    module.Declarations.FirstOrDefault(declaration => declaration.Name == name)?.Kind ?? "declaration",
                    GetRuntimeIdentity(module, name)))
                .ToArray();
            string[] privateDeclarations = module.Declarations
                .Where(declaration => !module.Exports.Contains(declaration.Name))
                .Select(declaration => declaration.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            MirFunction[] moduleFunctions = module.Mir!.Functions.ToArray();
            return new MirProjectModule(
                new MirModuleId(module.LogicalPath),
                imports,
                exports,
                privateDeclarations,
                moduleFunctions,
                module.Mir!.NpmImports,
                module.Mir.JavaScriptHostImports);
        }).ToArray();
        return new MirProjectGraph(aggregateProgram, mirModules);
    }

    private static string? GetRuntimeIdentity(ProjectModule module, string name)
    {
        if (!module.Bound!.ModuleScope!.Declarations.TryGetValue(name, out Symbol? symbol)) return null;
        return symbol switch
        {
            FunctionSymbol function => function.EmissionName,
            VariableSymbol { Type: RecordTypeSymbol record } => record.Id.ToString(),
            VariableSymbol { Type: EnumTypeSymbol @enum } => @enum.EmissionName,
            _ => null,
        };
    }

    private static IReadOnlyList<ProjectImport> ReadImports(CopelandProjectSource source)
    {
        SyntaxTree tree = SyntaxTree.Parse(source.SourceText, source.LogicalPath);
        return tree.Root.Members.OfType<ImportDeclarationSyntax>().Select(import =>
        {
            SyntaxToken[] tokens = import.Tokens.ToArray();
            SyntaxToken? specifier = tokens.LastOrDefault(token => token.Kind == SyntaxKind.StringToken);
            var bindings = new List<ProjectImportBinding>();
            int open = Array.FindIndex(tokens, token => token.Kind == SyntaxKind.OpenBraceToken);
            int close = Array.FindIndex(tokens, token => token.Kind == SyntaxKind.CloseBraceToken);
            if (open >= 0 && close > open)
            {
                for (int index = open + 1; index < close; index++)
                {
                    if (tokens[index].Kind != SyntaxKind.IdentifierToken) continue;
                    SyntaxToken exported = tokens[index];
                    SyntaxToken local = exported;
                    if (index + 2 < close && tokens[index + 1].Text == "as" && tokens[index + 2].Kind == SyntaxKind.IdentifierToken)
                    {
                        local = tokens[index + 2];
                        index += 2;
                    }
                    bindings.Add(new ProjectImportBinding(exported.Text, local.Text, local.Position, local.Text.Length));
                }
            }

            int start = tokens.FirstOrDefault()?.Position ?? 0;
            SyntaxToken last = tokens.LastOrDefault() ?? tokens.First();
            return new ProjectImport(specifier?.Value as string ?? string.Empty, bindings, specifier?.Position ?? start, specifier?.Text.Length ?? 1, start, last.Position + last.Text.Length);
        }).ToArray();
    }

    private static IReadOnlySet<string> ReadExports(CopelandProjectSource source)
    {
        SyntaxTree tree = SyntaxTree.ParseTokens(source.SourceText);
        var exports = new HashSet<string>(StringComparer.Ordinal);
        SyntaxToken[] tokens = tree.Tokens.ToArray();
        for (int index = 0; index + 1 < tokens.Length; index++)
        {
            if (tokens[index].Kind != SyntaxKind.IdentifierToken || tokens[index].Text != "export") continue;
            int nameIndex = tokens[index + 1].Text switch
            {
                "async" when index + 3 < tokens.Length && tokens[index + 2].Kind == SyntaxKind.FunctionKeyword => index + 3,
                _ when tokens[index + 1].Kind == SyntaxKind.FunctionKeyword && tokens[index + 2].Kind == SyntaxKind.StarToken => index + 3,
                _ when tokens[index + 1].Kind == SyntaxKind.FunctionKeyword => index + 2,
                "type" or "interface" or "flow" or "class" => index + 2,
                _ when tokens[index + 1].Kind is SyntaxKind.EnumKeyword or SyntaxKind.RecordKeyword => index + 2,
                "const" when index + 3 < tokens.Length && tokens[index + 2].Kind == SyntaxKind.RecordKeyword => index + 3,
                _ => -1,
            };
            if (nameIndex >= 0 && nameIndex < tokens.Length && tokens[nameIndex].Kind == SyntaxKind.IdentifierToken)
            {
                exports.Add(tokens[nameIndex].Text);
            }
        }
        return exports;
    }

    private static IReadOnlyList<ProjectDeclaration> ReadDeclarations(CopelandProjectSource source)
    {
        SyntaxToken[] tokens = SyntaxTree.ParseTokens(source.SourceText).Tokens.ToArray();
        var declarations = new List<ProjectDeclaration>();
        for (int index = 0; index + 1 < tokens.Length; index++)
        {
            string? kind = tokens[index].Kind switch
            {
                SyntaxKind.FunctionKeyword => "function",
                SyntaxKind.EnumKeyword => "enum",
                SyntaxKind.RecordKeyword => "record",
                _ when tokens[index].Kind == SyntaxKind.IdentifierToken && tokens[index].Text == "type" => "type",
                _ when tokens[index].Kind == SyntaxKind.IdentifierToken && tokens[index].Text == "interface" => "interface",
                _ when tokens[index].Kind == SyntaxKind.IdentifierToken && tokens[index].Text == "flow" => "flow",
                _ when tokens[index].Kind == SyntaxKind.IdentifierToken && tokens[index].Text == "class" => "class",
                _ => null,
            };
            int nameIndex = kind == "function" && tokens[index + 1].Kind == SyntaxKind.StarToken
                ? index + 2
                : index + 1;
            if (kind is null || nameIndex >= tokens.Length || tokens[nameIndex].Kind != SyntaxKind.IdentifierToken)
            {
                continue;
            }

            declarations.Add(new ProjectDeclaration(tokens[nameIndex].Text, kind));
        }
        return declarations;
    }

    private static bool IsRelative(string specifier) => specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal);

    private static string NormalizeLogicalPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        var parts = new List<string>();
        foreach (string part in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) throw new ArgumentException("Copeland module logical paths may not escape the project root.");
                parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(part);
        }
        if (parts.Count == 0) throw new ArgumentException("Copeland module logical paths must identify a .ts or .tsx project source.");
        return string.Join('/', parts);
    }

    private sealed class ProjectModule(CopelandProjectSource source, string logicalPath, IReadOnlyList<ProjectImport> imports, IReadOnlySet<string> exports, IReadOnlyList<ProjectDeclaration> declarations)
    {
        public CopelandProjectSource Source { get; } = source;
        public string LogicalPath { get; } = logicalPath;
        public IReadOnlyList<ProjectImport> Imports { get; } = imports;
        public IReadOnlySet<string> Exports { get; } = exports;
        public IReadOnlyList<ProjectDeclaration> Declarations { get; } = declarations;
        public BoundCompilation? Bound { get; set; }
        public MirProgram? Mir { get; set; }
    }

    private sealed class ProjectImport(string specifier, IReadOnlyList<ProjectImportBinding> bindings, int position, int length, int start, int end)
    {
        public string Specifier { get; } = specifier;
        public IReadOnlyList<ProjectImportBinding> Bindings { get; } = bindings;
        public int Position { get; } = position;
        public int Length { get; } = length;
        public int Start { get; } = start;
        public int End { get; } = end;
        public ProjectModule? Target { get; set; }
    }

    private sealed record ProjectImportBinding(string ExportedName, string LocalName, int Position, int Length);
    private sealed record ProjectDeclaration(string Name, string Kind);
    private sealed record TextReplacement(int Start, int Length, string Replacement);
}

public sealed record CopelandProjectSource(string LogicalPath, string SourcePath, string SourceText);

public sealed class CopelandProjectCompilation(CopelandCompilation? compilation, IReadOnlyList<Diagnostic> diagnostics, MirProjectGraph? mirProjectGraph = null)
{
    public CopelandCompilation? Compilation { get; } = compilation;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    public MirProjectGraph? MirProjectGraph { get; } = mirProjectGraph;
    public bool Success => Compilation is not null && Diagnostics.Count == 0 && Compilation.Success;
}
