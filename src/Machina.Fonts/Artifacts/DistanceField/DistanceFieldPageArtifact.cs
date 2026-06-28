namespace Machina.Fonts.Artifacts.DistanceField;

public sealed record DistanceFieldPageArtifact(
    string Path,
    string ContentHash,
    int ByteCount,
    int PageIndex,
    int Width,
    int Height,
    int ChannelCount,
    string DistanceField,
    IReadOnlyList<int> GlyphCodepoints);
