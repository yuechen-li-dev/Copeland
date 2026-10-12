using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Aurelian.Assets.Models;
using SkiaSharp;

/// <summary>Bounded preview asset compilation. Original voxel attributes remain
/// saved separately; atlas material values are aggregated onto a geometric LOD.</summary>
internal static class Trellis2MaterialAsset
{
    public static object Export(MeshClusterReduction mesh, float[] attributes, string output)
    {
        if (attributes.Length != mesh.SourceToReduced.Length * 6 || !attributes.All(float.IsFinite))
        {
            throw new InvalidDataException("AUR-TRELLIS-MATERIAL-003: Six finite PBR attributes must match the source vertex rows.");
        }
        var sums = new double[mesh.Positions.Length * 6];
        var counts = new int[mesh.Positions.Length];
        int clamped = 0;
        for (int source = 0; source < mesh.SourceToReduced.Length; source++)
        {
            int vertex = mesh.SourceToReduced[source];
            if (vertex < 0)
            {
                continue;
            }
            counts[vertex]++;
            for (int channel = 0; channel < 6; channel++)
            {
                float value = attributes[source * 6 + channel];
                if (value < 0 || value > 1)
                {
                    clamped++;
                }
                sums[vertex * 6 + channel] += Math.Clamp(value, 0, 1);
            }
        }
        var values = new float[sums.Length];
        for (int vertex = 0; vertex < counts.Length; vertex++)
        {
            for (int channel = 0; channel < 6; channel++)
            {
                values[vertex * 6 + channel] = (float)(sums[vertex * 6 + channel] / counts[vertex]);
            }
        }
        int[] indices = mesh.TriangleIndices.ToArray();
        Orient(mesh.Positions.ToArray(), indices);
        const int patch = 4;
        const int width = 2048;
        int triangles = indices.Length / 3;
        int tilesPerRow = width / patch;
        int height = (triangles + tilesPerRow - 1) / tilesPerRow * patch;
        var color = new byte[width * height * 4];
        var metallicRoughness = new byte[color.Length];
        using var geometry = new MemoryStream();
        using var vertexWriter = new BinaryWriter(geometry, System.Text.Encoding.UTF8, leaveOpen: true);
        Vector3 minimum = new(float.MaxValue);
        Vector3 maximum = new(float.MinValue);
        bool translucent = false;
        for (int triangle = 0; triangle < triangles; triangle++)
        {
            int a = indices[triangle * 3];
            int b = indices[triangle * 3 + 1];
            int c = indices[triangle * 3 + 2];
            int tileX = triangle % tilesPerRow * patch;
            int tileY = triangle / tilesPerRow * patch;
            for (int y = 0; y < patch; y++)
            {
                for (int x = 0; x < patch; x++)
                {
                    int pixel = ((tileY + y) * width + tileX + x) * 4;
                    float red = Attribute(0);
                    float green = Attribute(1);
                    float blue = Attribute(2);
                    float opacity = Attribute(5);
                    color[pixel] = Byte(red);
                    color[pixel + 1] = Byte(green);
                    color[pixel + 2] = Byte(blue);
                    color[pixel + 3] = Byte(opacity);
                    metallicRoughness[pixel] = 255;
                    metallicRoughness[pixel + 1] = Byte(Attribute(4));
                    metallicRoughness[pixel + 2] = Byte(Attribute(3));
                    metallicRoughness[pixel + 3] = 255;
                    // Padding texels do not determine whether the actual surface
                    // is translucent; use the three triangle corner samples.
                    if ((x == 1 && y == 1 || x == 2 && y == 1 || x == 1 && y == 2) && opacity < .99f)
                    {
                        translucent = true;
                    }

                    float Attribute(int channel)
                    {
                        float first = values[a * 6 + channel];
                        return Math.Clamp(first + (x - 1) * (values[b * 6 + channel] - first)
                            + (y - 1) * (values[c * 6 + channel] - first), 0, 1);
                    }
                }
            }
            Vector3 pa = ToAurelian(mesh.Positions[a]);
            Vector3 pb = ToAurelian(mesh.Positions[b]);
            Vector3 pc = ToAurelian(mesh.Positions[c]);
            Vector3 normal = Vector3.Normalize(Vector3.Cross(pb - pa, pc - pa));
            WriteVertex(pa, new((tileX + 1.5f) / width, (tileY + 1.5f) / height));
            WriteVertex(pb, new((tileX + 2.5f) / width, (tileY + 1.5f) / height));
            WriteVertex(pc, new((tileX + 1.5f) / width, (tileY + 2.5f) / height));

            void WriteVertex(Vector3 position, Vector2 uv)
            {
                minimum = Vector3.Min(minimum, position);
                maximum = Vector3.Max(maximum, position);
                vertexWriter.Write(position.X);
                vertexWriter.Write(position.Y);
                vertexWriter.Write(position.Z);
                vertexWriter.Write(normal.X);
                vertexWriter.Write(normal.Y);
                vertexWriter.Write(normal.Z);
                vertexWriter.Write(uv.X);
                vertexWriter.Write(uv.Y);
            }
        }
        WritePng(Path.Combine(output, "base-color.png"), width, height, color);
        WritePng(Path.Combine(output, "metallic-roughness.png"), width, height, metallicRoughness);
        byte[] colorPng = File.ReadAllBytes(Path.Combine(output, "base-color.png"));
        byte[] materialPng = File.ReadAllBytes(Path.Combine(output, "metallic-roughness.png"));
        int geometryBytes = checked((int)geometry.Length);
        int colorOffset = geometryBytes;
        int materialOffset = colorOffset + Aligned(colorPng.Length);
        int binaryBytes = materialOffset + Aligned(materialPng.Length);
        var document = new
        {
            asset = new { version = "2.0", generator = "Aurelian TRELLIS.2 C#/Vulkan material proof" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes = new[] { new { mesh = 0, name = "Generated pot preview LOD" } },
            meshes = new[] { new { primitives = new[] { new { attributes = new Dictionary<string, int>
                { ["POSITION"] = 0, ["NORMAL"] = 1, ["TEXCOORD_0"] = 2 }, material = 0, mode = 4 } } } },
            materials = new[] { new { name = "generated-pbr", doubleSided = true,
                alphaMode = translucent ? "BLEND" : "OPAQUE", pbrMetallicRoughness = new
                {
                    baseColorFactor = new[] { 1, 1, 1, 1 }, baseColorTexture = new { index = 0 },
                    metallicFactor = 1, roughnessFactor = 1, metallicRoughnessTexture = new { index = 1 },
                } } },
            samplers = new[] { new { magFilter = 9729, minFilter = 9729, wrapS = 33071, wrapT = 33071 } },
            textures = new[] { new { sampler = 0, source = 0 }, new { sampler = 0, source = 1 } },
            images = new[] { new { bufferView = 1, mimeType = "image/png" }, new { bufferView = 2, mimeType = "image/png" } },
            buffers = new[] { new { byteLength = binaryBytes } },
            bufferViews = new object[]
            {
                new { buffer = 0, byteOffset = 0, byteLength = geometryBytes, byteStride = 32, target = 34962 },
                new { buffer = 0, byteOffset = colorOffset, byteLength = colorPng.Length },
                new { buffer = 0, byteOffset = materialOffset, byteLength = materialPng.Length },
            },
            accessors = new object[]
            {
                new { bufferView = 0, byteOffset = 0, componentType = 5126, count = indices.Length, type = "VEC3",
                    min = new[] { minimum.X, minimum.Y, minimum.Z }, max = new[] { maximum.X, maximum.Y, maximum.Z } },
                new { bufferView = 0, byteOffset = 12, componentType = 5126, count = indices.Length, type = "VEC3" },
                new { bufferView = 0, byteOffset = 24, componentType = 5126, count = indices.Length, type = "VEC2" },
            },
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(document);
        using var stream = File.Create(Path.Combine(output, "pot.glb"));
        using var writer = new BinaryWriter(stream);
        writer.Write(0x46546c67u);
        writer.Write(2u);
        writer.Write(checked((uint)(12 + 8 + Aligned(json.Length) + 8 + binaryBytes)));
        writer.Write(Aligned(json.Length));
        writer.Write(0x4e4f534au);
        writer.Write(json);
        for (int padding = json.Length; padding < Aligned(json.Length); padding++)
        {
            writer.Write((byte)32);
        }
        writer.Write(binaryBytes);
        writer.Write(0x004e4942u);
        writer.Write(geometry.ToArray());
        WritePadded(colorPng);
        WritePadded(materialPng);
        return new { mesh.CellsPerUnit, mesh.MaximumPositionDeviation, Vertices = indices.Length, Triangles = triangles,
            AtlasWidth = width, AtlasHeight = height, PatchTexels = patch, ClampedSourceAttributeValues = clamped,
            AlphaMode = translucent ? "BLEND" : "OPAQUE", AttributeEncoding = "Base color RGB + opacity A; metallic B + roughness G, 8-bit PNG" };

        void WritePadded(byte[] bytes)
        {
            writer.Write(bytes);
            for (int padding = bytes.Length; padding < Aligned(bytes.Length); padding++)
            {
                writer.Write((byte)0);
            }
        }
    }

    private static void Orient(Vector3[] positions, int[] indices)
    {
        int triangles = indices.Length / 3;
        var edges = new Dictionary<(int A, int B), (int Triangle, int Slot, bool Forward)>();
        int[] neighbors = Enumerable.Repeat(-1, indices.Length).ToArray();
        var opposite = new bool[indices.Length];
        for (int triangle = 0; triangle < triangles; triangle++)
        {
            for (int slot = 0; slot < 3; slot++)
            {
                int a = indices[triangle * 3 + slot];
                int b = indices[triangle * 3 + (slot + 1) % 3];
                var key = (Math.Min(a, b), Math.Max(a, b));
                if (edges.TryGetValue(key, out var previous))
                {
                    // Non-manifold edges retain the first established pairing.
                    if (neighbors[previous.Triangle * 3 + previous.Slot] >= 0)
                    {
                        continue;
                    }
                    neighbors[triangle * 3 + slot] = previous.Triangle;
                    neighbors[previous.Triangle * 3 + previous.Slot] = triangle;
                    bool flip = previous.Forward == (a < b);
                    opposite[triangle * 3 + slot] = flip;
                    opposite[previous.Triangle * 3 + previous.Slot] = flip;
                }
                else
                {
                    edges.Add(key, (triangle, slot, a < b));
                }
            }
        }
        var visited = new bool[triangles];
        var flipped = new bool[triangles];
        var queue = new Queue<int>();
        var component = new List<int>();
        for (int seed = 0; seed < triangles; seed++)
        {
            if (visited[seed])
            {
                continue;
            }
            component.Clear();
            visited[seed] = true;
            queue.Enqueue(seed);
            double volume = 0;
            while (queue.TryDequeue(out int triangle))
            {
                component.Add(triangle);
                Vector3 a = positions[indices[triangle * 3]];
                Vector3 b = positions[indices[triangle * 3 + 1]];
                Vector3 c = positions[indices[triangle * 3 + 2]];
                volume += Vector3.Dot(a, Vector3.Cross(b, c)) * (flipped[triangle] ? -1 : 1);
                for (int slot = 0; slot < 3; slot++)
                {
                    int neighbor = neighbors[triangle * 3 + slot];
                    if (neighbor >= 0 && !visited[neighbor])
                    {
                        visited[neighbor] = true;
                        flipped[neighbor] = flipped[triangle] ^ opposite[triangle * 3 + slot];
                        queue.Enqueue(neighbor);
                    }
                }
            }
            foreach (int triangle in component)
            {
                if (flipped[triangle] ^ (volume < 0))
                {
                    (indices[triangle * 3 + 1], indices[triangle * 3 + 2]) = (indices[triangle * 3 + 2], indices[triangle * 3 + 1]);
                }
            }
        }
    }

    private static int Aligned(int bytes) => (bytes + 3) / 4 * 4;
    private static void WritePng(string path, int width, int height, byte[] pixels)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
    private static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
    private static Vector3 ToAurelian(Vector3 value) => new(value.X, value.Z, -value.Y);
}
