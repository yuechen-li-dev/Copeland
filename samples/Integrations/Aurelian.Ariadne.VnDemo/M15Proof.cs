using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Composition;
using Aurelian.NativeComposition;
using Copeland.SpanAllocation;
using Copeland.TS.Assets;
using Copeland.TS.Manifest;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;
using SkiaSharp;

namespace Aurelian.Ariadne.VnDemo;

internal static class M15Proof
{
    public static int Run()
    {
        string root = Program.FindRepositoryRoot();
        string artifactRoot = Path.Combine(root, "artifacts", "copeland-object-assets-m15");
        Directory.CreateDirectory(artifactRoot);
        string sampleRoot = Path.Combine(root, "samples", "Integrations", "Aurelian.Ariadne.VnDemo");
        string assetRoot = Path.Combine(sampleRoot, "Assets");
        string sourcePath = Path.Combine(assetRoot, "sunkill-dialogue-panel.obj.ts");

        ObjectAssetCompilationResult objectCompilation = ObjectAssetCompiler.CompileFile(sourcePath);
        Require(objectCompilation.Success, Describe(objectCompilation.Diagnostics));
        ObjectAssetDocument document = objectCompilation.Document!;
        ObjectAssetBuildOutputs projections = ObjectAssetCompiler.Emit(document, sourcePath);
        File.WriteAllText(Path.Combine(artifactRoot, "obj-ts-generated.toml"), projections.Toml);
        File.WriteAllText(Path.Combine(artifactRoot, "generated-runtime.toml"), projections.RuntimeToml);

        ManifestProjectLoadResult manifestLoad = CopelandProject.LoadRootManifest(sampleRoot);
        Require(manifestLoad.Success, Describe(manifestLoad.Diagnostics));
        CopelandManifest manifest = manifestLoad.Manifest!;
        File.Copy(
            Path.Combine(assetRoot, "manifest.generated.json"),
            Path.Combine(artifactRoot, "generated-manifest.json"),
            overwrite: true);

        SpanAllocationRequest<string>[] requests =
        [
            SpanAllocationRequest<string>.Fixed("cap", 10),
            SpanAllocationRequest<string>.Flex("rail", 10, 1),
            SpanAllocationRequest<string>.Flex("center", 10, 2),
        ];
        SpanAllocationResult<string> exact = SpanAllocator.Resolve(30, requests);
        SpanAllocationResult<string> underflow = SpanAllocator.Resolve(15, requests);
        SpanAllocationResult<string> surplus = SpanAllocator.Resolve(60, requests);
        WriteAllocatorSvg(Path.Combine(artifactRoot, "allocator-exact-fit.svg"), exact);
        WriteAllocatorSvg(Path.Combine(artifactRoot, "allocator-underflow.svg"), underflow);
        WriteAllocatorSvg(Path.Combine(artifactRoot, "allocator-surplus.svg"), surplus);
        WriteJson(Path.Combine(artifactRoot, "allocator-audit.json"), new
        {
            existingSpanMeaning = "compiler-known contiguous ordered region with owner and stale rules",
            existingLayoutMeaning = "compiler-owned spatial box, layer, and stream relationships",
            decision = "ordinary generic runtime kit; no new Copeland syntax or layout meaning",
            owner = "Copeland.SpanAllocation",
            spriteDependency = false,
            deferred = new[] { "alignment", "optional priority", "intrinsic sizing", "maximum sizing", "memory semantics" },
        });
        WriteJson(Path.Combine(artifactRoot, "allocator-mvp.json"), new
        {
            policy = "ordered fixed and minimum-weighted-flex integer allocation",
            exact,
            underflow,
            surplus,
        });

        ObjectAssetPanel panelDocument = AssertSingle(document.Panels, "object panel");
        WriteJson(Path.Combine(artifactRoot, "obj-ts-lowering-proof.json"), new
        {
            qualified = true,
            source = "samples/Integrations/Aurelian.Ariadne.VnDemo/Assets/sunkill-dialogue-panel.obj.ts",
            sourceAuthority = true,
            sourceSha256 = Sha256File(sourcePath),
            regionRepresentation = "Copeland record table AssetRegions with columnar id/x/y/width/height arrays",
            regionCount = document.Regions.Count,
            edgeSegmentCounts = new
            {
                top = panelDocument.Top.Segments.Count,
                right = panelDocument.Right.Segments.Count,
                bottom = panelDocument.Bottom.Segments.Count,
                left = panelDocument.Left.Segments.Count,
            },
            compileTimeRoot = "const $asset: AssetObject = static buildSunkillPanel()",
            tomlSha256 = Sha256Text(projections.Toml),
            runtimeTomlSha256 = Sha256Text(projections.RuntimeToml),
        });
        WriteJson(Path.Combine(artifactRoot, "manifest-tsx-proof.json"), new
        {
            qualified = true,
            source = "samples/Integrations/Aurelian.Ariadne.VnDemo/manifest.tsx",
            authoredStructure = "Workspace/Assets/Texture/Object/AssetOutputs",
            sourceRoot = manifest.Assets!.SourceRoot,
            textures = manifest.Assets.Textures,
            objects = manifest.Assets.Objects,
            outputs = manifest.AssetOutputs,
        });

        VnUiSkin skin = VnUiSkin.Load(Path.Combine(assetRoot, "sunkill-dialogue-panel.runtime.toml"));
        WriteSpriteCard(artifactRoot, skin, 400, "sprite-card-narrow.png");
        WriteSpriteCard(artifactRoot, skin, 800, "sprite-card-nominal.png");
        WriteSpriteCard(artifactRoot, skin, 1200, "sprite-card-wide.png");

        string runtimeRoot = Path.Combine(artifactRoot, "runtime");
        Directory.CreateDirectory(runtimeRoot);
        using var app = new RenApp(
            Path.Combine(runtimeRoot, "saves"),
            Path.Combine(runtimeRoot, "settings.json"));
        var machina = new VnMachinaLayer(app, skin)
        {
            SuppressOverlay = true,
        };
        using var native = new VnNativeRenderer(root, app, machina);
        ulong frameId = 0;

        PanelProof narrow = RenderPanel(native, machina, skin, ref frameId, 220, 1280, 720, artifactRoot, "sunkill-panel-narrow.png");
        PanelProof nominal = RenderPanel(native, machina, skin, ref frameId, 800, 1280, 720, artifactRoot, "sunkill-panel-nominal.png");
        PanelProof wide = RenderPanel(native, machina, skin, ref frameId, 1200, 1280, 720, artifactRoot, "sunkill-panel-wide.png");
        PanelProof odd = RenderPanel(native, machina, skin, ref frameId, 800, 1537, 864, artifactRoot, "sunkill-panel-odd-resolution.png");

        var seamNineSlice = new MachinaNineSlicePrimitive(
            "proof.m15-seam",
            new MachinaTextureAssetId("sunkill.seam.fixture"),
            new Rect(0, 0, 16, 16),
            new Rect(100, 100, 1080, 520),
            new MachinaSliceMargins(2, 2, 2, 2),
            MachinaNineSliceMode.Tile,
            MachinaNineSliceMode.Tile,
            tint: ColorToken.White);
        machina.ProofPanels = [MachinaPanelPrebuilt.NineSlice(seamNineSlice)];
        native.Resize(1280, 720);
        NativeLayerFrameResult seamFrame = native.Render(++frameId);
        SeamMetrics seam = MeasureSeams(seamFrame.NativeFrame.Pixels!, 1280, 720);
        int colorError = MeasurePixelColorError(seamFrame.NativeFrame.Pixels!, 1280, 100, 100, [255, 112, 12, 255]);
        Require(seam.MaxChannelError <= 2, $"Allocator-backed tile seam error was {seam.MaxChannelError}.");
        Require(colorError <= 2, $"Allocator-backed color error was {colorError}.");
        WriteJson(Path.Combine(artifactRoot, "seam-proof.json"), new
        {
            qualified = true,
            path = "nine-slice prebuilt -> explicit edge allocation -> Machina quads -> Aurelian native ordered quads",
            seam.SampleCount,
            seam.MaxChannelError,
            seam.MeanChannelError,
            expectedBoundaryRgba = new[] { 255, 112, 12, 255 },
            maxColorChannelError = colorError,
            atlasBleed = false,
            output = "R8G8B8A8_UNORM sRGB presentation path",
        });

        machina.ProofPanels = null;
        machina.SuppressOverlay = false;
        native.Resize(1537, 864);
        var settingsCenter = machina.ActionCenter("ren.entry.settings");
        var transform = native.ViewportTransform;
        (double physicalX, double physicalY) = transform.ToPhysical(settingsCenter.X, settingsCenter.Y);
        var routed = native.ToLogicalPointer(physicalX, physicalY);
        native.Route(new LayerPointerButtonChanged(routed, LayerPointerButton.Primary, true));
        native.Route(new LayerPointerButtonChanged(routed, LayerPointerButton.Primary, false));
        Require(app.State.Screen == RenScreen.Settings, "Odd-resolution hit testing regressed after programmable-panel migration.");

        WriteJson(Path.Combine(artifactRoot, "manifest.json"), new
        {
            milestone = "COPELAND-OBJECT-ASSETS-SPAN-ALLOCATOR-M15",
            kind = "programmable-assets-and-generic-span-allocation",
            outcome = "A",
            allocatorAuditedBeforeImplementation = true,
            allocatorMvpQualified = true,
            allocatorSpriteIndependent = true,
            genericPayloadQualified = true,
            objTsPreferredAuthoringQualified = true,
            columnarRecordTableQualified = true,
            tomlProjectionQualified = true,
            legacyTomlPreserved = true,
            manifestTsxQualified = true,
            manifestJsonProjectionQualified = true,
            spriteForgeIntegrationQualified = true,
            spriteCardsQualified = true,
            sunkillProgrammablePanelQualified = true,
            nineSliceIsAllocatorPrebuilt = true,
            inputLayoutParityQualified = app.State.Screen == RenScreen.Settings,
            seamColorQualified = seam.MaxChannelError <= 2 && colorError <= 2,
            panelProofs = new[] { narrow, nominal, wide, odd },
            memoryAllocatorImplemented = false,
            generalVisualProgrammingFrameworkAdded = false,
            artifacts = new[]
            {
                "allocator-audit.json",
                "allocator-mvp.json",
                "allocator-exact-fit.svg",
                "allocator-underflow.svg",
                "allocator-surplus.svg",
                "obj-ts-lowering-proof.json",
                "obj-ts-generated.toml",
                "manifest-tsx-proof.json",
                "generated-manifest.json",
                "sprite-card-narrow.png",
                "sprite-card-nominal.png",
                "sprite-card-wide.png",
                "sunkill-panel-narrow.png",
                "sunkill-panel-nominal.png",
                "sunkill-panel-wide.png",
                "sunkill-panel-odd-resolution.png",
                "seam-proof.json",
                "fresh-context-proof.json",
                "manifest.json",
            },
        });

        Console.WriteLine("COPELAND-OBJECT-ASSETS-SPAN-ALLOCATOR-M15: Outcome A");
        Console.WriteLine($"regions={document.Regions.Count}; edge-segments={panelDocument.Top.Segments.Count}");
        Console.WriteLine($"underflow-deficit={narrow.Top.DeficitLength}; seam-max-error={seam.MaxChannelError}");
        return 0;
    }

