using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.GameWorld2D;
using SkiaSharp;

/// <summary>Reviews the four-direction walk contract using the game's actual grid importer and pivots.</summary>
internal static class SpriteSheetReview
{
    private static readonly string[] Directions = ["down", "up", "left", "right"];

    public static int Run(string[] args)
    {
        if (args.Length != 3 || args[1] != "--out")
        {
            Console.Error.WriteLine("usage: Aurelian.ArtSlots review-sheet <4x4.png> --out <directory>");
            return 2;
        }
        string sourcePath = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(output);
        using var codec = SKCodec.Create(sourcePath)
            ?? throw new InvalidDataException("Could not open sprite sheet: " + sourcePath);
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            throw new InvalidDataException("Could not decode sprite sheet: " + sourcePath);
        }
        byte[] rgba = new byte[checked(info.Width * info.Height * 4)];
        Marshal.Copy(bitmap.GetPixels(), rgba, 0, rgba.Length);
        var atlas = new SpriteAtlasResource(new SpriteAssetId("sheet-review"),
            Convert.ToHexStringLower(SHA256.HashData(rgba)), (uint)info.Width, (uint)info.Height, rgba, SpriteSampling.Linear);
        IReadOnlyList<SpriteFrameMetadata> frames = GridSpriteSheet.Import(atlas, 4, 4, 64);
        var cells = frames.Select((frame, index) => Measure(frame, index, info.Width, info.Height)).ToArray();
        bool clearGutters = cells.All(cell => cell.MinimumMarginPixels > 0);
        int transparentPixels = Enumerable.Range(0, info.Width * info.Height).Count(index => rgba[index * 4 + 3] == 0);
        bool hasTransparency = transparentPixels > 0;
        var report = new
        {
            Source = Path.GetFileName(sourcePath),
            FileSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sourcePath))),
            Width = info.Width,
            Height = info.Height,
            FrameCount = frames.Count,
            Importer = "Aurelian.GameWorld2D.GridSpriteSheet.Import; 4x4; visible height 64 at 48 pixels/metre",
            PassedGeometry = clearGutters && hasTransparency,
            HasTransparency = hasTransparency,
            TransparentPixelFraction = transparentPixels / (double)(info.Width * info.Height),
            MinimumMarginPixels = cells.Min(cell => cell.MinimumMarginPixels),
            Cells = cells,
            Scope = "Geometry gate only. Check facing, identity, opposite limbs, alpha edges and motion visually. No automatic animation or artistic approval.",
        };
        File.WriteAllText(Path.Combine(output, "sheet-review.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        WritePoses(bitmap, frames, Path.Combine(output, "poses.png"));
        string animation = Path.Combine(output, "animation");
        Directory.CreateDirectory(animation);
        for (int step = 0; step < 4; step++)
        {
            WriteAnimationFrame(bitmap, frames, step, Path.Combine(animation, $"{step:D2}.png"));
        }
        Console.WriteLine($"SPRITE_SHEET_GEOMETRY {(report.PassedGeometry ? "PASSED" : "FAILED")} frames={frames.Count} margin={report.MinimumMarginPixels} -> {output}");
        return report.PassedGeometry ? 0 : 1;
    }

    private static CellMeasure Measure(SpriteFrameMetadata frame, int index, int width, int height)
    {
        int column = index % 4;
        int row = index / 4;
        int left = frame.AtlasX - column * width / 4;
        int top = frame.AtlasY - row * height / 4;
        int right = (column + 1) * width / 4 - frame.AtlasX - frame.Width;
        int bottom = (row + 1) * height / 4 - frame.AtlasY - frame.Height;
        return new CellMeasure(index, Directions[row], column, left, top, right, bottom,
            Math.Min(Math.Min(left, right), Math.Min(top, bottom)), frame.Width, frame.Height,
            frame.Width * frame.Scale, frame.Height * frame.Scale);
    }

    private static void WritePoses(SKBitmap source, IReadOnlyList<SpriteFrameMetadata> frames, string path)
    {
        using var image = new SKBitmap(640, 432);
        using var canvas = new SKCanvas(image);
        canvas.Clear(new SKColor(83, 111, 69));
        using var font = new SKFont(SKTypeface.Default, 13);
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        for (int index = 0; index < frames.Count; index++)
        {
            float x = index % 4 * 160;
            float y = index / 4 * 108;
            DrawPose(canvas, source, frames[index], x + 80, y + 82, 1);
            canvas.DrawText($"{Directions[index / 4]} / {index % 4}", x + 10, y + 102, font, ink);
        }
        WritePng(image, path);
    }

    private static void WriteAnimationFrame(SKBitmap source, IReadOnlyList<SpriteFrameMetadata> frames, int step, string path)
    {
        using var image = new SKBitmap(768, 400);
        using var canvas = new SKCanvas(image);
        canvas.Clear(new SKColor(83, 111, 69));
        using var font = new SKFont(SKTypeface.Default, 17);
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var line = new SKPaint { Color = new SKColor(150, 167, 123), StrokeWidth = 1 };
        for (int row = 0; row < 4; row++)
        {
            float x = row * 192 + 96;
            SpriteFrameMetadata frame = frames[row * 4 + step];
            canvas.DrawLine(x - 70, 100, x + 70, 100, line);
            canvas.DrawLine(x - 70, 350, x + 70, 350, line);
            DrawPose(canvas, source, frame, x, 100, 1);
            DrawPose(canvas, source, frame, x, 350, 3);
            canvas.DrawText(Directions[row], x - 26, 383, font, ink);
        }
        canvas.DrawText($"64px / 3x inspection     frame {step}     shared game importer", 12, 20, font, ink);
        WritePng(image, path);
    }

    private static void DrawPose(SKCanvas canvas, SKBitmap source, SpriteFrameMetadata frame, float anchorX, float anchorY, float magnification)
    {
        float scale = (float)frame.Scale * magnification;
        var region = SKRect.Create(frame.AtlasX, frame.AtlasY, frame.Width, frame.Height);
        var target = SKRect.Create(anchorX - (float)frame.PivotX * scale,
            anchorY - (float)frame.PivotY * scale, frame.Width * scale, frame.Height * scale);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(source, region, target, paint);
    }

    private static void WritePng(SKBitmap bitmap, string path)
    {
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private sealed record CellMeasure(int Frame, string Facing, int Column, int LeftMargin, int TopMargin,
        int RightMargin, int BottomMargin, int MinimumMarginPixels, int Width, int Height, double DisplayWidth, double DisplayHeight);
}
