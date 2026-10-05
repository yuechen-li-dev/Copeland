using System.Collections.ObjectModel;
using Copeland.TS.Diagnostics;
using Copeland.TS.Syntax;

namespace Copeland.TS.Manifest;

/// <summary>
/// Interprets the neutral TS-XML tree only when an explicit project loader has
/// selected the manifest profile. It never delegates to a TypeScript runtime.
/// </summary>
public static class ManifestBinder
{
    public static ManifestBindingResult Bind(
        SyntaxTree tree,
        string projectRoot,
        string sourcePath,
        ManifestBindingContext context)
    {
        var implementation = new Implementation(tree, Path.GetFullPath(projectRoot), sourcePath, context);
        return implementation.Bind();
    }

    private sealed class Implementation
    {
        private static readonly HashSet<string> WorkspaceElements = ["Package", "Packages", "Sidecars", "Security", "UpdatePolicy", "CompatFiles", "Assets", "AssetOutputs"];
        private static readonly HashSet<string> PackageElements = ["Targets", "RunTargets", "Tools", "Boundaries", "Publish", "Policies"];
        private readonly SyntaxTree _tree;
        private readonly string _projectRoot;
        private readonly string _sourcePath;
        private readonly ManifestBindingContext _context;
        private readonly List<Diagnostic> _diagnostics = [];
        private readonly Dictionary<string, ManifestValue> _constants = new(StringComparer.Ordinal);

        public Implementation(SyntaxTree tree, string projectRoot, string sourcePath, ManifestBindingContext context)
        {
            _tree = tree;
            _projectRoot = projectRoot;
            _sourcePath = sourcePath;
            _context = context;
        }

        public ManifestBindingResult Bind()
        {
            foreach (Diagnostic diagnostic in _tree.Diagnostics)
            {
                _diagnostics.Add(diagnostic with { SourcePath = _sourcePath });
            }

            ExportDefaultDeclarationSyntax? export = null;
            foreach (MemberSyntax member in _tree.Root.Members)
            {
                switch (member)
                {
                    case ImportDeclarationSyntax import:
                        ValidateImport(import);
                        break;
                    case GlobalStatementMemberSyntax { Statement: VariableDeclarationStatementSyntax variable }:
                        BindConstant(variable);
                        break;
                    case ExportDefaultDeclarationSyntax declaration:
                        if (export is not null)
                        {
                            Report("COPE-MANIFEST-0004", "A manifest has exactly one default export.", declaration.ExportToken);
                        }

                        export = declaration;
                        break;
                    default:
                        Report("COPE-MANIFEST-0005", "Manifest source permits only imports, const declarations, and one default export.", FirstToken(member));
                        break;
                }
            }

            if (export is null)
            {
                ReportAtStart("COPE-MANIFEST-0004", "A manifest must use 'export default define(<Workspace ...>)'.");
                return Failure();
            }

            TsXmlElementExpressionSyntax? workspaceElement = GetManifestRoot(export);
            if (workspaceElement is null)
            {
                return Failure();
            }

            CopelandManifest? manifest = _context == ManifestBindingContext.DependencyManifest
                ? BindDependencyPackage(workspaceElement)
                : BindWorkspace(workspaceElement);
            return _diagnostics.Count == 0 && manifest is not null
                ? new ManifestBindingResult(manifest, [])
                : Failure();
        }

        private ManifestBindingResult Failure()
            => new(null, _diagnostics.OrderBy(item => item.Position).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray());

        private void ValidateImport(ImportDeclarationSyntax import)
        {
            SyntaxToken? module = import.Tokens.LastOrDefault(token => token.Kind == SyntaxKind.StringToken);
            if (module?.Value is not string moduleName || !string.Equals(moduleName, "tspack/manifest", StringComparison.Ordinal))
            {
                Report("COPE-MANIFEST-0006", "Manifest imports are permitted only from 'tspack/manifest'.", import.Tokens[0]);
            }
        }

        private void BindConstant(VariableDeclarationStatementSyntax variable)
        {
            if (variable.Keyword.Kind != SyntaxKind.ConstKeyword)
            {
                Report("COPE-MANIFEST-0005", "Manifest declarations must use 'const'.", variable.Keyword);
                return;
            }

            if (variable.TypeColonToken is not null || variable.Initializer is not ExpressionSyntax initializer)
            {
                Report("COPE-MANIFEST-0007", "Manifest constants require an inferred restricted expression initializer.", variable.Identifier);
                return;
            }

            ManifestValue? value = Evaluate(initializer);
            if (value is not null)
            {
                if (!_constants.TryAdd(variable.Identifier.Text, value))
                {
                    Report("COPE-MANIFEST-0008", $"Duplicate manifest constant '{variable.Identifier.Text}'.", variable.Identifier);
                }
            }
        }

        private TsXmlElementExpressionSyntax? GetManifestRoot(ExportDefaultDeclarationSyntax export)
        {
            if (export.Expression is not CallExpressionSyntax call
                || call.Target is not NameExpressionSyntax helper
                || !IsRootDefinitionHelper(helper.IdentifierToken.Text)
                || call.Arguments.Count != 1
                || call.Arguments[0] is not TsXmlElementExpressionSyntax element)
            {
                Report("COPE-MANIFEST-0004", "Manifest default export must be a single define(<Workspace ...>) call.", export.ExportToken);
                return null;
            }

            string expectedRoot = _context == ManifestBindingContext.RootProject ? "Workspace" : "Package";
            if (!string.Equals(element.NameToken.Text, expectedRoot, StringComparison.Ordinal))
            {
                Report("COPE-MANIFEST-0009", $"Manifest root must be a <{expectedRoot}> element.", element.NameToken);
                return null;
            }

            return element;
        }