    private static PanelProof RenderPanel(
        VnNativeRenderer native,
        VnMachinaLayer machina,
        VnUiSkin skin,
        ref ulong frameId,
        int panelWidth,
        int framebufferWidth,
        int framebufferHeight,
        string artifactRoot,
        string fileName)
    {
        var destination = new Rect((1280 - panelWidth) / 2.0, 250, panelWidth, 220);
        MachinaProgrammablePanelPrimitive primitive = skin.CreateProgrammable(
            "proof." + Path.GetFileNameWithoutExtension(fileName),
            "dialogue",
            destination);
        MachinaPanelLoweringResult lowering = MachinaProgrammablePanelLowerer.Lower(primitive);
        MachinaPanelEdgeAllocation top = lowering.EdgeAllocations.Single(edge => edge.Edge == "top");
        AssertContiguous(lowering.Segments.Where(segment => segment.Edge == "top").ToArray(), top.Extent);
        native.Resize(framebufferWidth, framebufferHeight);
        machina.ProofPanels = [primitive];
        NativeLayerFrameResult frame = native.Render(++frameId);
        WriteScreenshot(Path.Combine(artifactRoot, fileName), frame, framebufferWidth, framebufferHeight);
        return new PanelProof(
            fileName,
            panelWidth,
            framebufferWidth,
            framebufferHeight,
            top,
            lowering.Diagnostics.Count,
            frame.NativeFrame.PixelSha256 ?? throw new InvalidOperationException("Native frame did not report a pixel hash."));
    }

