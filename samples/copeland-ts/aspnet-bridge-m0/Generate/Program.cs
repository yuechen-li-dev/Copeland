using Copeland.TS.Backend.AspNetCore;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;

string sampleRoot = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(sampleRoot, "index.html")))
{
    DirectoryInfo? parent = Directory.GetParent(sampleRoot);
    if (parent is null)
    {
        throw new InvalidOperationException("Could not locate the ASP.NET bridge M0 sample root.");
    }

    sampleRoot = parent.FullName;
}

string sourceRoot = Path.Combine(sampleRoot, "Copeland");
CopelandProjectSource[] sources = Directory.GetFiles(sourceRoot, "*.ts", SearchOption.AllDirectories)
    .OrderBy(path => path, StringComparer.Ordinal)
    .Select(path => new CopelandProjectSource(
        Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'),
        path,
        File.ReadAllText(path)))
    .ToArray();

CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
    sources,
    new CopelandCompilationOptions
    {
        SourcePath = Path.Combine(sourceRoot, "Project.ts"),
    });
if (!project.Success)
{
    throw new InvalidOperationException(string.Join(
        Environment.NewLine,
        project.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
}

MirProjectGraph graph = project.MirProjectGraph!;
CopelandBridgeGeneration bridge = CopelandBridgeGenerator.Generate(graph);
CSharpCompilation clr = CSharpBackend.Emit(graph.AggregateProgram);
if (clr.Diagnostics.Count > 0)
{
    throw new InvalidOperationException(string.Join(
        Environment.NewLine,
        clr.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
}

JavaScriptProjectCompilation browser = JavaScriptProjectEmitter.Emit(
    graph,
    new JavaScriptEmissionOptions
    {
        Profile = JavaScriptEmissionProfile.Production,
        RuntimeTarget = JavaScriptRuntimeTarget.Browser,
        RemoteOperationRoutes = bridge.Routes,
    });
if (!browser.Success)
{
    throw new InvalidOperationException(string.Join(
        Environment.NewLine,
        browser.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
}

string hostRoot = Path.Combine(sampleRoot, "Host");
string generatedRoot = Path.Combine(hostRoot, "Generated");
string browserRoot = Path.Combine(hostRoot, "wwwroot");
Directory.CreateDirectory(generatedRoot);
Directory.CreateDirectory(browserRoot);
File.WriteAllText(Path.Combine(generatedRoot, "Copeland.g.cs"), clr.SourceText);
File.WriteAllText(Path.Combine(generatedRoot, "BridgeEndpoints.g.cs"), bridge.EndpointSource);
File.WriteAllText(Path.Combine(browserRoot, "bridge-contract.json"), bridge.ContractJson);
File.WriteAllText(Path.Combine(browserRoot, "bridge-config.js"), "export const baseUrl = window.location.origin;\n");
File.Copy(Path.Combine(sampleRoot, "index.html"), Path.Combine(browserRoot, "index.html"), overwrite: true);
foreach ((string path, string content) in browser.Files)
{
    string outputPath = Path.Combine(browserRoot, path.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, content);
}

Console.WriteLine($"Generated bridge contract and hosts under {hostRoot}");