        private CopelandManifest BindDependencyPackage(TsXmlElementExpressionSyntax element)
        {
            ManifestPackage package = BindPackage(element);
            return new CopelandManifest(
                _projectRoot,
                _sourcePath,
                new ManifestWorkspace("dependency", "nodejs"),
                [package],
                [],
                [],
                [],
                null,
                null,
                []);
        }

        private bool IsRootDefinitionHelper(string helper)
            => _context == ManifestBindingContext.RootProject
                ? helper is "define" or "defineWorkspace"
                : helper == "definePackage";

        private CopelandManifest? BindWorkspace(TsXmlElementExpressionSyntax element)
        {
            Dictionary<string, ManifestValue> attributes = BindAttributes(element, ["name", "runtime"]);
            string? name = RequiredString(attributes, "name", element.NameToken);
            string runtime = OptionalString(attributes, "runtime", "nodejs", element.NameToken) ?? "nodejs";
            if (runtime is not ("nodejs" or "bun" or "deno"))
            {
                Report("COPE-MANIFEST-0010", "Workspace runtime must be 'nodejs', 'bun', or 'deno'.", AttributeToken(element, "runtime") ?? element.NameToken);
            }

            var packages = new List<ManifestPackage>();
            var references = new List<ManifestPackageReference>();
            var sidecars = new List<ManifestSidecarBinding>();
            ManifestSecurity? security = null;
            ManifestUpdatePolicy? updatePolicy = null;
            var compatFiles = new List<ManifestCompatFile>();
            ManifestAssetGraph? assets = null;
            ManifestAssetOutputs? assetOutputs = null;
            var seenSingletons = new HashSet<string>(StringComparer.Ordinal);
            bool hasPackageReferences = false;

            foreach (TsXmlElementExpressionSyntax child in ElementChildren(element))
            {
                if (!WorkspaceElements.Contains(child.NameToken.Text))
                {
                    Report("COPE-MANIFEST-0011", $"Element <{child.NameToken.Text}> is not valid inside <Workspace>.", child.NameToken);
                    continue;
                }

                switch (child.NameToken.Text)
                {
                    case "Package":
                        packages.Add(BindPackage(child));
                        break;
                    case "Packages":
                        if (!seenSingletons.Add("Packages"))
                        {
                            Report("COPE-MANIFEST-0012", "<Workspace> cannot contain duplicate <Packages> elements.", child.NameToken);
                        }

                        hasPackageReferences = true;
                        references.AddRange(BindPackageReferences(child));
                        break;
                    case "Sidecars":
                        if (!seenSingletons.Add("Sidecars"))
                        {
                            Report("COPE-MANIFEST-0012", "<Workspace> cannot contain duplicate <Sidecars> elements.", child.NameToken);
                        }

                        sidecars.AddRange(BindSidecars(child));
                        break;
                    case "Security":
                        if (!seenSingletons.Add("Security"))
                        {
                            Report("COPE-MANIFEST-0012", "<Workspace> cannot contain duplicate <Security> elements.", child.NameToken);
                        }

                        security = BindSecurity(child);
                        break;
                    case "UpdatePolicy":
                        if (!seenSingletons.Add("UpdatePolicy"))
                        {
                            Report("COPE-MANIFEST-0012", "<Workspace> cannot contain duplicate <UpdatePolicy> elements.", child.NameToken);
                        }

                        updatePolicy = BindUpdatePolicy(child);
                        break;
                    case "CompatFiles":
                        if (!seenSingletons.Add("CompatFiles"))
                        {
                            Report("COPE-MANIFEST-0012", "<Workspace> cannot contain duplicate <CompatFiles> elements.", child.NameToken);
                        }

                        compatFiles.AddRange(BindCompatFiles(child));
                        break;
                    case "Assets":
                        if (!seenSingletons.Add("Assets"))
                        {
                            Report("COPE-MANIFEST-0012", "<Workspace> cannot contain duplicate <Assets> elements.", child.NameToken);
                        }

                        assets = BindAssets(child);
                        break;
                    case "AssetOutputs":
                        if (!seenSingletons.Add("AssetOutputs"))
                        {
                            Report("COPE-MANIFEST-0012", "<Workspace> cannot contain duplicate <AssetOutputs> elements.", child.NameToken);
                        }

                        assetOutputs = BindAssetOutputs(child);
                        break;
                }
            }

            if (hasPackageReferences && packages.Count > 0)
            {
                Report("COPE-MANIFEST-0013", "<Workspace> cannot mix inline <Package> declarations with <Packages> references.", element.NameToken);
            }

            ValidateUnique(packages.Select(package => (package.Name, element.NameToken)), "package", element.NameToken);
            ValidateUnique(references.Select(reference => (reference.Name, element.NameToken)), "package reference", element.NameToken);
            ValidateUnique(sidecars.Select(sidecar => (sidecar.LogicalBindingId, element.NameToken)), "sidecar binding", element.NameToken);
            IReadOnlyList<ManifestDeploymentBinding> bindings = name is null
                ? []
                : packages.SelectMany(package => package.RunTargets.Select(target => new ManifestDeploymentBinding(
                    $"{name}/{package.Name}/{target.Name}",
                    package.Name,
                    target.Name,
                    target.Runtime,
                    target.Command,
                    target.WorkingDirectory))).ToArray();
            ValidateSidecarReferences(sidecars, bindings, element.NameToken);
            if (sidecars.Count(binding => binding.IsDefault) > 1)
            {
                Report("COPE-MANIFEST-0032", "<Sidecars> permits exactly zero or one default binding.", element.NameToken);
            }
            return name is null
                ? null
                : new CopelandManifest(_projectRoot, _sourcePath, new ManifestWorkspace(name, runtime), packages, bindings, sidecars, references, security, updatePolicy, compatFiles, assets, assetOutputs);
        }

