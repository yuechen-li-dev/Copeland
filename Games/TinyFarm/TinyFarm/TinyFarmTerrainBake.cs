using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.GameWorld2D;
using SkiaSharp;
using TinyFarm.Core;

namespace TinyFarm.Native;

/// <summary>Offline material cache. Baked textures contain only ground, never live objects or water.</summary>
internal static class TinyFarmTerrainBake
{
    private const string Recipe = "tinyfarm.natural-shore.multiscale.v1";
    private sealed record Entry(string Scene, string InputHash, string PngHash, string PixelsHash);

    public static SpriteAtlasResource LoadDirt(string assets)
    {
        return TinyFarmM24Assets.LoadResource("terrain-study-packed-earth",
            Path.Combine(assets, "terrain-study", "packed-earth.png"),
            "1090b984cc3de700b2d4d0c5348f63b4b31bd3d05bbd0bd926d3dccdacfd669f", SpriteSampling.Linear);
    }

    public static void Write(string assets, string output)
    {
        Directory.CreateDirectory(output);
        using var app = new TinyFarm.TinyFarmApp(assetDirectory: assets, legacySaveDirectory: "unused");
        var meadow = TinyFarmM24Assets.Load(Path.Combine(assets, "m24")).Meadow;
        var dirt = LoadDirt(assets);
        var study = new TinyFarmTerrainStudy(TinyFarmTerrainMode.Multiscale, meadow, dirt);
        var entries = new List<Entry>();
        foreach (var scene in new[] { TinyFarmSceneIds.Farm, TinyFarmSceneIds.Riverside })
        {
            var definition = app.Game.Definitions.Scenes.Get(scene);
            var frame = TinyFarmFrameProjector.Project(app.Game.State, app.Game.Definitions) with
            {
                ActiveScene = scene,
                SceneWidth = definition.Width,
                SceneHeight = definition.Height,
            };
            SpriteAtlasResource ground = study.Get(frame, app.Game.Host.Session.Field);
            string file = Path.Combine(output, scene.Value + ".png");
            using var data = SKData.CreateCopy(ground.Rgba8);
            using var image = SKImage.FromPixels(new SKImageInfo((int)ground.Width, (int)ground.Height,
                SKColorType.Rgba8888, SKAlphaType.Unpremul), data);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using (var stream = File.Create(file))
            {
                encoded.SaveTo(stream);
            }
            entries.Add(new(scene.Value, InputHash(frame, meadow, dirt), Hash(File.ReadAllBytes(file)), ground.ContentHash));
        }
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("TINYFARM_TERRAIN_BAKED scenes=2 directory=" + output);
    }

    public static SpriteAtlasResource Read(string directory, TinyFarmFrame frame, SpriteAtlasResource meadow, SpriteAtlasResource dirt)
    {
        string scene = frame.ActiveScene?.Value ?? throw new InvalidOperationException("A bake requires a scene.");
        var entries = JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(Path.Combine(directory, "manifest.json")))
            ?? throw new InvalidDataException("Missing terrain bake entries.");
        Entry entry = entries.Single(item => item.Scene == scene);
        if (entry.InputHash != InputHash(frame, meadow, dirt))
        {
            throw new InvalidDataException("Stale ground bake. Run --bake-terrain Assets/shoreline/ground from TinyFarm's asset authoring workflow.");
        }
        SpriteAtlasResource resource = TinyFarmM24Assets.LoadResource("terrain-study-Multiscale-" + scene,
            Path.Combine(directory, scene + ".png"), entry.PngHash, SpriteSampling.Linear);
        if (resource.ContentHash != entry.PixelsHash
            || resource.Width != (frame.SceneWidth + TinyFarmTerrainStudy.Padding * 2) * TinyFarmTerrainStudy.PixelsPerUnit
            || resource.Height != (frame.SceneHeight + TinyFarmTerrainStudy.Padding * 2) * TinyFarmTerrainStudy.PixelsPerUnit)
        {
            throw new InvalidDataException("Ground bake pixels or extent disagree with the authored recipe.");
        }
        return resource;
    }

    private static string InputHash(TinyFarmFrame frame, SpriteAtlasResource meadow, SpriteAtlasResource dirt)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Recipe,
            frame.SceneWidth,
            frame.SceneHeight,
            TinyFarmTerrainStudy.Padding,
            Meadow = meadow.ContentHash,
            Dirt = dirt.ContentHash,
            River = TinyFarmLandscape.River.Points,
            Roads = frame.ActiveScene == TinyFarmSceneIds.Farm ? TinyFarmLandscape.FarmRoads : TinyFarmLandscape.RiversideRoads,
        });
        return Hash(bytes);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
