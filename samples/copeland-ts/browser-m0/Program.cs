using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;

string sampleRoot = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(sampleRoot, "index.html")))
{
    DirectoryInfo? parent = Directory.GetParent(sampleRoot);
    if (parent is null)
    {
        throw new InvalidOperationException("Could not locate the browser M0 sample root.");
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

var browserHost = new CopelandJavaScriptHostModuleContract(
    "@copeland/browser-m0",
    [
        new CopelandJavaScriptHostFunctionContract(
            "setText",
            [CopelandJavaScriptHostType.String, CopelandJavaScriptHostType.String],
            CopelandJavaScriptHostType.Void),
        new CopelandJavaScriptHostFunctionContract(
            "onClick",
            [
                CopelandJavaScriptHostType.String,
                new CopelandJavaScriptHostType.Callable([CopelandJavaScriptHostType.Int], CopelandJavaScriptHostType.Int),
            ],
            CopelandJavaScriptHostType.Void),
    ]);

CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(sources, new CopelandCompilationOptions
{
    SourcePath = Path.Combine(sourceRoot, "Project.ts"),
    JavaScriptHostModules = [browserHost],
});
if (!project.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, project.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
}

JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(
    project.MirProjectGraph!,
    new JavaScriptEmissionOptions { RuntimeTarget = JavaScriptRuntimeTarget.Browser });
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

Console.WriteLine($"Browser ESM written to {outputRoot}");
