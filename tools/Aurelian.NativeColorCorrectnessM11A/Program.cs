using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

const int Columns = 4;
const int Rows = 4;
const int Cell = 48;
const int Width = Columns * Cell;
const int Height = Rows * Cell;

string root = FindRepositoryRoot();
string output = Path.Combine(root, "artifacts", "aurelian-native-color-correctness-m11a");
Directory.CreateDirectory(output);

CompiledGraphicsProgram analyticProgram = Compile(root, "src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts");
CompiledGraphicsProgram texturedProgram = Compile(root, "samples/Aurelian/ForwardTexturedM3.v.ts");
VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
    PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.NativeColorCorrectnessM11A"));
Require(init.Success && init.Plant is not null, "Vulkan initialization failed: " + string.Join("; ", init.Diagnostics.Select(item => item.Message)));

Swatch[] swatches =
[
    Solid("white", 255, 255, 255),
    Solid("black", 0, 0, 0),
    Solid("middle-gray", 128, 128, 128),
    Solid("red", 255, 0, 0),
    Solid("green", 0, 255, 0),
    Solid("blue", 0, 0, 255),
    Solid("yellow", 255, 255, 0),
    Solid("cyan", 0, 255, 255),
    Solid("magenta", 255, 0, 255),
    Solid("sage", 127, 165, 118),
    Solid("sand", 213, 191, 141),
    Solid("coral", 183, 109, 84),
    Alpha("red-50-over-slate", 255, 0, 0, 128, 32, 62, 54),
    Alpha("cyan-25-over-navy", 0, 255, 255, 64, 16, 32, 64),
    Alpha("white-50-over-gray", 255, 255, 255, 128, 128, 128, 128),
    Alpha("pastel-75-over-black", 226, 213, 165, 192, 0, 0, 0),
];

