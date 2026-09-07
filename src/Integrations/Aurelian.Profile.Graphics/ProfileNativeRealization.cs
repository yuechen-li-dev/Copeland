using Aurelian.Graphics.Vulkan.Native2D;
using Copeland.Profile;
using Machina.Core.Assets;
using Machina.VectorAssets;

namespace Aurelian.Profile.Graphics;

public static class ProfileNativeDiagnosticCodes
{
    public const string EmptyComposition = "AURELIAN-PROFILE-NATIVE-0001";
    public const string UnsupportedPaint = "AURELIAN-PROFILE-NATIVE-0002";
    public const string OpenContour = "AURELIAN-PROFILE-NATIVE-0003";
    public const string InvalidGeometry = "AURELIAN-PROFILE-NATIVE-0004";
}

public sealed record ProfileNativeDiagnostic(
    string Id,
    string Message,
    string SourcePath,
    string? LayerId,
    string? ItemId);

public sealed record ProfileNativePaintSupport(
    string FillRule,
    IReadOnlyList<string> PaintModes,
    bool Alpha,
    bool Gradient,
    bool Stroke)
{
    public static ProfileNativePaintSupport M19FlatFill { get; } = new(
        "NonZero",
        ["FlatFill"],
        Alpha: true,
        Gradient: false,
        Stroke: false);
}

public sealed record ProfileNativeCompileResult(
    ProfileNativeCompositionResource? Resource,
    IReadOnlyList<ProfileNativeDiagnostic> Diagnostics)
{
    public bool Success => Resource is not null && Diagnostics.Count == 0;
}

public sealed record ProfileNativeDrawItem(
    int PainterIndex,
    string LayerId,
    string ItemId,
    MachinaVectorIconId FieldIdentity,
    VectorBounds FieldBounds,
    Native2DTint Fill,
    string GeometryHash,
    string SourcePath);

public sealed class ProfileNativeCompositionResource
{
    internal ProfileNativeCompositionResource(
        string compositionHash,
        string geometryHash,
        string sourcePath,
        VectorBounds bounds,
        VectorIconAtlas atlas,
        byte[] rgbaPixels,
        IReadOnlyList<ProfileNativeDrawItem> drawPlan)
    {
        CompositionHash = compositionHash;
        GeometryHash = geometryHash;
        SourcePath = sourcePath;
        Bounds = bounds;
        Atlas = atlas;
        RgbaPixels = rgbaPixels;
        DrawPlan = drawPlan;
    }

    public string CompositionHash { get; }

    public string GeometryHash { get; }

    public string SourcePath { get; }

    public VectorBounds Bounds { get; }

    public IReadOnlyList<ProfileNativeDrawItem> DrawPlan { get; }

    public int GeometryCount => Atlas.Entries.Count;

    public int AtlasWidth => Atlas.Width;

    public int AtlasHeight => Atlas.Height;

    public int MinimumFieldDimension => Atlas.Entries.Values.Min(static entry => Math.Min(entry.Width, entry.Height));

    public int MaximumFieldDimension => Atlas.Entries.Values.Max(static entry => Math.Max(entry.Width, entry.Height));

    internal VectorIconAtlas Atlas { get; }

    internal byte[] RgbaPixels { get; }
}

public readonly record struct ProfileNativeInstance(float OriginX, float OriginY, float Scale)
{
    public void Validate()
    {
        if (!float.IsFinite(OriginX) || !float.IsFinite(OriginY)
            || !float.IsFinite(Scale) || Scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Scale), "Profile instance transform must be finite with positive scale.");
        }
    }
}

public static class ProfileNativeCompiler
{
    public const int DefaultQualitySize = 192;

    public const int DefaultMinimumShortAxis = 40;

    public const double DefaultPixelRange = 12;

    public static ProfileNativeCompileResult Compile(
        ProfileComposition composition,
        string sourcePath,
        VectorIconCompilationSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(composition);
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A source path is required for native Profile diagnostics.", nameof(sourcePath));
        }

        settings ??= new VectorIconCompilationSettings(
            qualitySize: DefaultQualitySize,
            pixelRange: DefaultPixelRange,
            minimumShortAxis: DefaultMinimumShortAxis);
        var diagnostics = new List<ProfileNativeDiagnostic>();
        var artifacts = new Dictionary<MachinaVectorIconId, VectorIconMsdfArtifact>();
        var drawPlan = new List<ProfileNativeDrawItem>();
        int painterIndex = 0;

