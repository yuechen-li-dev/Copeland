using Aurelian.Assets.ArtSlots;
using SkiaSharp;

if (args.Length > 0 && args[0] == "review-sheet")
{
    return SpriteSheetReview.Run(args[1..]);
}

// Usage: dotnet run --project tools/Aurelian.ArtSlots -- <art-slots.json> [--out <directory>] [--scale <n>]
// Or:    dotnet run --project tools/Aurelian.ArtSlots -- review-sheet <4x4.png> --out <directory>
// Writes, for the slots in the manifest:
//   pending.md          the worklist (usage and brief for every slot without final art)
//   placeholders/*.png  each placeholder rasterized (an img2img or control image to paint over)
//   contact-sheet.png   every slot as the game currently realizes it, on a checkerboard
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: Aurelian.ArtSlots <art-slots.json> [--out <directory>] [--scale <n>]");
    Console.Error.WriteLine("   or: Aurelian.ArtSlots review-sheet <4x4.png> --out <directory>");
    return 2;
}
string manifest = args[0];
string output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest))!, "..", "..", "artifacts", "art-slots");
float scale = 4;
for (int index = 1; index < args.Length - 1; index++)
{
    if (args[index] == "--out")
    {
        output = args[index + 1];
    }
    else if (args[index] == "--scale")
    {
        scale = float.Parse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture);
    }
}
ArtSlotCatalog catalog = ArtSlotCatalog.Load(manifest);
Directory.CreateDirectory(Path.Combine(output, "placeholders"));
File.WriteAllText(Path.Combine(output, "pending.md"), catalog.PendingReport());

var realized = new List<ArtSlotImage>();
foreach (ArtSlotDefinition slot in catalog.Slots)
{
    ArtSlotImage placeholder = catalog.RealizePlaceholder(slot.Id, scale);
    WritePng(placeholder, Path.Combine(output, "placeholders", slot.Id + ".png"));
    realized.Add(catalog.Realize(slot.Id, scale));
}
WriteContactSheet(realized, Path.Combine(output, "contact-sheet.png"));
Console.WriteLine($"{catalog.Slots.Count} slots, {catalog.Pending.Count} pending -> {Path.GetFullPath(output)}");
return 0;

static void WritePng(ArtSlotImage image, string path)
{
    using SKBitmap bitmap = ToBitmap(image);
    using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(path, data.ToArray());
}

static SKBitmap ToBitmap(ArtSlotImage image)
{
    var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
    var bitmap = new SKBitmap(info);
    System.Runtime.InteropServices.Marshal.Copy(image.Rgba, 0, bitmap.GetPixels(), image.Rgba.Length);
    return bitmap;
}

static void WriteContactSheet(IReadOnlyList<ArtSlotImage> images, string path)
{
    const int Cell = 320;
    const int Columns = 4;
    int rows = (images.Count + Columns - 1) / Columns;
    using var sheet = new SKBitmap(Cell * Columns, Cell * rows);
    using (var canvas = new SKCanvas(sheet))
    {
        using var light = new SKPaint { Color = new SKColor(236, 236, 230) };
        using var dark = new SKPaint { Color = new SKColor(214, 214, 206) };
        for (int y = 0; y < sheet.Height; y += 16)
        {
            for (int x = 0; x < sheet.Width; x += 16)
            {
                canvas.DrawRect(x, y, 16, 16, (x / 16 + y / 16) % 2 == 0 ? light : dark);
            }
        }
        using var frame = new SKPaint { Color = images.Count > 0 ? new SKColor(255, 0, 170) : SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 3 };
        using var label = new SKPaint { Color = SKColors.Black, TextSize = 16, IsAntialias = true };
        for (int index = 0; index < images.Count; index++)
        {
            ArtSlotImage image = images[index];
            float left = index % Columns * Cell;
            float top = index / Columns * Cell;
            float fit = Math.Min((Cell - 40f) / image.Width, (Cell - 40f) / image.Height);
            var target = SKRect.Create(left + (Cell - image.Width * fit) / 2, top + 8, image.Width * fit, image.Height * fit);
            using SKBitmap bitmap = ToBitmap(image);
            canvas.DrawBitmap(bitmap, target);
            if (image.IsPlaceholder)
            {
                // Magenta frame: still a placeholder.
                canvas.DrawRect(target, frame);
            }
            canvas.DrawText(image.Id, left + 8, top + Cell - 10, label);
        }
    }
    using SKData data = sheet.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(path, data.ToArray());
}
