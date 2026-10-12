using System.Security.Cryptography;
using System.Text.Json;

internal static class Trellis2OccupancyWeights
{
    public const string FlowHash = "ca01377c485bec418076d38ee80166d32dc776d744f2553b835cba1e97a7abf6";
    public const string DecoderHash = "1c76d4a40519aa2d711cc263a8404105231ac26db31d946bed48b84fee79009a";
    public const string FlowName = "ss_flow_img_dit_1_3B_64_bf16.safetensors";
    public const string DecoderName = "ss_dec_conv3d_16l8_fp16.safetensors";

    public static async Task Fetch(string directory)
    {
        Directory.CreateDirectory(directory);
        await Download(directory, FlowName, 2584426920, FlowHash,
            "https://huggingface.co/microsoft/TRELLIS.2-4B/resolve/af44b45f2e35a493886929c6d786e563ec68364d/ckpts/" + FlowName);
        await Download(directory, DecoderName, 147591972, DecoderHash,
            "https://huggingface.co/microsoft/TRELLIS-image-large/resolve/25e0d31ffbebe4b5a97464dd851910efc3002d96/ckpts/" + DecoderName);
    }

    public static async Task Download(string directory, string name, long length, string expectedHash, string url)
    {
        string path = Path.Combine(directory, name);
        if (File.Exists(path))
        {
            Validate(path, length, expectedHash);
            Console.WriteLine("Using verified local checkpoint: " + name);
            return;
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var response = await http.GetAsync(url + "?download=true", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != System.Net.HttpStatusCode.OK || response.Content.Headers.ContentLength != length)
        {
            throw new InvalidDataException("Checkpoint server returned unexpected status or length: " + name);
        }
        string partial = path + ".partial";
        using var source = await response.Content.ReadAsStreamAsync();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = 0;
        long reported = 0;
        var buffer = new byte[8 * 1024 * 1024];
        using (var file = File.Create(partial))
        {
            int count;
            while ((count = await source.ReadAsync(buffer)) != 0)
            {
                received += count;
                if (received > length)
                {
                    throw new InvalidDataException("Checkpoint download exceeds its pinned size.");
                }
                await file.WriteAsync(buffer.AsMemory(0, count));
                hash.AppendData(buffer, 0, count);
                if (received - reported >= 128 * 1024 * 1024)
                {
                    Console.WriteLine($"{name}: {received:N0}/{length:N0} bytes");
                    reported = received;
                }
            }
        }
        string actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (received != length || actualHash != expectedHash)
        {
            throw new InvalidDataException("Downloaded checkpoint hash/length mismatch: " + name);
        }
        File.Move(partial, path);
        File.WriteAllText(path + ".provenance.json", JsonSerializer.Serialize(new
        {
            Source = url,
            Bytes = received,
            Sha256 = actualHash,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Verified checkpoint: " + name);
    }

    public static void Validate(string path, long bytes, string hash)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length != bytes || Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() != hash)
        {
            throw new InvalidDataException("Local checkpoint differs from pinned publication: " + path);
        }
    }
}
