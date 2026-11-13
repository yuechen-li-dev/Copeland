using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Aurelian.Audio;

public enum ProceduralSoundKind
{
    Footstep,
    MetalImpact,
    Rain,
    ShelteredRain,
    WaterStep,
    Wind,
}

/// <summary>Bounded, seeded PCM preparation. Generate on a loading/worker thread, never a device callback.</summary>
public sealed record ProceduralSoundRecipe(ProceduralSoundKind Kind, uint Seed = 1)
{
    public int SampleRateHz { get; init; } = 48_000;
    public float DurationSeconds { get; init; } = .25f;
    public float Gain { get; init; } = .6f;
    public float FrequencyHz { get; init; } = 180;

    public AudioClipResource Prepare(AudioAssetId id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("A prepared sound needs an asset identity.", nameof(id));
        }
        if (!Enum.IsDefined(Kind) || SampleRateHz is < 8_000 or > 96_000
            || !float.IsFinite(DurationSeconds) || DurationSeconds < .01f || DurationSeconds > 10
            || !float.IsFinite(Gain) || Gain < 0 || Gain > 1
            || !float.IsFinite(FrequencyHz) || FrequencyHz < 20 || FrequencyHz > SampleRateHz * .2f)
        {
            throw new ArgumentOutOfRangeException(nameof(ProceduralSoundRecipe), "Invalid synthesis parameters.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        int frames = checked((int)Math.Round(DurationSeconds * SampleRateHz));
        var samples = new float[frames];
        uint state = Seed;
        double low = 0;
        double coefficient = 1 - Math.Exp(-Math.Tau * FrequencyHz / SampleRateHz);
        for (int frame = 0; frame < frames; frame++)
        {
            if ((frame & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            state = unchecked(state * 1664525u + 1013904223u);
            double noise = (state >> 8) / 8388607.5 - 1;
            low += coefficient * (noise - low);
            double time = (double)frame / SampleRateHz;
            double phase = Math.Tau * FrequencyHz * time;
            double value;
            if (Kind == ProceduralSoundKind.MetalImpact)
            {
                double modes = (Math.Sin(phase) + .5 * Math.Sin(phase * 2.71)
                    + .25 * Math.Sin(phase * 4.13)) / 1.75;
                value = (.8 * modes + .2 * noise) * Math.Exp(-time * 14 / DurationSeconds);
            }
            else if (Kind == ProceduralSoundKind.Footstep)
            {
                value = (.75 * low + .25 * Math.Sin(phase)) * Math.Exp(-time * 10 / DurationSeconds);
            }
            else if (Kind == ProceduralSoundKind.WaterStep)
            {
                double splash = (noise - low) * Math.Exp(-time * 6 / DurationSeconds);
                double body = Math.Sin(phase * .55) * Math.Exp(-time * 16 / DurationSeconds);
                value = .4 * splash + .2 * body;
            }
            else if (Kind == ProceduralSoundKind.ShelteredRain)
            {
                value = low;
            }
            else if (Kind == ProceduralSoundKind.Wind)
            {
                value = low * (.65 + .35 * Math.Sin(Math.Tau * time / DurationSeconds));
            }
            else
            {
                value = .65 * noise + .35 * low;
            }
            // Endpoints meet at zero. For rain this avoids a hard discontinuity on loop wrap.
            double fade = Math.Min(1, Math.Min(frame, frames - 1 - frame) / (SampleRateHz * .005));
            samples[frame] = (float)(value * fade * Gain);
        }
        // Canonical little-endian PCM identity, independent of machine byte order and asset label.
        byte[] bytes = new byte[checked(frames * sizeof(float) + sizeof(int))];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, SampleRateHz);
        for (int frame = 0; frame < frames; frame++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(sizeof(int) + frame * sizeof(float)), samples[frame]);
        }
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new(id, hash, SampleRateHz, 1, frames, samples,
            Kind is ProceduralSoundKind.Rain or ProceduralSoundKind.ShelteredRain or ProceduralSoundKind.Wind);
    }
}