        private ManifestAssetGraph BindAssets(TsXmlElementExpressionSyntax element)
        {
            Dictionary<string, ManifestValue> attributes = BindAttributes(element, ["root"]);
            string sourceRoot = OptionalString(attributes, "root", ".", element.NameToken) ?? ".";
            if (!IsSafeRelativeDirectory(sourceRoot))
            {
                Report("COPE-MANIFEST-0040", "Assets root must be a safe relative directory.", AttributeToken(element, "root") ?? element.NameToken);
                sourceRoot = ".";
            }

            var textures = new List<ManifestTextureAsset>();
            var objects = new List<ManifestObjectAsset>();
            foreach (TsXmlElementExpressionSyntax child in ElementChildren(element))
            {
                Dictionary<string, ManifestValue> childAttributes;
                switch (child.NameToken.Text)
                {
                    case "Texture":
                        childAttributes = BindAttributes(child, ["id", "src"]);
                        string? textureId = RequiredString(childAttributes, "id", child.NameToken);
                        string? textureSource = RequiredString(childAttributes, "src", child.NameToken);
                        if (textureId is not null && textureSource is not null)
                        {
                            ValidateAssetPath(textureSource, sourceRoot, child.NameToken);
                            textures.Add(new ManifestTextureAsset(textureId, textureSource));
                        }

                        break;
                    case "Object":
                        childAttributes = BindAttributes(child, ["id", "src", "dependsOn"]);
                        string? objectId = RequiredString(childAttributes, "id", child.NameToken);
                        string? objectSource = RequiredString(childAttributes, "src", child.NameToken);
                        IReadOnlyList<string> dependencies = OptionalStringArray(childAttributes, "dependsOn", child.NameToken);
                        if (objectId is not null && objectSource is not null)
                        {
                            if (!objectSource.EndsWith(".obj.ts", StringComparison.OrdinalIgnoreCase))
                            {
                                Report("COPE-MANIFEST-0041", $"Object asset '{objectId}' source must end in '.obj.ts'.", AttributeToken(child, "src") ?? child.NameToken);
                            }
                            ValidateAssetPath(objectSource, sourceRoot, child.NameToken);
                            objects.Add(new ManifestObjectAsset(objectId, objectSource, dependencies));
                        }

                        break;
                    default:
                        Report("COPE-MANIFEST-0042", $"Element <{child.NameToken.Text}> is not valid inside <Assets>.", child.NameToken);
                        break;
                }
            }

            ValidateUnique(textures.Select(texture => (texture.Id, element.NameToken)), "texture asset", element.NameToken);
            ValidateUnique(objects.Select(asset => (asset.Id, element.NameToken)), "object asset", element.NameToken);
            ValidateObjectDependencies(objects, element.NameToken);
            return new ManifestAssetGraph(sourceRoot, textures, objects);
        }

        private ManifestAssetOutputs BindAssetOutputs(TsXmlElementExpressionSyntax element)
        {
            _ = BindAttributes(element, []);
            var requested = new HashSet<string>(StringComparer.Ordinal);
            foreach (TsXmlElementExpressionSyntax child in ElementChildren(element))
            {
                if (child.NameToken.Text is not ("Toml" or "Json" or "Runtime" or "Audit"))
                {
                    Report("COPE-MANIFEST-0043", $"Unknown asset output <{child.NameToken.Text}>.", child.NameToken);
                    continue;
                }

                _ = BindAttributes(child, []);
                if (!requested.Add(child.NameToken.Text))
                {
                    Report("COPE-MANIFEST-0044", $"Duplicate asset output <{child.NameToken.Text}>.", child.NameToken);
                }
            }

            return new ManifestAssetOutputs(
                requested.Contains("Toml"),
                requested.Contains("Json"),
                requested.Contains("Runtime"),
                requested.Contains("Audit"));
        }

        private void ValidateAssetPath(string path, string sourceRoot, SyntaxToken anchor)
        {
            if (!IsSafeRelativePath(path))
            {
                Report("COPE-MANIFEST-0045", $"Asset source '{path}' must be a safe relative path.", anchor);
                return;
            }

            string fullPath = Path.GetFullPath(Path.Combine(_projectRoot, sourceRoot, path));
            if (!File.Exists(fullPath))
            {
                Report("COPE-MANIFEST-0046", $"Asset source '{path}' does not exist below '{sourceRoot}'.", anchor);
            }
        }