        foreach (ProfileLayer layer in composition.Layers)
        {
            foreach (ResolvedProfilePaintItem item in layer.Items)
            {
                ValidateItem(item, layer.Id.Name, sourcePath, diagnostics);
                if (diagnostics.Count > 0)
                {
                    continue;
                }

                VectorIconMsdfArtifact artifact;
                try
                {
                    artifact = VectorIconMsdfCompiler.Compile(
                        item.Shape,
                        item.ProfileIrHash,
                        sourcePath + "#" + item.Id,
                        settings);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    diagnostics.Add(new ProfileNativeDiagnostic(
                        ProfileNativeDiagnosticCodes.InvalidGeometry,
                        exception.Message,
                        sourcePath,
                        layer.Id.Name,
                        item.Id));
                    continue;
                }

                artifacts.TryAdd(artifact.Identity, artifact);
                drawPlan.Add(new ProfileNativeDrawItem(
                    painterIndex++,
                    layer.Id.Name,
                    item.Id,
                    artifact.Identity,
                    artifact.FieldBounds,
                    ParseFill(item.Style.Fill, sourcePath, layer.Id.Name, item.Id),
                    item.CanonicalContourHash,
                    sourcePath));
            }
        }

        if (drawPlan.Count == 0 && diagnostics.Count == 0)
        {
            diagnostics.Add(new ProfileNativeDiagnostic(
                ProfileNativeDiagnosticCodes.EmptyComposition,
                "Profile composition contains no paint geometry.",
                sourcePath,
                null,
                null));
        }
        if (diagnostics.Count > 0)
        {
            return new ProfileNativeCompileResult(null, diagnostics);
        }

        int atlasSize = DetermineAtlasSize(artifacts.Values);
        VectorIconAtlas atlas = VectorIconAtlasPacker.Pack(artifacts.Values.ToArray(), atlasSize, atlasSize);
        VectorBounds bounds = Union(composition.Layers.SelectMany(static layer => layer.Items).Select(static item => item.Shape.Bounds));
        return new ProfileNativeCompileResult(
            new ProfileNativeCompositionResource(
                composition.SemanticHash,
                composition.CanonicalGeometryHash,
                sourcePath,
                bounds,
                atlas,
                VectorIconAtlasPacker.ToRgba8(atlas),
                drawPlan),
            []);
    }

    private static void ValidateItem(
        ResolvedProfilePaintItem item,
        string layerId,
        string sourcePath,
        List<ProfileNativeDiagnostic> diagnostics)
    {
        if (item.Shape.FillRule != VectorFillRule.NonZero)
        {
            diagnostics.Add(new ProfileNativeDiagnostic(
                ProfileNativeDiagnosticCodes.UnsupportedPaint,
                $"Item '{item.Id}' uses unsupported fill rule '{item.Shape.FillRule}'.",
                sourcePath,
                layerId,
                item.Id));
        }
        foreach (VectorContour contour in item.Shape.Contours)
        {
            if (contour.Segments.Count == 0 || End(contour.Segments[^1]) != Start(contour.Segments[0]))
            {
                diagnostics.Add(new ProfileNativeDiagnostic(
                    ProfileNativeDiagnosticCodes.OpenContour,
                    $"Item '{item.Id}' contains an open or empty contour.",
                    sourcePath,
                    layerId,
                    item.Id));
                return;
            }
        }
        try
        {
            _ = ParseFill(item.Style.Fill, sourcePath, layerId, item.Id);
        }
        catch (ProfileNativePaintException exception)
        {
            diagnostics.Add(exception.Diagnostic);
        }
    }

    private static Native2DTint ParseFill(string fill, string sourcePath, string layerId, string itemId)
    {
        if (fill.Length is not 7 and not 9 || fill[0] != '#'
            || !uint.TryParse(fill.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out uint value))
        {
            throw new ProfileNativePaintException(new ProfileNativeDiagnostic(
                ProfileNativeDiagnosticCodes.UnsupportedPaint,
                $"Item '{itemId}' fill '{fill}' is outside M19 #RRGGBB/#RRGGBBAA flat-fill support.",
                sourcePath,
                layerId,
                itemId));
        }

        if (fill.Length == 7)
        {
            value = (value << 8) | 0xFF;
        }
        const float unit = 1f / 255f;
        return new Native2DTint(
            ((value >> 24) & 0xFF) * unit,
            ((value >> 16) & 0xFF) * unit,
            ((value >> 8) & 0xFF) * unit,
            (value & 0xFF) * unit);
    }

    private static int DetermineAtlasSize(IEnumerable<VectorIconMsdfArtifact> values)
    {
        VectorIconMsdfArtifact[] artifacts = values.ToArray();
        int columnCount = (int)Math.Ceiling(Math.Sqrt(artifacts.Length));
        int maximumWidth = artifacts.Max(static artifact => artifact.Width) + 2;
        int maximumHeight = artifacts.Max(static artifact => artifact.Height) + 2;
        int required = Math.Max(columnCount * maximumWidth, columnCount * maximumHeight);
        int atlasSize = 512;
        while (atlasSize < required && atlasSize < 4096)
        {
            atlasSize *= 2;
        }
        if (atlasSize >= required)
        {
            return atlasSize;
        }
        throw new InvalidOperationException("A single native Profile composition must fit within one bounded 4096x4096 atlas.");
    }

    private static VectorBounds Union(IEnumerable<VectorBounds> values)
    {
        VectorBounds[] bounds = values.ToArray();
        return new VectorBounds(
            bounds.Min(static item => item.MinX),
            bounds.Min(static item => item.MinY),
            bounds.Max(static item => item.MaxX),
            bounds.Max(static item => item.MaxY));
    }

    private static VectorPoint Start(VectorSegment segment)
    {
        return segment switch
        {
            VectorLine line => line.P0,
            VectorQuadratic quadratic => quadratic.P0,
            VectorCubic cubic => cubic.P0,
            _ => throw new InvalidOperationException("Unsupported Profile segment."),
        };
    }

    private static VectorPoint End(VectorSegment segment)
    {
        return segment switch
        {
            VectorLine line => line.P1,
            VectorQuadratic quadratic => quadratic.P2,
            VectorCubic cubic => cubic.P3,
            _ => throw new InvalidOperationException("Unsupported Profile segment."),
        };
    }

    private sealed class ProfileNativePaintException(ProfileNativeDiagnostic diagnostic) : Exception(diagnostic.Message)
    {
        public ProfileNativeDiagnostic Diagnostic { get; } = diagnostic;
    }
}

