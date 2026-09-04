using Msdfgen;

namespace Machina.Fonts.Generation.MsdfSharp;

public sealed class MsdfSharpDistanceFieldGenerator : IGlyphDistanceFieldGenerator
{
    private const double EdgeColoringAngleThreshold = 3d;
    private const ulong EdgeColoringSeed = 0;

    public GeneratedGlyphDistanceField Generate(
        GlyphOutline outline,
        MsdfGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(settings);

        cancellationToken.ThrowIfCancellationRequested();

        List<FontGenerationDiagnostic> diagnostics = [];
        int channelCount = FakeDistanceFieldValidation.GetChannelCount(settings.Kind);

        if (!TryCreateProjection(
                outline,
                settings,
                out Projection projection,
                out Msdfgen.Range range,
                out GlyphFieldPlacement? fieldPlacement,
                out FontGenerationDiagnostic? projectionDiagnostic))
        {
            diagnostics.Add(projectionDiagnostic!);
            return CreateResult(outline, settings, channelCount, diagnostics);
        }

        MsdfSharpShapeConversion conversion = MsdfSharpShapeConverter.Convert(outline);
        diagnostics.AddRange(conversion.Diagnostics);
        if (!conversion.Success || conversion.Shape is null)
        {
            return CreateResult(outline, settings, channelCount, diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();

        Shape shape = conversion.Shape;
        try
        {
            shape.Normalize();
            shape.OrientContours();

            if (!shape.Validate())
            {
                diagnostics.Add(CreateDiagnostic(
                    outline.Key,
                    FontGenerationDiagnosticCode.DistanceFieldGenerationFailed,
                    "MSDF shape validation failed after preprocessing."));

                return CreateResult(outline, settings, channelCount, diagnostics);
            }

            if (settings.Kind is DistanceFieldKind.Msdf or DistanceFieldKind.Mtsdf)
            {
                if (!TryApplyEdgeColoring(shape, outline.Key, settings.EdgeColoring, out FontGenerationDiagnostic? edgeColoringDiagnostic))
                {
                    diagnostics.Add(edgeColoringDiagnostic!);
                    return CreateResult(outline, settings, channelCount, diagnostics);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            Bitmap<float> bitmap = new(settings.Width, settings.Height, channelCount);
            DistanceMapping distanceMapping = new(range);
            SDFTransformation transformation = new(projection, distanceMapping);

            switch (settings.Kind)
            {
                case DistanceFieldKind.Sdf:
                    MsdfGenerator.GenerateSDF(bitmap, shape, transformation, new GeneratorConfig(false));
                    break;
                case DistanceFieldKind.Psdf:
                    MsdfGenerator.GeneratePSDF(bitmap, shape, transformation, new GeneratorConfig(false));
                    break;
                case DistanceFieldKind.Msdf:
                    MsdfGenerator.GenerateMSDF(bitmap, shape, projection, range, new MSDFGeneratorConfig(false, ErrorCorrectionConfig.Default));
                    break;
                case DistanceFieldKind.Mtsdf:
                    MsdfGenerator.GenerateMTSDF(bitmap, shape, transformation, new MSDFGeneratorConfig(false, ErrorCorrectionConfig.Default));
                    break;
                default:
                    diagnostics.Add(CreateDiagnostic(
                        outline.Key,
                        FontGenerationDiagnosticCode.InvalidGenerationSettings,
                        $"Distance-field kind '{settings.Kind}' is not supported by the MSDF-Sharp proof adapter."));

                    return CreateResult(outline, settings, channelCount, diagnostics);
            }

            cancellationToken.ThrowIfCancellationRequested();

            float[] pixels = bitmap.Pixels;
            if (pixels.Any(static value => !float.IsFinite(value)))
            {
                if (settings.Kind == DistanceFieldKind.Msdf)
                {
                    pixels = GenerateMonochromeMsdf(shape, projection, range, settings.Width, settings.Height);
                }

                if (pixels.Any(static value => !float.IsFinite(value)))
                {
                    int firstNonFinite = Array.FindIndex(pixels, static value => !float.IsFinite(value));
                    diagnostics.Add(CreateDiagnostic(
                        outline.Key,
                        FontGenerationDiagnosticCode.DistanceFieldGenerationFailed,
                        $"Distance-field generation produced a non-finite value at channel index {firstNonFinite}."));
                }
            }

            return new GeneratedGlyphDistanceField(
                outline.Key,
                outline.Metrics,
                settings.Width,
                settings.Height,
                settings.Kind,
                channelCount,
                pixels,
                fieldPlacement!,
                diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            diagnostics.Add(CreateDiagnostic(
                outline.Key,
                FontGenerationDiagnosticCode.DistanceFieldGenerationFailed,
                $"MSDF generation failed: {ex.Message}"));

            return CreateResult(outline, settings, channelCount, diagnostics);
        }
    }

    private static float[] GenerateMonochromeMsdf(
        Shape shape,
        Projection projection,
        Msdfgen.Range range,
        int width,
        int height)
    {
        Bitmap<float> sdfBitmap = new(width, height, 1);
        MsdfGenerator.GenerateSDF(sdfBitmap, shape, projection, range, new GeneratorConfig(false));

        float[] sdf = sdfBitmap.Pixels;
        float[] rgb = new float[checked(width * height * 3)];
        for (int pixelIndex = 0; pixelIndex < sdf.Length; pixelIndex++)
        {
            float distance = sdf[pixelIndex];
            int channelIndex = pixelIndex * 3;
            rgb[channelIndex] = distance;
            rgb[channelIndex + 1] = distance;
            rgb[channelIndex + 2] = distance;
        }

        return rgb;
    }

    private static bool TryApplyEdgeColoring(
        Shape shape,
        GlyphKey key,
        string edgeColoring,
        out FontGenerationDiagnostic? diagnostic)
    {
        diagnostic = null;

        if (edgeColoring.Equals("simple", StringComparison.OrdinalIgnoreCase))
        {
            EdgeColoring.EdgeColoringSimple(shape, EdgeColoringAngleThreshold, EdgeColoringSeed);
            return true;
        }

        if (edgeColoring.Equals("inktrap", StringComparison.OrdinalIgnoreCase))
        {
            EdgeColoring.EdgeColoringInkTrap(shape, EdgeColoringAngleThreshold, EdgeColoringSeed);
            return true;
        }

        diagnostic = CreateDiagnostic(
            key,
            FontGenerationDiagnosticCode.InvalidGenerationSettings,
            $"Unsupported edge coloring mode '{edgeColoring}'.");
        return false;
    }

    private static bool TryCreateProjection(
        GlyphOutline outline,
        MsdfGenerationSettings settings,
        out Projection projection,
        out Msdfgen.Range range,
        out GlyphFieldPlacement? fieldPlacement,
        out FontGenerationDiagnostic? diagnostic)
    {
        range = new Msdfgen.Range(settings.PixelRange);
        projection = new Projection();
        fieldPlacement = null;
        diagnostic = null;

        double drawableWidth = settings.Width - (settings.PixelRange * 2d);
        double drawableHeight = settings.Height - (settings.PixelRange * 2d);
        if (drawableWidth <= 0 || drawableHeight <= 0)
        {
            diagnostic = CreateDiagnostic(
                outline.Key,
                FontGenerationDiagnosticCode.InvalidGenerationSettings,
                "Width and height must leave drawable space after applying the configured pixel range.");
            return false;
        }

        double outlineWidth = Math.Max(outline.Bounds.MaxX - outline.Bounds.MinX, 0.0001d);
        double outlineHeight = Math.Max(outline.Bounds.MaxY - outline.Bounds.MinY, 0.0001d);
        double fitScale = Math.Min(drawableWidth / outlineWidth, drawableHeight / outlineHeight);
        double appliedScale = fitScale * settings.Scale;
        if (!double.IsFinite(appliedScale) || appliedScale <= 0)
        {
            diagnostic = CreateDiagnostic(
                outline.Key,
                FontGenerationDiagnosticCode.InvalidGenerationSettings,
                "Computed projection scale must be finite and greater than zero.");
            return false;
        }

        double pixelTranslateX = ((settings.Width - (outlineWidth * appliedScale)) / 2d) - (outline.Bounds.MinX * appliedScale);
        double pixelTranslateY = ((settings.Height - (outlineHeight * appliedScale)) / 2d) - (outline.Bounds.MinY * appliedScale);
        double shapeTranslateX = pixelTranslateX / appliedScale;
        double shapeTranslateY = pixelTranslateY / appliedScale;

        projection = new Projection(
            new Vector2(appliedScale, appliedScale),
            new Vector2(shapeTranslateX, shapeTranslateY));
        fieldPlacement = CreatePlacement(settings, appliedScale, shapeTranslateX, shapeTranslateY);

        return true;
    }

    private static GeneratedGlyphDistanceField CreateResult(
        GlyphOutline outline,
        MsdfGenerationSettings settings,
        int channelCount,
        IReadOnlyList<FontGenerationDiagnostic> diagnostics)
    {
        float[] data = new float[checked(settings.Width * settings.Height * channelCount)];
        return new GeneratedGlyphDistanceField(
            outline.Key,
            outline.Metrics,
            settings.Width,
            settings.Height,
            settings.Kind,
            channelCount,
            data,
            GlyphFieldPlacement.CreateFromMetricsBox(outline.Metrics, settings.PixelRange, Math.Max(settings.Scale, 0.0001d)),
            diagnostics);
    }

    private static GlyphFieldPlacement CreatePlacement(
        MsdfGenerationSettings settings,
        double projectionScale,
        double shapeTranslateX,
        double shapeTranslateY)
    {
        double glyphLeft = InverseProjectX(0d, projectionScale, shapeTranslateX);
        double glyphRight = InverseProjectX(settings.Width, projectionScale, shapeTranslateX);
        double glyphBottom = InverseProjectY(0d, projectionScale, shapeTranslateY);
        double glyphTop = InverseProjectY(settings.Height, projectionScale, shapeTranslateY);

        return new GlyphFieldPlacement(
            glyphLeft,
            -glyphTop,
            glyphRight,
            -glyphBottom,
            settings.PixelRange,
            projectionScale);
    }

    private static double InverseProjectX(double bitmapX, double projectionScale, double shapeTranslateX)
    {
        return (bitmapX / projectionScale) - shapeTranslateX;
    }

    private static double InverseProjectY(double bitmapY, double projectionScale, double shapeTranslateY)
    {
        return (bitmapY / projectionScale) - shapeTranslateY;
    }

    private static FontGenerationDiagnostic CreateDiagnostic(
        GlyphKey key,
        FontGenerationDiagnosticCode code,
        string message)
    {
        return new FontGenerationDiagnostic(
            FontGenerationDiagnosticSeverity.Error,
            code,
            message,
            key);
    }
}
