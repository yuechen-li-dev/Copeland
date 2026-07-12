namespace Aurelian.Shaders.Language.Artifacts.Files;

public sealed record ShaderArtifactFileDiagnostic(string Code, string Message, string? Path = null);
