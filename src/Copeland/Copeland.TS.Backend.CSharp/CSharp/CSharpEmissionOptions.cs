using Copeland.TS.Mir;

namespace Copeland.TS.Backend.CSharp;

/// <summary>Names and visibility are selected before source emission.</summary>
public sealed record CSharpEmissionOptions
{
    public string ModuleClassName { get; init; } = CSharpBackend.DefaultModuleClassName;
    public string Namespace { get; init; } = "Copeland.Generated";
    public string? RecordCarrierScope { get; init; }
    public IReadOnlySet<string>? PublicFunctionNames { get; init; }
    public IReadOnlyDictionary<string, NugetWorkerBinding>? NugetBindings { get; init; }
    public IReadOnlySet<MirRecordTypeId>? NugetDtoRecords { get; init; }
}
