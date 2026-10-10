using System.Numerics;
using Aurelian.Assets.Lighting;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

namespace Aurelian.Beacon3D;

internal static class BeaconLighting
{
    public static StaticDiffuseLightingRecipe Recipe()
    {
        var scene = SceneCompiler.Compile(BeaconScene.Arena());
        string floor = scene.Meshes.Single().Id;
        string lamp = scene.Boxes.Single(box => box.Id == "lamp").Id;
        return SceneDiffuseLighting.Describe(scene, floor, new(-11, -11, 22, 22), Graphics3DSettings.Default.SunDirection,
            new Dictionary<string, Vector3> { [lamp] = new(12, 7, 3) });
    }

    public static string Build(string output, string blender) =>
        StaticDiffuseLightingCompiler.Build(Recipe(), output, blender);
}
