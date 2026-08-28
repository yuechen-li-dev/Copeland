namespace Copeland.TS.Compiler;

public enum CopelandBackend
{
    JavaScript,
    CSharp,
}

public enum CopelandExecutionRuntime
{
    Node,
    Browser,
    RyuJit,
    NativeAot,
    DotNetWasm,
}

public enum CopelandArtifactKind
{
    JavaScript,
    ManagedExecutable,
    NativeExecutable,
    WasmModule,
    WebBundle,
    CompilerMetadata,
}

public sealed record CopelandBackendTarget(
    CopelandBackend Backend,
    CopelandExecutionRuntime Runtime,
    string TargetFramework,
    string? RuntimeIdentifier)
{
    public static CopelandBackendTarget Create(
        string? backend,
        string? runtime,
        string? targetFramework = null,
        string? runtimeIdentifier = null)
    {
        string normalizedBackend = string.IsNullOrWhiteSpace(backend) ? "javascript" : backend.ToLowerInvariant();
        string normalizedRuntime = string.IsNullOrWhiteSpace(runtime)
            ? normalizedBackend == "javascript" ? "node" : "ryujit"
            : runtime.ToLowerInvariant();

        CopelandBackend parsedBackend = normalizedBackend switch
        {
            "javascript" => CopelandBackend.JavaScript,
            "csharp" => CopelandBackend.CSharp,
            _ => throw Invalid($"Unknown Copeland backend '{backend}'. Valid backends are javascript and csharp."),
        };
        CopelandExecutionRuntime parsedRuntime = normalizedRuntime switch
        {
            "node" or "v8" => CopelandExecutionRuntime.Node,
            "browser" => CopelandExecutionRuntime.Browser,
            "ryujit" or "clr" or "dotnet" => CopelandExecutionRuntime.RyuJit,
            "nativeaot" => CopelandExecutionRuntime.NativeAot,
            "wasm" or "dotnetwasm" => CopelandExecutionRuntime.DotNetWasm,
            _ => throw Invalid($"Unknown Copeland runtime '{runtime}'. Valid runtimes are node, browser, ryujit, nativeaot, and wasm."),
        };

        bool valid = parsedBackend switch
        {
            CopelandBackend.JavaScript => parsedRuntime is CopelandExecutionRuntime.Node or CopelandExecutionRuntime.Browser,
            CopelandBackend.CSharp => parsedRuntime is CopelandExecutionRuntime.RyuJit or CopelandExecutionRuntime.NativeAot or CopelandExecutionRuntime.DotNetWasm,
            _ => false,
        };
        if (!valid)
        {
            string validRuntimes = parsedBackend == CopelandBackend.JavaScript
                ? "node or browser"
                : "ryujit, nativeaot, or wasm";
            throw Invalid($"backend={normalizedBackend} cannot use runtime={normalizedRuntime}; valid runtimes for {normalizedBackend} are {validRuntimes}.");
        }
        if (parsedRuntime == CopelandExecutionRuntime.NativeAot && string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            throw Invalid("The NativeAOT target requires an explicit .NET runtime identifier such as win-x64, linux-x64, or osx-arm64.");
        }

        return new CopelandBackendTarget(
            parsedBackend,
            parsedRuntime,
            string.IsNullOrWhiteSpace(targetFramework) ? "net10.0" : targetFramework,
            string.IsNullOrWhiteSpace(runtimeIdentifier) ? null : runtimeIdentifier);
    }

    public string BackendId => Backend == CopelandBackend.JavaScript ? "javascript" : "csharp";

    public string RuntimeId => Runtime switch
    {
        CopelandExecutionRuntime.Node => "node",
        CopelandExecutionRuntime.Browser => "browser",
        CopelandExecutionRuntime.RyuJit => "ryujit",
        CopelandExecutionRuntime.NativeAot => "nativeaot",
        CopelandExecutionRuntime.DotNetWasm => "wasm",
        _ => throw new InvalidOperationException("Unknown Copeland runtime."),
    };

    public IReadOnlyList<CopelandArtifactKind> ArtifactKinds => Runtime switch
    {
        CopelandExecutionRuntime.Node or CopelandExecutionRuntime.Browser => [CopelandArtifactKind.JavaScript, CopelandArtifactKind.CompilerMetadata],
        CopelandExecutionRuntime.RyuJit => [CopelandArtifactKind.ManagedExecutable, CopelandArtifactKind.CompilerMetadata],
        CopelandExecutionRuntime.NativeAot => [CopelandArtifactKind.NativeExecutable, CopelandArtifactKind.CompilerMetadata],
        CopelandExecutionRuntime.DotNetWasm => [CopelandArtifactKind.WasmModule, CopelandArtifactKind.WebBundle, CopelandArtifactKind.CompilerMetadata],
        _ => throw new InvalidOperationException("Unknown Copeland runtime."),
    };

    public string PrimaryArtifactId => ArtifactKinds[0] switch
    {
        CopelandArtifactKind.JavaScript => "javaScript",
        CopelandArtifactKind.ManagedExecutable => "managedExecutable",
        CopelandArtifactKind.NativeExecutable => "nativeExecutable",
        CopelandArtifactKind.WasmModule => "wasmModule",
        CopelandArtifactKind.WebBundle => "webBundle",
        CopelandArtifactKind.CompilerMetadata => "compilerMetadata",
        _ => throw new InvalidOperationException("Unknown Copeland artifact kind."),
    };

    public IReadOnlyList<string> Capabilities => Runtime switch
    {
        CopelandExecutionRuntime.Node => ["emitJavaScript", "runNode"],
        CopelandExecutionRuntime.Browser => ["emitJavaScript", "browser"],
        CopelandExecutionRuntime.RyuJit => ["emitManaged", "runClr"],
        CopelandExecutionRuntime.NativeAot => ["emitNative", "nativeAot"],
        CopelandExecutionRuntime.DotNetWasm => ["emitWasm", "browser"],
        _ => throw new InvalidOperationException("Unknown Copeland runtime."),
    };

    private static CopelandBackendTargetException Invalid(string message)
        => new("COPE-TARGET-0001", message);
}

public sealed class CopelandBackendTargetException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
