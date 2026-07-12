namespace Machina.Fonts.Generation;

public sealed class FakeAtlasPacker
{
    private readonly List<FontAtlasPage> pages = [];
    private int cursorX;
    private int cursorY;
    private int rowHeight;

    public FakeAtlasPacker(int pageWidth = 256, int pageHeight = 256)
    {
        if (pageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pageWidth));
        if (pageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pageHeight));

        PageWidth = pageWidth;
        PageHeight = pageHeight;
    }

    public int PageWidth { get; }

    public int PageHeight { get; }

    public IReadOnlyList<FontAtlasPage> Pages => pages.ToArray();

    public FakeAtlasPackResult Pack(FakeGlyphGenerationResult generated)
    {
        if (generated.IsMissing)
        {
            return FakeAtlasPackResult.Missing(generated.Key, generated.MissingReason ?? "Glyph is missing.");
        }

        if (generated.Metrics is null)
        {
            return FakeAtlasPackResult.Missing(generated.Key, "Generated glyph did not include metrics.");
        }

        if (generated.Width > PageWidth || generated.Height > PageHeight)
        {
            return FakeAtlasPackResult.Missing(generated.Key, "Glyph is too large for the fake atlas page.");
        }

        EnsurePage();

        if (cursorX + generated.Width > PageWidth)
        {
            cursorX = 0;
            cursorY += rowHeight;
            rowHeight = 0;
        }

        if (cursorY + generated.Height > PageHeight)
        {
            AddPage();
        }

        FontAtlasPage page = pages[^1];
        int x = cursorX;
        int y = cursorY;
        cursorX += generated.Width;
        rowHeight = Math.Max(rowHeight, generated.Height);

        GlyphAtlasEntry entry = new(
            generated.Key,
            page.Index,
            x,
            y,
            generated.Width,
            generated.Height,
            (double)x / PageWidth,
            (double)y / PageHeight,
            (double)(x + generated.Width) / PageWidth,
            (double)(y + generated.Height) / PageHeight,
            generated.Metrics,
            GlyphFieldPlacement.CreateFromMetricsBox(generated.Metrics));

        return FakeAtlasPackResult.Packed(entry);
    }

    private void EnsurePage()
    {
        if (pages.Count == 0)
        {
            AddPage();
        }
    }

    private void AddPage()
    {
        int index = pages.Count;
        string hash = $"fake-{index}-{PageWidth}x{PageHeight}";
        pages.Add(new FontAtlasPage(index, $"fake.page{index}.png", PageWidth, PageHeight, hash));
        cursorX = 0;
        cursorY = 0;
        rowHeight = 0;
    }
}

public sealed record FakeAtlasPackResult(GlyphAtlasEntry? Entry, GlyphKey Key, string? MissingReason)
{
    public bool IsMissing => MissingReason is not null;

    public static FakeAtlasPackResult Packed(GlyphAtlasEntry entry)
    {
        return new FakeAtlasPackResult(entry, entry.Key, null);
    }

    public static FakeAtlasPackResult Missing(GlyphKey key, string reason)
    {
        return new FakeAtlasPackResult(null, key, reason);
    }
}
