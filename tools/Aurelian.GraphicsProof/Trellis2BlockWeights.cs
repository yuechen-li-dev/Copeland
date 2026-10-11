using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class Trellis2BlockWeights
{
    public const string Revision = "af44b45f2e35a493886929c6d786e563ec68364d";
    public const string Checkpoint = "ss_flow_img_dit_1_3B_64_bf16";
    public const long CheckpointBytes = 2584426920;
    private const string HeaderHash = "eadcc6c616db95eab21996aa2da2e835b959f0784b22a370bc5dd9269190eed4";
    private static readonly string Url = $"https://huggingface.co/microsoft/TRELLIS.2-4B/resolve/{Revision}/ckpts/{Checkpoint}.safetensors";

    public static async Task Fetch(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        byte[] prefix = await Range(0, 8);
        ulong length = BinaryPrimitives.ReadUInt64LittleEndian(prefix);
        if (length is 0 or > 4194304)
        {
            throw new InvalidDataException("Unexpected checkpoint header length.");
        }
        byte[] header = await Range(8, (int)length);
        if (Convert.ToHexString(SHA256.HashData(header)).ToLowerInvariant() != HeaderHash)
        {
            throw new InvalidDataException("Pinned checkpoint header hash mismatch.");
        }
        var inventory = JsonNode.Parse(header)!.AsObject();
        var selected = inventory.Where(item => item.Key.StartsWith("blocks.0.", StringComparison.Ordinal)
            || item.Key.StartsWith("t_embedder.", StringComparison.Ordinal)
            || item.Key.StartsWith("adaLN_modulation.", StringComparison.Ordinal))
            .OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var subset = new JsonObject();
        var hashes = new List<object>();
        long offset = 0;
        foreach (var item in selected)
        {
            var info = item.Value!.DeepClone();
            var original = info["data_offsets"]!.AsArray();
            long start = original[0]!.GetValue<long>();
            long end = original[1]!.GetValue<long>();
            info["data_offsets"] = new JsonArray(offset, offset + end - start);
            subset[item.Key] = info;
            offset += end - start;
        }
        subset["__metadata__"] = new JsonObject { ["source"] = "microsoft/TRELLIS.2-4B", ["revision"] = Revision,
            ["scope"] = "First sparse-structure transformer block and shared timestep conditioning" };
        byte[] encoded = Encoding.UTF8.GetBytes(subset.ToJsonString());
        int paddedLength = (encoded.Length + 7) / 8 * 8;
        byte[] padded = Enumerable.Repeat((byte)' ', paddedLength).ToArray();
        encoded.CopyTo(padded, 0);
        string temporary = Path.Combine(outputDirectory, "block0.safetensors.partial");
        using (var file = File.Create(temporary))
        {
            byte[] size = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(size, (ulong)paddedLength);
            await file.WriteAsync(size);
            await file.WriteAsync(padded);
            foreach (var item in selected)
            {
                long start = item.Value!["data_offsets"]![0]!.GetValue<long>();
                long end = item.Value["data_offsets"]![1]!.GetValue<long>();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                for (long position = start; position < end; position += 8388608)
                {
                    byte[] data = await Range(8 + (long)length + position, (int)Math.Min(8388608, end - position));
                    await file.WriteAsync(data);
                    hash.AppendData(data);
                }
                hashes.Add(new { Name = item.Key, Sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
                    SourceOffsets = new[] { start, end }, Shape = item.Value["shape"] });
                Console.WriteLine($"Fetched {item.Key}: {end - start:N0} bytes");
            }
        }
        string path = Path.Combine(outputDirectory, "block0.safetensors");
        File.Move(temporary, path, overwrite: true);
        using var checksumStream = File.OpenRead(path);
        var provenance = new
        {
            ModelRevision = Revision, Checkpoint = Url, CheckpointBytes, HeaderSha256 = HeaderHash,
            SubsetSha256 = Convert.ToHexString(await SHA256.HashDataAsync(checksumStream)).ToLowerInvariant(),
            SelectedWeightBytes = offset, Tensors = hashes,
        };
        File.WriteAllText(Path.Combine(outputDirectory, "weights.json"), JsonSerializer.Serialize(provenance, new JsonSerializerOptions { WriteIndented = true }));

        async Task<byte[]> Range(long first, int count)
        {
            long last = first + count - 1;
            using var request = new HttpRequestMessage(HttpMethod.Get, Url + $"?download=true&range={first}-{last}");
            request.Headers.Range = new RangeHeaderValue(first, last);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || range?.From != first || range.To != last || range.Length != CheckpointBytes)
            {
                throw new InvalidDataException("Server did not honor the exact pinned checkpoint range: " + response.StatusCode);
            }
            byte[] data = new byte[count];
            using var stream = await response.Content.ReadAsStreamAsync();
            await stream.ReadExactlyAsync(data);
            if (stream.ReadByte() != -1)
            {
                throw new InvalidDataException("Oversized checkpoint range response.");
            }
            return data;
        }
    }
}
