namespace Aurelian.Inference.Whisper;

internal sealed class WhisperLogMel
{
    private const int FftSize = 400;
    private const int Hop = 160;
    private const int Bins = FftSize / 2 + 1;
    private const int Frames = 2 * TinyEnglishCheckpoint.AudioRows;
    private static readonly float[] Window = Enumerable.Range(0, FftSize)
        .Select(i => (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FftSize))).ToArray();
    private static readonly ComplexSample[] Twiddles = Enumerable.Range(0, FftSize)
        .Select(i => new ComplexSample(Math.Cos(-2 * Math.PI * i / FftSize), Math.Sin(-2 * Math.PI * i / FftSize))).ToArray();

    private readonly (int First, float[] Weights)[] bands;

    public WhisperLogMel(float[] filters)
    {
        bands = new (int, float[])[TinyEnglishCheckpoint.MelBins];
        for (int i = 0; i < bands.Length; i++)
        {
            var band = filters.AsSpan(i * Bins, Bins);
            int first = 0;
            int last = Bins - 1;
            while (first <= last && band[first] == 0) first++;
            while (last >= first && band[last] == 0) last--;
            if (first > last || band.ToArray().Any(value => !float.IsFinite(value) || value < 0))
            {
                throw new InvalidDataException("Invalid Whisper mel filter.");
            }
            bands[i] = (first, band.Slice(first, last - first + 1).ToArray());
        }
    }

    public void Fill(float[] pcm, int offset, int count, float[] mel, int threads, CancellationToken cancellation)
    {
        int activeFrames = Math.Min(Frames, (count + FftSize / 2) / Hop + 1);
        Parallel.For(0, activeFrames, new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = cancellation },
            frame => ComputeFrame(pcm, offset, count, mel, frame));
        // A full window can have one additional nonzero frame in the reference normalization.
        float peak = -10;
        for (int i = 0; i < activeFrames * bands.Length; i++)
        {
            peak = Math.Max(peak, mel[i]);
        }
        if ((count + FftSize / 2) / Hop >= Frames)
        {
            var tail = new float[bands.Length];
            for (int frame = Frames; frame <= (count + FftSize / 2) / Hop; frame++)
            {
                ComputeFrame(pcm, offset, count, tail, frame, 0);
                peak = Math.Max(peak, tail.Max());
            }
        }
        Array.Fill(mel, -10, activeFrames * bands.Length, mel.Length - activeFrames * bands.Length);
        float floor = peak - 8;
        for (int i = 0; i < mel.Length; i++)
        {
            mel[i] = (Math.Max(mel[i], floor) + 4) * 0.25f;
        }
    }

    private void ComputeFrame(float[] pcm, int pcmOffset, int count, float[] output, int frame, int? target = null)
    {
        Span<float> window = stackalloc float[FftSize];
        int first = frame * Hop - FftSize / 2;
        for (int i = 0; i < window.Length; i++)
        {
            int index = Math.Abs(first + i);
            window[i] = index < count ? pcm[pcmOffset + index] * Window[i] : 0;
        }
        Span<ComplexSample> spectrum = stackalloc ComplexSample[FftSize];
        Transform(window, 1, spectrum);
        Span<double> power = stackalloc double[Bins];
        for (int i = 0; i < Bins; i++)
        {
            power[i] = spectrum[i].Real * spectrum[i].Real + spectrum[i].Imaginary * spectrum[i].Imaginary;
        }

        int offset = (target ?? frame) * bands.Length;
        for (int i = 0; i < bands.Length; i++)
        {
            var (start, weights) = bands[i];
            double energy = 0;
            for (int bin = 0; bin < weights.Length; bin++)
            {
                energy += power[start + bin] * weights[bin];
            }
            output[offset + i] = (float)Math.Log10(Math.Max(1e-10, energy));
        }
    }

    private static void Transform(ReadOnlySpan<float> input, int stride, Span<ComplexSample> output)
    {
        int half = output.Length / 2;
        if (output.Length == 25)
        {
            for (int bin = 0; bin < output.Length; bin++)
            {
                double real = 0;
                double imaginary = 0;
                for (int index = 0; index < output.Length; index++)
                {
                    var phase = Twiddles[(bin * index * stride) % FftSize];
                    double value = input[index * stride];
                    real += value * phase.Real;
                    imaginary += value * phase.Imaginary;
                }
                output[bin] = new(real, imaginary);
            }
            return;
        }

        Transform(input, stride * 2, output[..half]);
        Transform(input[stride..], stride * 2, output[half..]);
        for (int i = 0; i < half; i++)
        {
            var even = output[i];
            var odd = output[i + half];
            var phase = Twiddles[i * stride];
            double real = phase.Real * odd.Real - phase.Imaginary * odd.Imaginary;
            double imaginary = phase.Real * odd.Imaginary + phase.Imaginary * odd.Real;
            output[i] = new(even.Real + real, even.Imaginary + imaginary);
            output[i + half] = new(even.Real - real, even.Imaginary - imaginary);
        }
    }

    private readonly record struct ComplexSample(double Real, double Imaginary);
}
