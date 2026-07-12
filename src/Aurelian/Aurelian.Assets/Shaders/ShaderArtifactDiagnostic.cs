namespace Aurelian.Assets.Shaders;

public sealed record ShaderArtifactDiagnostic(string Code, string Message, string? Path = null, string? Stage = null);
