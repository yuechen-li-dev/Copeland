using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.ComputeMath;

internal sealed class Trellis2EulerSampler(Trellis2FlowModel model, double timeRescale = 5, float guidanceRescale = .7f)
{
    public float[]? Result { get; private set; }
    public int ModelEvaluations { get; private set; }
    public List<object> Measurements { get; } = [];
    public const int Steps = 12;
    public const float SigmaMinimum = 1e-5f;

    public IEnumerable<Trellis2ComputeStage> Sample(GpuTensor positiveContext, GpuTensor negativeContext,
        string outputDirectory, uint seed)
    {
        float[] sample = Noise(seed, model.Tokens, model.InputChannels, tokenMajorNoise: model.InputChannels == 32);
        File.WriteAllBytes(Path.Combine(outputDirectory, "noise.f32"), MemoryMarshal.AsBytes(sample.AsSpan()).ToArray());
        for (int step = 0; step < Steps; step++)
        {
            double time = Time(step, timeRescale);
            double previous = Time(step + 1, timeRescale);
            bool guided = time >= .6 && time <= 1;
            float[]? positive = null;
            float[]? negative = null;
            foreach (var stage in model.Evaluate(sample, positiveContext, (float)(time * 1000),
                $"step.{step}.positive", result => positive = result))
            {
                yield return stage;
            }
            ModelEvaluations++;
            if (guided)
            {
                foreach (var stage in model.Evaluate(sample, negativeContext, (float)(time * 1000),
                    $"step.{step}.negative", result => negative = result))
                {
                    yield return stage;
                }
                ModelEvaluations++;
            }
            int captured = step;
            yield return new($"step.{captured}.euler", () =>
            {
                float[] velocity = guided ? Guided(sample, positive!, negative!, time, guidanceRescale) : positive!;
                float delta = (float)(time - previous);
                for (int index = 0; index < sample.Length; index++)
                {
                    sample[index] -= delta * velocity[index];
                }
                if (!sample.All(float.IsFinite))
                {
                    throw new InvalidDataException("Nonfinite Euler state at step " + captured);
                }
                File.WriteAllBytes(Path.Combine(outputDirectory, $"latent-step-{captured:D2}.f32"),
                    MemoryMarshal.AsBytes(sample.AsSpan()).ToArray());
                Measurements.Add(new { Step = captured, Time = time, Previous = previous, Guided = guided,
                    Rms = Math.Sqrt(sample.Average(value => (double)value * value)) });
            });
        }
        Result = sample;
    }

    public static double Time(int index, double rescale = 5)
    {
        double linear = 1 - (double)index / Steps;
        return rescale * linear / (1 + (rescale - 1) * linear);
    }

    public static float[] Guided(float[] sample, float[] positive, float[] negative, double time, float guidanceRescale = .7f)
    {
        if (sample.Length < 2 || positive.Length != sample.Length || negative.Length != sample.Length
            || !sample.All(float.IsFinite) || !positive.All(float.IsFinite) || !negative.All(float.IsFinite)
            || !double.IsFinite(time) || time <= 0 || time > 1
            || !float.IsFinite(guidanceRescale) || guidanceRescale < 0 || guidanceRescale > 1)
        {
            throw new InvalidDataException("CFG requires matching finite sample/prediction shapes and time in (0,1].");
        }
        float coefficient = (float)(SigmaMinimum + (1 - (double)SigmaMinimum) * time);
        float retained = 1 - SigmaMinimum;
        var positiveStart = new float[sample.Length];
        var guidedStart = new float[sample.Length];
        for (int index = 0; index < sample.Length; index++)
        {
            float prediction = 7.5f * positive[index] - 6.5f * negative[index];
            positiveStart[index] = retained * sample[index] - coefficient * positive[index];
            guidedStart[index] = retained * sample[index] - coefficient * prediction;
        }
        double stdPositive = StandardDeviation(positiveStart);
        double stdGuided = StandardDeviation(guidedStart);
        if (!double.IsFinite(stdGuided) || stdGuided == 0)
        {
            throw new InvalidDataException("CFG rescale has degenerate predicted clean-state variance.");
        }
        float scale = (float)(stdPositive / stdGuided);
        var velocity = new float[sample.Length];
        for (int index = 0; index < sample.Length; index++)
        {
            float rescaled = guidedStart[index] * scale;
            float start = guidanceRescale * rescaled + (1 - guidanceRescale) * guidedStart[index];
            velocity[index] = (retained * sample[index] - start) / coefficient;
        }
        return velocity;
    }

    private static double StandardDeviation(float[] values)
    {
        double mean = values.Average(value => (double)value);
        return Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / (values.Length - 1));
    }

    private static float[] Noise(uint seed, int tokens, int channels, bool tokenMajorNoise)
    {
        if (seed == 0)
        {
            throw new ArgumentException("Noise seed must be nonzero.");
        }
        var channelMajor = new float[tokens * channels];
        for (int index = 0; index < channelMajor.Length; index += 2)
        {
            double radius = Math.Sqrt(-2 * Math.Log(Uniform()));
            double phase = 2 * Math.PI * Uniform();
            channelMajor[index] = (float)(radius * Math.Cos(phase));
            channelMajor[index + 1] = (float)(radius * Math.Sin(phase));
        }
        if (tokenMajorNoise)
        {
            return channelMajor;
        }
        var tokenMajor = new float[channelMajor.Length];
        for (int token = 0; token < tokens; token++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                tokenMajor[token * channels + channel] = channelMajor[channel * tokens + token];
            }
        }
        return tokenMajor;

        double Uniform()
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return (seed + 0.5) / 4294967296.0;
        }
    }
}