using (init.Plant)
{
    RenderedPath before = RenderAnalytic(
        init.Plant!,
        analyticProgram,
        swatches,
        new Native2DPipelineOptions(Native2DPipelineKind.AnalyticShape2D, InputsAreSrgb: false));
    RenderedPath analytic = RenderAnalytic(init.Plant!, analyticProgram, swatches, Native2DPipelineOptions.AnalyticShape2D);
    RenderedPath textured = RenderTextured(init.Plant!, texturedProgram, swatches);
    Rgba clearExpected = new(128, 96, 64, 255);
    RenderedPath clear = RenderClear(init.Plant!, analyticProgram, clearExpected);

    RgbaPng.Write(Path.Combine(output, "palette-before-double-encoded.png"), Width, Height, before.Pixels);
    RgbaPng.Write(Path.Combine(output, "palette-after-analytic.png"), Width, Height, analytic.Pixels);
    RgbaPng.Write(Path.Combine(output, "palette-after-textured.png"), Width, Height, textured.Pixels);

    Comparison beforeComparison = Compare(swatches, before.Pixels);
    Comparison analyticComparison = Compare(swatches, analytic.Pixels);
    Comparison texturedComparison = Compare(swatches, textured.Pixels);
    Comparison parityComparison = ComparePaths(swatches, analytic.Pixels, textured.Pixels);
    Measurement clearComparison = Measure("render-pass-clear", clearExpected, ReadPixel(clear.Pixels, 0, 0, Width));

    Require(beforeComparison.MaximumChannelError >= 40, "Legacy sRGB attachment fault was not reproduced strongly enough.");
    Require(analyticComparison.MaximumChannelError <= 1, "Corrected analytic palette exceeded one byte of error.");
    Require(texturedComparison.MaximumChannelError <= 1, "Corrected textured palette exceeded one byte of error.");
    Require(parityComparison.MaximumChannelError <= 1, "Analytic and textured paths diverged by more than one byte.");
    Require(clearComparison.MaximumChannelError <= 1, "sRGB render-pass clear exceeded one byte of error.");

    WriteJson("palette-measurements.json", new
    {
        schema = "aurelian.native-color.palette.v1",
        authoredColorEncoding = "IEC 61966-2-1 sRGB bytes; straight alpha",
        targetFormat = VulkanTextureFormat.Rgba8Srgb.ToString(),
        samplePoint = "center pixel of each 48x48 swatch",
        expectedBlend = "decode source and destination sRGB to linear; source-over in linear RGB; encode result to sRGB; alpha remains linear",
        before = beforeComparison,
        afterAnalytic = analyticComparison,
        afterTextured = texturedComparison,
        analyticTextureParity = parityComparison,
        renderPassClear = clearComparison,
    });
    WriteJson("path-audit.json", new
    {
        schema = "aurelian.native-color.path-audit.v1",
        cause = "Authored sRGB color numbers and RGBA8 texture bytes were treated as linear shader values, then encoded by the sRGB color attachment a second time.",
        swapchain = "prefers B8G8R8A8_SRGB with SRGB_NONLINEAR when available",
        renderTarget = "matches swapchain sRGB format in the visible native path",
        materialColors = "decoded from authored sRGB to linear before uniform upload when rendering to an sRGB target",
        spriteTextures = "created as R8G8B8A8_SRGB when authored sprite bytes feed an sRGB target; sampling performs the required decode",
        dataTextures = "MSDF atlas remains UNORM because its channels are distance data, not color",
        blend = "straight alpha: srcAlpha / oneMinusSrcAlpha for RGB; one / oneMinusSrcAlpha for alpha; performed in linear space on sRGB attachments",
        premultiplication = "not used by the sprite path",
        png = "PNG evidence carries explicit sRGB and gAMA chunks; encoded bytes are identical to native readback",
        readback = "direct vkCmdCopyImageToBuffer from the render target; BGRA targets are normalized to RGBA by VulkanNativeFrameTarget",
        shaderGamma = "no shader gamma function; Vulkan format conversion is the sole output encoding",
    });
    WriteJson("proof.json", new
    {
        milestone = "AURELIAN-NATIVE-SPRITE-TILE-GRAPHICS-M11A",
        outcome = "A",
        sourceOfFadeIdentified = true,
        sourceOfFadeFixed = true,
        nativeReadbackQualified = true,
        pngEvidenceQualified = true,
        opaqueToleranceBytes = 1,
        alphaToleranceBytes = 1,
        beforeMaximumChannelError = beforeComparison.MaximumChannelError,
        beforeAverageChannelError = beforeComparison.AverageChannelError,
        afterAnalyticMaximumChannelError = analyticComparison.MaximumChannelError,
        afterAnalyticAverageChannelError = analyticComparison.AverageChannelError,
        afterTexturedMaximumChannelError = texturedComparison.MaximumChannelError,
        afterTexturedAverageChannelError = texturedComparison.AverageChannelError,
        analyticTextureParityMaximumChannelError = parityComparison.MaximumChannelError,
        renderPassClearMaximumChannelError = clearComparison.MaximumChannelError,
        gpu = init.Facts!.PhysicalDeviceName,
        validationRequested = true,
        validationAvailable = init.Facts.EnabledValidationLayers.Contains("VK_LAYER_KHRONOS_validation", StringComparer.Ordinal),
    });
    WriteJson("manifest.json", new
    {
        milestone = "AURELIAN-NATIVE-SPRITE-TILE-GRAPHICS-M11A",
        files = new[]
        {
            "palette-before-double-encoded.png",
            "palette-after-analytic.png",
            "palette-after-textured.png",
            "palette-measurements.json",
            "path-audit.json",
            "proof.json",
            "manifest.json",
        },
    });

    Console.WriteLine($"GPU: {init.Facts.PhysicalDeviceName}");
    Console.WriteLine($"Before max error: {beforeComparison.MaximumChannelError}; after analytic: {analyticComparison.MaximumChannelError}; textured: {texturedComparison.MaximumChannelError}; parity: {parityComparison.MaximumChannelError}");
}

RenderedPath RenderAnalytic(
    AurelianVulkanPlant plant,
    CompiledGraphicsProgram program,
    IReadOnlyList<Swatch> palette,
    Native2DPipelineOptions options)
{
    using var target = new VulkanNativeFrameTarget(plant, Width, Height, VulkanTextureFormat.Rgba8Srgb);
    using var renderer = new VulkanOrderedQuadRenderer(plant, program, target, options);
    using VulkanNativeFrameSession frame = target.BeginFrame(NativeFrameClearColor.Transparent);
    frame.Present(renderer, pass =>
    {
        for (int index = 0; index < palette.Count; index++)
        {
            SubmitAnalyticSwatch(pass, palette[index], index);
        }
    });
    VulkanNativeFrameResult result = frame.EndFrame();
    return new RenderedPath(result.Pixels!, result.PixelSha256!);
}

