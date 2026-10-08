using Aurelian.Games;
using Aurelian.Aetheris;
using Aurelian.World.Scenes;
using System.Numerics;
using System.Collections.Immutable;
using Aurelian.Assets.Models;

SceneGroup document = TrainingRange.Create();
if (args.Contains("--asset-demo", StringComparer.Ordinal))
{
    var assets = ModelAssetCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "assets.toml"));
    var crate = assets.Model("starter.crate");
    var panel = crate.Current.Occurrences.First(item => item.Primitive.Material.Slot == "panel").Primitive.Material;
    document = document with
    {
        Children = document.Children.AddRange(new SceneNode[]
        {
            Scene.Model("original", crate, at: new(-1.6f, 0, -4)),
            Scene.Model("customized", crate, at: new(1.6f, 0, -4)) with
            {
                Materials = ImmutableDictionary<string, Aurelian.Rendering.Contracts.Models.ModelMaterial>.Empty
                    .Add("panel", panel with { BaseColor = new(0.35f, 1, 0.45f, 1), Metallic = 0, Roughness = 0.9f }),
            },
        }),
    };
}
if (args.Contains("--aetheris-room", StringComparer.Ordinal))
{
    if (args.Contains("--asset-demo", StringComparer.Ordinal)) throw new ArgumentException("Choose one starter demonstration.");
    SceneGroup room = AetherisSceneAsset.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "room.aurelian.json")).Compose();
    document = Scene.World("room-range",
    [
        Scene.Instance("room", room),
        Scene.Agent("player", new StarterPlayerDefinition(), at: new(6, 0, -6)),
        Scene.Agent("target", new StarterTargetDefinition(3, new(0.4f, 1, 0.4f)), at: new(6, 1, -8)),
    ]);
}

GameStarter.Run("aurelian-starter",
    [.. GamePresets.FirstPersonShooter, GameConcept.ThirdPersonCamera, GameConcept.ThirdPersonControl],
    args, new StarterOptions(Title: "AURELIAN | COMPOSABLE STARTER"),
    sceneDocument: document);
