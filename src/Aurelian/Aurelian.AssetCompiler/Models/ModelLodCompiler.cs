using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Assets.Models;

/// <summary>Import-time only. Native meshoptimizer comes from the pinned MIT Meshoptimizer.NET package.</summary>
public static unsafe class ModelLodCompiler
{
    public const string Version = ModelLodArtifact.CompilerVersion;

    public static ModelPrimitive Compile(ModelPrimitive source)
    {
        // Admission precedes native code, including for programmatically authored primitives.
        _ = new StaticModel("lod-validation", "source", [new("source", source, Matrix4x4.Identity)]);
        if (source.Indices.Length > 1_000_000 || source.Vertices.Length > 1_000_000)
            throw new InvalidDataException("AUR-LOD-011: Offline primitive exceeds the one-million vertex/index budget.");
        if (source.Indices.Length < 96 || source.Material.AlphaBlend || source.Material.Transmission > 0)
            return source with { Lods = [] };

        var positions = source.Vertices.SelectMany(vertex => new[]
            { vertex.Position.X, vertex.Position.Y, vertex.Position.Z }).ToArray();
        var attributes = source.Vertices.SelectMany(vertex => new[]
        {
            vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z, vertex.Uv.X, vertex.Uv.Y,
            vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z, vertex.Tangent.W,
            vertex.Color.X, vertex.Color.Y, vertex.Color.Z, vertex.Color.W,
        }).ToArray();
        float[] weights = [.5f, .5f, .5f, 2, 2, .5f, .5f, .5f, 1, 1, 1, 1, 1];
        // Exact attribute welding only: hard normals, tangent handedness and UV seams stay distinct.
        var welded = new Dictionary<ModelVertex, uint>();
        var remap = new uint[source.Vertices.Length];
        for (int vertex = 0; vertex < remap.Length; vertex++)
        {
            if (!welded.TryGetValue(source.Vertices[vertex], out uint representative))
            {
                representative = (uint)vertex;
                welded.Add(source.Vertices[vertex], representative);
            }
            remap[vertex] = representative;
        }
        uint[] indices = source.Indices.Select(index => remap[index]).ToArray();
        var destination = new uint[indices.Length];
        var levels = ImmutableArray.CreateBuilder<ModelLod>();
        fixed (float* positionPointer = positions)
        fixed (float* attributePointer = attributes)
        fixed (float* weightPointer = weights)
        fixed (uint* indexPointer = indices)
        fixed (uint* destinationPointer = destination)
        {
            float scale = SimplifyScale(positionPointer, (nuint)source.Vertices.Length, 12);
            if (!float.IsFinite(scale) || scale <= 0) return source with { Lods = [] };
            int previousCount = indices.Length;
            float previousError = 0;
            for (int level = 1; level <= 3; level++)
            {
                nuint target = (nuint)(Math.Max(1, (indices.Length / 3) >> level) * 3);
                float error;
                // LockBorder=1. No permissive seam crossing, disconnected pruning or sloppy simplification.
                nuint count = SimplifyWithAttributes(destinationPointer, indexPointer, (nuint)indices.Length,
                    positionPointer, (nuint)source.Vertices.Length, 12, attributePointer, 52,
                    weightPointer, 13, null, target, .05f, 1, &error);
                if (count > (nuint)indices.Length || !float.IsFinite(error) || error < 0)
                    throw new InvalidDataException("AUR-LOD-003: Simplifier returned invalid geometry/error.");
                if (count < 3 || count >= (nuint)(previousCount * .9)) continue;
                float objectError = Math.Max(previousError, Math.Max(error * scale, scale * 1e-7f));
                if (!float.IsFinite(objectError)) throw new InvalidDataException("AUR-LOD-003: Simplifier returned invalid error.");
                OptimizeVertexCache(destinationPointer, destinationPointer, count, (nuint)source.Vertices.Length);
                levels.Add(new(destination.AsSpan(0, checked((int)count)).ToArray()
                    .Select(index => checked((int)index)).ToImmutableArray(), objectError));
                previousCount = (int)count;
                previousError = objectError;
            }
        }
        var result = source with { Lods = levels.ToImmutable() };
        result.ValidateLods();
        return result;
    }

    [DllImport("meshoptimizer", EntryPoint = "meshopt_simplifyWithAttributes", CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint SimplifyWithAttributes(uint* destination, uint* indices, nuint indexCount,
        float* positions, nuint vertexCount, nuint positionStride, float* attributes, nuint attributeStride,
        float* weights, nuint attributeCount, byte* locks, nuint targetCount, float targetError, uint options, float* error);

    [DllImport("meshoptimizer", EntryPoint = "meshopt_simplifyScale", CallingConvention = CallingConvention.Cdecl)]
    private static extern float SimplifyScale(float* positions, nuint vertexCount, nuint stride);

    [DllImport("meshoptimizer", EntryPoint = "meshopt_optimizeVertexCache", CallingConvention = CallingConvention.Cdecl)]
    private static extern void OptimizeVertexCache(uint* destination, uint* indices, nuint count, nuint vertexCount);
}