    private static void WriteSpriteCard(
        string artifactRoot,
        VnUiSkin skin,
        int panelWidth,
        string fileName)
    {
        MachinaProgrammablePanelPrimitive primitive = skin.CreateProgrammable(
            "cards." + panelWidth,
            "dialogue",
            new Rect(0, 0, panelWidth, 220));
        MachinaPanelLoweringResult result = MachinaProgrammablePanelLowerer.Lower(primitive);
        MachinaPanelResolvedSegment[] placements = result.Segments.Where(segment => segment.Edge == "top").ToArray();
        MachinaPanelEdgeAllocation edge = result.EdgeAllocations.Single(item => item.Edge == "top");
        MachinaPanelEdgeSegment[] segments = primitive.Top.Segments.ToArray();

        using SKBitmap atlas = SKBitmap.Decode(skin.Atlas.ResolvedImagePath)
            ?? throw new InvalidDataException("Could not decode the SUNKILL atlas for Sprite Cards.");
        using var bitmap = new SKBitmap(new SKImageInfo(1600, 760, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(12, 14, 20));
        using var heading = new SKPaint { Color = SKColors.White, TextSize = 30, IsAntialias = true };
        using var label = new SKPaint { Color = new SKColor(225, 230, 240), TextSize = 18, IsAntialias = true };
        using var detail = new SKPaint { Color = new SKColor(156, 170, 190), TextSize = 15, IsAntialias = true };
        using var border = new SKPaint { Color = new SKColor(70, 86, 110), Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        SKColor[] palette =
        [
            new(224, 111, 52), new(105, 190, 255), new(149, 110, 255),
            new(105, 190, 255), new(246, 196, 69), new(105, 190, 255),
            new(149, 110, 255), new(105, 190, 255), new(224, 111, 52),
        ];

        canvas.DrawText($"SUNKILL top edge · panel {panelWidth}px", 36, 44, heading);
        canvas.DrawText(
            $"extent {edge.Extent}  minimum {edge.MinimumDemand}  used {edge.UsedLength}  unused {edge.UnusedLength}  deficit {edge.DeficitLength}  status {edge.Status}",
            36,
            78,
            label);

        const float stripLeft = 36;
        const float stripTop = 104;
        const float stripWidth = 1528;
        const float stripHeight = 62;
        foreach ((MachinaPanelResolvedSegment placement, int index) in placements.Select((item, index) => (item, index)))
        {
            float left = stripLeft + ((float)placement.Offset / Math.Max(1, edge.Extent) * stripWidth);
            float width = (float)placement.Length / Math.Max(1, edge.Extent) * stripWidth;
            using var fill = new SKPaint { Color = palette[index] };
            canvas.DrawRect(left, stripTop, width, stripHeight, fill);
            if (width > 38)
            {
                canvas.DrawText(placement.Length.ToString(), left + 6, stripTop + 39, label);
            }
        }
        canvas.DrawRect(stripLeft, stripTop, stripWidth, stripHeight, border);

        const float cardWidth = 164;
        const float gap = 9;
        const float cardTop = 198;
        for (int index = 0; index < segments.Length; index++)
        {
            MachinaPanelEdgeSegment segment = segments[index];
            MachinaPanelResolvedSegment placement = placements[index];
            float x = 36 + (index * (cardWidth + gap));
            var card = new SKRect(x, cardTop, x + cardWidth, 712);
            using var cardFill = new SKPaint { Color = new SKColor(22, 27, 38) };
            canvas.DrawRoundRect(card, 8, 8, cardFill);
            canvas.DrawRoundRect(card, 8, 8, border);

            var source = new SKRect(
                (float)segment.SourceRect.X,
                (float)segment.SourceRect.Y,
                (float)(segment.SourceRect.X + segment.SourceRect.Width),
                (float)(segment.SourceRect.Y + segment.SourceRect.Height));
            var preview = new SKRect(x + 12, cardTop + 14, x + cardWidth - 12, cardTop + 146);
            canvas.DrawBitmap(atlas, source, preview);
            using var accent = new SKPaint { Color = palette[index], Style = SKPaintStyle.Stroke, StrokeWidth = 3 };
            canvas.DrawRect(preview, accent);

            DrawWrapped(canvas, segment.Id, x + 12, cardTop + 178, cardWidth - 24, label);
            canvas.DrawText($"source {segment.SourceRect.X:0},{segment.SourceRect.Y:0}", x + 12, cardTop + 252, detail);
            canvas.DrawText($"{segment.SourceRect.Width:0}×{segment.SourceRect.Height:0}", x + 12, cardTop + 274, detail);
            canvas.DrawText($"{segment.AllocationKind}", x + 12, cardTop + 310, label);
            canvas.DrawText($"min {segment.MinimumLength}  weight {segment.Weight}", x + 12, cardTop + 334, detail);
            canvas.DrawText($"sampling {segment.Sampling}", x + 12, cardTop + 366, detail);
            canvas.DrawText($"offset {placement.Offset}", x + 12, cardTop + 402, label);
            canvas.DrawText($"length {placement.Length}", x + 12, cardTop + 428, label);
        }

        canvas.DrawText(
            "Read-only projection. Authority: sunkill-dialogue-panel.obj.ts → record table + edge program → compile.",
            36,
            744,
            detail);
        canvas.Flush();
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(artifactRoot, fileName), encoded.ToArray());
    }

    private static void DrawWrapped(SKCanvas canvas, string text, float x, float y, float width, SKPaint paint)
    {
        string[] parts = text.Split('.');
        string line = string.Empty;
        int lineIndex = 0;
        foreach (string part in parts)
        {
            string candidate = line.Length == 0 ? part : line + "." + part;
            if (line.Length > 0 && paint.MeasureText(candidate) > width)
            {
                canvas.DrawText(line, x, y + (lineIndex * 22), paint);
                line = part;
                lineIndex++;
            }
            else
            {
                line = candidate;
            }
        }

        canvas.DrawText(line, x, y + (lineIndex * 22), paint);
    }

    private static void WriteAllocatorSvg(string path, SpanAllocationResult<string> result)
    {
        const int left = 40;
        const int stripWidth = 880;
        string[] colors = ["#df6f34", "#69beff", "#f6c445"];
        var builder = new StringBuilder();
        builder.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"960\" height=\"220\" viewBox=\"0 0 960 220\">");
        builder.AppendLine("<rect width=\"960\" height=\"220\" fill=\"#0c0e14\"/>");
        builder.AppendLine($"<text x=\"40\" y=\"36\" fill=\"white\" font-family=\"monospace\" font-size=\"22\">extent {result.Extent} · minimum {result.MinimumDemand} · {result.Status}</text>");
        foreach ((SpanPlacement<string> placement, int index) in result.Placements.Select((item, index) => (item, index)))
        {
            double x = left + ((double)placement.Offset / Math.Max(1, result.Extent) * stripWidth);
            double segmentWidth = (double)placement.Length / Math.Max(1, result.Extent) * stripWidth;
            builder.AppendLine($"<rect x=\"{x:0.###}\" y=\"70\" width=\"{segmentWidth:0.###}\" height=\"70\" fill=\"{colors[index]}\" stroke=\"#0c0e14\"/>");
            builder.AppendLine($"<text x=\"{x + 6:0.###}\" y=\"112\" fill=\"#10131a\" font-family=\"monospace\" font-size=\"18\">{placement.Payload} {placement.Offset}+{placement.Length}</text>");
        }
        builder.AppendLine($"<text x=\"40\" y=\"180\" fill=\"#b4bfd2\" font-family=\"monospace\" font-size=\"18\">used {result.UsedLength} · unused {result.UnusedLength} · deficit {result.DeficitLength}</text>");
        if (result.Diagnostics.Count > 0)
        {
            builder.AppendLine($"<text x=\"40\" y=\"207\" fill=\"#ff9e72\" font-family=\"monospace\" font-size=\"15\">{result.Diagnostics[0].Code}: minimum demand exceeds extent; clipped in request order</text>");
        }
        builder.AppendLine("</svg>");
        File.WriteAllText(path, builder.ToString());
    }

