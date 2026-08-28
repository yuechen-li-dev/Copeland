using System.Text.Json;

namespace Copeland.TS.Compiler;

/// <summary>
/// Versioned, compiler-neutral transport emitted by TSPack. This is a protocol
/// type rather than a serialization of either repository's internal model.
/// </summary>
public sealed record CompilerTargetDescriptor
{
    public int SchemaVersion { get; init; }
    public string ProjectRoot { get; init; } = string.Empty;
    public CompilerTargetIdentity Target { get; init; } = new();
    public CompilerIdentity Language { get; init; } = new();
    public VersionedCompilerIdentity Compiler { get; init; } = new();
    public CompilerToolIdentity Tool { get; init; } = new();
    public CompilerConfigReference CompilerConfig { get; init; } = new();
    public List<CompilerTargetSource> Sources { get; init; } = [];
    public List<CompilerPackageBinding> Packages { get; init; } = [];
    public CompilerRuntimeIdentity Runtime { get; init; } = new();
    public List<CompilerTargetOutput> Outputs { get; init; } = [];
    public List<string> Capabilities { get; init; } = [];
    public CompilerTargetPayload? CompilerPayload { get; init; }
}

public sealed record CompilerTargetIdentity
{
    public string Package { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

public record CompilerIdentity
{
    public string Id { get; init; } = string.Empty;
}

public sealed record VersionedCompilerIdentity : CompilerIdentity
{
    public string Version { get; init; } = string.Empty;
}

public sealed record CompilerToolIdentity
{
    public string Source { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}

public sealed record CompilerConfigReference
{
    public string Kind { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
}

public sealed record CompilerTargetSource
{
    public string LogicalPath { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
}

public sealed record CompilerPackageBinding
{
    public string SemanticIdentity { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string MaterializationPath { get; init; } = string.Empty;
    public string MaterializationName { get; init; } = string.Empty;
    public string LocalName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public List<string> TypeSurfaces { get; init; } = [];
}

public sealed record CompilerRuntimeIdentity
{
    public string Family { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

public sealed record CompilerTargetOutput
{
    public string Kind { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}

public sealed record CompilerTargetPayload
{
    public string Kind { get; init; } = string.Empty;
    public int SchemaVersion { get; init; }
    public JsonElement Data { get; init; }
}

public static class CompilerTargetDescriptorProtocol
{
    public const int SupportedSchemaVersion = 1;

    public static CompilerTargetDescriptor Load(string descriptorPath)
    {
        using FileStream stream = File.OpenRead(Path.GetFullPath(descriptorPath));
        CompilerTargetDescriptor? descriptor = JsonSerializer.Deserialize<CompilerTargetDescriptor>(stream, JsonOptions);
        if (descriptor is null)
        {
            throw Invalid("Resolved compiler-target descriptor is empty.");
        }

        Validate(descriptor);
        return descriptor;
    }

    public static void Validate(CompilerTargetDescriptor descriptor)
    {
        if (descriptor.SchemaVersion != SupportedSchemaVersion)
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0009",
                $"Compiler-target descriptor schemaVersion {descriptor.SchemaVersion} is not supported; expected {SupportedSchemaVersion}.");
        }
        if (descriptor.Target is null
            || descriptor.Language is null
            || descriptor.Compiler is null
            || descriptor.Sources is null
            || string.IsNullOrWhiteSpace(descriptor.ProjectRoot)
            || string.IsNullOrWhiteSpace(descriptor.Target.Package)
            || string.IsNullOrWhiteSpace(descriptor.Target.Name)
            || string.IsNullOrWhiteSpace(descriptor.Language.Id)
            || string.IsNullOrWhiteSpace(descriptor.Compiler.Id)
            || string.IsNullOrWhiteSpace(descriptor.Compiler.Version)
            || descriptor.Sources.Count == 0)
        {
            throw Invalid("Compiler-target descriptor is missing required target, identity, or source fields.");
        }
        if (!string.Equals(descriptor.Language.Id, "copeland-ts", StringComparison.Ordinal)
            || !string.Equals(descriptor.Compiler.Id, "tscl", StringComparison.Ordinal))
        {
            throw Invalid($"Compiler-target descriptor selects language '{descriptor.Language.Id}' and compiler '{descriptor.Compiler.Id}', not Copeland TS/tscl.");
        }
        if (descriptor.CompilerPayload is null
            || !string.Equals(descriptor.CompilerPayload.Kind, "copeland-v1", StringComparison.Ordinal)
            || descriptor.CompilerPayload.SchemaVersion != 1
            || descriptor.CompilerPayload.Data.ValueKind != JsonValueKind.Object)
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0010",
                "Compiler-target descriptor requires compilerPayload kind 'copeland-v1' at schemaVersion 1 with an object payload.");
        }
        if (descriptor.CompilerConfig is null || string.IsNullOrWhiteSpace(descriptor.CompilerConfig.Path))
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0012",
                "Copeland compiler-target descriptor requires a compilerConfig path owned by Copeland.");
        }
    }

    private static CopelandProjectContextException Invalid(string message)
        => new("COPE-PROJECT-0004", message);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
