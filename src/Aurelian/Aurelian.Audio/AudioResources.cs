using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Aurelian.Audio;

public sealed class AudioResourceScope : IDisposable
{
    private readonly Dictionary<AudioAssetId, AudioClipResource> resources = [];
    private bool disposed;

    public int Count => resources.Count;

    public AudioClipResource LoadPcmWav(AudioAssetId id, string path, bool loopByDefault = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateId(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = File.ReadAllBytes(path);
        AudioClipResource resource = PcmWavDecoder.Decode(id, bytes, loopByDefault);
        resources[id] = resource;
        return resource;
    }

    public AudioClipResource Add(AudioClipResource resource)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(resource);
        ValidateId(resource.Id);
        if (string.IsNullOrWhiteSpace(resource.ContentHash))
        {
            throw new ArgumentException("Audio resource content hash must not be empty.", nameof(resource));
        }
        if (resource.SampleRateHz <= 0 || resource.Channels is < 1 or > 2 || resource.FrameCount <= 0)
        {
            throw new ArgumentException("Audio resource metadata is invalid.", nameof(resource));
        }
        if (resource.Samples.LongLength != resource.FrameCount * resource.Channels)
        {
            throw new ArgumentException("Audio sample count does not match resource metadata.", nameof(resource));
        }
        if (resource.Strategy != AudioResourceStrategy.Resident)
        {
            throw new NotSupportedException("Streamed audio resources are not implemented in M4.");
        }
        resources[resource.Id] = resource;
        return resource;
    }

    public AudioClipResource Get(AudioAssetId id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return resources.TryGetValue(id, out AudioClipResource? resource)
            ? resource
            : throw new KeyNotFoundException($"Unknown audio asset '{id}'.");
    }

    public bool TryGet(AudioAssetId id, out AudioClipResource? resource)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return resources.TryGetValue(id, out resource);
    }

    public void Dispose()
    {
        resources.Clear();
        disposed = true;
    }

    private static void ValidateId(AudioAssetId id)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("Audio asset identity must not be empty.", nameof(id));
        }
    }
}

public static class PcmWavDecoder
{
    public static AudioClipResource Decode(AudioAssetId id, ReadOnlySpan<byte> bytes, bool loopByDefault = false)
    {
        if (bytes.Length < 44 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Audio resource is not a RIFF/WAVE file.");
        }

        int offset = 12;
        ushort format = 0;
        ushort channels = 0;
        int sampleRate = 0;
        ushort bitsPerSample = 0;
        ReadOnlySpan<byte> data = default;
        while (offset + 8 <= bytes.Length)
        {
            ReadOnlySpan<byte> chunkId = bytes.Slice(offset, 4);
            int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 4, 4));
            offset += 8;
            if (chunkSize < 0 || offset + chunkSize > bytes.Length)
            {
                throw new InvalidDataException("WAV chunk extends beyond the file.");
            }
            ReadOnlySpan<byte> chunk = bytes.Slice(offset, chunkSize);
            if (chunkId.SequenceEqual("fmt "u8))
            {
                if (chunk.Length < 16)
                {
                    throw new InvalidDataException("WAV format chunk is truncated.");
                }
                format = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]);
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                data = chunk;
            }
            offset += chunkSize + (chunkSize & 1);
        }

        if (format != 1 || channels is < 1 or > 2 || bitsPerSample != 16 || sampleRate <= 0 || data.IsEmpty)
        {
            throw new NotSupportedException("Only mono or stereo 16-bit PCM WAV resources are supported.");
        }

        int sampleCount = data.Length / 2;
        if (sampleCount % channels != 0)
        {
            throw new InvalidDataException("WAV PCM data is not aligned to complete frames.");
        }
        var samples = new float[sampleCount];
        for (int index = 0; index < sampleCount; index++)
        {
            samples[index] = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(index * 2, 2)) / 32768f;
        }
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new AudioClipResource(id, hash, sampleRate, channels, sampleCount / channels, samples, loopByDefault);
    }
}
