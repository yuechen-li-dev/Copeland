using System.Security.Cryptography;
using Aurelian.Audio;
using Dominatus.Actuators.Audio;

namespace Dominatus.Audio.Aurelian;

public static class DominatusAudioArtifactAdapter
{
    public static AudioClipResource Load(
        AudioResourceScope resources,
        AudioAssetId assetId,
        AudioArtifact artifact,
        bool loopByDefault = false)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Format != AudioFormat.Wav)
        {
            throw new NotSupportedException($"Dominatus {artifact.Format} artifacts are not supported by the M4 PCM WAV adapter.");
        }
        AudioClipResource resource = resources.LoadPcmWav(assetId, artifact.Path, loopByDefault);
        if (artifact.SampleRateHz is int sampleRate && sampleRate != resource.SampleRateHz)
        {
            throw new InvalidDataException("Dominatus artifact sample rate does not match decoded WAV metadata.");
        }
        if (artifact.Channels is int channels && channels != resource.Channels)
        {
            throw new InvalidDataException("Dominatus artifact channel count does not match decoded WAV metadata.");
        }
        if (artifact.SizeBytes is long size && size != new FileInfo(artifact.Path).Length)
        {
            throw new InvalidDataException("Dominatus artifact size does not match the generated file.");
        }
        return resource;
    }

    public static AudioEventId CorrelationId(AudioGenerationMetadata metadata, AudioAssetId assetId)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        string source = metadata.CommandIdempotencyKey ?? metadata.TextSha256 ?? assetId.Value;
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source));
        return new AudioEventId($"dominatus:{Convert.ToHexString(hash).ToLowerInvariant()}");
    }
}