        private void ValidateObjectDependencies(IReadOnlyList<ManifestObjectAsset> objects, SyntaxToken anchor)
        {
            IReadOnlyDictionary<string, ManifestObjectAsset> byId = objects
                .GroupBy(asset => asset.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (ManifestObjectAsset asset in objects)
            {
                foreach (string dependency in asset.Dependencies)
                {
                    if (!byId.ContainsKey(dependency))
                    {
                        Report("COPE-MANIFEST-0047", $"Object asset '{asset.Id}' depends on unknown object '{dependency}'.", anchor);
                    }
                }
            }

            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (ManifestObjectAsset asset in objects)
            {
                Visit(asset.Id, []);
            }

            void Visit(string id, IReadOnlyList<string> path)
            {
                if (visited.Contains(id) || !byId.TryGetValue(id, out ManifestObjectAsset? asset))
                {
                    return;
                }

                if (!visiting.Add(id))
                {
                    Report("COPE-MANIFEST-0048", "Object asset dependency cycle: " + string.Join(" -> ", path.Append(id)) + ".", anchor);
                    return;
                }

                foreach (string dependency in asset.Dependencies)
                {
                    Visit(dependency, path.Append(id).ToArray());
                }

                visiting.Remove(id);
                visited.Add(id);
            }
        }

        private IReadOnlyList<ManifestSidecarBinding> BindSidecars(TsXmlElementExpressionSyntax element)
        {
            if (_context != ManifestBindingContext.RootProject)
            {
                Report("COPE-MANIFEST-0018", "Dependency manifests cannot declare <Sidecars> or acquire root deployment authority.", element.NameToken);
            }

            var result = new List<ManifestSidecarBinding>();
            foreach (ManifestValue value in RequiredArray(BindAttributes(element, ["rows"]), "rows", element.NameToken))
            {
                if (value is not ManifestValue.Object row
                    || StringProperty(row, "id") is not string id
                    || string.IsNullOrWhiteSpace(id)
                    || StringProperty(row, "runTarget") is not string runTarget
                    || string.IsNullOrWhiteSpace(runTarget))
                {
                    Report("COPE-MANIFEST-0032", "Sidecar rows require non-empty string id and runTarget fields.", element.NameToken);
                    continue;
                }

                if (row.Properties.TryGetValue("default", out ManifestValue? defaultValue)
                    && defaultValue is not ManifestValue.Boolean)
                {
                    Report("COPE-MANIFEST-0032", $"Sidecar '{id}' default must be a boolean.", element.NameToken);
                    continue;
                }

                foreach (string field in row.Properties.Keys)
                {
                    if (field is not ("id" or "runTarget" or "default"))
                    {
                        Report("COPE-MANIFEST-0032", $"Sidecar '{id}' cannot declare '{field}'; launch configuration belongs to its RunTarget.", element.NameToken);
                    }
                }

                result.Add(new ManifestSidecarBinding(id, runTarget, defaultValue is ManifestValue.Boolean { BooleanValue: true }));
            }

            return result;
        }

        private void ValidateSidecarReferences(
            IReadOnlyList<ManifestSidecarBinding> sidecars,
            IReadOnlyList<ManifestDeploymentBinding> runTargets,
            SyntaxToken anchor)
        {
            var known = runTargets.Select(target => target.LogicalIdentity).ToHashSet(StringComparer.Ordinal);
            foreach (ManifestSidecarBinding sidecar in sidecars)
            {
                if (!known.Contains(sidecar.RunTargetIdentity))
                {
                    Report("COPE-MANIFEST-0032", $"Sidecar '{sidecar.LogicalBindingId}' references unknown or non-root RunTarget '{sidecar.RunTargetIdentity}'.", anchor);
                }
            }
        }

        private ManifestPackage BindPackage(TsXmlElementExpressionSyntax element)
        {
            Dictionary<string, ManifestValue> attributes = BindAttributes(element, ["name", "version", "kind", "license", "dependencies"]);
            string name = RequiredString(attributes, "name", element.NameToken) ?? string.Empty;
            string version = RequiredString(attributes, "version", element.NameToken) ?? string.Empty;
            string kind = RequiredString(attributes, "kind", element.NameToken) ?? string.Empty;
            if (kind is not ("app" or "library" or "service"))
            {
                Report("COPE-MANIFEST-0014", "Package kind must be 'app', 'library', or 'service'.", AttributeToken(element, "kind") ?? element.NameToken);
            }

            var targets = new List<ManifestTarget>();
            var runTargets = new List<ManifestRunTarget>();
            var tools = new List<ManifestValue>();
            var boundaries = new List<ManifestValue>();
            ManifestPublish? publish = null;
            ManifestValue? policies = null;
            var seenSingletons = new HashSet<string>(StringComparer.Ordinal);

            foreach (TsXmlElementExpressionSyntax child in ElementChildren(element))
            {
                if (!PackageElements.Contains(child.NameToken.Text))
                {
                    Report("COPE-MANIFEST-0011", $"Element <{child.NameToken.Text}> is not valid inside <Package>.", child.NameToken);
                    continue;
                }

                if (!seenSingletons.Add(child.NameToken.Text))
                {
                    Report("COPE-MANIFEST-0012", $"<Package> cannot contain duplicate <{child.NameToken.Text}> elements.", child.NameToken);
                }

                switch (child.NameToken.Text)
                {
                    case "Targets": targets.AddRange(BindTargets(child)); break;
                    case "RunTargets": runTargets.AddRange(BindRunTargets(child, name)); break;
                    case "Tools": tools.AddRange(RequiredArray(BindAttributes(child, ["values"]), "values", child.NameToken)); break;
                    case "Boundaries": boundaries.AddRange(RequiredArray(BindAttributes(child, ["rows"]), "rows", child.NameToken)); break;
                    case "Publish": publish = BindPublish(child); break;
                    case "Policies": policies = RequiredObject(BindAttributes(child, ["types", "boundaries"]), child.NameToken); break;
                }
            }

            ValidateUnique(targets.Select(target => (target.Name, element.NameToken)), "target", element.NameToken);
            ValidateUnique(runTargets.Select(target => (target.Name, element.NameToken)), "run target", element.NameToken);
            return new ManifestPackage(name, version, kind, OptionalString(attributes, "license", null, element.NameToken), attributes.GetValueOrDefault("dependencies"), targets, runTargets, tools, boundaries, publish, policies);
        }

        private IReadOnlyList<ManifestPackageReference> BindPackageReferences(TsXmlElementExpressionSyntax element)
        {
            Dictionary<string, ManifestValue> attributes = BindAttributes(element, ["rows"]);
            var result = new List<ManifestPackageReference>();
            foreach (ManifestValue value in RequiredArray(attributes, "rows", element.NameToken))
            {
                ManifestValue.Object? row = value as ManifestValue.Object;
                if (row is null)
                {
                    Report("COPE-MANIFEST-0015", "Package reference rows must be object literals.", element.NameToken);
                    continue;
                }

                string? name = StringProperty(row, "name");
                string? root = StringProperty(row, "root");
                string? manifest = StringProperty(row, "manifest");
                if (name is null || root is null || manifest is null)
                {
                    Report("COPE-MANIFEST-0015", "Package reference rows require string name, root, and manifest fields.", element.NameToken);
                    continue;
                }

                if (!IsSafeRelativePath(root) || !IsSafeRelativePath(manifest) || !manifest.StartsWith(root + "/", StringComparison.Ordinal))
                {
                    Report("COPE-MANIFEST-0016", "Package reference root and manifest must be safe relative paths, with manifest below root.", element.NameToken);
                    continue;
                }

                result.Add(new ManifestPackageReference(name, root, manifest));
            }

            return result;
        }

        private IReadOnlyList<ManifestTarget> BindTargets(TsXmlElementExpressionSyntax element)
        {
            var result = new List<ManifestTarget>();
            foreach (ManifestValue value in RequiredArray(BindAttributes(element, ["rows"]), "rows", element.NameToken))
            {
                if (value is not ManifestValue.Object row || StringProperty(row, "name") is not string name || string.IsNullOrWhiteSpace(name))
                {
                    Report("COPE-MANIFEST-0017", "Target rows require a non-empty string name.", element.NameToken);
                    continue;
                }

                foreach (string required in new[] { "entry", "runtime" })
                {
                    if (StringProperty(row, required) is null)
                    {
                        Report("COPE-MANIFEST-0017", $"Target row '{name}' requires string {required}.", element.NameToken);
                    }
                }

                result.Add(new ManifestTarget(name, row));
            }

            return result;
        }

        private IReadOnlyList<ManifestRunTarget> BindRunTargets(TsXmlElementExpressionSyntax element, string packageName)
        {
            if (_context != ManifestBindingContext.RootProject)
            {
                Report("COPE-MANIFEST-0018", "Dependency manifests cannot declare <RunTargets> or acquire root deployment authority.", element.NameToken);
            }

            var result = new List<ManifestRunTarget>();
            foreach (ManifestValue value in RequiredArray(BindAttributes(element, ["rows"]), "rows", element.NameToken))
            {
                if (value is not ManifestValue.Object row || StringProperty(row, "name") is not string name || string.IsNullOrWhiteSpace(name))
                {
                    Report("COPE-MANIFEST-0019", "RunTarget rows require a non-empty string name.", element.NameToken);
                    continue;
                }

                IReadOnlyList<string>? command = StringArrayProperty(row, "command");
                if (command is null || command.Count == 0 || command.Any(string.IsNullOrWhiteSpace))
                {
                    Report("COPE-MANIFEST-0019", $"RunTarget '{name}' requires a non-empty string argv command array.", element.NameToken);
                    continue;
                }

                string? runtime = StringProperty(row, "runtime");
                if (runtime is not null && runtime is not ("system" or "node" or "bun" or "deno"))
                {
                    Report("COPE-MANIFEST-0019", $"RunTarget '{name}' has an invalid runtime.", element.NameToken);
                }

                string? cwd = StringProperty(row, "cwd");
                if (cwd is not null && cwd is not ("workspace" or "package"))
                {
                    Report("COPE-MANIFEST-0019", $"RunTarget '{name}' cwd must be 'workspace' or 'package'.", element.NameToken);
                }

                result.Add(new ManifestRunTarget(packageName, name, runtime, command, cwd, row));
            }

            return result;
        }

        private ManifestSecurity BindSecurity(TsXmlElementExpressionSyntax element)
        {
            Dictionary<string, ManifestValue> attributes = BindAttributes(element, ["acknowledgedCapabilities", "acknowledgedLifecycleCategories"]);
            return new ManifestSecurity(
                attributes.GetValueOrDefault("acknowledgedCapabilities") ?? new ManifestValue.Array([]),
                attributes.GetValueOrDefault("acknowledgedLifecycleCategories") ?? new ManifestValue.Array([]));
        }

        private ManifestUpdatePolicy BindUpdatePolicy(TsXmlElementExpressionSyntax element)
            => new(RequiredArray(BindAttributes(element, ["rows"]), "rows", element.NameToken));

        private IReadOnlyList<ManifestCompatFile> BindCompatFiles(TsXmlElementExpressionSyntax element)
        {
            var result = new List<ManifestCompatFile>();
            foreach (TsXmlElementExpressionSyntax child in ElementChildren(element))
            {
                if (child.NameToken.Text != "JsonFile")
                {
                    Report("COPE-MANIFEST-0011", $"Element <{child.NameToken.Text}> is not valid inside <CompatFiles>.", child.NameToken);
                    continue;
                }

                Dictionary<string, ManifestValue> attributes = BindAttributes(child, ["path", "value"]);
                string? path = RequiredString(attributes, "path", child.NameToken);
                if (path is not null && !IsSafeRelativePath(path))
                {
                    Report("COPE-MANIFEST-0016", "JsonFile path must be a safe relative path.", AttributeToken(child, "path") ?? child.NameToken);
                }

                if (path is not null && attributes.TryGetValue("value", out ManifestValue? value))
                {
                    result.Add(new ManifestCompatFile(path, value));
                }
                else if (!attributes.ContainsKey("value"))
                {
                    Report("COPE-MANIFEST-0020", "JsonFile requires a value attribute.", child.NameToken);
                }
            }

            return result;
        }

        private ManifestPublish BindPublish(TsXmlElementExpressionSyntax element)
        {
            Dictionary<string, ManifestValue> attributes = BindAttributes(element, ["include", "exclude"]);
            return new ManifestPublish(
                RequiredStringArray(attributes, "include", element.NameToken),
                OptionalStringArray(attributes, "exclude", element.NameToken));
        }

        private Dictionary<string, ManifestValue> BindAttributes(TsXmlElementExpressionSyntax element, IReadOnlyCollection<string> allowed)
        {
            var result = new Dictionary<string, ManifestValue>(StringComparer.Ordinal);
            foreach (TsXmlAttributeSyntax attribute in element.Attributes)
            {
                string name = attribute.NameToken.Text;
                if (!allowed.Contains(name))
                {
                    Report("COPE-MANIFEST-0021", $"Unknown attribute '{name}' on <{element.NameToken.Text}>.", attribute.NameToken);
                    continue;
                }

                if (!result.TryAdd(name, EvaluateAttribute(attribute)))
                {
                    Report("COPE-MANIFEST-0022", $"Duplicate attribute '{name}' on <{element.NameToken.Text}>.", attribute.NameToken);
                }
            }

            foreach (TsXmlTextSyntax child in element.Children.OfType<TsXmlTextSyntax>())
            {
                if (!string.IsNullOrWhiteSpace(child.TextToken.Text))
                {
                    Report("COPE-MANIFEST-0023", "Manifest elements cannot contain text content.", child.TextToken);
                }
            }

            foreach (TsXmlExpressionChildSyntax child in element.Children.OfType<TsXmlExpressionChildSyntax>())
            {
                Report("COPE-MANIFEST-0023", "Manifest elements cannot contain braced child expressions.", child.OpenBraceToken);
            }

            return result;
        }

        private ManifestValue EvaluateAttribute(TsXmlAttributeSyntax attribute)
        {
            if (attribute.StringValueToken?.Value is string text)
            {
                return new ManifestValue.String(text);
            }

            if (attribute.ExpressionValue is not null)
            {
                return Evaluate(attribute.ExpressionValue) ?? new ManifestValue.Null();
            }

            Report("COPE-MANIFEST-0024", "Manifest attributes must have a string or braced compile-time value.", attribute.NameToken);
            return new ManifestValue.Boolean(true);
        }

        private ManifestValue? Evaluate(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case LiteralExpressionSyntax literal:
                    return EvaluateLiteral(literal);
                case ParenthesizedExpressionSyntax parenthesized:
                    return Evaluate(parenthesized.Expression);
                case NameExpressionSyntax name:
                    if (_constants.TryGetValue(name.IdentifierToken.Text, out ManifestValue? value)) return value;
                    Report("COPE-MANIFEST-0025", $"Unknown manifest constant '{name.IdentifierToken.Text}'.", name.IdentifierToken);
                    return null;
                case MemberAccessExpressionSyntax member:
                    return EvaluateMember(member);
                case ArrayLiteralExpressionSyntax array:
                    return new ManifestValue.Array(array.Elements.Select(Evaluate).Where(value => value is not null).Cast<ManifestValue>().ToArray());
                case ObjectLiteralExpressionSyntax obj:
                    return EvaluateObject(obj);
                case CallExpressionSyntax call:
                    return EvaluateCall(call);
                default:
                    Report("COPE-MANIFEST-0026", "Manifest expressions must be literal data, manifest constants, or approved compile-time helpers.", FirstToken(expression));
                    return null;
            }
        }

