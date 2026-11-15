using System.Buffers.Binary;
using System.Security.Cryptography;
using Aurelian.Audio;

namespace Aurelian.Beacon3D;

/// <summary>Small authored score, prepared during loading. Composition belongs to Beacon; clocks/mixing to the engine.</summary>
internal static class BeaconMusic
{
    public static IReadOnlyList<AudioClipResource> Prepare()
    {
        const int rate = 48_000;
        const int frames = rate * 8;
        float[] melody = new float[frames];
        float[] drums = new float[frames];
        int[] notes = [50, 57, 62, 65, 53, 60, 65, 69, 48, 55, 60, 64, 45, 52, 57, 60];
        uint noiseState = 73;
        for (int frame = 0; frame < frames; frame++)
        {
            double time = (double)frame / rate;
            int beat = (int)(time * 2);
            double pulse = time % .5;
            double frequency = 440 * Math.Pow(2, (notes[beat] - 69) / 12d);
            double attack = Math.Min(1, pulse / .008);
            double pluck = Math.Sin(Math.Tau * frequency * pulse) + .2 * Math.Sin(Math.Tau * frequency * 2 * pulse);
            melody[frame] = (float)(pluck * attack * Math.Exp(-pulse * 8) * .16);
            noiseState = unchecked(noiseState * 1664525u + 1013904223u);
            double noise = (noiseState >> 8) / 8388607.5 - 1;
            double kick = beat % 2 == 0 ? Math.Sin(Math.Tau * (50 * pulse + 4 * (1 - Math.Exp(-30 * pulse))))
                * Math.Exp(-pulse * 20) * .3 : 0;
            double snare = beat % 2 == 1 ? noise * Math.Exp(-pulse * 30) * .13 : 0;
            double hat = noise * Math.Exp(-(time % .25) * 95) * .045;
            double edge = Math.Min(1, Math.Min(frame, frames - 1 - frame) / (rate * .005));
            melody[frame] *= (float)edge;
            drums[frame] = (float)((kick + snare + hat) * edge);
        }
        return [Clip("beacon.music.base", melody), Clip("beacon.music.combat", drums)];
    }

    private static AudioClipResource Clip(string id, float[] samples)
    {
        byte[] bytes = new byte[samples.Length * sizeof(float) + sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 48_000);
        for (int frame = 0; frame < samples.Length; frame++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(sizeof(int) + frame * sizeof(float)), samples[frame]);
        }
        return new(new(id), Convert.ToHexString(SHA256.HashData(bytes)), 48_000, 1, samples.Length, samples, LoopByDefault: true);
    }
}
