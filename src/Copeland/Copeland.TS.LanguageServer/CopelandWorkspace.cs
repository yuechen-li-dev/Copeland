using System.Text.Json;
using Copeland.TS.Compiler;
using Copeland.TS.Diagnostics;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Copeland.TS.MSBuild;

namespace Copeland.TS.LanguageServer;

/// <summary>
/// Resident, editor-neutral project snapshot. Open buffers overlay disk content;
/// the generated workspace artifact remains the sole source of ownership.
/// </summary>
internal sealed class CopelandWorkspace
{
    private const int OwnershipSchemaVersion = 1;
    private readonly Dictionary<string, DocumentSnapshot> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _owners = new(StringComparer.OrdinalIgnoreCase);
    private string? _rootPath;
    private string? _ownershipPath;
    private string? _loadError;
    private CopelandTsXmlProfile _profile;
    private CopelandProjectSnapshot? _snapshot;
    private DateTime _ownershipLastWriteUtc;
    private string? _projectPath;
    private DateTime _projectLastWriteUtc;
    private CopelandEvaluatedProject? _evaluatedProject;

    public void Initialize(JsonElement parameters)
    {
        JsonElement options = parameters.TryGetProperty("initializationOptions", out JsonElement initializationOptions)
            ? initializationOptions
            : default;
        string? root = ReadOptionalString(options, "workspaceRoot") ?? UriToPath(ReadOptionalString(parameters, "rootUri"));
        if (string.IsNullOrWhiteSpace(root)) throw new LanguageServerException("CTS-LSP-0002: initializationOptions.workspaceRoot or rootUri is required.");
        _rootPath = Path.GetFullPath(root);
        _ownershipPath = ReadOptionalString(options, "ownershipFile")
            ?? Path.Combine(_rootPath, "obj", "copeland", "workspace", "editor-ownership.generated.json");
        _projectPath = ReadOptionalString(options, "project");
        if (!string.IsNullOrWhiteSpace(_projectPath)) _projectPath = Path.GetFullPath(_projectPath, _rootPath);
        string? requestedProfile = ReadOptionalString(options, "tsXmlProfile");
        _profile = string.Equals(requestedProfile, "react-m0", StringComparison.OrdinalIgnoreCase)
            ? CopelandTsXmlProfile.ReactM0
            : CopelandTsXmlProfile.None;
        LoadOwnership();
        LoadProjectModel();
        RebuildSnapshot();
    }

    public void Open(string uri, int version, string text)
    {
        string path = UriToPath(uri) ?? throw new LanguageServerException("CTS-LSP-0003: text document URI must be a file URI.");
        _documents[uri] = new DocumentSnapshot(path, version, text);
        RebuildSnapshot();
    }

    public bool Change(string uri, int version, string text)
    {
        if (!_documents.TryGetValue(uri, out DocumentSnapshot? document)) return false;
        if (version <= document.Version) return false;
        _documents[uri] = document with { Version = version, Text = text };
        RebuildSnapshot();
        return true;
    }

    public void Close(string uri)
    {
        _documents.Remove(uri);
        RebuildSnapshot();
    }

    public object[] Diagnostics(string uri)
    {
        ReloadIfOwnershipChanged();
        if (!TryGetOwnedDocument(uri, out DocumentSnapshot? document, out string? owner)) return [];
        if (!string.Equals(owner, "tscl", StringComparison.Ordinal)) return [];
        if (_loadError is not null) return [DiagnosticObject(_loadError, "CTS-LSP-OWNERSHIP", 1, document!.Text, 0, 1)];
        if (IsWorkspaceManifest(document!.Path)) return WorkspaceManifestDiagnostics(document.Text);
        return Compile(document).Diagnostics
            .Where(diagnostic => diagnostic.SourcePath is null || PathsEqual(diagnostic.SourcePath, document.Path))
            .OrderBy(diagnostic => diagnostic.Position)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .Select(diagnostic => DiagnosticObject(diagnostic.Message, diagnostic.Id, 1, document.Text, diagnostic.Position, diagnostic.Length))
            .ToArray();
    }