        private ManifestValue? EvaluateLiteral(LiteralExpressionSyntax literal)
            => literal.LiteralToken.Kind switch
            {
                SyntaxKind.StringToken => new ManifestValue.String((string)literal.LiteralToken.Value!),
                SyntaxKind.NumberToken when literal.LiteralToken.Value is int number => new ManifestValue.Number(number),
                SyntaxKind.TrueKeyword => new ManifestValue.Boolean(true),
                SyntaxKind.FalseKeyword => new ManifestValue.Boolean(false),
                SyntaxKind.NullKeyword => new ManifestValue.Null(),
                _ => ReportUnsupportedLiteral(literal),
            };

        private ManifestValue? ReportUnsupportedLiteral(LiteralExpressionSyntax literal)
        {
            Report("COPE-MANIFEST-0026", "This literal is not permitted in a manifest expression.", literal.LiteralToken);
            return null;
        }

        private ManifestValue? EvaluateMember(MemberAccessExpressionSyntax member)
        {
            ManifestValue? source = Evaluate(member.Target);
            if (source is ManifestValue.Object obj && obj.Properties.TryGetValue(member.NameToken.Text, out ManifestValue? result))
            {
                return result;
            }

            Report("COPE-MANIFEST-0025", $"Unknown manifest property '{member.NameToken.Text}'.", member.NameToken);
            return null;
        }

