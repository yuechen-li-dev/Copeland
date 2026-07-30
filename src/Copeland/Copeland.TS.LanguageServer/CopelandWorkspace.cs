using System.Text.Json;
using Copeland.TS.Compiler;
using Copeland.TS.Diagnostics;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Copeland.TS.MSBuild;
using Copeland.TS.MachinaSource;
using Copeland.TS.Mir.Machina;

namespace Copeland.TS.LanguageServer;

/// <summary>
/// Resident, editor-neutral project snapshot. Open buffers overlay disk content
/// over either a manifest-resolved compiler context or a legacy MSBuild project.
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
    private string? _manifestPath;
    private DateTime _manifestLastWriteUtc;
    private CopelandProjectContext? _manifestProjectContext;
    private DateTime _manifestContextLastWriteUtc;
    private string? _manifestContextDirectory;
    private DateTime _manifestContextDirectoryLastWriteUtc;

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
        if (!TryLoadManifestProjectContext())
        {
            LoadOwnership();
            LoadProjectModel();
        }

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
        IReadOnlyList<Diagnostic> projectDiagnostics = Compile(document).Diagnostics;
        IReadOnlyList<Diagnostic> syntaxDiagnostics = SyntaxTree.Parse(document.Text, document.Path).Diagnostics;
        IReadOnlyList<Diagnostic> layoutDiagnostics = LayoutDataCompiler.Compile(document.Text, document.Path).Diagnostics;
        return projectDiagnostics
            .Concat(syntaxDiagnostics)
            .Concat(layoutDiagnostics)
            .DistinctBy(diagnostic => (diagnostic.Id, diagnostic.Position, diagnostic.Length, diagnostic.Message))
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
        LayoutSlotSymbol? bindingSlot = FindBindingSlot(compilation, token);
        string? contentPolicyContents = DescribeContentPolicyAt(compilation, token.Position);
        string? tableContents = DescribeTableCellAt(compilation, token.Position);
        string? paintContents = DescribePaintAt(compilation, token.Position);
        string? relativeContents = DescribeRelativeDerivationAt(compilation.SyntaxTree, token);
        Symbol? symbol = FindSymbol(compilation, token.Text);
        DeclarationInfo? declaration = FindDeclaration(compilation.SyntaxTree, token.Text);
        string contents = relativeContents ?? contentPolicyContents ?? tableContents ?? paintContents ?? (bindingSlot is not null
            ? "slot " + bindingSlot.SemanticPath + "\ncardinality: exactly one renderable component/view\nhost: compiler-generated div layout region"
            : symbol is LayoutSymbol { BoundLayout: not null } layout
            ? DescribeLayout(layout)
            : symbol is LayoutTypeSymbol { BoundLayoutType: not null } layoutType
                ? DescribeLayoutType(layoutType)
            : symbol is not null ? Describe(symbol) : declaration?.Detail ?? (token.Kind == SyntaxKind.IdentifierToken ? token.Text : string.Empty));
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
        if (IsAwaitingLayoutOrigin(document.Text, compilation.SyntaxTree, ToOffset(document.Text, position)))
        {
            items["<0px, 0px>"] = new
            {
                label = "<0px, 0px>",
                kind = 15,
                detail = "required layout origin",
                insertText = "<${1:0px}, ${2:0px}>",
                insertTextFormat = 2,
            };
        }
        foreach (string keyword in new[] { "function", "template", "static", "type", "record", "layout", "layers", "stream", "satisfies", "bind", "csv", "row", "column", "grid", "anchor", "overlay", "slot", "with", "width", "height", "gap", "padding", "layer", "z", "overflow", "visible", "clip", "auto", "scroll", "scrollX", "scrollY", "fontSize", "minFontSize", "lines", "wrap", "textFit", "scaleDown", "textFallback", "ellipsis", "fill", "fit", "enum", "match", "return", "const", "let", "using", "import", "export", "async", "remote", "fieldsOf", "nameOf" })
        {
            items[keyword] = CompletionItem(keyword, 14, "keyword");
        }
        foreach (string column in new[] { "name", "content", "x", "y", "width", "height", "derivations", "layer", "z" })
        {
            items[column] = CompletionItem(column, 10, "CSV overlay column: " + TableColumnExpectation(column));
        }
        BoundModuleScope? scope = compilation.BoundCompilation?.ModuleScope;
        if (scope is not null)
        {
            foreach (Symbol symbol in scope.Declarations.Values.OrderBy(symbol => symbol.Name, StringComparer.Ordinal))
            {
                items[symbol.Name] = CompletionItem(symbol.Name, CompletionKind(symbol), Describe(symbol));
                if (symbol is LayerSetSymbol { BoundLayerSet: not null } layerSet)
                {
                    foreach (string layer in layerSet.BoundLayerSet.Layers)
                    {
                        items[layer] = CompletionItem(layer, 13, "semantic layer of " + layerSet.Name);
                    }
                }
            }
        }
        AddLayoutBindingSlotCompletions(items, compilation, ToOffset(document.Text, position));
        AddRelativeLayoutCompletions(items, compilation.SyntaxTree);
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
        LayoutSlotSymbol? bindingSlot = FindBindingSlot(compilation, token);
        if (bindingSlot is not null)
        {
            return new
            {
                uri = new Uri(bindingSlot.Source.SourcePath).AbsoluteUri,
                range = Range(bindingSlot.Source.SourcePath == current.Path ? current.Text : File.ReadAllText(bindingSlot.Source.SourcePath), bindingSlot.Source.Start, bindingSlot.Name.Length),
            };
        }
        if (TryFindRelativeSourceDefinition(compilation.SyntaxTree, token, out SyntaxToken? boxDeclaration))
        {
            return new { uri, range = Range(current.Text, boxDeclaration!.Position, boxDeclaration.Text.Length) };
        }
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
        if (externalSymbol is LayoutSymbol layout)
        {
            CopelandProjectModuleCompilation? target = project.Modules.FirstOrDefault(module =>
                module.BoundCompilation?.ModuleScope?.Declarations.Values.OfType<LayoutSymbol>().Any(candidate => ReferenceEquals(candidate, layout)) == true);
            if (target is not null)
            {
                SyntaxToken identifier = layout.Declaration?.Identifier ?? layout.StreamDeclaration?.Identifier ?? token;
                return new { uri = new Uri(target.Source.SourcePath).AbsoluteUri, range = Range(target.Source.SourceText, identifier.Position, identifier.Text.Length) };
            }
        }
        if (externalSymbol is LayoutTypeSymbol layoutType)
        {
            CopelandProjectModuleCompilation? target = project.Modules.FirstOrDefault(module =>
                module.BoundCompilation?.ModuleScope?.Declarations.Values.OfType<LayoutTypeSymbol>().Any(candidate => ReferenceEquals(candidate, layoutType)) == true);
            if (target is not null && layoutType.Declaration is not null)
            {
                return new { uri = new Uri(target.Source.SourcePath).AbsoluteUri, range = Range(target.Source.SourceText, layoutType.Declaration.Identifier.Position, layoutType.Declaration.Identifier.Text.Length) };
            }
        }
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
        int previousTokenEnd = -1;
        foreach (SyntaxToken token in compilation.SyntaxTree.Tokens
            .Where(token => token.Kind != SyntaxKind.EndOfFileToken)
            .OrderBy(token => token.Position)
            .ThenByDescending(token => token.Text.Length))
        {
            if (token.Position < previousTokenEnd)
            {
                continue;
            }

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
            previousTokenEnd = token.Position + Math.Max(1, token.Text.Length);
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
        if (_manifestPath is not null)
        {
            bool manifestChanged = File.Exists(_manifestPath) &&
                File.GetLastWriteTimeUtc(_manifestPath) != _manifestLastWriteUtc;
            bool contextChanged = _manifestContextDirectory is not null &&
                Directory.Exists(_manifestContextDirectory) &&
                Directory.GetLastWriteTimeUtc(_manifestContextDirectory) != _manifestContextDirectoryLastWriteUtc;
            if (!manifestChanged && !contextChanged)
            {
                return;
            }

            TryLoadManifestProjectContext();
            RebuildSnapshot();
            return;
        }

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

    private bool TryLoadManifestProjectContext()
    {
        _manifestProjectContext = null;
        _manifestPath = _rootPath is null
            ? null
            : CopelandProjectContextResolver.DiscoverManifest(_rootPath);
        if (_manifestPath is null)
        {
            return false;
        }

        _owners.Clear();
        _loadError = null;
        try
        {
            _manifestContextDirectory = Path.Combine(
                Path.GetDirectoryName(_manifestPath)!,
                ".tspack",
                "build-manifests");
            _manifestContextDirectoryLastWriteUtc = Directory.Exists(_manifestContextDirectory)
                ? Directory.GetLastWriteTimeUtc(_manifestContextDirectory)
                : DateTime.MinValue;
            CopelandProjectContext context = CopelandProjectContextResolver.Load(_manifestPath);
            _manifestProjectContext = context;
            _manifestLastWriteUtc = File.GetLastWriteTimeUtc(_manifestPath);
            _manifestContextLastWriteUtc = File.GetLastWriteTimeUtc(context.DescriptorPath);
            _profile = context.Options.TsXmlProfile;
            foreach (CopelandProjectSource source in context.Sources)
            {
                _owners[Path.GetFullPath(source.SourcePath)] = "tscl";
            }

            return true;
        }
        catch (CopelandProjectContextException exception)
        {
            _loadError = exception.Code + ": " + exception.Message;
            return true;
        }
    }

    private void RebuildSnapshot()
    {
        if (_rootPath is null || _loadError is not null)
        {
            _snapshot = null;
            return;
        }

        if (_manifestProjectContext is not null)
        {
            var overlays = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DocumentSnapshot document in _documents.Values)
            {
                overlays[Path.GetFullPath(document.Path)] = document.Text;
            }

            _snapshot = _manifestProjectContext.CreateSnapshot(overlays);
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
        if (_owners.TryGetValue(Path.GetFullPath(document.Path), out owner))
        {
            return true;
        }

        foreach ((string path, string candidateOwner) in _owners)
        {
            if (!PathsEqual(path, document.Path))
            {
                continue;
            }

            owner = candidateOwner;
            return true;
        }

        owner = null;
        return false;
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
    private bool IsWorkspaceManifest(string path)
        => _rootPath is not null &&
            (PathsEqual(path, Path.Combine(_rootPath, "tsconfig.tsx")) ||
             (_manifestPath is not null && PathsEqual(path, _manifestPath)));
    private static string? ReadOptionalString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? UriToPath(string? uri)
    {
        if (uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out Uri? value) || !value.IsFile)
        {
            return null;
        }

        string localPath = value.LocalPath;
        if (localPath.Length >= 3 && localPath[0] == '/' && localPath[2] == ':')
        {
            localPath = localPath[1..];
        }

        return Path.GetFullPath(localPath);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path)
    {
        return UriToPath(path) ?? Path.GetFullPath(path);
    }

    private static Symbol? FindSymbol(CopelandCompilation compilation, string name)
    {
        if (compilation.BoundCompilation?.ModuleScope?.Declarations.TryGetValue(name, out Symbol? symbol) == true) return symbol;
        BoundProgram? program = compilation.BoundCompilation?.Program;
        return program?.NpmImports.Select(import => (Symbol)import.Function).FirstOrDefault(symbol => symbol.Name == name)
            ?? program?.PackageImports.Select(import => (Symbol)import.Function).FirstOrDefault(symbol => symbol.Name == name)
            ?? program?.JavaScriptHostImports.Select(import => (Symbol)import.Function).FirstOrDefault(symbol => symbol.Name == name)
            ?? program?.NpmComponentImports.Select(import => (Symbol)import.Component).FirstOrDefault(symbol => symbol.Name == name);
    }

    private static string? DescribeContentPolicyAt(CopelandCompilation compilation, int position)
    {
        SyntaxTree? tree = compilation.SyntaxTree;
        if (tree is null) return null;
        foreach (StreamDeclarationSyntax stream in tree.Root.Members.OfType<StreamDeclarationSyntax>())
        {
            BoundLayoutDeclaration? layout = compilation.BoundCompilation?.Program.Layouts.SingleOrDefault(candidate => candidate.Name == stream.Identifier.Text);
            if (layout is null) continue;
            foreach (StreamNodeSyntax node in Enumerate(stream.Nodes))
            {
                LayoutPropertySyntax? property = node.Properties.FirstOrDefault(candidate =>
                    position >= candidate.Identifier.Position && position <= FirstToken(candidate.Value).Position + Math.Max(1, FirstToken(candidate.Value).Text.Length));
                if (property is null) continue;
                BoundLayoutNode? bound = Find(layout.Root, node.Identifier.Text);
                if (bound is null) return null;
                if (property.Identifier.Text == "overflow")
                {
                    BoundBoxOverflowPolicy overflow = bound.ResolvedOverflow;
                    return "overflow policy: " + overflow.Policy.ToString().ToLowerInvariant()
                        + "\noverflowX: " + overflow.X.ToString().ToLowerInvariant()
                        + "\noverflowY: " + overflow.Y.ToString().ToLowerInvariant()
                        + "\nbox: " + node.Identifier.Text;
                }
                if (bound.TextFit is not null && property.Identifier.Text is "fontSize" or "minFontSize" or "lines" or "wrap" or "textFit" or "textFallback")
                {
                    BoundTextFitPolicy text = bound.TextFit;
                    return "text region: " + node.Identifier.Text
                        + "\npreferred: " + text.PreferredFontSize.Px.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px"
                        + "\nminimum: " + text.MinimumFontSize.Px.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px"
                        + "\nlines: " + text.MaximumLines
                        + "\nwrap: " + text.Wrap.ToString().ToLowerInvariant()
                        + "\nfit: " + text.Fit.ToString().ToLowerInvariant()
                        + "\nfallback: " + text.Fallback.ToString().ToLowerInvariant()
                        + "\nprojected relation: text::Regions";
                }
            }
        }
        return null;

        static IEnumerable<StreamNodeSyntax> Enumerate(IEnumerable<StreamNodeSyntax> nodes)
        {
            foreach (StreamNodeSyntax node in nodes)
            {
                yield return node;
                foreach (StreamNodeSyntax child in Enumerate(node.Children)) yield return child;
            }
        }

        static BoundLayoutNode? Find(BoundLayoutNode node, string name)
        {
            if (node.Name == name) return node;
            foreach (BoundLayoutNode child in node.Children)
            {
                BoundLayoutNode? found = Find(child, name);
                if (found is not null) return found;
            }
            return null;
        }
    }

    private static string? DescribePaintAt(CopelandCompilation compilation, int position)
    {
        foreach (BoundLayoutDeclaration layout in compilation.BoundCompilation?.Program.Layouts ?? [])
        {
            string? description = Find(LayoutDataCompiler.Normalize(layout).Root);
            if (description is not null) return description;

            string? Find(NormalizedLayoutNode node)
            {
                if (node.Source is { } source && position >= source.Start && position <= source.Start + source.Length)
                {
                    return "semantic path: " + node.StableIdentity
                        + "\nlayer: " + node.LayerIdentity
                        + "\nlocal z: " + node.LocalZ
                        + "\nauthored order: " + node.AuthoredNodeOrder
                        + "\nresolved paint rank: (" + node.PaintOrder.LayerRank + ", " + node.PaintOrder.LocalZ + ", " + node.PaintOrder.AuthoredNodeOrder + ")";
                }
                foreach (NormalizedLayoutNode child in node.Children)
                {
                    string? nested = Find(child);
                    if (nested is not null) return nested;
                }
                return null;
            }
        }
        return null;
    }

    private static string? DescribeTableCellAt(CopelandCompilation compilation, int position)
    {
        SyntaxTree? tree = compilation.SyntaxTree;
        if (tree is null) return null;
        foreach (StreamDeclarationSyntax stream in tree.Root.Members.OfType<StreamDeclarationSyntax>())
        {
            foreach (StreamTableSyntax table in EnumerateTables(stream.Nodes, stream.Tables))
            {
                foreach (SyntaxToken header in table.Headers)
                {
                    if (header.Position == position)
                    {
                        return "CSV overlay column '" + header.Text + "'\nexpected: " + TableColumnExpectation(header.Text);
                    }
                }
                for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    StreamTableRowSyntax row = table.Rows[rowIndex];
                    int nameIndex = TableColumnIndex(table, "name");
                    string rowName = nameIndex >= 0 && nameIndex < row.Cells.Count && row.Cells[nameIndex] is NameExpressionSyntax name
                        ? name.IdentifierToken.Text
                        : "row " + (rowIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    for (int columnIndex = 0; columnIndex < row.Cells.Count && columnIndex < table.Headers.Count; columnIndex++)
                    {
                        if (FirstToken(row.Cells[columnIndex]).Position != position) continue;
                        string column = table.Headers[columnIndex].Text;
                        string normalized = DescribeNormalizedBox(compilation, stream.Identifier.Text, rowName);
                        return "row: " + rowName
                            + "\nparent: " + stream.Identifier.Text + "." + table.Identifier.Text
                            + "\ncolumn: " + column
                            + "\nexpected: " + TableColumnExpectation(column)
                            + (normalized.Length == 0 ? string.Empty : "\n" + normalized);
                    }
                }
            }
        }
        return null;
    }

    private static string DescribeNormalizedBox(CopelandCompilation compilation, string layoutName, string boxName)
    {
        BoundLayoutDeclaration? layout = compilation.BoundCompilation?.Program.Layouts
            .SingleOrDefault(candidate => candidate.Name == layoutName);
        if (layout is null) return string.Empty;
        NormalizedLayoutNode? box = Find(LayoutDataCompiler.Normalize(layout).Root);
        if (box is null) return string.Empty;
        return "semantic path: " + box.StableIdentity
            + "\nlayer: " + box.LayerIdentity
            + "\nlocal z: " + box.LocalZ
            + "\nauthored order: " + box.AuthoredNodeOrder
            + "\npaint order: (" + box.PaintOrder.LayerRank + ", " + box.PaintOrder.LocalZ + ", " + box.PaintOrder.AuthoredNodeOrder + ")";

        NormalizedLayoutNode? Find(NormalizedLayoutNode node)
        {
            if (node.Name == boxName) return node;
            foreach (NormalizedLayoutNode child in node.Children)
            {
                NormalizedLayoutNode? found = Find(child);
                if (found is not null) return found;
            }
            return null;
        }
    }

    private static IEnumerable<StreamTableSyntax> EnumerateTables(IEnumerable<StreamNodeSyntax> nodes, IEnumerable<StreamTableSyntax> tables)
    {
        foreach (StreamTableSyntax table in tables) yield return table;
        foreach (StreamNodeSyntax node in nodes)
        {
            foreach (StreamTableSyntax table in EnumerateTables(node.Children, node.Tables)) yield return table;
        }
    }

    private static string TableColumnExpectation(string column) => column switch
    {
        "name" => "semantic box identifier",
        "content" => "renderable ReactNode expression",
        "x" or "y" => "px or ui coordinate",
        "width" or "height" => "length, fill, fit, or derived",
        "derivations" => "static list of compiler-known relative layout transforms",
        "layer" => "symbol from the active layer set",
        "z" => "integral value from -5 through 5",
        _ => "supported CSV overlay column",
    };
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
                case TemplateDeclarationSyntax template:
                    string parameters = string.Join(", ", template.Parameters.Select(parameter => (parameter.StaticKeyword is null ? string.Empty : "static ") + parameter.Identifier.Text + ": " + (parameter.Type?.ToString() ?? "<missing>")));
                    yield return new DeclarationInfo(template.Identifier.Text, "template " + template.Identifier.Text + "(" + parameters + "): ProjectTree", 12, template.Identifier.Position);
                    break;
                case TypeAliasDeclarationSyntax alias:
                    yield return new DeclarationInfo(alias.Identifier.Text, "type " + alias.Identifier.Text + " = " + alias.TargetType, 13, alias.Identifier.Position);
                    break;
                case RecordDeclarationSyntax record:
                    yield return new DeclarationInfo(record.Identifier.Text, "record " + record.Identifier.Text, 23, record.Identifier.Position);
                    foreach (RecordFieldSyntax field in record.Fields) yield return new DeclarationInfo(field.Identifier.Text, "field of " + record.Identifier.Text, 7, field.Identifier.Position);
                    break;
                case LayoutDeclarationSyntax layout:
                    yield return new DeclarationInfo(layout.Identifier.Text, "layout " + (layout.Profile is null ? string.Empty : layout.Profile.Text + " ") + layout.Identifier.Text, 23, layout.Identifier.Position);
                    foreach (LayoutNodeSyntax node in layout.Nodes)
                    {
                        foreach (DeclarationInfo slot in LayoutSlots(node, layout.Identifier.Text)) yield return slot;
                    }
                    break;
                case LayerSetDeclarationSyntax layerSet:
                    yield return new DeclarationInfo(layerSet.Identifier.Text, "layers " + layerSet.Identifier.Text, 13, layerSet.Identifier.Position);
                    foreach (SyntaxToken layer in layerSet.Layers) yield return new DeclarationInfo(layer.Text, "semantic layer of " + layerSet.Identifier.Text, 13, layer.Position);
                    break;
                case LayoutTypeDeclarationSyntax layoutType:
                    yield return new DeclarationInfo(layoutType.Identifier.Text, "layout type " + layoutType.Identifier.Text, 13, layoutType.Identifier.Position);
                    foreach (LayoutNodeSyntax node in layoutType.Nodes)
                    {
                        foreach (DeclarationInfo slot in LayoutSlots(node, layoutType.Identifier.Text)) yield return slot;
                    }
                    break;
                case StreamDeclarationSyntax stream:
                    yield return new DeclarationInfo(stream.Identifier.Text, "stream " + stream.Identifier.Text + " (implicit column root)", 12, stream.Identifier.Position);
                    foreach (StreamNodeSyntax node in stream.Nodes)
                    {
                        foreach (DeclarationInfo region in StreamRegions(node, stream.Identifier.Text)) yield return region;
                    }
                    foreach (StreamTableSyntax table in stream.Tables)
                    {
                        foreach (DeclarationInfo region in StreamRegions(table, stream.Identifier.Text)) yield return region;
                    }
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
    private static IEnumerable<DeclarationInfo> LayoutSlots(LayoutNodeSyntax node, string layoutName)
    {
        yield return new DeclarationInfo(node.Identifier.Text, node.KindToken.Text + " slot of " + layoutName, 7, node.Identifier.Position);
        foreach (LayoutNodeSyntax child in node.Children)
        {
            foreach (DeclarationInfo slot in LayoutSlots(child, layoutName)) yield return slot;
        }
    }
    private static IEnumerable<DeclarationInfo> StreamRegions(StreamNodeSyntax node, string streamName)
    {
        yield return new DeclarationInfo(node.Identifier.Text, (node.KindToken?.Text ?? "region") + " of stream " + streamName, 7, node.Identifier.Position);
        foreach (StreamNodeSyntax child in node.Children)
        {
            foreach (DeclarationInfo region in StreamRegions(child, streamName)) yield return region;
        }
        foreach (StreamTableSyntax table in node.Tables)
        {
            foreach (DeclarationInfo region in StreamRegions(table, streamName)) yield return region;
        }
    }
    private static IEnumerable<DeclarationInfo> StreamRegions(StreamTableSyntax table, string streamName)
    {
        yield return new DeclarationInfo(table.Identifier.Text, "CSV overlay of stream " + streamName, 7, table.Identifier.Position);
        int nameIndex = TableColumnIndex(table, "name");
        if (nameIndex < 0) yield break;
        foreach (StreamTableRowSyntax row in table.Rows)
        {
            if (row.Cells.Count != table.Headers.Count || row.Cells[nameIndex] is not NameExpressionSyntax name) continue;
            yield return new DeclarationInfo(name.IdentifierToken.Text, "CSV layout box of stream " + streamName, 7, name.IdentifierToken.Position);
        }
    }
    private static int DeclarationPosition(SyntaxTree? tree, string name) => tree?.Tokens.FirstOrDefault(token => token.Kind == SyntaxKind.IdentifierToken && token.Text == name)?.Position ?? 0;
    private static SyntaxToken? TokenAt(SyntaxTree? tree, int offset) => tree?.Tokens.FirstOrDefault(token => offset >= token.Position && offset <= token.Position + token.Text.Length);
    private static int CompletionKind(Symbol symbol) => symbol switch { FunctionSymbol => 3, VariableSymbol => 6, ParameterSymbol => 6, LayerSetSymbol => 13, _ => 13 };
    private static object CompletionItem(string label, int kind, string detail) => new { label, kind, detail };
    private static string Describe(Symbol symbol) => symbol switch
    {
        LayoutSymbol layout => "layout " + (layout.Profile is null ? string.Empty : layout.Profile + " ") + layout.Name,
        LayoutTypeSymbol layoutType => "layout type " + layoutType.Name,
        LayerSetSymbol layerSet => "layers " + layerSet.Name + " (declaration-ordered semantic paint layers)",
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
        int layoutKind = LayoutTokenKind(compilation.SyntaxTree, token);
        if (layoutKind >= 0) return layoutKind;
        if (token.Kind.ToString().EndsWith("Keyword", StringComparison.Ordinal)) return 0;
        Symbol? symbol = FindSymbol(compilation, token.Text);
        return symbol switch { FunctionSymbol => 4, VariableSymbol => 6, ParameterSymbol => 6, _ when token.Kind == SyntaxKind.IdentifierToken => 6, _ => -1 };
    }

    private static int LayoutTokenKind(SyntaxTree? tree, SyntaxToken token)
    {
        if (tree is null) return -1;
        foreach (LayoutDeclarationSyntax layout in tree.Root.Members.OfType<LayoutDeclarationSyntax>())
        {
            if (SameToken(layout.LayoutKeyword, token) || SameToken(layout.Identifier, token)) return 10;
            if (layout.SatisfiesKeyword is not null && SameToken(layout.SatisfiesKeyword, token)) return 0;
            if (layout.ContractIdentifier is not null && SameToken(layout.ContractIdentifier, token)) return 10;
            if (layout.Profile is not null && SameToken(layout.Profile, token)) return 11;
            if (layout.Origin is not null
                && token.Kind == SyntaxKind.NumberToken
                && token.Position >= layout.Origin.LessToken.Position
                && token.Position < layout.Origin.GreaterToken.Position) return 15;
            foreach (LayoutNodeSyntax node in layout.Nodes)
            {
                int nodeKind = LayoutNodeTokenKind(node, token);
                if (nodeKind >= 0) return nodeKind;
            }
        }
        foreach (LayerSetDeclarationSyntax layerSet in tree.Root.Members.OfType<LayerSetDeclarationSyntax>())
        {
            if (SameToken(layerSet.LayersKeyword, token)) return 0;
            if (SameToken(layerSet.Identifier, token) || layerSet.Layers.Any(layer => SameToken(layer, token))) return 13;
        }
        foreach (LayoutTypeDeclarationSyntax layoutType in tree.Root.Members.OfType<LayoutTypeDeclarationSyntax>())
        {
            if (SameToken(layoutType.LayoutKeyword, token) || SameToken(layoutType.Identifier, token)) return 10;
            if (SameToken(layoutType.TypeKeyword, token)) return 0;
            foreach (LayoutNodeSyntax node in layoutType.Nodes)
            {
                int nodeKind = LayoutNodeTokenKind(node, token);
                if (nodeKind >= 0) return nodeKind;
            }
        }
        foreach (LayoutBindingDeclarationSyntax binding in tree.Root.Members.OfType<LayoutBindingDeclarationSyntax>())
        {
            if (SameToken(binding.BindKeyword, token)) return 0;
            if (SameToken(binding.LayoutIdentifier, token)) return 10;
            if (binding.Entries.Any(entry => SameToken(entry.SlotIdentifier, token))) return 13;
        }
        foreach (StreamDeclarationSyntax stream in tree.Root.Members.OfType<StreamDeclarationSyntax>())
        {
            if (SameToken(stream.StreamKeyword, token) || SameToken(stream.Identifier, token)) return 10;
            if (stream.SatisfiesKeyword is not null && SameToken(stream.SatisfiesKeyword, token)) return 0;
            if (stream.ContractIdentifier is not null && SameToken(stream.ContractIdentifier, token)) return 10;
            if (stream.Origin is not null && token.Kind == SyntaxKind.NumberToken && token.Position >= stream.Origin.LessToken.Position && token.Position < stream.Origin.GreaterToken.Position) return 15;
            foreach (StreamNodeSyntax node in stream.Nodes)
            {
                int nodeKind = StreamNodeTokenKind(node, token);
                if (nodeKind >= 0) return nodeKind;
            }
            foreach (StreamTableSyntax table in stream.Tables)
            {
                int tableKind = StreamTableTokenKind(table, token);
                if (tableKind >= 0) return tableKind;
            }
        }
        return -1;
    }

    private static LayoutSlotSymbol? FindBindingSlot(CopelandCompilation compilation, SyntaxToken token)
    {
        foreach (BoundLayoutBinding binding in compilation.BoundCompilation?.Program.LayoutBindings ?? [])
        {
            BoundLayoutBindingEntry? entry = binding.Entries.FirstOrDefault(candidate => SameToken(candidate.Syntax.SlotIdentifier, token));
            if (entry is not null) return entry.Slot;
        }

        return null;
    }

    private static void AddLayoutBindingSlotCompletions(Dictionary<string, object> items, CopelandCompilation compilation, int offset)
    {
        LayoutBindingDeclarationSyntax? syntax = compilation.SyntaxTree?.Root.Members
            .OfType<LayoutBindingDeclarationSyntax>()
            .FirstOrDefault(candidate => offset >= candidate.OpenBraceToken.Position
                && offset <= candidate.CloseBraceToken.Position);
        if (syntax is null || compilation.BoundCompilation is null) return;

        BoundLayoutBinding? binding = compilation.BoundCompilation.Program.LayoutBindings
            .FirstOrDefault(candidate => candidate.Syntax == syntax);
        if (binding is null) return;

        foreach (LayoutSlotSymbol slot in binding.Layout.Slots.Values
            .Where(slot => slot.IsBindable)
            .OrderBy(slot => slot.SemanticPath, StringComparer.Ordinal))
        {
            items[slot.Name] = CompletionItem(slot.Name, 7, "bindable slot " + slot.SemanticPath + " (exactly one ReactNode)");
        }
    }

    private static int LayoutNodeTokenKind(LayoutNodeSyntax node, SyntaxToken token)
    {
        if (SameToken(node.KindToken, token)) return 12;
        if (SameToken(node.Identifier, token)) return 13;
        int derivationKind = RelativeDerivationTokenKind(node.RelativeDerivations, token);
        if (derivationKind >= 0) return derivationKind;
        foreach (LayoutPropertySyntax property in node.Properties)
        {
            if (SameToken(property.Identifier, token)) return property.Identifier.Text is "layer" or "z" ? 10 : -1;
            if (property.Value is NameExpressionSyntax name
                && name.IdentifierToken.Text is "fill" or "fit"
                && SameToken(name.IdentifierToken, token)) return 14;
        }
        foreach (LayoutNodeSyntax child in node.Children)
        {
            int childKind = LayoutNodeTokenKind(child, token);
            if (childKind >= 0) return childKind;
        }
        return -1;
    }

    private static int StreamNodeTokenKind(StreamNodeSyntax node, SyntaxToken token)
    {
        if (node.KindToken is not null && SameToken(node.KindToken, token)) return 0;
        if (SameToken(node.Identifier, token)) return 13;
        int derivationKind = RelativeDerivationTokenKind(node.RelativeDerivations, token);
        if (derivationKind >= 0) return derivationKind;
        foreach (LayoutPropertySyntax property in node.Properties)
        {
            if (SameToken(property.Identifier, token)) return 10;
        }
        foreach (StreamNodeSyntax child in node.Children)
        {
            int childKind = StreamNodeTokenKind(child, token);
            if (childKind >= 0) return childKind;
        }
        foreach (StreamTableSyntax table in node.Tables)
        {
            int tableKind = StreamTableTokenKind(table, token);
            if (tableKind >= 0) return tableKind;
        }
        return -1;
    }

    private static int RelativeDerivationTokenKind(IReadOnlyList<LayoutRelativeDerivationSyntax>? derivations, SyntaxToken token)
    {
        foreach (LayoutRelativeDerivationSyntax derivation in derivations ?? [])
        {
            if (SameToken(derivation.WithKeyword, token)) return 0;
            if (SameToken(derivation.TransformIdentifier, token)) return 4;
            if (SameToken(derivation.SourceIdentifier, token)) return 13;
            if (derivation.GapOrPadding is LiteralExpressionSyntax { LiteralToken: var lengthToken } && SameToken(lengthToken, token)) return 15;
        }
        return -1;
    }

    private static readonly IReadOnlyDictionary<string, string> RelativeTransformDetails = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["centerIn"] = "relative layout intrinsic\nreads: source.x, source.y, source.width, source.height, self.width, self.height\nwrites: self.x, self.y",
        ["centerXIn"] = "relative layout intrinsic\nreads: source.x, source.width, self.width\nwrites: self.x",
        ["centerYIn"] = "relative layout intrinsic\nreads: source.y, source.height, self.height\nwrites: self.y",
        ["alignLeft"] = "relative layout intrinsic\nreads: source.x\nwrites: self.x",
        ["alignRight"] = "relative layout intrinsic\nreads: source.x, source.width, self.width\nwrites: self.x",
        ["alignTop"] = "relative layout intrinsic\nreads: source.y\nwrites: self.y",
        ["alignBottom"] = "relative layout intrinsic\nreads: source.y, source.height, self.height\nwrites: self.y",
        ["placeLeftOf"] = "relative layout intrinsic\nreads: source.x, self.width, gap\nwrites: self.x",
        ["placeRightOf"] = "relative layout intrinsic\nreads: source.x, source.width, gap\nwrites: self.x",
        ["placeAbove"] = "relative layout intrinsic\nreads: source.y, self.height, gap\nwrites: self.y",
        ["placeBelow"] = "relative layout intrinsic\nreads: source.y, source.height, gap\nwrites: self.y",
        ["insetFrom"] = "relative layout intrinsic\nreads: source frame, padding\nwrites: self frame",
        ["expandFrom"] = "relative layout intrinsic\nreads: source frame, padding\nwrites: self frame",
    };

    private static string? DescribeRelativeDerivationAt(SyntaxTree? tree, SyntaxToken token)
    {
        if (tree is null) return null;
        foreach (LayoutRelativeDerivationSyntax derivation in RelativeDerivations(tree))
        {
            if (SameToken(derivation.TransformIdentifier, token)) return RelativeTransformDetails.GetValueOrDefault(derivation.TransformIdentifier.Text);
            if (SameToken(derivation.SourceIdentifier, token)) return "relative layout source box " + derivation.SourceIdentifier.Text;
        }
        return null;
    }

    private static void AddRelativeLayoutCompletions(Dictionary<string, object> items, SyntaxTree? tree)
    {
        foreach ((string transform, string detail) in RelativeTransformDetails)
        {
            items[transform] = CompletionItem(transform, 3, detail);
        }
        foreach (SyntaxToken box in LayoutBoxDeclarations(tree))
        {
            items[box.Text] = CompletionItem(box.Text, 7, "relative layout box");
        }
    }

    private static bool TryFindRelativeSourceDefinition(SyntaxTree? tree, SyntaxToken token, out SyntaxToken? declaration)
    {
        declaration = null;
        if (!RelativeDerivations(tree).Any(derivation => SameToken(derivation.SourceIdentifier, token))) return false;
        declaration = LayoutBoxDeclarations(tree).FirstOrDefault(candidate => candidate.Text == token.Text);
        return declaration is not null;
    }

    private static IEnumerable<LayoutRelativeDerivationSyntax> RelativeDerivations(SyntaxTree? tree)
    {
        if (tree is null) yield break;
        foreach (LayoutDeclarationSyntax layout in tree.Root.Members.OfType<LayoutDeclarationSyntax>())
        {
            foreach (LayoutNodeSyntax node in layout.Nodes)
            {
                foreach (LayoutRelativeDerivationSyntax derivation in NodeDerivations(node)) yield return derivation;
            }
        }
        foreach (StreamDeclarationSyntax stream in tree.Root.Members.OfType<StreamDeclarationSyntax>())
        {
            foreach (StreamNodeSyntax node in stream.Nodes)
            {
                foreach (LayoutRelativeDerivationSyntax derivation in StreamDerivations(node)) yield return derivation;
            }
        }
    }

    private static IEnumerable<SyntaxToken> LayoutBoxDeclarations(SyntaxTree? tree)
    {
        if (tree is null) yield break;
        foreach (LayoutDeclarationSyntax layout in tree.Root.Members.OfType<LayoutDeclarationSyntax>())
        {
            foreach (LayoutNodeSyntax node in layout.Nodes) foreach (SyntaxToken box in NodeBoxes(node)) yield return box;
        }
        foreach (StreamDeclarationSyntax stream in tree.Root.Members.OfType<StreamDeclarationSyntax>())
        {
            foreach (StreamNodeSyntax node in stream.Nodes) foreach (SyntaxToken box in StreamBoxes(node)) yield return box;
        }
    }

    private static IEnumerable<LayoutRelativeDerivationSyntax> NodeDerivations(LayoutNodeSyntax node)
    {
        foreach (LayoutRelativeDerivationSyntax derivation in node.RelativeDerivations ?? []) yield return derivation;
        foreach (LayoutNodeSyntax child in node.Children) foreach (LayoutRelativeDerivationSyntax derivation in NodeDerivations(child)) yield return derivation;
    }

    private static IEnumerable<LayoutRelativeDerivationSyntax> StreamDerivations(StreamNodeSyntax node)
    {
        foreach (LayoutRelativeDerivationSyntax derivation in node.RelativeDerivations ?? []) yield return derivation;
        foreach (StreamNodeSyntax child in node.Children) foreach (LayoutRelativeDerivationSyntax derivation in StreamDerivations(child)) yield return derivation;
    }

    private static IEnumerable<SyntaxToken> NodeBoxes(LayoutNodeSyntax node)
    {
        yield return node.Identifier;
        foreach (LayoutNodeSyntax child in node.Children) foreach (SyntaxToken box in NodeBoxes(child)) yield return box;
    }

    private static IEnumerable<SyntaxToken> StreamBoxes(StreamNodeSyntax node)
    {
        yield return node.Identifier;
        foreach (StreamNodeSyntax child in node.Children) foreach (SyntaxToken box in StreamBoxes(child)) yield return box;
    }

    private static int StreamTableTokenKind(StreamTableSyntax table, SyntaxToken token)
    {
        if (SameToken(table.CsvKeyword, token)) return 0;
        if (SameToken(table.ContainerKindToken, token)) return 12;
        if (SameToken(table.Identifier, token)) return 13;
        if (table.Headers.Any(header => SameToken(header, token))) return 10;

        int nameIndex = TableColumnIndex(table, "name");
        int layerIndex = TableColumnIndex(table, "layer");
        int zIndex = TableColumnIndex(table, "z");
        foreach (StreamTableRowSyntax row in table.Rows)
        {
            for (int index = 0; index < row.Cells.Count; index++)
            {
                SyntaxToken cellToken = FirstToken(row.Cells[index]);
                if (!SameToken(cellToken, token)) continue;
                if (index == nameIndex || index == layerIndex) return 13;
                if (index == zIndex || token.Kind == SyntaxKind.NumberToken) return 15;
                return -1;
            }
        }
        return -1;
    }

    private static int TableColumnIndex(StreamTableSyntax table, string name)
    {
        for (int index = 0; index < table.Headers.Count; index++)
        {
            if (string.Equals(table.Headers[index].Text, name, StringComparison.Ordinal)) return index;
        }
        return -1;
    }

    private static SyntaxToken FirstToken(SyntaxNode node)
        => node.GetChildren().OfType<SyntaxToken>().FirstOrDefault() ?? new SyntaxToken(SyntaxKind.BadToken, 0, string.Empty, null);

    private static bool SameToken(SyntaxToken left, SyntaxToken right)
        => left.Position == right.Position && left.Text.Length == right.Text.Length;

    private static bool IsAwaitingLayoutOrigin(string text, SyntaxTree? tree, int offset)
    {
        bool parserRecognizesMissingOrigin = tree?.Root.Members
            .OfType<LayoutDeclarationSyntax>()
            .Any(layout => layout.Origin is null
                && offset >= layout.Identifier.Position + layout.Identifier.Text.Length
                && offset <= layout.Identifier.Position + layout.Identifier.Text.Length + 1) == true;
        if (parserRecognizesMissingOrigin) return true;

        string prefix = text[..Math.Clamp(offset, 0, text.Length)];
        return System.Text.RegularExpressions.Regex.IsMatch(
            prefix,
            @"\blayout\s+(?:(?:page)\s+)?[A-Za-z_][A-Za-z0-9_]*\s*$");
    }

    private static string DescribeLayout(LayoutSymbol layout)
    {
        BoundLayoutDeclaration bound = layout.BoundLayout!;
        string width = DescribeDimension(bound.Root.Dimensions.GetValueOrDefault("width"));
        string height = DescribeDimension(bound.Root.Dimensions.GetValueOrDefault("height"));
        int boxCount = Count(LayoutDataCompiler.Normalize(bound).Root);
        return "layout " + (layout.Profile is null ? string.Empty : layout.Profile + " ") + layout.Name
            + "\norigin: (" + DescribeCoordinate(bound.Origin.X) + ", " + DescribeCoordinate(bound.Origin.Y) + ")"
            + "\nsize: " + width + " × " + height
            + "\nnormalized boxes: " + boxCount;

        static int Count(NormalizedLayoutNode node) => 1 + node.Children.Sum(Count);
    }

    private static string DescribeLayoutType(LayoutTypeSymbol layoutType)
    {
        BoundLayoutTypeNode root = layoutType.BoundLayoutType!.Root;
        return "layout type " + layoutType.Name + "\n" + DescribeLayoutTypeNode(root, 0);
    }

    private static string DescribeLayoutTypeNode(BoundLayoutTypeNode node, int depth)
    {
        string prefix = new string(' ', depth * 2);
        string columns = node.Columns is int value ? " columns: " + value : string.Empty;
        string children = string.Concat(node.Children.Select(child => "\n" + DescribeLayoutTypeNode(child, depth + 1)));
        return prefix + node.Kind.ToString().ToLowerInvariant() + " " + node.Name + columns + children;
    }

    private static string DescribeDimension(BoundLayoutDimension? dimension)
        => dimension is { Kind: LayoutDimensionKind.Fixed, Length: MachinaLength length }
            ? length.Describe("host axis")
            : dimension?.Kind.ToString().ToLowerInvariant() ?? "unspecified";

    private static string DescribeCoordinate(BoundLayoutCoordinate coordinate)
        => coordinate.Value.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture)
            + (coordinate.Unit == LayoutCoordinateUnit.Px ? "px" : "ui");

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
