namespace Aurelian.Shaders.Language.External.Dxc;

public static class DxcCommandLineBuilder
{
    public static IReadOnlyList<string> Build(DxcValidationRequest request, string inputPath, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        return [
            "-T",
            request.Profile,
            "-E",
            request.EntryPoint,
            "-nHV",
            "2021",
            "-Ges",
            inputPath,
            "-Fo",
            outputPath,
        ];
    }
}