    public object? Hover(string uri, JsonElement position)
    {
        ReloadIfOwnershipChanged();
        if (!TryGetCopelandDocument(uri, out DocumentSnapshot? document)) return null;
        if (IsWorkspaceManifest(document!.Path)) return ManifestHover(document.Text, ToOffset(document.Text, position));
        CopelandCompilation compilation = Compile(document);
        SyntaxToken? token = TokenAt(compilation.SyntaxTree, ToOffset(document.Text, position));
        if (token is null) return null;
        Symbol? symbol = FindSymbol(compilation, token.Text);
        DeclarationInfo? declaration = FindDeclaration(compilation.SyntaxTree, token.Text);
        string contents = symbol is not null ? Describe(symbol) : declaration?.Detail ?? (token.Kind == SyntaxKind.IdentifierToken ? token.Text : string.Empty);
        if (symbol is null && declaration is null && token.Kind == SyntaxKind.IdentifierToken)
        {
            contents = DescribeClrType(token.Text) ?? contents;
        }
        return contents.Length == 0 ? null : new { contents = new { kind = "markdown", value = "```copeland\n" + contents + "\n```" }, range = Range(document.Text, token.Position, token.Text.Length) };
    }

    public object? Completion(string uri, JsonElement position)
    {
        ReloadIfOwnershipChanged();
        if (!TryGetCopelandDocument(uri, out DocumentSnapshot? document)) return null;
        if (IsWorkspaceManifest(document!.Path))
        {
            return new { isIncomplete = false, items = new[] { CompletionItem("tsc", 14, "workspace owner"), CompletionItem("tscl", 14, "workspace owner"), CompletionItem("include", 10, "workspace rule"), CompletionItem("exclude", 10, "workspace rule"), CompletionItem("project", 10, "declared project") } };
        }
        CopelandCompilation compilation = Compile(document!);
        var items = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (string keyword in new[] { "function", "record", "enum", "match", "return", "const", "let", "using", "import", "export", "async", "remote" })
        {
            items[keyword] = CompletionItem(keyword, 14, "keyword");
        }
        BoundModuleScope? scope = compilation.BoundCompilation?.ModuleScope;
        if (scope is not null)
        {
            foreach (Symbol symbol in scope.Declarations.Values.OrderBy(symbol => symbol.Name, StringComparer.Ordinal))
            {
                items[symbol.Name] = CompletionItem(symbol.Name, CompletionKind(symbol), Describe(symbol));
            }
        }
        if (compilation.BoundCompilation is not null)
        {
            foreach (BoundNpmImport import in compilation.BoundCompilation.Program.NpmImports)
            {
                items[import.Function.Name] = CompletionItem(import.Function.Name, 3, Describe(import.Function));
            }
            foreach (BoundPackageImport import in compilation.BoundCompilation.Program.PackageImports)
            {
                items[import.Function.Name] = CompletionItem(import.Function.Name, 3, Describe(import.Function));
            }
            foreach (BoundJavaScriptHostImport import in compilation.BoundCompilation.Program.JavaScriptHostImports)
            {
                items[import.Function.Name] = CompletionItem(import.Function.Name, 3, Describe(import.Function));
            }
            foreach (BoundNpmComponentImport import in compilation.BoundCompilation.Program.NpmComponentImports)
            {
                items[import.Component.Name] = CompletionItem(import.Component.Name, 7, Describe(import.Component));
            }
        }
        AddClrCompletionItems(items, document.Text, ToOffset(document.Text, position));
        foreach (DeclarationInfo declaration in Declarations(compilation.SyntaxTree))
        {
            items[declaration.Name] = CompletionItem(declaration.Name, declaration.Kind, declaration.Detail);
        }
        CopelandProjectCompilation project = CompileProject();
        CopelandProjectModuleCompilation? currentModule = project.Modules.FirstOrDefault(module => PathsEqual(module.Source.SourcePath, document.Path));
        if (currentModule is not null)
        {
            foreach (CopelandProjectImport import in currentModule.Imports.Where(import => import.TargetLogicalPath is not null))
            {
                CopelandProjectModuleCompilation? target = project.Modules.FirstOrDefault(module => module.LogicalPath == import.TargetLogicalPath);
                DeclarationInfo? declaration = target is null ? null : FindDeclaration(target.BoundCompilation?.SyntaxTree, import.ExportedName);
                items[import.LocalName] = CompletionItem(import.LocalName, declaration?.Kind ?? 13, declaration?.Detail ?? "imported from " + import.Specifier);
            }
        }
        return new { isIncomplete = false, items = items.Values.ToArray() };
    }

