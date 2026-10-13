using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Compiler <fixture-root>");
    return 1;
}
string root = Path.GetFullPath(args[0]);
string evidence = Path.GetFullPath(Path.Combine(root, "../../../docs/Copeland/reviews/evidence/vue-friction-m8"));
Directory.CreateDirectory(evidence);
var packages = new CopelandNpmPackageContract[]
{
    new("vue", "3.5.43", [new("h", [], "VueNode")]),
    new("@copeland/vue-controls", "0.1.0", [], Components:
    [
        new("SearchField", Properties:
        [
            new("id", "string", true), new("type", "string"), new("value", "string", true),
            new("onValueChange", "(string)=>void", true),
        ], Renderer: "vue"),
        new("Panel", Properties: [new("label", "string", true)], Renderer: "vue", DefaultSlot: true),
    ]),
    new("reka-ui", "2.11.0", [], Components:
    [
        new("Toggle", Properties:
        [
            new("modelValue", "boolean", true), new("onUpdate:modelValue", "(boolean)=>void", true),
        ], Renderer: "vue", DefaultSlot: true),
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
    ProjectTypes = CopelandProjectTypeSet.VueComponents,
    NpmDependencies = new CopelandNpmDependencyGraph(packages),
    JavaScriptHostModules = [CopelandProjectHostContracts.Browser(CopelandProjectTypeSet.VueComponents)],
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
var results = new List<object>();
foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
{
    JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!,
        new JavaScriptEmissionOptions { RuntimeTarget = JavaScriptRuntimeTarget.Browser, Profile = profile });
    emitted = LayoutJavaScriptProjectEmitter.AddLayouts(emitted, compilation.Modules);
    if (!emitted.Success)
    {
        foreach (var diagnostic in emitted.Diagnostics) Console.Error.WriteLine($"{diagnostic.Id}: {diagnostic.Message}");
        return 1;
    }
    string directory = Path.Combine(root, "artifacts", profile.ToString().ToLowerInvariant());
    foreach ((string name, string content) in emitted.Files)
    {
        string target = Path.Combine(directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, content, new UTF8Encoding(false));
    }
    results.Add(new
    {
        profile = profile.ToString(),
        files = emitted.Files.ToDictionary(file => file.Key,
            file => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(file.Value)))),
    });
    Console.WriteLine($"{profile}: compiled {emitted.Files.Count} files");
}
File.WriteAllText(Path.Combine(evidence, "compiler-report.json"), JsonSerializer.Serialize(new
{
    milestone = "CTS-VUE-FRICTION-M8",
    sourceHashes = sources.ToDictionary(source => source.LogicalPath,
        source => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.SourceText)))),
    results,
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
return 0;
