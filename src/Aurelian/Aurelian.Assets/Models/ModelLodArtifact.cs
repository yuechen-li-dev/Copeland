using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Assets.Models;

/// <summary>Deterministic delivery sidecar. Loading never invokes a simplifier or silently rebuilds stale data.</summary>
public static class ModelLodArtifact
{
    public const string CompilerVersion = "aurelian.lod.v1/meshoptimizer.net-1.0.7";
    private const uint Magic = 0x31444F4C;
    private const int MaximumBytes = 128 * 1024 * 1024;

    public static void Write(string path, StaticModel model)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(CompilerVersion);
            writer.Write(model.ContentIdentity);
            var primitives = model.Occurrences.Select(item => item.Primitive).DistinctBy(item => item.Id).ToArray();
            writer.Write(primitives.Length);
            foreach (var primitive in primitives)
            {
                primitive.ValidateLods();
                writer.Write(primitive.Id);
                writer.Write(primitive.Lods.Length);
                foreach (var level in primitive.Lods)
                {
                    writer.Write(level.Error);
                    writer.Write(level.Indices.Length);
                    foreach (int index in level.Indices) writer.Write(index);
                }
            }
        }
        if (stream.Length > MaximumBytes - 32) throw new InvalidDataException("AUR-LOD-004: Delivery LOD budget exceeded.");
        byte[] body = stream.ToArray();
        File.WriteAllBytes(path, [.. body, .. SHA256.HashData(body)]);
    }

    public static StaticModel Read(string path, StaticModel source)
    {
        long length = new FileInfo(path).Length;
        if (length < 40 || length > MaximumBytes) throw new InvalidDataException("AUR-LOD-004: Invalid LOD artifact size.");
        byte[] bytes = File.ReadAllBytes(path);
        var body = bytes.AsSpan(0, bytes.Length - 32);
        if (!SHA256.HashData(body).AsSpan().SequenceEqual(bytes.AsSpan(bytes.Length - 32)))
            throw new InvalidDataException("AUR-LOD-005: LOD artifact checksum mismatch.");
        using var stream = new MemoryStream(bytes, 0, body.Length, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadUInt32() != Magic || reader.ReadString() != CompilerVersion
            || reader.ReadString() != source.ContentIdentity)
            throw new InvalidDataException("AUR-LOD-006: LOD artifact version/source/settings mismatch; rebuild assets.");
        var originals = source.Occurrences.Select(item => item.Primitive).DistinctBy(item => item.Id)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        int primitiveCount = reader.ReadInt32();
        if (primitiveCount != originals.Count) throw new InvalidDataException("AUR-LOD-007: LOD primitive table mismatch.");
        var prepared = new Dictionary<string, ModelPrimitive>(StringComparer.Ordinal);
        for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
        {
            string id = reader.ReadString();
            if (!originals.TryGetValue(id, out var original) || prepared.ContainsKey(id))
                throw new InvalidDataException("AUR-LOD-007: Unknown or repeated LOD primitive.");
            int levelCount = reader.ReadInt32();
            if (levelCount < 0 || levelCount > 3) throw new InvalidDataException("AUR-LOD-001: Invalid level count.");
            var levels = ImmutableArray.CreateBuilder<ModelLod>();
            for (int level = 0; level < levelCount; level++)
            {
                float error = reader.ReadSingle();
                int count = reader.ReadInt32();
                if (count < 3 || count >= original.Indices.Length || count % 3 != 0
                    || (long)count * 4 > stream.Length - stream.Position)
                    throw new InvalidDataException("AUR-LOD-002: Invalid delivery index count.");
                var indices = ImmutableArray.CreateBuilder<int>(count);
                for (int index = 0; index < count; index++) indices.Add(reader.ReadInt32());
                levels.Add(new(indices.MoveToImmutable(), error));
            }
            var primitive = original with { Lods = levels.ToImmutable() };
            primitive.ValidateLods();
            prepared.Add(id, primitive);
        }
        if (stream.Position != stream.Length) throw new InvalidDataException("AUR-LOD-008: Trailing delivery data.");
        return new(source.Id, source.ContentIdentity, source.Occurrences.Select(occurrence => occurrence with
        {
            Primitive = occurrence.Primitive with { Lods = prepared[occurrence.Primitive.Id].Lods },
        }));
    }
}