RenderedPath RenderTextured(
    AurelianVulkanPlant plant,
    CompiledGraphicsProgram program,
    IReadOnlyList<Swatch> palette)
{
    using var target = new VulkanNativeFrameTarget(plant, Width, Height, VulkanTextureFormat.Rgba8Srgb);
    using var renderer = new VulkanOrderedQuadRenderer(plant, program, target, Native2DPipelineOptions.SpriteNearest);
    byte[] atlas = new byte[palette.Count * 4];
    for (int index = 0; index < palette.Count; index++)
    {
        atlas[index * 4] = palette[index].Source.Red;
        atlas[index * 4 + 1] = palette[index].Source.Green;
        atlas[index * 4 + 2] = palette[index].Source.Blue;
        atlas[index * 4 + 3] = palette[index].Source.Alpha;
    }
    Native2DTextureHandle texture = renderer.CreateTexture(Columns, Rows, atlas);
    Native2DTextureHandle background = renderer.CreateTexture(1, 1, [255, 255, 255, 255]);
    using VulkanNativeFrameSession frame = target.BeginFrame(NativeFrameClearColor.Transparent);
    frame.Present(renderer, pass =>
    {
        for (int index = 0; index < palette.Count; index++)
        {
            Swatch swatch = palette[index];
            Native2DRect destination = CellRect(index);
            pass.SubmitQuad(new NativeQuadSubmission(destination, Native2DUvRect.Full, background, ToTint(swatch.Background)));
            int column = index % Columns;
            int row = index / Columns;
            pass.SubmitQuad(new NativeQuadSubmission(
                destination,
                new Native2DUvRect(
                    (float)column / Columns,
                    (float)row / Rows,
                    (float)(column + 1) / Columns,
                    (float)(row + 1) / Rows),
                texture,
                Native2DTint.White));
        }
    });
    VulkanNativeFrameResult result = frame.EndFrame();
    return new RenderedPath(result.Pixels!, result.PixelSha256!);
}

RenderedPath RenderClear(AurelianVulkanPlant plant, CompiledGraphicsProgram program, Rgba color)
{
    using var target = new VulkanNativeFrameTarget(plant, Width, Height, VulkanTextureFormat.Rgba8Srgb);
    using var renderer = new VulkanOrderedQuadRenderer(plant, program, target, Native2DPipelineOptions.AnalyticShape2D);
    using VulkanNativeFrameSession frame = target.BeginFrame(new NativeFrameClearColor(
        color.Red / 255f,
        color.Green / 255f,
        color.Blue / 255f,
        color.Alpha / 255f));
    frame.Present(renderer, _ => { });
    VulkanNativeFrameResult result = frame.EndFrame();
    return new RenderedPath(result.Pixels!, result.PixelSha256!);
}

void SubmitAnalyticSwatch(VulkanOrderedQuadRenderer renderer, Swatch swatch, int index)
{
    Native2DRect destination = CellRect(index);
    Submit(destination, ToTint(swatch.Background));
    Submit(destination, ToTint(swatch.Source));

    void Submit(Native2DRect rect, Native2DTint color)
    {
        renderer.SubmitAnalyticShape(new NativeAnalyticShapeSubmission(
            rect,
            new Native2DSize(rect.Width, rect.Height),
            Native2DUvRect.Full,
            NativeAnalyticShapeKind.RoundedRect,
            color,
            0,
            color,
            0));
    }
}

Comparison Compare(IReadOnlyList<Swatch> palette, byte[] pixels)
{
    var rows = new List<Measurement>(palette.Count);
    for (int index = 0; index < palette.Count; index++)
    {
        Rgba expected = BlendExpected(palette[index]);
        Rgba actual = ReadCenter(pixels, index);
        rows.Add(Measure(palette[index].Name, expected, actual));
    }
    return Summarize(rows);
}

Comparison ComparePaths(IReadOnlyList<Swatch> palette, byte[] expectedPixels, byte[] actualPixels)
{
    var rows = new List<Measurement>(palette.Count);
    for (int index = 0; index < palette.Count; index++)
    {
        rows.Add(Measure(palette[index].Name, ReadCenter(expectedPixels, index), ReadCenter(actualPixels, index)));
    }
    return Summarize(rows);
}

Measurement Measure(string name, Rgba expected, Rgba actual)
{
    int[] error =
    [
        Math.Abs(expected.Red - actual.Red),
        Math.Abs(expected.Green - actual.Green),
        Math.Abs(expected.Blue - actual.Blue),
        Math.Abs(expected.Alpha - actual.Alpha),
    ];
    return new Measurement(name, expected, actual, error, error.Average(), error.Max());
}

Comparison Summarize(IReadOnlyList<Measurement> measurements)
{
    int[] errors = measurements.SelectMany(item => item.PerChannelError).ToArray();
    return new Comparison(measurements, errors.Average(), errors.Max());
}

Rgba BlendExpected(Swatch swatch)
{
    double alpha = swatch.Source.Alpha / 255.0;
    byte red = Encode(Decode(swatch.Source.Red) * alpha + Decode(swatch.Background.Red) * (1 - alpha));
    byte green = Encode(Decode(swatch.Source.Green) * alpha + Decode(swatch.Background.Green) * (1 - alpha));
    byte blue = Encode(Decode(swatch.Source.Blue) * alpha + Decode(swatch.Background.Blue) * (1 - alpha));
    byte outputAlpha = (byte)Math.Round(swatch.Source.Alpha + swatch.Background.Alpha * (1 - alpha), MidpointRounding.AwayFromZero);
    return new Rgba(red, green, blue, outputAlpha);
}

