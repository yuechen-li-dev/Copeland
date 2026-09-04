using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Machina.Fonts.Generation;
using Machina.Core.Assets;
using Msdfgen;

namespace Machina.VectorAssets;

public sealed record VectorIconCompilationSettings
{
    public VectorIconCompilationSettings(
        int qualitySize = 64,
        double pixelRange = 4,
        string edgeColoring = "simple",
        int minimumShortAxis = 16)
    {
        if (qualitySize <= 0 || minimumShortAxis <= 0 || minimumShortAxis > qualitySize)
        {
            throw new ArgumentOutOfRangeException(nameof(qualitySize));
        }
        if (!double.IsFinite(pixelRange) || pixelRange <= 0 || qualitySize <= pixelRange * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelRange));
        }
        if (edgeColoring is not "simple" and not "inktrap")
        {
            throw new ArgumentException("Edge coloring must be 'simple' or 'inktrap'.", nameof(edgeColoring));
        }

        QualitySize = qualitySize;
        PixelRange = pixelRange;
        EdgeColoring = edgeColoring;
        MinimumShortAxis = minimumShortAxis;
    }

    public int QualitySize { get; }

    public double PixelRange { get; }

    public string EdgeColoring { get; }

    public int MinimumShortAxis { get; }
}

public sealed record VectorIconSourceProvenance(
    string SourceName,
    string SourceHash,
    string NormalizedGeometryHash,
    string Compiler,
    string Settings);

public sealed record VectorIconMsdfArtifact(
    MachinaVectorIconId Identity,
    VectorShape Shape,
    VectorBounds PlaneBounds,
    VectorBounds FieldBounds,
    int Width,
    int Height,
    double PixelRange,
    double ProjectionScale,
    ReadOnlyMemory<float> FieldPixels,
    string FieldHash,
    VectorIconSourceProvenance Provenance)
{
    public int ChannelCount => 3;
}

public sealed record VectorIconCompilationResult(
    VectorIconMsdfArtifact? Artifact,
    IReadOnlyList<VectorSourceDiagnostic> Diagnostics)
{
    public bool Success => Artifact is not null && Diagnostics.Count == 0;
}

public static class VectorIconMsdfCompiler
{
    private const double EdgeColoringAngleThreshold = 3d;
    private const ulong EdgeColoringSeed = 0;

    public static VectorIconCompilationResult CompileSvg(
        string source,
        string sourceName,
        VectorIconCompilationSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("Source name must not be empty.", nameof(sourceName));
        }

        settings ??= new VectorIconCompilationSettings();
        VectorSourceParseResult parsed = SvgVectorIconParser.Parse(source);
        if (!parsed.Success)
        {
            return new VectorIconCompilationResult(null, parsed.Diagnostics);
        }

        try
        {
            VectorIconMsdfArtifact artifact = Compile(parsed.Shape!, source, sourceName, settings);
            return new VectorIconCompilationResult(artifact, []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new VectorIconCompilationResult(
                null,
                [new VectorSourceDiagnostic("svg", null, $"MSDF compilation failed: {ex.Message}")]);
        }
    }

    public static VectorIconMsdfArtifact Compile(
        VectorShape shape,
        string source,
        string sourceName,
        VectorIconCompilationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);

        (int width, int height) = DetermineFieldSize(shape.Bounds, settings);
        double drawableWidth = width - (settings.PixelRange * 2);
        double drawableHeight = height - (settings.PixelRange * 2);
        double projectionScale = Math.Min(drawableWidth / shape.Bounds.Width, drawableHeight / shape.Bounds.Height);
        double translateX = ((width - (shape.Bounds.Width * projectionScale)) / 2d / projectionScale) - shape.Bounds.MinX;
        double translateY = ((height - (shape.Bounds.Height * projectionScale)) / 2d / projectionScale) - shape.Bounds.MinY;

        Shape nativeShape = ConvertShape(shape);
        nativeShape.Normalize();
        nativeShape.OrientContours();
        if (!nativeShape.Validate())
        {
            throw new InvalidOperationException("MSDF shape validation failed after exact-zero sanitization and orientation.");
        }

        if (settings.EdgeColoring == "simple")
        {
            EdgeColoring.EdgeColoringSimple(nativeShape, EdgeColoringAngleThreshold, EdgeColoringSeed);
        }
        else
        {
            EdgeColoring.EdgeColoringInkTrap(nativeShape, EdgeColoringAngleThreshold, EdgeColoringSeed);
        }

