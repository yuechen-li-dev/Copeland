using System.Text.Json;
using SkiaSharp;

namespace TinyFarm.Native;

/// <summary>Contact sheets and exact observation agreement for the four ordinary native playtest runs.</summary>
internal static class TinyFarmTerrainEvidence
{
    private static readonly string[] Modes = ["legacy", "alpha", "detail", "multiscale"];

    public static void Write(string output)
    {
        var baseline = ReadTrace(Path.Combine(output, "legacy", "trace.jsonl"));
        var disagreements = new List<string>();
        int comparisons = 0;
        foreach (string mode in Modes)
        {
            var trace = ReadTrace(Path.Combine(output, mode, "trace.jsonl"));
            if (trace.Length != baseline.Length)
            {
                throw new InvalidDataException("Native script step counts disagree.");
            }
            for (int step = 0; step < trace.Length; step++)
            {
                if (trace[step]["tinyfarm.terrain"] != mode)
                {
                    throw new InvalidDataException("The selected terrain mode was not active in the native game.");
                }
                if (trace[step].Count != baseline[step].Count)
                {
                    throw new InvalidDataException("Native observation fact sets disagree.");
                }
                foreach (var pair in baseline[step])
                {
                    if (pair.Key == "tinyfarm.terrain")
                    {
                        continue;
                    }
                    comparisons++;
                    if (!trace[step].TryGetValue(pair.Key, out string? actual) || actual != pair.Value)
                    {
                        disagreements.Add($"{mode} step={step} key={pair.Key}: {pair.Value} != {actual}");
                    }
                }
            }
        }
        WriteSheet(output, "comparison.png", ["00-farm.png", "01-riverside.png"], null);
        WriteSheet(output, "path-edges.png", ["00-farm.png"], new(440, 330, 1440, 1000));
        WriteSheet(output, "riverbank.png", ["01-riverside.png"], new(960, 170, 1430, 960));
        var report = new
        {
            Passed = disagreements.Count == 0,
            Modes,
            StepsEach = baseline.Length,
            ExactFactComparisons = comparisons,
            Includes = "All observed facts except the selected terrain mode, including state and fluid hashes.",
            Disagreements = disagreements,
            Scope = "Real Vulkan application captures and normal playtest commands. Contact sheets crop/resample captures only for comparison. No human perceptual qualification.",
        };
        File.WriteAllText(Path.Combine(output, "native-agreement.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (disagreements.Count != 0)
        {
            throw new InvalidDataException("Native observations disagree; see native-agreement.json.");
        }
        Console.WriteLine($"TINYFARM_TERRAIN_NATIVE_AGREEMENT_PASSED comparisons={comparisons}");
    }

    private static Dictionary<string, string>[] ReadTrace(string path)
    {
        var trace = File.ReadLines(path).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.GetProperty("error").ValueKind != JsonValueKind.Null)
            {
                throw new InvalidDataException("Native playtest script contains an error: " + path);
            }
            var facts = root.GetProperty("observation").EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.GetString()!);
            if (!facts.ContainsKey("tinyfarm.semantic-hash") || !facts.ContainsKey("tinyfarm.field-hash"))
            {
                throw new InvalidDataException("Native proof must include state and fluid hashes.");
            }
            return facts;
        }).ToArray();
        if (trace.Length == 0)
        {
            throw new InvalidDataException("Native comparison traces must contain completed steps.");
        }
        return trace;
    }

    private static void WriteSheet(string output, string name, string[] captures, SKRect? crop)
    {
        const int TileWidth = 480;
        int tileHeight = crop is SKRect region ? (int)(TileWidth * region.Height / region.Width) : 270;
        using var bitmap = new SKBitmap(TileWidth * Modes.Length, (tileHeight + 34) * captures.Length);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(22, 27, 31));
        using var font = new SKFont(SKTypeface.Default, 18);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        for (int row = 0; row < captures.Length; row++)
        {
            for (int column = 0; column < Modes.Length; column++)
            {
                using var source = SKBitmap.Decode(Path.Combine(output, Modes[column], captures[row]));
                if (source.Width != 1920 || source.Height != 1080)
                {
                    throw new InvalidDataException("The comparison requires native 1920x1080 captures.");
                }
                int x = column * TileWidth;
                int y = row * (tileHeight + 34);
                canvas.DrawText(Modes[column].ToUpperInvariant() + " / " + captures[row], x + 8, y + 24, font, paint);
                canvas.DrawBitmap(source, crop ?? new SKRect(0, 0, source.Width, source.Height),
                    new SKRect(x, y + 34, x + TileWidth, y + 34 + tileHeight));
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(Path.Combine(output, name));
        data.SaveTo(stream);
    }
}
