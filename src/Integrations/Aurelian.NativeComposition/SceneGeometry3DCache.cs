using Aurelian.Graphics.Vulkan.Native3D;

namespace Aurelian.NativeComposition;

/// <summary>Scene-owned immutable local meshes. Frame projection changes transforms, not mesh data.</summary>
public sealed class SceneGeometry3DCache
{
    private readonly Dictionary<object, NativeMesh3D> meshes = new();
    private readonly HashSet<object> active = [];

    internal void Begin() => active.Clear();

    internal NativeMesh3D Get(object key, Func<NativeMesh3D> create)
    {
        active.Add(key);
        if (!meshes.TryGetValue(key, out var mesh))
        {
            mesh = create();
            meshes.Add(key, mesh);
        }
        return mesh;
    }

    internal void End()
    {
        foreach (object key in meshes.Keys.Where(key => !active.Contains(key)).ToArray())
        {
            meshes.Remove(key);
        }
    }
}
