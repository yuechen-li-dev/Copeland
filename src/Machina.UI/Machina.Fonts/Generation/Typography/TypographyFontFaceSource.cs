namespace Machina.Fonts.Generation.Typography;

public sealed record TypographyFontFaceSource
{
    public TypographyFontFaceSource(
        FontFaceId face,
        string path,
        int faceIndex = 0)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (faceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be greater than or equal to zero.");
        }

        string trimmedPath = path.Trim();
        if (trimmedPath.Length == 0)
        {
            throw new ArgumentException("Font path must not be empty.", nameof(path));
        }

        Face = face;
        Path = trimmedPath;
        FaceIndex = faceIndex;
    }

    public FontFaceId Face { get; }

    public string Path { get; }

    public int FaceIndex { get; }
}
