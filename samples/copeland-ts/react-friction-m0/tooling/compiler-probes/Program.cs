using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CompilerProbes <corpus.json> <report.json>");
    return 1;
}

using JsonDocument corpus = JsonDocument.Parse(File.ReadAllText(args[0]));
var results = new List<object>();
bool expectationsMatched = true;

foreach (JsonElement specimen in corpus.RootElement.GetProperty("cases").EnumerateArray())
{
    string name = specimen.GetProperty("name").GetString()!;
    var sources = new List<CopelandProjectSource>();
    foreach (JsonElement source in specimen.GetProperty("sources").EnumerateArray())
    {
        string path = source.GetProperty("path").GetString()!;
        sources.Add(new CopelandProjectSource(path, path, source.GetProperty("text").GetString()!));
    }

    var options = new CopelandCompilationOptions
    {
        SourcePath = "Main.tsx",
        TsXmlProfile = CopelandTsXmlProfile.ReactM0,
        NpmPackages =
        [
            new CopelandNpmPackageContract("react", "19.2.7",
                [new CopelandNpmFunctionContract("createElement", [], "ReactNode")])
        ]
    };
    CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(sources, options);
    var diagnostics = compilation.Diagnostics.Select(diagnostic => new
    {
        code = diagnostic.Id,
        message = diagnostic.Message,
        repairKind = diagnostic.RepairKind,
        suggestedReplacement = diagnostic.SuggestedReplacement
    }).ToList();
    IReadOnlyDictionary<string, string>? emittedFiles = null;
    if (diagnostics.Count == 0 && compilation.MirProjectGraph is not null)
    {
        JavaScriptProjectCompilation emission = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph);
        emission = LayoutJavaScriptProjectEmitter.AddLayouts(emission, compilation.Modules);
        diagnostics.AddRange(emission.Diagnostics.Select(diagnostic => new
        {
            code = diagnostic.Id,
            message = diagnostic.Message,
            repairKind = (string?)null,
            suggestedReplacement = (string?)null
        }));
        emittedFiles = emission.Files;
    }

    bool accepted = diagnostics.Count == 0;
    bool expectedAccepted = specimen.GetProperty("expected").GetString() == "accept";
    bool matches = accepted == expectedAccepted;
    if (specimen.TryGetProperty("diagnostic", out JsonElement expectedDiagnostic))
    {
        matches &= diagnostics.Any(diagnostic => diagnostic.code == expectedDiagnostic.GetString());
    }
    expectationsMatched &= matches;
    results.Add(new
    {
        name,
        accepted,
        expectationMatched = matches,
        diagnostics,
        javascript = emittedFiles?.Where(file => file.Key.EndsWith(".js", StringComparison.Ordinal))
            .ToDictionary(file => file.Key, file => file.Value),
        css = emittedFiles?.GetValueOrDefault("generated/layouts.css")
    });
    Console.WriteLine($"{name}: {(accepted ? "accepted" : string.Join(", ", diagnostics.Select(diagnostic => diagnostic.code).Distinct()))}");
}

string? directory = Path.GetDirectoryName(Path.GetFullPath(args[1]));
Directory.CreateDirectory(directory!);
var report = new
{
    milestone = corpus.RootElement.TryGetProperty("milestone", out JsonElement milestone)
        ? milestone.GetString()
        : "CTS-REACT-FRICTION-M0",
    compilerAssemblySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CopelandProjectCompiler).Assembly.Location))),
    corpusSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(args[0]))),
    expectationsMatched,
    results
};
File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
return expectationsMatched ? 0 : 1;
