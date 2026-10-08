using System.Numerics;
using Aurelian.Spatial3D;
using Aurelian.World.Scenes;

namespace Aurelian.NativeComposition;

/// <summary>Collision is compiled from authored scene facts, never from a rendered frame.</summary>
public static class SceneSpatial3D
{
    public static SpatialWorld3D Build(ScenePlan plan, IEnumerable<Collider3D>? additional = null)
    {
        var colliders = new List<Collider3D>();
        foreach (PlacedSceneBox box in plan.Boxes.Where(box => box.Collision == SceneCollision.Solid))
        {
            colliders.Add(new(box.Id, CollisionMesh3D.Box(box.HalfSize), box.WorldTransform,
                box.CollisionLayer, box.CollisionMask, box.Id));
        }
        foreach (PlacedSceneMesh mesh in plan.Meshes.Where(mesh => mesh.Collision == SceneCollision.Solid))
        {
            IEnumerable<int> indices = mesh.Indices.IsEmpty ? Enumerable.Range(0, mesh.Vertices.Length) : mesh.Indices;
            var geometry = new CollisionMesh3D(mesh.Vertices.Select(vertex => vertex.Position), indices,
                mesh.ClosedCollision, mesh.SourceIdentity, mesh.TriangleFaces);
            colliders.Add(new(mesh.Id, geometry, mesh.WorldTransform, mesh.CollisionLayer, mesh.CollisionMask, mesh.Id));
        }
        if (additional is not null) colliders.AddRange(additional);
        return new(colliders);
    }
}