        private ManifestValue EvaluateObject(ObjectLiteralExpressionSyntax syntax)
        {
            var properties = new Dictionary<string, ManifestValue>(StringComparer.Ordinal);
            foreach (ObjectPropertySyntax property in syntax.Properties)
            {
                string name = property.NameToken.Value as string ?? property.NameToken.Text;
                ManifestValue? value = Evaluate(property.ValueExpression);
                if (value is not null && !properties.TryAdd(name, value))
                {
                    Report("COPE-MANIFEST-0022", $"Duplicate object property '{name}'.", property.NameToken);
                }
            }

            return new ManifestValue.Object(new ReadOnlyDictionary<string, ManifestValue>(properties));
        }

        private ManifestValue? EvaluateCall(CallExpressionSyntax call)
        {
            string? helper = call.Target switch
            {
                NameExpressionSyntax name => name.IdentifierToken.Text,
                MemberAccessExpressionSyntax member when member.Target is NameExpressionSyntax owner => owner.IdentifierToken.Text + "." + member.NameToken.Text,
                _ => null,
            };
            if (helper is null || !IsApprovedHelper(helper))
            {
                Report("COPE-MANIFEST-0026", "Only approved TSPack compile-time helpers are valid in manifests.", FirstToken(call));
                return null;
            }

            ManifestValue?[] arguments = call.Arguments.Select(Evaluate).ToArray();
            if (arguments.Any(argument => argument is null)) return null;
            ManifestValue[] values = arguments.Cast<ManifestValue>().ToArray();
            return helper switch
            {
                "defineDeps" when values.Length == 1 && values[0] is ManifestValue.Object obj => obj,
                "npm" when values.Length == 2 => ObjectOf(("kind", StringOf("npm")), ("package", values[0]), ("range", values[1])),
                "git" when values.Length is 1 or 2 => MergeObject("ref", values[0], values.ElementAtOrDefault(1)),
                "path" when values.Length == 1 => ObjectOf(("kind", StringOf("path")), ("path", values[0])),
                "workspace" when values.Length is 1 or 2 => MergeObject("name", values[0], values.ElementAtOrDefault(1), "workspace"),
                "dep" or "peer" or "tool" when values.Length is 1 or 2 => MergeObject("source", values[0], values.ElementAtOrDefault(1), helper),
                "Env" or "Service" when values.Length is 1 or 2 => MergeObject("name", values[0], values.ElementAtOrDefault(1), helper == "Service" ? "service" : null),
                "json" when values.Length == 1 => values[0],
                "TsConfig.manifestEditor" when values.Length is 0 or 1 => values.Length == 0 ? new ManifestValue.Object(new ReadOnlyDictionary<string, ManifestValue>(new Dictionary<string, ManifestValue>())) : values[0],
                "VSCode.settings" or "VSCode.extensions" when values.Length is 0 or 1 => values.Length == 0 ? new ManifestValue.Object(new ReadOnlyDictionary<string, ManifestValue>(new Dictionary<string, ManifestValue>())) : values[0],
                _ => InvalidHelperArguments(helper, call),
            };
        }

