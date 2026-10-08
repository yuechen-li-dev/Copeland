using System.Numerics;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.RayQueries;

namespace Aurelian.Spatial3D.Vulkan;

/// <summary>Same authored colliders and hit provenance as the CPU world, projected into retained hardware traversal.</summary>
public sealed class VulkanSpatialRayQueries3D : IRayQueryWorld3D, IDisposable
{
    private readonly (Collider3D Collider, int Triangle)[] provenance;
    private readonly VulkanRayQueryScene scene;
    public ulong BottomLevelAddress => scene.BottomLevelAddress;
    public ulong TopLevelAddress => scene.TopLevelAddress;
    public int DispatchCount => scene.DispatchCount;

    public VulkanSpatialRayQueries3D(AurelianVulkanPlant plant, byte[] spirv, SpatialWorld3D world, int capacity = 4096)
    {
        var triangles = new List<RayQueryTriangle>();
        var sources = new List<(Collider3D, int)>();
        foreach (Collider3D collider in world.Colliders)
        {
            for (int index = 0; index < collider.Mesh.Indices.Length; index += 3)
            {
                Vector3 a = Vector3.Transform(collider.Mesh.Positions[collider.Mesh.Indices[index]], collider.Transform);
                Vector3 b = Vector3.Transform(collider.Mesh.Positions[collider.Mesh.Indices[index + 1]], collider.Transform);
                Vector3 c = Vector3.Transform(collider.Mesh.Positions[collider.Mesh.Indices[index + 2]], collider.Transform);
                triangles.Add(new(a, b, c, collider.Layer, collider.Mask));
                sources.Add((collider, index / 3));
            }
        }
        provenance = sources.ToArray();
        scene = new(plant, spirv, triangles, capacity: capacity);
    }

    public SpatialHit3D? Raycast(Ray3D ray, QueryFilter3D? filter = null) => RaycastBatch([ray], filter)[0];

    public SpatialHit3D?[] RaycastBatch(IReadOnlyList<Ray3D> rays, QueryFilter3D? filter = null)
    {
        QueryFilter3D actual = filter ?? QueryFilter3D.All;
        foreach (Ray3D ray in rays) ray.Validate();
        RayQueryRequest[] requests = rays.Select(ray => new RayQueryRequest(ray.Origin, ray.Direction,
            ray.MaximumDistance, actual.IncludedLayers, actual.QueryLayer)).ToArray();
        RayQueryHit[] native = scene.Trace(requests);
        var result = new SpatialHit3D?[native.Length];
        for (int index = 0; index < result.Length; index++)
        {
            RayQueryHit hit = native[index];
            if (hit.Kind == 0) continue;
            if (hit.Kind != 1 || hit.Primitive >= provenance.Length) throw new InvalidDataException("GPU hit does not reference this world.");
            var source = provenance[hit.Primitive];
            result[index] = new(source.Collider.Id, source.Triangle, hit.Distance, hit.Distance / rays[index].MaximumDistance,
                hit.Point, hit.Normal, SpatialQueryStatus3D.Contact, source.Collider.SemanticOwnerId,
                source.Collider.Mesh.SourceIdentity, source.Collider.Mesh.TriangleFaces.IsEmpty ? null : source.Collider.Mesh.TriangleFaces[source.Triangle]);
        }
        return result;
    }

    public void Dispose() => scene.Dispose();
}
