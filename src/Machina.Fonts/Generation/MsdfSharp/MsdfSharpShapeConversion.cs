using Msdfgen;

namespace Machina.Fonts.Generation.MsdfSharp;

internal sealed record MsdfSharpShapeConversion
{
    public MsdfSharpShapeConversion(
        bool success,
        Shape? shape,
        IReadOnlyList<FontGenerationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (diagnostics.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException("Diagnostics must not contain null entries.", nameof(diagnostics));
        }

        Success = success;
        Shape = shape;
        Diagnostics = [.. diagnostics];
    }

    public bool Success { get; }

    public Shape? Shape { get; }

    public IReadOnlyList<FontGenerationDiagnostic> Diagnostics { get; }
}
