namespace Aurelian.Shaders.Artifacts;

public sealed class ShaderArtifactOptions
{
    public required string OutputRoot { get; init; }
    public required string SourcePath { get; init; }
    public required string SourceText { get; init; }
    public required string EntryShader { get; init; }
    public IReadOnlyDictionary<string, bool> BoolSpecialization { get; init; } = new Dictionary<string, bool>(StringComparer.Ordinal);
    public bool StrictDxc { get; init; }
}
