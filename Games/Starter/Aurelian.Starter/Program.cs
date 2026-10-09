using Aurelian.Games;
using Aurelian.Aetheris;
using Aurelian.World.Scenes;
using System.Numerics;
using System.Collections.Immutable;
using Aurelian.Assets.Models;
using Aurelian.Humanoid;

SceneGroup document = TrainingRange.Create();
HumanoidPlayerOptions? humanoid = null;
int bodyOption = Array.IndexOf(args, "--humanoid");
if (bodyOption < 0 && args.Contains("--locomotion", StringComparer.Ordinal))
{
    throw new ArgumentException("--locomotion requires --humanoid.");
}
if (bodyOption >= 0)
{
    if (bodyOption + 1 >= args.Length || args[bodyOption + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new ArgumentException("--humanoid requires a gameplay body path.");
    }
    humanoid = HumanoidPlayerOptions.Load(args[bodyOption + 1]) with
    {
        Attachments = [HumanoidPlayerOptions.PlaceholderWeapon()],
    };
    int locomotionOption = Array.IndexOf(args, "--locomotion");
    if (locomotionOption >= 0)
    {
        if (locomotionOption + 1 >= args.Length || args[locomotionOption + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("--locomotion requires a baked animation path.");
        }
        humanoid = humanoid with { Locomotion = HumanoidLocomotionBank.Load(args[locomotionOption + 1], humanoid.Body) };
    }
}
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
    [.. GamePresets.FirstPersonShooter, GameConcept.ThirdPersonCamera, GameConcept.ThirdPersonControl,
        .. (humanoid is null ? Array.Empty<GameConcept>() : new[] { GameConcept.HumanoidPresentation })],
    args, new StarterOptions(Title: "AURELIAN | COMPOSABLE STARTER"),
    sceneDocument: document, humanoid: humanoid);
