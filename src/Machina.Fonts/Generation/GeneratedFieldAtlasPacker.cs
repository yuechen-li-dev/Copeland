namespace Machina.Fonts.Generation;

public sealed class GeneratedFieldAtlasPacker
{
    public GeneratedFieldAtlasPackResult Pack(
        IReadOnlyList<GeneratedGlyphDistanceField> fields,
        GeneratedFieldAtlasPackOptions options)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(options);

        List<FontGenerationDiagnostic> diagnostics = [];
        List<GeneratedGlyphDistanceField> packableFields = [];

        DistanceFieldKind? kind = null;
        int? channelCount = null;

        foreach (GeneratedGlyphDistanceField field in fields)
        {
            if (field is null)
            {
                throw new ArgumentException("Fields must not contain null values.", nameof(fields));
            }

            if (IsMetricsOnly(field))
            {
                diagnostics.Add(new FontGenerationDiagnostic(
                    FontGenerationDiagnosticSeverity.Info,
                    FontGenerationDiagnosticCode.MetricsOnlyGlyphSkipped,
                    $"Glyph U+{field.Key.Codepoint:X4} is metrics-only and was not packed into atlas pages.",
                    field.Key));
                continue;
            }

            if (field.Diagnostics.Any(static diagnostic => diagnostic.Severity == FontGenerationDiagnosticSeverity.Error))
            {
                diagnostics.Add(new FontGenerationDiagnostic(
                    FontGenerationDiagnosticSeverity.Error,
                    FontGenerationDiagnosticCode.AtlasPackingFailed,
                    $"Glyph U+{field.Key.Codepoint:X4} could not be packed because its generated field contains error diagnostics.",
                    field.Key));
                continue;
            }

            if (kind is null)
            {
                kind = field.Kind;
                channelCount = field.ChannelCount;
            }
            else if (kind != field.Kind || channelCount != field.ChannelCount)
            {
                diagnostics.Add(new FontGenerationDiagnostic(
                    FontGenerationDiagnosticSeverity.Error,
                    FontGenerationDiagnosticCode.AtlasPackingFailed,
                    "All generated fields in one pack call must share the same distance-field kind and channel count.",
                    field.Key));
                continue;
            }

            packableFields.Add(field);
        }

        if (diagnostics.Any(static diagnostic => diagnostic.Severity == FontGenerationDiagnosticSeverity.Error))
        {
            return new GeneratedFieldAtlasPackResult(
                false,
                FontAtlasSnapshot.Empty,
                Array.Empty<GeneratedFieldAtlasPage>(),
                diagnostics);
        }

        if (packableFields.Count == 0)
        {
            return new GeneratedFieldAtlasPackResult(
                true,
                new FontAtlasSnapshot(1, Array.Empty<FontAtlasPage>(), new Dictionary<GlyphKey, GlyphAtlasEntry>()),
                Array.Empty<GeneratedFieldAtlasPage>(),
                diagnostics);
        }

        GeneratedGlyphDistanceField[] orderedFields = packableFields
            .OrderByDescending(static field => field.Height)
            .ThenByDescending(static field => field.Width)
            .ThenBy(static field => field.Key.Face.Value, StringComparer.Ordinal)
            .ThenBy(static field => field.Key.EmSize)
            .ThenBy(static field => field.Key.Weight)
            .ThenBy(static field => field.Key.Slant)
            .ThenBy(static field => field.Key.Codepoint)
            .ToArray();

        List<PageBuilder> pageBuilders = [];
        foreach (GeneratedGlyphDistanceField field in orderedFields)
        {
            if (!TryPlaceField(field, options, channelCount!.Value, pageBuilders, out GlyphAtlasEntry? entry, out PageBuilder? pageBuilder))
            {
                diagnostics.Add(new FontGenerationDiagnostic(
                    FontGenerationDiagnosticSeverity.Error,
                    FontGenerationDiagnosticCode.AtlasPackingFailed,
                    $"Glyph U+{field.Key.Codepoint:X4} is too large for an empty atlas page.",
                    field.Key));

                return new GeneratedFieldAtlasPackResult(
                    false,
                    FontAtlasSnapshot.Empty,
                    Array.Empty<GeneratedFieldAtlasPage>(),
                    diagnostics);
            }

            pageBuilder!.Entries.Add(entry!);
            CopyFieldData(field, pageBuilder.Data, options.PageWidth, channelCount.Value, entry!.X, entry.Y);
        }