    public object? Definition(string uri, JsonElement position)
    {
        ReloadIfOwnershipChanged();
        if (!TryGetCopelandDocument(uri, out DocumentSnapshot? document)) return null;
        DocumentSnapshot current = document!;
        CopelandProjectCompilation project = CompileProject();
        CopelandCompilation compilation = Compile(current, project);
        SyntaxToken? token = TokenAt(compilation.SyntaxTree, ToOffset(current.Text, position));
        if (token is null || token.Kind != SyntaxKind.IdentifierToken) return null;
        CopelandProjectModuleCompilation? sourceModule = project.Modules.FirstOrDefault(module => PathsEqual(module.Source.SourcePath, current.Path));
        CopelandProjectImport? import = sourceModule?.Imports.FirstOrDefault(candidate => candidate.LocalName == token.Text && candidate.TargetLogicalPath is not null);
        if (import?.TargetLogicalPath is not null)
        {
            CopelandProjectModuleCompilation? target = project.Modules.FirstOrDefault(module => module.LogicalPath == import.TargetLogicalPath);
            SyntaxToken? importedDeclaration = target?.BoundCompilation?.SyntaxTree.Tokens.FirstOrDefault(candidate => candidate.Kind == SyntaxKind.IdentifierToken && candidate.Text == import.ExportedName);
            if (target is not null && importedDeclaration is not null)
            {
                return new { uri = new Uri(target.Source.SourcePath).AbsoluteUri, range = Range(target.Source.SourceText, importedDeclaration.Position, importedDeclaration.Text.Length) };
            }
        }
        Symbol? externalSymbol = FindSymbol(compilation, token.Text);
        object? externalDefinition = ExternalDefinition(externalSymbol);
        if (externalDefinition is not null) return externalDefinition;
        SyntaxToken? declaration = compilation.SyntaxTree?.Tokens.FirstOrDefault(candidate => candidate.Kind == SyntaxKind.IdentifierToken && candidate.Text == token.Text);
        return declaration is null ? null : new { uri, range = Range(current.Text, declaration.Position, declaration.Text.Length) };
    }

    public object[] DocumentSymbols(string uri)
    {
        ReloadIfOwnershipChanged();
        if (!TryGetCopelandDocument(uri, out DocumentSnapshot? document) || IsWorkspaceManifest(document!.Path)) return [];
        CopelandCompilation compilation = Compile(document!);
        if (compilation.SyntaxTree is null) return [];
        return Declarations(compilation.SyntaxTree)
            .Select(declaration => new { name = declaration.Name, detail = declaration.Detail, kind = declaration.Kind, range = Range(document.Text, declaration.Position, declaration.Name.Length), selectionRange = Range(document.Text, declaration.Position, declaration.Name.Length) })
            .ToArray<object>();
    }

    public object SemanticTokens(string uri)
    {
        ReloadIfOwnershipChanged();
        if (!TryGetCopelandDocument(uri, out DocumentSnapshot? document) || IsWorkspaceManifest(document!.Path)) return new { data = Array.Empty<int>() };
        CopelandCompilation compilation = Compile(document!);
        if (compilation.SyntaxTree is null) return new { data = Array.Empty<int>() };
        var data = new List<int>();
        int previousLine = 0;
        int previousCharacter = 0;
        foreach (SyntaxToken token in compilation.SyntaxTree.Tokens.Where(token => token.Kind != SyntaxKind.EndOfFileToken))
        {
            int kind = TokenKind(token, compilation);
            if (kind < 0) continue;
            (int line, int character) = LineCharacter(document.Text, token.Position);
            data.Add(line - previousLine);
            data.Add(line == previousLine ? character - previousCharacter : character);
            data.Add(Math.Max(1, token.Text.Length));
            data.Add(kind);
            data.Add(0);
            previousLine = line;
            previousCharacter = character;
        }
        return new { data };
    }

