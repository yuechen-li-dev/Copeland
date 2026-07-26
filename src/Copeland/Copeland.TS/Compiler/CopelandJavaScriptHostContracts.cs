namespace Copeland.TS.Compiler;

/// <summary>
/// A deliberately small, compiler-configured JavaScript host boundary. Unlike
/// npm contracts, host calls are synchronous direct ESM calls and may accept
/// Copeland callable values. This is a discovery seam for browser hosts, not a
/// general dynamic-JavaScript escape hatch.
/// </summary>
public sealed record CopelandJavaScriptHostModuleContract(
    string ModuleSpecifier,
    IReadOnlyList<CopelandJavaScriptHostFunctionContract> Exports,
    CopelandJavaScriptHostEnvironment Environment = CopelandJavaScriptHostEnvironment.Browser);

public sealed record CopelandJavaScriptHostFunctionContract(
    string ExportName,
    IReadOnlyList<CopelandJavaScriptHostType> ParameterTypes,
    CopelandJavaScriptHostType ResultType);

public enum CopelandJavaScriptHostEnvironment
{
    Browser,
}

public abstract record CopelandJavaScriptHostType
{
    private CopelandJavaScriptHostType()
    {
    }

    public sealed record Primitive(string Name) : CopelandJavaScriptHostType;

    public sealed record Callable(
        IReadOnlyList<CopelandJavaScriptHostType> Parameters,
        CopelandJavaScriptHostType ReturnType) : CopelandJavaScriptHostType;

    public static Primitive Int { get; } = new("int");
    public static Primitive String { get; } = new("string");
    public static Primitive Void { get; } = new("void");
}

internal sealed class CopelandJavaScriptHostContractResolver(IEnumerable<CopelandJavaScriptHostModuleContract> modules)
{
    private readonly Dictionary<string, CopelandJavaScriptHostModuleContract> _modules = modules
        .ToDictionary(module => module.ModuleSpecifier, StringComparer.Ordinal);

    public bool TryGetModule(string specifier, out CopelandJavaScriptHostModuleContract? module)
        => _modules.TryGetValue(specifier, out module);
}
