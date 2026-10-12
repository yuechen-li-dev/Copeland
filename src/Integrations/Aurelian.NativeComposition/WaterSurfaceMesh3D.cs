using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

namespace Aurelian.NativeComposition;

/// <summary>Reusable indexed base patch. Wave displacement stays on the GPU;
/// authors choose tessellation to resolve their shortest geometric wavelength.</summary>
public static class WaterSurfaceMesh3D
{
    public static SceneMesh Create(string id, Vector2 size, WaterSurface3D? profile = null,
        int cellsX = 96, int cellsZ = 96, Vector3? at = null)
    {
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0
            || cellsX is < 1 or > 256 || cellsZ is < 1 or > 256)
        {
            throw new ArgumentException("Water patch needs finite positive size and 1..256 cells per axis.");
        }
        profile ??= new();
        profile.Validate();
        var vertices = ImmutableArray.CreateBuilder<SceneVertex>((cellsX + 1) * (cellsZ + 1));
        var indices = ImmutableArray.CreateBuilder<int>(cellsX * cellsZ * 6);
        for (int z = 0; z <= cellsZ; z++)
        {
            for (int x = 0; x <= cellsX; x++)
            {
                vertices.Add(new(new((float)x / cellsX * size.X - size.X * .5f, 0,
                    (float)z / cellsZ * size.Y - size.Y * .5f), Vector3.UnitY, Vector4.One));
            }
        }
        for (int z = 0; z < cellsZ; z++)
        {
            for (int x = 0; x < cellsX; x++)
            {
                int first = z * (cellsX + 1) + x;
                int next = first + cellsX + 1;
                indices.Add(first);
                indices.Add(next);
                indices.Add(first + 1);
                indices.Add(first + 1);
                indices.Add(next);
                indices.Add(next + 1);
            }
        }
        return Scene.Mesh(id, vertices.MoveToImmutable(), at) with
        {
            Indices = indices.MoveToImmutable(),
            Material = profile.Material(id),
        };
    }
}