        Projection projection = new(
            new Vector2(projectionScale, projectionScale),
            new Vector2(translateX, translateY));
        Msdfgen.Range range = new(settings.PixelRange);
        Bitmap<float> bitmap = new(width, height, 3);
        MsdfGenerator.GenerateMSDF(
            bitmap,
            nativeShape,
            projection,
            range,
            new MSDFGeneratorConfig(false, ErrorCorrectionConfig.Default));

        float[] pixels = bitmap.Pixels;
        if (pixels.Any(static value => !float.IsFinite(value)))
        {
            pixels = GenerateMonochromeFallback(nativeShape, projection, range, width, height);
        }
        if (pixels.Any(static value => !float.IsFinite(value)))
        {
            throw new InvalidOperationException("Distance-field generation produced non-finite values after the qualified RGB SDF fallback.");
        }

        double fieldLeft = -translateX;
        double fieldBottom = -translateY;
        VectorBounds fieldBounds = new(
            fieldLeft,
            fieldBottom,
            fieldLeft + (width / projectionScale),
            fieldBottom + (height / projectionScale));
        string settingsText = FormattableString.Invariant(
            $"vector-msdf-v1;quality={settings.QualitySize};shortMin={settings.MinimumShortAxis};range={settings.PixelRange:R};edge={settings.EdgeColoring};orientation=TopToBottom;fill=NonZero");
        string identityHash = HashUtf8(shape.NormalizedGeometryHash + "|" + settingsText);
        string fieldHash = HashFloats(pixels);
        string sourceHash = HashUtf8(source);

        return new VectorIconMsdfArtifact(
            new MachinaVectorIconId("vector-icon-sha256-" + identityHash),
            shape,
            shape.Bounds,
            fieldBounds,
            width,
            height,
            settings.PixelRange,
            projectionScale,
            pixels,
            fieldHash,
            new VectorIconSourceProvenance(
                sourceName,
                sourceHash,
                shape.NormalizedGeometryHash,
                "Machina.VectorAssets/vector-msdf-v1/MSDF-Sharp",
                settingsText));
    }

    private static (int Width, int Height) DetermineFieldSize(VectorBounds bounds, VectorIconCompilationSettings settings)
    {
        double aspect = bounds.Width / bounds.Height;
        if (aspect >= 1)
        {
            int height = Math.Max(settings.MinimumShortAxis, (int)Math.Ceiling(settings.QualitySize / aspect));
            return (settings.QualitySize, Math.Min(settings.QualitySize, height));
        }

        int width = Math.Max(settings.MinimumShortAxis, (int)Math.Ceiling(settings.QualitySize * aspect));
        return (Math.Min(settings.QualitySize, width), settings.QualitySize);
    }

    private static Shape ConvertShape(VectorShape source)
    {
        Shape result = new();
        result.SetYAxisOrientation(YAxisOrientation.Upward);
        foreach (VectorContour sourceContour in source.Contours)
        {
            Contour contour = new();
            foreach (VectorSegment sourceSegment in sourceContour.Segments)
            {
                contour.AddEdge(sourceSegment switch
                {
                    VectorLine line => new LinearSegment(ToNative(line.P0), ToNative(line.P1), EdgeColor.WHITE),
                    VectorQuadratic quadratic => new QuadraticSegment(ToNative(quadratic.P0), ToNative(quadratic.P1), ToNative(quadratic.P2), EdgeColor.WHITE),
                    VectorCubic cubic => new CubicSegment(ToNative(cubic.P0), ToNative(cubic.P1), ToNative(cubic.P2), ToNative(cubic.P3), EdgeColor.WHITE),
                    _ => throw new InvalidOperationException($"Unsupported vector segment '{sourceSegment.GetType().Name}'."),
                });
            }
            result.AddContour(contour);
        }
        return result;
    }

    private static float[] GenerateMonochromeFallback(
        Shape shape,
        Projection projection,
        Msdfgen.Range range,
        int width,
        int height)
    {
        Bitmap<float> sdfBitmap = new(width, height, 1);
        MsdfGenerator.GenerateSDF(sdfBitmap, shape, projection, range, new GeneratorConfig(false));
        float[] rgb = new float[checked(width * height * 3)];
        for (int index = 0; index < sdfBitmap.Pixels.Length; index++)
        {
            rgb[index * 3] = sdfBitmap.Pixels[index];
            rgb[(index * 3) + 1] = sdfBitmap.Pixels[index];
            rgb[(index * 3) + 2] = sdfBitmap.Pixels[index];
        }
        return rgb;
    }

    private static Vector2 ToNative(VectorPoint point) => new(point.X, point.Y);

    private static string HashUtf8(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string HashFloats(float[] values)
    {
        byte[] bytes = new byte[checked(values.Length * sizeof(float))];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