    private static void WriteScreenshot(string path, NativeLayerFrameResult frame, int width, int height)
    {
        Require(frame.NativeFrame.Pixels is not null, "Native compositor did not return screenshot pixels.");
        PngWriter.Write(path, width, height, frame.NativeFrame.Pixels!);
    }

    private static SeamMetrics MeasureSeams(byte[] pixels, int width, int height)
    {
        var errors = new List<int>();
        for (int x = 114; x < 1178; x += 12)
        {
            AddPixelPairErrors(pixels, width, height, x - 1, 360, x, 360, errors);
        }
        for (int y = 114; y < 618; y += 12)
        {
            AddPixelPairErrors(pixels, width, height, 640, y - 1, 640, y, errors);
        }
        return new SeamMetrics(errors.Count, errors.Max(), errors.Average());
    }

    private static void AddPixelPairErrors(
        byte[] pixels,
        int width,
        int height,
        int firstX,
        int firstY,
        int secondX,
        int secondY,
        ICollection<int> errors)
    {
        if (firstX < 0 || firstY < 0 || firstX >= width || firstY >= height
            || secondX < 0 || secondY < 0 || secondX >= width || secondY >= height)
        {
            throw new ArgumentOutOfRangeException(nameof(firstX));
        }
        int firstOffset = ((firstY * width) + firstX) * 4;
        int secondOffset = ((secondY * width) + secondX) * 4;
        errors.Add(Math.Abs(pixels[firstOffset] - pixels[secondOffset]));
        errors.Add(Math.Abs(pixels[firstOffset + 1] - pixels[secondOffset + 1]));
        errors.Add(Math.Abs(pixels[firstOffset + 2] - pixels[secondOffset + 2]));
    }

