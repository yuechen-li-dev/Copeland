using System.Numerics;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.World.Scenes;

namespace Aurelian.NativeComposition;

/// <summary>Explicit static Lambertian authoring projection of existing scene geometry.</summary>
public static class SceneDiffuseLighting
{
    public static StaticDiffuseLightingRecipe Describe(ScenePlan scene, string receiverId,
        HorizontalDiffuseReceiver receiver, Vector3 sunDirection, IReadOnlyDictionary<string, Vector3>? emission = null)
    {
        if (!scene.Models.IsEmpty || !scene.Agents.IsEmpty)
            throw new NotSupportedException("Diffuse compilation requires explicit static boxes/meshes; models and agents are not admitted yet.");
        var bodies = new List<StaticDiffuseContributor>();
        foreach (var box in scene.Boxes)
            Add(box.Id, SceneGeometry3D.Build(new([box], [])));
        foreach (var mesh in scene.Meshes)
            Add(mesh.Id, SceneGeometry3D.Build(new([], [mesh])));
        var recipe = new StaticDiffuseLightingRecipe(scene.Id, receiverId, receiver,
            [sunDirection.X, sunDirection.Y, sunDirection.Z], bodies.ToArray());
        recipe.Validate();
        return recipe;

        void Add(string id, Aurelian.Graphics.Vulkan.Native3D.Native3DVertex[] vertices)
        {
            var positions = new List<float>();
            var colours = new List<float>();
            for (int index = 0; index < vertices.Length; index += 3)
            {
                var first = vertices[index];
                var second = vertices[index + 1];
                var third = vertices[index + 2];
                if (first.Color != second.Color || first.Color != third.Color || first.Color.W != 1)
                    throw new NotSupportedException("Static diffuse authoring requires opaque constant albedo per triangle: " + id);
                // Game triangles carry authoritative normals; align the authoring face orientation with them.
                if (Vector3.Dot(Vector3.Cross(second.Position - first.Position, third.Position - first.Position), first.Normal) < 0)
                    (second, third) = (third, second);
                foreach (var vertex in new[] { first, second, third })
                {
                    positions.Add(vertex.Position.X);
                    positions.Add(vertex.Position.Y);
                    positions.Add(vertex.Position.Z);
                }
                colours.AddRange([first.Color.X, first.Color.Y, first.Color.Z]);
            }
            Vector3 emitted = emission?.GetValueOrDefault(id) ?? Vector3.Zero;
            bodies.Add(new(id, positions.ToArray(), colours.Take(3).ToArray(), [emitted.X, emitted.Y, emitted.Z])
                { FaceAlbedos = colours.ToArray() });
        }
    }
}
