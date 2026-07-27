using Copeland.TS.Backend.AspNetCore;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;

var options = GeneratorOptions.Parse(args);
string sourceRoot = Path.Combine(options.SourceRoot, "Copeland");
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

CopelandProjectCompilation browserProject = CopelandProjectCompiler.CompileToMir(sources, new CopelandCompilationOptions
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
EnsureSuccess(browserProject);

CopelandProjectCompilation bridgeProject = CopelandProjectCompiler.CompileToMir(
    [bridgeSource],
    new CopelandCompilationOptions
    {
        SourcePath = bridgeSource.SourcePath,
        ClrReferences = [new CopelandClrReference(typeof(System.Text.Json.JsonSerializer).Assembly.Location)],
    });
EnsureSuccess(bridgeProject);

CopelandBridgeGeneration bridge = CopelandBridgeGenerator.Generate(bridgeProject.MirProjectGraph!);
CSharpCompilation clr = CSharpBackend.Emit(bridgeProject.MirProjectGraph!.AggregateProgram);
if (clr.Diagnostics.Count > 0)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, clr.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
}

JavaScriptProjectCompilation browser = JavaScriptProjectEmitter.Emit(
    browserProject.MirProjectGraph!,
    new JavaScriptEmissionOptions
    {
        RuntimeTarget = JavaScriptRuntimeTarget.Browser,
        Profile = JavaScriptEmissionProfile.Production,
        RemoteOperationRoutes = bridge.Routes,
    });
EnsureBrowserSuccess(browser);

Directory.CreateDirectory(options.GeneratedRoot);
Directory.CreateDirectory(options.WebRoot);
File.WriteAllText(Path.Combine(options.GeneratedRoot, "Copeland.g.cs"), clr.SourceText);
File.WriteAllText(Path.Combine(options.GeneratedRoot, "BridgeEndpoints.g.cs"), bridge.EndpointSource);
File.WriteAllText(Path.Combine(options.WebRoot, "bridge-contract.json"), bridge.ContractJson);
File.WriteAllText(Path.Combine(options.WebRoot, "bridge-config.js"), "export const baseUrl = window.location.origin;\n");
File.Copy(Path.Combine(options.SourceRoot, "index.html"), Path.Combine(options.WebRoot, "index.html"), overwrite: true);
foreach ((string path, string content) in browser.Files)
{
    string outputPath = Path.Combine(options.WebRoot, path.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, content);
}

Console.WriteLine($"COPELAND_STANDALONE_GENERATED {options.WebRoot}");

static void EnsureSuccess(CopelandProjectCompilation compilation)
{
    if (!compilation.Success)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
    }
}

static void EnsureBrowserSuccess(JavaScriptProjectCompilation compilation)
{
    if (!compilation.Success)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
    }
}

sealed record GeneratorOptions(string SourceRoot, string WebRoot, string GeneratedRoot)
{
    public static GeneratorOptions Parse(string[] args)
    {
        string? sourceRoot = GetValue(args, "--source-root");
        string? webRoot = GetValue(args, "--web-root");
        string? generatedRoot = GetValue(args, "--generated-root");
        if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(webRoot) || string.IsNullOrWhiteSpace(generatedRoot))
        {
            throw new ArgumentException("Expected --source-root, --web-root, and --generated-root.");
        }

        return new GeneratorOptions(Path.GetFullPath(sourceRoot), Path.GetFullPath(webRoot), Path.GetFullPath(generatedRoot));
    }

    private static string? GetValue(IReadOnlyList<string> args, string name)
    {
        for (int index = 0; index < args.Count - 1; index += 1)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal)) return args[index + 1];
        }

        return null;
    }
}
