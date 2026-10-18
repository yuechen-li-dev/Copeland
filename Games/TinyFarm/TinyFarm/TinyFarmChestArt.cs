using System.Security.Cryptography;
using Aurelian.GameWorld2D;
using SkiaSharp;

namespace TinyFarm.Native;

/// <summary>Repo-owned two-pose vector sprite (closed and open chest), realized through the existing RGBA resource path.</summary>
internal static class TinyFarmChestArt
{
    public static SpriteAtlasResource Create()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(512, 256, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            Draw(canvas, false);
            canvas.Translate(256, 0);
            Draw(canvas, true);
        }
        byte[] pixels = TinyFarmImage.From(bitmap).ToRgba();
        return new SpriteAtlasResource(new SpriteAssetId("tinyfarm-shipping-chest"),
            Convert.ToHexStringLower(SHA256.HashData(pixels)), 512, 256, pixels, SpriteSampling.Linear);
    }

    private static void Draw(SKCanvas canvas, bool open)
    {
        using var outline = Stroke(new SKColor(48, 44, 33), 5);
        using var grain = Stroke(new SKColor(73, 53, 34, 125), 2);
        using var leaf = Stroke(new SKColor(65, 101, 52), 4);
        using var shadow = Fill(new SKColor(20, 34, 26, 65));
        using var front = Fill(SKColors.White);
        front.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 140), new SKPoint(0, 228),
            [new SKColor(186, 130, 66), new SKColor(113, 76, 43)], SKShaderTileMode.Clamp);
        using var top = Fill(new SKColor(202, 155, 89));
        using var side = Fill(new SKColor(98, 72, 45));
        using var metal = Fill(new SKColor(181, 172, 113));
        using var dark = Fill(new SKColor(43, 40, 29));
        using var label = Fill(new SKColor(219, 219, 169));

        canvas.DrawOval(new SKRect(24, 206, 24 + 212, 206 + 27), shadow);
        SKPoint[] face = [new(32, 142), new(196, 142), new(196, 220), new(32, 220)];
        SKPoint[] right = [new(196, 142), new(224, 110), new(224, 188), new(196, 220)];
        SKPoint[] lid = open
            ? [new(32, 88), new(196, 88), new(224, 48), new(60, 48)]
            : [new(32, 142), new(196, 142), new(224, 110), new(60, 110)];
        Polygon(canvas, face, front, outline);
        Polygon(canvas, right, side, outline);
        if (open)
        {
            SKPoint[] interior = [new(32, 142), new(196, 142), new(224, 110), new(60, 110)];
            Polygon(canvas, interior, dark, outline);
            canvas.DrawLine(32, 142, 32, 88, outline);
            canvas.DrawLine(196, 142, 196, 88, outline);
        }
        Polygon(canvas, lid, top, outline);
        for (int x = 54; x < 190; x += 28)
        {
            canvas.DrawLine(x, 149, x, 214, grain);
            canvas.DrawLine(x, open ? 85 : 139, x + 25, open ? 51 : 113, grain);
        }
        canvas.DrawRect(48, 142, 11, 78, metal);
        canvas.DrawRect(172, 142, 11, 78, metal);
        canvas.DrawRect(103, 147, 24, 24, metal);
        canvas.DrawRect(113, 156, 5, 8, dark);
        canvas.DrawOval(new SKRect(102, 181, 102 + 26, 181 + 23), label);
        canvas.DrawLine(115, 183, 108, 176, leaf);
        canvas.DrawLine(115, 183, 122, 176, leaf);
    }

    private static void Polygon(SKCanvas canvas, SKPoint[] points, SKPaint fill, SKPaint stroke)
    {
        using var path = new SKPath();
        path.AddPoly(points, close: true);
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);
    }

    private static SKPaint Fill(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

    private static SKPaint Stroke(SKColor color, float width) => new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
        StrokeJoin = SKStrokeJoin.Round,
    };
}