    public object? SignatureHelp(string uri, JsonElement position)
    {
        ReloadIfOwnershipChanged();
        if (!TryGetCopelandDocument(uri, out DocumentSnapshot? document)) return null;
        DocumentSnapshot current = document!;
        CopelandCompilation compilation = Compile(current);
        int offset = ToOffset(current.Text, position);
        SyntaxToken? name = compilation.SyntaxTree?.Tokens.LastOrDefault(token => token.Position < offset && token.Kind == SyntaxKind.IdentifierToken);
        if (name is null || FindSymbol(compilation, name.Text) is not FunctionSymbol function) return null;
        return new { signatures = new[] { new { label = Describe(function), parameters = function.Parameters.Select(parameter => new { label = parameter.Name + ": " + parameter.Type.Name }).ToArray() } }, activeSignature = 0, activeParameter = 0 };
    }

    private void LoadOwnership()
    {
        _owners.Clear();
        _loadError = null;
        if (_ownershipPath is null || !File.Exists(_ownershipPath))
        {
            _loadError = "CTS-LSP-OWNERSHIP: ownership metadata is missing. Run `tscl workspace sync`.";
            return;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_ownershipPath));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out JsonElement schema) || schema.GetInt32() != OwnershipSchemaVersion)
            {
                _loadError = "CTS-LSP-OWNERSHIP: unsupported ownership metadata schema.";
                return;
            }
            foreach (JsonElement file in root.GetProperty("files").EnumerateArray())
            {
                string relative = file.GetProperty("path").GetString() ?? string.Empty;
                string owner = file.GetProperty("owner").GetString() ?? string.Empty;
                if (owner is not ("tscl" or "tsc")) throw new LanguageServerException("CTS-LSP-OWNERSHIP: invalid file owner in ownership metadata.");
                _owners[Path.GetFullPath(relative, _rootPath!)] = owner;
            }
            _ownershipLastWriteUtc = File.GetLastWriteTimeUtc(_ownershipPath);
        }
        catch (Exception exception) when (exception is IOException or JsonException or LanguageServerException)
        {
            _loadError = "CTS-LSP-OWNERSHIP: " + exception.Message;
        }
    }

    private void ReloadIfOwnershipChanged()
    {
        if (_ownershipPath is null || !File.Exists(_ownershipPath)) return;
        bool ownershipChanged = File.GetLastWriteTimeUtc(_ownershipPath) != _ownershipLastWriteUtc;
        bool projectChanged = _projectPath is not null && File.Exists(_projectPath) && File.GetLastWriteTimeUtc(_projectPath) != _projectLastWriteUtc;
        if (!ownershipChanged && !projectChanged) return;
        if (ownershipChanged) LoadOwnership();
        if (ownershipChanged || projectChanged) LoadProjectModel();
        RebuildSnapshot();
    }

    private void LoadProjectModel()
    {
        _evaluatedProject = null;
        string? projectPath = _projectPath;
        if (_projectPath is null && _rootPath is not null)
        {
            string? ownershipProject = TryGetOwnershipProject();
            if (ownershipProject is not null) projectPath = Path.GetFullPath(ownershipProject, _rootPath);
        }
        if (string.IsNullOrWhiteSpace(projectPath)) return;
        if (!File.Exists(projectPath))
        {
            _loadError = "CTS-LSP-PROJECT: declared project '" + projectPath + "' does not exist.";
            return;
        }
        try
        {
            _evaluatedProject = CopelandProjectModelLoader.Load(projectPath);
            _projectPath = projectPath;
            _projectLastWriteUtc = File.GetLastWriteTimeUtc(projectPath);
            _profile = _evaluatedProject.Options.TsXmlProfile;
        }
        catch (Exception exception)
        {
            _loadError = "CTS-LSP-PROJECT: " + exception.Message;
        }
    }

    private void RebuildSnapshot()
    {
        if (_rootPath is null || _loadError is not null)
        {
            _snapshot = null;
            return;
        }

        IEnumerable<CopelandProjectSource> evaluatedSources = _evaluatedProject?.Sources ?? [];
        var sources = new List<CopelandProjectSource>();
        foreach ((string path, string owner) in _owners.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (owner != "tscl" || !File.Exists(path)) continue;
            DocumentSnapshot? document = _documents.Values.FirstOrDefault(candidate => PathsEqual(candidate.Path, path));
            string sourceText = document?.Text ?? File.ReadAllText(path);
            CopelandProjectSource? evaluatedSource = evaluatedSources.FirstOrDefault(source => PathsEqual(source.SourcePath, path));
            if (_evaluatedProject is not null && evaluatedSource is null) continue;
            sources.Add(new CopelandProjectSource(
                evaluatedSource?.LogicalPath ?? Path.GetRelativePath(_rootPath, path),
                path,
                sourceText));
        }
        _snapshot = CopelandProjectCompiler.CreateSnapshot(sources, new CopelandCompilationOptions
        {
            TargetStage = CopelandCompilationStage.Bound,
            ProjectRoot = _rootPath,
            TsXmlProfile = _evaluatedProject?.Options.TsXmlProfile ?? _profile,
            ClrReferences = _evaluatedProject?.Options.ClrReferences ?? [],
            PackageContracts = _evaluatedProject?.Options.PackageContracts ?? [],
            NpmDependencies = _evaluatedProject?.Options.NpmDependencies,
            JavaScriptHostModules = _evaluatedProject?.Options.JavaScriptHostModules ?? [],
        });
    }

    private string? TryGetOwnershipProject()
    {
        if (_ownershipPath is null || !File.Exists(_ownershipPath)) return null;
        using JsonDocument ownership = JsonDocument.Parse(File.ReadAllText(_ownershipPath));
        return ownership.RootElement.GetProperty("files").EnumerateArray()
            .Where(file => file.GetProperty("owner").GetString() == "tscl")
            .Select(file => file.TryGetProperty("project", out JsonElement project) ? project.GetString() : null)
            .FirstOrDefault(project => !string.IsNullOrWhiteSpace(project));
    }

    private bool TryGetOwnedDocument(string uri, out DocumentSnapshot? document, out string? owner)
    {
        if (!_documents.TryGetValue(uri, out document))
        {
            string? path = UriToPath(uri);
            if (path is null || !File.Exists(path)) { owner = null; return false; }
            document = new DocumentSnapshot(path, 0, File.ReadAllText(path));
        }
        if (IsWorkspaceManifest(document.Path)) { owner = "tscl"; return true; }
        return _owners.TryGetValue(Path.GetFullPath(document.Path), out owner);
    }

    private bool TryGetCopelandDocument(string uri, out DocumentSnapshot? document)
    {
        if (!TryGetOwnedDocument(uri, out document, out string? owner)) return false;
        return owner == "tscl";
    }

    private CopelandProjectCompilation CompileProject() => _snapshot?.CompileToMir() ?? new CopelandProjectCompilation(null, []);

    private CopelandCompilation Compile(DocumentSnapshot document) => Compile(document, CompileProject());

    private static CopelandCompilation Compile(DocumentSnapshot document, CopelandProjectCompilation project)
    {
        CopelandProjectModuleCompilation? module = project.Modules.FirstOrDefault(candidate => PathsEqual(candidate.Source.SourcePath, document.Path));
        IReadOnlyList<Diagnostic> diagnostics = project.Diagnostics
            .Where(diagnostic => diagnostic.SourcePath is null || PathsEqual(diagnostic.SourcePath, document.Path))
            .ToArray();
        return new CopelandCompilation(
            CopelandCompilationStage.Bound,
            diagnostics,
            module?.BoundCompilation?.SyntaxTree,
            module?.BoundCompilation,
            null,
            null);
    }
    private bool IsWorkspaceManifest(string path) => _rootPath is not null && PathsEqual(path, Path.Combine(_rootPath, "tsconfig.tsx"));
    private static string? ReadOptionalString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? UriToPath(string? uri) => uri is not null && Uri.TryCreate(uri, UriKind.Absolute, out Uri? value) && value.IsFile ? value.LocalPath : null;
    private static bool PathsEqual(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static Symbol? FindSymbol(CopelandCompilation compilation, string name)
    {
        if (compilation.BoundCompilation?.ModuleScope?.Declarations.TryGetValue(name, out Symbol? symbol) == true) return symbol;
        BoundProgram? program = compilation.BoundCompilation?.Program;
        return program?.NpmImports.Select(import => (Symbol)import.Function).FirstOrDefault(symbol => symbol.Name == name)
            ?? program?.PackageImports.Select(import => (Symbol)import.Function).FirstOrDefault(symbol => symbol.Name == name)
            ?? program?.JavaScriptHostImports.Select(import => (Symbol)import.Function).FirstOrDefault(symbol => symbol.Name == name)
            ?? program?.NpmComponentImports.Select(import => (Symbol)import.Component).FirstOrDefault(symbol => symbol.Name == name);
    }
    private static DeclarationInfo? FindDeclaration(SyntaxTree? tree, string name) => Declarations(tree).FirstOrDefault(declaration => declaration.Name == name);
    private static IEnumerable<DeclarationInfo> Declarations(SyntaxTree? tree)
    {
        if (tree is null) yield break;
        foreach (MemberSyntax member in tree.Root.Members)
        {
            switch (member)
            {
                case FunctionDeclarationSyntax function:
                    yield return new DeclarationInfo(function.Identifier.Text, "function " + function.Identifier.Text, 12, function.Identifier.Position);
                    break;
                case RecordDeclarationSyntax record:
                    yield return new DeclarationInfo(record.Identifier.Text, "record " + record.Identifier.Text, 23, record.Identifier.Position);
                    foreach (RecordFieldSyntax field in record.Fields) yield return new DeclarationInfo(field.Identifier.Text, "field of " + record.Identifier.Text, 7, field.Identifier.Position);
                    break;
                case EnumDeclarationSyntax @enum:
                    yield return new DeclarationInfo(@enum.Identifier.Text, "enum " + @enum.Identifier.Text, 10, @enum.Identifier.Position);
                    foreach (EnumCaseSyntax @case in @enum.Cases) yield return new DeclarationInfo(@case.Identifier.Text, "case of " + @enum.Identifier.Text, 20, @case.Identifier.Position);
                    break;
                case TableDeclarationSyntax table:
                    yield return new DeclarationInfo(table.Identifier.Text, "record table " + table.Identifier.Text, 23, table.Identifier.Position);
                    foreach (TableColumnSyntax column in table.Columns) yield return new DeclarationInfo(column.Identifier.Text, "column of table " + table.Identifier.Text, 7, column.Identifier.Position);
                    break;
            }
        }
    }
    private static int DeclarationPosition(SyntaxTree? tree, string name) => tree?.Tokens.FirstOrDefault(token => token.Kind == SyntaxKind.IdentifierToken && token.Text == name)?.Position ?? 0;
    private static SyntaxToken? TokenAt(SyntaxTree? tree, int offset) => tree?.Tokens.FirstOrDefault(token => offset >= token.Position && offset <= token.Position + token.Text.Length);
    private static int CompletionKind(Symbol symbol) => symbol switch { FunctionSymbol => 3, VariableSymbol => 6, ParameterSymbol => 6, _ => 13 };
    private static object CompletionItem(string label, int kind, string detail) => new { label, kind, detail };
    private static string Describe(Symbol symbol) => symbol switch
    {
        FunctionSymbol function => (function.IsRemote ? "remote " : string.Empty) + "function " + function.Name + "(" + string.Join(", ", function.Parameters.Select(parameter => parameter.Name + ": " + parameter.Type.Name)) + "): " + function.InvocationReturnType.Name,
        NpmFunctionSymbol function => "npm function " + function.Name + "(" + string.Join(", ", function.Parameters.Select(parameter => parameter.Name + ": " + parameter.Type.Name)) + "): " + function.InvocationReturnType.Name,
        CopelandPackageFunctionSymbol function => "package function " + function.Name + "(" + string.Join(", ", function.Parameters.Select(parameter => parameter.Name + ": " + parameter.Type.Name)) + "): " + function.ReturnType.Name,
        JavaScriptHostFunctionSymbol function => "host function " + function.Name + "(" + string.Join(", ", function.Parameters.Select(parameter => parameter.Name + ": " + parameter.Type.Name)) + "): " + function.ReturnType.Name,
        NpmComponentSymbol component => "npm component " + component.Name + " from " + component.PackageName + "@" + component.PackageVersion,
        VariableSymbol variable => variable.Name + ": " + variable.Type.Name,
        ParameterSymbol parameter => parameter.Name + ": " + parameter.Type.Name,
        _ => symbol.Name,
    };

    private static int TokenKind(SyntaxToken token, CopelandCompilation compilation)
    {
        if (token.Kind.ToString().EndsWith("Keyword", StringComparison.Ordinal)) return 0;
        Symbol? symbol = FindSymbol(compilation, token.Text);
        return symbol switch { FunctionSymbol => 4, VariableSymbol => 6, ParameterSymbol => 6, _ when token.Kind == SyntaxKind.IdentifierToken => 6, _ => -1 };
    }

    private string? DescribeClrType(string name)
    {
        if (_snapshot is null) return null;
        Type[] types = new CopelandClrMetadataResolver(_snapshot.Options.ClrReferences)
            .FindTypesBySimpleName(name)
            .Take(2)
            .ToArray();
        return types.Length == 1 ? "CLR type " + types[0].FullName : null;
    }

    private void AddClrCompletionItems(Dictionary<string, object> items, string text, int offset)
    {
        if (_snapshot is null) return;
        string line = text[..Math.Clamp(offset, 0, text.Length)].Split('\n').Last();
        int usingIndex = line.LastIndexOf("using ", StringComparison.Ordinal);
        if (usingIndex < 0) return;
        string candidate = line[(usingIndex + "using ".Length)..].Trim();
        if (!candidate.EndsWith(".", StringComparison.Ordinal)) return;
        string @namespace = candidate[..^1];
        CopelandClrMetadataResolver resolver = new(_snapshot.Options.ClrReferences);
        foreach (string childNamespace in resolver.FindNamespaceChildren(@namespace))
        {
            items[childNamespace] = CompletionItem(childNamespace, 9, "CLR namespace " + @namespace + "." + childNamespace);
        }
        foreach (Type type in resolver.FindTypesInNamespace(@namespace).OrderBy(type => type.Name, StringComparer.Ordinal).Take(100))
        {
            items[type.Name] = CompletionItem(type.Name, 7, "CLR type " + type.FullName);
        }
    }

    private object? ExternalDefinition(Symbol? symbol)
    {
        if (_snapshot is null || symbol is null) return null;
        string? path = symbol switch
        {
            CopelandPackageFunctionSymbol package => _snapshot.Options.PackageContracts
                .FirstOrDefault(contract => contract.PackageId == package.PackageId)?.SourcePath,
            NpmFunctionSymbol npm => _snapshot.Options.NpmDependencies?.Packages
                .FirstOrDefault(contract => contract.PackageName == npm.PackageName)?.SourcePath,
            NpmComponentSymbol npm => _snapshot.Options.NpmDependencies?.Packages
                .FirstOrDefault(contract => contract.PackageName == npm.PackageName)?.SourcePath,
            _ => null,
        };
        return string.IsNullOrWhiteSpace(path) || !File.Exists(path)
            ? null
            : new { uri = new Uri(path).AbsoluteUri, range = Range(File.ReadAllText(path), 0, 1) };
    }

    private static object DiagnosticObject(string message, string code, int severity, string text, int start, int length) => new { range = Range(text, start, length), severity, code, source = "tscl", message };
    private static object[] WorkspaceManifestDiagnostics(string text) => text.Contains("tscl", StringComparison.Ordinal) ? [] : [DiagnosticObject("CTS-LSP-WORKSPACE: workspace manifest does not declare tscl ownership.", "CTS-LSP-WORKSPACE", 1, text, 0, 1)];
    private static object? ManifestHover(string text, int offset) => offset >= 0 && offset <= text.Length ? new { contents = new { kind = "markdown", value = "`tsconfig.tsx` declares explicit tsc/tscl source ownership. Run `tscl workspace sync` after changes." } } : null;
    private static object Range(string text, int start, int length) => new { start = Position(text, start), end = Position(text, Math.Min(text.Length, Math.Max(start + Math.Max(1, length), 0))) };
    private static object Position(string text, int offset) { (int line, int character) = LineCharacter(text, offset); return new { line, character }; }
    private static (int Line, int Character) LineCharacter(string text, int offset)
    {
        int safeOffset = Math.Clamp(offset, 0, text.Length); int line = 0; int character = 0;
        for (int index = 0; index < safeOffset; index += 1) { if (text[index] == '\n') { line += 1; character = 0; } else { character += 1; } }
        return (line, character);
    }
    private static int ToOffset(string text, JsonElement position)
    {
        int line = position.GetProperty("line").GetInt32(); int character = position.GetProperty("character").GetInt32(); int currentLine = 0; int offset = 0;
        while (offset < text.Length && currentLine < line) { if (text[offset++] == '\n') currentLine += 1; }
        return Math.Min(text.Length, offset + character);
    }

    private sealed record DocumentSnapshot(string Path, int Version, string Text);
    private sealed record DeclarationInfo(string Name, string Detail, int Kind, int Position);
}
