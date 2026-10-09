using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Compiler;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

/// <summary>
/// Project-owned shader modules. Source spelling resolves to declaration identity
/// before binding; no filesystem loader or textual shader concatenation is used.
/// </summary>
internal sealed class GpuModuleGraph
{
    private readonly Dictionary<string, Module> modules = new(StringComparer.Ordinal);
    private readonly List<VdMirDiagnostic> diagnostics;
    private readonly bool scoped;

    public GpuModuleGraph(GpuCompilationRequest request, List<VdMirDiagnostic> diagnostics)
    {
        this.diagnostics = diagnostics;
        foreach (GpuSourceFile source in request.Sources.OrderBy(source => source.Path, StringComparer.Ordinal))
        {
            string path = Normalize(source.Path);
            var projectSource = new CopelandProjectSource(path, source.Path, source.Source);
            SyntaxTree tree = SyntaxTree.Parse(source.Source, source.Path);
            var module = new Module(source, tree, CopelandProjectCompiler.ReadSourceImports(projectSource),
                CopelandProjectCompiler.ReadExports(projectSource));
            if (!modules.TryAdd(path, module))
            {
                Error("COPE-GPU-MODULE-0001", "Duplicate normalized shader source path.", source.Path, 0, 1);
            }
            foreach (var diagnostic in tree.Diagnostics)
            {
                diagnostics.Add(new(diagnostic.Id, "SDSL-V1000", "syntax", diagnostic.Message,
                    new(source.Path, diagnostic.Position, diagnostic.Length), []));
            }
        }
        scoped = modules.Values.Any(module => module.Imports.Count > 0 || module.Exports.Count > 0);
        foreach (Module module in modules.Values)
        {
            foreach (MemberSyntax member in module.Tree.Root.Members)
            {
                SyntaxToken? name = DeclarationName(member);
                if (name is null)
                {
                    continue;
                }
                if (name.Text.StartsWith("Vts", StringComparison.Ordinal)
                    || name.Text.StartsWith("__vts_", StringComparison.Ordinal))
                {
                    Error("COPE-GPU-SYMBOL-0002", "Shader declarations beginning with Vts or __vts_ are reserved for compiler-generated identities.",
                        module.Source.Path, name.Position, name.Text.Length);
                }
                if (!module.Symbols.TryAdd(name.Text, Identity(module, name.Text)))
                {
                    Error("COPE-GPU-SYMBOL-0001", $"Duplicate declaration '{name.Text}'.", module.Source.Path, name.Position, name.Text.Length);
                }
            }
        }
        if (scoped)
        {
            foreach (Module module in modules.Values)
            {
                ResolveImports(module);
            }
            DetectCycles();
        }
    }

    public IEnumerable<(GpuSourceFile Source, SyntaxTree Tree)> Sources => modules.Values
        .OrderBy(module => module.Source.Path, StringComparer.Ordinal)
        .Select(module => (module.Source, module.Tree));

    public string Declare(string path, string name) => Identity(modules[Normalize(path)], name);

    public string Resolve(string path, string name)
    {
        if (!scoped)
        {
            return name;
        }
        Module module = modules[Normalize(path)];
        return module.Symbols.GetValueOrDefault(name) ?? "__vts_unresolved_" + name;
    }