double Decode(byte encoded)
{
    double value = encoded / 255.0;
    return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
}

byte Encode(double linear)
{
    double encoded = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
    return (byte)Math.Clamp(Math.Round(encoded * 255, MidpointRounding.AwayFromZero), 0, 255);
}

Rgba ReadCenter(byte[] pixels, int index)
{
    int x = index % Columns * Cell + Cell / 2;
    int y = index / Columns * Cell + Cell / 2;
    int offset = (y * Width + x) * 4;
    return new Rgba(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
}

Rgba ReadPixel(byte[] pixels, int x, int y, int width)
{
    int offset = (y * width + x) * 4;
    return new Rgba(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
}

Native2DRect CellRect(int index)
{
    return new Native2DRect(index % Columns * Cell, index / Columns * Cell, Cell, Cell);
}

Native2DTint ToTint(Rgba color)
{
    return new Native2DTint(color.Red / 255f, color.Green / 255f, color.Blue / 255f, color.Alpha / 255f);
}

Swatch Solid(string name, byte red, byte green, byte blue)
{
    return new Swatch(name, new Rgba(red, green, blue, 255), new Rgba(0, 0, 0, 255));
}

Swatch Alpha(string name, byte red, byte green, byte blue, byte alpha, byte backgroundRed, byte backgroundGreen, byte backgroundBlue)
{
    return new Swatch(name, new Rgba(red, green, blue, alpha), new Rgba(backgroundRed, backgroundGreen, backgroundBlue, 255));
}

CompiledGraphicsProgram Compile(string repositoryRoot, string relativePath)
{
    string source = File.ReadAllText(Path.Combine(repositoryRoot, relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);
    VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(relativePath, source)]));
    Require(module.Success, string.Join(Environment.NewLine, module.Diagnostics.Select(item => item.Message)));
    VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
    Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated, backend.Vertex.DxcOutput + backend.Pixel.DxcOutput);
    return CompiledGraphicsProgramExporter.Export(module, backend);
}

void WriteJson(string name, object value)
{
    File.WriteAllText(
        Path.Combine(output, name),
        JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }) + Environment.NewLine,
        Encoding.UTF8);
}

void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
    {
        directory = directory.Parent;
    }
    return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
}

readonly record struct Rgba(byte Red, byte Green, byte Blue, byte Alpha);
sealed record Swatch(string Name, Rgba Source, Rgba Background);
sealed record Measurement(string Name, Rgba Expected, Rgba Actual, int[] PerChannelError, double AverageChannelError, int MaximumChannelError);
sealed record Comparison(IReadOnlyList<Measurement> Swatches, double AverageChannelError, int MaximumChannelError);
sealed record RenderedPath(byte[] Pixels, string Sha256);

static class RgbaPng
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void Write(string path, int width, int height, byte[] rgba)
    {
        using FileStream stream = File.Create(path);
        stream.Write(Signature);
        WriteChunk(stream, "IHDR"u8, Header(width, height));
        WriteChunk(stream, "sRGB"u8, [0]);
        WriteChunk(stream, "gAMA"u8, [0, 0, 177, 143]);
        WriteChunk(stream, "IDAT"u8, Compress(width, height, rgba));
        WriteChunk(stream, "IEND"u8, []);
    }

    private static byte[] Header(int width, int height)
    {
        byte[] data = new byte[13];
        WriteBigEndian(data, 0, width);
        WriteBigEndian(data, 4, height);
        data[8] = 8;
        data[9] = 6;
        return data;
    }

    private static byte[] Compress(int width, int height, byte[] rgba)
    {
        int rowBytes = width * 4;
        byte[] raw = new byte[(rowBytes + 1) * height];
        for (int row = 0; row < height; row++)
        {
            Buffer.BlockCopy(rgba, row * rowBytes, raw, row * (rowBytes + 1) + 1, rowBytes);
        }
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, byte[] data)
    {
        WriteBigEndian(stream, data.Length);
        stream.Write(type);
        stream.Write(data);
        uint crc = UpdateCrc(UpdateCrc(0xFFFFFFFFu, type), data);
        WriteBigEndian(stream, unchecked((int)~crc));
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }
        return crc;
    }

    private static void WriteBigEndian(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        WriteBigEndian(bytes, 0, value);
        stream.Write(bytes);
    }

    private static void WriteBigEndian(Span<byte> bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
