using System.Text.Json;

internal static class Trellis2SamplerProof
{
    public static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        float[] sample = [-1.3f, .2f, .7f, 1.1f, -.9f, 3.1f, .8f];
        float[] positive = [.3f, -.4f, .2f, 1.3f, -.1f, 2.1f, .5f];
        float[] negative = [-.1f, -.6f, .1f, .8f, -.3f, 1.7f, .3f];
        var measurements = new List<object>();
        int evaluations = 0;
        for (int step = 0; step < 12; step++)
        {
            // Independent closed form for the rational time sequence.
            double expectedTime = (60.0 - 5 * step) / (60 - 4 * step);
            double time = Trellis2EulerSampler.Time(step);
            if (Math.Abs(time - expectedTime) > 1e-15 || time <= Trellis2EulerSampler.Time(step + 1))
            {
                throw new InvalidDataException("Incorrect rescaled monotonic Euler schedule.");
            }
            bool guided = time >= .6 && time <= 1;
            evaluations += guided ? 2 : 1;
            float[] actual = Trellis2EulerSampler.Guided(sample, positive, negative, time);
            double retained = 1 - 1e-5;
            double coefficient = 1e-5 + retained * time;
            double[] clean = sample.Select((value, index) => retained * value - coefficient * positive[index]).ToArray();
            double[] cfg = sample.Select((value, index) => retained * value
                - coefficient * (negative[index] + 7.5 * (positive[index] - negative[index]))).ToArray();
            double cleanMean = clean.Average();
            double cfgMean = cfg.Average();
            double scale = Math.Sqrt(clean.Sum(value => Math.Pow(value - cleanMean, 2))
                / cfg.Sum(value => Math.Pow(value - cfgMean, 2)));
            double maximum = 0;
            for (int index = 0; index < sample.Length; index++)
            {
                double expected = (retained * sample[index] - cfg[index] * (.3 + .7 * scale)) / coefficient;
                maximum = Math.Max(maximum, Math.Abs(actual[index] - expected));
            }
            if (maximum > 5e-6)
            {
                throw new InvalidDataException("CFG differs from independent double equations: " + maximum);
            }
            measurements.Add(new { Step = step, Time = time, Guided = guided, MaximumError = maximum });
        }
        if (evaluations != 22 || Trellis2EulerSampler.Time(12) != 0)
        {
            throw new InvalidDataException("Guidance interval must produce exactly 22 full evaluations.");
        }
        Reject(() => Trellis2EulerSampler.Guided(sample, positive[..2], negative, 1));
        Reject(() => Trellis2EulerSampler.Guided(sample, positive, negative, 0));
        Reject(() => Trellis2EulerSampler.Guided([1, 1], [1, 1], [1, 1], 1));
        bool disposed = false;
        var failure = new InvalidOperationException("Intentional schedule failure");
        var schedule = new DominatusComputeSchedule() { Observe = InferenceProofLog.Write };
        try
        {
            schedule.Run(FailingStages());
            throw new InvalidDataException("Dominatus schedule swallowed a failed compute stage.");
        }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure))
        {
            if (!disposed || schedule.Measurements.Count != 1)
            {
                throw new InvalidDataException("Failed schedule did not dispose its iterator or preserve completed stages.");
            }
        }
        File.WriteAllText(Path.Combine(outputDirectory, "schedule-failure-inspection.json"),
            JsonSerializer.Serialize(schedule.Inspection, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(outputDirectory, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, Steps = 12, ModelEvaluations = evaluations, Measurements = measurements,
            DominatusStageFailurePropagated = true, IteratorDisposedOnFailure = disposed,
            Scope = "Rescaled schedule and CFG-rescale arithmetic versus independent double equations; not a learned-model oracle",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_TRELLIS2_SAMPLER_MATH_PASSED");

        IEnumerable<ComputeStage> FailingStages()
        {
            try
            {
                yield return new("proof.first", () => { });
                yield return new("proof.failure", () => throw failure);
                yield return new("proof.unreachable", () => throw new InvalidDataException("Executed a stage after failure."));
            }
            finally
            {
                disposed = true;
            }
        }
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException("Invalid CFG inputs were admitted.");
    }
}
