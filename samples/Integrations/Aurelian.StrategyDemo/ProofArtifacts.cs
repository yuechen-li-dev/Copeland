using System.Text.Json;
using SkiaSharp;

namespace Aurelian.StrategyDemo;

public static class ProofArtifacts
{
    public static string DirectoryPath { get; } = Path.Combine(FindRoot(), "artifacts", "aurelian-rts-pearl-mining-m18");

    public static string M19DirectoryPath { get; } = Path.Combine(FindRoot(), "artifacts", "aurelian-native-graphics-m19");

    public static void Write(string name, object value)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    public static void WriteM19(string name, object value)
    {
        Directory.CreateDirectory(M19DirectoryPath);
        File.WriteAllText(
            Path.Combine(M19DirectoryPath, name),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    public static void Save(SKBitmap bitmap, string name)
    {
        Directory.CreateDirectory(DirectoryPath);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(Path.Combine(DirectoryPath, name));
        data.SaveTo(stream);
    }

    public static void SaveM19(SKBitmap bitmap, string name)
    {
        Directory.CreateDirectory(M19DirectoryPath);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(Path.Combine(M19DirectoryPath, name));
        data.SaveTo(stream);
    }

    public static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Run from a Copeland checkout.");
    }
}
