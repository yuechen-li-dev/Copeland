using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Aurelian.GameWorld2D;

namespace TinyFarm.Native;

/// <summary>Repo-owned two-pose vector sprite, realized through the existing RGBA resource path.</summary>
internal static class TinyFarmChestArt
{
    public static SpriteAtlasResource Create()
    {
        using var bitmap = new Bitmap(512, 256, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Draw(graphics, false);
            graphics.TranslateTransform(256, 0);
            Draw(graphics, true);
        }
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, 512, 256), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[512 * 256 * 4];
        try
        {
            for (int y = 0; y < 256; y++)
            {
                var row = new byte[512 * 4];
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < 512; x++)
                {
                    int target = (y * 512 + x) * 4;
                    pixels[target] = row[x * 4 + 2];
                    pixels[target + 1] = row[x * 4 + 1];
                    pixels[target + 2] = row[x * 4];
                    pixels[target + 3] = row[x * 4 + 3];
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return new SpriteAtlasResource(new SpriteAssetId("tinyfarm-shipping-chest"),
            Convert.ToHexStringLower(SHA256.HashData(pixels)), 512, 256, pixels, SpriteSampling.Linear);
    }

    private static void Draw(Graphics graphics, bool open)
    {
        using var outline = new Pen(Color.FromArgb(255, 48, 44, 33), 5) { LineJoin = LineJoin.Round };
        using var grain = new Pen(Color.FromArgb(125, 73, 53, 34), 2);
        using var shadow = new SolidBrush(Color.FromArgb(65, 20, 34, 26));
        using var front = new LinearGradientBrush(new Point(0, 140), new Point(0, 228),
            Color.FromArgb(186, 130, 66), Color.FromArgb(113, 76, 43));
        using var top = new SolidBrush(Color.FromArgb(202, 155, 89));
        using var side = new SolidBrush(Color.FromArgb(98, 72, 45));
        using var metal = new SolidBrush(Color.FromArgb(181, 172, 113));
        using var dark = new SolidBrush(Color.FromArgb(43, 40, 29));
        graphics.FillEllipse(shadow, 24, 206, 212, 27);
        Point[] face = [new(32, 142), new(196, 142), new(196, 220), new(32, 220)];
        Point[] right = [new(196, 142), new(224, 110), new(224, 188), new(196, 220)];
        Point[] lid = open
            ? [new(32, 88), new(196, 88), new(224, 48), new(60, 48)]
            : [new(32, 142), new(196, 142), new(224, 110), new(60, 110)];
        graphics.FillPolygon(front, face);
        graphics.FillPolygon(side, right);
        graphics.DrawPolygon(outline, face);
        graphics.DrawPolygon(outline, right);
        if (open)
        {
            Point[] interior = [new(32, 142), new(196, 142), new(224, 110), new(60, 110)];
            graphics.FillPolygon(dark, interior);
            graphics.DrawPolygon(outline, interior);
            graphics.DrawLine(outline, 32, 142, 32, 88);
            graphics.DrawLine(outline, 196, 142, 196, 88);
        }
        graphics.FillPolygon(top, lid);
        graphics.DrawPolygon(outline, lid);
        for (int x = 54; x < 190; x += 28)
        {
            graphics.DrawLine(grain, x, 149, x, 214);
            graphics.DrawLine(grain, x, open ? 85 : 139, x + 25, open ? 51 : 113);
        }
        graphics.FillRectangle(metal, 48, 142, 11, 78);
        graphics.FillRectangle(metal, 172, 142, 11, 78);
        graphics.FillRectangle(metal, 103, 147, 24, 24);
        graphics.FillRectangle(dark, 113, 156, 5, 8);
        using var label = new SolidBrush(Color.FromArgb(219, 219, 169));
        using var leaf = new Pen(Color.FromArgb(65, 101, 52), 4);
        graphics.FillEllipse(label, 102, 181, 26, 23);
        graphics.DrawLine(leaf, 115, 183, 108, 176);
        graphics.DrawLine(leaf, 115, 183, 122, 176);
    }
}
