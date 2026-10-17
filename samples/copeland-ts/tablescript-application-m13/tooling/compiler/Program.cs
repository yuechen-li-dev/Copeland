using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.InstinctCorpus;
using Copeland.TS.Mir;

string root = Path.GetFullPath(args.Single());
string evidence = Path.GetFullPath(Path.Combine(root, "../../../docs/Copeland/reviews/evidence/tablescript-application-m13"));
Directory.CreateDirectory(evidence);
string workbook = Path.GetFullPath(Path.Combine(root, "../tablescript-friction-m12/Workbook/Workbook.ts"));
CopelandProjectSource Source(string logical, string path) => new(logical, path, File.ReadAllText(path));
CopelandProjectSource[] domainSources =
[
    Source("src/Workbook.ts", workbook),
    Source("src/Application.ts", Path.Combine(root, "src/Application.ts")),
];
CopelandProjectCompilation domain = CopelandProjectCompiler.CompileToMir(domainSources);
Require(domain.Success, string.Join(Environment.NewLine, domain.Diagnostics));
var csharp = CSharpBackend.Emit(domain.MirProjectGraph!.AggregateProgram);
Require(csharp.Diagnostics.Count == 0, string.Join(Environment.NewLine, csharp.Diagnostics));
Directory.CreateDirectory(Path.Combine(root, "artifacts"));
File.WriteAllText(Path.Combine(root, "artifacts/Application.g.cs"), csharp.SourceText);
Type module = CorpusRunner.CompileCSharp(csharp.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
string probeName = domain.MirProjectGraph.Modules.Single(item => item.Id.Value == "src/Application.ts")
    .Exports.Single(item => item.Name == "applicationProbe").RuntimeName!;
int[] observed = (int[])module.GetMethod(probeName)!.Invoke(null, null)!;
int[] expected = [31210, 12710, 47, 2, 6, 12, 5, 2, 20, 30, 1, 1, 31210];
Require(observed.SequenceEqual(expected), "Application C# probe: " + JsonSerializer.Serialize(observed));

var packages = new CopelandNpmPackageContract[]
{
    new("react", "19.2.7", [new("createElement", [], "ReactNode")]),
    new("@copeland/cafe-controls", "0.1.0", [], Components:
    [
        new("PreviewForm", Properties:
        [
            new("onCommit", "()=>void", true), new("children", "View[]"),
        ], Renderer: "react", DefaultSlot: true),
        new("CountField", Properties:
        [
            new("id", "string", true), new("label", "string", true),
            new("value", "int", true), new("resetKey", "int", true), new("onValueChange", "(int)=>void", true),
        ], Renderer: "react"),
    ]),
};
CopelandProjectSource[] frontendSources = domainSources.Concat(
    new[] { "Editor.ts", "Cafe.tsx", "Collections.ts" }
        .Select(name => Source("src/" + name, Path.Combine(root, "src", name)))).ToArray();
CopelandProjectCompilation frontend = CopelandProjectCompiler.CompileToMir(frontendSources, new CopelandCompilationOptions
{
    ProjectRoot = root,
    ProjectTypes = CopelandProjectTypeSet.CopelandViews,
    PresentationAdapter = MirPresentationAdapter.React,
    NpmDependencies = new CopelandNpmDependencyGraph(packages),
});
Require(frontend.Success, string.Join(Environment.NewLine, frontend.Diagnostics));
var outputs = new List<object>();
foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
{
    JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(frontend.MirProjectGraph!,
        new JavaScriptEmissionOptions { RuntimeTarget = JavaScriptRuntimeTarget.Browser, Profile = profile });
    emitted = LayoutJavaScriptProjectEmitter.AddLayouts(emitted, frontend.Modules);
    Require(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
    string directory = Path.Combine(root, "artifacts", profile.ToString().ToLowerInvariant());
    foreach ((string name, string content) in emitted.Files)
    {
        string path = Path.Combine(directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
    outputs.Add(new { profile = profile.ToString(), files = emitted.Files.ToDictionary(file => file.Key, file => Hash(Encoding.UTF8.GetBytes(file.Value))) });
    Console.WriteLine($"{profile}: {emitted.Files.Count} ESM/layout artifacts");
}
File.WriteAllText(Path.Combine(evidence, "compiler-report.json"), JsonSerializer.Serialize(new
{
    milestone = "CTS-TABLESCRIPT-APPLICATION-M13",
    sourceHashes = frontendSources.ToDictionary(source => source.LogicalPath, source => Hash(File.ReadAllBytes(source.SourcePath))),
    csharpApplicationProbe = observed,
    outputs,
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("C# application probe: " + JsonSerializer.Serialize(observed));
return 0;

static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
static void Require(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

