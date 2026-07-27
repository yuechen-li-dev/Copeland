using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;

string sampleRoot = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(sampleRoot, "index.html")))
{
    DirectoryInfo? parent = Directory.GetParent(sampleRoot);
    if (parent is null)
    {
        throw new InvalidOperationException("Could not locate the TS-XML React M0 sample root.");
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
});
if (!project.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, project.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
}

JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(
    project.MirProjectGraph!,
    new JavaScriptEmissionOptions
    {
        RuntimeTarget = JavaScriptRuntimeTarget.Browser,
        Profile = JavaScriptEmissionProfile.Production,
    });
if (!emitted.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
}

string outputRoot = Path.Combine(sampleRoot, "generated");
Directory.CreateDirectory(outputRoot);
foreach ((string path, string content) in emitted.Files)
{
    string outputPath = Path.Combine(outputRoot, path.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, content);
}

Console.WriteLine($"React browser ESM written to {outputRoot}");
