using System.Security.Cryptography;
using NLayer;

namespace Aurelian.Audio;

/// <summary>
/// Decodes MP3 (MPEG-1/2 Layer III) into a resident clip with the managed NLayer decoder. Decoding a full music track
/// takes noticeable time; games can run <see cref="Decode(AudioAssetId, byte[], bool)"/> on a worker thread and
/// <see cref="AudioResourceScope.Add"/> the result on the game thread.
/// </summary>
public static class Mp3Decoder
{
    public static AudioClipResource Decode(AudioAssetId id, byte[] bytes, bool loopByDefault = false)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("Audio asset identity must not be empty.", nameof(id));
        }
        using var stream = new MemoryStream(bytes, writable: false);
        using var file = new MpegFile(stream);
        int channels = file.Channels;
        int sampleRate = file.SampleRate;
        if (channels is < 1 or > 2 || sampleRate <= 0)
        {
            throw new InvalidDataException("The MP3 stream has unsupported channel or sample-rate metadata.");
        }
        var samples = new List<float>(checked((int)Math.Min(int.MaxValue, Math.Max(0, file.Length / sizeof(float)))));
        float[] buffer = new float[8192 * channels];
        int read;
        while ((read = file.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            for (int index = 0; index < read; index++)
            {
                samples.Add(buffer[index]);
            }
        }
        int complete = samples.Count - samples.Count % channels;
        if (complete == 0)
        {
            throw new InvalidDataException("The MP3 stream contains no audio frames.");
        }
        float[] interleaved = samples.GetRange(0, complete).ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new AudioClipResource(id, hash, sampleRate, channels, complete / channels, interleaved, loopByDefault);
    }
}
