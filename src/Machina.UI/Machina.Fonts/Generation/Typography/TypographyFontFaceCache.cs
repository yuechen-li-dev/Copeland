using System.Collections.Concurrent;
using Typography.OpenFont;

namespace Machina.Fonts.Generation.Typography;

internal sealed class TypographyFontFaceCache
{
    private readonly IReadOnlyDictionary<FontFaceId, TypographyFontFaceSource> faceSources;
    private readonly ConcurrentDictionary<FontFaceId, Lazy<CachedTypeface>> cachedFaces = new();

    public TypographyFontFaceCache(IReadOnlyDictionary<FontFaceId, TypographyFontFaceSource> faceSources)
    {
        ArgumentNullException.ThrowIfNull(faceSources);

        if (faceSources.Count == 0)
        {
            throw new ArgumentException("At least one face source must be configured.", nameof(faceSources));
        }

        this.faceSources = new Dictionary<FontFaceId, TypographyFontFaceSource>(faceSources);
    }

    public bool TryGetSource(FontFaceId face, out TypographyFontFaceSource source)
    {
        return faceSources.TryGetValue(face, out source!);
    }

    public CachedTypeface GetOrLoad(FontFaceId face)
    {
        if (!faceSources.ContainsKey(face))
        {
            throw new KeyNotFoundException($"No Typography font face source is configured for '{face}'.");
        }

        Lazy<CachedTypeface> lazyFace = cachedFaces.GetOrAdd(
            face,
            static (key, state) => new Lazy<CachedTypeface>(
                () => LoadTypeface(state[key]),
                LazyThreadSafetyMode.ExecutionAndPublication),
            faceSources);

        return lazyFace.Value;
    }

    private static CachedTypeface LoadTypeface(TypographyFontFaceSource source)
    {
        if (source.FaceIndex != 0)
        {
            return CachedTypeface.Failed(source, "Typography proof adapter currently supports only face index 0.");
        }

        if (!File.Exists(source.Path))
        {
            return CachedTypeface.Failed(source, $"Font file '{source.Path}' was not found.");
        }

        try
        {
            OpenFontReader reader = new();
            using FileStream stream = File.OpenRead(source.Path);
            Typeface typeface = reader.Read(stream, 0, ReadFlags.Full);
            return CachedTypeface.Loaded(source, typeface);
        }
        catch (Exception ex)
        {
            return CachedTypeface.Failed(source, $"Failed to load font file '{source.Path}': {ex.Message}");
        }
    }

    internal sealed record CachedTypeface
    {
        private CachedTypeface(
            TypographyFontFaceSource source,
            Typeface? typeface,
            string? errorMessage)
        {
            Source = source;
            Typeface = typeface;
            ErrorMessage = errorMessage;
        }

        public TypographyFontFaceSource Source { get; }

        public Typeface? Typeface { get; }

        public string? ErrorMessage { get; }

        public bool Success => Typeface is not null && ErrorMessage is null;

        public static CachedTypeface Loaded(TypographyFontFaceSource source, Typeface typeface)
        {
            ArgumentNullException.ThrowIfNull(typeface);
            return new CachedTypeface(source, typeface, null);
        }

        public static CachedTypeface Failed(TypographyFontFaceSource source, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                throw new ArgumentException("Error message must not be empty.", nameof(errorMessage));
            }

            return new CachedTypeface(source, null, errorMessage);
        }
    }
}