        private ManifestValue? InvalidHelperArguments(string helper, CallExpressionSyntax call)
        {
            Report("COPE-MANIFEST-0027", $"Helper '{helper}' received an unsupported manifest argument shape.", FirstToken(call));
            return null;
        }

        private static bool IsApprovedHelper(string helper)
            => helper is "defineDeps" or "npm" or "git" or "path" or "workspace" or "dep" or "peer" or "tool" or "Env" or "Service" or "json" or "TsConfig.manifestEditor" or "VSCode.settings" or "VSCode.extensions";

        private static ManifestValue.Object ObjectOf(params (string Key, ManifestValue Value)[] entries)
            => new(new ReadOnlyDictionary<string, ManifestValue>(entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)));

        private static ManifestValue.String StringOf(string text) => new(text);

        private static ManifestValue.Object MergeObject(string firstName, ManifestValue firstValue, ManifestValue? options, string? kind = null)
        {
            var properties = new Dictionary<string, ManifestValue>(StringComparer.Ordinal) { [firstName] = firstValue };
            if (kind is not null) properties["kind"] = StringOf(kind);
            if (options is ManifestValue.Object objectOptions)
            {
                foreach ((string key, ManifestValue value) in objectOptions.Properties) properties[key] = value;
            }

            return new ManifestValue.Object(new ReadOnlyDictionary<string, ManifestValue>(properties));
        }