        FontAtlasPage[] pages = pageBuilders
            .Select(builder => new FontAtlasPage(
                builder.Index,
                $"{options.PageNamePrefix}.page{builder.Index}.dfpage",
                builder.Width,
                builder.Height,
                null))
            .ToArray();

        Dictionary<GlyphKey, GlyphAtlasEntry> glyphs = pageBuilders
            .SelectMany(static page => page.Entries)
            .ToDictionary(static entry => entry.Key, static entry => entry);

        GeneratedFieldAtlasPage[] generatedPages = pageBuilders
            .Select(static builder => new GeneratedFieldAtlasPage(
                builder.Index,
                builder.Width,
                builder.Height,
                builder.ChannelCount,
                builder.Data,
                builder.Entries))
            .ToArray();

        FontAtlasSnapshot snapshot = new(1, pages, glyphs);
        return new GeneratedFieldAtlasPackResult(true, snapshot, generatedPages, diagnostics);
    }

    private static bool TryPlaceField(
        GeneratedGlyphDistanceField field,
        GeneratedFieldAtlasPackOptions options,
        int channelCount,
        List<PageBuilder> pageBuilders,
        out GlyphAtlasEntry? entry,
        out PageBuilder? pageBuilder)
    {
        entry = null;
        pageBuilder = null;

        int strideWidth = checked(field.Width + options.Padding);
        int strideHeight = checked(field.Height + options.Padding);
        if (field.Width > options.PageWidth || field.Height > options.PageHeight)
        {
            return false;
        }

        if (strideWidth > options.PageWidth || strideHeight > options.PageHeight)
        {
            return false;
        }

        pageBuilder = pageBuilders.LastOrDefault();
        if (pageBuilder is null)
        {
            pageBuilder = new PageBuilder(0, options.PageWidth, options.PageHeight, channelCount);
            pageBuilders.Add(pageBuilder);
        }

        while (!pageBuilder.TryPlace(field, options.Padding, out entry))
        {
            if (pageBuilder.IsEmpty)
            {
                return false;
            }

            pageBuilder = new PageBuilder(pageBuilders.Count, options.PageWidth, options.PageHeight, channelCount);
            pageBuilders.Add(pageBuilder);
        }

        return true;
    }

    private static void CopyFieldData(
        GeneratedGlyphDistanceField field,
        float[] pageData,
        int pageWidth,
        int channelCount,
        int destinationX,
        int destinationY)
    {
        ReadOnlySpan<float> source = field.Data.Span;
        for (int y = 0; y < field.Height; y++)
        {
            int sourceOffset = checked(y * field.Width * channelCount);
            int destinationOffset = checked(((destinationY + y) * pageWidth + destinationX) * channelCount);
            source.Slice(sourceOffset, field.Width * channelCount).CopyTo(pageData.AsSpan(destinationOffset, field.Width * channelCount));
        }
    }

    private static bool IsMetricsOnly(GeneratedGlyphDistanceField field)
    {
        return field.Diagnostics.Any(static diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.EmptyOutline);
    }

    private sealed class PageBuilder
    {
        private int cursorX;
        private int cursorY;
        private int shelfHeight;

        public PageBuilder(int index, int width, int height, int channelCount)
        {
            Index = index;
            Width = width;
            Height = height;
            ChannelCount = channelCount;
            Data = new float[checked(width * height * channelCount)];
            Entries = [];
        }

        public int Index { get; }

        public int Width { get; }

        public int Height { get; }

        public int ChannelCount { get; }

        public float[] Data { get; }

        public List<GlyphAtlasEntry> Entries { get; }

        public bool IsEmpty => Entries.Count == 0;

        public bool TryPlace(GeneratedGlyphDistanceField field, int padding, out GlyphAtlasEntry? entry)
        {
            entry = null;

            int strideWidth = checked(field.Width + padding);
            int strideHeight = checked(field.Height + padding);

            if (cursorX + strideWidth > Width)
            {
                cursorX = 0;
                cursorY += shelfHeight;
                shelfHeight = 0;
            }

            if (cursorY + strideHeight > Height)
            {
                return false;
            }

            int x = cursorX;
            int y = cursorY;
            cursorX += strideWidth;
            shelfHeight = Math.Max(shelfHeight, strideHeight);

            entry = new GlyphAtlasEntry(
                field.Key,
                Index,
                x,
                y,
                field.Width,
                field.Height,
                (double)x / Width,
                (double)y / Height,
                (double)(x + field.Width) / Width,
                (double)(y + field.Height) / Height,
                field.Metrics,
                field.Placement);

            return true;
        }
    }
}
