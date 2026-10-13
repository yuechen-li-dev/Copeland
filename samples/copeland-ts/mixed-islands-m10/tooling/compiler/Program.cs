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
string evidence = args.Length == 2
    ? Path.GetFullPath(args[1])
    : Path.GetFullPath(Path.Combine(root, "../../../docs/Copeland/reviews/evidence/mixed-islands-m10"));
Directory.CreateDirectory(evidence);
var results = new List<object>();
foreach (MirPresentationAdapter adapter in new[] { MirPresentationAdapter.React, MirPresentationAdapter.Vue })
{
    var props = new CopelandNpmComponentPropertyContract[]
    {
        new("hostKey", "string", true), new("id", "string", true),
        new("label", "string", true), new("value", "string", true),
        new("onValueChange", "(string)=>void", true),
    };
    var packages = new CopelandNpmPackageContract[]
    {
        new("react", "19.2.7", [new("createElement", [], "ReactNode")]),
        new("vue", "3.5.43", [new("h", [], "VueNode")]),
        new("@copeland/island-controls", "0.1.0", [], Components:
        [
            new("ReactEditor", props, Renderer: adapter.Identity, PresentationHost: true),
            new("VueEditor", props, Renderer: adapter.Identity, PresentationHost: true),
        ]),
        new("@copeland/lifecycle-host", "0.1.0",
        [new("focus", ["string"], "void"), new("saveDraft", ["string", "string"], "string", "string", IsPromise: true)]),
    };
    string sourcePath = Path.Combine(root, "src/Application.tsx");
    var compilation = CopelandProjectCompiler.CompileToMir(
        [new("src/Application.tsx", sourcePath, File.ReadAllText(sourcePath))], new()
        {
            ProjectTypes = CopelandProjectTypeSet.CopelandViews,
            PresentationAdapter = adapter,
            NpmDependencies = new(packages),
            JavaScriptHostModules = [CopelandProjectHostContracts.Browser(CopelandProjectTypeSet.CopelandViews)],
        });
    if (!compilation.Success)
    {
        foreach (var diagnostic in compilation.Diagnostics)
        {
            Console.Error.WriteLine($"{diagnostic.Id} at {diagnostic.Position}: {diagnostic.Message}");
        }
        return 1;
    }
    foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
    {
        var emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!,
            new() { RuntimeTarget = JavaScriptRuntimeTarget.Browser, Profile = profile });
        emitted = LayoutJavaScriptProjectEmitter.AddLayouts(emitted, compilation.Modules);
        if (!emitted.Success)
        {
            foreach (var diagnostic in emitted.Diagnostics)
            {
                Console.Error.WriteLine($"{diagnostic.Id}: {diagnostic.Message}");
            }
            return 1;
        }
        string destination = Path.Combine(root, "artifacts", adapter.Identity, profile.ToString().ToLowerInvariant());
        foreach ((string name, string content) in emitted.Files)
        {
            string path = Path.Combine(destination, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
        results.Add(new { adapter = adapter.Identity, profile = profile.ToString(),
            hashes = emitted.Files.ToDictionary(file => file.Key,
                file => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(file.Value)))) });
        Console.WriteLine($"{adapter.Identity}/{profile}: {emitted.Files.Count} files");
    }
}
File.WriteAllText(Path.Combine(evidence, "compiler-report.json"), JsonSerializer.Serialize(new
{
    milestone = "CTS-MIXED-ISLANDS-M10",
    sourceHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "src/Application.tsx")))),
    results,
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
return 0;
