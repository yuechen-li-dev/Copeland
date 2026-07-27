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
        throw new InvalidOperationException("Could not locate the unified React + CLR sample root.");
    }

    sampleRoot = parent.FullName;
}

string sourceRoot = Path.Combine(sampleRoot, "Copeland");
CopelandProjectSource[] sources = Directory.GetFiles(sourceRoot, "*.ts*", SearchOption.AllDirectories)
    .OrderBy(path => path, StringComparer.Ordinal)
    .Select(path => new CopelandProjectSource(
        Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'),
        path,
        File.ReadAllText(path)))
    .ToArray();
CopelandProjectSource bridgeSource = sources.Single(source => source.LogicalPath == "Bridge.ts");

var state = new CopelandJavaScriptHostType.TypeParameter("State");
var @event = new CopelandJavaScriptHostType.TypeParameter("Event");
var sender = new CopelandJavaScriptHostType.Callable([@event], CopelandJavaScriptHostType.Void);
var browserHost = new CopelandJavaScriptHostModuleContract(
    "@copeland/browser-v1",
    [
        new CopelandJavaScriptHostFunctionContract(
            "getMountElement",
            [CopelandJavaScriptHostType.String],
            new CopelandJavaScriptHostType.Named("ReactMountElement")),
        new CopelandJavaScriptHostFunctionContract(
            "dispatchReact",
            [
                state,
                new CopelandJavaScriptHostType.Callable([state, @event], state),
                new CopelandJavaScriptHostType.Callable([state, sender], CopelandJavaScriptHostType.Void),
            ],
            sender,
            ["State", "Event"]),
    ]);

CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(sources, new CopelandCompilationOptions
{
    SourcePath = Path.Combine(sourceRoot, "Project.tsx"),
    TsXmlProfile = CopelandTsXmlProfile.ReactM0,
    NpmPackages =
    [
        new CopelandNpmPackageContract("react", "19.2.7", [new CopelandNpmFunctionContract("createElement", [], "ReactNode")]),
        new CopelandNpmPackageContract("react-dom/client", "19.2.7", [new CopelandNpmFunctionContract("createRoot", ["ReactMountElement"], "ReactRoot")]),
    ],
    JavaScriptHostModules = [browserHost],
    ClrReferences = [new CopelandClrReference(typeof(System.Text.Json.JsonSerializer).Assembly.Location)],
});
if (!project.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, project.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
}

MirProjectGraph graph = project.MirProjectGraph!;
CopelandProjectCompilation bridgeProject = CopelandProjectCompiler.CompileToMir(
    [bridgeSource],
    new CopelandCompilationOptions
    {
        SourcePath = bridgeSource.SourcePath,
        ClrReferences = [new CopelandClrReference(typeof(System.Text.Json.JsonSerializer).Assembly.Location)],
    });
if (!bridgeProject.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, bridgeProject.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
}

MirProjectGraph bridgeGraph = bridgeProject.MirProjectGraph!;
CopelandBridgeGeneration bridge = CopelandBridgeGenerator.Generate(bridgeGraph);
CSharpCompilation clr = CSharpBackend.Emit(bridgeGraph.AggregateProgram);
if (clr.Diagnostics.Count > 0)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, clr.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
}

JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(
    graph,
    new JavaScriptEmissionOptions
    {
        RuntimeTarget = JavaScriptRuntimeTarget.Browser,
        Profile = JavaScriptEmissionProfile.Production,
        RemoteOperationRoutes = bridge.Routes,
    });
if (!emitted.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
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
foreach ((string path, string content) in emitted.Files)
{
    string outputPath = Path.Combine(browserRoot, path.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, content);
}

Console.WriteLine($"Unified React + CLR browser and bridge artifacts written to {hostRoot}");
