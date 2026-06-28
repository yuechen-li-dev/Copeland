namespace Machina.Fonts.Generation;

public sealed record GeneratedFieldAtlasPackOptions
{
    public GeneratedFieldAtlasPackOptions(
        int pageWidth,
        int pageHeight,
        int padding,
        string pageNamePrefix)
    {
        if (pageWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageWidth), "Page width must be greater than zero.");
        }

        if (pageHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageHeight), "Page height must be greater than zero.");
        }

        if (padding < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(padding), "Padding must be non-negative.");
        }

        if (string.IsNullOrWhiteSpace(pageNamePrefix))
        {
            throw new ArgumentException("Page name prefix must not be empty.", nameof(pageNamePrefix));
        }

        PageWidth = pageWidth;
        PageHeight = pageHeight;
        Padding = padding;
        PageNamePrefix = pageNamePrefix;
    }

    public int PageWidth { get; }

    public int PageHeight { get; }

    public int Padding { get; }

    public string PageNamePrefix { get; }
}