        private static IEnumerable<TsXmlElementExpressionSyntax> ElementChildren(TsXmlElementExpressionSyntax element)
            => element.Children.OfType<TsXmlElementChildSyntax>().Select(child => child.Element).OfType<TsXmlElementExpressionSyntax>();

        private IReadOnlyList<ManifestValue> RequiredArray(Dictionary<string, ManifestValue> attributes, string name, SyntaxToken anchor)
            => attributes.TryGetValue(name, out ManifestValue? value) && value is ManifestValue.Array array
                ? array.Values
                : MissingArray(name, anchor);

        private IReadOnlyList<ManifestValue> MissingArray(string name, SyntaxToken anchor)
        {
            Report("COPE-MANIFEST-0028", $"Attribute '{name}' must be an array expression.", anchor);
            return [];
        }

        private ManifestValue? RequiredObject(Dictionary<string, ManifestValue> attributes, SyntaxToken anchor)
        {
            if (attributes.Count == 0) return null;
            return new ManifestValue.Object(new ReadOnlyDictionary<string, ManifestValue>(attributes));
        }

        private string? RequiredString(Dictionary<string, ManifestValue> attributes, string name, SyntaxToken anchor)
        {
            if (attributes.TryGetValue(name, out ManifestValue? value) && value is ManifestValue.String text && !string.IsNullOrWhiteSpace(text.Text)) return text.Text;
            Report("COPE-MANIFEST-0029", $"Attribute '{name}' must be a non-empty string.", AttributeToken(anchor, name) ?? anchor);
            return null;
        }

        private string? OptionalString(Dictionary<string, ManifestValue> attributes, string name, string? defaultValue, SyntaxToken anchor)
        {
            if (!attributes.TryGetValue(name, out ManifestValue? value)) return defaultValue;
            if (value is ManifestValue.String text) return text.Text;
            Report("COPE-MANIFEST-0029", $"Attribute '{name}' must be a string.", anchor);
            return defaultValue;
        }

        private IReadOnlyList<string> RequiredStringArray(Dictionary<string, ManifestValue> attributes, string name, SyntaxToken anchor)
            => StringArray(attributes.GetValueOrDefault(name), name, anchor);

        private IReadOnlyList<string> OptionalStringArray(Dictionary<string, ManifestValue> attributes, string name, SyntaxToken anchor)
            => attributes.ContainsKey(name) ? StringArray(attributes[name], name, anchor) : [];

        private IReadOnlyList<string> StringArray(ManifestValue? value, string name, SyntaxToken anchor)
        {
            if (value is ManifestValue.Array array && array.Values.All(item => item is ManifestValue.String)) return array.Values.Cast<ManifestValue.String>().Select(item => item.Text).ToArray();
            Report("COPE-MANIFEST-0028", $"Attribute '{name}' must be an array of strings.", anchor);
            return [];
        }

        private static string? StringProperty(ManifestValue.Object value, string name)
            => value.Properties.GetValueOrDefault(name) is ManifestValue.String text ? text.Text : null;

        private static IReadOnlyList<string>? StringArrayProperty(ManifestValue.Object value, string name)
            => value.Properties.GetValueOrDefault(name) is ManifestValue.Array array && array.Values.All(item => item is ManifestValue.String)
                ? array.Values.Cast<ManifestValue.String>().Select(item => item.Text).ToArray()
                : null;

        private static bool IsSafeRelativePath(string value)
            => !string.IsNullOrWhiteSpace(value)
                && !Path.IsPathRooted(value)
                && !value.Contains("..", StringComparison.Ordinal)
                && !value.Contains('\\');

        private static bool IsSafeRelativeDirectory(string value)
            => value == "." || IsSafeRelativePath(value.TrimEnd('/'));

        private void ValidateUnique(IEnumerable<(string Name, SyntaxToken Token)> values, string kind, SyntaxToken fallback)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string name, SyntaxToken token) in values)
            {
                if (!names.Add(name)) Report("COPE-MANIFEST-0030", $"Duplicate {kind} '{name}'.", token.Text.Length == 0 ? fallback : token);
            }
        }

        private SyntaxToken? AttributeToken(TsXmlElementExpressionSyntax element, string name)
            => element.Attributes.FirstOrDefault(attribute => attribute.NameToken.Text == name)?.NameToken;

        private SyntaxToken? AttributeToken(SyntaxToken _, string __) => null;

        private void Report(string id, string message, SyntaxToken token)
            => _diagnostics.Add(new Diagnostic(id, message, token.Position, Math.Max(1, token.Text.Length), _sourcePath));

        private void ReportAtStart(string id, string message)
            => _diagnostics.Add(new Diagnostic(id, message, 0, Math.Max(1, _tree.Text.Length), _sourcePath));

        private static SyntaxToken FirstToken(SyntaxNode node)
            => node.GetChildren().Select(child => child switch
            {
                SyntaxToken token => token,
                SyntaxNode childNode => FirstToken(childNode),
                _ => null,
            }).FirstOrDefault(token => token is not null)
                ?? new SyntaxToken(SyntaxKind.BadToken, 0, string.Empty, null);
    }
}