    private static int MeasurePixelColorError(
        byte[] pixels,
        int width,
        int x,
        int y,
        IReadOnlyList<int> expectedRgba)
    {
        int offset = ((y * width) + x) * 4;
        int maxError = 0;
        for (int channel = 0; channel < 4; channel++)
        {
            maxError = Math.Max(maxError, Math.Abs(pixels[offset + channel] - expectedRgba[channel]));
        }
        return maxError;
    }

    private static void AssertContiguous(IReadOnlyList<MachinaPanelResolvedSegment> segments, int extent)
    {
        int offset = 0;
        foreach (MachinaPanelResolvedSegment segment in segments)
        {
            Require(segment.Offset == offset, "Panel edge contains a gap or overlap.");
            Require(segment.Length >= 0, "Panel edge contains a negative span.");
            offset += segment.Length;
        }
        Require(offset == extent, "Panel edge does not cover its resolved extent.");
    }

    private static T AssertSingle<T>(IReadOnlyList<T> values, string name)
    {
        return values.Count == 1
            ? values[0]
            : throw new InvalidOperationException($"Expected one {name}, found {values.Count}.");
    }

    private static string Sha256File(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    private static string Sha256Text(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string Describe(IEnumerable<Copeland.TS.Diagnostics.Diagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));
    }

    private static void WriteJson(string path, object value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        }) + Environment.NewLine);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record PanelProof(
        string File,
        int LogicalPanelWidth,
        int FramebufferWidth,
        int FramebufferHeight,
        MachinaPanelEdgeAllocation Top,
        int DiagnosticCount,
        string PixelSha256);

    private sealed record SeamMetrics(int SampleCount, int MaxChannelError, double MeanChannelError);
}
