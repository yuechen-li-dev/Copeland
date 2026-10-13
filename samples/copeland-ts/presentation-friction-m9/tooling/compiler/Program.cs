using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: Compiler <fixture-root> [evidence-directory]");
    return 1;
}
string root = Path.GetFullPath(args[0]);
string evidence = args.Length == 2 ? Path.GetFullPath(args[1])
    : Path.GetFullPath(Path.Combine(root, "../../../docs/Copeland/reviews/evidence/presentation-friction-m9"));
Directory.CreateDirectory(evidence);
var results = new List<object>();
foreach (MirPresentationAdapter adapter in new[] { MirPresentationAdapter.React, MirPresentationAdapter.Vue })
{
    var packages = new CopelandNpmPackageContract[]
    {
        new("vue", "3.5.43", [new("h", [], "VueNode")]),
        new("react", "19.2.7", [new("createElement", [], "ReactNode")]),
        new("@copeland/presentation-controls", "0.1.0", [], Components:
        [
            new("SearchField", Properties:
            [
                new("id", "string", true), new("type", "string"), new("value", "string", true),
                new("onValueChange", "(string)=>void", true),
            ], Renderer: adapter.Identity),
            new("ArrangementToggle", Properties: [new("value", "boolean", true),
                new("onValueChange", "(boolean)=>void", true), new("children", "View[]")], Renderer: adapter.Identity, DefaultSlot: true),
            new("Panel", Properties: [new("label", "string", true), new("children", "View[]")], Renderer: adapter.Identity, DefaultSlot: true),
        ]),
        new("@copeland/lifecycle-host", "0.1.0",
        [
            new("search", ["string"], "string", "string", IsPromise: true),
            new("saveDraft", ["string", "string"], "string", "string", IsPromise: true),
            new("focus", ["string"], "void"),
        ]),
    };
    var options = new CopelandCompilationOptions
    {
        ProjectRoot = root,
        ProjectTypes = CopelandProjectTypeSet.CopelandViews,
        PresentationAdapter = adapter,
        NpmDependencies = new CopelandNpmDependencyGraph(packages),
        JavaScriptHostModules = [CopelandProjectHostContracts.Browser(CopelandProjectTypeSet.CopelandViews)],
    };
    CopelandProjectSource[] sources = Directory.EnumerateFiles(Path.Combine(root, "src"))
        .Where(path => path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".tsx", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .Select(path => new CopelandProjectSource("src/" + Path.GetFileName(path), path, File.ReadAllText(path)))
        .ToArray();
    CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(sources, options);
    if (!compilation.Success)
    {
        foreach (var diagnostic in compilation.Diagnostics)
        {
            Console.Error.WriteLine($"{diagnostic.Id}: {diagnostic.Message}");
        }
        return 1;
    }
    foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
    {
        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!,
            new JavaScriptEmissionOptions { RuntimeTarget = JavaScriptRuntimeTarget.Browser, Profile = profile });
        emitted = LayoutJavaScriptProjectEmitter.AddLayouts(emitted, compilation.Modules);
        if (!emitted.Success)
        {
            foreach (var diagnostic in emitted.Diagnostics)
            {
                Console.Error.WriteLine($"{diagnostic.Id}: {diagnostic.Message}");
            }
            return 1;
        }
        string directory = Path.Combine(root, "artifacts", adapter.Identity, profile.ToString().ToLowerInvariant());
        foreach ((string name, string content) in emitted.Files)
        {
            string target = Path.Combine(directory, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content, new UTF8Encoding(false));
        }
        results.Add(new
        {
            adapter = adapter.Identity,
            profile = profile.ToString(),
            files = emitted.Files.ToDictionary(file => file.Key,
                file => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(file.Value)))),
        });
        Console.WriteLine($"{adapter.Identity}/{profile}: compiled {emitted.Files.Count} files");
    }
}
var sourceHashes = Directory.EnumerateFiles(Path.Combine(root, "src"))
    .Order(StringComparer.Ordinal)
    .ToDictionary(path => "src/" + Path.GetFileName(path),
        path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
File.WriteAllText(Path.Combine(evidence, "compiler-report.json"), JsonSerializer.Serialize(new
{
    milestone = "CTS-PRESENTATION-FRICTION-M9",
    sourceHashes,
    results,
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
return 0;
