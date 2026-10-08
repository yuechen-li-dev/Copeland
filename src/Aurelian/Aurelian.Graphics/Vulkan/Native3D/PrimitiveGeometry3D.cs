using System.Numerics;

namespace Aurelian.Graphics.Vulkan.Native3D;

public static class PrimitiveGeometry3D
{
    public static void AddBox(List<Native3DVertex> vertices, Vector3 center, Vector3 halfSize, Vector4 color)
    {
        Vector3 min = center - halfSize;
        Vector3 max = center + halfSize;
        AddQuad(vertices, new(min.X, min.Y, min.Z), new(min.X, min.Y, max.Z), new(min.X, max.Y, max.Z), new(min.X, max.Y, min.Z), -Vector3.UnitX, color);
        AddQuad(vertices, new(max.X, min.Y, max.Z), new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z), Vector3.UnitX, color);
        AddQuad(vertices, new(min.X, max.Y, min.Z), new(min.X, max.Y, max.Z), new(max.X, max.Y, max.Z), new(max.X, max.Y, min.Z), Vector3.UnitY, color);
        AddQuad(vertices, new(min.X, min.Y, max.Z), new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, min.Y, max.Z), -Vector3.UnitY, color);
        AddQuad(vertices, new(max.X, min.Y, min.Z), new(min.X, min.Y, min.Z), new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z), -Vector3.UnitZ, color);
        AddQuad(vertices, new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z), Vector3.UnitZ, color);
    }

    public static void AddQuad(List<Native3DVertex> vertices, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector4 color)
    {
        AddTriangle(vertices, a, b, c, normal, color);
        AddTriangle(vertices, a, c, d, normal, color);
    }

    public static void AddTriangle(List<Native3DVertex> vertices, Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector4 color)
    {
        vertices.Add(new(a, normal, color));
        vertices.Add(new(b, normal, color));
        vertices.Add(new(c, normal, color));
    }
}

