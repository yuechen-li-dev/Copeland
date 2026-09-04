using Copeland.Profile;

namespace Machina.VectorAssets;

public static class ProfileVectorIconCompiler
{
    public static VectorIconCompilationResult Compile(
        ProfileCompilationResult profile,
        string source,
        string sourceName,
        VectorIconCompilationSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(source);
        if (!profile.Success || profile.Shape is null)
        {
            string message = profile.Diagnostics.Count == 0
                ? "Profile did not produce canonical contours."
                : string.Join("; ", profile.Diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));
            return new VectorIconCompilationResult(
                null,
                [new VectorSourceDiagnostic("profile", null, message)]);
        }

        settings ??= new VectorIconCompilationSettings();
        try
        {
            VectorIconMsdfArtifact artifact = VectorIconMsdfCompiler.Compile(
                profile.Shape,
                source,
                sourceName,
                settings);
            return new VectorIconCompilationResult(artifact, []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new VectorIconCompilationResult(
                null,
                [new VectorSourceDiagnostic("profile", null, $"MSDF compilation failed: {ex.Message}")]);
        }
    }
}