    private string Identity(Module module, string name)
    {
        if (!scoped || module.IsEntryModule && modules.Values.Count(item => item.IsEntryModule) == 1)
        {
            return name;
        }
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(module.Source.Path)));
        return "Vts_" + Convert.ToHexString(hash.AsSpan(0, 8)) + "_" + name;
    }

    private void ResolveImports(Module module)
    {
        foreach (ImportDeclarationSyntax declaration in module.Tree.Root.Members.OfType<ImportDeclarationSyntax>())
        {
            if (!IsNamedImport(declaration.Tokens))
            {
                SyntaxToken token = declaration.Tokens[0];
                Error("COPE-GPU-MODULE-0004", "GPU modules require import { name, name as alias } from a relative module; other import shapes are unsupported.",
                    module.Source.Path, token.Position, token.Text.Length);
            }
        }
        foreach (var import in module.Imports)
        {
            if (!import.Specifier.StartsWith("./", StringComparison.Ordinal)
                && !import.Specifier.StartsWith("../", StringComparison.Ordinal))
            {
                Error("COPE-GPU-MODULE-0002", "GPU imports must name a relative project-owned module.", module.Source.Path, import.Position, import.Length);
                continue;
            }
            string[] candidates = ImportCandidates(module.Source.Path, import.Specifier);
            Module[] matches = candidates.Where(modules.ContainsKey).Select(path => modules[path]).ToArray();
            if (matches.Length != 1)
            {
                Error("COPE-GPU-MODULE-0003", $"Module '{import.Specifier}' resolves to {matches.Length} supplied sources; exactly one is required.", module.Source.Path, import.Position, import.Length);
                continue;
            }
            Module target = matches[0];
            module.Dependencies.Add(target);
            if (import.Bindings.Count == 0)
            {
                Error("COPE-GPU-MODULE-0004", "GPU modules require named imports; side-effect, namespace and default imports are unsupported.", module.Source.Path, import.Position, import.Length);
            }
            foreach (var binding in import.Bindings)
            {
                if (!target.Exports.Contains(binding.ExportedName) || !target.Symbols.TryGetValue(binding.ExportedName, out string? identity))
                {
                    Error("COPE-GPU-MODULE-0005", $"Module '{target.Source.Path}' does not export '{binding.ExportedName}'.", module.Source.Path, binding.Position, binding.Length);
                    continue;
                }
                if (!module.Symbols.TryAdd(binding.LocalName, identity))
                {
                    Error("COPE-GPU-MODULE-0006", $"Import alias '{binding.LocalName}' collides with a local declaration or import.", module.Source.Path, binding.Position, binding.Length);
                }
            }
        }
    }

    private static bool IsNamedImport(IReadOnlyList<SyntaxToken> tokens)
    {
        if (tokens.Count < 6 || tokens[1].Kind != SyntaxKind.OpenBraceToken)
        {
            return false;
        }
        int index = 2;
        while (index < tokens.Count && tokens[index].Kind != SyntaxKind.CloseBraceToken)
        {
            if (tokens[index].Kind != SyntaxKind.IdentifierToken)
            {
                return false;
            }
            index++;
            if (index < tokens.Count && tokens[index].Text == "as")
            {
                index++;
                if (index >= tokens.Count || tokens[index].Kind != SyntaxKind.IdentifierToken)
                {
                    return false;
                }
                index++;
            }
            if (index < tokens.Count && tokens[index].Kind == SyntaxKind.CommaToken)
            {
                index++;
            }
            else if (index >= tokens.Count || tokens[index].Kind != SyntaxKind.CloseBraceToken)
            {
                return false;
            }
        }
        if (index + 2 >= tokens.Count || tokens[index].Kind != SyntaxKind.CloseBraceToken
            || tokens[index + 1].Text != "from" || tokens[index + 2].Kind != SyntaxKind.StringToken)
        {
            return false;
        }
        index += 3;
        return index == tokens.Count || index + 1 == tokens.Count && tokens[index].Kind == SyntaxKind.SemicolonToken;
    }

    private void DetectCycles()
    {
        var active = new List<Module>();
        var complete = new HashSet<Module>();
        foreach (Module module in modules.Values.OrderBy(module => module.Source.Path, StringComparer.Ordinal))
        {
            Visit(module);
        }
        void Visit(Module module)
        {
            if (complete.Contains(module))
            {
                return;
            }
            if (active.Contains(module))
            {
                string cycle = string.Join(" -> ", active.Skip(active.IndexOf(module)).Append(module).Select(item => item.Source.Path));
                Error("COPE-GPU-MODULE-0007", "Shader import cycle: " + cycle, module.Source.Path, 0, 1);
                return;
            }
            active.Add(module);
            foreach (Module dependency in module.Dependencies.OrderBy(item => item.Source.Path, StringComparer.Ordinal))
            {
                Visit(dependency);
            }
            active.RemoveAt(active.Count - 1);
            complete.Add(module);
        }
    }

    private void Error(string code, string message, string path, int start, int length)
    {
        diagnostics.Add(new(code, "SDSL-V1509", "module", message, new(path, start, length), []));
    }

    private static SyntaxToken? DeclarationName(MemberSyntax member) => member switch
    {
        FunctionDeclarationSyntax function => function.Identifier,
        ShaderStreamDeclarationSyntax stream => stream.Identifier,
        RecordDeclarationSyntax record => record.Identifier,
        EnumDeclarationSyntax enumeration => enumeration.Identifier,
        TypeAliasDeclarationSyntax alias => alias.Identifier,
        InterfaceDeclarationSyntax requirement => requirement.Identifier,
        TemplateDeclarationSyntax template => template.Identifier,
        GlobalStatementMemberSyntax { Statement: VariableDeclarationStatementSyntax value } => value.Identifier,
        _ => null,
    };

    internal static string[] ImportCandidates(string path, string specifier)
    {
        if (!specifier.StartsWith("./", StringComparison.Ordinal) && !specifier.StartsWith("../", StringComparison.Ordinal))
        {
            return [];
        }
        string directory = Normalize(path);
        int separator = directory.LastIndexOf('/');
        directory = separator < 0 ? string.Empty : directory[..(separator + 1)];
        string stem = Normalize(directory + specifier);
        return stem.EndsWith(".ts", StringComparison.Ordinal) ? [stem] : [stem + ".v.ts", stem + ".ts"];
    }

    internal static string Normalize(string path)
    {
        var components = new List<string>();
        foreach (string component in path.Replace('\\', '/').Split('/'))
        {
            if (component is "" or ".")
            {
                continue;
            }
            if (component == ".." && components.Count > 0 && components[^1] != "..")
            {
                components.RemoveAt(components.Count - 1);
            }
            else
            {
                components.Add(component);
            }
        }
        return string.Join('/', components);
    }

    private sealed class Module(GpuSourceFile source, SyntaxTree tree,
        IReadOnlyList<CopelandProjectCompiler.SourceImport> imports, IReadOnlySet<string> exports)
    {
        public GpuSourceFile Source { get; } = source;
        public SyntaxTree Tree { get; } = tree;
        public IReadOnlyList<CopelandProjectCompiler.SourceImport> Imports { get; } = imports;
        public IReadOnlySet<string> Exports { get; } = exports;
        public Dictionary<string, string> Symbols { get; } = new(StringComparer.Ordinal);
        public List<Module> Dependencies { get; } = [];
        public bool IsEntryModule { get; } = tree.Root.Members.OfType<FunctionDeclarationSyntax>()
            .Any(function => (function.Annotations ?? []).Any(annotation => annotation.NameToken.Text is "vertex" or "pixel" or "compute"));
    }
}