public sealed class ProfileNativeRealizationCache : IDisposable
{
    private readonly VulkanOrderedQuadRenderer renderer;
    private readonly Dictionary<string, CacheEntry> entries = new(StringComparer.Ordinal);
    private bool disposed;

    public ProfileNativeRealizationCache(VulkanOrderedQuadRenderer renderer, int capacity = 64)
    {
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        if (renderer.PipelineKind != Native2DPipelineKind.MsdfText)
        {
            throw new ArgumentException("Canonical Profile realization uses the qualified native MSDF pipeline.", nameof(renderer));
        }
        if (capacity is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count => entries.Count;

    public int UploadCount { get; private set; }

    public int CacheHits { get; private set; }

    public void Warm(ProfileNativeCompositionResource resource)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(resource);
        _ = Resolve(resource);
    }

    public void Submit(ProfileNativeCompositionResource resource, ProfileNativeInstance instance)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(resource);
        instance.Validate();
        CacheEntry entry = Resolve(resource);
        foreach (ProfileNativeDrawItem item in resource.DrawPlan)
        {
            VectorIconAtlasEntry field = resource.Atlas.Entries[item.FieldIdentity];
            Native2DUvRect uv = NormalizeUv(new Native2DUvRect(
                (float)field.U0,
                (float)field.V0,
                (float)field.U1,
                (float)field.V1));
            float x = instance.OriginX + ((float)item.FieldBounds.MinX * instance.Scale);
            float y = instance.OriginY - ((float)item.FieldBounds.MaxY * instance.Scale);
            renderer.SubmitMsdfQuad(new NativeMsdfQuadSubmission(
                new Native2DRect(
                    x,
                    y,
                    (float)item.FieldBounds.Width * instance.Scale,
                    (float)item.FieldBounds.Height * instance.Scale),
                uv,
                entry.Texture,
                item.Fill,
                NativeMsdfParameters.Create(
                    (float)field.PixelRange,
                    instance.Scale / (float)field.ProjectionScale)));
        }
    }

    public bool Invalidate(string compositionHash)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!entries.Remove(compositionHash, out CacheEntry? entry))
        {
            return false;
        }
        renderer.DisposeTexture(entry.Texture);
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        foreach (CacheEntry entry in entries.Values)
        {
            renderer.DisposeTexture(entry.Texture);
        }
        entries.Clear();
        disposed = true;
    }

    private CacheEntry Resolve(ProfileNativeCompositionResource resource)
    {
        if (entries.TryGetValue(resource.CompositionHash, out CacheEntry? cached))
        {
            if (!string.Equals(cached.AtlasHash, resource.Atlas.AtlasHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A Profile composition hash was reused with different realized content.");
            }
            CacheHits++;
            return cached;
        }
        if (entries.Count >= Capacity)
        {
            throw new InvalidOperationException($"Profile native cache reached its explicit capacity of {Capacity}; invalidate an asset before adding another.");
        }

        Native2DTextureHandle texture = renderer.CreateTexture(
            (uint)resource.Atlas.Width,
            (uint)resource.Atlas.Height,
            FlipRows(resource.RgbaPixels, resource.Atlas.Width, resource.Atlas.Height));
        var entry = new CacheEntry(resource.Atlas.AtlasHash, texture);
        entries.Add(resource.CompositionHash, entry);
        UploadCount++;
        return entry;
    }

    private static Native2DUvRect NormalizeUv(Native2DUvRect source)
    {
        return new Native2DUvRect(source.U0, 1f - source.V1, source.U1, 1f - source.V0);
    }

    private static byte[] FlipRows(byte[] source, int width, int height)
    {
        int rowBytes = checked(width * 4);
        byte[] result = new byte[source.Length];
        for (int sourceRow = 0; sourceRow < height; sourceRow++)
        {
            int destinationRow = height - sourceRow - 1;
            source.AsSpan(sourceRow * rowBytes, rowBytes)
                .CopyTo(result.AsSpan(destinationRow * rowBytes, rowBytes));
        }
        return result;
    }

    private sealed record CacheEntry(string AtlasHash, Native2DTextureHandle Texture);
}
