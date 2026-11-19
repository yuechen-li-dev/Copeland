using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Graphics.Vulkan.Native3D;

internal static class ResidentSurfaceTable3D
{
    private const int MaximumDecals = 4096;
    private const int MaximumPerReceiver = 16;
    private const int ProjectorWords = 21;

    public static float[] Pack(float[] materialIndices, IReadOnlyDictionary<string, int> receivers, ImmutableArray<SurfaceDecal3D> decals)
    {
        if (decals.Length > MaximumDecals)
            throw new NotSupportedException("AUR-DECAL-002: The resident table admits at most 4096 projectors.");

        var ranges = new Dictionary<int, List<SurfaceDecal3D>>();
        foreach (var decal in decals)
        {
            decal.Validate();
            if (!receivers.TryGetValue(decal.Receiver, out int receiver))
                throw new InvalidDataException($"AUR-DECAL-003: No resident receiver '{decal.Receiver}'.");
            if (decal.Opacity == 0) continue;
            if (!ranges.TryGetValue(receiver, out var range))
            {
                range = [];
                ranges.Add(receiver, range);
            }
            range.Add(decal);
            if (range.Count > MaximumPerReceiver)
                throw new NotSupportedException($"AUR-DECAL-002: Receiver '{decal.Receiver}' exceeds 16 active projectors.");
        }

        // The instance/ray ID indexes an exact f32 material index, or a
        // negative word address pointing to that material and its projector range.
        var words = new float[materialIndices.Length + ranges.Count * 2 + ranges.Values.Sum(range => range.Count) * ProjectorWords];
        materialIndices.CopyTo(words, 0);
        int offset = materialIndices.Length;
        foreach (var (receiver, range) in ranges)
        {
            words[receiver] = -offset;
            words[offset++] = materialIndices[receiver];
            words[offset++] = range.Count;
            foreach (var decal in range)
            {
                PackProjector(words.AsSpan(offset, ProjectorWords), decal);
                offset += ProjectorWords;
            }
        }
        return words;
    }

    private static void PackProjector(Span<float> words, SurfaceDecal3D decal)
    {
        Quaternion rotation = Quaternion.Normalize(decal.Orientation);
        Vector3 x = Vector3.Transform(Vector3.UnitX, rotation);
        Vector3 y = Vector3.Transform(Vector3.UnitY, rotation);
        Vector3 z = Vector3.Transform(Vector3.UnitZ, rotation);
        words[0] = x.X;
        words[1] = x.Y;
        words[2] = x.Z;
        words[3] = 1 / decal.HalfExtent.X;
        words[4] = y.X;
        words[5] = y.Y;
        words[6] = y.Z;
        words[7] = 1 / decal.HalfExtent.Y;
        words[8] = z.X;
        words[9] = z.Y;
        words[10] = z.Z;
        words[11] = 1 / decal.HalfExtent.Z;
        words[12] = decal.Center.X;
        words[13] = decal.Center.Y;
        words[14] = decal.Center.Z;
        words[15] = decal.Opacity;
        words[16] = decal.ColorMultiplier.X;
        words[17] = decal.ColorMultiplier.Y;
        words[18] = decal.ColorMultiplier.Z;
        words[19] = decal.Roughness ?? 0;
        words[20] = decal.NormalFlattening;
    }
}
