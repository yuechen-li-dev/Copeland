using Machina.Fonts.Artifacts.DistanceField;
using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public static class DistanceFieldPageReferenceReader
{
    public static DistanceFieldPageReference Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!DistanceFieldPageArtifactReader.TryRead(path, out DistanceFieldPageArtifactDocument? document, out string? error))
        {
            throw new InvalidDataException(error ?? "DF page artifact could not be read.");
        }

        return FromArtifact(document!);
    }

    public static DistanceFieldPageReference FromArtifact(DistanceFieldPageArtifactDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!TryParseKind(document.DistanceField, out DistanceFieldKind kind))
        {
            throw new InvalidDataException($"Unsupported distance-field kind '{document.DistanceField}'.");
        }

        return new DistanceFieldPageReference(
            document.Path,
            kind,
            document.PageIndex,
            document.Width,
            document.Height,
            document.ChannelCount,
            document.Data);
    }

    public static bool TryParseKind(string text, out DistanceFieldKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        switch (text.Trim().ToLowerInvariant())
        {
            case "sdf":
                kind = DistanceFieldKind.Sdf;
                return true;
            case "psdf":
                kind = DistanceFieldKind.Psdf;
                return true;
            case "msdf":
                kind = DistanceFieldKind.Msdf;
                return true;
            case "mtsdf":
                kind = DistanceFieldKind.Mtsdf;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
